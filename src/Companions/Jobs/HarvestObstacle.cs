using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97 fix list part 1 (T1-T7), after log 97d599fc: trees, bushes, stumps, logs and loose rocks in the way of the Ore
// job's vein work, and swings that do nothing.
//  T1  What counts: a thing the game lets be damaged (TreeBase, TreeLog, Destructible, MineRock) that isn't the vein.
//      The tool comes from the thing's own damage table and tool tier: the axe if chop hurts it and the axe's tier is
//      enough, else the pickaxe if the pickaxe hurts it, else it can't be cleared.
//  T2  The game's swing stops digging once it has hit anything that isn't ground (Attack.DoMeleeAttack). What a swing hit
//      comes from the hit register (HarvestHitRegister -> OnHitRegistered -> ONoteHit). A swing that did nothing useful
//      and hit a thing of T1 makes that thing the one to clear.
//  T3  Clearing: walk into the tool's reach (TStepTime, one side step, TStepTime more), aim at where the thing is now on
//      every swing, progress = that thing losing health or going. A swing that hit another thing of T1 instead moves on
//      to that one (3 times at most).
//  T4  Move slightly: TNoProgSwings swings that did nothing = one step to the side (the mover's own side step) and the
//      count starts again, once per spot or part; after that the old endings.
//  T5  TMaxChops things cleared per vein (the count isn't reset by a new plan).
//  T6  Nothing it has can clear it: the spot is left without using a new start; said once per vein when an axe would do.
//      Those spots are noted on the shared plan; a companion with no axe waits at the end of its trench while a companion
//      with an axe is still on the trench and hasn't given them up.
// Every way in ends: a thing is either cleared (TMaxChops at most) or goes on the skip list and is never picked again.
public partial class HarvestController
{
	private enum OCtx
	{
		Trench,
		Other
	}

	private const int OMaxSwitches = 3;

	private bool _oActive;

	private GameObject _oGo;

	private OCtx _oCtx;

	private string _oWhat = "";

	private float _oWalkSince;

	private bool _oSideWalk;

	private bool _oSideSwing;

	private int _oNoProg;

	private float _oHealth;

	private int _oSwitches;

	// what the swing in progress hit first that wasn't ground or the vein (the hit register)
	private GameObject _tHitObj;

	private readonly HashSet<int> _oSkip = new HashSet<int>();

	// the skipped things an axe would clear (it has none): the spots they block are left for a companion with an axe
	private readonly HashSet<int> _oNoAxe = new HashSet<int>();

	private int _tChopsFor;

	private int _tNoAxeSaidFor;

	// T4: the side step being carried out, and the spot or part that has had its one side step
	private float _tSideUntil;

	private Vector3 _tSideGoal;

	private bool _tSideHas;

	private Vector3 _tSideKey;

	// the key of the swing in progress (the spot, or the centre of the part's box)
	private Vector3 _tKey;

	private float _tAxeWaitNext;

	private bool _tAxeWaiting;

	private static bool OHurts(HitData.DamageModifier m)
	{
		return m != HitData.DamageModifier.Immune && m != HitData.DamageModifier.Ignore;
	}

	// T1: a thing the game lets be damaged that isn't the vein, a creature or a building
	private bool OIsThing(GameObject go)
	{
		if (go == null || !go.activeInHierarchy)
		{
			return false;
		}
		if (go.GetComponentInParent<Character>() != null || go.GetComponentInParent<WearNTear>() != null)
		{
			return false;
		}
		MineRock5 m5 = go.GetComponentInParent<MineRock5>();
		if (m5 != null)
		{
			return false;   // the vein, or another split rock: the rock rules deal with those
		}
		return go.GetComponentInParent<TreeBase>() != null || go.GetComponentInParent<TreeLog>() != null || go.GetComponentInParent<Destructible>() != null || go.GetComponentInParent<MineRock>() != null;
	}

