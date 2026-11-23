using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 build 2 (John's approved list reviews/1.5.98-clearing-pop-build-list.md, revised 2026-11-18 01:26Z): clearing and
// the pop, by the game's own support rule (MineRock5.UpdateSupport / GetSupport: a part's box touching the terrain, a solid
// thing, a lower breakable thing or a supported part of the same vein is held up; CheckSupport runs after a part breaks).
// 1.5.98 GG2 / GG3 (John, test 15, 2026-11-19): after the trench and the things in the pit -
//   1. the dirt step: ONE dirt list per vein (in the shared plan) - every point of a 0.5 m grid under each standing part's
//      own game box where the ground is higher than that part's bottom (that ground is what the game's support rule
//      sees). Each companion takes the nearest point no one has, stands on the pit ground outside every part's box, feet
//      at most 0.7 m above the point, within its swing, and digs it down to 0.3 m under the part's bottom. 3 swings that
//      don't lower the ground there: the point waits 1 min. A point too high to swing at from anywhere near: steps are cut
//      toward a place to stand (never under a part). No ore is mined while the dirt list has a point.
//   2. the ore step (the dirt list is empty): the lowest standing part is mined from the pit ground beside it (a break
//      makes the game check the rest); a part too high to hit from the ground: from a ladder (hammer from its bag).
//   3. a part that didn't break waits 1 min (never given up); no hammer / no ladder spot doesn't count against it.
//      "Done" = no dirt left and every part gone or buried under the game's 8 m dig limit. Every point and part left
//      having failed since the last thing that broke or went down = stuck: they stop and say why, part by part.
public partial class HarvestController
{
	private sealed class OvPart
	{
		public MineRock5 Rock;

		public Collider C;

		public Vector3 Centre;

		public Vector3 Half;

		public bool Out;          // wholly under the lowest the ground can go: never uncovered, left

		public bool Root;         // M4: held straight up by something that isn't this vein (the dirt, a solid thing, a lower breakable thing)

		public bool Ground;       // M4: its box touches the terrain (the game's own overlap)

		public bool OnWorld;      // X2: its box touches an indestructible world object (a burial chamber): mined first

		public readonly List<OvPart> Links = new List<OvPart>();   // M4: the parts of the same rock its box touches

		public bool Held;         // M4: held up by the game's rule (3 passes, as MineRock5.UpdateSupport)

		public bool Sim;          // M4: the what-if pass

		public string Why;

		public float CoolAt = -1f;   // SV7 (replaces GG3c's 1 min wait): when it was set aside; -1 = not set aside

		public bool CoolOnce;        // SV7: came back once because no other part was free

		public float[] CoolGround;   // SV7: with no pit map, the ground round it when it was set aside

		public float FailAt = -1f;   // GG3d: when a try last failed (the stuck rule); -1 = never

		public bool Wait;         // GG3a: waiting for a hammer / a ladder spot - doesn't count toward stuck

		public string CoolWhy;

		public bool OutOfReach;   // Z3: too high to hit from the pit ground beside it (the last look)

		public bool LadderWait;   // S3: its wait is only "it needs a ladder" (the last step's ladder pass may take it)

		public GameObject HoldThing;   // Z2: the tree / log / stump / rock it rests on (the Wood or Stone job takes it)

		public int HoldMode;

		public string HoldType;

		public bool LadderTried;  // Z4: one try at building a ladder for it

		public int LadderTrees;   // Z4: trees taken for its ladder's wood (at most 3)

		public HarvestController Holder;

		public HarvestController Holder2;  // F9: a second companion, from more than a quarter turn round it

		public bool IsRock;       // J2: a piece of a plain rock (not this vein) that a part of the vein rests on

		public bool LadderGaveUp; // V3: a ladder round on this rock piece ended without it breaking
	}

	// GG2: one point of the dirt list
	private sealed class OvDirt
	{
		public long Key;          // its grid cell (0.5 m)

		public Vector3 P;         // x, z of the cell; y = the ground there at the last look

		public float Goal;        // 0.3 m under the part's bottom (not under the game's dig limit)

		public OvPart Part;

		public HarvestController Holder;

		public HarvestController Holder2;   // F9: a second companion, from more than a quarter turn round it
	}

	private const float OvPartsEvery = 2f;         // P2: the parts are looked at every 2 s (and after a break)

	private const float OvCool = 60f;              // GG2 / GG3: a point or part that failed waits 1 min

	private const float OvDirtGrid = 0.5f;         // GG2: the dirt grid under each part's box

	private const float OvDirtUnder = 0.3f;        // GG2: dug to 0.3 m under the part's bottom

	private const float OvDirtEvery = 10f;         // GG2: the dirt list is made again every 10 s (and right after a point is done)

	private const float OvStandAt = 0.5f;
	private const float OvPlaceUpDown = 0.7f;
	private const float OvPlaceOff = 1.0f;         // S3 tune (test of ea18287: 190 "off its place" at 0.5 m - a swing nudges it that far): off = more than 1 m (flat)      // S3: on its place = within 0.7 m up or down of it (or of the ground there now)          // GG2: at its place to stand = within 0.5 m of it (flat)

	private OvPart _rpPart;

	// the round on one part (ore step): the part, where it stands, walking there
	private OvPart _clFor;

	private Vector3 _clStand;

	private bool _clWalk;

	private string _clPopWhy;      // M4: why the last step picked this part (for the log)

	private Vector3 _clAim;        // M1: where the game's swing meets the part's rock from its place

	private Vector3 _standAim;     // M1: OvStandFor's aim for a part





	// GG2: the dirt point it digs, where it stands, walking there, one way up per point
	private OvDirt _drSpot;

	private Vector3 _drStand;

	private bool _drWalk;

	private bool _drForThing;     // P2: _drSpot is the ground at a thing's base (_rpThing), not a point of the dirt list


	private float _clNextPick;    // GG2: standing by - the lists are looked at again once a second

	// shared per plan (kept in a side table so OvPlan stays as it is)
	private sealed class OvClear
	{
		public readonly List<OvPart> Parts = new List<OvPart>();

		public readonly Dictionary<int, OvPart> ById = new Dictionary<int, OvPart>();

		// J2 (John 11-22 18:41Z: "clear the stone so that is either not held up by the ground or it is not holding up the
		// ore vein"): the plain-rock pieces a part of the vein rests on (the game's rule, lower than the vein's centre),
		// made again at each parts look; RockById keeps each piece's set-aside and holders between looks
		public readonly List<OvPart> RockPieces = new List<OvPart>();

		public readonly Dictionary<int, OvPart> RockById = new Dictionary<int, OvPart>();

		// T3: plain-rock pieces found in the way of a part's swing (their collider ids) - on the rock list until they are gone
		public readonly HashSet<int> InWay = new HashSet<int>();

		// V4 (John 11-23 03:10Z: "If that part is not reachable in any way, even from building a ladder ... they should just
		// mine out the attached section of the ore to break it"): the vein parts (their collider ids) whose box touches a
		// rock piece that can't be reached - mined first by the last step
		public readonly HashSet<int> V4 = new HashSet<int>();

		public bool RockBack;   // V5: the set-aside rock pieces came back once when the dirt list emptied (once per vein)

		// F10 (silver): the vein's middle line - the middle of each 1 m slice along its long direction, joined slice to slice
		public readonly List<Vector3> Mid = new List<Vector3>();

		public int MidParts = -1;   // how many parts it was made from (made again when that changes)

		// F2 (John 11-21 23:30Z / 11-22 04:24Z / 05:43Z): the pit's one stone dump all companions use after the pop
		public bool StoneDumpHas;

		public Vector3 StoneDump;

		public HarvestController StoneTaker;   // the one companion taking stone off the dump for a pile (one at a time)

		public float NoPileAt = -1f;           // a stone pile couldn't be placed then - none tried until the pit map changes

		public float At = -100f;

		public int Alive = -1;

		public int Mined;

		public int Popped;

		// W0 (test 26, John 2026-11-20: "2 or more when there are no remaining large groups of parts still attached
		// together. Single standing parts and buried parts should not be counted"): the pop was seen
		public bool PopSeen;

		// F2: the pit's shared list of loose ore, stone and wood (by item instance id), looked at again every 5 s
		public readonly Dictionary<int, OvLoose> Loose = new Dictionary<int, OvLoose>();

		public float LooseAt = -100f;

		public float LooseChangedAt = -1f;   // R1: the last time an item came onto the list or left it

		public bool TopBack;     // T2: the dirt on top of the vein is back on the list (once, at the stop)
		public int TopSkipped;   // T2: dirt points on top of the vein left off the last dirt scan

		public HarvestController DumpCarryBy;   // S5: who carried the ore dump to storage last, and when it started
		public float DumpCarryAt = -1f;

		// P1 step 2: per dirt point (its key), the squares lowered toward it from its nearest deepened trench spot
		public readonly Dictionary<long, List<OvLowSq>> LowLines = new Dictionary<long, List<OvLowSq>>();

		public bool Done;

		public bool Stopped;

		public float StoppedAt;   // SV3: when the stuck stop came

		public bool Said;

		// GG2: the dirt list and its points' cooldowns (kept across looks by grid cell)
		public readonly List<OvDirt> Dirt = new List<OvDirt>();

		public float DirtAt = -100f;

		public readonly Dictionary<long, float> DirtCool = new Dictionary<long, float>();

		public readonly Dictionary<long, float> DirtFail = new Dictionary<long, float>();

		public readonly Dictionary<long, string> DirtWhy = new Dictionary<long, string>();

		// E2 (silver): each point's tried places (a place it repositioned away from) - cleared when it comes back after a change
		public readonly Dictionary<long, List<Vector3>> DirtTried = new Dictionary<long, List<Vector3>>();

		// E3 (silver): each point's places it already tried to dig deeper (at most 3 per point) - cleared with the tried list
		public readonly Dictionary<long, List<Vector3>> DirtDeep = new Dictionary<long, List<Vector3>>();

		// E4: points that came back once because no other dirt point was free (rule 8b) - a second set-aside with nothing
		// changed waits for a change (8a)
		public readonly HashSet<long> DirtOnce = new HashSet<long>();

		// E4 (no pit map): the ground within 2 m of a set-aside point when it was set aside (a 5 x 5 grid, 1 m apart)
		public readonly Dictionary<long, float[]> DirtGround = new Dictionary<long, float[]>();

		// GG3d: the last time something broke or the ground at a dirt point went down
		public float ProgAt;

		// P5: low parts - their box goes into ground the game won't dig under their bottom; mined in the dirt work
		public readonly List<OvPart> Low = new List<OvPart>();

		public int HeldCount;     // M4: parts held up by the game's rule at the last look

		public readonly HashSet<HarvestController> HammerSaid = new HashSet<HarvestController>();
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
			c = new OvClear { ProgAt = Time.time };
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
		c.RockPieces.Clear();   // J2
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
					if (c.ById.ContainsKey(id))
					{
						c.ProgAt = Time.time;    // GG3: a part broke - progress (the stuck rule)
						c.DirtAt = -100f;        // GG2: the dirt list is looked at again
					}
					if (c.ById.TryGetValue(id, out OvPart gone) && gone.Holder == null && gone.Holder2 == null)
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
				part.Ground = false;
				part.OnWorld = false;
				part.Links.Clear();
				part.Why = null;
				part.HoldThing = null;
				c.Parts.Add(part);
				alive++;
			}
			// M4: every part, the buried ones too (the game checks them all)
			for (int i = first; i < c.Parts.Count; i++)
			{
				OvPartRoot(c.Parts[i], rock, origin, c);
			}
		}
		c.Popped += popped;
		c.Alive = alive;
		c.HeldCount = OvSupport(c, null);
		foreach (OvPart part in c.Parts)
		{
			part.Held = part.Sim;
		}
		// W0: the game dropped 2 or more parts at once (since the last look, none of them being mined) and no group of
		// 3 or more linked parts above the floor is left standing = the pop
		if (!c.PopSeen && popped >= OvPopDrop)
		{
			int big = OvBiggestGroup(c);
			if (big < OvPopGroup)
			{
				c.PopSeen = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{_vp.Name}\" popped: {popped} part(s) fell at once and no group of {OvPopGroup} or more parts is left standing ({c.Parts.Count} left, the biggest group {big})");
			}
			else if (CompanionsPlugin.LogNormal)
			{
				LogInfo($"{popped} part(s) of \"{_vp.Name}\" fell at once, not the pop: a group of {big} linked parts still stands");
			}
		}
	}

	private const int OvPopDrop = 2;    // W0: parts the game drops at once
	private const int OvPopGroup = 3;   // W0: a "large group" of linked parts (their boxes touch) still standing

	// W0: the most parts above the floor (not Out) linked together by touching boxes (the game's support links)
	private static int OvBiggestGroup(OvClear c)
	{
		HashSet<OvPart> live = new HashSet<OvPart>();
		foreach (OvPart part in c.Parts)
		{
			if (!part.Out)
			{
				live.Add(part);
			}
		}
		HashSet<OvPart> seen = new HashSet<OvPart>();
		Stack<OvPart> todo = new Stack<OvPart>();
		int best = 0;
		foreach (OvPart start in live)
		{
			if (!seen.Add(start))
			{
				continue;
			}
			int n = 0;
			todo.Push(start);
			while (todo.Count > 0)
			{
				OvPart part = todo.Pop();
				n++;
				foreach (OvPart l in part.Links)
				{
					if (live.Contains(l) && seen.Add(l))
					{
						todo.Push(l);
					}
				}
			}
			best = Mathf.Max(best, n);
		}
		return best;
	}

	// M4: the game's MineRock5.UpdateSupport over the vein's parts as they are now: 3 passes in the game's order; a part is
	// held when it is held straight up (Root) or its box touches a part of the same rock held earlier. `without` = as if
	// that part had broken (its collider gone, as the game sees it). The result is in each part's Sim; returns how many
	// are held.
	private static int OvSupport(OvClear c, OvPart without)
	{
		foreach (OvPart part in c.Parts)
		{
			part.Sim = false;
		}
		for (int pass = 0; pass < 3; pass++)
		{
			foreach (OvPart part in c.Parts)
			{
				if (part.Sim || part == without)
				{
					continue;
				}
				if (part.Root)
				{
					part.Sim = true;
					continue;
				}
				foreach (OvPart l in part.Links)
				{
					if (l != without && l.Sim)
					{
						part.Sim = true;
						break;
					}
				}
			}
		}
		int n = 0;
		foreach (OvPart part in c.Parts)
		{
			if (part.Sim)
			{
				n++;
			}
		}
		return n;
	}

	// the game's support test for one part (MineRock5.UpdateSupport / GetSupport, same box and layer mask): what its box
	// touches. M4: a part of the same rock is a link (the game asks whether THAT part is held - no "rests on a part buried
	// out of reach" rule, the game has none); the terrain, a solid thing that can't be broken, or a breakable thing lower
	// than the rock's centre holds it straight up (Root).
	private void OvPartRoot(OvPart part, MineRock5 rock, Vector3 origin, OvClear c)
	{
		int n = Physics.OverlapBoxNonAlloc(part.Centre, part.Half, s_cBuf, Quaternion.identity, s_cMask);
		for (int j = 0; j < n; j++)
		{
			Collider o = s_cBuf[j];
			if (o == null || o == part.C || o.attachedRigidbody != null || o.isTrigger)
			{
				continue;
			}
			if (o.gameObject.layer == s_cTerrain)
			{
				part.Ground = true;
				if (!part.Root)
				{
					part.Root = true;
					part.Why = "touches the dirt";
				}
				continue;
			}
			MineRock5 m = o.GetComponentInParent<MineRock5>();
			if (m == rock)
			{
				if (c.ById.TryGetValue(o.GetInstanceID(), out OvPart other) && !part.Links.Contains(other))
				{
					part.Links.Add(other);
				}
				continue;
			}
			if (!part.OnWorld && OvIsWorldSolid(o))
			{
				part.OnWorld = true;   // X2: held by a thing that can never go - this part has to be mined
			}
			// J2: every plain-rock piece this part rests on by the game's rule (a breakable thing lower than the vein's
			// centre), whatever else holds the part
			if (m != null && !OvSameOre(_vp, m) && o.transform.position.y < origin.y && o.GetComponentInParent<IDestructible>() != null)
			{
				OvRockPieceNote(c, o, m);
			}
			if (part.Root)
			{
				continue;
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

	// J2: a plain-rock piece goes on this look's list once (its set-aside and holders kept from the last look)
	private void OvRockPieceNote(OvClear c, Collider o, MineRock5 m)
	{
		int id = o.GetInstanceID();
		if (!c.RockById.TryGetValue(id, out OvPart rp))
		{
			rp = new OvPart { Rock = m, C = o, IsRock = true };
			c.RockById[id] = rp;
		}
		if (c.RockPieces.Contains(rp))
		{
			return;
		}
		Bounds b = o.bounds;
		rp.Centre = b.center;
		rp.Half = b.extents;
		rp.Why = "a vein part rests on it";
		c.RockPieces.Add(rp);
	}

	// J2: the nearest rock piece it can work (not set aside, not two others on it) gets a round of the part's own kind
	// (OvClRound: its place, the swing at the piece's own rock, 3 swings without work end it, set aside until something
	// within 2 m changes). True = a round started.
	private bool OvRockFirst(OvClear c)
	{
		// T3: the pieces found in the way of a swing stay on the list until they are gone
		if (c.InWay.Count > 0)
		{
			List<int> dead = new List<int>();
			foreach (int id in c.InWay)
			{
				if (!c.RockById.TryGetValue(id, out OvPart wp) || wp.C == null || !wp.C.gameObject.activeInHierarchy)
				{
					dead.Add(id);
				}
				else if (!c.RockPieces.Contains(wp))
				{
					c.RockPieces.Add(wp);
				}
			}
			foreach (int id in dead)
			{
				c.InWay.Remove(id);
			}
		}
		if (c.RockPieces.Count == 0)
		{
			return false;
		}
		// V5 (John 11-23 04:37Z): once the dirt list is empty, each set-aside rock piece comes back once (once per vein)
		if (!c.RockBack && OvDirtDone(c))
		{
			c.RockBack = true;
			foreach (OvPart rp in c.RockById.Values)
			{
				if (rp.CoolAt < 0f)
				{
					continue;
				}
				rp.CoolAt = -1f;
				rp.CoolGround = null;
				rp.CoolOnce = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"The rock piece \"{(rp.C != null ? rp.C.name : "?")}\" comes back once: the dirt is done (V5)");
			}
		}
		Vector3 me = base.transform.position;
		List<OvPart> pick = new List<OvPart>();
		foreach (OvPart rp in c.RockPieces)
		{
			if (rp.C == null || !rp.C.gameObject.activeInHierarchy || OvPartCooling(rp) || OvPartHeldByOther(rp))
			{
				continue;
			}
			pick.Add(rp);
		}
		pick.Sort((OvPart a, OvPart b) => Flat(me, a.Centre).CompareTo(Flat(me, b.Centre)));
		int tries = 0;
		foreach (OvPart rp in pick)
		{
			if (tries++ >= 6)
			{
				break;   // the rest next look
			}
			int r = OvStandForPart(rp, out Vector3 stand, out string why);
			if (r != 0)
			{
				// V3 / V4: once the dirt and the low parts are done, and not because other companions have the places
				bool late = OvDirtDone(c) && !OvLowLeft(c) && !_sfpBusy;
				if (late && !OvIsLow(rp) && !rp.LadderTried && !rp.LadderGaveUp)
				{
					// V3: no place to hit it from (V2 included) - a ladder (two stacked when one is too low)
					if (CompanionsPlugin.LogNormal) LogInfo($"No place to hit the rock piece \"{rp.C.name}\" from ({why}) - from a ladder (V3)");
					OvClBegin(rp, stand, ladder: true);
					return true;
				}
				if (late)
				{
					OvV4Mark(c, rp, why);   // V4: not from a ladder either
				}
				OvPartCool(rp, why, wait: false);
				continue;
			}
			OvSpot ws = OvClSpotFor(rp);
			if (ws != null && OvWayOutNeeded(ws))
			{
				OvWayOutHere(ws);
				return true;
			}
			OvClBegin(rp, stand, ladder: false);
			return true;
		}
		return false;
	}

	// V4: every vein part (not buried) whose box touches the rock piece goes on the vein's V4 list (said once each)
	private void OvV4Mark(OvClear c, OvPart rp, string why)
	{
		Bounds rb = new Bounds(rp.Centre, rp.Half * 2f);
		foreach (OvPart p in c.Parts)
		{
			if (p.IsRock || p.Out || p.C == null || !p.C.gameObject.activeSelf || !new Bounds(p.Centre, p.Half * 2f).Intersects(rb))
			{
				continue;
			}
			if (c.V4.Add(p.C.GetInstanceID()) && CompanionsPlugin.LogNormal)
			{
				LogInfo($"The part \"{p.C.name}\" is attached to the rock piece \"{rp.C.name}\" that can't be reached ({why}{(rp.LadderTried || rp.LadderGaveUp ? "; its ladder was tried" : "")}) - mined first (V4)");
			}
		}
	}

	private static bool OvV4Has(OvClear c, OvPart p)
	{
		return p.C != null && c.V4.Contains(p.C.GetInstanceID());
	}

	private string _clRockWhy;   // J2: why a rock piece's round stopped on silver (its swing would meet / met the vein)

	// F9 (John 11-21 23:13Z / 23:30Z): at most two on a part - held by others only when both its places are other
	// companions working it
	private bool OvPartHeldByOther(OvPart part)
	{
		return OvPartSlotBusy(part, 1) && OvPartSlotBusy(part, 2);
	}

	// one of the part's two places (F9) is another companion working it (a stale holder is cleared)
	private bool OvPartSlotBusy(OvPart part, int slot)
	{
		HarvestController h = slot == 1 ? part.Holder : part.Holder2;
		if (h == null || h == this)
		{
			return false;
		}
		if (!h.isActiveAndEnabled || h._vp != _vp || (h._rpPart != part && h._clFor != part))
		{
			if (slot == 1)
			{
				part.Holder = null;
			}
			else
			{
				part.Holder2 = null;
			}
			return false;
		}
		// SV1 (V1, John 11-21: "held by another" is free to take over if no one is actually at it working it)
		if (h._clFor == part && !h.OvHolderWorking(h._clWalk))
		{
			return false;
		}
		return true;
	}

	// F9: the other companion working the part (null = none)
	private HarvestController OvPartOther(OvPart part)
	{
		return OvPartSlotBusy(part, 1) ? part.Holder : OvPartSlotBusy(part, 2) ? part.Holder2 : null;
	}

	// F9: takes a place on the part - the first unless another companion is working it there (then the second)
	private void OvPartClaim(OvPart part)
	{
		if (part.Holder == this || part.Holder2 == this)
		{
			return;
		}
		if (!OvPartSlotBusy(part, 1))
		{
			part.Holder = this;
		}
		else
		{
			part.Holder2 = this;
		}
	}

	// F9: lets go of its place on the part
	private void OvPartUnclaim(OvPart part)
	{
		if (part == null)
		{
			return;
		}
		if (part.Holder == this)
		{
			part.Holder = null;
		}
		if (part.Holder2 == this)
		{
			part.Holder2 = null;
		}
	}

	// SV5: why it stands by, said once and again only when the reason changes (cleared when it starts work)
	private string _sbWhy;

	private void OvStandBy(string why)
	{
		if (why == _sbWhy)
		{
			return;
		}
		_sbWhy = why;
		if (CompanionsPlugin.LogNormal) LogInfo($"Stands by on \"{_vp?.Name}\": {why}");
	}

	// SV5: what another companion is doing with what it holds
	private string OvHolderDoing(bool walking)
	{
		if (_dgActive || (_humanoid != null && _humanoid.InAttack()))
		{
			return "swinging at it";
		}
		return walking ? "walking to it and getting closer" : "at its place";
	}

	private string OvName()
	{
		return _character?.m_name ?? "another companion";
	}

	// SV1: a holder is working its point or part while it swings (a dig or an attack under way), while it is at its place
	// (not walking), or while it walks there and the mover doesn't call the walk stuck (MHR_Mover.Stuck: gave up, or on an
	// unstuck step). A dead, gone or switched-off holder isn't working. The one that takes it over finds its own place by
	// every current rule; the old holder sees it isn't the holder any more on its next tick and lets it go.
	private bool OvHolderWorking(bool walking)
	{
		if (_character == null || _character.IsDead() || !isActiveAndEnabled)
		{
			return false;
		}
		if (_dgActive || (_humanoid != null && _humanoid.InAttack()) || !walking)
		{
			return true;
		}
		return _ai == null || !MHR_Mover.Stuck(_ai);
	}

	private float OvPartBottom(OvPart part)
	{
		return part.Centre.y - part.Half.y;
	}

	// the clearing chooser (OvDoneTick, after the trench and its way out). True = busy (or standing by), false = done.
	private bool OvClearTick()
	{
		OvClear c = OvC();
		// SV3 (log 07ee23fb: the stop was for good, so they stood there while John dug the vein free): a stopped vein
		// starts again when any square of its pit map changed after the stop - a dig by anyone (the player too), a part
		// hit or gone. No clock.
		if (c.Stopped && _vp.Map != null && _vp.Map.LastChange > c.StoppedAt)
		{
			OvStopUndo(c);
		}
		if (c.Done)
		{
			return false;
		}
		OvPartsLook(c, false);
		if (OvPopped(c))
		{
			OvPitOpen();   // F1 / U6: from the pop, the pit is open for its stone and wood piles
		}
		if (_lsItem != null)
		{
			OvLooseTick(c);   // F2: the loose item it is going for
			return true;
		}
		// R3c.2 (log 7f376ab0: zero pickups after the pop): once the pop is seen, a loose item it can take comes before
		// leftover parts, dirt and the part round - between swings only, and without standing by for other companions' items
		if (OvPopped(c) && _humanoid != null && !_humanoid.InAttack() && OvLooseTick(c, standBy: false))
		{
			return true;
		}
		if (_clFor != null)
		{
			// FF1: between swings of the round, the pit list first
			if (OvPitBetween())
			{
				return true;
			}
			OvClRound(c);
			return true;
		}
		if (_drSpot != null)
		{
			if (OvPitBetween())
			{
				return true;
			}
			OvDrTick(c);
			return true;
		}
		if (_lwSq != null)
		{
			if (OvPitBetween())
			{
				return true;
			}
			OvLowTick(c);   // P1 step 2
			return true;
		}
		// Y1 / Y6a: a way out first, then the things in the pit
		if (OvFirst())
		{
			return true;
		}
		// Z2: a tree, log, stump or rock a part rests on is cleared first, the Wood / Stone job's way
		if (OvHoldFirst(c))
		{
			return true;
		}
		// J2: the plain-rock pieces a part of the vein rests on, before the dirt and the parts
		if (Time.time >= _clNextPick && OvRockFirst(c))
		{
			return true;
		}
		// X2: a part held by an indestructible world object is mined first (nothing else can free the vein from it)
		if (Time.time >= _clNextPick && OvWorldFirst(c))
		{
			return true;
		}
		float now = Time.time;
		if (now < _clNextPick)
		{
			return true;   // standing by: looked again once a second
		}
		OvDirtScan(c, false);
		// D1 (test 23): after a pop the vein is done once nothing standing can still be worked - F2: and nothing loose in
		// the pit is left that it can take
		if (OvPopDone(c))
		{
			if (OvLooseTick(c))
			{
				if (_lsItem == null)
				{
					_clNextPick = Time.time + 1f;   // standing by: looked again once a second
				}
				return true;
			}
			c.Done = true;
			OvClearSay(c);
			return false;
		}
		if (c.Dirt.Count > 0 || OvLowLeft(c))
		{
			return OvDrChoose(c);
		}
		return OvOreChoose(c);
	}

	// ---------- GG2: the dirt list ----------
	private static long OvDirtKey(float x, float z)
	{
		long ix = Mathf.RoundToInt(x / OvDirtGrid), iz = Mathf.RoundToInt(z / OvDirtGrid);
		return (ix << 32) ^ (iz & 0xffffffffL);
	}

	// the vein's dirt list: made by whichever companion finds it due (every 10 s, or right after a point is done or a
	// part breaks), read by the others
	private void OvDirtScan(OvClear c, bool force)
	{
		float now = Time.time;
		if (!force && now - c.DirtAt < OvDirtEvery)
		{
			return;
		}
		c.DirtAt = now;
		Dictionary<long, HarvestController> held = new Dictionary<long, HarvestController>();
		Dictionary<long, HarvestController> held2 = new Dictionary<long, HarvestController>();   // F9
		foreach (OvDirt d in c.Dirt)
		{
			if (d.Holder != null && d.Holder._drSpot == d && d.Holder._vp == _vp)
			{
				held[d.Key] = d.Holder;
			}
			if (d.Holder2 != null && d.Holder2._drSpot == d && d.Holder2._vp == _vp)
			{
				held2[d.Key] = d.Holder2;
			}
		}
		Dictionary<long, OvDirt> byKey = new Dictionary<long, OvDirt>();
		c.Low.Clear();
		c.TopSkipped = 0;   // T2
		foreach (OvPart part in c.Parts)
		{
			if (part.Out || part.C == null || !part.C.gameObject.activeSelf)
			{
				continue;
			}
			float bottom = OvPartBottom(part);
			float x0 = Mathf.Ceil((part.Centre.x - part.Half.x) / OvDirtGrid) * OvDirtGrid, x1 = part.Centre.x + part.Half.x;
			float z0 = Mathf.Ceil((part.Centre.z - part.Half.z) / OvDirtGrid) * OvDirtGrid, z1 = part.Centre.z + part.Half.z;
			bool any = false, low = false;
			for (float x = x0; x <= x1 + 0.001f; x += OvDirtGrid)
			{
				for (float z = z0; z <= z1 + 0.001f; z += OvDirtGrid)
				{
					any = true;
					low |= OvDirtAt(part, bottom, x, z, byKey);
				}
			}
			if (!any)
			{
				low |= OvDirtAt(part, bottom, part.Centre.x, part.Centre.z, byKey);   // a box narrower than the grid: its middle
			}
			if (low && part.Ground)
			{
				c.Low.Add(part);   // P5; M4: and its box touches the terrain by the game's own check
			}
		}
		// a point that has gone (dug down, or its part broke) frees whoever had it
		foreach (KeyValuePair<long, HarvestController> kv in held)
		{
			if (byKey.TryGetValue(kv.Key, out OvDirt nd))
			{
				nd.Holder = kv.Value;
				kv.Value._drSpot = nd;
			}
		}
		foreach (KeyValuePair<long, HarvestController> kv in held2)
		{
			if (byKey.TryGetValue(kv.Key, out OvDirt nd))
			{
				nd.Holder2 = kv.Value;
				kv.Value._drSpot = nd;
			}
		}
		c.Dirt.Clear();
		c.Dirt.AddRange(byKey.Values);
	}

	// true = P5: ground in the part's box here that the game won't let go under the part's bottom (a low part)
	private bool OvDirtAt(OvPart part, float bottom, float x, float z, Dictionary<long, OvDirt> byKey)
	{
		Vector3 pt = new Vector3(x, 0f, z);
		float g = GroundAt(pt);
		if (g <= bottom + OvDigGain)
		{
			return false;   // the ground is under the part's bottom here: it doesn't hold it
		}
		float floor = OvFloor(pt);
		if (floor >= bottom)
		{
			return true;   // P5 (John: "Any low parts that go into the ground need to be removed"): the part is mined in the dirt work
		}
		// T2 (John 11-22 22:55Z: "They do not need to clear dirt from the top. Clearing from the bottom will cause that dirt
		// to fall"), silver: ground above the part's top here is dirt on top - not on the list, until the stop brings it back
		if (OvIsSilver(_vp) && g > part.Centre.y + part.Half.y)
		{
			OvClear tc = OvC();
			if (tc != null && !tc.TopBack)
			{
				tc.TopSkipped++;
				return false;
			}
		}
		float goal = Mathf.Max(bottom - OvDirtUnder, floor + OvDigGain);
		long key = OvDirtKey(x, z);
		if (byKey.TryGetValue(key, out OvDirt had))
		{
			if (goal < had.Goal)
			{
				had.Goal = goal;
				had.Part = part;
			}
			return false;
		}
		pt.y = g;
		byKey[key] = new OvDirt { Key = key, P = pt, Goal = goal, Part = part };
		return false;
	}

	// E4 (John 2026-11-21: "No timeout checks... It should be logic based"; "set it aside until the other areas are done or
	// something changes. Then come back to it"): a set-aside point (DirtCool holds when it was set aside) stays aside until
	// (8a) a square of the pit map within 2 m of it changed after that (a dig, a hit, a part gone) - or, with no pit map, the
	// ground within 2 m of it moved 0.1 m - which also clears its tried list and its come-back-once mark; (8b, OvDrChoose)
	// no other dirt point is free. No clock.
	private bool OvDirtCooling(OvClear c, OvDirt d)
	{
		if (!c.DirtCool.TryGetValue(d.Key, out float at))
		{
			return false;
		}
		if (!OvDirtChangedNear(c, d, at))
		{
			return true;
		}
		c.DirtCool.Remove(d.Key);
		c.DirtTried.Remove(d.Key);
		c.DirtDeep.Remove(d.Key);
		c.DirtOnce.Remove(d.Key);
		c.DirtGround.Remove(d.Key);
		if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} is back on the list: something within 2 m of it changed{OvChgSaid()}");
		return false;
	}

	private const float OvAsideNear = 2f;      // E4: what "near" a set-aside point is (8a)

	private const float OvAsideMove = 0.1f;    // E4: with no pit map, the ground move that counts as a change

	// E4 (8a): something within 2 m of the point changed after `at`
	private bool OvDirtChangedNear(OvClear c, OvDirt d, float at)
	{
		_chgSq = -1;   // I3
		OvMap m = _vp?.Map;
		if (m != null && m.Built && m.ChangedAt != null)
		{
			int r = Mathf.CeilToInt(OvAsideNear / OvMapCell);
			int cx = Mathf.FloorToInt((d.P.x - m.X0) / OvMapCell);
			int cz = Mathf.FloorToInt((d.P.z - m.Z0) / OvMapCell);
			for (int dz = -r; dz <= r; dz++)
			{
				for (int dx = -r; dx <= r; dx++)
				{
					int x = cx + dx, z = cz + dz;
					if (x < 0 || z < 0 || x >= m.W || z >= m.H || (dx * dx + dz * dz) * OvMapCell * OvMapCell > OvAsideNear * OvAsideNear)
					{
						continue;
					}
					if (m.ChangedAt[z * m.W + x] > at)
					{
						_chgSq = z * m.W + x;   // I3
						return true;
					}
				}
			}
			return false;
		}
		if (!c.DirtGround.TryGetValue(d.Key, out float[] was))
		{
			return false;
		}
		float[] now = OvDirtGroundGrid(d.P);
		for (int i = 0; i < now.Length; i++)
		{
			if (Mathf.Abs(now[i] - was[i]) >= OvAsideMove)
			{
				return true;
			}
		}
		return false;
	}

	// E4: the ground within 2 m of p (a 5 x 5 grid, 1 m apart) - only used with no pit map
	private float[] OvDirtGroundGrid(Vector3 p)
	{
		float[] g = new float[25];
		int i = 0;
		for (int dz = -2; dz <= 2; dz++)
		{
			for (int dx = -2; dx <= 2; dx++)
			{
				g[i++] = GroundAt(new Vector3(p.x + dx, 0f, p.z + dz));
			}
		}
		return g;
	}

	// L4: a low part (P5: its box touches the ground by the game's own check) - never a ladder part
	private bool OvIsLow(OvPart part)
	{
		OvClear c = OvC();
		return c != null && c.Low.Contains(part);
	}

	// P5: a low part still standing (not buried out of reach)
	private static bool OvLowUp(OvPart part)
	{
		return part != null && !part.Out && part.C != null && part.C.gameObject.activeSelf;
	}

	// D1 (test 23, John: after the pop "They should be organizing the ore and rocks"): parts fell in the game's support drop
	// (Popped, counted by OvPartsLook) and every part still standing is buried out of reach (Out) or a low part with no
	// place on the pit ground to hit it from (its last look found none: OutOfReach, waiting). Those stay in the ground like
	// the buried parts, and the vein's finish runs (OvDoneTick: the way out, then the after-vein steps).
	// ---------- F2: the pit's shared list of loose items ----------
	// John (2026-11-20): "They need to clean the entire pit of ore"; "Can we do a shared sweep that marks all the ore parts
	// and the NPC update the list as they pick up the ore? With some timed auto sweep"; stone and wood tossed in the pit go
	// on it too; no time limits on the job, only for getting stuck.
	// P1 step 2: one square of a tunnel toward a dirt point - lowered to its goal from the square before it (or the spot)
	private sealed class OvLowSq
	{
		public Vector3 P;
		public Vector3 From;      // where it stands to dig it: the square before it, or the deepened trench spot
		public float Goal;
		public OvLowSq Prev;
		public bool Done;
		public bool Failed;
		public HarvestController Holder;
	}

	private OvLowSq _lwSq;        // P1 step 2: the square it is lowering
	private bool _lwOn;           // P1 step 2: its dig is on (its own feet may go down to the square's goal, Q-P1d (a))

	private sealed class OvLoose
	{
		public ItemDrop D;

		public string Name;

		public bool Ore;

		public HarvestController Holder;

		public float AsideAt = -1f;   // F5: set aside at (a failed try) - back when something within 2 m changes; -1 = not aside

		public bool Once;             // F5: it came back once with nothing else left
	}

	private const float OvLooseEvery = 5f;      // the shared look at the pit
	private const float OvLooseNoCloser = 10f;   // the stuck rule: no 0.3 m closer in 10 s (or the mover gave up)
	private const float OvLooseReach = 1.2f;     // close enough to pick it up (flat), as the old pick-back

	private OvLoose _lsItem;
	private float _lsBest;
	private float _lsBestAt;
	private float _lsStart;
	private static readonly Collider[] s_lsBuf = new Collider[512];

	// the vein's list: looked at by whichever companion finds it due (every 5 s), read by the others. On it: ore, stone
	// and wood lying on the pit map's area (S3; the pit ground before the map is built), tossed or not - except ore on the
	// ore dump (the dump carry takes that to storage).
	private void OvLooseScan(OvClear c)
	{
		float now = Time.time;
		if (now - c.LooseAt < OvLooseEvery)
		{
			return;
		}
		c.LooseAt = now;
		float r = 0f;
		foreach (OvSpot sp in _vp.Spots)
		{
			r = Mathf.Max(r, Flat(sp.P, _vp.Center));
		}
		r += OvThingRadius + 8f;   // + 8 m: the pit's depth (the game's dig limit), the look is a sphere
		Vector3 lookAt = _vp.Center;
		OvMap lm = _vp.Map;
		bool onMap = lm != null && lm.Built;
		if (onMap)
		{
			// S3 (John 11-22 Q1 / 22:06Z: anything in the pit, tossed piles too): the whole pit map's area
			float hw = lm.W * OvMapCell * 0.5f, hh = lm.H * OvMapCell * 0.5f;
			lookAt = new Vector3(lm.X0 + hw, _vp.Center.y, lm.Z0 + hh);
			r = Mathf.Sqrt(hw * hw + hh * hh) + 8f;
		}
		bool hasDump = PitDumpFor(out Vector3 dump);
		int n = Physics.OverlapSphereNonAlloc(lookAt, r, s_lsBuf, _itemLayerMask);
		HashSet<int> seen = new HashSet<int>();
		for (int i = 0; i < n; i++)
		{
			Collider col = s_lsBuf[i];
			ItemDrop d = col != null && col.attachedRigidbody != null ? col.attachedRigidbody.GetComponent<ItemDrop>() : null;
			if (d == null || d.m_itemData == null)
			{
				continue;
			}
			ZNetView nv = d.GetComponent<ZNetView>();
			if (nv == null || !nv.IsValid())
			{
				continue;
			}
			int id = d.gameObject.GetInstanceID();
			if (!seen.Add(id))
			{
				continue;
			}
			Vector3 at = d.transform.position;
			if (onMap ? !OvOnMapArea(lm, at) : !OvInPitPt(at))
			{
				continue;
			}
			d.Load();
			string name = MHR_GatherRules.DropName(d);
			bool ore = MHR_GatherRules.IsOreItem(d.m_itemData);
			// F4 (John 11-21 23:30Z / 11-22 04:24Z): any other item the game's own auto-pickup takes is on the list too (not
			// what a player dropped - the game's OnPlayerDrop clears that flag); stone and wood as before (also tossed ones)
			if (!ore && name != "Stone" && name != "Wood" && !d.m_autoPickup)
			{
				continue;
			}
			// F2: stone tossed where the walk to the stone dump gave up is left there
			if (name == "Stone" && MHR_GatherRules.LeftStone(d.gameObject))
			{
				continue;
			}
			// S3b: "on the ore dump" = inside the same 2.5 m ball round the dump the dump carry looks in (CollectTick)
			if (ore && hasDump && Vector3.Distance(at, dump + Vector3.up) <= 2.5f)
			{
				seen.Remove(id);
				continue;   // on the ore dump
			}
			if (!c.Loose.ContainsKey(id))
			{
				c.Loose[id] = new OvLoose { D = d, Name = name, Ore = ore };
				c.LooseChangedAt = now;   // R1
			}
		}
		List<int> gone = new List<int>();
		foreach (KeyValuePair<int, OvLoose> kv in c.Loose)
		{
			if (!seen.Contains(kv.Key) && (kv.Value.Holder == null || kv.Value.Holder._lsItem != kv.Value))
			{
				gone.Add(kv.Key);
			}
		}
		foreach (int id in gone)
		{
			c.Loose.Remove(id);
			c.LooseChangedAt = now;   // R1
		}
	}

	// S5: one companion at a time; after a carry that picked up nothing it starts again only once the pit's item list changed
	private bool OvDumpCarry(OvClear c)
	{
		if (!PitDumpFor(out Vector3 dump))
		{
			return false;
		}
		HarvestController by = c.DumpCarryBy;
		if (by != null && by != this && by.isActiveAndEnabled && MHR_GatherRules.Collecting(by))
		{
			return false;
		}
		if (by != null && c.DumpCarryAt >= 0f && !MHR_GatherRules.Collecting(by) && !MHR_GatherRules.CollectTook(by) && c.LooseChangedAt <= c.DumpCarryAt)
		{
			return false;
		}
		if (!MHR_GatherRules.StartDumpCarry(this, dump))
		{
			return false;
		}
		c.DumpCarryBy = this;
		c.DumpCarryAt = Time.time;
		if (CompanionsPlugin.LogNormal) LogInfo($"Nothing else on the pit's list it can take - carrying the ore at the ore dump {MHR_Mover.Fmt(dump)} to storage near it, then back to the pit's list (S5)");
		_ovTrip = true;
		OvLeave("carrying the ore dump to storage", quiet: true);
		return true;
	}

	private static bool OvLooseHere(OvLoose l)
	{
		return l.D != null && l.D.gameObject.activeInHierarchy && l.D.m_itemData != null;
	}

	// 0 = it takes it now, 1 = not now (someone else's, cooling, not enough for a pile), 2 = only its weight / bag stops it
	private int OvLooseTakes(OvClear c, OvLoose l, Inventory inv, Dictionary<string, int> pileOk)
	{
		if (!OvLooseHere(l) || (l.Holder != null && l.Holder != this && l.Holder._lsItem == l) || OvLooseAside(l))
		{
			return 1;
		}
		// F4: wood and the other items are taken whatever the count (wood is piled when there is enough, else it stays in
		// the bag - StartPile); only stone keeps the pile rule
		if (l.Name == "Stone" && _ovPitOpen)
		{
			// F2 (after the pop): stone lying away from the stone dump is taken whatever the count (it goes to the dump, or
			// into a pile); stone on the dump only by the one companion taking it for a pile, and only when the dump's stone
			// and its own make a full pile it can place now
			if (OvAtStoneDump(c, l.D.transform.position))
			{
				if (c.StoneTaker != null && c.StoneTaker != this && c.StoneTaker.isActiveAndEnabled && c.StoneTaker._vp == _vp)
				{
					return 1;
				}
				if (!MHR_GatherRules.PileCost(this, "Stone", out int dcost) || OvNoPileNow(c))
				{
					return 1;
				}
				int have = MHR_GatherRules.CountStone(inv);
				foreach (OvLoose o in c.Loose.Values)
				{
					if (o.Name == "Stone" && OvLooseHere(o) && !OvLooseAside(o) && OvAtStoneDump(c, o.D.transform.position))
					{
						have += o.D.m_itemData.m_stack;
					}
				}
				if (have < dcost)
				{
					return 1;
				}
				// X1 (e) (John 11-23 05:29Z): the stone it still needs for the pile must fit in its bag (the game's room check)
				int need = dcost - MHR_GatherRules.CountStone(inv);
				if (need > 0 && !inv.CanAddItem(l.D.m_itemData, need))
				{
					return 1;
				}
				// X1 (a): over its weight line is fine - the pile gets it back under (only the bag's room counts)
				return inv.CanAddItem(l.D.m_itemData) ? 0 : 2;
			}
		}
		else if (l.Name == "Stone")
		{
			// stone: only while what lies in the pit and what it carries make one more pile it can build now (a
			// last bit short of a pile stays where it is)
			if (!pileOk.TryGetValue(l.Name, out int ok))
			{
				ok = 0;
				if (MHR_GatherRules.PileCost(this, l.Name, out int cost))
				{
					int have = 0;
					foreach (ItemDrop.ItemData it in inv.GetAllItems())
					{
						if (it.m_dropPrefab != null && it.m_dropPrefab.name == l.Name)
						{
							have += it.m_stack;
						}
					}
					foreach (OvLoose o in c.Loose.Values)
					{
						if (o.Name == l.Name && OvLooseHere(o) && !OvLooseAside(o))
						{
							have += o.D.m_itemData.m_stack;
						}
					}
					ok = have >= cost ? 1 : 0;
				}
				pileOk[l.Name] = ok;
			}
			if (ok == 0)
			{
				return 1;
			}
		}
		// a pickup never takes it over its weight limit - T4 (John 11-22 22:55Z): an ore stack is taken when one of it fits
		// (the part over the limit is dropped right after the pickup, and stays on the list)
		float takes = l.Ore ? l.D.m_itemData.GetWeight() / Mathf.Max(1, l.D.m_itemData.m_stack) : l.D.m_itemData.GetWeight();
		if (inv.GetTotalWeight() + takes > HeavyLimit || !inv.CanAddItem(l.D.m_itemData))
		{
			return 2;
		}
		return 0;
	}

	// true = busy with the list (going for an item, or taking its ore to the dump to make room); false = nothing on it it
	// can take
	private bool OvLooseTick(OvClear c, bool standBy = true)
	{
		if (_humanoid == null || _humanoid.GetInventory() == null)
		{
			return false;
		}
		float now = Time.time;
		Vector3 me = base.transform.position;
		if (_lsItem != null)
		{
			OvLoose l = _lsItem;
			if (!OvLooseHere(l))
			{
				OvLooseOff();
				return true;
			}
			Vector3 at = l.D.transform.position;
			float dist = Flat(me, at);
			if (dist <= OvLooseReach && Mathf.Abs(at.y - me.y) <= 2f)
			{
				_ai?.StopMoving();
				if (!l.D.CanPickup(autoPickupDelay: false))
				{
					l.D.RequestOwn();
					if (now - _lsBestAt > OvLooseNoCloser)
					{
						OvLooseCool(l, "it couldn't take it (another game owns it)");
					}
					return true;
				}
				// the player's E (Humanoid.Pickup), as the old pick-back
				string what = l.D.m_itemData.m_shared.m_name;
				int count = l.D.m_itemData.m_stack;
				if (_humanoid.Pickup(l.D.gameObject, autoequip: false, autoPickupDelay: false))
				{
					c.Loose.Remove(l.D.gameObject.GetInstanceID());
					c.LooseChangedAt = now;   // R1
					if (CompanionsPlugin.LogDetailed) Log($"Picked up {count}x {MHR_List.Local(what)} in the pit ({c.Loose.Count} on the pit's list)");
					// T4: over its weight limit with it - the ore over the limit is dropped right here (it stays on the list)
					Inventory pin = _humanoid.GetInventory();
					if (l.Ore && pin != null && pin.GetTotalWeight() > HeavyLimit)
					{
						float w = pin.GetTotalWeight();
						int dropped = MHR_GatherRules.DropOreTo(_humanoid, HeavyLimit, null);
						if (dropped > 0 && CompanionsPlugin.LogNormal) LogInfo($"Picked up {count} {MHR_List.Local(what)} ({w:F0} of {HeavyLimit:F0}) - dropped {dropped} ore here to get under its limit; it comes back for it (T4)");
					}
					OvLooseOff();
				}
				else
				{
					OvLooseCool(l, "the pickup didn't take");
				}
				return true;
			}
			if (dist < _lsBest - 0.3f)
			{
				_lsBest = dist;
				_lsBestAt = now;
			}
			if ((now - _lsStart > 0.5f && MHR_Mover.GaveUp(_ai)) || now - _lsBestAt > OvLooseNoCloser)
			{
				OvLooseCool(l, MHR_Mover.GaveUp(_ai) ? "the way there failed" : $"no closer in {OvLooseNoCloser:F0} s");
				return true;
			}
			_ai?.MHR_Go(Time.deltaTime, at, OvLooseReach * 0.8f, false, "Pickup");
			return true;
		}
		OvLooseScan(c);
		Inventory inv = _humanoid.GetInventory();
		Dictionary<string, int> pileOk = new Dictionary<string, int>();
		OvLoose best = null;
		float bd = float.MaxValue;
		bool heavyLeft = false;
		foreach (OvLoose l in c.Loose.Values)
		{
			int t = OvLooseTakes(c, l, inv, pileOk);
			if (t == 2 && l.Ore)
			{
				heavyLeft = true;
			}
			if (t != 0)
			{
				continue;
			}
			float d = Flat(me, l.D.transform.position);
			// R3c.2 (John, 2026-11-20: ore over stone in the bags): the nearest ore first, stone only when no ore is left it can take
			// F4: wood and the other items only when no ore or stone is left it can take
			if (!l.Ore)
			{
				d += l.Name == "Stone" ? 10000f : 20000f;
			}
			if (d < bd)
			{
				bd = d;
				best = l;
			}
		}
		if (best != null && best.Name == "Stone" && _ovPitOpen && OvAtStoneDump(c, best.D.transform.position))
		{
			if (c.StoneTaker != this && CompanionsPlugin.LogNormal) LogInfo("Taking stone off the stone dump for a pile (the one companion doing it)");
			c.StoneTaker = this;   // F2
		}
		if (best != null)
		{
			best.Holder = this;
			_lsItem = best;
			_lsBest = bd;
			_lsBestAt = now;
			_lsStart = now;
			return true;
		}
		// F2: nothing it can take - it isn't taking stone off the dump now; its stone that makes no pile goes to the dump
		if (c.StoneTaker == this)
		{
			c.StoneTaker = null;
		}
		if (_ovPitOpen && OvStoneToDump(c))
		{
			return true;
		}
		// full: ore still in the pit that only its weight stops - its ore to a chest (S4), else to the dump, then on
		if (heavyLeft && MHR_GatherRules.HasOre(_humanoid) && OvHeavyDump())
		{
			if (CompanionsPlugin.LogNormal) LogInfo("Full with ore from the pit - taking it away (a chest, else the ore dump), then back to the pit's list");
			return true;
		}
		// S5: nothing else it can take - the ore at the dump goes to storage with room within 20 m of it (the dump carry)
		if (_ovPitOpen && OvDumpCarry(c))
		{
			return true;
		}
		// F5: nothing else it can take - each set-aside item that hasn't come back yet comes back once
		bool back = false;
		foreach (OvLoose l in c.Loose.Values)
		{
			if (OvLooseHere(l) && l.AsideAt >= 0f && !l.Once && (l.Holder == null || l.Holder == this || l.Holder._lsItem != l))
			{
				l.AsideAt = -1f;
				l.Once = true;
				back = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"The loose \"{MHR_List.Local(l.D.m_itemData.m_shared.m_name)}\" in the pit comes back once: nothing else is left it can take");
			}
		}
		if (back)
		{
			return true;
		}
		// another companion is still going for an item: the vein isn't done yet (it stands by)
		if (!standBy)
		{
			return false;   // R3c.2: the pick-only call goes on to the leftover parts
		}
		foreach (OvLoose l in c.Loose.Values)
		{
			if (l.Holder != null && l.Holder != this && l.Holder._lsItem == l && OvLooseHere(l))
			{
				_ai?.StopMoving();
				return true;
			}
		}
		return false;
	}

	// ---------- F2: the stone dump ----------
	private const float OvStoneDumpNear = 2.5f;   // F2: on the dump = within 2.5 m of it (the ore dump's own test, OvLooseScan)

	private bool OvAtStoneDump(OvClear c, Vector3 p)
	{
		return c.StoneDumpHas && Flat(p, c.StoneDump) <= OvStoneDumpNear;
	}

	// F2: no stone pile is tried until the pit map changes after one couldn't be placed
	private bool OvNoPileNow(OvClear c)
	{
		if (c.NoPileAt < 0f)
		{
			return false;
		}
		OvMap m = _vp?.Map;
		if (m != null && m.LastChange > c.NoPileAt)
		{
			c.NoPileAt = -1f;
			if (CompanionsPlugin.LogNormal) LogInfo("The pit changed - stone piles are tried again");
			return false;
		}
		return true;
	}

	// F2: the stone dump (made when first needed: the first stone pile spot the game allows in the pit, else the trench
	// floor at the trench spot nearest it at least 2.5 m from the ore dump)
	internal bool OvStoneDumpAt(out Vector3 at)
	{
		at = Vector3.zero;
		if (_vp == null || !_ovPitOpen)
		{
			return false;
		}
		OvClear c = OvC();
		if (!c.StoneDumpHas)
		{
			if (MHR_GatherRules.StonePileSpot(this, out Vector3 sp))
			{
				OvStoneDumpSet(c, sp, "the first stone pile spot the game allows in the pit");
			}
			else
			{
				bool hasOre = PitDumpFor(out Vector3 od);
				OvSpot best = null;
				float bd = float.MaxValue;
				Vector3 me = base.transform.position;
				foreach (OvSpot q in _vp.Spots)
				{
					Vector3 f = new Vector3(q.P.x, GroundAt(q.P), q.P.z);
					if (hasOre && Flat(f, od) < OvStoneDumpNear)
					{
						continue;
					}
					float d = Flat(me, f);
					if (d < bd)
					{
						bd = d;
						best = q;
					}
				}
				if (best == null)
				{
					return false;
				}
				OvStoneDumpSet(c, new Vector3(best.P.x, GroundAt(best.P), best.P.z), $"no stone pile spot - the trench floor at trench spot {best.Idx + 1}");
			}
		}
		at = c.StoneDump;
		at.y = GroundAt(at);
		return true;
	}

	// F2: the stone dump, if there is one yet (none is made here)
	internal bool OvStoneDumpHas(out Vector3 at)
	{
		at = Vector3.zero;
		if (_vp == null || !_ovPitOpen || !s_ovClear.TryGetValue(_vp, out OvClear c) || !c.StoneDumpHas)
		{
			return false;
		}
		at = c.StoneDump;
		return true;
	}

	private void OvStoneDumpSet(OvClear c, Vector3 at, string why)
	{
		c.StoneDump = at;
		c.StoneDumpHas = true;
		if (CompanionsPlugin.LogNormal) LogInfo($"The stone dump for \"{_vp.Name}\": {MHR_Mover.Fmt(at)} ({why}) - all the pit's stone goes there");
	}

	// F2: called by StartPile - a stone pile is being built after the pop: it isn't taking stone off the dump any more, and
	// the first one's spot is the stone dump when there is none yet
	internal void OvStonePileStarted(Vector3 spot)
	{
		if (_vp == null || !_ovPitOpen)
		{
			return;
		}
		OvClear c = OvC();
		if (c.StoneTaker == this)
		{
			c.StoneTaker = null;
		}
		if (!c.StoneDumpHas)
		{
			OvStoneDumpSet(c, spot, "the first stone pile's spot");
		}
	}

	// X1 (John 11-23 05:29Z: "If it can pick up the stone and then has enough to build a pile it should build the pile"):
	// collecting = it is the one taking stone off the dump, it has fewer than a pile, and the dump's stone and its own make
	// one (a pile can be tried now). While collecting, the too-heavy steps leave its stone alone.
	internal bool OvStoneCollecting()
	{
		if (_vp == null || !_ovPitOpen || _humanoid == null || !s_ovClear.TryGetValue(_vp, out OvClear c) || c.StoneTaker != this || OvNoPileNow(c))
		{
			return false;
		}
		if (!MHR_GatherRules.PileCost(this, "Stone", out int cost))
		{
			return false;
		}
		int have = MHR_GatherRules.CountStone(_humanoid.GetInventory());
		if (have >= cost)
		{
			return false;
		}
		foreach (OvLoose o in c.Loose.Values)
		{
			if (o.Name == "Stone" && OvLooseHere(o) && !OvLooseAside(o) && OvAtStoneDump(c, o.D.transform.position))
			{
				have += o.D.m_itemData.m_stack;
			}
		}
		return have >= cost;
	}

	// F2: called by StartPile - no place the game allows for a stone pile: none tried again until the pit map changes
	// X1 (f): also when it ran out of stamina on the way to the pile's spot (why)
	internal void OvNoPile(string why = null)
	{
		if (_vp == null || !_ovPitOpen)
		{
			return;
		}
		OvClear c = OvC();
		if ((c.NoPileAt < 0f || why != null) && CompanionsPlugin.LogNormal) LogInfo((why ?? "No place the game allows for a stone pile") + " - the stone goes to the stone dump; no pile tried until the pit changes");
		c.NoPileAt = Time.time;
		if (c.StoneTaker == this)
		{
			c.StoneTaker = null;
		}
	}

	// F2: no stone pile is tried now (the pit hasn't changed since one couldn't be placed)
	internal bool OvNoPileNow()
	{
		return _vp != null && _ovPitOpen && OvNoPileNow(OvC());
	}

	// F2: its stone that makes no pile it can place now (fewer than a pile, or no pile tried now) - walked to the stone dump
	// and dropped there (the toss, the player's drop). True = started.
	private bool OvStoneToDump(OvClear c)
	{
		int have = MHR_GatherRules.CountStone(_humanoid.GetInventory());
		if (have <= 0)
		{
			return false;
		}
		bool pile = MHR_GatherRules.PileCost(this, "Stone", out int cost) && have >= cost && !OvNoPileNow(c);
		if (pile || !OvStoneDumpAt(out Vector3 at))
		{
			return false;   // a pile's worth: StartPile builds it (StoneDue)
		}
		return MHR_GatherRules.TossAt(this, "Stone", at, $"its {have} stone (short of a pile, or no pile now) to the stone dump");
	}

	// F5 (John 11-21 23:40Z: logic, not a timer - the 60 s wait goes): a failed try sets the item aside until something
	// within 2 m of it changes on the pit map; with nothing else left it comes back once (OvLooseTick), then it is ignored
	private void OvLooseCool(OvLoose l, string why)
	{
		l.AsideAt = Time.time;
		if (CompanionsPlugin.LogNormal) LogInfo($"The loose \"{(l.D != null ? MHR_List.Local(l.D.m_itemData.m_shared.m_name) : "?")}\" in the pit is set aside until something within 2 m of it changes{(l.Once ? "" : " or nothing else is left")}: {why}");
		OvLooseOff();
	}

	// F5: set aside and nothing within 2 m of it changed since (a change puts it back on the list)
	private bool OvLooseAside(OvLoose l)
	{
		if (l.AsideAt < 0f)
		{
			return false;
		}
		if (l.D != null && OvChangedNearPt(l.D.transform.position, l.AsideAt))
		{
			l.AsideAt = -1f;
			if (CompanionsPlugin.LogNormal) LogInfo($"The loose \"{MHR_List.Local(l.D.m_itemData.m_shared.m_name)}\" in the pit is back on the list: something within 2 m of it changed{OvChgSaid()}");
			return false;
		}
		return true;
	}

	// F5: on the pit map, a square within 2 m of p changed after `at` (no map = false)
	private bool OvChangedNearPt(Vector3 p, float at)
	{
		_chgSq = -1;   // I3
		OvMap m = _vp?.Map;
		if (m == null || !m.Built || m.ChangedAt == null)
		{
			return false;
		}
		int r = Mathf.CeilToInt(OvAsideNear / OvMapCell);
		int cx = Mathf.FloorToInt((p.x - m.X0) / OvMapCell);
		int cz = Mathf.FloorToInt((p.z - m.Z0) / OvMapCell);
		for (int dz = -r; dz <= r; dz++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				int x = cx + dx, z = cz + dz;
				if (x < 0 || z < 0 || x >= m.W || z >= m.H || (dx * dx + dz * dz) * OvMapCell * OvMapCell > OvAsideNear * OvAsideNear)
				{
					continue;
				}
				if (m.ChangedAt[z * m.W + x] > at)
				{
					_chgSq = z * m.W + x;   // I3
					return true;
				}
			}
		}
		return false;
	}

	private void OvLooseOff()
	{
		if (_lsItem != null && _lsItem.Holder == this)
		{
			_lsItem.Holder = null;
		}
		_lsItem = null;
	}

	// F2: the vein is gone (it popped to the last part) - the list of its pit before it leaves
	private bool OvGoneLoose()
	{
		OvPitOpen();
		if (_lsItem == null && Time.time < _clNextPick)
		{
			return true;   // standing by: looked again once a second
		}
		bool busy = OvLooseTick(OvC());
		if (busy && _lsItem == null)
		{
			_clNextPick = Time.time + 1f;
		}
		return busy;
	}

	// F1: from the pop, the pit is open for its stone and wood piles (once per vein)
	private void OvPitOpen()
	{
		if (_ovPitOpen)
		{
			return;
		}
		_ovPitOpen = true;
		MHR_GatherRules.OpenPit(this);
	}

	// D4: the pit ground of the vein it is on every `step` m, by the pit test (the ring and the trench)
	internal bool PitGrid(List<Vector3> into, float step)
	{
		into.Clear();
		OvPlan p = _vp;
		if (p == null || p.Spots.Count == 0)
		{
			return false;
		}
		float r = 0f;
		foreach (OvSpot sp in p.Spots)
		{
			r = Mathf.Max(r, Flat(sp.P, p.Center));
		}
		r += OvThingRadius;
		for (float x = -r; x <= r; x += step)
		{
			for (float z = -r; z <= r; z += step)
			{
				Vector3 pt = new Vector3(p.Center.x + x, p.Center.y, p.Center.z + z);
				if (OvInPit(p, new Bounds(pt, Vector3.zero)))
				{
					pt.y = GroundAt(pt);
					into.Add(pt);
				}
			}
		}
		return into.Count > 0;
	}

	// D2: the vein job saw parts fall in the game's support drop (MHR_GatherRules.AfterVein's "popped")
	internal bool OvSawPop()
	{
		return _vp != null && OvPopped(OvC());
	}

	// W0 (replaces U6's "no part left above the floor", which missed test 26's pop: ~17 parts still in the dirt stayed):
	// the pop OvPartsLook saw - 2 or more parts dropped at once and no group of 3 or more linked parts left standing
	private static bool OvPopped(OvClear c)
	{
		return c.PopSeen;
	}

	// R3c.1 (log 7f376ab0: back from the chest trip, the scan skipped the popped vein as "every part left of it is under
	// the ground" - idle): this vein's plan saw the pop and its after-pop work (leftover parts, loose items) isn't done
	internal static bool OvAfterPopOpen(MineRock5 m)
	{
		if (m == null)
		{
			return false;
		}
		foreach (OvPlan p in s_ovPlans)
		{
			if (p.Rocks.Contains(m) && s_ovClear.TryGetValue(p, out OvClear c) && c.PopSeen && !c.Done && !c.Stopped)
			{
				return true;
			}
		}
		return false;
	}

	private bool OvPopDone(OvClear c)
	{
		if (c.Popped <= 0)
		{
			return false;
		}
		foreach (OvPart part in c.Parts)
		{
			if (part.Out)
			{
				continue;
			}
			if (OvIsLow(part) && part.OutOfReach && part.Wait && OvPartCooling(part))
			{
				continue;
			}
			return false;
		}
		return true;
	}

	// K1: a low part no swing from the ground reaches (OutOfReach, K2) doesn't hold the last step back (L4: never a ladder part)
	private static bool OvLowLeft(OvClear c)
	{
		foreach (OvPart part in c.Low)
		{
			if (OvLowUp(part) && !part.OutOfReach)
			{
				return true;
			}
		}
		return false;
	}

	// GG2 / P5 (John: "Its better for the NPC's to work clearing one area rather than running around"): ONE list - the dirt
	// points and the low parts - and the nearest (flat) that no one has and that isn't waiting, with a place to stand at the
	// bottom of the pit (P3). A dirt point is dug (OvDrTick); a low part is mined from there (the part round, OvClRound).
	private bool OvDrChoose(OvClear c)
	{
		Vector3 me = base.transform.position;
		List<OvDirt> free = new List<OvDirt>();
		List<OvPart> lows = new List<OvPart>();
		bool others = false;
		bool othersDirt = false;   // F8: a dirt point another companion is working
		string othersWhy = null;   // SV5
		// W1: a lost point's note goes once the point is off the list
		if (_drLost.Count > 0)
		{
			HashSet<long> on = new HashSet<long>();
			foreach (OvDirt d in c.Dirt)
			{
				on.Add(d.Key);
			}
			_drLost.RemoveWhere((long k) => !on.Contains(k));
		}
		foreach (OvDirt d in c.Dirt)
		{
			// K4: a point already down to its goal (the dig's own test, OvDrTick) isn't picked - the list is made again soon
			if (OvDirtCooling(c, d) || GroundAt(d.P) <= d.Goal + OvDigGain)
			{
				continue;
			}
			// W1 (log c993a0b8: Je, Comp and Gunter took one point from each other every frame, none got there): a point
			// taken over from this one isn't taken back while another companion still has it; the note goes once none has
			if (_drLost.Contains(d.Key))
			{
				HarvestController lh = OvDirtHasOther(d);
				if (lh != null)
				{
					others = true;
					othersDirt = true;
					othersWhy ??= $"the dirt at {MHR_Mover.Fmt(d.P)} was taken over by {lh.OvName()} - left to it (W1)";
					continue;
				}
				_drLost.Remove(d.Key);
			}
			// SV1: another companion's point is taken over when that companion isn't working it
			// F9 (John 11-21 23:13Z / 23:30Z): at most two on a point - full only when both are other companions working it
			if (OvDirtSlotBusy(d, d.Holder) && OvDirtSlotBusy(d, d.Holder2))
			{
				others = true;
				othersDirt = true;
				othersWhy ??= $"the dirt at {MHR_Mover.Fmt(d.P)} is {d.Holder.OvName()}'s and {d.Holder2.OvName()}'s (two on it)";
				continue;
			}
			free.Add(d);
		}
		foreach (OvPart part in c.Low)
		{
			if (!OvLowUp(part) || OvPartCooling(part) || lows.Contains(part))
			{
				continue;
			}
			if (OvPartHeldByOther(part))
			{
				others = true;
				othersWhy ??= $"the low part \"{part.C.name}\" is {part.Holder.OvName()}'s ({part.Holder.OvName()} is {part.Holder.OvHolderDoing(part.Holder._clWalk)})";
				continue;
			}
			lows.Add(part);
		}
		// F8 (John 11-21 23:13Z / 23:30Z, silver only): a low part (in the ground at the game's dig limit) is mined only once
		// all the dirt the game lets them dig is done - no dirt point free, none another companion is working, none set aside
		// that hasn't had its one return (E4)
		if (OvIsSilver(_vp) && lows.Count > 0)
		{
			bool dirtLeft = free.Count > 0 || othersDirt;
			if (!dirtLeft)
			{
				foreach (OvDirt d in c.Dirt)
				{
					if (c.DirtCool.ContainsKey(d.Key) && !OvOnceUsed(c, d) && GroundAt(d.P) > d.Goal + OvDigGain)
					{
						dirtLeft = true;
						break;
					}
				}
			}
			if (dirtLeft)
			{
				if (CompanionsPlugin.LogDetailed) Log($"{lows.Count} low part(s) wait until the dirt is done (silver)");
				lows.Clear();
			}
		}
		// E4 (8b, John: "set it aside until the other areas are done ... Then come back to it"): no other dirt point or low
		// part is free and none is being worked by another companion - each set-aside point that hasn't come back this way
		// yet comes back once, its tried list cleared; set aside again with nothing changed, it waits for a change (8a)
		if (free.Count == 0 && lows.Count == 0 && !others)
		{
			foreach (OvDirt d in c.Dirt)
			{
				if (!c.DirtCool.ContainsKey(d.Key) || OvOnceUsed(c, d) || GroundAt(d.P) <= d.Goal + OvDigGain)
				{
					continue;
				}
				c.DirtCool.Remove(d.Key);
				c.DirtTried.Remove(d.Key);
				c.DirtDeep.Remove(d.Key);
				c.DirtGround.Remove(d.Key);
				c.DirtOnce.Add(d.Key);
				free.Add(d);
				if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} comes back once: no other dirt point is free");
			}
			// SV7: the set-aside low parts the same way (they had a 1 min wait)
			foreach (OvPart part in c.Low)
			{
				if (!OvLowUp(part) || part.CoolAt < 0f || (part.CoolOnce && part.FailAt > c.ProgAt) || part.Wait || lows.Contains(part) || OvPartHeldByOther(part))
				{
					continue;
				}
				part.CoolAt = -1f;
				part.CoolGround = null;
				part.CoolOnce = true;
				lows.Add(part);
				if (CompanionsPlugin.LogNormal) LogInfo($"The low part \"{(part.C != null ? part.C.name : "?")}\" comes back once: no other dirt point or low part is free");
			}
		}
		// F8: after the set-aside dirt came back (E4), that dirt goes first too
		if (OvIsSilver(_vp) && free.Count > 0)
		{
			lows.Clear();
		}
		// nearest first: a dirt point by its own place, a low part by the nearest point of its box
		List<KeyValuePair<float, object>> all = new List<KeyValuePair<float, object>>();
		foreach (OvDirt d in free)
		{
			all.Add(new KeyValuePair<float, object>(Flat(me, d.P), d));
		}
		foreach (OvPart part in lows)
		{
			all.Add(new KeyValuePair<float, object>(Flat(me, OvBoxNear(part, me)), part));
		}
		all.Sort((KeyValuePair<float, object> a, KeyValuePair<float, object> b) => a.Key.CompareTo(b.Key));
		// F10 (John 11-20 20:59Z / 11-21 23:30Z / 11-22 04:24Z, silver only; replaces SV6): each companion works its own side of
		// the vein's middle line first (a point up to 0.5 m past the line counts as its side too); the other side only when
		// its own side has no free point - then its side switches and it goes round by the trench
		if (OvIsSilver(_vp) && OvMidLine(c))
		{
			if (_mlPlan != _vp || _mlSide == 0)
			{
				_mlPlan = _vp;
				_mlSide = OvMidSide(c, me, out _) >= 0f ? 1 : -1;
				if (CompanionsPlugin.LogNormal) LogInfo($"Its side of the middle line of \"{_vp.Name}\": {(_mlSide > 0 ? "A" : "B")} (where it stands)");
			}
			bool own = false;
			foreach (KeyValuePair<float, object> kv in all)
			{
				if (kv.Value is OvDirt od && OvMidOwn(c, od.P))
				{
					own = true;
					break;
				}
			}
			if (!own && all.Exists((KeyValuePair<float, object> kv) => kv.Value is OvDirt))
			{
				_mlSide = -_mlSide;
				if (CompanionsPlugin.LogNormal) LogInfo($"No free dirt left on its side of the middle line - to the other side ({(_mlSide > 0 ? "A" : "B")})");
			}
			all.RemoveAll((KeyValuePair<float, object> kv) => kv.Value is OvDirt od2 && !OvMidOwn(c, od2.P));
		}
		int tries = 0;
		foreach (KeyValuePair<float, object> kv in all)
		{
			if (tries++ >= 6)
			{
				break;   // the rest next second
			}
			if (kv.Value is OvPart lp)
			{
				int lr = OvStandFor(c, new Vector3(lp.Centre.x, OvPartBottom(lp), lp.Centre.z), lp, out Vector3 lstand, out string lwhy);
				lp.OutOfReach = lr == 1;
				if (lr == 2)
				{
					OvPartCool(lp, lwhy, wait: false);
					continue;
				}
				if (lr == 1)
				{
					// K1 (John, 2026-11-19: no ladder until the rest of the dirt and low parts are removed): it waits for the
					// last step (OvOreChoose), not counted as a failure and not holding that step back (OvLowLeft)
					OvPartCool(lp, lwhy + " - a low part (it touches the ground): never from a ladder, it waits for a place on the ground", wait: true);
					continue;
				}
				OvSpot lws = OvClSpotFor(lp);
				if (lws != null && OvWayOutNeeded(lws))
				{
					OvWayOutHere(lws);
					return true;
				}
				OvClBegin(lp, lstand, ladder: false, low: true);
				return true;
			}
			OvDirt d = (OvDirt)kv.Value;
			int r = OvStandFor(c, new Vector3(d.P.x, GroundAt(d.P), d.P.z), null, out Vector3 stand, out string why, d);
			if (r != 0 && OvDirtOther(d) != null)
			{
				// F9: no place more than a quarter turn round it from the companion already on it - left to that one (not
				// set aside)
				others = true;
				othersDirt = true;
				othersWhy ??= $"the dirt at {MHR_Mover.Fmt(d.P)} is {OvDirtOther(d).OvName()}'s ({why})";
				continue;
			}
			if (r != 0)
			{
				OvDirtCool(c, d, why);
				continue;
			}
			// R4: a way out near where it will work first, when there's none within 15 m
			OvSpot ws = OvNearest(d.P);
			if (ws != null && OvWayOutNeeded(ws))
			{
				OvWayOutHere(ws);
				return true;
			}
			OvDirtClaim(d);   // F9
			_drSpot = d;
			_sbWhy = null;   // SV5
			_drStand = stand;
			_drWalk = true;
			_drMpTried = _drMpOn = false;   // MP4: one way up at the map's blocking step per round
			_moveTimer = 0f;
			_ai?.StopMoving();
			if (CompanionsPlugin.LogNormal) LogInfo($"Dirt holding up \"{d.Part.C.name}\" of \"{_vp.Name}\" at {MHR_Mover.Fmt(d.P)} (ground {GroundAt(d.P):F2}, the part's bottom {OvPartBottom(d.Part):F2}) - digging it to {d.Goal:F2} from {MHR_Mover.Fmt(stand)} (the bottom of the pit there, ground {stand.y:F2}); {c.Dirt.Count} dirt point(s) and {lows.Count} low part(s) free");
			return true;
		}
		_clNextPick = Time.time + 1f;
		if (all.Count > 6)
		{
			return true;
		}
		_ai?.StopMoving();
		if (others)
		{
			OvStandBy((othersWhy ?? "the rest is other companions'") + $"; {free.Count + lows.Count} other free point(s)/low part(s) had no place to work them from");
			return true;   // the rest are other companions' - it stands by
		}
		OvStuckCheck(c);
		if (!c.Stopped)
		{
			OvStandBy($"every dirt point and low part left is set aside or had no place to work it from ({c.Dirt.Count} point(s), {c.DirtCool.Count} set aside) - each comes back when something within 2 m of it changes");
		}
		return true;
	}

	// V1: the vein's dirt work is done - no dirt point left on its list
	private static bool OvDirtDone(OvClear c)
	{
		return c != null && c.Dirt.Count == 0;
	}

	// U1 (log bcbf7cf5: 7 points had used their come-back before the last progress, so the stop waited for them forever):
	// a point's one come-back is used only when it failed after the last progress - each progress gives it one more
	private static bool OvOnceUsed(OvClear c, OvDirt d)
	{
		return c.DirtOnce.Contains(d.Key) && (!c.DirtFail.TryGetValue(d.Key, out float f) || f > c.ProgAt);
	}

	// E4: set aside (no clock - OvDirtCooling says when it comes back)
	private void OvDirtCool(OvClear c, OvDirt d, string why)
	{
		float now = Time.time;
		c.DirtCool[d.Key] = now;
		c.DirtFail[d.Key] = now;
		c.DirtWhy[d.Key] = why;
		OvMap m = _vp?.Map;
		if (m == null || !m.Built)
		{
			c.DirtGround[d.Key] = OvDirtGroundGrid(d.P);
		}
		if (d.Holder == this)
		{
			d.Holder = null;
		}
		if (d.Holder2 == this)
		{
			d.Holder2 = null;   // F9
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} (under \"{(d.Part.C != null ? d.Part.C.name : "?")}\") is set aside until something within 2 m of it changes{(c.DirtOnce.Contains(d.Key) ? "" : " or no other dirt point is free")}: {why}");
	}

	// F10: the side it works (1 / -1, 0 = not chosen yet) and the plan that side is for
	private int _mlSide;

	private OvPlan _mlPlan;

	private const float OvMidSlice = 1f;   // F10: the slices along the vein the middle is found in (John OK 11-22 04:29Z)

	private const float OvMidBand = 0.5f;  // F10: a point up to 0.5 m past the line is worked from either side

	// F10: the middle line - the long direction is the main axis of the part centres (flat); in each 1 m slice along it the
	// middle is halfway between the vein's two outer edges across it (the parts' game boxes in that slice); the middles are
	// joined slice to slice, so the line bends with the vein. False = fewer than 2 slices (no line).
	private bool OvMidLine(OvClear c)
	{
		if (c.MidParts == c.ById.Count)
		{
			return c.Mid.Count >= 2;
		}
		c.MidParts = c.ById.Count;
		c.Mid.Clear();
		if (c.ById.Count == 0)
		{
			return false;
		}
		float mx = 0f, mz = 0f;
		foreach (OvPart p in c.ById.Values)
		{
			mx += p.Centre.x;
			mz += p.Centre.z;
		}
		mx /= c.ById.Count;
		mz /= c.ById.Count;
		float sxx = 0f, szz = 0f, sxz = 0f;
		foreach (OvPart p in c.ById.Values)
		{
			float dx = p.Centre.x - mx, dz = p.Centre.z - mz;
			sxx += dx * dx;
			szz += dz * dz;
			sxz += dx * dz;
		}
		float ang = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);
		Vector3 ax = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
		Vector3 pr = new Vector3(-ax.z, 0f, ax.x);
		Vector3 o = new Vector3(mx, 0f, mz);
		float t0 = float.MaxValue, t1 = float.MinValue;
		foreach (OvPart p in c.ById.Values)
		{
			OvBoxSpan(p, o, ax, out float a0, out float a1);
			t0 = Mathf.Min(t0, a0);
			t1 = Mathf.Max(t1, a1);
		}
		for (float t = t0; t < t1; t += OvMidSlice)
		{
			float lo = float.MaxValue, hi = float.MinValue;
			foreach (OvPart p in c.ById.Values)
			{
				OvBoxSpan(p, o, ax, out float a0, out float a1);
				if (a1 < t || a0 > t + OvMidSlice)
				{
					continue;
				}
				OvBoxSpan(p, o, pr, out float b0, out float b1);
				lo = Mathf.Min(lo, b0);
				hi = Mathf.Max(hi, b1);
			}
			if (lo > hi)
			{
				continue;
			}
			c.Mid.Add(o + ax * (t + OvMidSlice * 0.5f) + pr * ((lo + hi) * 0.5f));
		}
		if (CompanionsPlugin.LogNormal && c.Mid.Count >= 2) LogInfo($"The middle line of \"{_vp.Name}\": {c.Mid.Count} points from {MHR_Mover.Fmt(c.Mid[0])} to {MHR_Mover.Fmt(c.Mid[c.Mid.Count - 1])} ({c.ById.Count} parts, {OvMidSlice:F0} m slices)");
		return c.Mid.Count >= 2;
	}

	// F10: a part's game box (flat corners) projected on dir from o
	private static void OvBoxSpan(OvPart p, Vector3 o, Vector3 dir, out float a0, out float a1)
	{
		a0 = float.MaxValue;
		a1 = float.MinValue;
		for (int i = 0; i < 4; i++)
		{
			Vector3 k = new Vector3(p.Centre.x + ((i & 1) == 0 ? -p.Half.x : p.Half.x), 0f, p.Centre.z + ((i & 2) == 0 ? -p.Half.z : p.Half.z));
			float v = Vector3.Dot(k - o, dir);
			a0 = Mathf.Min(a0, v);
			a1 = Mathf.Max(a1, v);
		}
	}

	// F10: the signed flat distance of p from the middle line (its nearest piece; + one side, - the other); dist = how far
	private float OvMidSide(OvClear c, Vector3 p, out float dist)
	{
		dist = float.MaxValue;
		float side = 0f;
		for (int i = 0; i + 1 < c.Mid.Count; i++)
		{
			Vector3 a = c.Mid[i], b = c.Mid[i + 1];
			Vector3 ab = b - a;
			ab.y = 0f;
			Vector3 ap = p - a;
			ap.y = 0f;
			float len = ab.sqrMagnitude;
			float t = len > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / len) : 0f;
			Vector3 q = a + ab * t;
			q.y = 0f;
			Vector3 pq = new Vector3(p.x, 0f, p.z) - new Vector3(q.x, 0f, q.z);
			float d = pq.magnitude;
			if (d < dist)
			{
				dist = d;
				float cr = ab.x * ap.z - ab.z * ap.x;
				side = cr >= 0f ? d : -d;
			}
		}
		return side;
	}

	// F10: p is on its own side of the middle line, or within 0.5 m past it
	private bool OvMidOwn(OvClear c, Vector3 p)
	{
		return _mlSide == 0 || OvMidSide(c, p, out _) * _mlSide >= -OvMidBand;
	}

	// F9: h holds a place on the point and is another companion working it (SV1's test: swinging, or walking and closer)
	private bool OvDirtSlotBusy(OvDirt d, HarvestController h)
	{
		// I2 (John 11-22 Q2: free to take over if no one is actually at it): the holder counts only within 3 m (flat) of it
		return h != null && h != this && h.isActiveAndEnabled && h._drSpot == d && h._vp == _vp && h.OvHolderWorking(h._drWalk) && Flat(h.transform.position, d.P) <= OvDirtHoldNear;
	}

	private const float OvDirtHoldNear = 3f;   // I2

	// W1: the points taken over from this companion (their keys)
	private readonly HashSet<long> _drLost = new HashSet<long>();

	// W1: another companion (alive, on this vein) that has the point as its point now - near it or not; null = none
	private HarvestController OvDirtHasOther(OvDirt d)
	{
		foreach (HarvestController h in new HarvestController[] { d.Holder, d.Holder2 })
		{
			if (h != null && h != this && h.isActiveAndEnabled && h._vp == _vp && h._drSpot != null && h._drSpot.Key == d.Key && h._character != null && !h._character.IsDead())
			{
				return h;
			}
		}
		return null;
	}

	// F9: the other companion working the point (null = none)
	private HarvestController OvDirtOther(OvDirt d)
	{
		return OvDirtSlotBusy(d, d.Holder) ? d.Holder : OvDirtSlotBusy(d, d.Holder2) ? d.Holder2 : null;
	}

	// F9: q is more than a quarter turn (90 degrees, flat) round `about` from `other`
	private static bool OvQuarterOk(Vector3 q, Vector3 about, Vector3 other)
	{
		Vector3 a = q - about, b = other - about;
		a.y = 0f;
		b.y = 0f;
		if (a.sqrMagnitude < 0.0001f || b.sqrMagnitude < 0.0001f)
		{
			return false;
		}
		return Vector3.Dot(a.normalized, b.normalized) < 0f;
	}

	// F9: takes a place on the point - the first one unless another companion is working it there (then the second)
	private void OvDirtClaim(OvDirt d)
	{
		if (d.Holder == this || d.Holder2 == this)
		{
			return;
		}
		if (!OvDirtSlotBusy(d, d.Holder))
		{
			d.Holder = this;
		}
		else
		{
			d.Holder2 = this;
		}
	}

	private void OvDrOff()
	{
		OvLwOff();            // P1 step 2: dropping the dirt work drops a lowering too
		_dgBackNoProg = -1;   // S3
		_drRepos = null;      // E2
		_drDeep = false;      // E3
		if (_drSpot != null && _drSpot.Holder == this)
		{
			_drSpot.Holder = null;
		}
		if (_drSpot != null && _drSpot.Holder2 == this)
		{
			_drSpot.Holder2 = null;   // F9
		}
		_drSpot = null;
		_drWalk = false;
		if (_drForThing)
		{
			_drForThing = false;
			OvRampThingDrop();
		}
		if (_dgDirt)
		{
			OvDigOff();
			_dgDirt = false;
			_dgClear = false;
		}
	}

	private void OvDrFail(OvClear c, string why)
	{
		if (_drForThing)
		{
			// P2: the thing waits 60 s (FF1's cooldown), any companion may try it again then - in the trench, a way out
			// first (Z2, once per try)
			GameObject t = _rpThing;
			int tm = _rpThingMode;
			string tt = _rpThingType;
			OvSpot ts = _rpThingSpot;
			if (CompanionsPlugin.LogNormal) LogInfo($"The ground at the base of \"{(t != null ? t.name : "?")}\": {why}");
			OvDrOff();
			if (OvThingWayOut(t, tm, tt, ts, "the ground at its base: " + why))
			{
				return;
			}
			OvPitLeftNote(t);
			return;
		}
		OvDirt d = _drSpot;
		OvDrOff();
		if (d != null)
		{
			OvDirtCool(c, d, why);
		}
	}

	// P2: the thing has come down into reach (or the swing reached it): back to the Wood / Stone flow, the same try (it
	// won't start another base dig: the hand clock decides from here)
	private void OvDrThingBack(string how)
	{
		GameObject t = _rpThing;
		int mode = _rpThingMode;
		string type = _rpThingType;
		OvSpot sp = _rpThingSpot;
		OvDrOff();
		if (t == null || !t.activeInHierarchy || _vp == null)
		{
			return;
		}
		_hdRampNext = true;
		OvTake(t, mode, type, sp, canSkip: false, how);
	}

	// K8 (test 18: the log over the pit - the base dig ended at once and nothing cut a way up to it): the ground at the
	// thing's base is dug down (P2). If the thing's box bottom is still more than 0.8 m above its feet, a way up is cut with
	// the pickaxe to the ground beside the thing (the Y6 way up: OvRampBegin toward a point 0.5 m short of the thing's box
	// on its side, done when its feet are within a step of the box bottom), then the thing goes back to the Wood / Stone
	// flow ("Up beside it", the same try: no second base dig or way up). Once per try. If the way up gives up, the thing
	// waits 60 s (OvPitLeftNote, FF1's cooldown). Otherwise it goes straight back as before.
	private void OvDrThingUp()
	{
		GameObject t = _rpThing;
		Vector3 me = base.transform.position;
		if (t == null || !t.activeInHierarchy || _vp == null || !OvThingBox(t, out Bounds b) || b.min.y <= me.y + OvThingLow)
		{
			OvDrThingBack("The ground at its base is dug down");
			return;
		}
		// the dirt dig ends without letting the thing go (its claim and hand-back details stay for the way up)
		_drForThing = false;
		OvDrOff();
		Vector3 np = b.ClosestPoint(new Vector3(me.x, b.center.y, me.z));
		Vector3 dir = np - me;
		dir.y = 0f;
		dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : base.transform.forward;
		Vector3 goal = np - dir * 0.5f;
		goal.y = GroundAt(goal);
		_rpThingBase = b.min.y;
		_rpThingUp = true;
		OvRampBegin(goal, null, outOfTrench: false, $"the ground at the base of \"{t.name}\" is dug down and it is still {b.min.y - me.y:F1} m above its feet - a way up beside it");
	}

	// ---------- E2 / E3 (silver veins, John 2026-11-21): reposition, then mine deeper, then set aside ----------
	private const int OvDirtNoProg = 2;        // rule 5b: swings in a row with no work before it moves (the stone job's 2)

	private const float OvFeetDig = 0.5f;      // rule 5c: a dig within 0.5 m (flat) of its own feet

	private const float OvDeepMax = 2f;        // rule 6: never lower than 2 m under the bottom of the point's part

	// F7 (a): the ground at its feet and 4 points at its body radius (forward, back, left, right)
	private void OvFeetRead(float[] into, out Vector3 at)
	{
		at = base.transform.position;
		float r = OvBodyRadius();
		into[0] = GroundAt(at);
		into[1] = GroundAt(at + Vector3.forward * r);
		into[2] = GroundAt(at + Vector3.back * r);
		into[3] = GroundAt(at + Vector3.left * r);
		into[4] = GroundAt(at + Vector3.right * r);
	}

	// F7 (a): any of those went down 0.05 m (OvDigGain) or more since they were read
	private bool OvFeetWentDown(float[] was, Vector3 at)
	{
		float r = OvBodyRadius();
		Vector3[] pts = { at, at + Vector3.forward * r, at + Vector3.back * r, at + Vector3.left * r, at + Vector3.right * r };
		for (int i = 0; i < 5; i++)
		{
			if (was[i] - GroundAt(pts[i]) >= OvDigGain)
			{
				return true;
			}
		}
		return false;
	}

	// ---------- G1: the pickaxe's own dig, read from the game (Attack.SpawnOnHitTerrain's prefab, its TerrainOp) ----------
	private sealed class OvDigOp
	{
		public float R, Delta, Power;
		public Vector3 Off;      // the TerrainOp's place in the prefab (the prefab is placed facing the swinger)
	}

	private static readonly Dictionary<string, OvDigOp> s_digOp = new Dictionary<string, OvDigOp>();

	// G1: the pickaxe dig's raise / lower part (radius, delta, power) from the game's TerrainOp. null = no TerrainOp or
	// its raise / lower is off (the own-feet check is off). Level and smooth: if a mod turns them on, only the raise /
	// lower part is predicted (logged once)
	private OvDigOp OvDigOpFor(ItemDrop.ItemData pick)
	{
		GameObject pf = pick?.m_shared?.m_spawnOnHitTerrain;
		if (pf == null)
		{
			return null;
		}
		if (s_digOp.TryGetValue(pf.name, out OvDigOp d))
		{
			return d;
		}
		d = null;
		TerrainOp op = pf.GetComponentInChildren<TerrainOp>();
		if (op != null && op.m_settings != null)
		{
			TerrainOp.Settings st = op.m_settings;
			if (st.m_raise && st.m_raiseRadius > 0f)
			{
				d = new OvDigOp { R = st.m_raiseRadius, Delta = st.m_raiseDelta, Power = st.m_raisePower, Off = pf.transform.InverseTransformPoint(op.transform.position) };
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"The pickaxe's dig \"{pf.name}\" (the game's TerrainOp): raise/lower {(st.m_raise ? $"radius {st.m_raiseRadius:F2} m, delta {st.m_raiseDelta:F2} m, power {st.m_raisePower:F2}" : "off")}, level {(st.m_level ? "ON (not predicted)" : "off")}, smooth {(st.m_smooth ? "ON (not predicted)" : "off")}, offset {(d != null ? MHR_Mover.Fmt(d.Off) : "-")} - the own-feet check is {(d != null ? "on" : "off")}");
		}
		else if (CompanionsPlugin.LogNormal)
		{
			LogInfo($"The pickaxe's dig \"{pf.name}\" has no TerrainOp - the own-feet check (G1) is off");
		}
		s_digOp[pf.name] = d;
		return d;
	}

	// G1 (John 11-22: "Yes"): standing at `stand` and swinging toward `aim`, the game's dig at `dig` (the first ray's
	// ground hit) would lower the ground at its feet: TerrainComp.RaiseTerrain - every vertex within the radius of the
	// dig's vertex goes to dig height + delta x (1 - distance / radius)^power, only when that is lower than it is now.
	// The 5 feet points of F7 (a) (its feet, and its body radius forward / back / left / right), the 4 vertices round
	// each: true when any of them would go down 0.05 m (OvDigGain) or more
	private bool OvDigLowersFeet(OvDigOp d, Vector3 stand, Vector3 aim, Vector3 dig)
	{
		Vector3 flat = aim - stand;
		flat.y = 0f;
		Quaternion yaw = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : Quaternion.identity;
		Vector3 at = dig + yaw * d.Off;
		float r = OvBodyRadius();
		Vector3[] pts = { stand, stand + Vector3.forward * r, stand + Vector3.back * r, stand + Vector3.left * r, stand + Vector3.right * r };
		foreach (Vector3 pt in pts)
		{
			Heightmap hm = Heightmap.FindHeightmap(pt);
			if (hm == null)
			{
				continue;
			}
			float sc = hm.m_scale;
			int half = hm.m_width / 2;
			Vector3 hp = hm.transform.position;
			hm.WorldToVertex(at, out int cx, out int cy);   // the game's own centre vertex (rounded)
			float rv = d.R / sc;
			Vector3 lp = pt - hp;
			int x0 = Mathf.FloorToInt(lp.x / sc) + half, y0 = Mathf.FloorToInt(lp.z / sc) + half;
			for (int vy = y0; vy <= y0 + 1; vy++)
			{
				for (int vx = x0; vx <= x0 + 1; vx++)
				{
					if (vx < 0 || vy < 0 || vx > hm.m_width || vy > hm.m_width)
					{
						continue;
					}
					float dist = Vector2.Distance(new Vector2(cx, cy), new Vector2(vx, vy));
					if (dist > rv)
					{
						continue;
					}
					float fall = 1f;
					if (d.Power > 0f)
					{
						fall = 1f - dist / rv;
						if (d.Power != 1f)
						{
							fall = Mathf.Pow(fall, d.Power);
						}
					}
					float h = hp.y + hm.GetHeight(vx, vy);
					float to = at.y + d.Delta * fall;
					if (d.Delta < 0f && h - to >= OvDigGain)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private const float OvDeepStep = 0.5f;     // rule 6: a place is dug down 0.5 m at a time

	private const int OvDeepPlaces = 3;        // rule 6: at most 3 tried places per point are dug deeper

	private string _drRepos;     // rule 5 / E2: why it moves to another place (set by the swing or the check before it)

	private bool _drDeep;        // E3: digging the place it will stand on deeper

	private Vector3 _drDeepPt;   // E3: that place

	private float _drDeepGoal;   // E3: the ground there this step digs to

	// E2: the place it was on goes on the point's tried list; the next place (rules 2-3) that passes E1 and isn't tried;
	// none: E3 (mine deeper); nothing there either: the point is set aside (E4)
	private void OvDrRepos(OvClear c, OvDirt d, string why)
	{
		if (!c.DirtTried.TryGetValue(d.Key, out List<Vector3> tl))
		{
			tl = new List<Vector3>();
			c.DirtTried[d.Key] = tl;
		}
		if (!OvTriedHas(tl, _drStand))
		{
			tl.Add(_drStand);
		}
		_dgBackNoProg = -1;   // a new place counts its own swings
		int r = OvStandFor(c, new Vector3(d.P.x, GroundAt(d.P), d.P.z), null, out Vector3 st, out string w, d);
		if (r == 0)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Moves for the dirt at {MHR_Mover.Fmt(d.P)}: {why} - to {MHR_Mover.Fmt(st)} ({tl.Count} place(s) tried)");
			OvDrGoTo(st);
			return;
		}
		if (OvDrDeepNext(c, d, why))
		{
			return;
		}
		OvDrFail(c, $"{why}; {w}; no place could be dug deeper to work it from");
	}

	private void OvDrGoTo(Vector3 st)
	{
		_drStand = st;
		_drWalk = true;
		_drMpTried = _drMpOn = false;
		_moveTimer = 0f;
		_ai?.StopMoving();
	}

	// the lowest the ground may go at q for point d: 2 m under its part's bottom, never past the game's 8 m limit
	private float OvDeepLowest(OvDirt d, Vector3 q)
	{
		return Mathf.Max(d.Part != null ? OvPartBottom(d.Part) - OvDeepMax : float.MinValue, OvFloor(q));
	}

	// E3 (rule 6, John: "If they need to mine deeper to clear an area that is ok"): the next tried place within its swing
	// of the point (nearest the point first, at most 3 per point) that can go 0.5 m lower: dug down from beside it (a
	// place found by the same rules, E1 toward that place, not within 0.5 m of it). True = started.
	private bool OvDrDeepNext(OvClear c, OvDirt d, string why)
	{
		if (!c.DirtTried.TryGetValue(d.Key, out List<Vector3> tl) || tl.Count == 0)
		{
			return false;
		}
		if (!c.DirtDeep.TryGetValue(d.Key, out List<Vector3> done))
		{
			done = new List<Vector3>();
			c.DirtDeep[d.Key] = done;
		}
		float range = FindBestTool(2)?.m_shared?.m_attack != null ? FindBestTool(2).m_shared.m_attack.m_attackRange : 2f;
		float reach = Mathf.Max(0.8f, range - OvStandAt);
		List<Vector3> pick = new List<Vector3>();
		foreach (Vector3 t in tl)
		{
			if (Flat(t, d.P) <= reach + 0.01f && !OvTriedHas(done, t))
			{
				pick.Add(t);
			}
		}
		pick.Sort((Vector3 a, Vector3 b) => Flat(a, d.P).CompareTo(Flat(b, d.P)));
		foreach (Vector3 t in pick)
		{
			if (done.Count >= OvDeepPlaces)
			{
				return false;
			}
			done.Add(t);
			Vector3 q = new Vector3(t.x, GroundAt(t), t.z);
			float lowest = OvDeepLowest(d, q);
			if (q.y <= lowest + OvDigGain)
			{
				continue;
			}
			if (OvStandFor(c, q, null, out Vector3 st, out string w, null, q) != 0)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"Can't dig {MHR_Mover.Fmt(q)} deeper for the dirt at {MHR_Mover.Fmt(d.P)}: {w}");
				continue;
			}
			_drDeep = true;
			_drDeepPt = q;
			_drDeepGoal = Mathf.Max(q.y - OvDeepStep, lowest);
			if (CompanionsPlugin.LogNormal) LogInfo($"No place left to work the dirt at {MHR_Mover.Fmt(d.P)} from ({why}) - digging {MHR_Mover.Fmt(q)} down to {_drDeepGoal:F2} from {MHR_Mover.Fmt(st)} to work it from there");
			OvDrGoTo(st);
			return true;
		}
		return false;
	}

	// E3: a deeper dig of its place ended - reached = 0.5 m lower: E1 from there for real (passes: it works the point from
	// there; fails: another 0.5 m while above the lowest allowed); else that place is done with: the next place, or set aside
	private void OvDrDeepStep(OvClear c, OvDirt d, bool reached, string why)
	{
		_drDeep = false;
		Vector3 q = new Vector3(_drDeepPt.x, GroundAt(_drDeepPt), _drDeepPt.z);
		if (reached)
		{
			Vector3 aim = new Vector3(d.P.x, GroundAt(d.P), d.P.z);
			if (OvSwingClean(FindBestTool(2)?.m_shared?.m_attack, q, aim))
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"Dug {MHR_Mover.Fmt(q)} down - the swing from there meets the dirt at {MHR_Mover.Fmt(d.P)} without the vein: working it from there");
				OvDrGoTo(q);
				return;
			}
			float lowest = OvDeepLowest(d, q);
			if (q.y > lowest + OvDigGain)
			{
				_drDeep = true;
				_drDeepPt = q;
				_drDeepGoal = Mathf.Max(q.y - OvDeepStep, lowest);
				if (CompanionsPlugin.LogNormal) LogInfo($"Dug {MHR_Mover.Fmt(q)} down - the swing from there still meets the vein: down to {_drDeepGoal:F2} next");
				return;
			}
			why = $"down to its lowest allowed ({lowest:F2}) and the swing from there still meets the vein";
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Stops digging {MHR_Mover.Fmt(q)} deeper: {why}");
		if (OvDrDeepNext(c, d, why))
		{
			return;
		}
		OvDrFail(c, "no place to work it from without the swing meeting the vein, even dug deeper");
	}

	// one frame of digging a dirt point (from OvClearTick while _drSpot is set)
	private void OvDrTick(OvClear c)
	{
		OvDirt d = _drSpot;
		if (_drForThing)
		{
			if (_rpThing == null || !_rpThing.activeInHierarchy)
			{
				if (CompanionsPlugin.LogNormal) LogInfo("The thing it was digging under is gone");
				OvDrOff();
				return;
			}
			// P2: at its place at the bottom and the thing is down to 0.8 m above its feet: the axe / pickaxe now
			if (!_drWalk && !_dgActive && OvThingBox(_rpThing, out Bounds tb) && tb.min.y <= base.transform.position.y + OvThingLow)
			{
				OvDrThingBack("Down within reach");
				return;
			}
		}
		else if (!c.Dirt.Contains(d))
		{
			// the list was made again without it: the ground there is down (or its part is gone)
			if (CompanionsPlugin.LogDetailed) Log($"The dirt at {MHR_Mover.Fmt(d.P)} is off the list now");
			OvDrOff();
			return;
		}
		else if (d.Holder != this && d.Holder2 != this)
		{
			// SV1: another companion took the point over while this one wasn't working it (stuck on the way): let it go
			// F9: it is in neither of the point's two places
			if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} was taken over by {(d.Holder != null ? d.Holder.OvName() : d.Holder2 != null ? d.Holder2.OvName() : "another companion")} (this one was stuck on the way to it) - picking again");
			_drLost.Add(d.Key);   // W1: not taken back while another companion has it
			_ai?.StopMoving();
			OvDrOff();
			return;
		}
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (_dgActive && _dgDirt && !_dgSwung && OvDrOffPlace(me, OvPlaceOff))
		{
			// S3: moved off its place (a fight, a push) - back to it first, its swing count kept
			OvDigPause("the dirt");
			_drWalk = true;
			_moveTimer = 0f;
			return;
		}
		if (_dgActive && _dgDirt)
		{
			OvDigTick();
			if (_dgActive)
			{
				return;
			}
			bool reached = _dgReached, limit = _dgLimit;
			_dgDirt = false;
			_dgClear = false;
			if (_state != HarvestState.Vein)
			{
				return;   // a swing hit a thing and handed it to the Wood / Stone flow: the point stays its own, no fail
			}
			if (reached && _drForThing)
			{
				OvDrThingUp();
				return;
			}
			if (_drDeep)
			{
				// E3: the dig of the place it stands to work the point from (reached = 0.5 m deeper; anything else = this
				// place is done with)
				string dr = _drRepos;
				_drRepos = null;
				OvDrDeepStep(c, d, reached, dr ?? (limit ? "the game's 8 m dig limit" : "its swings did no work"));
				return;
			}
			if (reached)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} is down to {GroundAt(d.P):F2} (under \"{d.Part.C.name}\"'s bottom {OvPartBottom(d.Part):F2})");
				OvDrOff();
				c.DirtAt = Mathf.Min(c.DirtAt, Time.time - OvDirtEvery + 0.5f);   // the list again in 0.5 s
				return;
			}
			if (_drRepos != null && !_drForThing)
			{
				// E2 / rule 5 (silver): a swing hit the vein, dug at its own feet, or 2 swings did no work - another place
				string why = _drRepos;
				_drRepos = null;
				OvDrRepos(c, d, why);
				return;
			}
			OvDrFail(c, limit ? "it is at the game's 8 m dig limit" : $"{OvDirtNoProg} swings didn't lower the ground there");
			return;
		}
		if (_drWalk && !_drForThing && GroundAt(d.P) <= d.Goal + OvDigGain)
		{
			// SV2: on the way there the ground at the point went down to its goal (another companion's dig, or the
			// player's): the point is done - the same test as at the point, run during the walk too
			if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} is down to {GroundAt(d.P):F2} already (goal {d.Goal:F2}) - dug while it was on the way");
			_ai?.StopMoving();
			OvDrOff();
			c.DirtAt = Mathf.Min(c.DirtAt, Time.time - OvDirtEvery + 0.5f);   // the list again in 0.5 s
			return;
		}
		if (_drWalk)
		{
			float flat = Flat(me, _drStand);
			// HH2: there = within 0.5 m (flat) and on that ground - feet no more than a step above the ground there now (standing on a part above the
			// place is not there: it goes on, and the mover's give-up (Z4) ends it)
			if (OvDrOffPlace(me))
			{
				float up = _drStand.y - me.y;
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				bool tooHigh = up >= OvRampRatio * hh && flat <= OvRampReach;
				// MP4: the map's route there blocked by one step too high - the way up at that step's foot
				if (OvRtWayUpWalk(_drStand, ref _drMpTried, ref _drMpOn, ref _drMpFoot, ref _drMpTop, dt, "the place to dig the dirt from"))
				{
					return;
				}
				// P3: the place to stand is the bottom of the pit - no steps cut toward it (HH3 removed)
				if (tooHigh || OvWalkGaveUp(dt, _drStand))
				{
					string nw = $"couldn't get to {MHR_Mover.Fmt(_drStand)} to dig it ({flat:F1} m away, {up:F1} m up)";
					if (!_drForThing && OvIsSilver(_vp))
					{
						// SV4 (silver, log 07ee23fb: Gunter was set aside 0.5 m from his place): the place goes on the point's
						// tried list, then a better angle (another place within 3 m), then deeper (E3), and only then set aside
						_ai?.StopMoving();
						if (_drDeep)
						{
							OvDrDeepStep(c, d, false, nw);
						}
						else
						{
							OvDrRepos(c, d, nw);
						}
						return;
					}
					OvDrFail(c, nw);
					return;
				}
				OvWalk(_drStand, 0.25f, flat > 8f, dt);
				return;
			}
			_drWalk = false;
			_ai?.StopMoving();
		}
		else if (Flat(me, _drStand) > 1.2f || me.y - GroundAt(_drStand) > OvStepUp + 0.5f)
		{
			_drWalk = true;   // pushed off its place (or back from clearing a thing): back to it
			_moveTimer = 0f;
			return;
		}
		float g = GroundAt(d.P);
		if (g <= d.Goal + OvDigGain && _drForThing)
		{
			OvDrThingUp();
			return;
		}
		if (g <= d.Goal + OvDigGain)
		{
			OvDrOff();
			c.DirtAt = Mathf.Min(c.DirtAt, Time.time - OvDirtEvery + 0.5f);
			return;
		}
		if (_drDeep)
		{
			// E3: digging the place it will stand on 0.5 m deeper (from beside it), as a dirt point of its own
			float dg = GroundAt(_drDeepPt);
			if (dg <= _drDeepGoal + OvDigGain)
			{
				OvDrDeepStep(c, d, true, null);
				return;
			}
			OvDigBegin(new Vector3(_drDeepPt.x, dg, _drDeepPt.z), null, ramp: false);
			OvDigResume();
			_dgClear = true;
			_dgDirt = true;
			_dgGoalY = _drDeepGoal;
			return;
		}
		OvDigBegin(new Vector3(d.P.x, g, d.P.z), null, ramp: false);
		OvDigResume();
		_dgClear = true;
		_dgDirt = true;
		_dgGoalY = d.Goal;
		if (_drForThing)
		{
			_dgWalkThing = true;   // P2: the thing beside the point is the one it digs under - not handed over before the swing
		}
	}

	// P3 (John, 2026-11-19: "It should just be stand in the bottom of the trench or pit, whatever level that is at"): the
	// place to stand for work at `aim` - pit ground (P1) on a 0.5 m grid within its swing (the pickaxe's range less the
	// 0.5 m the walk stops short: 1.3 m) of the dirt point; the LOWEST of them (within 0.3 m of the lowest: the nearest to
	// the companion). A part: OvStandForPart (M1). 0 = found, 1 = a part from a ladder, 2 = none.
	private int OvStandFor(OvClear c, Vector3 aim, OvPart part, out Vector3 stand, out string why, OvDirt dirt = null, Vector3? notNear = null)
	{
		if (part != null)
		{
			return OvStandForPart(part, out stand, out why);
		}
		stand = Vector3.zero;
		why = null;
		if (dirt != null)
		{
			_u2On = false;   // U2: set again only by the last-resort search below
		}
		Vector3 me = base.transform.position;
		float range = FindBestTool(2)?.m_shared?.m_attack != null ? FindBestTool(2).m_shared.m_attack.m_attackRange : 2f;
		float reach = Mathf.Max(0.8f, range - OvStandAt);
		List<Vector3> cand = new List<Vector3>();
		float low = float.MaxValue;
		int noStand = 0;
		string nearSaid = null;   // K1: what the map says on the nearest square with no place to stand (log only)
		float nearD = float.MaxValue;
		for (float x = Mathf.Floor((aim.x - reach) / OvDirtGrid) * OvDirtGrid; x <= aim.x + reach + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor((aim.z - reach) / OvDirtGrid) * OvDirtGrid; z <= aim.z + reach + 0.001f; z += OvDirtGrid)
			{
				Vector3 pt = new Vector3(x, 0f, z);
				// G2: not within 1.2 m of another companion's place or walk goal (the board)
				if (Flat(pt, aim) > reach + 0.01f || !OvInPitPt(pt) || OvBoardTaken(pt) != null || (notNear.HasValue && Flat(pt, notNear.Value) <= OvFeetDig))
				{
					continue;
				}
				pt.y = GroundAt(pt);
				// R2.2: the map says no place to stand there (stone, rock, a part or a thing on it, or too little room): dropped;
				// a built floor: its feet go on the floor; don't know: as before
				int ms = OvMapStand(pt, out float mf, out string msWhy);
				if (ms == 0)
				{
					noStand++;
					if (Flat(pt, aim) < nearD)
					{
						nearD = Flat(pt, aim);
						nearSaid = $"the nearest, {MHR_Mover.Fmt(pt)}: {msWhy}";
					}
					continue;
				}
				if (ms == 1)
				{
					pt.y = mf;
				}
				cand.Add(pt);
				low = Mathf.Min(low, pt.y);
			}
		}
		// F9 (John 11-21 23:30Z: "stand places > a quarter turn apart around the item"): with another companion on the point,
		// only a place more than 90 degrees round the point (flat, about its centre) from that one's place
		HarvestController opp = dirt != null ? OvDirtOther(dirt) : null;
		int quarter = 0;
		if (opp != null)
		{
			quarter = cand.RemoveAll((Vector3 q) => !OvQuarterOk(q, aim, opp._drStand));
		}
		string noStandSaid = noStand > 0 ? $" ({noStand} with no place to stand by the pit map{(nearSaid != null ? "; " + nearSaid : "")})" : "";
		// K1 (John 11-22 19:35Z: "They should be digging the surrounding area lower so they can reach those spots"): on
		// silver, none within its swing goes on to the D1.3 dig-in from the trench (below) instead of stopping here
		if (cand.Count == 0 && !OvIsSilver(_vp))
		{
			why = $"no pit ground within its swing ({reach:F1} m) of it that isn't within 1.2 m of another companion{noStandSaid}{(quarter > 0 ? $" ({quarter} not a quarter turn round it from {opp.OvName()})" : "")}";
			return 2;
		}
		// MP3 (the map-path list, John 2026-11-21): nearest first among the lowest; one the map says there is no way to (not
		// just one step too high - MP4 cuts that on the walk) is dropped; at most 5 asked, the rest taken as they are
		// D1 (John, 2026-11-21, silver veins only: "avoid the ore other than to clear dirt off it"): on a silver vein the place
		// must be one where the game's swing from it meets the dirt (or anything but the vein) before the vein's rock.
		// D1.2 (John 20:23Z: "They should be working from the trench not from the top or the ore"): and only a place on the
		// trench floor - its ground within 0.7 m of the ground at the trench spot nearest it - never the mound, the top or
		// the ore. D1.3: none within its swing of the point = it digs in from the trench: the trench-floor place within 3 m
		// of the point nearest it (the swing check too), its swings cut the bank toward the point (P4's bank cut is work)
		// E1 (John 2026-11-21): the check is OvSwingClean - no ray meets the vein first and the swing's first hit is the
		// ground, at any height. E2: a place on this point's tried list (one it repositioned away from) isn't taken again.
		bool silver = OvIsSilver(_vp);
		Attack sa = silver ? FindBestTool(2)?.m_shared?.m_attack : null;
		// F10: on silver, a stand place on its own side of the middle line only
		bool midOn = silver && dirt != null && _mlPlan == _vp && _mlSide != 0 && OvMidLine(c);
		int otherSide = 0;
		if (midOn)
		{
			otherSide = cand.RemoveAll((Vector3 q) => OvMidSide(c, q, out _) * _mlSide < 0f);
		}
		int oreFirst = 0, offFloor = 0, triedN = 0;
		bool digIn = false;
		// J1 (John 11-22 18:41Z: dirt above the vein may be dug from on the dirt; "Dirt that is on top of the ore should get
		// cleared as they clear under it"): while the point's ground is above its part's top, a place up on the dirt is
		// allowed too (every other check stays); once it is down to the part's top, the trench-floor rule again
		bool dirtAbove = silver && dirt != null && dirt.Part != null && GroundAt(dirt.P) > dirt.Part.Centre.y + dirt.Part.Half.y;
		// F7 (a): every vein - a place it repositioned away from (it dug its own floor there) isn't taken again for this point
		List<Vector3> tried = dirt != null && c.DirtTried.TryGetValue(dirt.Key, out List<Vector3> tl) ? tl : null;
		// F7 (c) / G1, silver: a place already more than 2 m under the point's part isn't taken; nor one where the game's
		// dig on that swing (its TerrainOp, OvDigLowersFeet) would lower the ground at its feet, or lands under 2 m below
		// the part
		float tooLowY = silver && dirt != null && dirt.Part != null ? OvPartBottom(dirt.Part) - OvDeepMax : float.MinValue;
		OvDigOp dop = silver ? OvDigOpFor(FindBestTool(2)) : null;   // G1
		int tooLow = 0, digsSelf = 0, digLow = 0;
		if (silver)
		{
			List<Vector3> ok = new List<Vector3>();
			foreach (Vector3 q in cand)
			{
				if (ok.Count + oreFirst >= OvSwTestMax)
				{
					break;
				}
				if (OvTriedHas(tried, q))
				{
					triedN++;
					continue;
				}
				if (q.y < tooLowY)
				{
					tooLow++;
					continue;
				}
				if (!dirtAbove && !OvOnTrenchFloor(q))   // J1
				{
					offFloor++;
					continue;
				}
				if (!OvSwingClean(sa, q, aim, out Vector3 dg1))
				{
					oreFirst++;
					continue;
				}
				if (dop != null && dg1.y < tooLowY)
				{
					digLow++;
					continue;
				}
				if (dop != null && OvDigLowersFeet(dop, q, aim, dg1))
				{
					digsSelf++;   // G1: the game's dig would lower the ground at its feet
					continue;
				}
				ok.Add(q);
			}
			if (ok.Count == 0)
			{
				// D1.3: in from the trench
				for (float x = Mathf.Floor((aim.x - OvDigInFar) / OvDirtGrid) * OvDirtGrid; x <= aim.x + OvDigInFar + 0.001f; x += OvDirtGrid)
				{
					for (float z = Mathf.Floor((aim.z - OvDigInFar) / OvDirtGrid) * OvDirtGrid; z <= aim.z + OvDigInFar + 0.001f; z += OvDirtGrid)
					{
						Vector3 q = new Vector3(x, 0f, z);
						if (Flat(q, aim) <= reach + 0.01f || Flat(q, aim) > OvDigInFar + 0.01f || !OvInPitPt(q) || OvBoardTaken(q) != null || (notNear.HasValue && Flat(q, notNear.Value) <= OvFeetDig))
						{
							continue;
						}
						q.y = GroundAt(q);
						int ms = OvMapStand(q, out float mf, out _);
						if (ms == 0)
						{
							continue;
						}
						if (ms == 1)
						{
							q.y = mf;
						}
						if (!dirtAbove && !OvOnTrenchFloor(q))   // J1
						{
							continue;
						}
						if (OvTriedHas(tried, q))
						{
							triedN++;
							continue;
						}
						// K1 (Q-K1, John 11-22 19:43Z: yes): a dig-in place may have its feet more than 2 m under the part (its
						// swings cut the bank toward the point); where the dig lands stays checked below (digLow)
						if (opp != null && !OvQuarterOk(q, aim, opp._drStand))
						{
							continue;   // F9
						}
						if (midOn && OvMidSide(c, q, out _) * _mlSide < 0f)
						{
							otherSide++;
							continue;   // F10
						}
						ok.Add(q);
					}
				}
				ok.Sort((Vector3 p1, Vector3 p2) => Flat(p1, aim).CompareTo(Flat(p2, aim)));
				List<Vector3> fine = new List<Vector3>();
				foreach (Vector3 q in ok)
				{
					if (fine.Count + oreFirst >= OvSwTestMax)
					{
						break;
					}
					if (!OvSwingClean(sa, q, aim, out Vector3 dg2))
					{
						oreFirst++;
						continue;
					}
					if (dop != null && dg2.y < tooLowY)
					{
						digLow++;
						continue;
					}
					if (dop != null && OvDigLowersFeet(dop, q, aim, dg2))
					{
						digsSelf++;   // G1
						continue;
					}
					fine.Add(q);
					if (fine.Count >= 3)
					{
						break;
					}
				}
				ok = fine;
				digIn = ok.Count > 0;
			}
			cand = ok;
		}
		else
		{
			if (tried != null)
			{
				// F7 (a): its tried places out first, then the lowest of what is left
				cand.RemoveAll((Vector3 q) => OvTriedHas(tried, q));
				low = float.MaxValue;
				foreach (Vector3 q in cand)
				{
					low = Mathf.Min(low, q.y);
				}
			}
			cand.RemoveAll((Vector3 q) => q.y > low + 0.3f);
		}
		if (!digIn)
		{
			cand.Sort((Vector3 p1, Vector3 p2) => (Flat(me, p1) + Mathf.Abs(me.y - p1.y)).CompareTo(Flat(me, p2) + Mathf.Abs(me.y - p2.y)));
		}
		int noWay = 0;
		foreach (Vector3 pt in cand)
		{
			if (noWay < OvRtProbeMax && OvRtNoWay(pt, out _))
			{
				noWay++;
				continue;
			}
			stand = pt;
			if (digIn && CompanionsPlugin.LogNormal) LogInfo($"No trench-floor place within its swing of the dirt at {MHR_Mover.Fmt(aim)} - digging in toward it from the trench at {MHR_Mover.Fmt(pt)} ({Flat(pt, aim):F1} m from it)");
			return 0;
		}
		// U2 (John 11-23 00:19-00:55Z: to reach the last dirt the trench-floor, own-feet, 2 m-under and middle-line rules give
		// way; strike the base of the dirt nearby to get closer, never a pit at its own feet, never deeper than needed)
		if (silver && dirt != null && dirt.Part != null && cand.Count == 0 && OvU2Stand(c, aim, dirt, sa, reach, tried, opp, notNear, out Vector3 u2))
		{
			stand = u2;
			_u2On = true;
			if (CompanionsPlugin.LogNormal) LogInfo($"No place to work the dirt at {MHR_Mover.Fmt(aim)} from by the usual rules - the last-resort place {MHR_Mover.Fmt(u2)} ({Flat(u2, aim):F1} m from it): the trench floor, own-feet, 2 m-under and middle-line rules give way; it strikes the dirt nearby to get closer (U2)");
			return 0;
		}
		string sideNote = otherSide > 0 ? $" ({otherSide} place(s) on the other side of the middle line left out)" : "";
		// G1 (c): every count on its own (one count no longer hides the others)
		why = (silver && cand.Count == 0 ? $"no place within {OvDigInFar:F0} m of it to work it from: {oreFirst} where the swing meets the vein first, {digsSelf} where the game's dig would lower the ground at its own feet, {tooLow} more than 2 m under the part, {digLow} where the dig lands more than 2 m under the part, {offFloor} up on the dirt (not the trench floor), {triedN} tried already{noStandSaid}" : $"no way there by the pit map to the {noWay} lowest place(s) within its swing of it") + sideNote;
		return 2;
	}

	// U2: true while a dirt round works from a last-resort place (OvDrTick's swing result reads it)
	private bool _u2On;

	// U2: places within 3 m of the point where the body fits (pit map, head room), on the pit's ground, not another
	// companion's place, a quarter turn from a second companion on the point, not tried already, either side of the middle
	// line, whose swing toward the point meets no part of the vein first and meets the ground first - the nearest to the point
	private bool OvU2Stand(OvClear c, Vector3 aim, OvDirt dirt, Attack sa, float reach, List<Vector3> tried, HarvestController opp, Vector3? notNear, out Vector3 stand)
	{
		stand = Vector3.zero;
		float partBottom = OvPartBottom(dirt.Part);   // W2
		List<Vector3> ok = new List<Vector3>();
		for (float x = Mathf.Floor((aim.x - OvDigInFar) / OvDirtGrid) * OvDirtGrid; x <= aim.x + OvDigInFar + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor((aim.z - OvDigInFar) / OvDirtGrid) * OvDirtGrid; z <= aim.z + OvDigInFar + 0.001f; z += OvDirtGrid)
			{
				Vector3 q = new Vector3(x, 0f, z);
				if (Flat(q, aim) > OvDigInFar + 0.01f || !OvInPitPt(q) || OvBoardTaken(q) != null || (notNear.HasValue && Flat(q, notNear.Value) <= OvFeetDig))
				{
					continue;
				}
				// W2 (John 11-23 05:12Z: "Shouldnt it come from the trench?"): the feet on the ground at the place itself (a
				// built floor only when the map says the square is one), no higher than the bottom of the part the dirt holds up
				q.y = GroundAt(q);
				int ms = OvMapStand(q, out float mf, out _);
				if (ms == 0)
				{
					continue;
				}
				if (ms == 1)
				{
					OvMap um = _vp.Map;
					int ui = OvMapIndex(um, q);
					if (ui >= 0 && mf > um.Ground[ui] + 0.05f)
					{
						q.y = mf;   // a built floor
					}
				}
				if (q.y > partBottom)
				{
					continue;
				}
				if (OvTriedHas(tried, q) || (opp != null && !OvQuarterOk(q, aim, opp._drStand)))
				{
					continue;
				}
				ok.Add(q);
			}
		}
		ok.Sort((Vector3 p1, Vector3 p2) => Flat(p1, aim).CompareTo(Flat(p2, aim)));
		int n = 0, noWay = 0;
		foreach (Vector3 q in ok)
		{
			if (n++ >= OvSwTestMax)
			{
				break;
			}
			if (!OvSwingClean(sa, q, aim))
			{
				continue;
			}
			if (noWay < OvRtProbeMax && OvRtNoWay(q, out _))
			{
				noWay++;
				continue;
			}
			stand = q;
			return true;
		}
		return false;
	}

	// ---------- M1: where to stand to hit a part's rock, by the game's own swing ----------
	private const int OvSwTestMax = 80;     // M1: at most 80 places looked at per part per choice

	private static readonly RaycastHit[] s_swHits = new RaycastHit[32];

	private static int s_swMask = -1;       // the game's Attack.m_attackMaskTerrain

	private static int s_bodyMask = -1;     // solid things a body can't stand in (not the terrain, not creatures)

	// M1 (test 19: the swing aimed at the part's box, which is bigger than its rock, from outside every box - the rock was
	// never within the swing): pit ground (P1) on the 0.5 m grid within the pickaxe's range + 0.5 m (flat) of the part's
	// box, feet no higher than the box top, where a body fits (the game's physics: a capsule of its size touches nothing
	// solid but the ground - inside a part's box is fine when the rock isn't there) and the game's own pickaxe swing from
	// there (OvSwingReaches) meets the part's own rock first. Lowest first: of those within 0.3 m of the lowest that
	// work, the nearest to the companion. The aim goes in _standAim. 0 = found; 1 = pit ground but no swing from it
	// reaches the rock (the last step's ladder, K1); 2 = no pit ground near it.
	// V2 (John 11-23 03:05-03:10Z: "They can also stand on the rock. Anywhere they need to stand to reach and clear the
	// last part"): the pit ground first; none works = the same search again with places on top of the vein's rock, plain
	// rock or stone and built pieces, and the ground just outside the pit (every other check the same)
	private int OvStandForPart(OvPart part, out Vector3 stand, out string why)
	{
		int r = OvStandForPartIn(part, false, out stand, out why);
		if (r == 0)
		{
			return 0;
		}
		bool busy1 = _sfpBusy;
		Vector3 st1 = stand, aim1 = _standAim;
		int r2 = OvStandForPartIn(part, true, out Vector3 st2, out string why2);
		_sfpBusy |= busy1;
		if (r2 == 0)
		{
			stand = st2;
			if (CompanionsPlugin.LogNormal) LogInfo($"No place on the pit ground to hit \"{(part.C != null ? part.C.name : "?")}\" from ({why}) - from {MHR_Mover.Fmt(st2)} instead, up on what is there or just outside the pit (V2)");
			return 0;
		}
		stand = st1;
		_standAim = aim1;
		why += $"; nor up on the rock, the ore or a built piece or just outside the pit ({why2}) (V2)";
		return r;
	}

	private bool _sfpBusy;   // V3: the last part stand search failed only because other companions have the places

	private int OvStandForPartIn(OvPart part, bool any, out Vector3 stand, out string why)
	{
		stand = Vector3.zero;
		why = null;
		_swOther = null;   // T3
		_sfpBusy = false;   // V3
		_standAim = part.Centre;
		// K2 (John 11-22 19:35Z: rock pieces "should use the lower-part rule"): a plain-rock piece (J2) too - a swing whose
		// first hit is the ground within 1 m of its box reaches it (that swing digs the dirt off it)
		bool low = OvIsLow(part) || part.IsRock;   // L4
		Vector3 me = base.transform.position;
		Attack a = FindBestTool(2)?.m_shared?.m_attack;
		float range = a != null ? a.m_attackRange : 1.8f;
		float reach = range + OvStandAt;
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		float rad = _character != null ? _character.GetRadius() : 0.4f;
		Bounds box = new Bounds(part.Centre, part.Half * 2f);
		float maxFeet = box.max.y;
		float pickReach = OvPickReach();
		int tooLow = 0, taken = 0, noStand = 0;
		List<Vector3> cand = new List<Vector3>();
		for (float x = Mathf.Floor((box.min.x - reach) / OvDirtGrid) * OvDirtGrid; x <= box.max.x + reach + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor((box.min.z - reach) / OvDirtGrid) * OvDirtGrid; z <= box.max.z + reach + 0.001f; z += OvDirtGrid)
			{
				Vector3 pt = new Vector3(x, 0f, z);
				if (Flat(pt, box.ClosestPoint(new Vector3(x, box.center.y, z))) > reach + 0.01f || (!any && !OvInPitPt(pt)) || (any && OvInPitPt(pt) && OvMapStand(pt, out _, out _) != 0))
				{
					continue;   // V2: the second search takes only what the first couldn't (off the pit, or up on something)
				}
				pt.y = GroundAt(pt);
				// R2.3: the map says no place to stand there: dropped before the body and swing tests; a built floor: its feet
				// go on the floor; don't know: as before. V2: up on the top of what stands there too
				int ms = any ? OvMapStandOn(pt, out float mf) : OvMapStand(pt, out mf, out _);
				if (ms == 0)
				{
					noStand++;
					continue;
				}
				if (ms == 1)
				{
					pt.y = mf;
				}
				if (pt.y > maxFeet)
				{
					continue;
				}
				// P1 (test 22: the 80 tests went to trench-floor points that can't reach, never the higher ground beside part
				// 104): a point whose feet + the pickaxe's reach (OvPickReach: attack height + range x sin(half the sweep))
				// doesn't get to the box bottom is dropped before the tests
				if (pt.y + pickReach < box.min.y)
				{
					tooLow++;
					continue;
				}
				// G2: not within 1.2 m of another companion's place or walk goal (the board)
				if (OvBoardTaken(pt) != null)
				{
					taken++;
					continue;
				}
				cand.Add(pt);
			}
		}
		// F9: with another companion on the part, only a place more than 90 degrees round its centre from that one's place
		HarvestController opp = OvPartOther(part);
		if (opp != null)
		{
			int qn = cand.RemoveAll((Vector3 q) => !OvQuarterOk(q, part.Centre, opp._clStand));
			if (cand.Count == 0 && qn > 0)
			{
				why = $"no place beside it more than a quarter turn round it from {opp.OvName()} ({qn})";
				_sfpBusy = true;
				return 2;
			}
		}
		if (cand.Count == 0 && taken > 0)
		{
			why = $"every place beside it is within 1.2 m of another companion ({taken})";
			_sfpBusy = true;
			return 2;
		}
		if (cand.Count == 0 && tooLow > 0)
		{
			why = $"no pit ground beside it high enough for the pickaxe to reach its bottom ({tooLow} too low)";
			return 1;   // as before P1: pit ground, but no swing from it reaches (the ladder's case)
		}
		if (cand.Count == 0 && noStand > 0)
		{
			why = $"no place to stand beside it by the pit map ({noStand} with stone, rock, a part or a thing on them, or too little room)";
			return 2;
		}
		if (cand.Count == 0)
		{
			why = "no pit ground near it (no higher than its top)";
			return 2;
		}
		cand.Sort((Vector3 p1, Vector3 p2) =>
		{
			int yc = p1.y.CompareTo(p2.y);
			return yc != 0 ? yc : Flat(me, p1).CompareTo(Flat(me, p2));
		});
		float band = float.MaxValue, bd = float.MaxValue;
		int tested = 0, noFit = 0, noWay = 0, probes = 0;
		bool found = false;
		foreach (Vector3 pt in cand)
		{
			if (pt.y > band || tested >= OvSwTestMax)
			{
				break;
			}
			tested++;
			if (!OvBodyFits(pt, rad, hh))
			{
				noFit++;
				continue;
			}
			if (!OvSwingReaches(a, pt, part, out Vector3 hit, lowDirt: low))
			{
				continue;
			}
			// J2 (silver): a rock piece only from where no ray of the swing meets the vein first
			// V1 (John 11-23 03:05Z, log f93a7cd2: his swings hit the rock piece and the silver together): once the dirt is
			// done, J2 no longer applies - a ray whose first hit is the rock piece is enough (OvSwingReaches above)
			if (part.IsRock && OvIsSilver(_vp) && !OvDirtDone(OvC()) && !OvSwingClean(a, pt, part.Centre, out _, part.C))
			{
				continue;
			}
			float d = Flat(me, pt) + Mathf.Abs(me.y - pt.y);
			// MP3 (the map-path list, John 2026-11-21): a place that would be chosen is dropped when the map says there is
			// no way there from where it stands (not just one step too high - MP4 cuts that on the walk); at most 5 asked
			if ((!found || d < bd) && probes < OvRtProbeMax)
			{
				probes++;
				if (OvRtNoWay(pt, out _))
				{
					noWay++;
					continue;
				}
			}
			if (!found)
			{
				found = true;
				band = pt.y + 0.3f;
			}
			if (d < bd)
			{
				bd = d;
				stand = pt;
				_standAim = hit;
			}
		}
		if (found)
		{
			return 0;
		}
		if (noWay > 0)
		{
			why = $"no way there by the pit map to the {noWay} place(s) beside it the swing reaches it from";
			return 2;
		}
		stand = cand[0];
		why = $"no place on the pit ground beside it where the game's pickaxe swing meets its rock first ({tested} looked at{(noFit > 0 ? $", {noFit} with no room for its body" : "")})";
		if (!any)
		{
			OvInWayNote(part);   // T3
		}
		return 1;
	}

	// M1: a body (a capsule of its radius and height) on the ground at pt touches nothing solid but the ground
	private bool OvBodyFits(Vector3 pt, float rad, float hh)
	{
		if (s_bodyMask < 0)
		{
			s_bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
		}
		float r = rad * 0.9f;
		return !Physics.CheckCapsule(pt + Vector3.up * (r + 0.15f), pt + Vector3.up * Mathf.Max(r + 0.2f, hh - r), r, s_bodyMask, QueryTriggerInteraction.Ignore);
	}

	// M1: the game's pickaxe swing (Attack.DoMeleeAttack) from feet at pt, facing the part's centre (flat): from the attack
	// origin (its origin joint, else the feet) + the attack height + the attack offset, a ray every 4 degrees across the
	// attack angle (vertical or horizontal as the attack says), each as long as the attack range, as wide as its ray width,
	// on the game's attack layers with the terrain; each ray counts only its FIRST hit, skipping companions and creatures
	// (the game skips ones it isn't hostile to) and hits behind it. True = a ray's first hit is the part's own rock (hit =
	// where). A ray that doesn't meet the rock within range (Collider.Raycast, the rock alone) isn't cast at all.
	// L4: lowDirt = a low part (touches the ground): a ray whose first hit is the ground within 0.5 m of the part's box also
	// counts - that swing digs the dirt off it (K3: digging toward a part counts as work)
	// T3: the first thing another swing at the part met instead of it (from a place its body fits), in OvStandForPart
	private Collider _swOther;

	// T3 (log 0e6bdc1c: "Rock1_cell.017" set aside for 7 minutes, rock piece 147 in front of it): the plain-rock piece a swing
	// met before the part goes on the rock list (J2's round mines it: its own place search with every check), then the part
	private void OvInWayNote(OvPart part)
	{
		Collider o = _swOther;
		_swOther = null;
		OvClear c = OvC();
		if (o == null || c == null || o == part.C || !o.gameObject.activeInHierarchy)
		{
			return;
		}
		MineRock5 m = o.GetComponentInParent<MineRock5>();
		if (m == null || OvSameOre(_vp, m) || o.GetComponentInParent<Piece>() != null)
		{
			return;
		}
		int id = o.GetInstanceID();
		if (c.InWay.Contains(id))
		{
			return;
		}
		OvRockPieceNote(c, o, m);
		if (c.RockById.TryGetValue(id, out OvPart rp))
		{
			rp.Why = "in the way of \"" + (part.C != null ? part.C.name : "a part") + "\"";
		}
		c.InWay.Add(id);
		if (CompanionsPlugin.LogNormal) LogInfo($"The rock piece \"{o.name}\" is in the way of the swing at \"{(part.C != null ? part.C.name : "?")}\" - mining it first (T3)");
	}

	private bool OvSwingReaches(Attack a, Vector3 pt, OvPart part, out Vector3 hit, bool lowDirt = false)
	{
		hit = part.Centre;
		if (s_swMask < 0)
		{
			s_swMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
		}
		float range = a != null ? a.m_attackRange : 1.8f;
		float height = a != null ? a.m_attackHeight : 1.0f;
		float angle = a != null ? a.m_attackAngle : 120f;
		float width = a != null ? a.m_attackRayWidth : 0.2f;
		float offset = a != null ? a.m_attackOffset : 0f;
		bool vertical = a == null || a.m_attackType == Attack.AttackType.Vertical;
		Vector3 jointUp = Vector3.zero;
		if (a != null && !string.IsNullOrEmpty(a.m_attackOriginJoint) && _character != null && _character.GetVisual() != null)
		{
			Transform j = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
			if (j != null)
			{
				jointUp = Vector3.up * (j.position.y - base.transform.position.y);
			}
		}
		Vector3 flat = part.Centre - pt;
		flat.y = 0f;
		if (flat.sqrMagnitude < 0.0001f)
		{
			flat = base.transform.forward;
		}
		Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
		Vector3 fwd = yaw * Vector3.forward;
		Vector3 o = pt + jointUp + Vector3.up * height + yaw * Vector3.right * offset;
		float half = angle / 2f;
		for (float ang = -half; ang <= half; ang += 4f)
		{
			Vector3 dir = yaw * ((vertical ? Quaternion.Euler(ang, 0f, 0f) : Quaternion.Euler(0f, -ang, 0f)) * Vector3.forward);
			// D1.1: a ray that doesn't meet the rock is skipped, unless the swing starts against it (the game counts that)
			Bounds sb = part.C.bounds;
			sb.Expand(width * 2f);
			RaycastHit rh = default(RaycastHit);
			if (!part.C.Raycast(new Ray(o, dir), out rh, range) && !sb.Contains(o))
			{
				continue;
			}
			int n = width > 0f ? Physics.SphereCastNonAlloc(o, width, dir, s_swHits, Mathf.Max(0f, range - width), s_swMask, QueryTriggerInteraction.Ignore) : Physics.RaycastNonAlloc(o, dir, s_swHits, range, s_swMask, QueryTriggerInteraction.Ignore);
			Collider first = null;
			float fd = float.MaxValue;
			Vector3 fp = rh.point;
			for (int i = 0; i < n; i++)
			{
				RaycastHit h = s_swHits[i];
				if (h.collider == null || h.distance >= fd || h.collider.GetComponentInParent<Character>() != null)
				{
					continue;
				}
				Vector3 hp = OvSwingPoint(h, o, dir, range);   // D1.1: the game's point for a swing that starts against it
				if (angle < 180f && Vector3.Dot(hp - o, fwd) <= 0f)
				{
					continue;
				}
				first = h.collider;
				fd = h.distance;
				fp = hp;
			}
			if (first == part.C)
			{
				hit = fp;
				return true;
			}
			if (first != null && _swOther == null && first.gameObject.layer != LayerMask.NameToLayer("terrain"))
			{
				_swOther = first;   // T3
			}
			if (lowDirt && first != null && first.gameObject.layer == LayerMask.NameToLayer("terrain"))
			{
				Bounds pad = new Bounds(part.Centre, part.Half * 2f);
				pad.Expand(1f);
				if (pad.Contains(fp))
				{
					hit = fp;
					return true;
				}
			}
		}
		return false;
	}

	private const float OvDigInFar = 3f;   // D1.3: how far from a dirt point it digs in from the trench

	// E2: q is one of the point's tried places (within 0.25 m of one)
	private static bool OvTriedHas(List<Vector3> tried, Vector3 q)
	{
		if (tried == null)
		{
			return false;
		}
		foreach (Vector3 t in tried)
		{
			if (Flat(t, q) < 0.25f)
			{
				return true;
			}
		}
		return false;
	}

	// D1.2: on the trench floor - the ground at q is within 0.7 m (a step) of the ground at the trench spot nearest it
	private bool OvOnTrenchFloor(Vector3 q)
	{
		OvSpot sp = _vp != null && _vp.Spots.Count > 0 ? OvNearest(q) : null;
		return sp == null || q.y <= GroundAt(sp.P) + OvStepUp;
	}

	// D1.1 (Attack.DoMeleeAttack): a hit with no point (the swing's sphere started against the collider, distance 0) is
	// at the end of the ray for a mesh (a rock's), else the collider's closest point to the start; any other hit is where it is
	private static Vector3 OvSwingPoint(RaycastHit h, Vector3 o, Vector3 dir, float range)
	{
		if (h.point == Vector3.zero && h.normal == -dir)
		{
			return h.collider is MeshCollider ? o + dir * range : h.collider.ClosestPoint(o);
		}
		return h.point;
	}

	// E1 (John 2026-11-21: "clear all the dirt from the silver ore vein WITHOUT hitting the vein"; "only that the strike will
	// connect" with the dirt, at any height): the game's pickaxe swing from feet at pt toward aim (Attack.DoMeleeAttack: the
	// rays in the game's order, the top of the arc first; each ray strikes only the first thing it meets; the ground is dug
	// only when the first thing the swing meets is the ground). True = no ray's first hit is this vein's rock AND the first
	// thing the swing meets is the ground (above or below its feet); false = a ray meets the vein first, or the swing's
	// first hit is something else, or it meets nothing at all
	private bool OvSwingClean(Attack a, Vector3 pt, Vector3 aim)
	{
		return OvSwingClean(a, pt, aim, out _);
	}

	// F7 (d): dig = where the game digs on that swing (the first ray's hit, when it is the ground)
	// J2: want = a rock piece - then true = no ray's first hit is the vein and some ray's first hit is that piece
	private bool OvSwingClean(Attack a, Vector3 pt, Vector3 aim, out Vector3 dig, Collider want = null)
	{
		dig = Vector3.zero;
		bool wantHit = false;
		if (s_swMask < 0)
		{
			s_swMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
		}
		float range = a != null ? a.m_attackRange : 1.8f;
		float height = a != null ? a.m_attackHeight : 1.0f;
		float angle = a != null ? a.m_attackAngle : 120f;
		float width = a != null ? a.m_attackRayWidth : 0.2f;
		float offset = a != null ? a.m_attackOffset : 0f;
		bool vertical = a == null || a.m_attackType == Attack.AttackType.Vertical;
		Vector3 jointUp = Vector3.zero;
		if (a != null && !string.IsNullOrEmpty(a.m_attackOriginJoint) && _character != null && _character.GetVisual() != null)
		{
			Transform j = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
			if (j != null)
			{
				jointUp = Vector3.up * (j.position.y - base.transform.position.y);
			}
		}
		Vector3 flat = aim - pt;
		flat.y = 0f;
		if (flat.sqrMagnitude < 0.0001f)
		{
			flat = base.transform.forward;
		}
		Quaternion yaw = Quaternion.LookRotation(flat.normalized, Vector3.up);
		Vector3 fwd = yaw * Vector3.forward;
		Vector3 o = pt + jointUp + Vector3.up * height + yaw * Vector3.right * offset;
		float half = angle / 2f;
		bool anyHit = false, groundFirst = false;
		for (float ang = -half; ang <= half; ang += 4f)
		{
			Vector3 dir = yaw * ((vertical ? Quaternion.Euler(ang, 0f, 0f) : Quaternion.Euler(0f, -ang, 0f)) * Vector3.forward);
			int n = width > 0f ? Physics.SphereCastNonAlloc(o, width, dir, s_swHits, Mathf.Max(0f, range - width), s_swMask, QueryTriggerInteraction.Ignore) : Physics.RaycastNonAlloc(o, dir, s_swHits, range, s_swMask, QueryTriggerInteraction.Ignore);
			Collider first = null;
			float fd = float.MaxValue;
			Vector3 fp = Vector3.zero;
			for (int i = 0; i < n; i++)
			{
				RaycastHit h = s_swHits[i];
				if (h.collider == null || h.distance >= fd || h.collider.GetComponentInParent<Character>() != null)
				{
					continue;
				}
				Vector3 hp = OvSwingPoint(h, o, dir, range);   // D1.1: the game's point for a swing that starts against it
				if (angle < 180f && Vector3.Dot(hp - o, fwd) <= 0f)
				{
					continue;
				}
				first = h.collider;
				fd = h.distance;
				fp = hp;
			}
			if (first == null)
			{
				continue;
			}
			MineRock5 m5 = first.GetComponentInParent<MineRock5>();
			if (m5 != null && _vp != null && (_vp.Rocks.Contains(m5) || OvSameOre(_vp, m5)))
			{
				return false;   // this ray strikes the vein (H2: or another vein of its ore)
			}
			if (want != null && first == want)
			{
				wantHit = true;   // J2
			}
			if (!anyHit)
			{
				anyHit = true;
				groundFirst = first.GetComponentInParent<Heightmap>() != null;
				if (groundFirst)
				{
					dig = fp;
				}
			}
		}
		return want != null ? wantHit : anyHit && groundFirst;
	}

	// S3: inside the pit map's squares (its whole area)
	private static bool OvOnMapArea(OvMap m, Vector3 pt)
	{
		return pt.x >= m.X0 && pt.x < m.X0 + m.W * OvMapCell && pt.z >= m.Z0 && pt.z < m.Z0 + m.H * OvMapCell;
	}

	// P1: on the pit's ground (K7: inside the trench ring and the trench). Before the pit's
	// ground is first worked out: inside the ring or within 2 m of a trench spot (the old test).
	private bool OvInPitPt(Vector3 pt)
	{
		OvPlan p = _vp;
		if (p.PitCells.Count > 0)
		{
			return p.PitCells.Contains(OvPitKey(pt.x, pt.z));
		}
		if (p.Poly.Count >= 3 && OvInside(p.Poly, new Vector2(pt.x, pt.z)))
		{
			return true;
		}
		foreach (OvSpot sp in p.Spots)
		{
			if (Flat(sp.P, pt) <= OvThingRadius)
			{
				return true;
			}
		}
		return false;
	}

	// ---------- GG2 2.: the ore step ----------
	// SV7 (John 11-21: "No timeout checks"): a set-aside part stays aside until something within 2 m of its box changed
	// after it was set aside (a pit map square, or with no map the ground moved 0.1 m), or no other part is free
	// (OvOreChoose). No clock.
	private bool OvPartCooling(OvPart part)
	{
		if (part.CoolAt < 0f)
		{
			return false;
		}
		if (!OvBoxChangedNear(part.Centre, part.Half, part.CoolAt, part.CoolGround))
		{
			return true;
		}
		part.CoolAt = -1f;
		part.CoolOnce = false;
		part.CoolGround = null;
		if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{(part.C != null ? part.C.name : "?")}\" is back on the list: something within 2 m of it changed{OvChgSaid()}");
		return false;
	}

	// SV7: a pit map square within 2 m of the box (flat) changed after `at`; with no map, the ground there moved 0.1 m
	private bool OvBoxChangedNear(Vector3 centre, Vector3 half, float at, float[] was)
	{
		_chgSq = -1;   // I3
		OvMap m = _vp?.Map;
		if (m != null && m.Built && m.ChangedAt != null)
		{
			if (m.LastChange <= at)
			{
				return false;
			}
			int x0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - half.x - OvAsideNear - m.X0) / OvMapCell));
			int x1 = Mathf.Min(m.W - 1, Mathf.FloorToInt((centre.x + half.x + OvAsideNear - m.X0) / OvMapCell));
			int z0 = Mathf.Max(0, Mathf.FloorToInt((centre.z - half.z - OvAsideNear - m.Z0) / OvMapCell));
			int z1 = Mathf.Min(m.H - 1, Mathf.FloorToInt((centre.z + half.z + OvAsideNear - m.Z0) / OvMapCell));
			for (int z = z0; z <= z1; z++)
			{
				for (int x = x0; x <= x1; x++)
				{
					if (m.ChangedAt[z * m.W + x] > at)
					{
						_chgSq = z * m.W + x;   // I3
						return true;
					}
				}
			}
			return false;
		}
		if (was == null)
		{
			return false;
		}
		float[] now = OvBoxGroundGrid(centre, half);
		for (int i = 0; i < now.Length && i < was.Length; i++)
		{
			if (Mathf.Abs(now[i] - was[i]) >= OvAsideMove)
			{
				return true;
			}
		}
		return false;
	}

	// SV7: the ground over the box and 2 m round it, 1 m apart - only used with no pit map
	private float[] OvBoxGroundGrid(Vector3 centre, Vector3 half)
	{
		List<float> g = new List<float>();
		for (float z = centre.z - half.z - OvAsideNear; z <= centre.z + half.z + OvAsideNear + 0.001f; z += 1f)
		{
			for (float x = centre.x - half.x - OvAsideNear; x <= centre.x + half.x + OvAsideNear + 0.001f; x += 1f)
			{
				g.Add(GroundAt(new Vector3(x, 0f, z)));
			}
		}
		return g.ToArray();
	}

	// no dirt and no low parts left (John, 2026-11-20: the pop part only once the low parts are cleared): M4 - the part
	// whose break leaves the fewest held by the game's support check (none = it pops the vein), nearest first; no one
	// else's, not waiting; from the pit ground where the game's swing meets its rock (M1), or from a ladder when none does
	private bool OvOreChoose(OvClear c)
	{
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		bool standing = false, others = false;
		string othersWhy = null;   // SV5
		// S3 (John, 2026-11-20): parts hit from the ground go before ladder parts - a ladder only once every standing part
		// (that no other companion has) needs one at its last look (OutOfReach). A part not looked at yet doesn't need one.
		bool ladderPass = true;
		foreach (OvPart part in c.Parts)
		{
			if (!part.Out && !part.OutOfReach && !OvPartHeldByOther(part) && !OvIsLow(part))
			{
				ladderPass = false;
				break;
			}
		}
		foreach (OvPart part in c.Parts)
		{
			if (part.Out)
			{
				continue;
			}
			standing = true;
			if (OvPartCooling(part) && !(ladderPass && part.LadderWait))
			{
				continue;
			}
			if (OvPartHeldByOther(part))
			{
				others = true;
				othersWhy ??= $"the part \"{part.C.name}\" is {part.Holder.OvName()}'s ({part.Holder.OvName()} is {part.Holder.OvHolderDoing(part.Holder._clWalk)})";
				continue;
			}
			cand.Add(part);
		}
		// SV7 (as the dirt's 8b): no other part is free and none is another companion's - each set-aside part that hasn't
		// come back this way yet comes back once; set aside again with nothing changed, it waits for a change
		if (standing && cand.Count == 0 && !others)
		{
			foreach (OvPart part in c.Parts)
			{
				if (part.Out || part.CoolAt < 0f || part.CoolOnce || OvPartHeldByOther(part))
				{
					continue;
				}
				part.CoolAt = -1f;
				part.CoolGround = null;
				part.CoolOnce = true;
				cand.Add(part);
				if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{(part.C != null ? part.C.name : "?")}\" comes back once: no other part is free");
			}
		}
		if (!standing)
		{
			// GG3d: no dirt left and every part gone or buried under the game's dig limit
			c.Done = true;
			OvClearSay(c);
			return false;
		}
		// M4 (John: "All they need to do is break whatever part is closest to the ground / holding up the other parts"):
		// for each part, the game's support pass as if it had broken - how many parts would still be held. Fewest first (0
		// = it pops the vein), then nearest.
		Dictionary<OvPart, int> after = new Dictionary<OvPart, int>();
		foreach (OvPart part in cand)
		{
			after[part] = OvSupport(c, part);
		}
		cand.Sort((OvPart a, OvPart b) =>
		{
			bool va = OvV4Has(c, a), vb = OvV4Has(c, b);   // V4: the parts attached to a rock piece that can't be reached first
			if (va != vb)
			{
				return va ? -1 : 1;
			}
			int bc = after[a].CompareTo(after[b]);
			return bc != 0 ? bc : Flat(me, OvBoxNear(a, me)).CompareTo(Flat(me, OvBoxNear(b, me)));
		});
		_clPopWhy = null;
		int tries = 0;
		foreach (OvPart pick in cand)
		{
			if (tries++ >= 4)
			{
				break;
			}
			int r = OvStandFor(c, new Vector3(pick.Centre.x, OvPartBottom(pick), pick.Centre.z), pick, out Vector3 stand, out string why);
			pick.OutOfReach = r == 1;
			if (r == 2)
			{
				OvPartCool(pick, why, wait: false);
				continue;
			}
			if (r == 1 && OvIsLow(pick))
			{
				// L4: a low part is never mined from a ladder - it waits 60 s (not a failure) for a place on the ground
				OvPartCool(pick, why + " - a low part (it touches the ground): never from a ladder", wait: true);
				continue;
			}
			if (r == 1 && !ladderPass)
			{
				// S3: it waits 60 s (not a failure) while other parts may be hit from the ground; the next part is tried
				OvPartCool(pick, why + " - it waits for a ladder until every part needs one", wait: true);
				pick.LadderWait = true;
				continue;
			}
			OvSpot ws = OvClSpotFor(pick);
			if (ws != null && OvWayOutNeeded(ws))
			{
				OvWayOutHere(ws);
				return true;
			}
			int left = after[pick];
			_clPopWhy = (OvV4Has(c, pick) ? "attached to a rock piece that can't be reached (V4); " : "") + (left == 0 ? $"the part that pops it: by the game's support check nothing of the vein stays held once it breaks ({c.HeldCount} held now)" : $"the part whose break drops the most: by the game's support check {left} of the {c.HeldCount} held part(s) stay held once it breaks");
			OvClBegin(pick, stand, ladder: r == 1);
			return true;
		}
		_clNextPick = Time.time + 1f;
		_ai?.StopMoving();
		if (cand.Count > 4)
		{
			return true;   // the rest next second
		}
		if (others)
		{
			OvStandBy((othersWhy ?? "the rest is other companions'") + $"; {cand.Count} free part(s) had no place to hit them from");
			return true;
		}
		OvStuckCheck(c);
		if (!c.Stopped)
		{
			OvStandBy($"every part left is waiting or had no place to hit it from ({cand.Count} tried)");
		}
		return true;
	}

	// X2: the nearest part whose box touches an indestructible world object (not buried, no one on it, not waiting) - mined
	// with the normal part round from a place where the swing meets its rock; none = it
	// waits 60 s (a cooldown) and the next is tried. True = a round began.
	private bool OvWorldFirst(OvClear c)
	{
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		foreach (OvPart part in c.Parts)
		{
			if (part.OnWorld && !part.Out && part.Holder == null && !OvPartCooling(part))
			{
				cand.Add(part);
			}
		}
		if (cand.Count == 0)
		{
			return false;
		}
		cand.Sort((OvPart a, OvPart b) => Flat(me, OvBoxNear(a, me)).CompareTo(Flat(me, OvBoxNear(b, me))));
		int tries = 0;
		foreach (OvPart pick in cand)
		{
			if (tries++ >= 4)
			{
				break;
			}
			int r = OvStandFor(c, new Vector3(pick.Centre.x, OvPartBottom(pick), pick.Centre.z), pick, out Vector3 stand, out string why);
			if (r == 0)
			{
				OvSpot ws = OvClSpotFor(pick);
				if (ws != null && OvWayOutNeeded(ws))
				{
					OvWayOutHere(ws);
					return true;
				}
				_clPopWhy = "a part held by a thing that can't be damaged (X2)";
				OvClBegin(pick, stand, ladder: false, low: OvIsLow(pick));
				return true;
			}
			OvPartCool(pick, why + " - a part held by a thing that can't be damaged", wait: true);
		}
		return false;
	}

	// GG3c / GG3a: a part that didn't break (or can't get its ladder) waits 1 min; wait = not counted by the stuck rule
	private void OvPartCool(OvPart part, string why, bool wait)
	{
		float now = Time.time;
		part.CoolAt = now;   // SV7
		OvMap pm = _vp?.Map;
		part.CoolGround = pm == null || !pm.Built ? OvBoxGroundGrid(part.Centre, part.Half) : null;
		part.CoolWhy = why;
		part.Wait = wait;
		part.LadderWait = false;
		if (!wait)
		{
			part.FailAt = now;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{(part.C != null ? part.C.name : "?")}\" of \"{_vp.Name}\" is set aside until something within 2 m of it changes{(part.CoolOnce ? "" : " or no other part is free")}: {why}");
	}

	// GG3d (John's rule: two tries with nothing broken = stop and wait): nothing to take now, nothing another companion is
	// on - when every dirt point and part waiting has failed since the last thing that broke or went down (and not just
	// for a hammer or a ladder spot), the vein work stops here and says why, point by point and part by part
	private void OvStuckCheck(OvClear c)
	{
		float now = Time.time;
		// R3c.3: after the pop, never while a loose item is still in the pit and not cooling down
		if (OvPopped(c))
		{
			foreach (OvLoose l in c.Loose.Values)
			{
				if (OvLooseHere(l) && !OvLooseAside(l))
				{
					return;
				}
			}
		}
		// SV2 (log 07ee23fb: the stop was judged on a list made before John dug the last point away): the dirt list is made
		// again right now and the stop is judged on that list only
		if (!c.Stopped)
		{
			OvDirtScan(c, true);
		}
		bool failed = false;
		foreach (OvDirt d in c.Dirt)
		{
			if (!OvDirtCooling(c, d) || !c.DirtFail.TryGetValue(d.Key, out float f) || f <= c.ProgAt)
			{
				return;   // one that will come back after the last progress: it waits
			}
			failed = true;
		}
		// P5: a low part that will come back after the last progress: it waits
		foreach (OvPart part in c.Low)
		{
			if (!OvLowUp(part))
			{
				continue;
			}
			if (!OvPartCooling(part))
			{
				return;
			}
			if (part.Wait)
			{
				continue;   // waiting for a hammer or a ladder spot: not counted (as for the other parts)
			}
			if (part.FailAt <= c.ProgAt)
			{
				return;
			}
			failed = true;
		}
		if (c.Dirt.Count == 0)
		{
			foreach (OvPart part in c.Parts)
			{
				if (part.Out)
				{
					continue;
				}
				if (!OvPartCooling(part))
				{
					return;
				}
				if (part.Wait)
				{
					continue;
				}
				if (part.FailAt <= c.ProgAt)
				{
					return;
				}
				failed = true;
			}
		}
		if (!failed || c.Stopped)
		{
			return;   // only waiting for a hammer or a ladder spot: it stands by (it said so once)
		}
		// P1 (John 11-22 21:23-21:25Z, log 0acd24d1): on silver, before stopping, the ground near the dirt points it had no
		// place to work from is lowered so it can reach them
		// T2: every other point and part failed - the dirt on top of the vein comes back on the list once
		if (OvIsSilver(_vp) && !c.TopBack && c.TopSkipped > 0)
		{
			c.TopBack = true;
			if (CompanionsPlugin.LogNormal) LogInfo($"Every other dirt point and part of \"{_vp.Name}\" failed - the {c.TopSkipped} dirt point(s) on top of the vein are back on the list, once (T2)");
			OvDirtScan(c, true);
			return;
		}
		if (OvIsSilver(_vp) && OvLowerTry(c))
		{
			return;
		}
		c.Stopped = true;
		c.StoppedAt = now;
		c.Done = true;
		OvClearSay(c);
	}

	// ---------- P1 (John 11-22 21:23-21:25Z, log 0acd24d1): lower the ground near dirt it can't reach (silver) ----------
	// The pickaxe's swing starts 1.0 m above the feet and its top ray rises 1.8 m x sin 60 = 1.56 m more, so from feet
	// 2.56 m under a part's bottom no ray of the swing can reach the part, and under the vein the body (1.9 m) fits.
	private const float OvLowerUnder = 2.56f;

	// a dirt point set aside because no place within 3 m of it could work it (OvStandFor's silver reason)
	private static bool OvNoPlaceWhy(OvClear c, OvDirt d)
	{
		return c.DirtWhy.TryGetValue(d.Key, out string w) && w != null && w.StartsWith("no place within");
	}

	// the goal for the ground near a point: 2.56 m under its part's bottom, never under the game's 8 m floor there
	private float OvLowerGoal(OvDirt d, Vector3 at)
	{
		return Mathf.Max(OvFloor(at), OvPartBottom(d.Part) - OvLowerUnder);
	}

	// Step 1 (S1): each such point's nearest open trench spot, at any distance, gets that deeper goal (never a higher one) and the
	// trench reopens for the spots above it (I1, OvReopenCheck) - only when a goal went deeper in this call, so each spot
	// is lowered once. Then step 2. True = the trench was reopened, or step 2 has work (the clearing waits for it).
	private bool OvLowerTry(OvClear c)
	{
		OvPlan p = _vp;
		if (p == null || p.Spots.Count == 0)
		{
			return false;
		}
		int pts = 0, deeper = 0;
		foreach (OvDirt d in c.Dirt)
		{
			if (d.Part == null || !OvNoPlaceWhy(c, d))
			{
				continue;
			}
			pts++;
			// S1 (log ebd571a0: the nearest spots were 4.6-5.2 m away, none within 3 m): its one nearest open spot, at any distance
			OvSpot near = null;
			float nd = float.MaxValue;
			foreach (OvSpot s in p.Spots)
			{
				if (s.Closed)
				{
					continue;
				}
				float f = Flat(s.P, d.P);
				if (f < nd)
				{
					nd = f;
					near = s;
				}
			}
			if (near != null)
			{
				float g = OvLowerGoal(d, near.P);
				if (g < near.Target - 0.01f)
				{
					near.Target = g;
					deeper++;
				}
			}
		}
		if (pts == 0)
		{
			return false;
		}
		if (deeper > 0 && CompanionsPlugin.LogNormal) LogInfo($"{pts} dirt point(s) of \"{p.Name}\" have no place to work them from: {deeper} trench spot goal(s) (each point's nearest spot) set to {OvLowerUnder:F2} m under the part's bottom (not under the game's 8 m floor) - so the swing can't reach the vein and there is room under it (P1)");
		// only when a spot's goal went deeper now: a spot is lowered once (a lap that can't take it there isn't run again)
		if (deeper > 0 && OvReopenCheck(p, "P1: the trench beside dirt it can't reach goes deeper"))
		{
			return true;
		}
		return OvLowerStep2(c);
	}

	// Step 2 (John 11-22 21:23Z: "If they need to clear more of the surrounding area to get to the points ... that is ok"):
	// for each such point, the pit squares every 0.5 m on the straight line from its nearest deepened trench spot (one at its
	// deeper goal) toward it, up to the first square from which the point is within its swing, get the same goal (2.56 m
	// under the part's bottom, not under the 8 m floor). They are lowered one at a time, nearest the spot first, each from
	// the square before it. A square is lowered once: done, or failed (a failed square ends its line). True = it took a
	// square, or another companion is on one (it stands by).
	private bool OvLowerStep2(OvClear c)
	{
		OvPlan p = _vp;
		float range = FindBestTool(2)?.m_shared?.m_attack != null ? FindBestTool(2).m_shared.m_attack.m_attackRange : 2f;
		float reach = Mathf.Max(0.8f, range - OvStandAt);
		foreach (OvDirt d in c.Dirt)
		{
			if (d.Part == null || !OvNoPlaceWhy(c, d) || c.LowLines.ContainsKey(d.Key))
			{
				continue;
			}
			List<OvLowSq> line = new List<OvLowSq>();
			c.LowLines[d.Key] = line;
			OvSpot from = null;
			float fd = float.MaxValue;
			foreach (OvSpot s in p.Spots)
			{
				float g = OvLowerGoal(d, s.P);
				if (s.Closed || s.Target > g + 0.01f || GroundAt(s.P) > s.Target + 0.3f)
				{
					continue;   // not a deepened spot that is dug to its goal
				}
				float f = Flat(s.P, d.P);
				if (f < fd)
				{
					fd = f;
					from = s;
				}
			}
			if (from == null)
			{
				continue;
			}
			Vector3 dir = d.P - from.P;
			dir.y = 0f;
			float len = dir.magnitude;
			if (len < 0.01f)
			{
				continue;
			}
			dir /= len;
			Vector3 at = new Vector3(from.P.x, GroundAt(from.P), from.P.z);
			OvLowSq prev = null;
			for (float t = OvDirtGrid; t < len; t += OvDirtGrid)
			{
				Vector3 q = from.P + dir * t;
				q.y = GroundAt(q);
				if (!OvInPitPt(q))
				{
					break;
				}
				OvLowSq sq = new OvLowSq { P = q, From = prev != null ? prev.P : at, Goal = OvLowerGoal(d, q), Prev = prev };
				sq.Done = q.y <= sq.Goal + OvDigGain;
				line.Add(sq);
				prev = sq;
				if (Flat(q, d.P) <= reach)
				{
					break;   // from here the point is within its swing
				}
			}
			if (line.Count > 0 && CompanionsPlugin.LogNormal) LogInfo($"No place to work the dirt at {MHR_Mover.Fmt(d.P)} from even with the deeper trench: {line.Count} square(s) from trench spot {from.Idx + 1} toward it get its goal ({line[0].Goal:F2}, {OvLowerUnder:F2} m under the part's bottom) - lowered one by one, each from the one before it (P1)");
		}
		Vector3 me = base.transform.position;
		OvLowSq best = null;
		float bd = float.MaxValue;
		bool othersOn = false;
		foreach (List<OvLowSq> line in c.LowLines.Values)
		{
			foreach (OvLowSq sq in line)
			{
				if (sq.Failed)
				{
					break;   // a failed square ends its line
				}
				if (sq.Done)
				{
					continue;
				}
				if (sq.Holder != null && sq.Holder != this && sq.Holder.isActiveAndEnabled && sq.Holder._lwSq == sq)
				{
					othersOn = true;
					break;
				}
				if (sq.Prev != null && !sq.Prev.Done)
				{
					break;
				}
				float dd = Flat(me, sq.From);
				if (dd < bd)
				{
					bd = dd;
					best = sq;
				}
				break;   // only the first open square of each line
			}
		}
		if (best != null)
		{
			best.Holder = this;
			_lwSq = best;
			_lwOn = false;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Lowering the square at {MHR_Mover.Fmt(best.P)} to {best.Goal:F2} from {MHR_Mover.Fmt(best.From)} (P1)");
			return true;
		}
		if (othersOn)
		{
			OvStandBy("another companion is lowering the ground toward the dirt it can't reach (P1)");
			return true;
		}
		return false;
	}

	private void OvLwOff()
	{
		if (_lwSq != null && _lwSq.Holder == this)
		{
			_lwSq.Holder = null;
		}
		_lwSq = null;
		if (_lwOn)
		{
			_lwOn = false;
			OvDigOff();
			_dgDirt = false;
			_dgClear = false;
		}
	}

	// P1 step 2: to the square before it, then the existing dirt dig (OvDigBegin / OvDigTick, every silver swing check) down
	// to the square's goal. Ends: the square at its goal (done), or the dig ended short of it / the walk gave up (failed).
	private void OvLowTick(OvClear c)
	{
		OvLowSq sq = _lwSq;
		float dt = Time.deltaTime;
		if (_lwOn && _dgActive)
		{
			OvDigTick();
			if (_dgActive)
			{
				return;
			}
		}
		if (_lwOn)
		{
			// the dig ended
			bool limit = _dgLimit;
			string why = _drRepos;
			_drRepos = null;
			_lwOn = false;
			_dgDirt = false;
			_dgClear = false;
			if (_state != HarvestState.Vein)
			{
				return;   // a swing hit a thing and handed it to the Wood / Stone flow: the square stays its own
			}
			if (GroundAt(sq.P) <= sq.Goal + OvDigGain)
			{
				sq.Done = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"The square at {MHR_Mover.Fmt(sq.P)} is down to {GroundAt(sq.P):F2} (goal {sq.Goal:F2}) (P1)");
			}
			else
			{
				sq.Failed = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"The square at {MHR_Mover.Fmt(sq.P)} stopped at {GroundAt(sq.P):F2} (goal {sq.Goal:F2}): {why ?? (limit ? "the game's 8 m dig limit" : $"{OvDirtNoProg} swings did no work")} - its line ends here (P1)");
			}
			OvLwOff();
			c.DirtAt = Mathf.Min(c.DirtAt, Time.time - OvDirtEvery + 0.5f);   // the dirt list again in 0.5 s
			return;
		}
		if (GroundAt(sq.P) <= sq.Goal + OvDigGain)
		{
			sq.Done = true;
			OvLwOff();
			return;
		}
		Vector3 me = base.transform.position;
		Vector3 stand = new Vector3(sq.From.x, GroundAt(sq.From), sq.From.z);
		if (Flat(me, stand) > 0.5f || me.y - stand.y > OvStepUp)
		{
			if (OvWalkGaveUp(dt, stand))
			{
				sq.Failed = true;
				if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to {MHR_Mover.Fmt(stand)} to lower the square at {MHR_Mover.Fmt(sq.P)} - its line ends here (P1)");
				OvLwOff();
				return;
			}
			OvWalk(stand, 0.25f, Flat(me, stand) > 8f, dt);
			return;
		}
		_ai?.StopMoving();
		OvDigBegin(new Vector3(sq.P.x, GroundAt(sq.P), sq.P.z), null, ramp: false);
		OvDigResume();
		_dgClear = true;
		_dgDirt = true;
		_dgGoalY = sq.Goal;
		_lwOn = true;
	}

	// the trench spot nearest the part (flat, to its box) that no other companion stands on or walks to (the way-out check)
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
			if (OvBoardTaken(q.P) != null)
			{
				continue;
			}
			if (d < bd)
			{
				bd = d;
				best = q;
			}
		}
		return best ?? any;
	}

	// a round on the part starts: to the place to stand beside it (or the ladder's way)
	private void OvClBegin(OvPart part, Vector3 stand, bool ladder, bool low = false)
	{
		_clFor = part;
		_clRockWhy = null;   // J2
		OvPartClaim(part);   // F9
		_sbWhy = null;   // SV5
		_clStand = stand;
		_clRampTried = false;   // S2: one map way up per round
		_clMpTried = _clMpOn = false;   // MP4: one way up at the map's blocking step per round
		_clAim = _standAim;
		_clWalk = !ladder;
		_moveTimer = 0f;
		_ai?.StopMoving();
		OvLnBegin(ladder);
		if (CompanionsPlugin.LogNormal) LogInfo($"Mining \"{_vp.Name}\"{(low ? "" : " (no dirt holds it up now)")}: {(low ? "a low part that goes into the ground (P5)" : ladder ? $"a part too high to hit from the ground beside it - from a ladder" : _clPopWhy ?? "the lowest part")} \"{part.C.name}\" at {MHR_Mover.Fmt(part.Centre)}, bottom {OvPartBottom(part):F1}{(part.Why != null ? " (" + part.Why + ")" : part.Held ? " (held by other parts)" : " (not held - it falls when a part breaks)")}{(ladder ? "" : $" - from {MHR_Mover.Fmt(stand)}, swinging at its rock at {MHR_Mover.Fmt(_standAim)}")}");
	}

	// ---------- S3 (test of 05e19c2: a fight moved Je 7 m from its place and 3 m down; it went on swinging at its part from
	// there, the game dug the ground in front each time and its feet sank 2 m - a hole) ----------
	private bool _clRampTried;   // S2: this part round already started a way up from the map's wall check

	private bool _clMpTried, _clMpOn, _drMpTried, _drMpOn;   // MP4: per round: the map's blocking step tried / being walked to

	private Vector3 _clMpFoot, _clMpTop, _drMpFoot, _drMpTop;
	private int _dgBackNoProg = -1, _dgBackMiss;   // S3: the round's swing counts kept while it walks back (-1 = none)

	// off the place it was given: more than 0.5 m from it (flat), or more than 0.7 m under / over it (the ground there now
	// counts too: a dig beside it lowers it, a built floor (R2) is above it)
	private bool OvOffPlace(Vector3 stand, float flatOff = OvStandAt)
	{
		Vector3 me = base.transform.position;
		if (Flat(me, stand) > flatOff)
		{
			return true;
		}
		float g = GroundAt(stand);
		return me.y < Mathf.Min(stand.y, g) - OvPlaceUpDown || me.y > Mathf.Max(stand.y, g) + OvPlaceUpDown;
	}

	// the dirt round's place: as above, and (HH2) feet no more than a step above the ground there
	private bool OvDrOffPlace(Vector3 me, float flatOff = OvStandAt)
	{
		return OvOffPlace(_drStand, flatOff) || me.y - GroundAt(_drStand) > OvStepUp;
	}

	// the swing round stops for the walk back; its no-progress and miss counts are kept for when it starts again, so the
	// walk back never starts the 3-swing / 20-swing ends over
	private void OvDigPause(string what)
	{
		_dgBackNoProg = _dgNoProg;
		_dgBackMiss = _dgPartMiss;
		if (CompanionsPlugin.LogNormal) LogInfo($"Off its place for {what} ({Flat(base.transform.position, _dgPart ? _clStand : _drStand):F1} m away) - back to it first, {_dgNoProg} swing(s) in a row without work kept");
		OvDigOff();
		_dgClear = false;
		_dgPart = false;
		_dgDirt = false;
	}

	private void OvDigResume()
	{
		if (_dgBackNoProg >= 0)
		{
			_dgNoProg = _dgBackNoProg;
			_dgPartMiss = _dgBackMiss;
			_dgBackNoProg = -1;
		}
	}

	private void OvClOff()
	{
		_dgBackNoProg = -1;   // S3
		_clFor = null;
		_clWalk = false;
		OvLnOff();
		if (_dgClear)
		{
			OvDigOff();
			_dgClear = false;
		}
	}

	// one frame of the round on a part (from OvClearTick while _clFor is set)
	private void OvClRound(OvClear c)
	{
		OvPart part = _clFor;
		if (part.C == null || !part.C.gameObject.activeSelf)
		{
			// N5: the part it was going for broke (its swings, another companion's, or the pop): mined, the drops around
			// it, then the chooser
			if (part.C != null)
			{
				c.ById.Remove(part.C.GetInstanceID());
				c.RockById.Remove(part.C.GetInstanceID());   // J2
			}
			if (!part.IsRock)
			{
				c.Mined++;
			}
			c.ProgAt = Time.time;
			c.DirtAt = -100f;
			OvPartUnclaim(part);   // F9
			if (CompanionsPlugin.LogNormal) LogInfo($"Mined the part \"{(part.C != null ? part.C.name : "?")}\" of \"{_vp.Name}\" - picking up, then looking at what still holds it up");
			OvClOff();
			OvPartsLook(c, true);
			OvPartDrops(part.Centre);
			return;
		}
		if (part.Holder != this && part.Holder2 != this)
		{
			// SV1: another companion took the part over while this one wasn't working it (stuck on the way): let it go
			// F9: it is in neither of the part's two places
			if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{part.C.name}\" was taken over by {(part.Holder != null ? part.Holder.OvName() : part.Holder2 != null ? part.Holder2.OvName() : "another companion")} (this one was stuck on the way to it) - picking again");
			_ai?.StopMoving();
			OvClOff();
			return;
		}
		// Z4: a round from a ladder - planning, wood, building and the climb, then the part swing below
		if (_clLadder && OvLnTick(c, part))
		{
			return;
		}
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (_dgActive && _dgClear && _dgPart && !_dgSwung && !_clLadder && OvOffPlace(_clStand, OvPlaceOff))
		{
			// S3: moved off its place (a fight, a push) - back to it first, its swing count kept
			OvDigPause("the part");
			_clWalk = true;
			_moveTimer = 0f;
			return;
		}
		if (_dgActive && _dgClear)
		{
			OvDigTick();
			if (_dgActive)
			{
				return;
			}
			_dgClear = false;
			if (_state != HarvestState.Vein)
			{
				return;   // a swing hit a thing and handed it to the Wood / Stone flow: the round goes on after it
			}
			// K3: why the round ended
			OvClFail(_clRockWhy != null ? _clRockWhy + $" (from {MHR_Mover.Fmt(me)})" : _dgLimit ? $"digging toward it from {MHR_Mover.Fmt(me)} reached the game's 8 m dig limit" : _dgPartMiss >= OvPartMissMax ? $"{OvPartMissMax} swings from {MHR_Mover.Fmt(me)} without hitting it" : $"{OvNoProgSwings} swings in a row from {MHR_Mover.Fmt(me)} neither hit it nor dug toward it");
			return;
		}
		if (_clLadder)
		{
			OvClFail("its swings from the ladder were cut short");
			return;
		}
		if (_clWalk)
		{
			float flat = Flat(me, _clStand);
			if (OvOffPlace(_clStand))
			{
				float up = _clStand.y - me.y;
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				bool tooHigh = up >= OvRampRatio * hh && flat <= OvRampReach;
				// S2 (the radar; test of 05e19c2: the walk to a part never looked at what was in front, only waited for the
				// mover to give up): within 3 m of the place, a step up or more above its feet, and the map's squares in front
				// read a wall - the way up from here now, once per round (R1.3's check on trench walks). Not for something it
				// never swings at (can't be damaged, a built piece, this vein's own rock): the walk goes on, the mover's
				// give-up ends it. The way up's own R1.2 strikes a plain rock or hands a thing over; when it ends, this walk
				// goes on.
				// MP4: the map's route there blocked by one step too high - the way up at that step's foot
				if (OvRtWayUpWalk(_clStand, ref _clMpTried, ref _clMpOn, ref _clMpFoot, ref _clMpTop, dt, "the place beside the part"))
				{
					return;
				}
				if (!_clRampTried && _vp?.Map != null && flat <= OvRampReach && up >= OvStepUp && OvMapWallAhead(_clStand, out byte wk, out Collider wc, out _) == 1)
				{
					byte hk = wc != null ? OvMapKind(_vp, _vp.Map, wc) : wk;
					MineRock5 wm = wc != null ? wc.GetComponentInParent<MineRock5>() : null;
					if (hk != MkFixed && hk != MkBuilt && (wm == null || !_vp.Rocks.Contains(wm)))
					{
						_clRampTried = true;
						_ai?.StopMoving();
						OvRampBegin(_clStand, null, outOfTrench: false, $"the map reads {OvMapKindName(wk)} in front on the way to the place beside the part, {up:F1} m up, {flat:F1} m away");
						return;
					}
				}
				// P3: the place to stand is the bottom of the pit - no steps cut toward it (HH3 removed)
				if (tooHigh || OvWalkGaveUp(dt, _clStand))
				{
					OvClFail($"couldn't get to {MHR_Mover.Fmt(_clStand)} beside it ({flat:F1} m away, {up:F1} m up)");
					return;
				}
				OvWalk(_clStand, 0.25f, flat > 8f, dt);
				return;
			}
			_clWalk = false;
			_ai?.StopMoving();
		}
		else if (Flat(me, _clStand) > 1.2f)
		{
			_clWalk = true;   // pushed off its place (or back from clearing a thing): back to it
			_moveTimer = 0f;
			return;
		}
		// M1: the swing at the point where the game's own swing from its place meets the part's rock (OvStandFor) - not
		// the nearest point of its box; 3 swings that neither hit it nor dig toward it end the round (K3)
		Vector3 bp = _clAim;
		OvDigBegin(bp, null, ramp: false);
		OvDigResume();
		_dgClear = true;
		_dgPart = true;
		_dgGoalY = me.y;
	}

	// the round on _clFor ended without it breaking: it waits 1 min (GG3c, never given up)
	private void OvClFail(string why, bool wait = false)
	{
		OvPart root = _clFor;
		bool ladder = _clLadder;
		OvClOff();
		if (root == null || _vp == null)
		{
			return;
		}
		if (root.IsRock && ladder)
		{
			root.LadderGaveUp = true;   // V3: its ladder round ended without it breaking
			OvClear vc = OvC();
			if (vc != null)
			{
				OvV4Mark(vc, root, why);   // V4: not from a ladder either - now, not at its next try (it is set aside below)
			}
		}
		OvPartUnclaim(root);   // F9
		OvPartCool(root, why, wait);
		OvPartsLook(OvC(), true);
	}

	// SV3: the stuck stop is undone - every point and part that failed before now gets its try again (ProgAt = now), the
	// set-aside points may come back once more (8b), and the stop is said again if it comes again
	private void OvStopUndo(OvClear c)
	{
		OvMap m = _vp.Map;
		Vector3 at = Vector3.zero;
		float best = -1f;
		if (m.ChangedAt != null)
		{
			for (int i = 0; i < m.ChangedAt.Length; i++)
			{
				if (m.ChangedAt[i] > best)
				{
					best = m.ChangedAt[i];
					at = new Vector3(m.X0 + (i % m.W + 0.5f) * OvMapCell, m.Ground[i], m.Z0 + (i / m.W + 0.5f) * OvMapCell);
				}
			}
		}
		c.Stopped = false;
		c.Done = false;
		c.Said = false;
		c.ProgAt = Time.time;
		c.DirtOnce.Clear();
		foreach (OvPart part in c.Parts)
		{
			part.CoolOnce = false;   // SV7: each may come back once more
		}
		c.DirtAt = -100f;   // the dirt list again now
		if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{_vp.Name}\" starts again: the pit changed at {MHR_Mover.Fmt(at)}");
	}

	// P8 / GG3d: said once per vein
	private void OvClearSay(OvClear c)
	{
		if (c.Said)
		{
			return;
		}
		c.Said = true;
		int left = 0, outN = 0;
		foreach (OvPart part in c.Parts)
		{
			left++;
			if (part.Out)
			{
				outN++;
			}
		}
		if (c.Stopped)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Stuck on \"{_vp.Name}\": every dirt point and part left has failed since the last thing broke or went down - stopping. {c.Dirt.Count} dirt point(s) left:");
			int n = 0;
			foreach (OvDirt d in c.Dirt)
			{
				if (n++ >= 40)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"  ... and {c.Dirt.Count - 40} more");
					break;
				}
				if (CompanionsPlugin.LogNormal) LogInfo($"  dirt at {MHR_Mover.Fmt(d.P)} under \"{(d.Part.C != null ? d.Part.C.name : "?")}\": ground {GroundAt(d.P):F2}, goal {d.Goal:F2} - {(c.DirtWhy.TryGetValue(d.Key, out string w) ? w : "?")}");
			}
			foreach (OvPart part in c.Parts)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"  part \"{part.C.name}\": centre {MHR_Mover.Fmt(part.Centre)}, size {part.Half.x * 2f:F1} x {part.Half.y * 2f:F1} x {part.Half.z * 2f:F1}, bottom {OvPartBottom(part):F1}, ground {GroundAt(part.Centre):F1}, floor {OvFloor(part.Centre):F1}, {(part.Out ? "buried out of reach" : part.Root ? "holds it up (" + part.Why + ")" : part.Held ? "held by other parts" : "not held")}{(part.OutOfReach && !part.Out ? ", too high to hit from the ground beside it" : "")}{(part.CoolWhy != null ? ", last try: " + part.CoolWhy : "")}");
			}
			_talk?.SayProblem("vein:clear_stopped", string.Format(ModLocalization.Loc("hc_speech_vein_clear_stopped"), MHR_Items.NiceName(_vFor)));
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{_vp.Name}\" is done: {c.Mined} part(s) mined, {c.Popped} fell by themselves, {left} left ({outN} buried out of reach{(left > outN ? $", {left - outN} low part(s) with no place to hit them from, left in the ground after the pop (D1)" : "")})");
	}

	// Z2: the root rests on a tree / log / stump / rock that isn't left (the Y6e count) - that thing comes first
	private bool OvHeldByThing(OvPart part)
	{
		GameObject t = part.HoldThing;
		// not one cooling down (FF1: 60 s after someone couldn't clear it), nor one it has no tool for
		return part.Root && t != null && t.activeInHierarchy && !OvPitCooling(t.GetInstanceID()) && FindBestTool(part.HoldMode) != null;
	}

	private bool OvAnyHeldByThing(OvClear c)
	{
		foreach (OvPart part in c.Parts)
		{
			if (!part.Out && OvHeldByThing(part))
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
			if (part.Out || !OvHeldByThing(part))
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
		if (OvExitTriedNear(sp.P))
		{
			return false;
		}
		float h = _character != null ? _character.GetHeight() : 1.8f;
		return OvWallTop(sp) - GroundAt(sp.P) >= OvRampRatio * h;   // Y2
	}

	// R4: the way out cut from this spot (the E2 walk along the floor, then the ramp straight out; OvWayOutTick)
	private void OvWayOutHere(OvSpot sp, string head = null, string why = "a way out first")
	{
		OvPlan p = _vp;
		// T4 (John, 2026-11-20: "updating the existing way down / up"): a saved way out within 15 m whose bottom the trench
		// has gone below is extended - cut from the floor at the trench spot nearest its bottom up to that bottom; the
		// ramp merge (OvRampFinish) then moves its saved bottom down to the floor
		OvRamp ext = OvStrandedNear(sp.P);
		if (ext != null && OvExtendFloor(ext, out Vector3 extFloor))
		{
			p.ExitBy = this;
			_woWhy = why;
			_woCombat = false;
			_woActive = true;
			_woOut = false;
			_woFloor = extFloor;   // W1: on the way out's own line, downhill from its old bottom
			_woGoal = ext.Bottom;
			_moveTimer = 0f;
			_ai?.StopMoving();
			if (CompanionsPlugin.LogNormal) LogInfo($"{head ?? $"No way out of the trench within {OvWayNear:F0} m of where it works"} - extending the way out at {MHR_Mover.Fmt(ext.Bottom)} down its own line to the floor at {MHR_Mover.Fmt(_woFloor)} ({ext.Bottom.y - _woFloor.y:F1} m up to its bottom)");
			return;
		}
		string pickWhy = null;
		if (head == null)
		{
			sp = OvWayOutPick(sp, out pickWhy);   // M2 (not in a fight: that one is cut toward the enemy, Y5)
		}
		p.ExitBy = this;
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
		if (CompanionsPlugin.LogNormal) LogInfo($"{head ?? $"No way out of the trench within {OvWayNear:F0} m of where it works"} - going to {MHR_Mover.Fmt(_woFloor)} (spot {sp.Idx + 1}) to cut one ({_woGoal.y - _woFloor.y:F1} m up){(pickWhy != null ? " - " + pickWhy : "")}");
	}

	// T4: the nearest saved way out within 15 m (flat) of `at` that reached out of the trench but whose bottom the trench
	// floor has gone 1.5 x its height or more below (OvRampOut no longer counts it)
	private OvRamp OvStrandedNear(Vector3 at)
	{
		OvRamp best = null;
		float bd = float.MaxValue;
		foreach (OvRamp r in _vp.Ramps)
		{
			OvSpot bs = OvNearestIn(_vp, r.Bottom);
			// G3 (d) / H4: only one whose top is past the trench and whose bottom stops short of the floor (more than a hop,
			// OvRtHopUp 1.1 m, above it - the MP6 measure) - a way out that fails the map check for another reason isn't extended
			if (r.Top.y < r.Need - OvStepUp || OvRampOut(r) || _rpTried.Contains(r) || !OvRampPastTrench(_vp, r) || bs == null || r.Bottom.y - GroundAt(bs.P) < OvRtHopUp)
			{
				continue;
			}
			float d = Flat(r.Bottom, at);
			if (d <= OvWayNear && d < bd && !OvExitTriedNear(r.Bottom))
			{
				bd = d;
				best = r;
			}
		}
		return best;
	}

	// W1 (test 26: the extension started at the trench spot nearest the old bottom, 1.4 m from it and toward the vein, so the
	// cut ran 6 m along the floor; John: it must follow the original path): the floor start of an extension on the way
	// out's own line - from its old bottom on downhill (away from its top) in 0.5 m steps, at most 15 m, to the first
	// point where the ground is as low as the old ramp's slope (its run per metre of rise, at least 1) reaches there. A
	// step off the pit's ground or into a part's box stops it at the last good point. False = not one good step (the
	// normal way-out cut instead).
	private bool OvExtendFloor(OvRamp r, out Vector3 floor)
	{
		floor = r.Bottom;
		Vector3 dn = r.Bottom - r.Top;
		dn.y = 0f;
		if (dn.sqrMagnitude < 0.01f)
		{
			return false;
		}
		dn.Normalize();
		float rise = r.Top.y - r.Bottom.y;
		float ratio = rise > 0.5f ? Mathf.Max(1f, Flat(r.Top, r.Bottom) / rise) : 1f;
		OvClear c = OvC();
		bool any = false;
		for (float d = 0.5f; d <= OvWayNear + 0.001f; d += 0.5f)
		{
			Vector3 pt = r.Bottom + dn * d;
			pt.y = GroundAt(pt);
			if (!OvInPitPt(pt))
			{
				break;
			}
			bool inBox = false;
			foreach (OvPart part in c.Parts)
			{
				if (new Bounds(part.Centre, part.Half * 2f).Contains(pt + Vector3.up * 0.5f))
				{
					inBox = true;
					break;
				}
			}
			if (inBox)
			{
				break;
			}
			floor = pt;
			any = true;
			if (pt.y <= r.Bottom.y - d / ratio)
			{
				break;
			}
		}
		return any;
	}

	private static readonly RaycastHit[] s_woHits = new RaycastHit[32];

	// M2 (test 19: a way out cut straight into a plain rock, and up a 13 m wall with a 6 m one 10 m away): of the trench
	// spots within 15 m (flat) of the spot where it works with no way-out try within 15 m in the last 60 s (K5 / M3) and a
	// wall outside 1.5 x its height or more above the floor (a lower spot needs no way out - the walk there would only
	// say so), the one with no rock in its line, lowest wall first (the wall top along the line out, Y2, less the floor
	// there). None without a rock: the lowest wall. None at all: the spot it was given.
	private OvSpot OvWayOutPick(OvSpot sp, out string why)
	{
		why = null;
		float h = _character != null ? _character.GetHeight() : 1.8f;
		OvSpot best = null;
		bool bestRock = true;
		float bestClimb = float.MaxValue;
		int looked = 0, rocks = 0;
		foreach (OvSpot q in _vp.Spots)
		{
			// Z5: not a spot whose line out crosses an object that can't be damaged
			if (Flat(q.P, sp.P) > OvWayNear || OvExitTriedNear(q.P) || OvOutBlocked(q))
			{
				continue;
			}
			float floor = GroundAt(q.P);
			float climb = OvWallTop(q) - floor;
			if (climb < OvRampRatio * h)
			{
				continue;
			}
			looked++;
			bool rock = OvRockInLine(q, floor);
			if (rock)
			{
				rocks++;
			}
			if ((!rock && bestRock) || (rock == bestRock && climb < bestClimb))
			{
				best = q;
				bestRock = rock;
				bestClimb = climb;
			}
		}
		if (best == null)
		{
			return sp;
		}
		why = $"the lowest wall within {OvWayNear:F0} m{(bestRock ? " (every line out has a rock in it)" : "")} of {looked} spot(s) looked at, {rocks} with a rock in the line out";
		return best;
	}

	// M2: a rock in the line the way out is cut along - from the floor at the spot to the top of the wall outside it (the
	// ramp's line), at 0.5 m and 1.5 m above it, by the game's physics (solid things, not the ground): something whose
	// drops are stone by the game's drop tables (MHR_Drops), not this vein
	private bool OvRockInLine(OvSpot q, float floor)
	{
		if (s_bodyMask < 0)
		{
			s_bodyMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
		}
		Vector3 from = q.P;
		from.y = floor;
		Vector3 to = OvOutside(q);
		to.y = OvWallTop(q);
		Vector3 d = to - from;
		float len = d.magnitude;
		if (len < 0.1f)
		{
			return false;
		}
		d /= len;
		for (int k = 0; k < 2; k++)
		{
			Vector3 o = from + Vector3.up * (k == 0 ? 0.5f : 1.5f);
			int n = Physics.RaycastNonAlloc(o, d, s_woHits, len, s_bodyMask, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < n; i++)
			{
				GameObject go = MHR_Drops.Root(s_woHits[i].collider);
				if (go == null)
				{
					continue;
				}
				MineRock5 mr = go.GetComponent<MineRock5>();
				if (mr != null && _vp.Rocks.Contains(mr))
				{
					continue;
				}
				if (MHR_Drops.KindOf(go) == MHR_Drops.Stone)
				{
					return true;
				}
			}
		}
		return false;
	}

	// the nearest point of a part's box (the game's box, as the support rule uses) to p
	private static Vector3 OvBoxNear(OvPart part, Vector3 p)
	{
		return new Bounds(part.Centre, part.Half * 2f).ClosestPoint(p);
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
			if (part.Holder == null && part.Holder2 == null && !part.Out)
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
