using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 build 2 (John's approved list reviews/1.5.98-clearing-pop-build-list.md, revised 2026-11-18 01:26Z): clearing and
// the pop. After the trench (and its way out) the companions mine the "roots" - the parts that hold the vein up by the
// game's own rule (MineRock5.UpdateSupport / GetSupport, same box and layer mask) - lowest first. Parts held only by other
// parts are never touched: when the last root breaks the game's CheckSupport drops them all at once. 1.5.98 N1-N5 (test 6):
// a part is reached the way a player does it - the trench's dig swing at a point 1 m further in toward it, hitting
// whatever is in front (dirt or the vein's rock), from the trench spot nearest it.
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

		public int Rounds;        // P7: mining tries that ended without it breaking (R2d: 2 = set aside, 3 = given up)

		public bool GaveUp;

		public bool SetAside;     // R2d: 2 rounds failed - it waits until every other root is mined or set aside

		public bool HasStart;     // R2c: where its last round started (the next starts at least 3 m from there)

		public Vector3 LastStart;

		public bool RampTried;    // P5: the way up toward it was cut (once per round)

		public GameObject HoldThing;   // Z2: the tree / log / stump / rock it rests on (the Wood or Stone job takes it)

		public int HoldMode;

		public string HoldType;

		public bool OutOfReach;   // Z3: its box bottom is more than the pickaxe's reach above the floor under it

		public bool Chain;        // Z5: made a root because the root it rests on couldn't be broken (once, never cleared)

		public string ChainWhy;

		public bool ChainCut;     // Z5: the parts resting on it were made roots (once)

		public bool LadderTried;  // Z4: one try at building a ladder for it

		public int LadderTrees;   // Z4: trees taken for its ladder's wood (at most 3)

		public HarvestController Holder;
	}

	private const float OvPartsEvery = 2f;         // P2: the parts are looked at every 2 s (and after a break)

	private const int OvPartRounds = 2;            // P7 / R2d: 2 rounds per part, then it is set aside ...

	private const int OvPartRoundsMax = 3;         // R2d: ... and gets one more when everything else is done or set aside

	private const float OvClOtherAngle = 3f;       // R2c: the next round starts at least 3 m from where the last one did

	private const int OvPopFails = 2;              // P7: 2 pops in a row that break nothing stop the vein work

	private OvPart _rpPart;


	// 1.5.98 L1-L4: the round on one part - the part it is for, where it stands (a trench spot, then each dug point), the
	// way in toward the part, how many points it dug and may dig, and the dig point's 30 s clock
	private OvPart _clFor;

	private bool _clPop;

	private Vector3 _clStand;

	private Vector3 _clDir;

	private bool _clWalk;

	private int _clStep;

	private int _clCap;

	private float _clBestG;

	private float _clIdle;

	private int _clAlive;

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
				part.HoldThing = null;
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
				OvPartRoot(part, rock, origin, c);
			}
		}
		// Z5: parts made roots because the root under them couldn't be broken stay roots
		// Z3: and whether each part can be hit from the floor under it
		float reach = OvPickReach();
		foreach (OvPart part in c.Parts)
		{
			if (part.Out)
			{
				continue;
			}
			if (part.Chain && !part.Root)
			{
				part.Root = true;
				part.Why = part.ChainWhy;
			}
			part.OutOfReach = OvPartBottom(part) - GroundAt(part.Centre) > reach;
		}
		c.Popped += popped;
		c.Alive = alive;
	}

	// the game's support test for one part (MineRock5.UpdateSupport / GetSupport, same box and layer mask)
	private void OvPartRoot(OvPart part, MineRock5 rock, Vector3 origin, OvClear c)
	{
		{
			{
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
						// Z2: a tree, log, stump or rock the Wood / Stone job would take: that is cleared, not the part
						GameObject t = OvThingOf(o, out int tm, out string tt);
						if (t != null)
						{
							part.HoldThing = t;
							part.HoldMode = tm;
							part.HoldType = tt;
						}
						part.Why = "rests on \"" + o.name + "\"" + (t != null ? $" ({t.name} at {MHR_Mover.Fmt(t.transform.position)})" : "") + " (lower than the vein's centre)";
					}
				}
			}
		}
	}

	private bool OvPartHeldByOther(OvPart part)
	{
		HarvestController h = part.Holder;
		if (h == null || h == this)
		{
			return false;
		}
		if (h._vp != _vp || (h._rpPart != part && h._clFor != part))
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
		if (_clFor != null)
		{
			OvClRound(c);
			return true;
		}
		// Y1 / Y6a: a way out first, then the things in the pit, before the next round
		if (OvFirst())
		{
			return true;
		}
		// Z5: a root it couldn't break - the parts resting on it become roots (each root's once)
		bool cut = false;
		foreach (OvPart part in c.Parts)
		{
			if (part.Root && part.GaveUp && !part.ChainCut && !part.Out)
			{
				OvChainCut(c, part);
				cut = true;
			}
		}
		if (cut)
		{
			OvPartsLook(c, true);
		}
		// Z2: a tree, log, stump or rock a root rests on is cleared first, the Wood / Stone job's way
		if (OvHoldFirst(c))
		{
			return true;
		}
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		foreach (OvPart part in c.Parts)
		{
			if (part.Root && !part.Out && !part.GaveUp && !part.SetAside && !part.OutOfReach && !OvHeldByThing(part))
			{
				cand.Add(part);
			}
		}
		if (cand.Count == 0)
		{
			// R2d: every root left is set aside - each gets its one more round now
			foreach (OvPart part in c.Parts)
			{
				if (part.Root && !part.Out && !part.GaveUp && !part.OutOfReach && !OvHeldByThing(part))
				{
					cand.Add(part);
				}
			}
		}
		// Z3 / Z4: the roots too high to hit from the floor get their rounds from a ladder - after every root in reach
		bool ladder = false;
		if (cand.Count == 0)
		{
			foreach (OvPart part in c.Parts)
			{
				if (part.Root && !part.Out && !part.GaveUp && part.OutOfReach && !OvHeldByThing(part))
				{
					cand.Add(part);
				}
			}
			ladder = cand.Count > 0;
		}
		bool pop = false;
		if (cand.Count == 0 && OvAnyHeldByThing(c))
		{
			_ai?.StopMoving();   // Z2: only roots on a thing another companion is clearing are left - it stands by
			return true;
		}
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
		// R1 (John: "work from one spot"): the nearest to where it stands first (whole metres, flat, from its feet to the
		// part's game box), the lowest first among those at the same distance. R2d: fewer failed rounds come first.
		Dictionary<OvPart, int> fromMe = new Dictionary<OvPart, int>();
		foreach (OvPart part in cand)
		{
			fromMe[part] = Mathf.FloorToInt(Flat(me, OvBoxNear(part, me)));
		}
		cand.Sort((OvPart a, OvPart b) =>
		{
			if (a.Rounds != b.Rounds)
			{
				return a.Rounds.CompareTo(b.Rounds);
			}
			int da = fromMe[a], db = fromMe[b];
			if (da != db)
			{
				return da.CompareTo(db);
			}
			return OvPartBottom(a).CompareTo(OvPartBottom(b));
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
		Vector3 near = OvBoxNear(pick, me);   // L6: the part's game box (a vein part's mesh can't be asked)
		Vector3 foot = new Vector3(near.x, GroundAt(near), near.z);
		float h = _character != null ? _character.GetHeight() : 1.8f;
		if (!ladder && !pick.RampTried && Flat(me, near) <= OvRampReach && foot.y >= me.y + OvRampRatio * h)
		{
			pick.RampTried = true;
			_rpPart = pick;
			OvRampBegin(foot, null, outOfTrench: false, $"the part \"{pick.C.name}\" is {foot.y - me.y:F1} m up");
			return true;
		}
		// R4: a way out near where it will work first (the E2 way, from that spot), when there's none within 15 m
		OvClStandFor(pick, out OvSpot q4, out bool here4);
		OvSpot ws = here4 ? OvNearest(me) : q4;
		if (ws != null && OvWayOutNeeded(ws))
		{
			pick.Holder = null;
			OvWayOutHere(ws);
			return true;
		}
		OvClBegin(pick, pop, ladder);
		return true;
	}

	// Z2: the root rests on a tree / log / stump / rock that isn't left (the Y6e count) - that thing comes first
	private bool OvHeldByThing(OvPart part)
	{
		GameObject t = part.HoldThing;
		// not one it just left (Y6e: it waits for one other piece of work - a round on a part is one), nor one it has no tool for
		return part.Root && t != null && t.activeInHierarchy && t.GetInstanceID() != _pitSkipId && !(_vp.PitLeft.TryGetValue(t.GetInstanceID(), out int left) && left >= 2) && FindBestTool(part.HoldMode) != null;
	}

	private bool OvAnyHeldByThing(OvClear c)
	{
		foreach (OvPart part in c.Parts)
		{
			if (!part.Out && !part.GaveUp && OvHeldByThing(part))
			{
				return true;
			}
		}
		return false;
	}

	// Z2: the nearest such thing it can take (not one another companion is on, not one it just left, with a tool for it),
	// handed to the Wood / Stone flow like the things in the pit (OvTake). True = taken.
	private bool OvHoldFirst(OvClear c)
	{
		Vector3 me = base.transform.position;
		GameObject best = null;
		OvPart bp = null;
		float bd = float.MaxValue;
		foreach (OvPart part in c.Parts)
		{
			if (part.Out || part.GaveUp || !OvHeldByThing(part))
			{
				continue;
			}
			GameObject t = part.HoldThing;
			int id = t.GetInstanceID();
			if (s_claimedTargets.Contains(id) && id != _claimedTargetId)
			{
				continue;
			}
			float d = Flat(me, t.transform.position);
			if (d < bd)
			{
				bd = d;
				best = t;
				bp = part;
			}
		}
		if (best == null)
		{
			return false;
		}
		OvRelease();
		return OvTake(best, bp.HoldMode, bp.HoldType, OvNearest(best.transform.position), canSkip: false, $"It holds the vein up (the part \"{bp.C.name}\" rests on it)") == 1;
	}

	// Z5: the parts whose game box takes in the root's rock (the game's own test: a part touching a held-up part is held
	// up through it) become roots, once each
	private void OvChainCut(OvClear c, OvPart root)
	{
		root.ChainCut = true;
		int made = 0;
		foreach (OvPart part in c.Parts)
		{
			if (part == root || part.Out || part.GaveUp || part.Chain || part.Root || part.C == null)
			{
				continue;
			}
			int n = Physics.OverlapBoxNonAlloc(part.Centre, part.Half, s_cBuf, Quaternion.identity, s_cMask);
			for (int j = 0; j < n; j++)
			{
				if (s_cBuf[j] == root.C)
				{
					part.Chain = true;
					part.ChainWhy = $"rests on \"{root.C.name}\", which it couldn't break";
					made++;
					break;
				}
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't break \"{(root.C != null ? root.C.name : "?")}\" of \"{_vp.Name}\" - {made} part(s) resting on it are next");
	}

	// R4: no real way out (R3) with its bottom within 15 m (flat) of this spot, no try within 15 m of it, nobody else
	// cutting one, and the wall outside it 1.5 x its height or more above the floor there
	private bool OvWayOutNeeded(OvSpot sp)
	{
		OvPlan p = _vp;
		if (p.ExitBy != null && p.ExitBy != this && p.ExitBy._vp == p && OvActive().Contains(p.ExitBy))
		{
			return false;
		}
		foreach (OvRamp r in p.Ramps)
		{
			if (OvRampOut(r) && Flat(r.Bottom, sp.P) <= OvWayNear)
			{
				return false;
			}
		}
		foreach (Vector3 t in p.ExitAt)
		{
			if (Flat(t, sp.P) <= OvWayNear)
			{
				return false;
			}
		}
		float h = _character != null ? _character.GetHeight() : 1.8f;
		return OvWallTop(sp) - GroundAt(sp.P) >= OvRampRatio * h;   // Y2
	}

	// R4: the way out cut from this spot (the E2 walk along the floor, then the ramp straight out; OvWayOutTick)
	private void OvWayOutHere(OvSpot sp, string head = null, string why = "a way out first")
	{
		OvPlan p = _vp;
		p.ExitBy = this;
		_pitSkipId = 0;   // Y6e: another piece of work started
		_woWhy = why;
		_woCombat = false;
		_woActive = true;
		_woOut = false;
		_woFloor = sp.P;
		_woFloor.y = GroundAt(sp.P);
		_woGoal = OvOutside(sp);
		_woGoal.y = OvWallTop(sp);   // Y2
		_moveTimer = 0f;
		_ai?.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"{head ?? $"No way out of the trench within {OvWayNear:F0} m of where it works"} - going to {MHR_Mover.Fmt(_woFloor)} (spot {sp.Idx + 1}) to cut one ({_woGoal.y - _woFloor.y:F1} m up)");
	}

	// the nearest point of a part's box (the game's box, as the support rule uses) to p
	private static Vector3 OvBoxNear(OvPart part, Vector3 p)
	{
		return new Bounds(part.Centre, part.Half * 2f).ClosestPoint(p);
	}

	// L2: the trench spot nearest the part (flat, to its box); R2c: after a failed round, the nearest one at least 3 m
	// from where that round started (none = the nearest)
	private OvSpot OvClSpotFor(OvPart part)
	{
		OvSpot best = null, any = null;
		float bd = float.MaxValue, ad = float.MaxValue;
		foreach (OvSpot q in _vp.Spots)
		{
			float d = Flat(q.P, OvBoxNear(part, q.P));
			if (d < ad)
			{
				ad = d;
				any = q;
			}
			if (part.HasStart && part.Rounds > 0 && Flat(q.P, part.LastStart) < OvClOtherAngle)
			{
				continue;
			}
			if (OvBoardTaken(q.P) != null)
			{
				continue;   // AA6 (a): another companion stands there or is going there - the next nearest
			}
			if (d < bd)
			{
				bd = d;
				best = q;
			}
		}
		return best ?? any;
	}

	// R1: where the round starts - where it stands when that is nearer (flat) to the part's box than the trench spot is
	// (R2c: and at least 3 m from where its last round started), else that trench spot
	private void OvClStandFor(OvPart part, out OvSpot q, out bool here)
	{
		Vector3 me = base.transform.position;
		q = OvClSpotFor(part);
		float dq = q != null ? Flat(q.P, OvBoxNear(part, q.P)) : float.MaxValue;
		float dm = Flat(me, OvBoxNear(part, me));
		here = dm < dq && !(part.HasStart && part.Rounds > 0 && Flat(me, part.LastStart) < OvClOtherAngle) && OvBoardTaken(me) == null;
	}

	private const float OvClAt = 0.8f;             // L2: on the spot it walks to (flat)

	private const int OvClCapMore = 2;             // L4: dig points = metres from the trench spot to the part + 2

	// L1 / L2: a round on the part starts - it goes to the trench spot nearest it
	private void OvClBegin(OvPart part, bool pop, bool ladder = false)
	{
		OvClStandFor(part, out OvSpot q, out bool here);
		_pitSkipId = 0;   // Y6e: another piece of work started
		_clFor = part;
		_clPop = pop;
		_clStand = here ? base.transform.position : q != null ? new Vector3(q.P.x, GroundAt(q.P), q.P.z) : base.transform.position;
		part.HasStart = true;
		part.LastStart = _clStand;
		Vector3 d = part.Centre - _clStand;
		d.y = 0f;
		_clDir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.zero;
		_clStep = 0;
		_clCap = Mathf.CeilToInt(d.magnitude) + OvClCapMore;
		_clWalk = true;
		_moveTimer = 0f;
		_ai?.StopMoving();
		OvLnBegin(ladder);
		if (CompanionsPlugin.LogNormal) LogInfo($"Clearing \"{_vp.Name}\": {(ladder ? $"a part too high to hit from the floor ({OvPartBottom(part) - GroundAt(part.Centre):F1} m up) - from a ladder" : pop ? "no part holds it up any more but some still stand - the lowest" : "a part that holds it up")}: \"{part.C.name}\" at {MHR_Mover.Fmt(part.Centre)}, bottom {OvPartBottom(part):F1}{(part.Why != null ? " (" + part.Why + ")" : "")}, round {part.Rounds + 1} of {(part.SetAside ? OvPartRoundsMax : OvPartRounds)} - from {(here ? "where it stands" : $"trench spot {(q != null ? q.Idx + 1 : 0)}")} at {MHR_Mover.Fmt(_clStand)}, {d.magnitude:F1} m from its centre");
	}

	private void OvClOff()
	{
		_clFor = null;
		_clWalk = false;
		OvLnOff();
		if (_dgClear)
		{
			OvDigOff();
			_dgClear = false;
		}
	}

	// N1-N5: one frame of the round (from OvClearTick while _clFor is set; only this ticks its dig points)
	private void OvClRound(OvClear c)
	{
		OvPart part = _clFor;
		if (part.C == null || !part.C.gameObject.activeSelf)
		{
			// N5: the part it was going for broke (its swings, another companion's, or the pop): mined, the drops around
			// it (the pickup scans 8 m, so the parts it broke on the way in too), then the chooser
			if (part.C != null)
			{
				c.ById.Remove(part.C.GetInstanceID());
			}
			c.Mined++;
			if (_clPop)
			{
				c.PopFails = 0;
			}
			if (part.Holder == this)
			{
				part.Holder = null;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Mined the part \"{(part.C != null ? part.C.name : "?")}\" of \"{_vp.Name}\" - picking up, then looking at what still holds it up");
			OvClOff();
			OvPartsLook(c, true);
			OvPartDrops(part.Centre);
			return;
		}
		// Z4: a round from a ladder - planning, wood, building and the climb, then the part swing below
		if (_clLadder && OvLnTick(c, part))
		{
			return;
		}
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (_dgActive && _dgClear)
		{
			// N4: the point's 30 s clock - the ground there going down or a part of the vein breaking starts it again
			float g = GroundAt(_dgPoint);
			if (g < _clBestG - OvDigGain || c.Alive < _clAlive)
			{
				_clBestG = Mathf.Min(_clBestG, g);
				_clIdle = 0f;
			}
			else
			{
				_clIdle += dt;
			}
			_clAlive = c.Alive;
			if (_clIdle >= OvSpotIdle)
			{
				OvClFail($"{OvSpotIdle:F0} s at {MHR_Mover.Fmt(_dgPoint)} without the ground going down or a part breaking");
				return;
			}
			OvDigTick();
			if (_dgActive)
			{
				return;
			}
			_dgClear = false;
			if (_dgPart)
			{
				OvClFail($"{OvNoProgSwings} swings at the part from {MHR_Mover.Fmt(me)} landed on nothing");
				return;
			}
			if (_dgLimit)
			{
				// R2 (John: "Any parts that cannot be cleared of dirt from the bottom should be mined out"): the dirt in
				// front can't go lower and is above a step - the part itself from here when it's within reach (R2a), else
				// the round ends (R2b)
				float lg = GroundAt(_dgPoint);
				Vector3 bp = OvBoxNear(part, me);
				float reach = GetAttackRange();
				if (Flat(me, bp) <= reach)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"The dirt toward \"{part.C.name}\" at {MHR_Mover.Fmt(_dgPoint)} is at the game's 8 m dig limit, {lg - me.y:F1} m above its feet - mining the part from here");
					OvDigBegin(bp, null, ramp: false);
					_dgClear = true;
					_dgPart = true;
					_dgGoalY = me.y;
					_clBestG = GroundAt(bp);
					_clIdle = 0f;
					_clAlive = c.Alive;
					return;
				}
				OvClFail($"the dirt toward it at {MHR_Mover.Fmt(_dgPoint)} is at the game's 8 m dig limit, {lg - me.y:F1} m above its feet, and the part is out of reach ({Flat(me, bp):F1} m, reach {reach:F1} m)");
				return;
			}
			if (!_dgReached)
			{
				OvClFail($"{OvNoProgSwings} swings at {MHR_Mover.Fmt(_dgPoint)} did nothing (or it couldn't get to it)");
				return;
			}
			_clStand = new Vector3(_dgPoint.x, GroundAt(_dgPoint), _dgPoint.z);
			_clWalk = true;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogDetailed) Log($"Point {_clStep} of {_clCap} toward \"{part.C.name}\" is clear - stepping onto it");
			return;
		}
		if (_clWalk)
		{
			float flat = Flat(me, _clStand);
			if (flat > OvClAt)
			{
				if (WalkTimer(dt, _clStand) && _moveTimer > 10f)
				{
					OvClFail($"couldn't get to {MHR_Mover.Fmt(_clStand)} in {10f:F0} s ({flat:F1} m away)");
					return;
				}
				WalkGo(_clStand, 0.3f, flat > 8f, dt);
				return;
			}
			_clWalk = false;
			_ai?.StopMoving();
		}
		// N1: every point started counts toward the cap
		if (_clStep >= _clCap || _clDir == Vector3.zero)
		{
			OvClFail($"{_clStep} point(s) toward it and it's still standing");
			return;
		}
		// N2: the trench's dig swing at a point 1 m further in - the game's swing hits what is in front, dirt or rock
		Vector3 pt = _clStand + _clDir * 1f;
		pt.y = GroundAt(pt);
		_clStep++;
		OvDigBegin(pt, null, ramp: false);
		_dgClear = true;
		_dgGoalY = me.y;
		_clBestG = pt.y;
		_clIdle = 0f;
		_clAlive = c.Alive;
		if (CompanionsPlugin.LogDetailed) Log($"Toward \"{part.C.name}\": swinging at {MHR_Mover.Fmt(pt)} (ground {pt.y:F2}, its feet {me.y:F2}), point {_clStep} of {_clCap}");
	}

	// the round on _clFor ended without it breaking: a round (2 = left), and the 2-tries stop rule
	private void OvClFail(string why)
	{
		OvPart root = _clFor;
		OvClOff();
		if (root == null || _vp == null)
		{
			return;
		}
		if (root.Holder == this)
		{
			root.Holder = null;
		}
		root.RampTried = false;
		OvClear c = OvC();
		root.Rounds++;
		if (_clPop)
		{
			c.PopFails++;
		}
		// R2d (John: "clear what else is around them and come back to it later"): 2 failed rounds set it aside, the third
		// leaves it
		if (root.Rounds >= OvPartRoundsMax)
		{
			root.GaveUp = true;
		}
		else if (root.Rounds >= OvPartRounds)
		{
			root.SetAside = true;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{(root.C != null ? root.C.name : "?")}\" of \"{_vp.Name}\" didn't break: {why} (round {root.Rounds} of {(root.SetAside ? OvPartRoundsMax : OvPartRounds)}{(root.GaveUp ? ", left" : root.SetAside && root.Rounds == OvPartRounds ? ", set aside until the rest are done" : "")})");
		if (c.PopFails >= OvPopFails && !c.Stopped)
		{
			// P7: John's rule - two tries with nothing broken: stop and wait, with everything we need to find out why
			c.Stopped = true;
			c.Done = true;
			OvClearSay(c);
		}
		OvPartsLook(c, true);
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
				if (CompanionsPlugin.LogNormal) LogInfo($"  part \"{part.C.name}\": centre {MHR_Mover.Fmt(part.Centre)}, size {part.Half.x * 2f:F1} x {part.Half.y * 2f:F1} x {part.Half.z * 2f:F1}, bottom {OvPartBottom(part):F1}, ground {GroundAt(part.Centre):F1}, floor {OvFloor(part.Centre):F1}, {(part.Out ? "buried out of reach" : part.Root ? "holds it up (" + part.Why + ")" : "hanging on other parts")}{(part.OutOfReach && !part.Out ? $", out of reach ({OvPartBottom(part) - GroundAt(part.Centre):F1} m up)" : "")}, rounds {part.Rounds}{(part.GaveUp ? ", given up" : "")}");
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

	// N5: the drops are picked up the existing way, then back to the chooser (OvResume)
	private void OvPartDrops(Vector3 at)
	{
		_lastDestroyPos = at;
		_currentDrop = null;
		_dropTimer = 0f;
		_dropScanDelayTimer = 0.5f;
		_dropsPickedUp = 0;
		_state = HarvestState.CollectingDrops;
		_ai?.StopMoving();
	}
}
