using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 (Ore job rebuild, build 1 - John's approved list reviews/1.5.97-ore-rebuild-build1-list.md, 2026-11-17):
// the Ore job on a split vein outdoors. This file only decides WHAT is next; it never walks, climbs or swings by itself:
//  - a tree, log, stump, bush or rock in the trench is handed to the Wood / Stone job's own flow (UpdateMoving /
//    UpdateAttacking with WorkMode), which aims, picks the tool, rests, swings and gives up exactly as those jobs do;
//  - dirt (a trench spot, a ramp step) is walked to with WalkTo's halves (WalkTimer / WalkGo) and swung at with SwingNow,
//    the same code the Ore / Stone jobs use, and judged by the ground getting 5 cm lower;
//  - the way up is a ramp of dirt steps (John's way: hit the wall in front, step in, hit again) - no climbing on a vein.
// Build 1 stops when the trench is done (it says so and stands by); clearing and the pop come in build 2.
public partial class HarvestController
{
	// ---------- the shared plan (one per vein; touching deposits within 4 m join it) ----------
	private sealed class OvSpot
	{
		public Vector3 P;

		public int Idx;

		public float Floor;

		public float Target;

		public float LapStart;

		public float LapDepth;

		public bool Closed;

		public string ClosedWhy;

		public bool Done;

		public string DoneWhy;

		public bool Skip;

		public string SkipWhy;

		public bool RampTried;

		public HarvestController Holder;
	}

	private sealed class OvRamp
	{
		public Vector3 Bottom;

		public Vector3 Top;
	}

	private sealed class OvPlan
	{
		public readonly List<MineRock5> Rocks = new List<MineRock5>();

		public readonly List<OvSpot> Spots = new List<OvSpot>();

		public readonly List<Vector2> Poly = new List<Vector2>();

		public readonly List<OvRamp> Ramps = new List<OvRamp>();

		public readonly Dictionary<HarvestController, float> Workers = new Dictionary<HarvestController, float>();

		public Vector3 Center;

		public float Goal;

		public int Seen;

		public int Lap = 1;

		public bool RingFixed;

		public bool Done;

		public bool DoneSaid;

		public float LastWork;

		public float GrowAt;

		public string Name;

		public Bounds Box;
	}

	private const float OvRingOut = 1.0f;          // C2.2: the ring 1 m outside the seen parts

	private const float OvSpacing = 1.0f;          // C2.2: a spot every 1 m

	private const float OvJoin = 4.0f;             // C2.1: deposits within 4 m join

	private const float OvGrowEvery = 2.0f;        // C2.3

	private const float OvKeep = 0.5f;             // C2.3: work kept for a new spot within 0.5 m of an old one

	private const float OvFloorMargin = 0.15f;     // C2.5

	private const float OvWaterFloor = 29.5f;      // C2.5

	private const float OvGoalUnder = 1.0f;        // C2.6: the trench goal, 1 m under the vein's lowest part

	private const float OvLapStep = 2.0f;          // C2.7

	private const int OvMaxLaps = 5;               // C2.7

	private const float OvLapMinGain = 0.3f;       // C2.7

	private const float OvPlanKeep = 3600f;        // C2.10: kept 1 hour after the last companion leaves

	private const float OvThingRadius = 2.0f;      // C3.2

	private const float OvRampRatio = 1.5f;        // C3.3: 1.5 x its height up: the way up at once

	private const float OvNoHeadway = 3f;          // C4.1

	private const float OvStepUp = 0.7f;           // C4.1 / C5: a step it walks or hops (dig it when higher)

	private const float OvDigGain = 0.05f;         // C4.3

	private const int OvNoProgSwings = 3;          // C4.4 / C5

	private const float OvRampAhead = 1.0f;        // C5: it looks 1 m ahead

	private const float OvRampTime = 60f;          // C5: 60 s without 0.3 m higher

	private const float OvRampRise = 0.3f;

	private const float OvRampSave = 1.5f;         // C5: a ramp that rose 1.5 m is saved

	private const float OvRampNear = 10f;          // C5: a saved ramp's bottom within 10 m

	private const float OvRampTopNear = 5f;        // C5: ... and its top within 5 m of where it's going

	private const float OvOutDeep = 1.5f;          // C6: 1.5 m under the ground outside the ring: in the trench

	private const float OvOutSide = 3f;            // C6: 3 m outside the ring

	private const float OvDigGate = 2.0f;          // C8

	private static readonly List<OvPlan> s_ovPlans = new List<OvPlan>();

	// ---------- this companion's state ----------
	private GameObject _vFor;

	private MineRock5 _vRock;

	private OvPlan _vp;

	private int _vFirstSide = -1;

	private OvSpot _ovSpot;

	private int _ovIdx = -1;

	private int _ovDir;

	private bool _ovTurned;

	private int _ovTurnLap;

	private GameObject _ovHand;

	private OvSpot _ovHandSpot;

	private int _ovHandMode;

	private bool _ovDoneSaid;

	// C6: getting out before letting go of the vein
	private bool _ovOut;

	private string _ovOutWhy;

	// stuck in its trench: it waits for you (set a job, or move it 5 m)
	private bool _stuckInHole;

	private Vector3 _stuckAt;

	// SayGaveUp (HarvestMining.cs) writes these

	private string _lastGiveUpText = "";

	// no working pickaxe: a working one in the bag sends it back (NoToolTick)
	private GameObject _noToolRock;

	private float _noToolNext;

	// C4: the dirt it is working
	private bool _dgActive;

	private Vector3 _dgPoint;

	private OvSpot _dgSpot;

	private bool _dgRamp;

	private bool _dgWalking;

	private bool _dgSwung;

	private float _dgSwungAt;

	private float _dgBefore;

	private int _dgNoProg;

	private bool _dgLastProg;

	// C5: the ramp
	private bool _rpActive;

	private Vector3 _rpGoal;

	private OvSpot _rpSpot;

	private bool _rpOut;

	private Vector3 _rpStart;

	private float _rpBestY;

	private float _rpBestAt;

	private int _rpNoProg;

	private Vector3 _rpStepPt;

	private int _rpSwings;

	private OvRamp _rpSaved;

	private readonly HashSet<OvRamp> _rpTried = new HashSet<OvRamp>();

	// part 1 G3 (kept): more than 20 m outside the vein while the vein work moves it - back to it
	private const float LeashOut = 20f;

	private const float LeashNear = 10f;

	private const float LeashBackTime = 60f;

	private int _leashId;

	private int _leashCount;

	private bool _leashBack;

	private float _leashSince;

	private Bounds _vBox;

	private float _vBoxAt = -100f;

	private static readonly List<MHR_Vein.Area> s_vAreas = new List<MHR_Vein.Area>();

	private static readonly Collider[] s_vBuf = new Collider[64];

	private static int s_vMask = -1;

	private static int s_vTerrain = -1;

	private static int s_ovThingMask;

	private static readonly Collider[] s_ovThingBuf = new Collider[48];

	private static readonly System.Reflection.FieldInfo s_tLevelDelta = HarmonyLib.AccessTools.Field(typeof(TerrainComp), "m_levelDelta");

	private static readonly System.Reflection.FieldInfo s_tSmoothDelta = HarmonyLib.AccessTools.Field(typeof(TerrainComp), "m_smoothDelta");

	private static float Flat(Vector3 a, Vector3 b)
	{
		return new Vector2(a.x - b.x, a.z - b.z).magnitude;
	}

	// B3: the mode of what it is working - a thing handed to the Wood (1) or Stone (2) flow, else the job's own
	private int WorkMode()
	{
		return (_vFor != null && _ovHandMode != 0 && _state != HarvestState.Vein) ? _ovHandMode : GetMode();
	}

	// vein work is on (a dirt swing may dig)
	internal bool VeinWorking => _vFor != null && _state == HarvestState.Vein;

	// ---------- the floor (C2.5) ----------
	// the lowest the game lets the ground at p go: 8 m under its first height (TerrainComp keeps the change within 8 m)
	private static float TFloorAt(Vector3 p)
	{
		float g = GroundAt(p);
		Heightmap hm = Heightmap.FindHeightmap(p);
		TerrainComp tc = TerrainComp.FindTerrainCompiler(p);
		if (hm == null || tc == null)
		{
			return g - 8f;
		}
		float[] level = s_tLevelDelta?.GetValue(tc) as float[], smooth = s_tSmoothDelta?.GetValue(tc) as float[];
		if (level == null || smooth == null)
		{
			return g - 8f;
		}
		hm.WorldToVertex(p, out int x, out int y);
		int i = y * (hm.m_width + 1) + x;
		if (i < 0 || i >= level.Length || i >= smooth.Length)
		{
			return g - 8f;
		}
		return g - Mathf.Max(0f, 8f + Mathf.Clamp(level[i] + smooth[i], -8f, 8f));
	}

	private static float OvFloor(Vector3 p)
	{
		return Mathf.Max(TFloorAt(p) + OvFloorMargin, OvWaterFloor);
	}

	// ---------- C1: starting a vein ----------
	private void OvStart()
	{
		MineRock5 rock = _target != null ? _target.GetComponent<MineRock5>() : null;
		if (rock == null)
		{
			ResetToIdle();
			return;
		}
		ItemDrop.ItemData pick = FindBestTool(3);
		if (pick == null)
		{
			SayGaveUp(GiveUpWhy.NoTool, "no working pickaxe in its bag");
			_noToolRock = _target;
			_noToolNext = Time.time + 1f;
			ResetToIdle();
			return;
		}
		if (pick.m_shared.m_toolTier < rock.m_minToolTier)
		{
			SayGaveUp(GiveUpWhy.TooHard, $"best pickaxe tier {pick.m_shared.m_toolTier}, the vein needs {rock.m_minToolTier}");
			_blacklist[_target.GetInstanceID()] = Time.time;
			ResetToIdle();
			return;
		}
		OvPlan p = OvPlanFor(rock);
		if (p == null)
		{
			SayGaveUp(GiveUpWhy.Buried, "no part of it sticks out of the ground");
			_blacklist[_target.GetInstanceID()] = Time.time;
			ResetToIdle();
			return;
		}
		_vFor = _target;
		_vRock = rock;
		_vp = p;
		_vFirstSide = SideOf(base.transform.position);
		_ovSpot = null;
		_ovIdx = -1;
		_ovDir = 0;
		_ovTurned = false;
		_ovDoneSaid = false;
		_ovHand = null;
		_ovHandMode = 0;
		_rpTried.Clear();
		OvDigOff();
		_rpActive = false;
		_ai?.StopMoving();
		MHR_Mover.Release(_ai);
		_ai?.SetFollowTarget(null);
		EquipTool(pick);
		_state = HarvestState.Vein;
		p.Workers[this] = Time.time;
		p.LastWork = Time.time;
		if (CompanionsPlugin.LogNormal) LogInfo($"On the vein \"{p.Name}\" ({p.Rocks.Count} deposit(s), {p.Spots.Count} trench spots, lap {p.Lap}{(p.Done ? ", the trench is done" : "")}) - the trench round it first");
	}

	// C3.2: the thing handed to the Wood / Stone flow is over (gone, or that flow gave up on it): back to the vein
	private void OvResume()
	{
		if (_vp == null || _vRock == null || _vFor == null)
		{
			OvLeave("the vein is gone", quiet: true);
			return;
		}
		if (_ovHandMode != 0)
		{
			GameObject thing = _ovHand;
			OvSpot q = _ovHandSpot;
			if (thing != null && thing.activeInHierarchy)
			{
				OvSkip(q, "\"" + thing.name + "\" is in the way and it couldn't clear it", normal: true);
			}
			else if (CompanionsPlugin.LogNormal)
			{
				LogInfo($"Cleared what was in the trench at spot {(q != null ? q.Idx + 1 : 0)} - back to the trench");
			}
		}
		_ovHand = null;
		_ovHandSpot = null;
		_ovHandMode = 0;
		_target = _vFor;
		_targetType = "MineRock5";
		ClaimTarget();
		_ai?.SetFollowTarget(null);
		_state = HarvestState.Vein;
	}

	// lets go of the vein (the plan stays for the others, and for this one when it comes back)
	private void OvLeave(string why, bool quiet = false)
	{
		OvRelease();
		OvDigOff();
		_rpActive = false;
		_ovOut = false;
		if (_vp != null)
		{
			_vp.Workers.Remove(this);
		}
		if (!quiet && CompanionsPlugin.LogNormal && _vFor != null)
		{
			LogInfo($"Leaving the vein work ({why})");
		}
		_vFor = null;
		_vRock = null;
		_vp = null;
		_ovHand = null;
		_ovHandSpot = null;
		_ovHandMode = 0;
		_ovIdx = -1;
		_ovDir = 0;
		ResetToIdle();
		if (GetMode() != 3)
		{
			RestoreLoadout();
		}
	}

	// C6: leaving the vein - out of the trench first when it's in it, toward dest
	private void OvLeaveOut(Vector3 dest, string why)
	{
		if (_vFor == null || _ovOut)
		{
			return;
		}
		if (_vp != null && OvInTrench(dest, out Vector3 goal))
		{
			OvRelease();
			OvDigOff();
			_ovHand = null;
			_ovHandSpot = null;
			_ovHandMode = 0;
			_ovOut = true;
			_ovOutWhy = why;
			_ai?.SetFollowTarget(null);
			if (CompanionsPlugin.LogNormal) LogInfo($"Leaving the vein ({why}) - out of the trench first, toward {MHR_Mover.Fmt(goal)}");
			OvRampBegin(goal, null, outOfTrench: true, why);
			return;
		}
		OvLeave(why);
	}

	// ExitGatherMode: the job changed (or the gather job ended) while on the vein
	private void OvJobOff()
	{
		_stuckInHole = false;
		if (_vFor == null || _ovOut)
		{
			return;
		}
		Player owner = MHR_Owner.Of(this);
		Vector3 dest = owner != null ? owner.transform.position : base.transform.position;
		if (_vp != null && OvInTrench(dest, out Vector3 goal))
		{
			OvRelease();
			OvDigOff();
			_ovHand = null;
			_ovHandSpot = null;
			_ovHandMode = 0;
			_ovOut = true;
			_ovOutWhy = "the job changed";
			if (CompanionsPlugin.LogNormal) LogInfo($"The job changed while it's in the trench - getting out first, toward {MHR_Mover.Fmt(goal)}");
			OvRampBegin(goal, null, outOfTrench: true, "the job changed");
			return;
		}
		OvRelease();
		OvDigOff();
		_rpActive = false;
		if (_vp != null)
		{
			_vp.Workers.Remove(this);
		}
		_vFor = null;
		_vRock = null;
		_vp = null;
		_ovHand = null;
		_ovHandMode = 0;
	}

	// C6: in the trench = its feet 1.5 m or more under the ground 3 m outside the ring at the nearest spot. The goal out is
	// the ground 3 m outside the ring at the spot nearest to where it's going.
	private bool OvInTrench(Vector3 dest, out Vector3 goal)
	{
		goal = base.transform.position;
		if (_vp == null || _vp.Spots.Count == 0)
		{
			return false;
		}
		Vector3 me = base.transform.position;
		OvSpot near = null, toward = null;
		float nd = float.MaxValue, td = float.MaxValue;
		foreach (OvSpot s in _vp.Spots)
		{
			float a = Flat(me, s.P), b = Flat(dest, s.P);
			if (a < nd)
			{
				nd = a;
				near = s;
			}
			if (b < td)
			{
				td = b;
				toward = s;
			}
		}
		Vector3 nearOut = OvOutside(near);
		if (me.y > GroundAt(nearOut) - OvOutDeep)
		{
			return false;
		}
		goal = OvOutside(toward);
		goal.y = GroundAt(goal);
		return true;
	}

	private Vector3 OvOutside(OvSpot s)
	{
		Vector3 d = s.P - _vp.Center;
		d.y = 0f;
		d = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
		Vector3 o = s.P + d * OvOutSide;
		o.y = GroundAt(o);
		return o;
	}

	// C6: called from Update while it gets out (also after a job change)
	private void OvOutTick()
	{
		if (!_rpActive)
		{
			_ovOut = false;
			OvLeave(_ovOutWhy ?? "out of the trench");
			return;
		}
		_ai?.SetFollowTarget(null);
		OvRampTick();
	}

	// stuck in the trench: it waits for you - move it 5 m (or change the job) and it carries on
	private bool OvStuckTick()
	{
		if (!_stuckInHole)
		{
			return false;
		}
		if (Vector3.Distance(base.transform.position, _stuckAt) > 5f)
		{
			_stuckInHole = false;
			if (CompanionsPlugin.LogNormal) LogInfo("Out of the hole - back to work");
			return false;
		}
		_ai?.StopMoving();
		return true;
	}

	private void OvStuck(string why)
	{
		SayGaveUp(GiveUpWhy.StuckInHole, why);
		Vector3 me = base.transform.position;
		OvLeave("stuck in the trench: " + why);
		_stuckInHole = true;
		_stuckAt = me;
		LogWarn($"Stuck in the trench at {MHR_Mover.Fmt(me)} ({why}) - waiting for you (set a job, or move it 5 m)");
	}

	// ---------- C2: the plan ----------
	private static bool OvRockBounds(MineRock5 r, out Bounds b)
	{
		b = default(Bounds);
		bool any = false;
		if (r == null)
		{
			return false;
		}
		foreach (Collider c in r.GetComponentsInChildren<Collider>())
		{
			if (c == null || !c.enabled || c.isTrigger || !c.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (!any)
			{
				b = c.bounds;
				any = true;
			}
			else
			{
				b.Encapsulate(c.bounds);
			}
		}
		return any;
	}

	private static float OvFlatGap(Bounds a, Bounds b)
	{
		float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
		float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private OvPlan OvPlanFor(MineRock5 rock)
	{
		float now = Time.time;
		s_ovPlans.RemoveAll((OvPlan q) => q.Workers.Count == 0 && now - q.LastWork > OvPlanKeep);
		foreach (OvPlan q in s_ovPlans)
		{
			if (q.Rocks.Contains(rock))
			{
				q.Rocks.RemoveAll((MineRock5 r) => r == null);
				if (q.Rocks.Count > 0)
				{
					return q;
				}
			}
		}
		// C2.1: the vein, and deposits within 4 m of it (and of those)
		OvPlan p = new OvPlan();
		p.Rocks.Add(rock);
		p.Name = MHR_Items.NiceName(rock.gameObject);
		for (int pass = 0; pass < 4; pass++)
		{
			bool added = false;
			for (int i = 0; i < p.Rocks.Count; i++)
			{
				if (!OvRockBounds(p.Rocks[i], out Bounds b))
				{
					continue;
				}
				foreach (Collider c in Physics.OverlapSphere(b.center, b.extents.magnitude + OvJoin + 2f))
				{
					MineRock5 m = c != null ? c.GetComponentInParent<MineRock5>() : null;
					if (m == null || p.Rocks.Contains(m) || GetHarvestCandidateWithType(c, 3, out _) == null || InDungeonAt(m.transform.position))
					{
						continue;
					}
					if (OvRockBounds(m, out Bounds mb) && OvFlatGap(b, mb) <= OvJoin)
					{
						p.Rocks.Add(m);
						added = true;
					}
				}
			}
			if (!added)
			{
				break;
			}
		}
		if (!OvRing(p, null))
		{
			return null;
		}
		p.LastWork = now;
		s_ovPlans.Add(p);
		if (CompanionsPlugin.LogNormal) LogInfo($"Plan for the vein \"{p.Name}\": {p.Rocks.Count} deposit(s), {p.Seen} part(s) seen, a ring of {p.Spots.Count} spots 1 m out from them; the trench goal is {p.Goal:F1} (1 m under the vein's lowest part, never under the floor); lap 1 takes each spot down 2 m at most");
		return p;
	}

	// C2.1-C2.4, C2.6, C2.7: the ring round the seen parts. keep = the old ring (C2.3: work kept within 0.5 m). False = no
	// part is seen.
	private bool OvRing(OvPlan p, List<OvSpot> keep)
	{
		List<Vector2> pts = new List<Vector2>();
		float lowest = float.MaxValue;
		int seen = 0;
		Bounds box = default(Bounds);
		bool anyBox = false;
		foreach (MineRock5 rock in p.Rocks)
		{
			if (rock == null || !MHR_Vein.Areas(rock, s_vAreas, out bool haveBounds))
			{
				continue;
			}
			Vector3 origin = rock.transform.position;
			foreach (MHR_Vein.Area ha in s_vAreas)
			{
				if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
				{
					continue;
				}
				Vector3 c = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
				Vector3 h = haveBounds ? ha.BoundSize : ha.Collider.bounds.size * 0.5f;
				lowest = Mathf.Min(lowest, c.y - h.y);
				Bounds pb = new Bounds(c, h * 2f);
				if (!anyBox)
				{
					box = pb;
					anyBox = true;
				}
				else
				{
					box.Encapsulate(pb);
				}
				if (!IsSegmentExposed(ha.Collider))
				{
					continue;
				}
				seen++;
				pts.Add(new Vector2(c.x - h.x, c.z - h.z));
				pts.Add(new Vector2(c.x + h.x, c.z - h.z));
				pts.Add(new Vector2(c.x + h.x, c.z + h.z));
				pts.Add(new Vector2(c.x - h.x, c.z + h.z));
			}
		}
		if (seen == 0)
		{
			return false;
		}
		p.Seen = seen;
		p.Box = box;
		p.Goal = lowest - OvGoalUnder;
		List<Vector2> hull = OvHull(pts);
		Vector2 mid = Vector2.zero;
		foreach (Vector2 v in hull)
		{
			mid += v;
		}
		mid /= Mathf.Max(1, hull.Count);
		p.Center = new Vector3(mid.x, GroundAt(new Vector3(mid.x, 0f, mid.y)), mid.y);
		// the outline moved 1 m outward (round at the corners), then a spot every 1 m along it
		p.Poly.Clear();
		int n = hull.Count;
		if (n < 3)
		{
			for (int k = 0; k < 24; k++)
			{
				float a = k * Mathf.PI * 2f / 24f;
				p.Poly.Add(mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (OvRingOut + 0.5f));
			}
		}
		else
		{
			for (int i = 0; i < n; i++)
			{
				Vector2 prev = hull[(i + n - 1) % n], cur = hull[i], next = hull[(i + 1) % n];
				Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
				Vector2 n0 = new Vector2(e0.y, -e0.x), n1 = new Vector2(e1.y, -e1.x);
				float a0 = Mathf.Atan2(n0.y, n0.x), a1 = Mathf.Atan2(n1.y, n1.x);
				if (a1 < a0)
				{
					a1 += Mathf.PI * 2f;
				}
				int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) / (15f * Mathf.Deg2Rad)));
				for (int k = 0; k <= steps; k++)
				{
					float a = Mathf.Lerp(a0, a1, (float)k / steps);
					p.Poly.Add(cur + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * OvRingOut);
				}
			}
		}
		float per = 0f;
		for (int i = 0; i < p.Poly.Count; i++)
		{
			per += (p.Poly[(i + 1) % p.Poly.Count] - p.Poly[i]).magnitude;
		}
		int count = Mathf.Max(4, Mathf.RoundToInt(per / OvSpacing));
		float step = per / count;
		p.Spots.Clear();
		int seg = 0;
		float segStart = 0f;
		for (int k = 0; k < count; k++)
		{
			float at = k * step;
			while (seg < p.Poly.Count - 1)
			{
				float len = (p.Poly[(seg + 1) % p.Poly.Count] - p.Poly[seg]).magnitude;
				if (at <= segStart + len)
				{
					break;
				}
				segStart += len;
				seg++;
			}
			Vector2 a2 = p.Poly[seg], b2 = p.Poly[(seg + 1) % p.Poly.Count];
			float sl = (b2 - a2).magnitude;
			Vector2 q2 = sl > 0.001f ? Vector2.Lerp(a2, b2, Mathf.Clamp01((at - segStart) / sl)) : a2;
			Vector3 pos = new Vector3(q2.x, 0f, q2.y);
			pos.y = GroundAt(pos);
			OvSpot s = new OvSpot { P = pos, Idx = k };
			s.Floor = OvFloor(pos);
			s.Target = Mathf.Max(s.Floor, p.Goal);
			OvSpot old = null;
			if (keep != null)
			{
				float od = OvKeep;
				foreach (OvSpot o in keep)
				{
					float d = Flat(o.P, pos);
					if (d <= od)
					{
						od = d;
						old = o;
					}
				}
			}
			if (old != null)
			{
				s.LapStart = old.LapStart;
				s.LapDepth = Mathf.Max(s.Target, old.LapStart - OvLapStep);
				s.Done = old.Done;
				s.DoneWhy = old.DoneWhy;
				s.Skip = old.Skip;
				s.SkipWhy = old.SkipWhy;
				s.RampTried = old.RampTried;
				s.Holder = old.Holder;
			}
			else
			{
				s.LapStart = pos.y;
				s.LapDepth = Mathf.Max(s.Target, pos.y - OvLapStep);
			}
			OvClosedCheck(s);
			if (!s.Closed && !s.Done && pos.y <= s.LapDepth + OvDigGain)
			{
				s.Done = true;
				s.DoneWhy = "already at this lap's depth";
			}
			p.Spots.Add(s);
		}
		return true;
	}

	// C2.4: closed for good - a building within 1 m, someone else's ward or a no-build area, water it would swim in
	private void OvClosedCheck(OvSpot s)
	{
		if (_character != null && MHR_Water.WouldSwim(_character, s.P))
		{
			s.Closed = true;
			s.ClosedWhy = "water it would swim in";
			return;
		}
		if (!PrivateArea.CheckAccess(s.P, 0f, flash: false) || Location.IsInsideNoBuildLocation(s.P))
		{
			s.Closed = true;
			s.ClosedWhy = "a ward or a no-build area";
			return;
		}
		foreach (Collider c in Physics.OverlapSphere(s.P + Vector3.up * 0.5f, 1.0f, LayerMask.GetMask("piece", "piece_nonsolid"), QueryTriggerInteraction.Ignore))
		{
			if (c != null && c.GetComponentInParent<Piece>() != null)
			{
				s.Closed = true;
				s.ClosedWhy = "a building";
				return;
			}
		}
	}

	// the convex outline (counter-clockwise) of the points (Andrew's monotone chain)
	private static List<Vector2> OvHull(List<Vector2> pts)
	{
		pts.Sort((Vector2 a, Vector2 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
		List<Vector2> h = new List<Vector2>();
		for (int pass = 0; pass < 2; pass++)
		{
			int start = h.Count;
			for (int k = 0; k < pts.Count; k++)
			{
				Vector2 p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
				while (h.Count >= start + 2 && OvCross(h[h.Count - 2], h[h.Count - 1], p) <= 0f)
				{
					h.RemoveAt(h.Count - 1);
				}
				h.Add(p);
			}
			h.RemoveAt(h.Count - 1);
		}
		return h;
	}

	private static float OvCross(Vector2 o, Vector2 a, Vector2 b)
	{
		return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
	}

	private static bool OvInside(List<Vector2> poly, Vector2 p)
	{
		bool inside = false;
		for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
		{
			if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
			{
				inside = !inside;
			}
		}
		return inside;
	}

	// C2.3: a part seen outside the ring makes the ring again (at most every 2 s, until lap 1 is complete)
	private void OvGrow(OvPlan p)
	{
		if (p.RingFixed || Time.time < p.GrowAt)
		{
			return;
		}
		p.GrowAt = Time.time + OvGrowEvery;
		bool outside = false;
		foreach (MineRock5 rock in p.Rocks)
		{
			if (rock == null || !MHR_Vein.Areas(rock, s_vAreas, out bool haveBounds))
			{
				continue;
			}
			Vector3 origin = rock.transform.position;
			foreach (MHR_Vein.Area ha in s_vAreas)
			{
				if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf || !IsSegmentExposed(ha.Collider))
				{
					continue;
				}
				Vector3 c = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
				Vector3 h = haveBounds ? ha.BoundSize : ha.Collider.bounds.size * 0.5f;
				if (!OvInside(p.Poly, new Vector2(c.x - h.x, c.z - h.z)) || !OvInside(p.Poly, new Vector2(c.x + h.x, c.z - h.z)) || !OvInside(p.Poly, new Vector2(c.x + h.x, c.z + h.z)) || !OvInside(p.Poly, new Vector2(c.x - h.x, c.z + h.z)))
				{
					outside = true;
					break;
				}
			}
			if (outside)
			{
				break;
			}
		}
		if (!outside)
		{
			return;
		}
		List<OvSpot> old = new List<OvSpot>(p.Spots);
		int before = old.Count;
		if (!OvRing(p, old))
		{
			p.Spots.Clear();
			p.Spots.AddRange(old);
			return;
		}
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h != null)
			{
				h._ovSpot = null;
				h._ovIdx = -1;
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"More of the vein \"{p.Name}\" is uncovered - the ring grows from {before} to {p.Spots.Count} spots ({p.Seen} parts seen; work done kept)");
	}

	// C2.7: no open spot is left in this lap
	private void OvLapEnd(OvPlan p)
	{
		foreach (OvSpot s in p.Spots)
		{
			if (OvOpen(s))
			{
				return;
			}
		}
		float best = 0f;
		int done = 0, skipped = 0, closed = 0;
		foreach (OvSpot s in p.Spots)
		{
			if (s.Closed)
			{
				closed++;
				continue;
			}
			best = Mathf.Max(best, s.LapStart - GroundAt(s.P));
			if (s.Skip)
			{
				skipped++;
			}
			else
			{
				done++;
			}
		}
		if (p.Lap == 1)
		{
			p.RingFixed = true;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Lap {p.Lap} of the trench round \"{p.Name}\" is over: {done} spots done, {skipped} skipped, {closed} closed for good; the most any spot went down this lap: {best:F1} m");
		if (p.Lap >= OvMaxLaps || best < OvLapMinGain)
		{
			p.Done = true;
			return;
		}
		p.Lap++;
		int open = 0;
		foreach (OvSpot s in p.Spots)
		{
			if (s.Closed)
			{
				continue;
			}
			float g = GroundAt(s.P);
			s.Skip = false;
			s.SkipWhy = null;
			s.RampTried = false;
			s.Holder = null;
			if (g > s.Target + OvDigGain)
			{
				s.Done = false;
				s.DoneWhy = null;
				s.LapStart = g;
				s.LapDepth = Mathf.Max(s.Target, g - OvLapStep);
				open++;
			}
			else
			{
				s.Done = true;
				s.DoneWhy = "at the trench goal";
			}
		}
		if (open == 0)
		{
			p.Done = true;
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Lap {p.Lap} of the trench round \"{p.Name}\": {open} spots still above the goal, each down 2 m at most this lap");
	}

	private static bool OvOpen(OvSpot s)
	{
		return !s.Closed && !s.Done && !s.Skip;
	}

	private bool OvHeldByOther(OvSpot s)
	{
		HarvestController h = s.Holder;
		if (h == null || h == this)
		{
			return false;
		}
		if (h._vp != _vp || h._ovSpot != s || !_vp.Workers.TryGetValue(h, out float at) || Time.time - at > 5f)
		{
			s.Holder = null;
			return false;
		}
		return true;
	}

	private void OvRelease()
	{
		if (_ovSpot != null && _ovSpot.Holder == this)
		{
			_ovSpot.Holder = null;
		}
		_ovSpot = null;
	}

	private void OvSkip(OvSpot s, string why, bool normal = false)
	{
		if (s == null)
		{
			return;
		}
		s.Skip = true;
		s.SkipWhy = why;
		if (s.Holder == this)
		{
			s.Holder = null;
		}
		if (_ovSpot == s)
		{
			_ovSpot = null;
		}
		if (normal)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {s.Idx + 1}/{_vp?.Spots.Count ?? 0} at {MHR_Mover.Fmt(s.P)}: skipped this lap ({why})");
		}
		else if (CompanionsPlugin.LogDetailed)
		{
			Log($"Trench spot {s.Idx + 1}/{_vp?.Spots.Count ?? 0} at {MHR_Mover.Fmt(s.P)}: skipped this lap ({why})");
		}
	}

	private void OvSpotDone(OvSpot s, string why)
	{
		s.Done = true;
		s.DoneWhy = why;
		if (s.Holder == this)
		{
			s.Holder = null;
		}
		if (_ovSpot == s)
		{
			_ovSpot = null;
		}
		if (CompanionsPlugin.LogDetailed) Log($"Trench spot {s.Idx + 1}/{_vp?.Spots.Count ?? 0} at {MHR_Mover.Fmt(s.P)}: done for lap {_vp?.Lap ?? 0} ({why}), ground {GroundAt(s.P):F2}");
	}

	// ---------- C3: the chooser (HarvestState.Vein) ----------
	private void OvTick()
	{
		if (_vp == null || _vRock == null || _vFor == null || !_vFor.activeInHierarchy)
		{
			OvLeave("the vein is gone");
			return;
		}
		OvPlan p = _vp;
		float now = Time.time;
		p.Workers[this] = now;
		p.LastWork = now;
		if (now - _vBoxAt > 1f)
		{
			_vBoxAt = now;
			_vBox = p.Box;
		}
		_target = _vFor;
		if (_ai != null && _ai.GetFollowTarget() != null)
		{
			_ai.SetFollowTarget(null);
		}
		if (_rpActive)
		{
			OvRampTick();
			return;
		}
		if (_dgActive)
		{
			OvDigTick();
			return;
		}
		OvGrow(p);
		if (p.Done)
		{
			OvDoneTick();
			return;
		}
		if (_ovSpot == null || !OvOpen(_ovSpot))
		{
			OvRelease();
			if (!OvPick())
			{
				OvLapEnd(p);
				_ai?.StopMoving();
				return;
			}
		}
		OvSpot q = _ovSpot;
		// 1. anything on the spot: the Wood / Stone flow
		if (OvThing(q))
		{
			return;
		}
		Vector3 me = base.transform.position;
		float g = GroundAt(q.P);
		// 2. far up: the way up first
		float height = _character != null ? _character.GetHeight() : 1.8f;
		if (!q.RampTried && g >= me.y + OvRampRatio * height)
		{
			OvRampBegin(q.P, q, outOfTrench: false, $"spot {q.Idx + 1} is {g - me.y:F1} m up (1.5 x its height is {OvRampRatio * height:F1} m)");
			return;
		}
		// 3. the dirt at the spot
		if (g <= q.LapDepth + OvDigGain)
		{
			OvSpotDone(q, "down to this lap's depth");
			return;
		}
		OvDigBegin(new Vector3(q.P.x, g, q.P.z), q, ramp: false);
	}

	// C3.5: the next spot. True = it has one (_ovSpot)
	private bool OvPick()
	{
		List<OvSpot> spots = _vp.Spots;
		int n = spots.Count;
		if (n == 0)
		{
			return false;
		}
		if (_ovTurnLap != _vp.Lap)
		{
			_ovTurnLap = _vp.Lap;
			_ovTurned = false;
		}
		Vector3 me = base.transform.position;
		int found = -1;
		if (_ovIdx < 0 || _ovIdx >= n || _ovDir == 0)
		{
			// the first: the nearest open spot along the ring from where it stands
			int home = 0;
			float hd = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				float d = Flat(me, spots[i].P);
				if (d < hd)
				{
					hd = d;
					home = i;
				}
			}
			int dirFound = 0;
			for (int k = 0; k <= n / 2 && found < 0; k++)
			{
				int a = (home + k) % n, b = (home - k + n) % n;
				if (OvOpen(spots[a]) && !OvHeldByOther(spots[a]))
				{
					found = a;
					dirFound = k == 0 ? 0 : 1;
				}
				else if (OvOpen(spots[b]) && !OvHeldByOther(spots[b]))
				{
					found = b;
					dirFound = -1;
				}
			}
			if (found >= 0)
			{
				if (dirFound == 0)
				{
					// toward the lower ground next to it
					dirFound = GroundAt(spots[(found + 1) % n].P) <= GroundAt(spots[(found - 1 + n) % n].P) ? 1 : -1;
				}
				_ovDir = dirFound;
			}
		}
		else
		{
			found = OvNextOpen(_ovIdx, _ovDir, out bool heldNext);
			if (found < 0 && heldNext && !_ovTurned)
			{
				// the next spot that way is held by another companion: turn round once
				_ovTurned = true;
				_ovDir = -_ovDir;
				found = OvNextOpen(_ovIdx, _ovDir, out _);
				if (found >= 0 && CompanionsPlugin.LogDetailed) Log($"Another companion is on the next spot - turned round, on to spot {found + 1}");
			}
			if (found < 0)
			{
				// the nearest free spot anywhere (fewest spots away along the ring)
				for (int k = 1; k <= n / 2 + 1 && found < 0; k++)
				{
					int a = (_ovIdx + k) % n, b = (_ovIdx - k + n) % n;
					if (OvOpen(spots[a]) && !OvHeldByOther(spots[a]))
					{
						found = a;
					}
					else if (OvOpen(spots[b]) && !OvHeldByOther(spots[b]))
					{
						found = b;
					}
				}
			}
		}
		bool help = false;
		if (found < 0)
		{
			// nothing free: help on the nearest open spot another companion holds (it never waits)
			float bd = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				if (OvOpen(spots[i]))
				{
					float d = Flat(me, spots[i].P);
					if (d < bd)
					{
						bd = d;
						found = i;
					}
				}
			}
			help = found >= 0;
		}
		if (found < 0)
		{
			return false;
		}
		OvSpot s = spots[found];
		if (!help)
		{
			s.Holder = this;
		}
		_ovSpot = s;
		_ovIdx = found;
		if (_ovDir == 0)
		{
			_ovDir = 1;
		}
		if (CompanionsPlugin.LogDetailed) Log($"Trench spot {found + 1}/{n} (lap {_vp.Lap}) at {MHR_Mover.Fmt(s.P)}, {Flat(me, s.P):F1} m away{(help ? " - helping, another companion is on it" : "")}; ground {GroundAt(s.P):F2}, this lap down to {s.LapDepth:F2}");
		return true;
	}

	// the next open spot from idx going dir; heldNext = the first open one that way is held by another
	private int OvNextOpen(int idx, int dir, out bool heldNext)
	{
		heldNext = false;
		List<OvSpot> spots = _vp.Spots;
		int n = spots.Count;
		bool first = true;
		for (int k = 1; k < n; k++)
		{
			int i = ((idx + dir * k) % n + n) % n;
			if (!OvOpen(spots[i]))
			{
				continue;
			}
			if (OvHeldByOther(spots[i]))
			{
				if (first)
				{
					heldNext = true;
					return -1;
				}
				continue;
			}
			return i;
		}
		return -1;
	}

	// C3.2: anything within 2 m of the spot that the Wood job (tree, log, stump, bush) or the Stone job (rock) would take is
	// handed to that job's own flow. True = handed over (or the spot skipped for it).
	private bool OvThing(OvSpot q)
	{
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		int k = Physics.OverlapSphereNonAlloc(q.P + Vector3.up * 1.0f, OvThingRadius, s_ovThingBuf, s_ovThingMask, QueryTriggerInteraction.Ignore);
		GameObject best = null, blocked = null;
		string bestType = null;
		int bestMode = 0;
		float bd = float.MaxValue;
		for (int i = 0; i < k; i++)
		{
			Collider c = s_ovThingBuf[i];
			if (c == null || c.GetComponentInParent<Character>() != null || c.GetComponentInParent<MineRock5>() != null)
			{
				continue;
			}
			// list v2 V1 (kept): ore the Ore job itself would mine is never "in the way"
			if (GetHarvestCandidateWithType(c, 3, out _) != null)
			{
				continue;
			}
			int mode = 1;
			GameObject go = GetHarvestCandidateWithType(c, 1, out string type);
			if (go == null)
			{
				// a bush: something the axe hurts that isn't a stump (the Wood flow, with the axe)
				Destructible d = c.GetComponentInParent<Destructible>();
				if (d != null && d.m_damages.m_chop != HitData.DamageModifier.Immune && d.m_damages.m_chop != HitData.DamageModifier.Ignore && d.gameObject.name.IndexOf("stub", System.StringComparison.OrdinalIgnoreCase) < 0)
				{
					go = d.gameObject;
					type = "Destructible";
				}
			}
			if (go == null)
			{
				mode = 2;
				go = GetHarvestCandidateWithType(c, 2, out type);
			}
			if (go == null || !go.activeInHierarchy || IsBuilt(go))
			{
				continue;
			}
			int id = go.GetInstanceID();
			if (s_claimedTargets.Contains(id) && id != _claimedTargetId)
			{
				continue;   // another companion is on it
			}
			if (_blacklist.ContainsKey(id))
			{
				blocked = go;
				continue;
			}
			float d2 = Flat(go.transform.position, q.P);
			if (d2 < bd)
			{
				bd = d2;
				best = go;
				bestType = type;
				bestMode = mode;
			}
		}
		if (best == null)
		{
			if (blocked != null)
			{
				OvSkip(q, "\"" + blocked.name + "\" is in the way and it couldn't clear it", normal: true);
				return true;
			}
			return false;
		}
		ItemDrop.ItemData tool = FindBestTool(bestMode);
		if (tool == null)
		{
			OvSkip(q, "\"" + best.name + "\" is in the way and it has no " + (bestMode == 1 ? "axe" : "pickaxe") + " for it", normal: true);
			return true;
		}
		_ovHand = best;
		_ovHandSpot = q;
		_ovHandMode = bestMode;
		_ai?.StopMoving();
		_target = best;
		_targetType = bestMode == 1 && bestType == "Destructible" ? "Destructible" : ClassifyTargetType(best, bestMode);
		_swingCount = 0;
		_totalAttempts = 0;
		_consecutiveFailures = 0;
		_reapproach = false;
		_rock5ActiveSections = -1;
		ClaimTarget();
		EquipTool(tool);
		NoteLogsAround();
		_moveTimer = 0f;
		_moveLogTimer = 0f;
		_moveTargetLocked = false;
		_attackFaceLocked = false;
		_state = HarvestState.Moving;
		if (CompanionsPlugin.LogNormal) LogInfo($"In the trench at spot {q.Idx + 1}: \"{best.name}\" ({_targetType}) at {MHR_Mover.Fmt(best.transform.position)} - clearing it the {(bestMode == 1 ? "Wood" : "Stone")} job's way with {tool.m_shared.m_name}");
		return true;
	}

	// C7 (build 1): the trench is done - it says so once and stands by the vein
	private void OvDoneTick()
	{
		OvRelease();
		_ai?.StopMoving();
		if (_ovDoneSaid)
		{
			return;
		}
		_ovDoneSaid = true;
		int notDug = 0, closed = 0;
		string where = "";
		foreach (OvSpot s in _vp.Spots)
		{
			if (s.Closed)
			{
				closed++;
				continue;
			}
			if (GroundAt(s.P) > s.Target + 0.3f)
			{
				if (notDug < 6)
				{
					where += (notDug > 0 ? ", " : "") + MHR_Mover.Fmt(s.P);
				}
				notDug++;
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_vp.Name}\" is done after {_vp.Lap} lap(s): {_vp.Spots.Count} spots, {closed} closed for good, {notDug} more than 0.3 m above the goal{(notDug > 0 ? " (at " + where + (notDug > 6 ? ", ..." : "") + ")" : "")} - clearing comes in build 2; it stands by");
		if (!_vp.DoneSaid)
		{
			_vp.DoneSaid = true;
			string text = notDug > 0 ? string.Format(ModLocalization.Loc("hc_speech_vein_trench_done_gaps"), MHR_Items.NiceName(_vFor), notDug) : string.Format(ModLocalization.Loc("hc_speech_vein_trench_done"), MHR_Items.NiceName(_vFor));
			_talk?.SayProblem("vein:trench_done", text);
		}
	}

	// ---------- C4: dirt ----------
	private void OvDigBegin(Vector3 point, OvSpot spot, bool ramp)
	{
		_dgActive = true;
		_dgPoint = point;
		_dgSpot = spot;
		_dgRamp = ramp;
		_dgWalking = false;
		_dgSwung = false;
		_dgNoProg = 0;
		_dgLastProg = false;
		_moveTimer = 0f;
	}

	private void OvDigOff()
	{
		_dgActive = false;
		_dgSwung = false;
		_dgWalking = false;
	}

	private void OvFace(Vector3 point)
	{
		if (_ai == null || _character == null)
		{
			return;
		}
		_ai.LookAtPoint(point);
		Vector3 d = point - base.transform.position;
		d.y = 0f;
		if (d.sqrMagnitude > 0.001f)
		{
			_character.SetLookDir(d.normalized);
			MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(d.normalized));
		}
	}

	private void OvDigTick()
	{
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		bool busy = _humanoid != null && (_humanoid.InAttack() || _humanoid.InDodge());
		if (_dgSwung)
		{
			if (busy || Time.time - _dgSwungAt < 0.35f)
			{
				OvFace(_dgPoint);
				return;
			}
			OvDigJudge();
			return;
		}
		float reach = GetAttackRange();
		if (!_dgRamp)
		{
			// walking to it: within reach, and not more than 1.2 m below its feet (then it walks on down to it)
			float flat = Flat(me, _dgPoint);
			bool below = _dgPoint.y < me.y - 1.2f;
			if (flat > reach * 0.9f || (below && flat > 0.6f))
			{
				if (!_dgWalking)
				{
					_dgWalking = true;
					_moveTimer = 0f;
				}
				float up = GroundAt(_dgPoint) - me.y;
				OvSpot q = _dgSpot;
				if (q != null && !q.RampTried && up >= OvStepUp && MHR_Mover.NoHeadway(_ai) >= OvNoHeadway)
				{
					OvDigOff();
					OvRampBegin(_dgPoint, q, outOfTrench: false, $"no headway for {OvNoHeadway:F0} s toward spot {q.Idx + 1}, {up:F1} m up");
					return;
				}
				if (WalkTimer(dt, _dgPoint) && _moveTimer > 10f)
				{
					OvDigOff();
					if (q != null && up > 0f && !q.RampTried)
					{
						OvRampBegin(_dgPoint, q, outOfTrench: false, $"the walk to spot {q.Idx + 1} gave up, {up:F1} m up");
					}
					else
					{
						OvSkip(q, $"couldn't get to it in {10f:F0} s ({flat:F1} m away, {up:F1} m up)");
					}
					return;
				}
				WalkGo(_dgPoint, below ? 0.3f : reach * 0.8f, flat > 8f, dt);
				return;
			}
		}
		if (_dgWalking)
		{
			_dgWalking = false;
			_ai?.StopMoving();
		}
		float g = GroundAt(_dgPoint);
		if (_dgSpot != null && !_dgRamp)
		{
			if (g <= _dgSpot.LapDepth + OvDigGain)
			{
				OvDigOff();
				OvSpotDone(_dgSpot, "down to this lap's depth");
				return;
			}
		}
		if (g <= OvFloor(_dgPoint) + OvDigGain)
		{
			OvDigOff();
			_dgLastProg = false;
			if (_dgSpot != null && !_dgRamp)
			{
				OvSpotDone(_dgSpot, "at the floor (the game's 8 m limit or the water)");
			}
			return;
		}
		OvFace(_dgPoint);
		int sw = SwingNow(out _, out _);
		if (sw == -1)
		{
			OvDigOff();
			_noToolRock = _vFor;
			_noToolNext = Time.time + 1f;
			OvLeaveOut(base.transform.position, "no working pickaxe");
			return;
		}
		if (sw == 1)
		{
			_dgSwung = true;
			_dgSwungAt = Time.time;
			_dgBefore = g;
		}
	}

	private void OvDigJudge()
	{
		_dgSwung = false;
		float after = GroundAt(_dgPoint);
		bool prog = _dgBefore - after >= OvDigGain;
		_dgLastProg = prog;
		_dgNoProg = prog ? 0 : _dgNoProg + 1;
		if (CompanionsPlugin.LogDetailed) Log($"Dirt swing at {MHR_Mover.Fmt(_dgPoint)}{(_dgRamp ? " (ramp step)" : _dgSpot != null ? $" (spot {_dgSpot.Idx + 1})" : "")}: ground {_dgBefore:F2} -> {after:F2}{(prog ? "" : $" - no change ({_dgNoProg} in a row)")}, feet {base.transform.position.y:F2}");
		if (_dgRamp)
		{
			OvDigOff();   // one swing per ramp step: the ramp looks again
			return;
		}
		if (_dgSpot != null && after <= _dgSpot.LapDepth + OvDigGain)
		{
			OvDigOff();
			OvSpotDone(_dgSpot, "down to this lap's depth");
			return;
		}
		if (_dgNoProg >= OvNoProgSwings)
		{
			OvDigOff();
			if (_dgSpot != null)
			{
				OvSpotDone(_dgSpot, $"can't go lower ({OvNoProgSwings} swings with no change)");
			}
		}
	}

	// ---------- C5: the way up (a ramp) ----------
	private void OvRampBegin(Vector3 goal, OvSpot spot, bool outOfTrench, string why)
	{
		Vector3 me = base.transform.position;
		_rpActive = true;
		_rpGoal = goal;
		_rpSpot = spot;
		_rpOut = outOfTrench;
		_rpStart = me;
		_rpBestY = me.y;
		_rpBestAt = Time.time;
		_rpNoProg = 0;
		_rpSwings = 0;
		_rpStepPt = new Vector3(float.MaxValue, 0f, 0f);
		_rpSaved = null;
		_moveTimer = 0f;
		if (spot != null)
		{
			spot.RampTried = true;
		}
		if (_vp != null)
		{
			foreach (OvRamp r in _vp.Ramps)
			{
				if (!_rpTried.Contains(r) && Flat(r.Bottom, me) <= OvRampNear && Vector3.Distance(r.Top, goal) <= OvRampTopNear)
				{
					_rpSaved = r;
					break;
				}
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The way up ({why}): " + (_rpSaved != null ? $"using the ramp from {MHR_Mover.Fmt(_rpSaved.Bottom)} to {MHR_Mover.Fmt(_rpSaved.Top)}" : $"cutting a ramp toward {MHR_Mover.Fmt(goal)} from {MHR_Mover.Fmt(me)}"));
	}

	private void OvRampTick()
	{
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		float reach = GetAttackRange();
		if (_dgActive)
		{
			OvDigTick();
			if (!_dgActive)
			{
				_rpSwings++;
				_rpNoProg = _dgLastProg ? 0 : _rpNoProg + 1;
				if (_rpNoProg >= OvNoProgSwings)
				{
					OvRampFail($"{OvNoProgSwings} swings at the step at {MHR_Mover.Fmt(_rpStepPt)} changed nothing");
				}
			}
			return;
		}
		if (_rpSaved != null)
		{
			if (Flat(me, _rpSaved.Top) <= 1.0f && Mathf.Abs(me.y - _rpSaved.Top.y) <= OvStepUp)
			{
				OvRampFinish("up the saved ramp");
				return;
			}
			if (WalkTimer(dt, _rpSaved.Top) && _moveTimer > 10f)
			{
				_rpTried.Add(_rpSaved);
				if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get up the ramp at {MHR_Mover.Fmt(_rpSaved.Bottom)} in 10 s - cutting its own");
				_rpSaved = null;
				_rpStart = me;
				_rpBestY = me.y;
				_rpBestAt = Time.time;
				_moveTimer = 0f;
				return;
			}
			WalkGo(_rpSaved.Top, 0.5f, false, dt);
			return;
		}
		float gGoal = GroundAt(_rpGoal);
		if (gGoal <= me.y + OvStepUp || (Flat(me, _rpGoal) <= reach && gGoal <= me.y + OvStepUp))
		{
			OvRampFinish("up");
			return;
		}
		if (me.y > _rpBestY + OvRampRise)
		{
			_rpBestY = me.y;
			_rpBestAt = Time.time;
		}
		if (Time.time - _rpBestAt > OvRampTime)
		{
			OvRampFail($"{OvRampTime:F0} s without getting {OvRampRise:F1} m higher");
			return;
		}
		Vector3 dir = _rpGoal - me;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.01f)
		{
			OvRampFinish("there");
			return;
		}
		dir.Normalize();
		Vector3 ahead = me + dir * OvRampAhead;
		float ga = GroundAt(ahead);
		if (ga > me.y + OvStepUp)
		{
			// the wall in front: dig it (one swing, then look again)
			ahead.y = ga;
			if (Flat(ahead, _rpStepPt) > 0.5f)
			{
				_rpStepPt = ahead;
				_rpNoProg = 0;
			}
			_ai?.StopMoving();
			OvDigBegin(ahead, null, ramp: true);
			return;
		}
		// low enough to walk or hop: on toward the goal, the goal kept on its own level (the mover hops, never climbs)
		Vector3 walk = me + dir * 1.5f;
		walk.y = Mathf.Min(GroundAt(walk), me.y + OvStepUp);
		_moveTimer = 0f;
		WalkGo(walk, 0.3f, false, dt);
	}

	private void OvRampFinish(string how)
	{
		Vector3 me = base.transform.position;
		float rise = me.y - _rpStart.y;
		_rpActive = false;
		OvDigOff();
		_ai?.StopMoving();
		if (_vp != null && _rpSaved == null && rise >= OvRampSave)
		{
			OvRamp same = null;
			foreach (OvRamp r in _vp.Ramps)
			{
				if (Flat(r.Bottom, _rpStart) <= OvRampNear)
				{
					same = r;
					break;
				}
			}
			if (same != null)
			{
				if (_rpStart.y < same.Bottom.y)
				{
					same.Bottom = _rpStart;
				}
				if (me.y > same.Top.y)
				{
					same.Top = me;
				}
				if (CompanionsPlugin.LogNormal) LogInfo($"The ramp at {MHR_Mover.Fmt(same.Bottom)} now goes to {MHR_Mover.Fmt(same.Top)}");
			}
			else
			{
				_vp.Ramps.Add(new OvRamp { Bottom = _rpStart, Top = me });
				if (CompanionsPlugin.LogNormal) LogInfo($"A ramp for everyone on this vein: {MHR_Mover.Fmt(_rpStart)} up to {MHR_Mover.Fmt(me)} ({rise:F1} m; {_vp.Ramps.Count} on this vein)");
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The way up is done ({how}): rose {rise:F1} m in {_rpSwings} swing(s), feet at {me.y:F1}");
		if (_rpOut)
		{
			_ovOut = false;
			OvLeave(_ovOutWhy ?? "out of the trench");
		}
	}

	private void OvRampFail(string why)
	{
		_rpActive = false;
		OvDigOff();
		_ai?.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"The way up gave up: {why} (it rose {base.transform.position.y - _rpStart.y:F1} m)");
		if (_rpOut)
		{
			_ovOut = false;
			OvStuck("couldn't cut its way out: " + why);
			return;
		}
		OvSkip(_rpSpot, "the way up to it failed: " + why, normal: true);
	}

	// ---------- C8: the dig gate (SpawnOnHitTerrain asks) ----------
	internal bool AllowDig(Vector3 hitPoint)
	{
		if (!_dgActive || !_dgSwung || _vFor == null)
		{
			return false;
		}
		return Vector3.Distance(hitPoint, _dgPoint) <= OvDigGate && hitPoint.y >= OvFloor(hitPoint);
	}

	internal void NotifyDig(Vector3 hitPoint)
	{
		_lastHitAt = Time.time;
		_noDamageSwings = 0;
	}

	// ---------- the leash (part 1 G3, kept) ----------
	private float LeashDist(Vector3 me, out Vector3 near)
	{
		near = _vp != null ? _vp.Box.ClosestPoint(me) : me;
		near.y = GroundAt(near);
		return Flat(me, near);
	}

	private bool LeashTick()
	{
		if (_ai == null || _humanoid == null || _vp == null || _vFor == null || _ovOut || _stuckInHole)
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
				if (CompanionsPlugin.LogNormal) LogInfo($"Back by the vein at {MHR_Mover.Fmt(me)} ({d:F0} m from it)");
				return false;
			}
			if (Time.time - _leashSince > LeashBackTime)
			{
				_leashBack = false;
				SayGaveUp(GiveUpWhy.OutOfReach, $"couldn't get back to the vein in {LeashBackTime:F0} s");
				OvLeave("couldn't get back to the vein");
				_stuckInHole = true;
				_stuckAt = me;
				return true;
			}
			MHR_Mover.Go(_ai, Time.deltaTime, near, 2f, true, "Gather");
			return true;
		}
		if (_state != HarvestState.Vein && _ovHandMode == 0)
		{
			return false;
		}
		if (_vFor.GetInstanceID() != _leashId)
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
			SayGaveUp(GiveUpWhy.OutOfReach, $"{dist:F0} m outside the vein while working it, for the second time");
			OvLeave("too far from the vein twice");
			_stuckInHole = true;
			_stuckAt = me;
			return true;
		}
		_leashBack = true;
		_leashSince = Time.time;
		OvDigOff();
		_rpActive = false;
		if (_ovHandMode != 0)
		{
			OvResume();
		}
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"{dist:F0} m outside the vein while working it (at {MHR_Mover.Fmt(me)}) - coming back ({LeashBackTime:F0} s at most)");
		return true;
	}

	// ---------- no working pickaxe (test review A2, kept) ----------
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

	private void NoToolTick(int mode)
	{
		if (_noToolRock == null)
		{
			_noToolRock = null;
			return;
		}
		if (mode != 3 || _vFor != null)
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
		_stuckInHole = false;
		_blacklist.Remove(_noToolRock.GetInstanceID());
		_noToolRock = null;
		_scanTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo("Has a working pickaxe again - back to the vein");
	}

	// ---------- for the stone toss / ore spot (MHR_GatherRules.Yard.cs) ----------
	internal bool VeinBox(out Bounds box, out int firstSide, out int side)
	{
		box = _vBox;
		firstSide = _vFirstSide;
		side = SideOf(base.transform.position);
		return Time.time - _vBoxAt < 30f;
	}

	internal static Vector3 SideDirOf(int s)
	{
		return SideDir(s < 0 ? 0 : s);
	}

	// 0 N (+z), 1 E (+x), 2 S (-z), 3 W (-x) of the whole vein
	private int SideOf(Vector3 p)
	{
		Bounds b = _vp != null ? _vp.Box : _vBox;
		Vector3 d = p - b.center;
		float ex = Mathf.Max(0.1f, b.extents.x);
		float ez = Mathf.Max(0.1f, b.extents.z);
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

	// the fall line (CompanionFallDamagePatch): what the vein work was doing
	internal string FallNote()
	{
		if (_vFor == null)
		{
			return _stuckInHole ? "stuck in a hole, waiting" : "no vein work";
		}
		string what = _ovOut ? "getting out of the trench (" + _ovOutWhy + ")" : _rpActive ? (_rpSaved != null ? "walking up a saved ramp" : "cutting a ramp toward " + MHR_Mover.Fmt(_rpGoal)) : _dgActive ? (_dgWalking ? "walking to dirt at " : "digging dirt at ") + MHR_Mover.Fmt(_dgPoint) : _ovHandMode != 0 ? "clearing \"" + (_ovHand != null ? _ovHand.name : "?") + "\" from the trench" : _vp != null && _vp.Done ? "standing by (the trench is done)" : "choosing the next spot";
		return $"vein \"{_vp?.Name}\" lap {_vp?.Lap ?? 0}, {what}";
	}

	// ---------- shared reads of a vein (your own work's log lines, PlayerCombatLog.cs; the Ore scan) ----------
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

	// part 2 M5 (kept for the scan): every part this vein has left is under the ground (its top less than 0.7 m above the
	// floor at its place) - the Ore job's search leaves it. Looked at again every 30 s.
	private static readonly Dictionary<int, KeyValuePair<float, bool>> s_vUnder = new Dictionary<int, KeyValuePair<float, bool>>();

	private static readonly List<MHR_Vein.Area> s_vUnderAreas = new List<MHR_Vein.Area>();

	internal static bool VeinAllUnder(MineRock5 rock)
	{
		if (rock == null)
		{
			return false;
		}
		int id = rock.GetInstanceID();
		if (s_vUnder.TryGetValue(id, out KeyValuePair<float, bool> kv) && Time.time - kv.Key < 30f)
		{
			return kv.Value;
		}
		if (s_vUnder.Count > 200)
		{
			s_vUnder.Clear();
		}
		bool all = false;
		if (MHR_Vein.Areas(rock, s_vUnderAreas, out bool haveBounds))
		{
			int alive = 0;
			all = true;
			Vector3 origin = rock.transform.position;
			foreach (MHR_Vein.Area ha in s_vUnderAreas)
			{
				if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
				{
					continue;
				}
				alive++;
				Vector3 centre = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
				float halfY = haveBounds ? ha.BoundSize.y : ha.Collider.bounds.size.y * 0.5f;
				if (centre.y + halfY >= OvFloor(centre) + 0.7f)
				{
					all = false;
					break;
				}
			}
			all &= alive > 0;
		}
		s_vUnder[id] = new KeyValuePair<float, bool>(Time.time, all);
		return all;
	}

	internal static void VeinNames(MineRock5 rock, HashSet<string> into)
	{
		into.Clear();
		if (rock == null || !MHR_Vein.Areas(rock, s_vUnderAreas, out _))
		{
			return;
		}
		foreach (MHR_Vein.Area ha in s_vUnderAreas)
		{
			if (ha.Health > 0f && ha.Collider != null && ha.Collider.gameObject.activeSelf)
			{
				into.Add(ha.Collider.name);
			}
		}
	}
}
