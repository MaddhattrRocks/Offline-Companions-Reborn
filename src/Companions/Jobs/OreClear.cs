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

		public bool Root;         // holds the vein up (dirt, a solid thing, a lower breakable thing, a buried part)

		public string Why;

		public float CoolUntil;   // GG3c: a try that didn't break it - it waits 1 min

		public float FailAt = -1f;   // GG3d: when a try last failed (the stuck rule); -1 = never

		public bool Wait;         // GG3a: waiting for a hammer / a ladder spot - doesn't count toward stuck

		public string CoolWhy;

		public bool OutOfReach;   // Z3: too high to hit from the pit ground beside it (the last look)

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

	private const float OvBodyPad = 0.35f;         // GG2: its body's half width - a stand point this close to a part's box is in it

	private const float OvStandAt = 0.5f;          // GG2: at its place to stand = within 0.5 m of it (flat)

	private OvPart _rpPart;

	// the round on one part (ore step): the part, where it stands, walking there
	private OvPart _clFor;

	private Vector3 _clStand;

	private bool _clWalk;


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
		float now = Time.time;
		if (now < _clNextPick)
		{
			return true;   // standing by: looked again once a second
		}
		OvDirtScan(c, false);
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
			if (low)
			{
				c.Low.Add(part);   // P5
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

	// P5: a low part still standing (not buried out of reach)
	private static bool OvLowUp(OvPart part)
	{
		return part != null && !part.Out && part.C != null && part.C.gameObject.activeSelf;
	}

	// K1: a low part that only a ladder reaches (OutOfReach, K2) doesn't count - it is left for the last step
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
				if (lr == 1)
				{
					// K1 (John, 2026-11-19: no ladder until the rest of the dirt and low parts are removed): it waits for the
					// last step (OvOreChoose), not counted as a failure and not holding that step back (OvLowLeft)
					OvPartCool(lp, lwhy + " - it waits for the ladder, after the dirt and the other low parts", wait: true);
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
			// P2: the thing waits 60 s (FF1's cooldown), any companion may try it again then
			GameObject t = _rpThing;
			if (CompanionsPlugin.LogNormal) LogInfo($"The ground at the base of \"{(t != null ? t.name : "?")}\": {why}");
			OvDrOff();
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
			// place is not there: it goes on, and the 10 s walk limit ends it)
			if (flat > OvStandAt || me.y - GroundAt(_drStand) > OvStepUp)
			{
				float up = _drStand.y - me.y;
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				bool tooHigh = up >= OvRampRatio * hh && flat <= OvRampReach;
				// P3: the place to stand is the bottom of the pit - no steps cut toward it (HH3 removed)
				if ((tooHigh || (WalkTimer(dt, _drStand) && _moveTimer > 10f)))
				{
					OvDrFail(c, $"couldn't get to {MHR_Mover.Fmt(_drStand)} to dig it ({flat:F1} m away, {up:F1} m up)");
					return;
				}
				WalkGo(_drStand, 0.25f, flat > 8f, dt);
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
	// 0.5 m the walk stops short: 1.3 m) of the dirt point / of the part's box; the LOWEST of them (within 0.3 m of the
	// lowest: the nearest to the companion). A part round also stands outside every standing part's box (a body can't
	// stand in rock), with its feet no higher than the part's top, and when the part's bottom is more than its swing
	// reaches (about 2.6 m) above that lowest place it is from a ladder. 0 = found, 1 = a part from a ladder, 2 = none.
	private int OvStandFor(OvClear c, Vector3 aim, OvPart part, out Vector3 stand, out string why)
	{
		stand = Vector3.zero;
		why = null;
		Vector3 me = base.transform.position;
		float range = FindBestTool(2)?.m_shared?.m_attack != null ? FindBestTool(2).m_shared.m_attack.m_attackRange : 2f;
		float reach = Mathf.Max(0.8f, range - OvStandAt);
		float up = OvPickReach();
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		Bounds box = part != null ? new Bounds(part.Centre, part.Half * 2f) : default;
		float maxFeet = part != null ? part.Centre.y + part.Half.y : float.MaxValue;
		float x0 = part != null ? box.min.x - reach : aim.x - reach, x1 = part != null ? box.max.x + reach : aim.x + reach;
		float z0 = part != null ? box.min.z - reach : aim.z - reach, z1 = part != null ? box.max.z + reach : aim.z + reach;
		List<Vector3> cand = new List<Vector3>();
		float low = float.MaxValue;
		bool any = false;
		for (float x = Mathf.Floor(x0 / OvDirtGrid) * OvDirtGrid; x <= x1 + 0.001f; x += OvDirtGrid)
		{
			for (float z = Mathf.Floor(z0 / OvDirtGrid) * OvDirtGrid; z <= z1 + 0.001f; z += OvDirtGrid)
			{
				Vector3 pt = new Vector3(x, 0f, z);
				Vector3 near = part != null ? box.ClosestPoint(new Vector3(x, box.center.y, z)) : aim;
				if (Flat(pt, near) > reach + 0.01f || !OvInPitPt(pt))
				{
					continue;
				}
				pt.y = GroundAt(pt);
				if (part != null && (OvInAnyBox(c, pt, hh) || pt.y > maxFeet))
				{
					continue;
				}
				any = true;
				cand.Add(pt);
				low = Mathf.Min(low, pt.y);
			}
		}
		if (!any)
		{
			why = part != null ? "no pit ground within its swing of it outside the parts (and no higher than its top)" : $"no pit ground within its swing ({reach:F1} m) of it";
			return 2;
		}
		// K2 (test 18): for a part, only the pit ground from which its bottom is within its swing (a part in the pit wall is
		// mined from the bank beside it); the lowest of those. None: the lowest of all, from a ladder (K1: the last step).
		float lowIn = float.MaxValue;
		if (part != null)
		{
			foreach (Vector3 pt in cand)
			{
				if (aim.y - pt.y <= up)
				{
					lowIn = Mathf.Min(lowIn, pt.y);
				}
			}
		}
		bool fromLadder = part != null && lowIn == float.MaxValue;
		float baseY = part != null && !fromLadder ? lowIn : low;
		float bd = float.MaxValue;
		foreach (Vector3 pt in cand)
		{
			if (pt.y > baseY + 0.3f || pt.y < baseY - 0.001f || (part != null && !fromLadder && aim.y - pt.y > up))
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
		if (fromLadder)
		{
			why = $"its bottom is more than its swing reaches ({up:F1} m) above all the pit ground beside it";
			return 1;
		}
		return 0;
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

	// GG2: a body standing on the ground at pt (its width, its height) would be inside a standing part's box
	private static bool OvInAnyBox(OvClear c, Vector3 pt, float hh)
	{
		foreach (OvPart part in c.Parts)
		{
			if (part.Out || part.C == null || !part.C.gameObject.activeSelf)
			{
				continue;
			}
			if (Mathf.Abs(pt.x - part.Centre.x) <= part.Half.x + OvBodyPad && Mathf.Abs(pt.z - part.Centre.z) <= part.Half.z + OvBodyPad && part.Centre.y - part.Half.y < pt.y + hh && part.Centre.y + part.Half.y > pt.y - 0.1f)
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

	// no dirt left: the lowest standing part (no one else's, not waiting), from the pit ground beside it, or from a ladder
	// when it is too high for that
	private bool OvOreChoose(OvClear c)
	{
		Vector3 me = base.transform.position;
		List<OvPart> cand = new List<OvPart>();
		bool standing = false, others = false;
		foreach (OvPart part in c.Parts)
		{
			if (part.Out)
			{
				continue;
			}
			standing = true;
			if (OvPartCooling(part))
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
		cand.Sort((OvPart a, OvPart b) =>
		{
			int bc = OvPartBottom(a).CompareTo(OvPartBottom(b));
			return bc != 0 ? bc : Flat(me, OvBoxNear(a, me)).CompareTo(Flat(me, OvBoxNear(b, me)));
		});
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
			OvSpot ws = OvClSpotFor(pick);
			if (ws != null && OvWayOutNeeded(ws))
			{
				OvWayOutHere(ws);
				return true;
			}
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

	// GG3c / GG3a: a part that didn't break (or can't get its ladder) waits 1 min; wait = not counted by the stuck rule
	private void OvPartCool(OvPart part, string why, bool wait)
	{
		float now = Time.time;
		part.CoolUntil = now + OvCool;
		part.CoolWhy = why;
		part.Wait = wait;
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
	private void OvClBegin(OvPart part, Vector3 stand, bool ladder, bool low = false)
	{
		_clFor = part;
		part.Holder = this;
		_clStand = stand;
		_clWalk = !ladder;
		_moveTimer = 0f;
		_ai?.StopMoving();
		OvLnBegin(ladder);
		if (CompanionsPlugin.LogNormal) LogInfo($"Mining \"{_vp.Name}\"{(low ? "" : " (no dirt holds it up now)")}: {(low ? "a low part that goes into the ground (P5)" : ladder ? $"a part too high to hit from the ground beside it - from a ladder" : "the lowest part")} \"{part.C.name}\" at {MHR_Mover.Fmt(part.Centre)}, bottom {OvPartBottom(part):F1}{(part.Why != null ? " (" + part.Why + ")" : part.Root ? "" : " (it rests on other parts)")}{(ladder ? "" : $" - from {MHR_Mover.Fmt(stand)}")}");
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
				if (tooHigh || (WalkTimer(dt, _clStand) && _moveTimer > 10f))
				{
					OvClFail($"couldn't get to {MHR_Mover.Fmt(_clStand)} beside it ({flat:F1} m away, {up:F1} m up)");
					return;
				}
				WalkGo(_clStand, 0.25f, flat > 8f, dt);
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
		// R2a's swing at the part itself: the nearest point of its game box; 3 swings that don't hit it end the round
		Vector3 bp = OvBoxNear(part, me);
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
				if (CompanionsPlugin.LogNormal) LogInfo($"  part \"{part.C.name}\": centre {MHR_Mover.Fmt(part.Centre)}, size {part.Half.x * 2f:F1} x {part.Half.y * 2f:F1} x {part.Half.z * 2f:F1}, bottom {OvPartBottom(part):F1}, ground {GroundAt(part.Centre):F1}, floor {OvFloor(part.Centre):F1}, {(part.Out ? "buried out of reach" : part.Root ? "holds it up (" + part.Why + ")" : "hanging on other parts")}{(part.OutOfReach && !part.Out ? ", too high to hit from the ground beside it" : "")}{(part.CoolWhy != null ? ", last try: " + part.CoolWhy : "")}");
			}
			_talk?.SayProblem("vein:clear_stopped", string.Format(ModLocalization.Loc("hc_speech_vein_clear_stopped"), MHR_Items.NiceName(_vFor)));
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{_vp.Name}\" is done: {c.Mined} part(s) mined, {c.Popped} fell by themselves, {left} left ({outN} buried out of reach)");
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
		if (CompanionsPlugin.LogNormal) LogInfo($"{head ?? $"No way out of the trench within {OvWayNear:F0} m of where it works"} - going to {MHR_Mover.Fmt(_woFloor)} (spot {sp.Idx + 1}) to cut one ({_woGoal.y - _woFloor.y:F1} m up)");
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
