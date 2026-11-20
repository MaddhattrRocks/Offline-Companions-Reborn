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

		public float CoolUntil;   // GG3c: a try that didn't break it - it waits 1 min

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
	}

	// GG2: one point of the dirt list
	private sealed class OvDirt
	{
		public long Key;          // its grid cell (0.5 m)

		public Vector3 P;         // x, z of the cell; y = the ground there at the last look

		public float Goal;        // 0.3 m under the part's bottom (not under the game's dig limit)

		public OvPart Part;

		public HarvestController Holder;
	}

	private const float OvPartsEvery = 2f;         // P2: the parts are looked at every 2 s (and after a break)

	private const float OvCool = 60f;              // GG2 / GG3: a point or part that failed waits 1 min

	private const float OvDirtGrid = 0.5f;         // GG2: the dirt grid under each part's box

	private const float OvDirtUnder = 0.3f;        // GG2: dug to 0.3 m under the part's bottom

	private const float OvDirtEvery = 10f;         // GG2: the dirt list is made again every 10 s (and right after a point is done)

	private const float OvStandAt = 0.5f;          // GG2: at its place to stand = within 0.5 m of it (flat)

	private OvPart _rpPart;

	// the round on one part (ore step): the part, where it stands, walking there
	private OvPart _clFor;

	private Vector3 _clStand;

	private bool _clWalk;

	private string _clPopWhy;      // M4: why the last step picked this part (for the log)

	private Vector3 _clAim;        // M1: where the game's swing meets the part's rock from its place

	private Vector3 _standAim;     // M1: OvStandFor's aim for a part

	private bool _standWall;       // W4: OvStandForPart found pit ground where its body fits, but no swing from it meets the rock

	private bool _clWall;          // W4: this round is John's way - right up to the wall facing the part, swing, step in, again

	private float _clHwBest, _clHwTime;   // W4: the walk up to the wall (E4's headway check)


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

		public bool Done;

		public bool Stopped;

		public bool Said;

		// GG2: the dirt list and its points' cooldowns (kept across looks by grid cell)
		public readonly List<OvDirt> Dirt = new List<OvDirt>();

		public float DirtAt = -100f;

		public readonly Dictionary<long, float> DirtCool = new Dictionary<long, float>();

		public readonly Dictionary<long, float> DirtFail = new Dictionary<long, float>();

		public readonly Dictionary<long, string> DirtWhy = new Dictionary<long, string>();

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
					if (c.ById.TryGetValue(id, out OvPart gone) && gone.Holder == null)
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
		if (OvPopped(c))
		{
			OvPitOpen();   // F1 / U6: from the pop, the pit is open for its stone and wood piles
		}
		if (_lsItem != null)
		{
			OvLooseTick(c);   // F2: the loose item it is going for
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
		foreach (OvDirt d in c.Dirt)
		{
			if (d.Holder != null && d.Holder._drSpot == d && d.Holder._vp == _vp)
			{
				held[d.Key] = d.Holder;
			}
		}
		Dictionary<long, OvDirt> byKey = new Dictionary<long, OvDirt>();
		c.Low.Clear();
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

	private bool OvDirtCooling(OvClear c, OvDirt d)
	{
		return c.DirtCool.TryGetValue(d.Key, out float until) && Time.time < until;
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
	private sealed class OvLoose
	{
		public ItemDrop D;

		public string Name;

		public bool Ore;

		public HarvestController Holder;

		public float CoolUntil;
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
	// and wood lying on the pit ground (the ring and the trench), tossed or not - except ore on the ore dump (the dump
	// carry takes that to storage).
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
		bool hasDump = PitDumpFor(out Vector3 dump);
		int n = Physics.OverlapSphereNonAlloc(_vp.Center, r, s_lsBuf, _itemLayerMask);
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
			if (!OvInPitPt(at))
			{
				continue;
			}
			d.Load();
			string name = MHR_GatherRules.DropName(d);
			bool ore = MHR_GatherRules.IsOreItem(d.m_itemData);
			if (!ore && name != "Stone" && name != "Wood")
			{
				continue;
			}
			if (ore && hasDump && Flat(at, dump) <= 2.5f)
			{
				seen.Remove(id);
				continue;   // on the ore dump
			}
			if (!c.Loose.ContainsKey(id))
			{
				c.Loose[id] = new OvLoose { D = d, Name = name, Ore = ore };
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
		}
	}

	private static bool OvLooseHere(OvLoose l)
	{
		return l.D != null && l.D.gameObject.activeInHierarchy && l.D.m_itemData != null;
	}

	// 0 = it takes it now, 1 = not now (someone else's, cooling, not enough for a pile), 2 = only its weight / bag stops it
	private int OvLooseTakes(OvClear c, OvLoose l, Inventory inv, Dictionary<string, int> pileOk)
	{
		if (!OvLooseHere(l) || (l.Holder != null && l.Holder != this && l.Holder._lsItem == l) || Time.time < l.CoolUntil)
		{
			return 1;
		}
		if (!l.Ore)
		{
			// stone / wood: only while what lies in the pit and what it carries make one more pile it can build now (a
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
						if (o.Name == l.Name && OvLooseHere(o) && Time.time >= o.CoolUntil)
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
		// a pickup never takes it over its weight limit
		if (inv.GetTotalWeight() + l.D.m_itemData.GetWeight() > HeavyLimit || !inv.CanAddItem(l.D.m_itemData))
		{
			return 2;
		}
		return 0;
	}

	// true = busy with the list (going for an item, or taking its ore to the dump to make room); false = nothing on it it
	// can take
	private bool OvLooseTick(OvClear c)
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
					if (CompanionsPlugin.LogDetailed) Log($"Picked up {count}x {MHR_List.Local(what)} in the pit ({c.Loose.Count} on the pit's list)");
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
			if (d < bd)
			{
				bd = d;
				best = l;
			}
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
		// full: ore still in the pit that only its weight stops - its ore to the dump first (the existing dump trip), then on
		if (heavyLeft && MHR_GatherRules.HasOre(_humanoid) && OvHeavyDump())
		{
			if (CompanionsPlugin.LogNormal) LogInfo("Full with ore from the pit - taking it to the ore dump, then back to the pit's list");
			return true;
		}
		// another companion is still going for an item: the vein isn't done yet (it stands by)
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

	// a failed try: the item waits 60 s (not a permanent skip) and goes back on the list
	private void OvLooseCool(OvLoose l, string why)
	{
		l.CoolUntil = Time.time + OvCool;
		if (CompanionsPlugin.LogNormal) LogInfo($"The loose \"{(l.D != null ? MHR_List.Local(l.D.m_itemData.m_shared.m_name) : "?")}\" in the pit waits 60 s: {why}");
		OvLooseOff();
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
		foreach (OvDirt d in c.Dirt)
		{
			// K4: a point already down to its goal (the dig's own test, OvDrTick) isn't picked - the list is made again soon
			if (OvDirtCooling(c, d) || GroundAt(d.P) <= d.Goal + OvDigGain)
			{
				continue;
			}
			if (d.Holder != null && d.Holder != this && d.Holder._drSpot == d && d.Holder._vp == _vp)
			{
				others = true;
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
				continue;
			}
			lows.Add(part);
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
				if (lr == 1 && _standWall)
				{
					OvSpot wws = OvClSpotFor(lp);
					if (wws != null && OvWayOutNeeded(wws))
					{
						OvWayOutHere(wws);
						return true;
					}
					OvClBegin(lp, lstand, ladder: false, low: true, wall: true);   // W4
					return true;
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
			int r = OvStandFor(c, new Vector3(d.P.x, GroundAt(d.P), d.P.z), null, out Vector3 stand, out string why);
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
			d.Holder = this;
			_drSpot = d;
			_drStand = stand;
			_drWalk = true;
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
			return true;   // the rest are other companions' - it stands by
		}
		OvStuckCheck(c);
		return true;
	}

	private void OvDirtCool(OvClear c, OvDirt d, string why)
	{
		float now = Time.time;
		c.DirtCool[d.Key] = now + OvCool;
		c.DirtFail[d.Key] = now;
		c.DirtWhy[d.Key] = why;
		if (d.Holder == this)
		{
			d.Holder = null;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} (under \"{(d.Part.C != null ? d.Part.C.name : "?")}\") waits {OvCool:F0} s: {why}");
	}

	private void OvDrOff()
	{
		if (_drSpot != null && _drSpot.Holder == this)
		{
			_drSpot.Holder = null;
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
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
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
			if (reached)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"The dirt at {MHR_Mover.Fmt(d.P)} is down to {GroundAt(d.P):F2} (under \"{d.Part.C.name}\"'s bottom {OvPartBottom(d.Part):F2})");
				OvDrOff();
				c.DirtAt = Mathf.Min(c.DirtAt, Time.time - OvDirtEvery + 0.5f);   // the list again in 0.5 s
				return;
			}
			OvDrFail(c, limit ? "it is at the game's 8 m dig limit" : $"{OvNoProgSwings} swings didn't lower the ground there");
			return;
		}
		if (_drWalk)
		{
			float flat = Flat(me, _drStand);
			// HH2: there = within 0.5 m (flat) and on that ground - feet no more than a step above the ground there now (standing on a part above the
			// place is not there: it goes on, and the mover's give-up (Z4) ends it)
			if (flat > OvStandAt || me.y - GroundAt(_drStand) > OvStepUp)
			{
				float up = _drStand.y - me.y;
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				bool tooHigh = up >= OvRampRatio * hh && flat <= OvRampReach;
				// P3: the place to stand is the bottom of the pit - no steps cut toward it (HH3 removed)
				if (tooHigh || OvWalkGaveUp(dt, _drStand))
				{
					OvDrFail(c, $"couldn't get to {MHR_Mover.Fmt(_drStand)} to dig it ({flat:F1} m away, {up:F1} m up)");
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
		OvDigBegin(new Vector3(d.P.x, g, d.P.z), null, ramp: false);
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
	private int OvStandFor(OvClear c, Vector3 aim, OvPart part, out Vector3 stand, out string why)
	{
		if (part != null)
		{
			return OvStandForPart(part, out stand, out why);
		}
		stand = Vector3.zero;
		why = null;
		Vector3 me = base.transform.position;
		float range = FindBestTool(2)?.m_shared?.m_attack != null ? FindBestTool(2).m_shared.m_attack.m_attackRange : 2f;
		float reach = Mathf.Max(0.8f, range - OvStandAt);
		List<Vector3> cand = new List<Vector3>();
		float low = float.MaxValue;
		for (float x = Mathf.Floor((aim.x - reach) / OvDirtGrid) * OvDirtGrid; x <= aim.x + reach + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor((aim.z - reach) / OvDirtGrid) * OvDirtGrid; z <= aim.z + reach + 0.001f; z += OvDirtGrid)
			{
				Vector3 pt = new Vector3(x, 0f, z);
				if (Flat(pt, aim) > reach + 0.01f || !OvInPitPt(pt))
				{
					continue;
				}
				pt.y = GroundAt(pt);
				cand.Add(pt);
				low = Mathf.Min(low, pt.y);
			}
		}
		if (cand.Count == 0)
		{
			why = $"no pit ground within its swing ({reach:F1} m) of it";
			return 2;
		}
		float bd = float.MaxValue;
		foreach (Vector3 pt in cand)
		{
			if (pt.y > low + 0.3f)
			{
				continue;
			}
			float d = Flat(me, pt) + Mathf.Abs(me.y - pt.y);
			if (d < bd)
			{
				bd = d;
				stand = pt;
			}
		}
		return 0;
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
	private int OvStandForPart(OvPart part, out Vector3 stand, out string why)
	{
		stand = Vector3.zero;
		why = null;
		_standAim = part.Centre;
		_standWall = false;
		bool low = OvIsLow(part);   // L4
		Vector3 me = base.transform.position;
		Attack a = FindBestTool(2)?.m_shared?.m_attack;
		float range = a != null ? a.m_attackRange : 1.8f;
		float reach = range + OvStandAt;
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		float rad = _character != null ? _character.GetRadius() : 0.4f;
		Bounds box = new Bounds(part.Centre, part.Half * 2f);
		float maxFeet = box.max.y;
		float pickReach = OvPickReach();
		int tooLow = 0;
		List<Vector3> cand = new List<Vector3>();
		for (float x = Mathf.Floor((box.min.x - reach) / OvDirtGrid) * OvDirtGrid; x <= box.max.x + reach + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor((box.min.z - reach) / OvDirtGrid) * OvDirtGrid; z <= box.max.z + reach + 0.001f; z += OvDirtGrid)
			{
				Vector3 pt = new Vector3(x, 0f, z);
				if (Flat(pt, box.ClosestPoint(new Vector3(x, box.center.y, z))) > reach + 0.01f || !OvInPitPt(pt))
				{
					continue;
				}
				pt.y = GroundAt(pt);
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
				cand.Add(pt);
			}
		}
		if (cand.Count == 0 && tooLow > 0)
		{
			why = $"no pit ground beside it high enough for the pickaxe to reach its bottom ({tooLow} too low)";
			return 1;   // as before P1: pit ground, but no swing from it reaches (the ladder's case)
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
		int tested = 0, noFit = 0;
		bool found = false;
		Vector3 wallAt = Vector3.zero;   // W4: of the places looked at where its body fits, the nearest (flat) to the part's box
		float wd = float.MaxValue;
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
			float bdist = Flat(pt, box.ClosestPoint(new Vector3(pt.x, box.center.y, pt.z)));
			if (bdist < wd)
			{
				wd = bdist;
				wallAt = pt;
			}
			if (!OvSwingReaches(a, pt, part, out Vector3 hit, lowDirt: low))
			{
				continue;
			}
			if (!found)
			{
				found = true;
				band = pt.y + 0.3f;
			}
			float d = Flat(me, pt) + Mathf.Abs(me.y - pt.y);
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
		stand = cand[0];
		if (wd < float.MaxValue)
		{
			stand = wallAt;      // W4: from here it walks right up to the wall facing the part and swings
			_standWall = true;
		}
		why = $"no place on the pit ground beside it where the game's pickaxe swing meets its rock first ({tested} looked at{(noFit > 0 ? $", {noFit} with no room for its body" : "")})";
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
			if (!part.C.Raycast(new Ray(o, dir), out RaycastHit rh, range))
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
				Vector3 hp = h.point == Vector3.zero && h.distance == 0f ? o : h.point;
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
	private bool OvPartCooling(OvPart part)
	{
		return Time.time < part.CoolUntil;
	}

	// no dirt and no low parts left (John, 2026-11-20: the pop part only once the low parts are cleared): M4 - the part
	// whose break leaves the fewest held by the game's support check (none = it pops the vein), nearest first; no one
	// else's, not waiting; from the pit ground where the game's swing meets its rock (M1), or from a ladder when none does
	private bool OvOreChoose(OvClear c)
	{
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		bool standing = false, others = false;
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
				continue;
			}
			cand.Add(part);
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
			if (r == 1 && OvIsLow(pick) && _standWall)
			{
				OvSpot wws = OvClSpotFor(pick);
				if (wws != null && OvWayOutNeeded(wws))
				{
					OvWayOutHere(wws);
					return true;
				}
				OvClBegin(pick, stand, ladder: false, low: true, wall: true);   // W4
				return true;
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
			_clPopWhy = left == 0 ? $"the part that pops it: by the game's support check nothing of the vein stays held once it breaks ({c.HeldCount} held now)" : $"the part whose break drops the most: by the game's support check {left} of the {c.HeldCount} held part(s) stay held once it breaks";
			OvClBegin(pick, stand, ladder: r == 1);
			return true;
		}
		_clNextPick = Time.time + 1f;
		_ai?.StopMoving();
		if (cand.Count > 4 || others)
		{
			return true;
		}
		OvStuckCheck(c);
		return true;
	}

	// X2: the nearest part whose box touches an indestructible world object (not buried, no one on it, not waiting) - mined
	// with the normal part round: a place where the swing meets its rock, else John's way from the wall (W4); none = it
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
			if (r == 0 || (r == 1 && _standWall))
			{
				OvSpot ws = OvClSpotFor(pick);
				if (ws != null && OvWayOutNeeded(ws))
				{
					OvWayOutHere(ws);
					return true;
				}
				_clPopWhy = "a part held by a thing that can't be damaged (X2)";
				OvClBegin(pick, stand, ladder: false, low: OvIsLow(pick), wall: r == 1);
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
		part.CoolUntil = now + OvCool;
		part.CoolWhy = why;
		part.Wait = wait;
		part.LadderWait = false;
		if (!wait)
		{
			part.FailAt = now;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The part \"{(part.C != null ? part.C.name : "?")}\" of \"{_vp.Name}\" waits {OvCool:F0} s: {why}");
	}

	// GG3d (John's rule: two tries with nothing broken = stop and wait): nothing to take now, nothing another companion is
	// on - when every dirt point and part waiting has failed since the last thing that broke or went down (and not just
	// for a hammer or a ladder spot), the vein work stops here and says why, point by point and part by part
	private void OvStuckCheck(OvClear c)
	{
		float now = Time.time;
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
		c.Stopped = true;
		c.Done = true;
		OvClearSay(c);
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
	private void OvClBegin(OvPart part, Vector3 stand, bool ladder, bool low = false, bool wall = false)
	{
		_clFor = part;
		part.Holder = this;
		_clStand = stand;
		_clAim = _standAim;
		_clWalk = !ladder;
		_clWall = wall;
		_clHwBest = float.MaxValue;
		_clHwTime = 0f;
		_moveTimer = 0f;
		_ai?.StopMoving();
		OvLnBegin(ladder);
		if (CompanionsPlugin.LogNormal) LogInfo($"Mining \"{_vp.Name}\"{(low ? "" : " (no dirt holds it up now)")}: {(low ? "a low part that goes into the ground (P5)" : ladder ? $"a part too high to hit from the ground beside it - from a ladder" : _clPopWhy ?? "the lowest part")} \"{part.C.name}\" at {MHR_Mover.Fmt(part.Centre)}, bottom {OvPartBottom(part):F1}{(part.Why != null ? " (" + part.Why + ")" : part.Held ? " (held by other parts)" : " (not held - it falls when a part breaks)")}{(ladder ? "" : wall ? $" - no place where the swing meets its rock yet: from {MHR_Mover.Fmt(stand)} right up to the wall facing it, swinging, stepping in (W4)" : $" - from {MHR_Mover.Fmt(stand)}, swinging at its rock at {MHR_Mover.Fmt(_standAim)}")}");
	}

	private void OvClOff()
	{
		_clFor = null;
		_clWalk = false;
		_clWall = false;
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
			}
			c.Mined++;
			c.ProgAt = Time.time;
			c.DirtAt = -100f;
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
			if (_clWall && _dgDamage && !_dgLimit)
			{
				// W4: those swings dug the dirt toward it (or hit its rock) - right up to the wall again and on
				_clHwBest = float.MaxValue;
				_clHwTime = 0f;
				if (CompanionsPlugin.LogNormal) LogInfo($"Dug toward \"{part.C.name}\" - stepping up to the wall again");
				return;
			}
			// K3: why the round ended
			OvClFail(_dgLimit ? $"digging toward it from {MHR_Mover.Fmt(me)} reached the game's 8 m dig limit" : _dgPartMiss >= OvPartMissMax ? $"{OvPartMissMax} swings from {MHR_Mover.Fmt(me)} without hitting it" : $"{OvNoProgSwings} swings in a row from {MHR_Mover.Fmt(me)} neither hit it nor dug toward it");
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
			if (flat > OvStandAt)
			{
				float up = _clStand.y - me.y;
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				bool tooHigh = up >= OvRampRatio * hh && flat <= OvRampReach;
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
		else if (!_clWall && Flat(me, _clStand) > 1.2f)   // W4: stepping in toward the part moves it off its place
		{
			_clWalk = true;   // pushed off its place (or back from clearing a thing): back to it
			_moveTimer = 0f;
			return;
		}
		if (_clWall)
		{
			// W4 (John, test 26: "Step right up to the wall and swing"): the aim is the part's rock - where a line from its
			// swing height toward the part's centre meets the part's own collider (never its box). It walks at it until it
			// is against the wall (E4: 1 s without getting 0.3 m closer, flat) or the rock is within its swing, then swings.
			Attack wa = FindBestTool(2)?.m_shared?.m_attack;
			float wRange = wa != null ? wa.m_attackRange : 1.8f;
			Vector3 eye = me + Vector3.up * (wa != null ? wa.m_attackHeight : 1f);
			Vector3 toC = part.Centre - eye;
			Vector3 rock = part.Centre;
			if (toC.sqrMagnitude > 0.0001f && part.C.Raycast(new Ray(eye, toC.normalized), out RaycastHit wh, toC.magnitude + 20f))
			{
				rock = wh.point;
			}
			_clAim = rock;
			float wf = Flat(me, rock);
			if (wf < _clHwBest - OvHeadwayGain)
			{
				_clHwBest = wf;
				_clHwTime = 0f;
			}
			else
			{
				_clHwTime += dt;
			}
			if (Vector3.Distance(eye, rock) > wRange && _clHwTime < OvRampHeadway)
			{
				Vector3 wd = rock - me;
				wd.y = 0f;
				Vector3 step = me + (wd.sqrMagnitude > 0.0001f ? wd.normalized : base.transform.forward) * 1.5f;
				step.y = Mathf.Min(GroundAt(step), me.y + OvStepUp);
				WalkGo(step, 0.3f, false, dt);
				return;
			}
			_ai?.StopMoving();
		}
		// M1: the swing at the point where the game's own swing from its place meets the part's rock (OvStandFor) - not
		// the nearest point of its box; 3 swings that neither hit it nor dig toward it end the round (K3)
		Vector3 bp = _clAim;
		OvDigBegin(bp, null, ramp: false);
		_dgClear = true;
		_dgPart = true;
		_dgGoalY = me.y;
	}

	// the round on _clFor ended without it breaking: it waits 1 min (GG3c, never given up)
	private void OvClFail(string why, bool wait = false)
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
		OvPartCool(root, why, wait);
		OvPartsLook(OvC(), true);
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
			if (r.Top.y < r.Need - OvStepUp || OvRampOut(r) || _rpTried.Contains(r))
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
			if (part.Holder == null && !part.Out)
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