	// T1: the tool for it, from its own damage table and tool tier. axeWould = chopping hurts it (an axe is what it lacks)
	private ItemDrop.ItemData OTool(GameObject go, out bool chop, out bool axeWould)
	{
		chop = false;
		axeWould = false;
		if (go == null)
		{
			return null;
		}
		HitData.DamageModifiers mods;
		int tier;
		TreeBase tb = go.GetComponentInParent<TreeBase>();
		TreeLog tl = go.GetComponentInParent<TreeLog>();
		Destructible d = go.GetComponentInParent<Destructible>();
		MineRock mr = go.GetComponentInParent<MineRock>();
		if (tb != null)
		{
			mods = tb.m_damageModifiers;
			tier = tb.m_minToolTier;
		}
		else if (tl != null)
		{
			mods = tl.m_damages;
			tier = tl.m_minToolTier;
		}
		else if (d != null)
		{
			mods = d.m_damages;
			tier = d.m_minToolTier;
		}
		else if (mr != null)
		{
			mods = mr.m_damageModifiers;
			tier = mr.m_minToolTier;
		}
		else
		{
			return null;
		}
		axeWould = OHurts(mods.m_chop);
		ItemDrop.ItemData axe = axeWould ? FindBestTool(1) : null;
		if (axe != null && axe.m_shared.m_toolTier >= tier)
		{
			chop = true;
			return axe;
		}
		if (OHurts(mods.m_pickaxe))
		{
			ItemDrop.ItemData pick = FindBestTool(3);
			if (pick != null && pick.m_shared.m_toolTier >= tier)
			{
				return pick;
			}
		}
		return null;
	}

	// the object the game damages (the tree, log, stump, bush or rock itself, not one of its child colliders)
	private static GameObject ORoot(GameObject go)
	{
		if (go == null)
		{
			return null;
		}
		Component c = (Component)go.GetComponentInParent<TreeBase>() ?? (Component)go.GetComponentInParent<TreeLog>() ?? (Component)go.GetComponentInParent<Destructible>() ?? go.GetComponentInParent<MineRock>();
		return c != null ? c.gameObject : go;
	}

	private bool OCan(GameObject go)
	{
		go = ORoot(go);
		if (go == null || _oActive || !OIsThing(go))
		{
			return false;
		}
		int id = go.GetInstanceID();
		// left for lack of an axe, and it has a tool for it now (you gave it one): it's cleared after all
		if (_oNoAxe.Contains(id) && OTool(go, out _, out _) != null)
		{
			_oNoAxe.Remove(id);
			_oSkip.Remove(id);
		}
		return !_oSkip.Contains(id);
	}

	// T6: the thing is one it left for lack of an axe - this spot is left the same way (no swing wasted, no new start)
	private bool ONoAxeSpot(GameObject go, TSpot q)
	{
		go = ORoot(go);
		if (go == null || q == null || !_oNoAxe.Contains(go.GetInstanceID()) || OCan(go))
		{
			return false;
		}
		_vp?.AxeSpots.Add(q.P);
		_tSub = TSubDig;
		TSpotGave(q, "\"" + go.name + "\" in the way and no axe that can chop it");
		TBeginItem();
		TNextSpot();
		return true;
	}

	// the hit register: one of its swings landed on go
	private void ONoteHit(GameObject go)
	{
		if (!_vSwung || _tHitObj != null || go == null || (_vRock != null && go.GetComponentInParent<MineRock5>() == _vRock))
		{
			return;
		}
		_tHitObj = ORoot(go);
	}

	// where the thing is now: the nearest point of its solid colliders to the swing's start
	private Vector3 OPoint(GameObject go, Vector3 from)
	{
		Vector3 best = go.transform.position;
		float bd = float.MaxValue;
		foreach (Collider c in go.GetComponentsInChildren<Collider>())
		{
			if (c == null || !c.enabled || c.isTrigger)
			{
				continue;
			}
			Vector3 p = SafeClosestPoint(c, from);
			float d = Vector3.Distance(p, from);
			if (d < bd)
			{
				bd = d;
				best = p;
			}
		}
		return best;
	}

	// T4: the mover's own side step, carried out over the next 1.2 s (TTick keeps the same goal going meanwhile)
	private void TSidestep(Vector3 toward, string why)
	{
		MHR_Mover.Go(_ai, Time.deltaTime, toward, 0.05f, false, "Vein");
		MHR_Mover.SidestepNow(_ai, toward, "Vein");
		_tSideGoal = toward;
		_tSideUntil = Time.time + 1.2f;
		if (CompanionsPlugin.LogNormal) LogInfo("A step to the side, then it tries again (" + why + ")");
	}

