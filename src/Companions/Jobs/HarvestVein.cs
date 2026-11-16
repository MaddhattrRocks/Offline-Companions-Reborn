using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97: the Ore job on a split vein outdoors (MineRock5: copper, silver, flametal...), mined like a player (John's
// approved plan T1-T8, 2026-11-14, reviews/1.5.97-vein-trench-plan.md):
//  - The game pops a vein when a part breaks (MineRock5.CheckSupport): every part whose box (its collider's bounds, set
//    once) touches no ground, no solid thing that can't be broken, nothing breakable lower than the vein's origin and no
//    held-up neighbour part, breaks at once and drops its ore. VeinScan copies that test (OverlapBox, same layers).
//  - The trench work (plan v3: the plan of the trench, digging it dirt first, what's in the way, getting out, clearing it
//    so it pops, the mining lap) is in HarvestTrench.cs. This file keeps the parts scan, the dirt spots, the swing, the
//    give-up rule and the dig rules the game asks for (AllowDig).
public partial class HarvestController
{
	private struct VPart
	{
		public Collider C;

		// its place in the game's list of the vein's parts
		public int Idx;

		public Vector3 Center;

		public Vector3 Half;

		public float Health;

		public bool Dirt;

		public bool Solid;

		public bool Low;

		public bool Direct => Dirt || Solid || Low;

		public float MinY => Center.y - Half.y;
	}

	private struct VSpot
	{
		public Vector3 P;

		public long Key;

		public Collider Part;

		public Vector3 PartCenter;
	}

	private const float VStandOut = 1.5f;

	private const float VDigMin = 0.05f;

	private const int VSpotTries = 3;

	private const int VNoProgSwings = 12;

	private const float VNoProgTime = 60f;

	private const float VWalkTime = 10f;

	private GameObject _vFor;

	private MineRock5 _vRock;

	private static readonly List<MHR_Vein.Area> s_vAreas = new List<MHR_Vein.Area>();

	private readonly List<VPart> _vParts = new List<VPart>();

	private readonly List<VSpot> _vSpots = new List<VSpot>();

	private Bounds _vUnion;

	private float _vLowest;

	private float _vHealth;

	private int _vDirtCount;

	private int _vSide = -1;

	private int _vFirstSide = -1;

	private Collider _vPart;

	private readonly Dictionary<Collider, GiveUpWhy> _vPartLeft = new Dictionary<Collider, GiveUpWhy>();

	private readonly Dictionary<long, int> _vSpotTried = new Dictionary<long, int>();

	private readonly HashSet<long> _vStuck = new HashSet<long>();

	private float _vProgAt;

	private bool _vSwung;

	private float _vEquipSince = -1f;

	private float _vSwungAt;

	private bool _vSwingDig;

	private long _vSwingKey;

	private Vector3 _vSwingAim;

	private float _vLastHealth;

	private int _vLastAlive;

	private int _vLastDirt;

	private bool _vDigPending;

	private Vector3 _vDigAt;

	private float _vDigGround;

	private float _vDigAimDeg;

	private static readonly Collider[] s_vBuf = new Collider[64];

	private static int s_vMask = -1;

	private static int s_vTerrain = -1;

	private static readonly HashSet<string> s_toolLogged = new HashSet<string>();

	private float _vUnionAt = -100f;

	// ---------- part 1 G3: the leash ----------
	// Log 97d599fc: a walk with no limit took Gunter 150 m from the vein. While its vein work is what moves it (the vein
	// job itself, getting out, its own steps), more than LeashOut outside the vein's box: stop, walk back with the
	// mover, plan again from where it stands. LeashBackTime to get back. The count is per vein and a new plan doesn't
	// reset it: a second time, or not back in time, is the existing give-up and it waits (set a job, or move it 5 m).
	private const float LeashOut = 20f;

	private const float LeashNear = 10f;

	private const float LeashBackTime = 60f;

	private int _leashId;

	private int _leashCount;

	private bool _leashBack;

	private float _leashSince;

	private float LeashDist(Vector3 me, out Vector3 near)
	{
		near = _vUnion.ClosestPoint(me);
		near.y = GroundAt(near);
		return Flat(me, near);
	}

	private void LeashStop(Vector3 me, string detail)
	{
		_leashBack = false;
		_xActive = false;
		_pitActive = false;
		_pitDropped = false;
		_ai.StopMoving();
		MHR_Mover.Release(_ai);
		SayGaveUp(GiveUpWhy.OutOfReach, detail);
		if (_vFor != null)
		{
			VeinLeave();
		}
		_xPendingAfter = false;
		_stuckInHole = true;
		_stuckAt = me;
		LogWarn($"Stopping the Ore job at {MHR_Mover.Fmt(me)}: {detail} - waiting for you (set a job, or move it 5 m)");
	}

	private bool LeashTick()
	{
		if (_ai == null || _character == null || _humanoid == null || _vUnionAt < 0f || _stuckInHole)
		{
			_leashBack = false;
			return false;
		}
		Vector3 me = base.transform.position;
		if (_leashBack)
		{
			float d = LeashDist(me, out Vector3 near);
			if (d <= LeashNear)
			{
				_leashBack = false;
				_ai.StopMoving();
				if (CompanionsPlugin.LogNormal) LogInfo($"Back by the vein at {MHR_Mover.Fmt(me)} ({d:F0} m from it) - planning again from here");
				if (_vFor != null)
				{
					_vFor = null;   // VeinTick plans the vein again from where it stands
				}
				else
				{
					_xPendingAfter = true;   // it was getting out after the vein: that starts again from here
				}
				return false;
			}
			if (Time.time - _leashSince > LeashBackTime)
			{
				LeashStop(me, $"couldn't get back to the vein in {LeashBackTime:F0} s (still {d:F0} m from it)");
				return true;
			}
			MHR_Mover.Go(_ai, Time.deltaTime, near, 2f, true, "Vein");
			return true;
		}
		// a getting-out that is heading somewhere far off (you on Follow, a chest at home) isn't held to the vein
		bool xNear = _xActive && Flat(_vUnion.ClosestPoint(_xRim), _xRim) <= LeashOut;
		bool veinWork = (_state == HarvestState.Attacking && _vFor != null && _vFor == _target) || xNear || (_pitActive && _pitVein);
		if (!veinWork)
		{
			return false;
		}
		if (_vFor != null && _vFor.GetInstanceID() != _leashId)
		{
			_leashId = _vFor.GetInstanceID();
			_leashCount = 0;
		}
		float dist = LeashDist(me, out _);
		if (dist <= LeashOut || _humanoid.InAttack())
		{
			return false;
		}
		_leashCount++;
		if (_leashCount >= 2)
		{
			LeashStop(me, $"{dist:F0} m outside the vein while working it, for the second time on this vein");
			return true;
		}
		_leashBack = true;
		_leashSince = Time.time;
		_xActive = false;
		_pitActive = false;
		_pitDropped = false;
		_tChopping = false;
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"{dist:F0} m outside the vein while working it (at {MHR_Mover.Fmt(me)}) - coming back ({LeashBackTime:F0} s at most)");
		return true;
	}

	// the vein it's working (or just finished): its box, the side it started on and the side it's on now (-1 = none)
	internal bool VeinBox(out Bounds box, out int firstSide, out int side)
	{
		box = _vUnion;
		firstSide = _vFirstSide;
		side = _vSide;
		return Time.time - _vUnionAt < 30f;
	}

	internal static Vector3 SideDirOf(int s)
	{
		return SideDir(s < 0 ? 0 : s);
	}

	// vein work is on: the swing's hit on the ground may dig (SpawnOnHitTerrain asks AllowDig)
	internal bool VeinWorking => _state == HarvestState.Attacking && _vFor != null && _vFor == _target && DigMode();

	// 1.5.97 (test review A2, A3): the vein it left because its pickaxe broke (none other in the bag)
	private bool _leaveNoTool;

	private GameObject _noToolRock;

	private float _noToolNext;

	private bool _tEnterNear;

	// the same rule as FindBestTool(3), without its log lines: a pickaxe that isn't broken
	private bool HasWorkingPick()
	{
		Inventory inv = _humanoid != null ? _humanoid.GetInventory() : null;
		if (inv == null)
		{
			return false;
		}
		foreach (ItemDrop.ItemData it in inv.GetAllItems())
		{
			if (it?.m_shared == null || Worn(it) || it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow || !string.IsNullOrEmpty(it.m_shared.m_ammoType))
			{
				continue;
			}
			if (it.GetDamage().m_pickaxe > 0f)
			{
				return true;
			}
		}
		return false;
	}

	// once a second while the Ore job is on and that vein is still there: a working pickaxe in the bag (you gave it one,
	// it picked one up) stops the getting-out where it stands, ends a "stuck in the hole" wait, and the normal scan takes
	// the vein again - like a player who equips another pickaxe and carries on. You more than follow range + 15 m away:
	// the normal rules (it catches up first; this keeps checking).
	private void NoToolTick(int mode)
	{
		if (_noToolRock == null)
		{
			_noToolRock = null;   // the vein is gone
			return;
		}
		if (mode != 3 || VeinWorking)
		{
			_noToolRock = null;
			return;
		}
		if (Time.time < _noToolNext)
		{
			return;
		}
		_noToolNext = Time.time + 1f;
		if (_humanoid == null || _humanoid.InAttack() || !HasWorkingPick())
		{
			return;
		}
		Player owner = MHR_Owner.Of(this);
		if (_setup != null && _setup.GetFollow() && owner != null && Vector3.Distance(base.transform.position, owner.transform.position) > PlayerRange() + 15f)
		{
			return;
		}
		bool wasOut = _xActive || _pitActive, wasStuck = _stuckInHole;
		_xActive = false;
		_xPendingAfter = false;
		_pitActive = false;
		_pitDropped = false;
		_stuckInHole = false;
		if (_ai != null)
		{
			_ai.StopMoving();
			MHR_Mover.Release(_ai);
		}
		_blacklist.Remove(_noToolRock.GetInstanceID());
		_tEnterNear = true;
		_noToolRock = null;
		_scanTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo($"Has a working pickaxe again - back to the vein{(wasOut ? " (it stops getting out)" : wasStuck ? " (no longer stuck in the hole)" : "")}");
	}

	private void VeinReset(GameObject target)
	{
		VPlanLeave();
		_vp = null;
		_vFor = target;
		_vRock = target != null ? target.GetComponent<MineRock5>() : null;
		_vSide = -1;
		_vFirstSide = -1;
		_vPart = null;
		_vPartLeft.Clear();
		_vSpotTried.Clear();
		_vStuck.Clear();
		_vProgAt = Time.time;
		_vSwung = false;
		_vDigPending = false;
		_vSpotsAt = -1f;
		TReset();
		VeinScan();
		_vLastHealth = _vHealth;
		_vLastAlive = _vParts.Count;
		_vLastDirt = _vDirtCount;
	}

	// ---------- what the dirt still holds (the game's support test) ----------
	private void VeinScan(bool force = true)
	{
		if (!force && Time.time < _vScanAt)
		{
			return;
		}
		_vScanAt = Time.time + 0.25f;
		_vParts.Clear();
		_vHealth = 0f;
		_vDirtCount = 0;
		if (_vRock == null || !MHR_Vein.Areas(_vRock, s_vAreas, out bool haveBounds))
		{
			return;
		}
		if (s_vMask < 0)
		{
			s_vMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
			s_vTerrain = LayerMask.NameToLayer("terrain");
		}
		Vector3 origin = _vRock.transform.position;
		bool first = true;
		_vLowest = float.MaxValue;
		int areaIdx = -1;
		foreach (MHR_Vein.Area ha in s_vAreas)
		{
			areaIdx++;
			if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
			{
				continue;
			}
			VPart p = default(VPart);
			p.C = ha.Collider;
			p.Idx = areaIdx;
			p.Health = ha.Health;
			if (haveBounds)
			{
				p.Center = origin + ha.BoundPos;
				p.Half = ha.BoundSize;
			}
			else
			{
				p.Center = ha.Collider.bounds.center;
				p.Half = ha.Collider.bounds.size * 0.5f;
			}
			int n = Physics.OverlapBoxNonAlloc(p.Center, p.Half, s_vBuf, Quaternion.identity, s_vMask);
			for (int i = 0; i < n; i++)
			{
				Collider c = s_vBuf[i];
				if (c == null || c == ha.Collider || c.attachedRigidbody != null || c.isTrigger)
				{
					continue;
				}
				if (c.gameObject.layer == s_vTerrain)
				{
					p.Dirt = true;
					continue;
				}
				IDestructible d = c.gameObject.GetComponentInParent<IDestructible>();
				if (d != null)
				{
					if ((object)d == _vRock)
					{
						continue;   // a neighbour part: held only if it is
					}
					if (c.transform.position.y < origin.y)
					{
						p.Low = true;
					}
					continue;
				}
				p.Solid = true;
			}
			_vParts.Add(p);
			_vHealth += p.Health;
			if (p.Dirt)
			{
				_vDirtCount++;
			}
			_vLowest = Mathf.Min(_vLowest, p.MinY);
			Bounds b = new Bounds(p.Center, p.Half * 2f);
			if (first)
			{
				_vUnion = b;
				first = false;
				_vUnionAt = Time.time;
			}
			else
			{
				_vUnion.Encapsulate(b);
			}
		}
	}

	// the dirt that holds parts: the ground inside each dirt-held part's box (a 0.5 m grid over its footprint) that is
	// above the box's bottom, skipping spots that wouldn't go lower
	private float _vSpotsAt = -1f;

	private float _vScanAt = -1f;

	private void VeinSpots(bool force = false)
	{
		// at most twice a second (it samples the ground over every part) unless a swing just changed things
		if (!force && Time.time < _vSpotsAt)
		{
			return;
		}
		_vSpotsAt = Time.time + 0.5f;
		_vSpots.Clear();
		HashSet<long> seen = new HashSet<long>();
		foreach (VPart p in _vParts)
		{
			if (!p.Dirt)
			{
				continue;
			}
			int nx = Mathf.Clamp(Mathf.CeilToInt(p.Half.x * 2f / 0.5f) + 1, 2, 12);
			int nz = Mathf.Clamp(Mathf.CeilToInt(p.Half.z * 2f / 0.5f) + 1, 2, 12);
			for (int ix = 0; ix < nx; ix++)
			{
				for (int iz = 0; iz < nz; iz++)
				{
					float x = p.Center.x - p.Half.x + p.Half.x * 2f * ix / (nx - 1);
					float z = p.Center.z - p.Half.z + p.Half.z * 2f * iz / (nz - 1);
					long key = SpotKey(x, z);
					if (!seen.Add(key) || _vStuck.Contains(key))
					{
						continue;
					}
					Vector3 q = new Vector3(x, p.Center.y, z);
					float h = GroundAt(q);
					if (h <= p.MinY - VDigMin)
					{
						continue;
					}
					q.y = h;
					_vSpots.Add(new VSpot
					{
						P = q,
						Key = key,
						Part = p.C,
						PartCenter = p.Center
					});
				}
			}
		}
	}

	private static long SpotKey(float x, float z)
	{
		long a = Mathf.RoundToInt(x * 2f);
		long b = Mathf.RoundToInt(z * 2f);
		return (a << 32) ^ (b & 0xFFFFFFFFL);
	}

	// 0 N (+z), 1 E (+x), 2 S (-z), 3 W (-x) of the whole vein (the stone toss and the ore spot go by it)
	private int SideOf(Vector3 p)
	{
		Vector3 d = p - _vUnion.center;
		float ex = Mathf.Max(0.1f, _vUnion.extents.x);
		float ez = Mathf.Max(0.1f, _vUnion.extents.z);
		if (Mathf.Abs(d.x) / ex >= Mathf.Abs(d.z) / ez)
		{
			return d.x >= 0f ? 1 : 3;
		}
		return d.z >= 0f ? 0 : 2;
	}

	private static Vector3 SideDir(int s)
	{
		switch (s)
		{
		case 0:
			return Vector3.forward;
		case 1:
			return Vector3.right;
		case 2:
			return Vector3.back;
		default:
			return Vector3.left;
		}
	}

	private float Reach()
	{
		return GetAttackRange();
	}

	// ---------- each frame while working the vein ----------
	private void VeinTick()
	{
		if (_vFor != _target)
		{
			VeinReset(_target);
			if (_vRock == null)
			{
				ResetToIdle();
				return;
			}
			ItemDrop.ItemData best = FindBestTool(3);
			if (best != null && best.m_shared.m_toolTier < _vRock.m_minToolTier)
			{
				SayGaveUp(GiveUpWhy.TooHard, $"best pickaxe tier {best.m_shared.m_toolTier}, the vein needs {_vRock.m_minToolTier}");
				VeinLeave();
				return;
			}
			VeinLogParts();
			LogToolOnce(ReflectionHelper.GetRightItem(_humanoid));
			TPlanEnter();
		}
		if (_humanoid == null || _character == null || _ai == null)
		{
			return;
		}
		bool busy = _humanoid.InAttack() || _humanoid.InDodge() || _character.IsStaggering();
		// 1.5.97 (John 2026-11-15): the pickaxe in hand first, before anything else - checked every update (not while it
		// has the axe out for a tree in the way)
		if (!busy && !_vSwung && !_tChopping)
		{
			ItemDrop.ItemData inHand = ReflectionHelper.GetRightItem(_humanoid);
			if (inHand == null || inHand.GetDamage().m_pickaxe <= 0f || Worn(inHand))
			{
				ItemDrop.ItemData pick = FindBestTool(3);
				if (pick == null)
				{
					SayGaveUp(GiveUpWhy.NoTool, "in hand: " + (inHand?.m_shared?.m_name ?? "nothing") + (Worn(inHand) ? " (worn out)" : ""));
					VeinLeave();
					return;
				}
				// S0 (L6): still not in hand 5 s after it first tried: give up with the "no tool" reason
				if (_vEquipSince < 0f)
				{
					_vEquipSince = Time.time;
				}
				else if (Time.time - _vEquipSince > 5f)
				{
					_vEquipSince = -1f;
					SayGaveUp(GiveUpWhy.NoTool, "couldn't get \"" + pick.m_shared.m_name + "\" in hand in 5 s");
					VeinLeave();
					return;
				}
				_ai.StopMoving();
				EquipTool(pick);
				if (ReflectionHelper.GetRightItem(_humanoid) == pick && CompanionsPlugin.LogNormal)
				{
					LogInfo("Pickaxe back in hand (\"" + pick.m_shared.m_name + "\"; it had " + (inHand?.m_shared?.m_name ?? "nothing") + " in its hand)");
				}
				return;
			}
		}
		if (!_tChopping)
		{
			_vEquipSince = -1f;   // the pickaxe is in hand
		}
		// a swing has finished: what did it do?
		if (_vSwung && !busy && Time.time - _vSwungAt > 0.35f)
		{
			_vSwung = false;
			TJudge();
			_vSpotsAt = -1f;
			if (_vFor == null || _target == null)
			{
				return;
			}
		}
		if (busy || _vSwung)
		{
			VeinFace(_vSwingAim);
			return;
		}
		VeinScan(force: false);
		if (_vParts.Count == 0)
		{
			VPlanDrop();   // the vein is gone: its plan with it
			_xPendingAfter = true;   // out of the trench once the ore is picked up (VeinExitTick)
			return;   // all broken: the next frame sees the target gone and collects the ore
		}
		TTick();
	}

	// mining: a part that still holds the rest (touching dirt, rock or a building), nearest first; else the free part
	// nearest to it
	private Collider VeinPickPart()
	{
		Collider best = null;
		float bd = float.MaxValue;
		bool bestHolds = false;
		foreach (VPart p in _vParts)
		{
			if (_vPartLeft.ContainsKey(p.C))
			{
				continue;
			}
			float d = Vector3.Distance(base.transform.position, p.Center);
			bool holds = p.Direct;
			if ((holds && !bestHolds) || (holds == bestHolds && d < bd))
			{
				best = p.C;
				bd = d;
				bestHolds = holds;
			}
		}
		if (best != null && CompanionsPlugin.LogDetailed)
		{
			Log($"Breaking out part \"{best.name}\" ({(bestHolds ? "it holds the rest" : "free - the rest pops with it")}, {bd:F1}m away)");
		}
		return best;
	}

	private void VeinFace(Vector3 aim)
	{
		Vector3 flat = aim - base.transform.position;
		flat.y = 0f;
		_ai.LookAtPoint(aim);
		if (flat.sqrMagnitude > 0.001f)
		{
			MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(flat.normalized));
		}
		// test review C2: an aim point straight above or below its eye has no direction on the flat - the game's
		// SetLookDir (Character.cs:3147) then logs "Look rotation viewing vector is zero" every call; the look stays
		Vector3 look = aim - Eye();
		if (look.sqrMagnitude > 0.01f && look.x * look.x + look.z * look.z > 0.01f)
		{
			_character.SetLookDir(look.normalized);
		}
	}

	// one swing at a spot: the tool, a breather when low on stamina, then the game's own attack
	private void VeinSwing(Vector3 aim, bool dig, long key)
	{
		// the progress the swing is measured against
		float health = _vHealth;
		int alive = _vParts.Count, dirt = _vDirtCount;
		bool leave;
		if (!ToolSwing(aim, out leave))
		{
			if (leave)
			{
				VeinLeave();
			}
			return;
		}
		_vLastHealth = health;
		_vLastAlive = alive;
		_vLastDirt = dirt;
		_tHitObj = null;
		_vDigPending = false;
		_vDigAimDeg = Mathf.Asin(Mathf.Clamp((aim - Eye()).normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
		_vSwung = true;
		_vSwungAt = Time.time;
		_vSwingDig = dig;
		_vSwingKey = key;
		_vSwingAim = aim;
	}

	// face the spot and swing the pickaxe like a player: a working pickaxe in hand (a worn one swapped), a breather when
	// low on stamina (the swing's own cost, by Pickaxes skill), then the game's attack. True = a swing started; leave =
	// no working pickaxe left (it has said so).
	private bool ToolSwing(Vector3 aim, out bool leave)
	{
		leave = false;
		VeinFace(aim);
		_attackTimer -= Time.deltaTime;
		if (_attackTimer > 0f)
		{
			return false;
		}
		ItemDrop.ItemData tool = ReflectionHelper.GetRightItem(_humanoid);
		if (tool == null || tool.GetDamage().m_pickaxe <= 0f || Worn(tool))
		{
			ItemDrop.ItemData best = FindBestTool(3);
			if (best == null)
			{
				SayGaveUp(GiveUpWhy.NoTool, "in hand: " + (tool?.m_shared?.m_name ?? "nothing") + (Worn(tool) ? " (worn out)" : ""));
				leave = true;
				return false;
			}
			EquipTool(best);
			_attackTimer = 0.25f;
			return false;
		}
		LogToolOnce(tool);
		float cost = SwingStamina(tool, secondary: false);
		if (_stamina != null && cost > 0f)
		{
			float max = Mathf.Max(1f, _stamina.MaxStamina);
			if (_catchingBreath)
			{
				if (_stamina.Stamina < (ModConfig.GatherResumeStamina?.Value ?? 0.6f) * max)
				{
					_attackTimer = 0.5f;
					return false;
				}
				_catchingBreath = false;
				if (CompanionsPlugin.LogDetailed) Log($"Got its breath back ({_stamina.Stamina:F0}/{max:F0}) - back to work");
			}
			if (_stamina.Stamina < cost + (ModConfig.GatherRestStamina?.Value ?? 0.15f) * max)
			{
				_catchingBreath = true;
				_attackTimer = 0.5f;
				if (CompanionsPlugin.LogDetailed) Log($"Catching its breath ({_stamina.Stamina:F0}/{max:F0} stamina) before swinging again");
				return false;
			}
		}
		bool started = _humanoid.StartAttack(null, secondaryAttack: false);
		_totalAttempts++;
		_attackTimer = started ? 0.4f : 0.5f;
		if (started)
		{
			_swingCount++;
		}
		return started;
	}

	private void VeinPartFail(GiveUpWhy why, string detail)
	{
		if (_vPart != null)
		{
			_vPartLeft[_vPart] = why;
			if (CompanionsPlugin.LogNormal) LogInfo("Leaving part \"" + _vPart.name + "\" of \"" + _target.name + "\" (" + detail + ")");
		}
		_vPart = null;
		_tItemNoProg = 0;
	}

	// nothing left it can dig or break from the trench: say why and move on
	private void VeinGiveUp()
	{
		GiveUpWhy why = GiveUpWhy.Buried;
		bool allReach = _vPartLeft.Count > 0;
		foreach (GiveUpWhy w in _vPartLeft.Values)
		{
			allReach &= w == GiveUpWhy.OutOfReach;
		}
		// a part still held by dirt it can't dig (below the water, or at the game's 8 m limit): buried too deep
		bool heldLeft = false;
		foreach (VPart p in _vParts)
		{
			if (p.Dirt)
			{
				heldLeft = true;
				break;
			}
		}
		if (_tAnyWard)
		{
			why = GiveUpWhy.Ward;
		}
		else if (_tAnyCantDig)
		{
			why = GiveUpWhy.CantDig;
		}
		else if (!heldLeft && allReach)
		{
			why = GiveUpWhy.OutOfReach;
		}
		SayGaveUp(why, $"{_vParts.Count} part(s) left, {_vDirtCount} touching dirt, {_vStuck.Count} dirt spot(s) that won't go lower, {_vPartLeft.Count} part(s) it couldn't get a swing at, its feet {base.transform.position.y:F1}");
		VeinLeave();
	}

	private void VeinLeave()
	{
		bool stop = false;
		bool noTool = _leaveNoTool;
		_leaveNoTool = false;
		if (_target != null && noTool)
		{
			// 1.5.97 (test review A2, A3): no working pickaxe isn't giving up on the vein - it isn't skipped and doesn't
			// count toward "gave up twice"; a working pickaxe in the bag sends it back (NoToolTick)
			_noToolRock = _target;
			_noToolNext = Time.time + 1f;
		}
		else if (_target != null)
		{
			int id = _target.GetInstanceID();
			_blacklist[id] = Time.time;
			// 1.5.97 (S9, John's Q1): given up on the same vein twice in a row with nothing broken in between - it stops
			// and waits for you (set a job, or move it 5 m), so it never goes round the same vein forever
			_vGiveUps.TryGetValue(id, out int n);
			_vGiveUps[id] = ++n;
			_vGiveUpWhy.TryGetValue(id, out string before);
			_vGiveUpWhy[id] = _lastGiveUpText;
			if (n >= 2)
			{
				stop = true;
				_vGiveUps.Remove(id);
				_vGiveUpWhy.Remove(id);
				LogWarn($"Stopping the Ore job: gave up on \"{_target.name}\" twice with nothing broken in between (1: {before}; 2: {_lastGiveUpText}) at {MHR_Mover.Fmt(base.transform.position)} - waiting for you (set a job, or move it 5 m)");
			}
		}
		_xPendingAfter = true;
		_tChopping = false;
		VPlanLeave();
		_vFor = null;
		_vRock = null;
		MHR_GatherRules.AfterVein(this);
		ResetToIdle();
		if (stop)
		{
			_stuckInHole = true;
			_stuckAt = base.transform.position;
			_xPendingAfter = false;
		}
	}

	// SpawnOnHitTerrain asks: may this swing's hit on the ground dig? A dig swing of the vein job (the trench, the dirt in
	// the way, the dirt under a held part), not below the water level, within 2.5 m of the vein; and the steps out of a
	// hole or trench (HarvestPit.cs). A swing meant for the rock never digs.
	internal bool AllowDig(Vector3 hitPoint)
	{
		if (PitDigging && Vector3.Distance(hitPoint, base.transform.position) < 3f)
		{
			return hitPoint.y >= TWater;
		}
		if (!VeinWorking || _vParts.Count == 0 || !_tDigOK)
		{
			return false;
		}
		if (hitPoint.y < TWater || hitPoint.y < _tDigMinY - 0.05f)
		{
			return false;
		}
		Bounds b = _vUnion;
		float dx = Mathf.Max(0f, Mathf.Max(b.min.x - hitPoint.x, hitPoint.x - b.max.x));
		float dz = Mathf.Max(0f, Mathf.Max(b.min.z - hitPoint.z, hitPoint.z - b.max.z));
		return Mathf.Sqrt(dx * dx + dz * dz) <= 2.5f;
	}

	// pop test D1: is a companion on the Ore job within r of this place?
	// part 1 L1: any companion within r, on any job (your demonstration is logged with them on Follow)
	internal static bool AnyCompanionNear(Vector3 p, float r)
	{
		foreach (CompanionSetup cs in CompanionSetup.AllCompanions)
		{
			if (cs != null && (cs.transform.position - p).sqrMagnitude <= r * r)
			{
				return true;
			}
		}
		return false;
	}

	// part 1 L2: a vein's parts as the companions' own scan reads them (the same boxes and the same "touching dirt"
	// test as VeinScan), for the lines about your own work. full = one line per part through log.
	internal static void VeinCount(MineRock5 rock, bool full, System.Action<string> log, out int parts, out int dirt)
	{
		parts = 0;
		dirt = 0;
		if (rock == null || !MHR_Vein.Areas(rock, s_vAreas, out bool haveBounds))
		{
			return;
		}
		if (s_vMask < 0)
		{
			s_vMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
			s_vTerrain = LayerMask.NameToLayer("terrain");
		}
		Vector3 origin = rock.transform.position;
		List<string> lines = full ? new List<string>() : null;
		Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
		foreach (MHR_Vein.Area ha in s_vAreas)
		{
			if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
			{
				continue;
			}
			Vector3 centre = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
			Vector3 half = haveBounds ? ha.BoundSize : ha.Collider.bounds.size * 0.5f;
			bool held = false;
			int n = Physics.OverlapBoxNonAlloc(centre, half, s_vBuf, Quaternion.identity, s_vMask);
			for (int i = 0; i < n; i++)
			{
				Collider c = s_vBuf[i];
				if (c != null && c != ha.Collider && c.attachedRigidbody == null && !c.isTrigger && c.gameObject.layer == s_vTerrain)
				{
					held = true;
					break;
				}
			}
			parts++;
			if (held)
			{
				dirt++;
			}
			min = Vector3.Min(min, centre - half);
			max = Vector3.Max(max, centre + half);
			lines?.Add($"  part \"{ha.Collider.name}\": centre {MHR_Mover.Fmt(centre)}, size {half.x * 2f:F1} x {half.y * 2f:F1} x {half.z * 2f:F1}, bottom {centre.y - half.y:F1}, ground {GroundAt(centre):F1}, {(held ? "touching dirt" : "not touching dirt")}, health {ha.Health:F0}");
		}
		if (full && log != null)
		{
			log($"Vein \"{rock.name}\" at {MHR_Mover.Fmt(origin)}: {parts} parts, {dirt} touching dirt, box {MHR_Mover.Fmt(min)} to {MHR_Mover.Fmt(max)}");
			foreach (string l in lines)
			{
				log(l);
			}
		}
	}

	internal static bool AnyOreNear(Vector3 p, float r)
	{
		foreach (CompanionSetup cs in CompanionSetup.AllCompanions)
		{
			if (cs == null || (cs.transform.position - p).sqrMagnitude > r * r)
			{
				continue;
			}
			HarvestController h = cs.GetComponent<HarvestController>();
			if (h != null && h.GetMode() == 3)
			{
				return true;
			}
		}
		return false;
	}

	internal void NotifyDig(Vector3 hitPoint)
	{
		_lastHitAt = Time.time;
		_noDamageSwings = 0;
		if (PitDigging)
		{
			PitNotifyDig(hitPoint);
			return;
		}
		_vDigPending = true;
		_vDigAt = hitPoint;
		_vDigGround = GroundAt(hitPoint);
	}

	// ---------- Detailed log: the vein's parts, the tool's swing and dig settings ----------
	private void VeinLogParts()
	{
		if (!CompanionsPlugin.LogDetailed || _vRock == null)
		{
			return;
		}
		Log($"Vein \"{_target.name}\": {_vParts.Count} parts, {_vDirtCount} touching dirt, needs tool tier {_vRock.m_minToolTier}, part health {_vRock.m_health:F0}, box {MHR_Mover.Fmt(_vUnion.min)} to {MHR_Mover.Fmt(_vUnion.max)}");
		int i = 0;
		foreach (VPart p in _vParts)
		{
			Log($"  part {i++} \"{p.C.name}\": centre {MHR_Mover.Fmt(p.Center)}, size {p.Half.x * 2f:F1} x {p.Half.y * 2f:F1} x {p.Half.z * 2f:F1}, bottom {p.MinY:F1}, ground {GroundAt(p.Center):F1}, held by {(p.Dirt ? "dirt " : "")}{(p.Solid ? "solid " : "")}{(p.Low ? "low-rock " : "")}{(p.Direct ? "" : "neighbours only")}, health {p.Health:F0}");
		}
	}

	private void LogToolOnce(ItemDrop.ItemData tool)
	{
		if (!CompanionsPlugin.LogDetailed || tool?.m_shared == null || !s_toolLogged.Add(tool.m_shared.m_name))
		{
			return;
		}
		Attack a = tool.m_shared.m_attack;
		string dig = "no dig";
		GameObject op = tool.m_shared.m_spawnOnHitTerrain;
		if (op != null)
		{
			TerrainOp t = op.GetComponent<TerrainOp>();
			if (t != null && t.m_settings != null)
			{
				TerrainOp.Settings st = t.m_settings;
				dig = $"\"{op.name}\": level={st.m_level} r{st.m_levelRadius:F1} offset {st.m_levelOffset:F2}, raise={st.m_raise} r{st.m_raiseRadius:F1} power {st.m_raisePower:F2} delta {st.m_raiseDelta:F2}, smooth={st.m_smooth} r{st.m_smoothRadius:F1} power {st.m_smoothPower:F1}";
			}
			else
			{
				dig = "\"" + op.name + "\" (no TerrainOp on it)";
			}
		}
		if (a != null)
		{
			// 1.5.97 W1: where the game starts the swing (Attack.GetAttackOrigin): the origin joint, or its feet when none
			string joint = string.IsNullOrEmpty(a.m_attackOriginJoint) ? "none - its feet" : a.m_attackOriginJoint;
			float startY = a.m_attackHeight;
			if (!string.IsNullOrEmpty(a.m_attackOriginJoint) && _character != null && _character.GetVisual() != null)
			{
				Transform j = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
				if (j != null)
				{
					startY += j.position.y - base.transform.position.y;
				}
				else
				{
					joint += " (not found on its model)";
				}
			}
			Log($"Tool \"{tool.m_shared.m_name}\": swing starts at joint {joint}, {startY:F2} m above its feet; swing {a.m_attackType}, range {a.m_attackRange:F2}, height {a.m_attackHeight:F2}, angle {a.m_attackAngle:F0}, max up/down {a.m_maxYAngle:F0} deg, ray width {a.m_attackRayWidth:F2}, stamina {a.m_attackStamina:F0}, hits terrain {a.m_hitTerrain}, pickaxe rule {a.m_pickaxeSpecial}, hits several {a.m_multiHit}, through walls {a.m_hitThroughWalls}, less damage each when several {a.m_lowerDamagePerHit}; dig: {dig}");
		}
	}
}
