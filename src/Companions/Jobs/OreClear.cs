using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 build 2 (John's approved list reviews/1.5.98-clearing-pop-build-list.md, revised 2026-11-18 01:26Z): clearing and
// the pop. After the trench (and its way out) the companions mine the "roots" - the parts that hold the vein up by the
// game's own rule (MineRock5.UpdateSupport / GetSupport, same box and layer mask) - lowest first. Parts held only by other
// parts are never touched: when the last root breaks the game's CheckSupport drops them all at once. Mining a part is the
// Ore job's existing MineRock5 attack, pinned to that part (_ovPin).
public partial class HarvestController
{
	private sealed class OvPart
	{
		public MineRock5 Rock;

		public Collider C;

		public Vector3 Centre;

		public Vector3 Half;

		public bool Out;          // wholly under the lowest the ground can go: never uncovered, left

		public bool Root;         // holds the vein up (dirt, a solid thing, a lower breakable thing, a buried part)

		public string Why;

		public int Rounds;        // P7: mining tries that ended without it breaking (2 = given up)

		public bool GaveUp;

		public bool RampTried;    // P5: the way up toward it was cut (once per round)

		public HarvestController Holder;
	}

	private const float OvPartsEvery = 2f;         // P2: the parts are looked at every 2 s (and after a break)

	private const int OvPartRounds = 2;            // P7: 2 rounds per part

	private const int OvPopFails = 2;              // P7: 2 pops in a row that break nothing stop the vein work

	// the part it is mining (the hand-off) and the pinned collider the attack aims at
	private OvPart _ovPart;

	private bool _ovPartPop;

	private OvPart _rpPart;

	internal Collider _ovPin;

	// shared per plan (kept in a side table so OvPlan stays as it is)
	private sealed class OvClear
	{
		public readonly List<OvPart> Parts = new List<OvPart>();

		public readonly Dictionary<int, OvPart> ById = new Dictionary<int, OvPart>();

		public float At = -100f;

		public int Alive = -1;

		public int PopFails;

		public int Mined;

		public int Popped;

		public bool Done;

		public bool Stopped;

		public bool Said;
	}

	private static readonly Dictionary<OvPlan, OvClear> s_ovClear = new Dictionary<OvPlan, OvClear>();

	private static readonly List<MHR_Vein.Area> s_cAreas = new List<MHR_Vein.Area>();

	private static readonly Collider[] s_cBuf = new Collider[64];

	private static int s_cMask = -1;

	private static int s_cTerrain = -1;

	private OvClear OvC()
	{
		if (!s_ovClear.TryGetValue(_vp, out OvClear c))
		{
			if (s_ovClear.Count > 50)
			{
				s_ovClear.Clear();
			}
			c = new OvClear();
			s_ovClear[_vp] = c;
		}
		return c;
	}

	// P2: what holds the vein up, by the game's rule. A part's box (its collider's bounds, as MineRock5.SetupColliders
	// makes it) touching: the terrain, a solid thing that can't be broken, a breakable thing lower than the vein's
	// centre, or a part of this vein that is out of reach = a root. Touching only other parts = hanging.
	private void OvPartsLook(OvClear c, bool force)
	{
		if (!force && Time.time - c.At < OvPartsEvery)
		{
			return;
		}
		c.At = Time.time;
		if (s_cMask < 0)
		{
			s_cMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");   // MineRock5.m_rayMask
			s_cTerrain = LayerMask.NameToLayer("terrain");
		}
		c.Parts.Clear();
		int alive = 0, popped = 0;
		foreach (MineRock5 rock in _vp.Rocks)
		{
			if (rock == null || !MHR_Vein.Areas(rock, s_cAreas, out bool haveBounds))
			{
				continue;
			}
			Vector3 origin = rock.transform.position;
			int first = c.Parts.Count;
			foreach (MHR_Vein.Area ha in s_cAreas)
			{
				if (ha.Collider == null)
				{
					continue;
				}
				int id = ha.Collider.GetInstanceID();
				if (ha.Health <= 0f || !ha.Collider.gameObject.activeSelf)
				{
					if (c.ById.TryGetValue(id, out OvPart gone) && gone.Holder == null && !gone.GaveUp)
					{
						popped++;   // it was standing at the last look and nobody was mining it
					}
					c.ById.Remove(id);
					continue;
				}
				if (!c.ById.TryGetValue(id, out OvPart part))
				{
					part = new OvPart { Rock = rock, C = ha.Collider };
					c.ById[id] = part;
				}
				part.Centre = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
				part.Half = haveBounds ? ha.BoundSize : ha.Collider.bounds.size * 0.5f;
				part.Out = part.Centre.y + part.Half.y < OvFloor(part.Centre);
				part.Root = false;
				part.Why = null;
				c.Parts.Add(part);
				alive++;
			}
			for (int i = first; i < c.Parts.Count; i++)
			{
				OvPart part = c.Parts[i];
				if (part.Out)
				{
					continue;
				}
				int n = Physics.OverlapBoxNonAlloc(part.Centre, part.Half, s_cBuf, Quaternion.identity, s_cMask);
				for (int j = 0; j < n && !part.Root; j++)
				{
					Collider o = s_cBuf[j];
					if (o == null || o == part.C || o.attachedRigidbody != null || o.isTrigger)
					{
						continue;
					}
					if (o.gameObject.layer == s_cTerrain)
					{
						part.Root = true;
						part.Why = "touches the dirt";
						continue;
					}
					MineRock5 m = o.GetComponentInParent<MineRock5>();
					if (m == rock)
					{
						if (c.ById.TryGetValue(o.GetInstanceID(), out OvPart other) && other.Out)
						{
							part.Root = true;
							part.Why = "rests on a part buried out of reach";
						}
						continue;   // held by another part: the chain
					}
					IDestructible d = o.GetComponentInParent<IDestructible>();
					if (d == null)
					{
						part.Root = true;
						part.Why = "rests on \"" + o.name + "\" (it can't be broken)";
					}
					else if (o.transform.position.y < origin.y)
					{
						part.Root = true;
						part.Why = "rests on \"" + o.name + "\" (lower than the vein's centre)";
					}
				}
			}
		}
		c.Popped += popped;
		c.Alive = alive;
	}

	private bool OvPartHeldByOther(OvPart part)
	{
		HarvestController h = part.Holder;
		if (h == null || h == this)
		{
			return false;
		}
		if (h._vp != _vp || (h._ovPart != part && h._rpPart != part))
		{
			part.Holder = null;
			return false;
		}
		return true;
	}

	private float OvPartBottom(OvPart part)
	{
		return part.Centre.y - part.Half.y;
	}

	// the clearing chooser (OvDoneTick, after the trench and its way out). True = busy (or standing by), false = done.
	private bool OvClearTick()
	{
		OvClear c = OvC();
		if (c.Done)
		{
			return false;
		}
		OvPartsLook(c, false);
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		foreach (OvPart part in c.Parts)
		{
			if (part.Root && !part.Out && !part.GaveUp)
			{
				cand.Add(part);
			}
		}
		bool pop = false;
		if (cand.Count == 0)
		{
			// P6: no root left but parts still stand (held by nothing we can see): the lowest one is mined - its break
			// makes the game check them all
			foreach (OvPart part in c.Parts)
			{
				if (!part.Out && !part.GaveUp)
				{
					cand.Add(part);
				}
			}
			pop = true;
		}
		if (cand.Count == 0)
		{
			c.Done = true;
			OvClearSay(c);
			return false;
		}
		// P3: the lowest first (within 1 m counts as level), then the nearest
		cand.Sort((OvPart a, OvPart b) =>
		{
			float ba = OvPartBottom(a), bb = OvPartBottom(b);
			if (Mathf.Abs(ba - bb) > 1f)
			{
				return ba.CompareTo(bb);
			}
			return Vector3.Distance(me, a.Centre).CompareTo(Vector3.Distance(me, b.Centre));
		});
		OvPart pick = null;
		foreach (OvPart part in cand)
		{
			if (!OvPartHeldByOther(part))
			{
				pick = part;
				break;
			}
		}
		if (pick == null)
		{
			_ai?.StopMoving();   // P4: every part left is another companion's - it stands by
			return true;
		}
		pick.Holder = this;
		// P5: up a wall within 3 m and 1.5 x its height above its feet: the way up toward it first (once a round)
		Vector3 near = pick.C.ClosestPoint(me);
		Vector3 foot = new Vector3(near.x, GroundAt(near), near.z);
		float h = _character != null ? _character.GetHeight() : 1.8f;
		if (!pick.RampTried && Flat(me, near) <= OvRampReach && foot.y >= me.y + OvRampRatio * h)
		{
			pick.RampTried = true;
			_rpPart = pick;
			OvRampBegin(foot, null, outOfTrench: false, $"the part \"{pick.C.name}\" is {foot.y - me.y:F1} m up");
			return true;
		}
		OvMinePart(pick, pop);
		return true;
	}

	// P3 / P5: the part goes to the Ore job's own MineRock5 attack, pinned to it
	private void OvMinePart(OvPart part, bool pop)
	{
		ItemDrop.ItemData tool = FindBestTool(3);
		if (tool == null)
		{
			OvDigOff();
			_noToolRock = _vFor;
			_noToolNext = Time.time + 1f;
			OvLeaveOut(base.transform.position, "no working pickaxe");
			return;
		}
		_ovPart = part;
		_ovPartPop = pop;
		_ovPin = part.C;
		_ai?.StopMoving();
		_target = part.Rock.gameObject;
		_targetType = "MineRock5";
		_swingCount = 0;
		_totalAttempts = 0;
		_consecutiveFailures = 0;
		_reapproach = false;
		_rock5ActiveSections = -1;
		_rock5CurrentSegment = part.C;
		EquipTool(tool);
		_moveTimer = 0f;
		_moveLogTimer = 0f;
		_moveTargetLocked = false;
		_attackFaceLocked = false;
		_state = HarvestState.Moving;
		if (CompanionsPlugin.LogNormal) LogInfo($"Clearing \"{_vp.Name}\": {(pop ? "no part holds it up any more but some still stand - mining the lowest" : "mining a part that holds it up")}: \"{part.C.name}\" at {MHR_Mover.Fmt(part.Centre)}, bottom {OvPartBottom(part):F1}{(part.Why != null ? " (" + part.Why + ")" : "")}, round {part.Rounds + 1} of {OvPartRounds}");
	}

	// the attack on the pinned part ended (it broke, the attack gave up, or the part fell): back to the chooser
	private void OvPartBack()
	{
		OvPart part = _ovPart;
		_ovPart = null;
		_ovPin = null;
		if (part == null || _vp == null)
		{
			return;
		}
		OvClear c = OvC();
		bool broke = part.C == null || !part.C.gameObject.activeSelf;
		part.Holder = null;
		part.RampTried = false;
		if (broke)
		{
			if (part.C != null)
			{
				c.ById.Remove(part.C.GetInstanceID());
			}
			c.Mined++;
			if (_ovPartPop)
			{
				c.PopFails = 0;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Mined the part \"{(part.C != null ? part.C.name : "?")}\" of \"{_vp.Name}\" - looking at what still holds it up");
		}
		else
		{
			part.Rounds++;
			if (_ovPartPop)
			{
				c.PopFails++;
			}
			if (part.Rounds >= OvPartRounds)
			{
				part.GaveUp = true;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{part.C.name}\" of \"{_vp.Name}\" didn't break (round {part.Rounds} of {OvPartRounds}{(part.GaveUp ? ", left" : "")})");
			if (c.PopFails >= OvPopFails && !c.Stopped)
			{
				// P7: John's rule - two tries with nothing broken: stop and wait, with everything we need to find out why
				c.Stopped = true;
				c.Done = true;
				OvClearSay(c);
			}
		}
		OvPartsLook(c, true);
	}

	// the walk to a part, or the attack on it, while the part broke or fell under it: the hand-off ends (Update)
	internal bool OvPinGone()
	{
		return _ovPart != null && (_ovPin == null || !_ovPin.gameObject.activeSelf);
	}

	// the attack's segment choice only takes the pinned part while one is pinned
	private bool OvPinSkip(Collider c)
	{
		return _ovPin != null && _ovPin.gameObject.activeSelf && c != _ovPin;
	}

	// P8 / P7: said once per vein
	private void OvClearSay(OvClear c)
	{
		if (c.Said)
		{
			return;
		}
		c.Said = true;
		int left = 0, outN = 0, gave = 0;
		foreach (OvPart part in c.Parts)
		{
			left++;
			if (part.Out)
			{
				outN++;
			}
			if (part.GaveUp)
			{
				gave++;
			}
		}
		if (c.Stopped)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Stopping on \"{_vp.Name}\": {OvPopFails} tries in a row broke nothing - every part as it stands:");
			foreach (OvPart part in c.Parts)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"  part \"{part.C.name}\": centre {MHR_Mover.Fmt(part.Centre)}, size {part.Half.x * 2f:F1} x {part.Half.y * 2f:F1} x {part.Half.z * 2f:F1}, bottom {OvPartBottom(part):F1}, ground {GroundAt(part.Centre):F1}, floor {OvFloor(part.Centre):F1}, {(part.Out ? "buried out of reach" : part.Root ? "holds it up (" + part.Why + ")" : "hanging on other parts")}, rounds {part.Rounds}{(part.GaveUp ? ", given up" : "")}");
			}
			_talk?.SayProblem("vein:clear_stopped", string.Format(ModLocalization.Loc("hc_speech_vein_clear_stopped"), MHR_Items.NiceName(_vFor)));
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{_vp.Name}\" is done: {c.Mined} part(s) mined, {c.Popped} fell by themselves, {left} left ({outN} buried out of reach, {gave} given up)");
	}

	// P8: the last parts fell and the vein is gone - the summary still gets said (the parts no one was mining fell)
	private void OvClearGone()
	{
		if (_vp == null || !s_ovClear.TryGetValue(_vp, out OvClear c) || c.Said)
		{
			return;
		}
		foreach (OvPart part in c.ById.Values)
		{
			if (part.Holder == null && !part.Out && !part.GaveUp)
			{
				c.Popped++;
			}
		}
		c.Parts.Clear();
		c.Done = true;
		OvClearSay(c);
	}

	// the pinned part broke (or fell): the drops are picked up the existing way, then back to the chooser (OvResume)
	private void OvPartDrops()
	{
		_lastDestroyPos = _ovPart != null ? _ovPart.Centre : base.transform.position;
		_currentDrop = null;
		_dropTimer = 0f;
		_dropScanDelayTimer = 0.5f;
		_dropsPickedUp = 0;
		_state = HarvestState.CollectingDrops;
		_ai?.StopMoving();
	}
}