	// T4: the one side step of this spot or part. True = taken now (the count starts again); false = it has had it.
	private bool TSideOnce(Vector3 key, Vector3 toward, string why)
	{
		if (_tSideHas && Vector3.Distance(_tSideKey, key) < 0.3f)
		{
			return false;
		}
		_tSideHas = true;
		_tSideKey = key;
		TSidestep(toward, why);
		return true;
	}

	// T2: the swing that just ended did nothing useful - did a thing of T1 eat it? True = that thing is being cleared.
	private bool OEaten(OCtx ctx)
	{
		GameObject hit = _tHitObj;
		if (!OCan(hit))
		{
			return false;
		}
		OBegin(hit, ctx, "the swing hit \"" + hit.name + "\" and nothing else");
		return true;
	}

	private void OBegin(GameObject go, OCtx ctx, string what)
	{
		go = ORoot(go);
		_oActive = true;
		_oGo = go;
		_oCtx = ctx;
		_oWhat = "\"" + (go != null ? go.name : "?") + "\"";
		_oWalkSince = _tClock;
		_oSideWalk = false;
		_oSideSwing = false;
		_oNoProg = 0;
		_oSwitches = 0;
		_oHealth = TObjHealth(go);
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"In the way: {_oWhat} at {MHR_Mover.Fmt(go != null ? go.transform.position : base.transform.position)} ({what}) - clearing it");
	}

	private void OTick()
	{
		if (TGone(_oGo))
		{
			_tChops++;
			OEnd(true, _oWhat + " is gone", false, false);
			return;
		}
		if (_tChops >= TMaxChops)
		{
			OEnd(false, $"cleared {TMaxChops} things on this vein already", false, false);
			return;
		}
		ItemDrop.ItemData tool = OTool(_oGo, out bool chop, out bool axeWould);
		if (tool == null)
		{
			OEnd(false, axeWould ? "no axe that can chop it" : "nothing it has can damage it", true, axeWould);
			return;
		}
		_tChopping = chop;
		Attack atk = tool.m_shared.m_attack;
		float range = atk != null ? atk.m_attackRange : 1.8f;
		Vector3 from = base.transform.position + Vector3.up * (atk != null ? atk.m_attackHeight : 1f);
		Vector3 pt = OPoint(_oGo, from);
		float d = Vector3.Distance(from, pt);
		if (d > range - 0.3f)
		{
			if (TRest())
			{
				_oWalkSince += Time.deltaTime;   // catching its breath: the clock waits
				return;
			}
			if (_tClock - _oWalkSince > TStepTime)
			{
				if (!_oSideWalk)
				{
					_oSideWalk = true;
					_oWalkSince = _tClock;
					TSidestep(pt, $"couldn't get within reach of {_oWhat} in {TStepTime:F0}s");
					return;
				}
				OEnd(false, $"couldn't get within reach of it ({d:F1} m from the swing's start, the reach is {range:F1}; {TStepTime:F0}s, a step to the side, {TStepTime:F0}s more)", false, false);
				return;
			}
			TWalkLevel(pt, Mathf.Max(0.4f, range - 0.8f));
			return;
		}
		_ai.StopMoving();
		if (chop)
		{
			TChopSwing(tool, pt, TFor.Obstacle);
		}
		else
		{
			TRockSwing(pt, TFor.Obstacle);
		}
	}

	// the swing at the thing has ended (TJudge)
	private void OJudge()
	{
		if (!_oActive)
		{
			return;
		}
		GameObject hit = _tHitObj;
		float h = TObjHealth(_oGo);
		bool prog = TGone(_oGo) || (_oHealth == -1f && h >= 0f) || (h >= 0f && _oHealth >= 0f && h < _oHealth - 0.01f);
		_oHealth = h;
		if (prog)
		{
			_oNoProg = 0;
			return;
		}
		// the swing landed on another thing of T1: that one is what's in the way
		if (hit != null && hit != _oGo && !hit.transform.IsChildOf(_oGo.transform) && !_oGo.transform.IsChildOf(hit.transform) && OIsThing(hit) && !_oSkip.Contains(hit.GetInstanceID()) && _oSwitches < OMaxSwitches)
		{
			_oSwitches++;
			if (CompanionsPlugin.LogDetailed) Log($"The swing at {_oWhat} hit \"{hit.name}\" instead - clearing that one ({_oSwitches} of {OMaxSwitches})");
			_oGo = hit;
			_oWhat = "\"" + hit.name + "\"";
			_oHealth = TObjHealth(hit);
			_oNoProg = 0;
			_oWalkSince = _tClock;
			return;
		}
		if (++_oNoProg < TNoProgSwings)
		{
			return;
		}
		if (!_oSideSwing)
		{
			_oSideSwing = true;
			_oNoProg = 0;
			_oWalkSince = _tClock;
			TSidestep(OPoint(_oGo, TOrigin()), $"{TNoProgSwings} swings at {_oWhat} took no health off it; the last hit {(hit != null ? "\"" + hit.name + "\"" : "nothing it reported")}");
			return;
		}
		OEnd(false, $"it takes no damage ({TNoProgSwings} swings, a step to the side, {TNoProgSwings} more; the last hit {(hit != null ? "\"" + hit.name + "\"" : "nothing it reported")})", false, false);
	}

	private void OEnd(bool cleared, string why, bool noTool, bool axeWould)
	{
		GameObject go = _oGo;
		string what = _oWhat;
		_oActive = false;
		_oGo = null;
		_tChopping = false;
		if (cleared)
		{
			if (_oCtx == OCtx.Trench)
			{
				TBlockCleared(why);
			}
			else
			{
				if (CompanionsPlugin.LogDetailed) Log("Cleared the way: " + why);
				TBeginItem();
			}
			return;
		}
		if (go != null)
		{
			_oSkip.Add(go.GetInstanceID());
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't clear {what}: {why}");
		TSpot q = _tQ;
		if (_oCtx != OCtx.Trench || _tPhase != TPhaseTrench || q == null)
		{
			_tWalkSince = _tClock;   // the old endings carry on from here (the thing is never picked again)
			return;
		}
		if (!noTool)
		{
			TBlockUnknown(q, what + " in the way: " + why);
			return;
		}
		// T6: no tool for it - the spot is left, no new start is used
		if (axeWould)
		{
			if (go != null)
			{
				_oNoAxe.Add(go.GetInstanceID());
			}
			_vp?.AxeSpots.Add(q.P);
			int vein = _target != null ? _target.GetInstanceID() : 0;
			if (_tNoAxeSaidFor != vein)
			{
				_tNoAxeSaidFor = vein;
				_talk?.SayProblem("vein:noaxe", string.Format(ModLocalization.Loc("hc_speech_no_working_tool"), ModLocalization.Loc("hc_tool_axe")));
			}
		}
		_tSub = TSubDig;
		TSpotGave(q, what + " in the way and " + why);
		TBeginItem();
		TNextSpot();
	}

	// T6: its own trench is done, it has no axe, and a spot left for an axe is still open to a companion with an axe who is
	// still on the trench. Ends when those spots are done or given up by that companion, or it leaves the trench.
	private bool TAxeWait()
	{
		if (_vp == null || _vp.AxeSpots.Count == 0)
		{
			return false;
		}
		if (Time.time < _tAxeWaitNext)
		{
			return _tAxeWaiting;
		}
		_tAxeWaitNext = Time.time + 1f;
		bool was = _tAxeWaiting;
		_tAxeWaiting = false;
		if (FindBestTool(1) != null)
		{
			return false;
		}
		int left = 0;
		string who = "";
		foreach (Vector3 p in _vp.AxeSpots)
		{
			TSpot s = null;
			foreach (TLine l in _tLines)
			{
				foreach (TSpot c in l.S)
				{
					if (Flat(c.P, p) <= 0.6f)
					{
						s = c;
						break;
					}
				}
				if (s != null)
				{
					break;
				}
			}
			if (s == null || s.Done)
			{
				continue;
			}
			foreach (HarvestController o in TOthers())
			{
				if (o._tPhase <= TPhaseTrench && !o._stuckInHole && (s.Gave == null || !s.Gave.Contains(o.MyId)) && o.FindBestTool(1) != null)
				{
					left++;
					who = o.MyName;
					break;
				}
			}
		}
		if (left == 0)
		{
			return false;
		}
		if (!was && CompanionsPlugin.LogNormal)
		{
			LogInfo($"Every spot it can do is done; {left} spot(s) with a tree are left for {who}, who has an axe - waiting for those");
		}
		_tAxeWaiting = true;
		return true;
	}
}
