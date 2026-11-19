using System.Collections.Generic;
using System.Text;
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

		public int OpenLap = 1;   // G7: the lap it was last opened for (only those count toward "how far a lap went down")

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

		public float Need;        // R3: the ground outside the trench there when it began (the top must reach it)
	}

	private sealed class OvPlan
	{
		public readonly List<MineRock5> Rocks = new List<MineRock5>();

		public readonly List<OvSpot> Spots = new List<OvSpot>();

		public readonly List<Vector2> Poly = new List<Vector2>();

		public readonly List<OvRamp> Ramps = new List<OvRamp>();

		public readonly Dictionary<HarvestController, float> Workers = new Dictionary<HarvestController, float>();

		// G6: the spot each thing was handed over for (a thing it couldn't clear skips only that spot)
		public readonly Dictionary<int, OvSpot> HandFor = new Dictionary<int, OvSpot>();
		public readonly Dictionary<int, int> PitLeft = new Dictionary<int, int>();   // Y6e: times a thing in the pit was left (2 = for this vein)

		public Vector3 Center;

		public float Goal;

		public int Seen;

		public int Lap = 1;

		public bool RingFixed;

		public bool Done;

		public bool DoneSaid;



		// A1: each companion's own stretch of the ring (first spot, how many spots), for the lap / spot count / workers in SKey
		public readonly Dictionary<HarvestController, int> SFrom = new Dictionary<HarvestController, int>();

		public readonly Dictionary<HarvestController, int> SLen = new Dictionary<HarvestController, int>();

		public string SKey;

		// E2: where a way out before "done" was tried (up to 3, 5 m apart), and who is trying one now
		public readonly List<Vector3> ExitAt = new List<Vector3>();

		public HarvestController ExitBy;

		public float LastWork;

		public float GrowAt;

		public string Name;

		public Bounds Box;
	}

	private const float OvRingOut = 1.0f;          // C2.2: the ring 1 m outside the seen parts

	private const float OvSpacing = 1.0f;          // C2.2: a spot every 1 m

	private const float OvPitNear = 8.0f;          // CC4: the pit pick takes things within 8 m (straight line)

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

	private const float OvThingNear = 4.0f;        // G5: things on a spot are looked for once it is within 4 m of it

	private const float OvRampRatio = 1.5f;        // C3.3: 1.5 x its height up: the way up at once

	private const float OvNoHeadway = 3f;          // C4.1

	private const float OvStepUp = 0.7f;           // C4.1 / C5: a step it walks or hops (dig it when higher)

	private const float OvDigGain = 0.05f;         // C4.3

	private const int OvNoProgSwings = 3;          // C4.4 / C5

	private const float OvRampAhead = 0.5f;        // E4 (John's way): right up against the wall, it digs the ground 0.5 m ahead

	private const float OvRampTime = 60f;          // C5: 60 s without 0.3 m higher

	private const float OvRampRise = 0.3f;

	private const float OvRampSave = 1.5f;         // C5: a ramp that rose 1.5 m is saved

	private const float OvRampNear = 10f;          // C5: a saved ramp's bottom within 10 m

	private const float OvRampTopNear = 5f;        // C5: ... and its top within 5 m of where it's going

	private const float OvThingAbove = 2f;        // V4: a thing more than 2 m above its feet is on the wall top

	private const float OvWayMore = 2f;           // R3: a way out's goal is (the height to climb) + 2 m out

	private const float OvWayNear = 15f;          // R4: a way out within 15 m (flat) of where it works is near enough

	private const float OvRampReach = 3f;          // G2: the way up only for a spot within 3 m (flat): the wall in front

	private const float OvRampBadNear = 2f;        // G1: no new way up within 2 m of where one met the dig limit, this lap

	private const float OvOutDeep = 1.5f;          // C6: 1.5 m under the ground outside the ring: in the trench

	private const float OvOutSide = 3f;            // C6: 3 m outside the ring

	private const float OvSpotIdle = 30f;          // J1: 30 s of work on a spot without the ground going down: skipped this lap

	private const float OvHeadwayGain = 0.3f;      // J2: getting 0.3 m closer is headway

	private const int OvBushMiss = 3;              // J3: 3 swings at a bush that don't hit it: the spot is skipped

	private const float OvSpread = 2f;             // H3: a spot within 2 m of another companion's spot counts as taken

	private const float OvSideNear = 1.2f;         // H4: another companion within 1.2 m ahead ...

	private const float OvSideAfter = 1f;          // H4: ... and no headway for 1 s: the walking code's side step

	private const float OvApart = 4f;              // A2: its stretch done, an open spot 4 m from every other companion

	private const int OvRampSwings = 2;            // E4: 2 swings at the wall in front, then it steps in and looks again

	private const float OvRampHeadway = 1f;        // E4: walking up to the wall, 1 s without 0.3 m closer = it is against it

	private const int OvWayOutTries = 3;           // E2: up to 3 tries at a way out ...

	private const float OvWayOutApart = 5f;        // E2: ... each 5 m from the places tried before

	private static readonly List<OvPlan> s_ovPlans = new List<OvPlan>();

	// ---------- this companion's state ----------
	private GameObject _vFor;

	private MineRock5 _vRock;

	private OvPlan _vp;

	private int _vFirstSide = -1;

	private OvSpot _ovSpot;

	private int _ovIdx = -1;

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

	private bool _dgThingChecked;

	// J1: the spot's own clock (work time on it in this lap without the ground going down)
	private OvSpot _spFor;

	private int _spLap;

	private float _spIdle;

	// J2 / H4: the job's own headway check while walking to dirt (best flat distance, and the walking time since)
	private float _dgHwBest;

	private float _dgHwTime;

	private float _dgSideAt = -100f;

	// H2: the things around the dirt were looked at for the next swing
	private bool _dgThingSwing;

	// J3: a bush cut where it stands (the dirt walk and swing, with the axe)
	private bool _dgBushOn;

	private GameObject _dgBush;

	private int _dgBushMiss;

	// H1: what its swings hit (HarvestHitRegister -> OnHitRegistered)
	private GameObject _ovThingHitGo;

	private float _ovThingHitAt = -100f;

	private float _ovOreHitAt = -100f;

	// E2 / E3: walking along the trench floor to where it tries a way out (a saved ramp's bottom, or a new place)
	private bool _woActive;

	private Vector3 _woFloor;

	private Vector3 _woGoal;

	private bool _woOut;      // getting out (leaving) rather than the way out before "done"


	private bool _exTry;      // E2: the ramp running now is a try at a way out of the trench (Y1 / Y5)

	private string _woWhy = "a way out first";   // Y1 / Y5: why this way out is cut (the log)

	private readonly List<Vector3> _outTried = new List<Vector3>();   // E2: places tried for getting out, this time

	private bool _ovStandSaid;

	private int _ovStandLap;

	// E4: the ramp step being dug (2 swings, then it looks again) and the walk up to the wall
	private int _dgRampN;

	private bool _dgRampProg;

	private float _rpHwBest;

	private float _rpHwTime;

	// A3: a bush it is cutting is claimed like the Wood / Stone jobs' targets
	private int _dgBushClaim;

	// 1.5.98 L4: a clearing dig point (toward a part out of reach): down to _dgGoalY; _dgReached = it got there (or to the
	// floor); _dgOre = a swing landed on the vein instead of the dirt
	private bool _dgClear;

	private float _dgGoalY;

	private bool _dgReached;

	// 1.5.98 R2: a clearing point at the game's 8 m limit, more than a step above its feet (not "done"); and the part
	// itself being mined from where it stands (R2a)
	private bool _dgLimit;

	private bool _dgPart;

	// V2: when and where the game last dug for one of its swings; the vein whose pickaxe sweep was logged
	private float _dgDugAt = -100f;

	private Vector3 _dgDugPt;

	private OvPlan _aimLogVp;

	// V5: when the thing handed to the Wood / Stone flow was handed over
	private float _hdSince;

	private int _pitSkipId;       // Y6e: the thing left last - not taken again until one other piece of work starts

	private float _firstAt;       // Y1 / Y6a: the "way out / things first" look runs at most once a second when it finds nothing

	private bool _hdRamped;       // Y6d: the way up toward this thing was cut (once per try)

	private bool _hdRampNext;     // Y6d: the next OvTake is the same try, after its way up

	private float _hdBest = float.MaxValue;

	private float _hdHwTime;

	private GameObject _rpThing;  // Y6d: the thing the way up is cut toward (handed back to the Wood / Stone flow after)

	private int _rpThingMode;

	private string _rpThingType;

	private OvSpot _rpThingSpot;

	private float _rpThingBase;

	private int _rpThingClaim;

	// Y6d: the way up toward a thing is over or dropped: its claim goes
	private void OvRampThingDrop()
	{
		_rpThing = null;
		if (_rpThingClaim != 0 && _rpThingClaim != _claimedTargetId)
		{
			s_claimedTargets.Remove(_rpThingClaim);
		}
		_rpThingClaim = 0;
	}

	private bool _dgWalkThing;    // Y6b: the things-near-the-dirt check before the walk to this dig point was made

	private static readonly Collider[] s_ovPitBuf = new Collider[512];

	// R3: a way out (out of the trench): the height it must reach (the ground outside when it began)
	private bool _rpWay;

	private float _rpNeed;

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

	// G1: where a way up met the game's dig limit this lap (no new way up from within 2 m of it)
	private readonly List<Vector3> _rpBad = new List<Vector3>();

	private int _rpBadLap = -1;

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
		if (_vFor != null && _state == HarvestState.Vein && _dgActive && _dgBushOn)
		{
			return 1;   // J3: a bush is cut with the axe
		}
		return (_vFor != null && _ovHandMode != 0 && _state != HarvestState.Vein) ? _ovHandMode : GetMode();
	}

	// vein work is on (a dirt swing may dig)
	internal bool VeinWorking => _vFor != null && _state == HarvestState.Vein;

	// G4: on a vein (its own work or a thing it clears for the trench): the mover's cliff climb is off
	internal bool OnVein => _vFor != null;

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
		_ovDoneSaid = false;
		_ovHand = null;
		_ovHandMode = 0;
		_rpTried.Clear();
		_woActive = false;
		_exTry = false;
		OvRampThingDrop();
		_pitSkipId = 0;
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
			OvClearGone();
			OvLeave("the vein is gone", quiet: true);
			return;
		}
		if (_ovHandMode != 0)
		{
			GameObject thing = _ovHand;
			OvSpot q = _ovHandSpot;
			if (thing != null && thing.activeInHierarchy)
			{
				// it came back with the thing still there: given up on it (so no check hands the same thing over again)
				_blacklist[thing.GetInstanceID()] = Time.time;
				OvPitLeftNote(thing);
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
		_ovHandAtOn = false;
		if (_rpActive)
		{
			// a thing cleared from a ramp step (H1 / H2): the ramp's 60 s clock starts again from here
			_rpBestY = base.transform.position.y;
			_rpBestAt = Time.time;
		}
		_target = _vFor;
		_targetType = "MineRock5";
		ClaimTarget();
		_ai?.SetFollowTarget(null);
		_state = HarvestState.Vein;
	}

	// lets go of the vein (the plan stays for the others, and for this one when it comes back)
	private void OvLeave(string why, bool quiet = false)
	{
		OvPartRelease();
		OvRelease();
		OvDigOff();
		OvRampThingDrop();
		_rpActive = false;
		_woActive = false;
		_exTry = false;
		if (_vp != null && _vp.ExitBy == this)
		{
			_vp.ExitBy = null;
		}
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
		ResetToIdle();
		if (GetMode() != 3)
		{
			RestoreLoadout();
		}
	}

	// build 2: lets go of the part it holds or mines
	private void OvPartRelease()
	{
		if (_rpPart != null && _rpPart.Holder == this)
		{
			_rpPart.Holder = null;
		}
		if (_clFor != null && _clFor.Holder == this)
		{
			_clFor.Holder = null;
		}
		_rpPart = null;
		OvClOff();
	}

	// C6: leaving the vein - out of the trench first when it's in it, toward dest
	private void OvLeaveOut(Vector3 dest, string why)
	{
		if (_vFor == null || _ovOut)
		{
			return;
		}
		OvPartRelease();
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
			OvGetOut(goal, why);
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
		OvPartRelease();
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
			OvGetOut(goal, "the job changed");
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
	// (G3) that same point: straight up the nearest wall; dest is where it heads after (the normal walk)
	private bool OvInTrench(Vector3 dest, out Vector3 goal)
	{
		goal = base.transform.position;
		if (_vp == null || _vp.Spots.Count == 0)
		{
			return false;
		}
		Vector3 me = base.transform.position;
		OvSpot near = null;
		float nd = float.MaxValue;
		foreach (OvSpot s in _vp.Spots)
		{
			float a = Flat(me, s.P);
			if (a < nd)
			{
				nd = a;
				near = s;
			}
		}
		Vector3 nearOut = OvOutside(near);
		if (me.y > GroundAt(nearOut) - OvOutDeep)
		{
			return false;
		}
		// G3: straight up the nearest wall (3 m outside the nearest spot); the normal walk takes it on from there
		goal = nearOut;
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

	// Y2: the wall outside a spot - the highest ground every 1 m from the spot straight out to 8 m (a single point 3 m
	// out read "shallow" where an earlier ramp had dug a channel, test 9)
	private const int OvWallOut = 8;

	private float OvWallTop(OvSpot s)
	{
		Vector3 d = s.P - _vp.Center;
		d.y = 0f;
		d = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
		float top = float.MinValue;
		for (int i = 1; i <= OvWallOut; i++)
		{
			top = Mathf.Max(top, GroundAt(s.P + d * i));
		}
		return top;
	}

	// C6: called from Update while it gets out (also after a job change)
	private void OvOutTick()
	{
		if (_woActive)
		{
			_ai?.SetFollowTarget(null);
			OvWayOutTick();
			return;
		}
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
		if (_vFor != null)
		{
			_target = _vFor;   // G9: the give-up line names the vein (it said "pickaxe" when no target was set)
		}
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

	// CC2 (test 12, John 2026-11-19: "It should measure to actual ore vein parts, not the damn box"): the live parts' own
	// boxes - the game's box for each part (MineRock5.SetupColliders: the part collider's bounds, the box its support check
	// uses), buried parts included
	private static readonly List<MHR_Vein.Area> s_ovJoinAreas = new List<MHR_Vein.Area>();

	private static void OvPartBoxes(MineRock5 r, List<Bounds> into)
	{
		into.Clear();
		if (r == null || !MHR_Vein.Areas(r, s_ovJoinAreas, out _))
		{
			return;
		}
		foreach (MHR_Vein.Area ha in s_ovJoinAreas)
		{
			if (ha.Health > 0f && ha.Collider != null && ha.Collider.gameObject.activeInHierarchy)
			{
				into.Add(ha.Collider.bounds);
			}
		}
	}

	// CC2: the gap between two boxes in 3D (height counts); 0 = touching or overlapping
	private static float OvGap3D(Bounds a, Bounds b)
	{
		float dx = Mathf.Max(0f, Mathf.Max(a.min.x - b.max.x, b.min.x - a.max.x));
		float dy = Mathf.Max(0f, Mathf.Max(a.min.y - b.max.y, b.min.y - a.max.y));
		float dz = Mathf.Max(0f, Mathf.Max(a.min.z - b.max.z, b.min.z - a.max.z));
		return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
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
		// C2.1 / CC2: the vein, and deposits with a part within 4 m (3D, part box to part box) of a part of it (and of those)
		OvPlan p = new OvPlan();
		p.Rocks.Add(rock);
		p.Name = MHR_Items.NiceName(rock.gameObject);
		List<Bounds> have = new List<Bounds>();
		List<Bounds> theirs = new List<Bounds>();
		OvPartBoxes(rock, have);
		StringBuilder joined = new StringBuilder();
		for (int pass = 0; pass < 4; pass++)
		{
			bool added = false;
			for (int i = 0; i < p.Rocks.Count; i++)
			{
				if (!OvRockBounds(p.Rocks[i], out Bounds b))
				{
					continue;
				}
				// the search net only: deposits whose colliders are near this one's; the join itself is part to part
				foreach (Collider c in Physics.OverlapSphere(b.center, b.extents.magnitude + OvJoin + 2f))
				{
					MineRock5 m = c != null ? c.GetComponentInParent<MineRock5>() : null;
					if (m == null || p.Rocks.Contains(m) || GetHarvestCandidateWithType(c, 3, out _) == null || InDungeonAt(m.transform.position))
					{
						continue;
					}
					OvPartBoxes(m, theirs);
					float gap = float.MaxValue;
					foreach (Bounds tb in theirs)
					{
						foreach (Bounds hb in have)
						{
							gap = Mathf.Min(gap, OvGap3D(tb, hb));
						}
						if (gap <= 0f)
						{
							break;
						}
					}
					if (gap <= OvJoin)
					{
						p.Rocks.Add(m);
						have.AddRange(theirs);
						added = true;
						joined.Append($"; joined \"{MHR_Items.NiceName(m.gameObject)}\" at {MHR_Mover.Fmt(m.transform.position)} (its nearest part {gap:F1} m from the vein's)");
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
		if (CompanionsPlugin.LogNormal) LogInfo($"Plan for the vein \"{p.Name}\": {p.Rocks.Count} deposit(s), {p.Seen} part(s) seen, a ring of {p.Spots.Count} spots 1 m out from them; the trench goal is {p.Goal:F1} (1 m under the vein's lowest part, never under the floor); lap 1 takes each spot down 2 m at most{joined}");
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
				s.OpenLap = old.OpenLap;
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
			if (s.OpenLap == p.Lap)
			{
				// G7: only the spots worked this lap (a spot done in an earlier lap kept its old drop: 6.7 m every lap)
				best = Mathf.Max(best, s.LapStart - GroundAt(s.P));
			}
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
		p.PitLeft.Clear();   // AA5: "left twice" is per lap - a left thing is tried again on the next lap (the laps are capped)
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
				s.OpenLap = p.Lap;
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

	// H3: held by another companion, or within 2 m of the spot another companion is working (each works its own stretch)
	private bool OvTaken(OvSpot s)
	{
		if (OvHeldByOther(s))
		{
			return true;
		}
		foreach (KeyValuePair<HarvestController, float> w in _vp.Workers)
		{
			HarvestController h = w.Key;
			if (h == null || h == this || Time.time - w.Value > 5f || h._vp != _vp || h._ovSpot == null || h._ovSpot.Holder != h)
			{
				continue;
			}
			if (Flat(h._ovSpot.P, s.P) <= OvSpread)
			{
				return true;
			}
		}
		return false;
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
			OvClearGone();
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
		if (_woActive)
		{
			OvWayOutTick();
			return;
		}
		// N1: a clearing dig point is ticked by the clearing itself (OvClRound), which then sees how it ended
		if (_dgActive && !_dgClear)
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
		// CC3 (test 12): no separate first pass round the ring (J4 removed) - a thing on a spot is cleared when the spot is
		// reached (1. below), one in the pit by the pit pick near it (Y6a / CC4)
		if (_ovSpot == null || !OvOpen(_ovSpot))
		{
			OvRelease();
			// Y1 / Y6a: a way out first, then the things in the pit, before the next trench spot
			if (OvFirst())
			{
				return;
			}
			if (!OvPick())
			{
				OvLapEnd(p);
				_ai?.StopMoving();
				return;
			}
		}
		OvSpot q = _ovSpot;
		Vector3 me = base.transform.position;
		// 1. anything on the spot: the Wood / Stone flow (G5: once it is within 4 m; farther, the dirt walk gets it there
		// and looks then)
		if (Flat(me, q.P) <= OvThingNear && OvThing(q))
		{
			return;
		}
		float g = GroundAt(q.P);
		// 2. far up: the way up first
		float height = _character != null ? _character.GetHeight() : 1.8f;
		if (!q.RampTried && g >= me.y + OvRampRatio * height && Flat(me, q.P) <= OvRampReach)
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

	// A1: the companions working this vein now (on it, clearing something for its trench included; not getting out or
	// stuck), in a fixed order
	private List<HarvestController> OvActive()
	{
		List<HarvestController> act = new List<HarvestController>();
		foreach (KeyValuePair<HarvestController, float> w in _vp.Workers)
		{
			if (w.Key != null && (w.Key == this || (w.Key._vp == _vp && w.Key._vFor != null && !w.Key._ovOut && !w.Key._stuckInHole)))
			{
				act.Add(w.Key);
			}
		}
		if (!act.Contains(this))
		{
			act.Add(this);
		}
		act.Sort((HarvestController a, HarvestController b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
		return act;
	}

	private int OvNearestIdx(Vector3 p)
	{
		int best = 0;
		float bd = float.MaxValue;
		for (int i = 0; i < _vp.Spots.Count; i++)
		{
			float d = Flat(p, _vp.Spots[i].P);
			if (d < bd)
			{
				bd = d;
				best = i;
			}
		}
		return best;
	}

	// A1: the ring split into equal stretches, one per companion working the vein, made again at each lap, when the ring
	// grows, or when a companion joins or leaves. Kept in the plan, so all of them use the same split. The stretches are
	// handed out so that the walk to each one's first spot is shortest in all.
	private void OvStretches()
	{
		OvPlan p = _vp;
		int n = p.Spots.Count;
		List<HarvestController> act = OvActive();
		string key = p.Lap + "|" + n;
		foreach (HarvestController h in act)
		{
			key += "|" + h.GetInstanceID();
		}
		if (key == p.SKey && p.SFrom.ContainsKey(this))
		{
			return;
		}
		p.SKey = key;
		p.SFrom.Clear();
		p.SLen.Clear();
		int k = Mathf.Max(1, Mathf.Min(act.Count, n));
		int anchor = OvNearestIdx(act[0].transform.position);
		int[] from = new int[k], len = new int[k];
		int at = anchor;
		for (int j = 0; j < k; j++)
		{
			from[j] = at % n;
			len[j] = n / k + (j < n % k ? 1 : 0);
			at += len[j];
		}
		// the hand-out: every order for up to 5 companions, else one by one (each the nearest free stretch)
		int[] bestOrder = null;
		float bestCost = float.MaxValue;
		if (k <= 5)
		{
			List<int[]> perms = new List<int[]>();
			OvPerms(new int[k], new bool[k], 0, perms);
			foreach (int[] o in perms)
			{
				float c = 0f;
				for (int j = 0; j < k; j++)
				{
					c += Flat(act[j].transform.position, p.Spots[from[o[j]]].P);
				}
				if (c < bestCost)
				{
					bestCost = c;
					bestOrder = o;
				}
			}
		}
		else
		{
			bestOrder = new int[k];
			bool[] used = new bool[k];
			for (int j = 0; j < k; j++)
			{
				int bj = 0;
				float bd = float.MaxValue;
				for (int a = 0; a < k; a++)
				{
					float d = used[a] ? float.MaxValue : Flat(act[j].transform.position, p.Spots[from[a]].P);
					if (d < bd)
					{
						bd = d;
						bj = a;
					}
				}
				used[bj] = true;
				bestOrder[j] = bj;
			}
		}
		string say = "";
		for (int j = 0; j < k; j++)
		{
			p.SFrom[act[j]] = from[bestOrder[j]];
			p.SLen[act[j]] = len[bestOrder[j]];
			say += (j > 0 ? ", " : "") + (act[j] == this ? "this one" : (act[j]._character != null ? act[j]._character.m_name : act[j].name.Replace("(Clone)", ""))) + " " + (from[bestOrder[j]] + 1) + "-" + ((from[bestOrder[j]] + len[bestOrder[j]] - 1) % n + 1);
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench ring round \"{p.Name}\" (lap {p.Lap}, {n} spots) is split into {k} stretch(es), each worked one way round: {say}");
	}

	private static void OvPerms(int[] cur, bool[] used, int depth, List<int[]> into)
	{
		if (depth == cur.Length)
		{
			into.Add((int[])cur.Clone());
			return;
		}
		for (int i = 0; i < cur.Length; i++)
		{
			if (!used[i])
			{
				used[i] = true;
				cur[depth] = i;
				OvPerms(cur, used, depth + 1, into);
				used[i] = false;
			}
		}
	}

	// A2: at least 4 m from every other companion working the vein and from the spot it is working
	private bool OvApartFromOthers(Vector3 p)
	{
		foreach (HarvestController h in OvActive())
		{
			if (h == this)
			{
				continue;
			}
			if (Flat(h.transform.position, p) < OvApart || (h._ovSpot != null && Flat(h._ovSpot.P, p) < OvApart))
			{
				return false;
			}
		}
		return true;
	}

	// C3.5 / A1 / A2: the next spot. True = it has one (_ovSpot); false = nothing for it now (it stands by until the lap ends)
	private bool OvPick()
	{
		List<OvSpot> spots = _vp.Spots;
		int n = spots.Count;
		if (n == 0)
		{
			return false;
		}
		OvStretches();
		Vector3 me = base.transform.position;
		int found = -1;
		// A1: its own stretch, from its first spot forward round the ring
		if (_vp.SFrom.TryGetValue(this, out int from) && _vp.SLen.TryGetValue(this, out int len))
		{
			for (int k = 0; k < len && found < 0; k++)
			{
				int i = (from + k) % n;
				if (OvOpen(spots[i]) && !OvTaken(spots[i]))
				{
					found = i;
				}
			}
		}
		bool other = false;
		if (found < 0)
		{
			// A2: its stretch is done - the nearest open spot 4 m from every other companion and its spot; else it stands by
			float bd = float.MaxValue;
			for (int i = 0; i < n; i++)
			{
				if (OvOpen(spots[i]) && !OvTaken(spots[i]) && OvApartFromOthers(spots[i].P))
				{
					float d = Flat(me, spots[i].P);
					if (d < bd)
					{
						bd = d;
						found = i;
					}
				}
			}
			other = found >= 0;
		}
		if (found < 0)
		{
			if (!_ovStandSaid || _ovStandLap != _vp.Lap)
			{
				_ovStandSaid = true;
				_ovStandLap = _vp.Lap;
				if (CompanionsPlugin.LogNormal) LogInfo($"Its stretch of the trench is done for lap {_vp.Lap} and every open spot is within 4 m of another companion - standing by until the lap ends");
			}
			return false;
		}
		_ovStandSaid = false;
		_pitSkipId = 0;   // Y6e: another piece of work started
		OvSpot s = spots[found];
		s.Holder = this;
		_ovSpot = s;
		_ovIdx = found;
		if (CompanionsPlugin.LogDetailed) Log($"Trench spot {found + 1}/{n} (lap {_vp.Lap}) at {MHR_Mover.Fmt(s.P)}, {Flat(me, s.P):F1} m away{(other ? " - its stretch is done, the nearest open spot 4 m from the others" : "")}; ground {GroundAt(s.P):F2}, this lap down to {s.LapDepth:F2}");
		return true;
	}

	// C3.2: anything within 2 m of the spot that the Wood job (tree, log, stump, bush) or the Stone job (rock) would take is
	// handed to that job's own flow (a bush: J3, cut where it stands). True = handed over (or the spot skipped for it).
	private bool OvThing(OvSpot q)
	{
		return OvThingAt(q.P, q, canSkip: true) != 0;
	}

	// the thing a collider belongs to, if the Wood job (tree, log, stump; a bush = "Destructible" with mode 1) or the Stone
	// job (mode 2) would take it; never a vein part, ore, a character or anything built
	private GameObject OvThingOf(Collider c, out int mode, out string type)
	{
		mode = 0;
		type = null;
		if (c == null || c.GetComponentInParent<Character>() != null || c.GetComponentInParent<MineRock5>() != null)
		{
			return null;
		}
		// list v2 V1 (kept): ore the Ore job itself would mine is never "in the way"
		if (GetHarvestCandidateWithType(c, 3, out _) != null)
		{
			return null;
		}
		mode = 1;
		GameObject go = GetHarvestCandidateWithType(c, 1, out type);
		if (go == null)
		{
			// a bush: something the axe hurts that isn't a stump
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
			return null;
		}
		return go;
	}

	// a thing found in the trench (or hit by a dirt swing): a bush is cut where it stands (J3), the rest go to the Wood /
	// Stone flow. 1 = taken, 2 = the spot was skipped (no tool for it), 0 = left alone (no tool, and canSkip is off).
	private int OvTake(GameObject best, int bestMode, string bestType, OvSpot q, bool canSkip, string how)
	{
		ItemDrop.ItemData tool = FindBestTool(bestMode);
		if (tool == null)
		{
			if (!canSkip)
			{
				return 0;
			}
			OvDigOff();
			OvSkip(q, "\"" + best.name + "\" is in the way and it has no " + (bestMode == 1 ? "axe" : "pickaxe") + " for it", normal: true);
			return 2;
		}
		if (q != null && !_vp.HandFor.ContainsKey(best.GetInstanceID()))
		{
			_vp.HandFor[best.GetInstanceID()] = q;
		}
		_dgLastProg = true;   // not a swing that changed nothing (H5)
		if (bestMode == 1 && bestType == "Destructible")
		{
			// J3: a bush - the dirt walk to it, faced, and the dirt swing with the axe; 3 swings that don't hit it skip the spot
			Vector3 at = best.transform.position;
			at.y = GroundAt(at);
			OvDigBegin(at, q, ramp: false);
			_dgBushOn = true;
			_dgBush = best;
			_dgBushMiss = 0;
			// A3: claimed, so another companion leaves it alone (the same list the Wood / Stone jobs claim their targets in)
			_dgBushClaim = best.GetInstanceID();
			s_claimedTargets.Add(_dgBushClaim);
			if (CompanionsPlugin.LogNormal) LogInfo($"{how}: the bush \"{best.name}\" at {MHR_Mover.Fmt(best.transform.position)}{(q != null ? $" (spot {q.Idx + 1})" : "")} - cutting it where it stands with {tool.m_shared.m_name}");
			return 1;
		}
		OvDigOff();
		_ovHand = best;
		_ovHandSpot = q;
		_ovHandMode = bestMode;
		_hdSince = Time.time;   // Y6e: after a way up toward it (Y6d) the clock starts here again
		_hdClock = 0f;          // AA4
		_hdHitSeen = _hdSince;
		_ovHandAtOn = false;    // AA6: the reach point is looked at again
		_ovHandAtNext = 0f;
		_hdRamped = _hdRampNext;
		_hdRampNext = false;
		_hdBest = float.MaxValue;
		_hdHwTime = 0f;
		_pitSkipId = 0;
		_ai?.StopMoving();
		_target = best;
		_targetType = ClassifyTargetType(best, bestMode);
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
		if (CompanionsPlugin.LogNormal) LogInfo($"{how}: \"{best.name}\" ({_targetType}) at {MHR_Mover.Fmt(best.transform.position)}{(q != null ? $" (spot {q.Idx + 1})" : "")} - clearing it the {(bestMode == 1 ? "Wood" : "Stone")} job's way with {tool.m_shared.m_name}");
		return 1;
	}

	// the nearest thing within 2 m of p (1 m above the ground as it is now), taken with OvTake. With canSkip (a spot's own
	// check) a thing it gave up on for this spot, or one it has no tool for, skips the spot; without it (H2, before a swing)
	// those are left alone. 1 = taken, 2 = spot skipped, 0 = nothing.
	private int OvThingAt(Vector3 p, OvSpot q, bool canSkip)
	{
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		// G5: centred 1 m above the ground as it is now (the planned height can be 8 m above the dug ground)
		Vector3 at = p;
		at.y = GroundAt(p) + 1.0f;
		int k = Physics.OverlapSphereNonAlloc(at, OvThingRadius, s_ovThingBuf, s_ovThingMask, QueryTriggerInteraction.Ignore);
		GameObject best = null, blocked = null;
		string bestType = null;
		int bestMode = 0;
		float bd = float.MaxValue;
		for (int i = 0; i < k; i++)
		{
			GameObject go = OvThingOf(s_ovThingBuf[i], out int mode, out string type);
			if (go == null)
			{
				continue;
			}
			// V4 / Y6a: a thing whose bottom is more than 2 m above the trench floor at its nearest spot is on the wall top,
			// not in the trench - left alone
			OvSpot fs = q ?? OvNearest(p);
			bool haveBox = OvThingBox(go, out Bounds tb);
			// AA7: one whose box hangs over the spot is in the trench however high it is
			bool overP = haveBox && p.x >= tb.min.x && p.x <= tb.max.x && p.z >= tb.min.z && p.z <= tb.max.z;
			if (!overP && s_ovThingBuf[i].bounds.min.y > (fs != null ? GroundAt(fs.P) : base.transform.position.y) + OvThingAbove)
			{
				continue;
			}
			if (_vp.PitLeft.TryGetValue(go.GetInstanceID(), out int left) && left >= 2)
			{
				continue;   // Y6e: left for this vein
			}
			int id = go.GetInstanceID();
			if (s_claimedTargets.Contains(id) && id != _claimedTargetId)
			{
				continue;   // another companion is on it
			}
			if (_blacklist.ContainsKey(id))
			{
				// G6: a thing it couldn't clear skips only the spot it was handed over for; for the others it is ignored
				if (canSkip && q != null && _vp.HandFor.TryGetValue(id, out OvSpot hs) && hs == q)
				{
					blocked = go;
				}
				continue;
			}
			float d2 = Flat(go.transform.position, p);
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
				OvDigOff();
				OvSkip(q, "\"" + blocked.name + "\" is in the way and it couldn't clear it", normal: true);
				return 2;
			}
			return 0;
		}
		return OvTake(best, bestMode, bestType, q, canSkip, q != null ? $"In the trench at spot {q.Idx + 1}" : "In the trench");
	}

	// H1: HarvestHitRegister told it one of its swings hit go (noted whatever the swing was aimed at)
	private void OvNoteHit(GameObject go)
	{
		if (go == null)
		{
			return;
		}
		MineRock5 m = go.GetComponentInParent<MineRock5>();
		if (m != null && _vp != null && _vp.Rocks.Contains(m))
		{
			_ovOreHitAt = Time.time;
			return;
		}
		_ovThingHitGo = go;
		_ovThingHitAt = Time.time;
	}

	private static bool OvSame(GameObject a, GameObject b)
	{
		return a != null && b != null && (a == b || a.transform.IsChildOf(b.transform) || b.transform.IsChildOf(a.transform));
	}

	// G9: what is still within 2 m of a spot that the Wood or Stone job would take (the same tests as OvThing)
	private void OvThingsLeft(OvSpot q, Dictionary<int, string> into)
	{
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		Vector3 at = q.P;
		at.y = GroundAt(q.P) + 1.0f;
		int k = Physics.OverlapSphereNonAlloc(at, OvThingRadius, s_ovThingBuf, s_ovThingMask, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < k; i++)
		{
			GameObject go = OvThingOf(s_ovThingBuf[i], out _, out _);
			if (go == null || into.ContainsKey(go.GetInstanceID()))
			{
				continue;
			}
			into[go.GetInstanceID()] = "\"" + go.name + "\" at " + MHR_Mover.Fmt(go.transform.position) + " (spot " + (q.Idx + 1) + (_blacklist.ContainsKey(go.GetInstanceID()) ? ", it gave up on it" : "") + ")";
		}
	}

	private OvSpot OvNearest(Vector3 p)
	{
		OvSpot near = null;
		float nd = float.MaxValue;
		foreach (OvSpot s in _vp.Spots)
		{
			float d = Flat(p, s.P);
			if (d < nd)
			{
				nd = d;
				near = s;
			}
		}
		return near;
	}

	// G10: a saved ramp whose top is out of the trench (not 1.5 m or more under the ground 3 m outside the ring there)
	private bool OvHasWayOut()
	{
		foreach (OvRamp r in _vp.Ramps)
		{
			if (OvRampOut(r))
			{
				return true;
			}
		}
		return false;
	}

	// R3: a saved ramp is a way out only if its top reached the ground outside as it was when it began
	private static bool OvRampOut(OvRamp r)
	{
		return r.Top.y >= r.Need - OvStepUp;
	}

	// R5: the nearest way out of this vein's trench (a saved ramp that is really out, R3) not tried on this trip, for the
	// mover when it is stuck below its goal on a vein (where it would otherwise climb). False = none.
	internal bool VeinWayOut(Vector3 pos, HashSet<int> tried, out Vector3 bottom, out Vector3 top, out int id)
	{
		bottom = top = pos;
		id = -1;
		if (_vp == null)
		{
			return false;
		}
		float bd = float.MaxValue;
		for (int i = 0; i < _vp.Ramps.Count; i++)
		{
			OvRamp r = _vp.Ramps[i];
			if (tried.Contains(i) || !OvRampOut(r))
			{
				continue;
			}
			float d = Flat(pos, r.Bottom);
			if (d < bd)
			{
				bd = d;
				bottom = r.Bottom;
				top = r.Top;
				id = i;
			}
		}
		return id >= 0;
	}

	// E2: the nearest spot whose floor is at least 5 m from every place tried before (null = none)
	private OvSpot OvWayOutSpot(List<Vector3> tried)
	{
		Vector3 me = base.transform.position;
		OvSpot best = null;
		float bd = float.MaxValue;
		foreach (OvSpot sp in _vp.Spots)
		{
			bool far = true;
			foreach (Vector3 t in tried)
			{
				if (Flat(t, sp.P) < OvWayOutApart)
				{
					far = false;
					break;
				}
			}
			float d = Flat(me, sp.P);
			if (far && d < bd)
			{
				bd = d;
				best = sp;
			}
		}
		return best;
	}

	// E2 / E3: getting out of the trench (leaving): a saved way out of the trench first (walked to along the floor), else
	// a new one cut from here
	private void OvGetOut(Vector3 goal, string why)
	{
		_outTried.Clear();
		_woActive = false;
		Vector3 me = base.transform.position;
		OvRamp best = null;
		float bd = float.MaxValue;
		foreach (OvRamp r in _vp.Ramps)
		{
			if (_rpTried.Contains(r) || !OvRampOut(r))
			{
				continue;
			}
			float d = Flat(me, r.Bottom);
			if (d < bd)
			{
				bd = d;
				best = r;
			}
		}
		if (best != null)
		{
			_woActive = true;
			_woOut = true;
			_woFloor = best.Bottom;
			_woGoal = best.Top;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Getting out ({why}) by the way out at {MHR_Mover.Fmt(best.Bottom)} ({bd:F0} m along the trench)");
			return;
		}
		OvRampBegin(goal, null, outOfTrench: true, why);
	}

	// E2: getting out failed at one place: the next (5 m from the places tried), up to 3; then it is stuck and waits for you
	private void OvGetOutNext(string why)
	{
		OvSpot sp = _outTried.Count < OvWayOutTries ? OvWayOutSpot(_outTried) : null;
		if (sp == null)
		{
			_ovOut = false;
			_woActive = false;
			OvStuck($"couldn't cut its way out in {_outTried.Count} tries: " + why);
			return;
		}
		_woActive = true;
		_woOut = true;
		_woFloor = sp.P;
		_woFloor.y = GroundAt(sp.P);
		_woGoal = OvOutside(sp);
		_woGoal.y = OvWallTop(sp);   // Y2
		_moveTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo($"Trying another place to get out: {MHR_Mover.Fmt(_woFloor)} (spot {sp.Idx + 1}, try {_outTried.Count + 1} of {OvWayOutTries}) - {why}");
	}

	// E2 / E3: the walk along the trench floor to where it tries the way out (the normal walk, 10 s like any other)
	private void OvWayOutTick()
	{
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (Flat(me, _woFloor) <= 1.0f)
		{
			_woActive = false;
			_ai?.StopMoving();
			if (_woOut)
			{
				OvRampBegin(_woGoal, null, outOfTrench: true, _ovOutWhy ?? "getting out");
				return;
			}
			if (me.y > _woGoal.y - OvOutDeep)   // Y2: the wall top along the line out
			{
				_vp.ExitAt.Add(me);   // R4: counted, so no other try is made within 15 m of here
				_vp.ExitBy = null;
				if (CompanionsPlugin.LogNormal) LogInfo("The trench is shallow here - no way out needed");
				return;
			}
			_vp.ExitAt.Add(me);
			_exTry = true;
			OvRampBegin(_woGoal, null, outOfTrench: false, _woWhy);
			return;
		}
		if (WalkTimer(dt, _woFloor) && _moveTimer > 10f)
		{
			_woActive = false;
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to {MHR_Mover.Fmt(_woFloor)} along the trench in 10 s");
			if (!_woOut && _vp.ExitBy == this)
			{
				_vp.ExitBy = null;
			}
			if (_woOut)
			{
				_outTried.Add(_woFloor);
				OvGetOutNext("couldn't get there");
				return;
			}
			_vp.ExitAt.Add(_woFloor);   // counts as a try
			return;
		}
		WalkGo(_woFloor, 0.5f, Flat(me, _woFloor) > 8f, dt);
	}

	// C7 (build 1): the trench is done - it says so once and stands by the vein
	private void OvDoneTick()
	{
		OvRelease();
		if (!_ovDoneSaid)
		{
			OvTrenchDoneSay();
		}
		// build 2: clearing and the pop
		if (OvClearTick())
		{
			return;
		}
		if (OvC().Stopped)
		{
			_ai?.StopMoving();   // P7: it waits by the vein
			return;
		}
		// P8: done - it won't pick this vein again for a while (parts given up or buried may be left), and leaves by the
		// way out
		foreach (MineRock5 r in _vp.Rocks)
		{
			if (r != null)
			{
				_blacklist[r.gameObject.GetInstanceID()] = Time.time;
			}
		}
		OvLeaveOut(MHR_GatherRules.AfterVeinGoal(this, out Vector3 dvGoal, out _) ? dvGoal : base.transform.position, "the vein is done");
	}

	private void OvTrenchDoneSay()
	{
		_ai?.StopMoving();
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
		// G9: the trees, bushes and rocks still within 2 m of a spot (at today's ground), so the log shows what was missed
		Dictionary<int, string> left = new Dictionary<int, string>();
		foreach (OvSpot s in _vp.Spots)
		{
			if (!s.Closed)
			{
				OvThingsLeft(s, left);
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Left in the trench: {(left.Count == 0 ? "nothing" : left.Count + " thing(s): " + string.Join("; ", new List<string>(left.Values).GetRange(0, Mathf.Min(12, left.Count))) + (left.Count > 12 ? "; ..." : ""))}");
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_vp.Name}\" is done after {_vp.Lap} lap(s): {_vp.Spots.Count} spots, {closed} closed for good, {notDug} more than 0.3 m above the goal{(notDug > 0 ? " (at " + where + (notDug > 6 ? ", ..." : "") + ")" : "")} - clearing next");
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
		OvDigOff();
		_dgActive = true;
		_dgPoint = point;
		_dgSpot = spot;
		_dgRamp = ramp;
		_dgWalking = false;
		_dgSwung = false;
		_dgNoProg = 0;
		_dgLastProg = false;
		_dgThingChecked = false;
		_dgThingSwing = false;
		_dgWalkThing = false;
		_dgBushOn = false;
		_dgBush = null;
		_dgBushMiss = 0;
		_dgRampN = 0;
		_dgRampProg = false;
		_dgHwBest = float.MaxValue;
		_dgHwTime = 0f;
		_dgClear = false;
		_dgReached = false;
		_dgLimit = false;
		_dgPart = false;
		_bdWaitFrom = -1f;   // AA6: a new point - its own wait
		_bdNextPick = 0f;
		// J1: the walk clock starts here only (not each time it goes from swinging back to walking)
		_moveTimer = 0f;
	}

	private void OvDigOff()
	{
		_dgActive = false;
		_dgSwung = false;
		_dgWalking = false;
		_dgBushOn = false;
		_dgBush = null;
		if (_dgBushClaim != 0)
		{
			if (_dgBushClaim != _claimedTargetId)
			{
				s_claimedTargets.Remove(_dgBushClaim);
			}
			_dgBushClaim = 0;
		}
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

	// J1: work on a spot (walking to it, swinging, catching its breath, cutting a bush on it) counts toward 30 s; the ground
	// going down (or a swing landing on the vein's ore or on the bush it cuts) starts the 30 s again. True = skipped.
	private bool OvSpotClock(float dt)
	{
		OvSpot q = _dgSpot;
		if (q == null || _vp == null)
		{
			return false;
		}
		if (_spFor != q || _spLap != _vp.Lap)
		{
			_spFor = q;
			_spLap = _vp.Lap;
			_spIdle = 0f;
		}
		_spIdle += dt;
		if (_spIdle < OvSpotIdle)
		{
			return false;
		}
		// Z1 (test 10): a pit thing it was cutting where it stands counts as left once (two = left for this vein)
		GameObject bush = _dgBushOn ? _dgBush : null;
		OvDigOff();
		if (bush != null)
		{
			OvPitLeftNote(bush);
		}
		OvSkip(q, $"{OvSpotIdle:F0} s of work on it without the ground going down", normal: true);
		return true;
	}

	private void OvSpotProgress()
	{
		if (_dgSpot != null && _spFor == _dgSpot)
		{
			_spIdle = 0f;
		}
	}

	// H4: another companion on this vein within 1.2 m ahead (toward the dirt it walks to)
	private bool OvOtherAhead(Vector3 me, Vector3 toward)
	{
		Vector3 dir = toward - me;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.01f || _vp == null)
		{
			return false;
		}
		dir.Normalize();
		foreach (HarvestController h in _vp.Workers.Keys)
		{
			if (h == null || h == this)
			{
				continue;
			}
			Vector3 o = h.transform.position - me;
			o.y = 0f;
			if (o.magnitude <= OvSideNear && o.sqrMagnitude > 0.0001f && Vector3.Dot(o.normalized, dir) > 0.5f)
			{
				return true;
			}
		}
		return false;
	}

	private void OvDigTick()
	{
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (OvSpotClock(dt))
		{
			return;
		}
		bool busy = _humanoid != null && (_humanoid.InAttack() || _humanoid.InDodge());
		if (_dgSwung)
		{
			if (busy || Time.time - _dgSwungAt < 0.35f)
			{
				OvFace(_dgBushOn && _dgBush != null ? _dgBush.transform.position : _dgPoint);
				return;
			}
			OvDigJudge();
			return;
		}
		if (_dgBushOn && (_dgBush == null || !_dgBush.activeInHierarchy))
		{
			// J3: the bush is down
			if (CompanionsPlugin.LogNormal) LogInfo($"Cut the bush at {MHR_Mover.Fmt(_dgPoint)} - back to the trench");
			OvDigOff();
			_dgLastProg = true;
			return;
		}
		// Y6b: before it walks to a dig point, anything within 2 m of it is cleared first (H2's check, made earlier: Je walked
		// 30 s into a fir standing on the point in test 9)
		if (!_dgBushOn && !_dgWalkThing)
		{
			_dgWalkThing = true;
			OvSpot ws = _dgSpot ?? (_rpActive ? _rpSpot : null) ?? OvNearest(_dgPoint);
			if (OvThingAt(_dgPoint, ws, canSkip: false) != 0)
			{
				return;
			}
		}
		float reach = GetAttackRange();
		if (!_dgRamp)
		{
			// walking to it: within reach, and not more than 1.2 m below its feet (then it walks on down to it)
			float flat = Flat(me, _dgPoint);
			if (!_dgBushOn && !_dgThingChecked && _dgSpot != null && flat <= OvThingNear)
			{
				// G5: within 4 m of the spot - anything on it now goes to the Wood / Stone flow (a bush: cut here) first
				_dgThingChecked = true;
				if (OvThing(_dgSpot))
				{
					return;
				}
			}
			bool below = _dgPoint.y < me.y - 1.2f;
			float upNow = GroundAt(_dgPoint) - me.y;
			float h = _character != null ? _character.GetHeight() : 1.8f;
			if (!_dgBushOn && _dgSpot != null && !_dgSpot.RampTried && flat <= OvRampReach && upNow >= OvRampRatio * h)
			{
				// G2: at the wall in front of the spot (within 3 m) and it is 1.5 x its height up: carve the way up
				OvSpot rq = _dgSpot;
				OvDigOff();
				OvRampBegin(_dgPoint, rq, outOfTrench: false, $"spot {rq.Idx + 1} is {upNow:F1} m up, {flat:F1} m away (1.5 x its height is {OvRampRatio * h:F1} m)");
				return;
			}
			if (flat > reach * 0.9f || (below && flat > 0.6f))
			{
				_dgWalking = true;
				float up = GroundAt(_dgPoint) - me.y;
				OvSpot q = _dgSpot;
				// J2: the job's own headway - 0.3 m closer (flat) resets it; only time spent walking counts
				if (flat < _dgHwBest - OvHeadwayGain)
				{
					_dgHwBest = flat;
					_dgHwTime = 0f;
				}
				else
				{
					_dgHwTime += dt;
				}
				// H4: the side step when another companion is in the way is in WalkGo now (AA6, every walk on a vein)
				// AA6 (a): a trench point another companion stands on or walks to - it waits up to 5 s, then the spot is skipped
				if (q != null && !_dgBushOn && !_dgClear && OvBoardTrenchWait())
				{
					return;
				}
				if (!_dgBushOn && q != null && !q.RampTried && flat <= OvRampReach && up >= OvStepUp && _dgHwTime >= OvNoHeadway)
				{
					OvDigOff();
					OvRampBegin(_dgPoint, q, outOfTrench: false, $"no headway for {OvNoHeadway:F0} s toward spot {q.Idx + 1} (not 0.3 m closer), {up:F1} m up");
					return;
				}
				if (WalkTimer(dt, _dgPoint) && _moveTimer > 10f)
				{
					if (_dgBushOn)
					{
						GameObject b = _dgBush;
						OvDigOff();
						OvBushGiveUp(b, q, $"couldn't get to it in {10f:F0} s ({flat:F1} m away, {up:F1} m up)");
						return;
					}
					OvDigOff();
					if (q != null && up > 0f && !q.RampTried && flat <= OvRampReach)
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
		if (_dgBushOn)
		{
			// J3: the bush, with the axe (WorkMode)
			OvFace(_dgBush.transform.position);
			int bw = SwingNow(out _, out _);
			if (bw == -1)
			{
				GameObject b = _dgBush;
				OvDigOff();
				OvBushGiveUp(b, _dgSpot, "no axe for it");
				return;
			}
			if (bw == 1)
			{
				_dgSwung = true;
				_dgSwungAt = Time.time;
			}
			return;
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
		// N3: a clearing point always gets its swing first (OvDigJudge decides when it's done)
		if (!_dgClear && g <= OvFloor(_dgPoint) + OvDigGain)
		{
			OvDigOff();
			_dgLastProg = false;
			if (_dgSpot != null && !_dgRamp)
			{
				OvSpotDone(_dgSpot, "at the floor (the game's 8 m limit or the water)");
			}
			return;
		}
		// H2: before each dirt or ramp swing, anything within 2 m of the dirt (a tree that fell in, a log that rolled in)
		// is cleared first. Things it gave up on, or has no tool for, are left alone here.
		if (!_dgThingSwing)
		{
			_dgThingSwing = true;
			OvSpot hs = _dgSpot ?? (_rpActive ? _rpSpot : null) ?? OvNearest(_dgPoint);
			if (OvThingAt(_dgPoint, hs, canSkip: false) != 0)
			{
				return;
			}
		}
		OvFace(_dgPoint);
		int sw = SwingNow(out ItemDrop.ItemData swItem, out _);
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
			OvSweepLog(swItem);
		}
	}

	// R6 (kept by W1c): the pickaxe's sweep (the game's own attack data, as the combat log prints it), once per vein
	private void OvSweepLog(ItemDrop.ItemData item)
	{
		if (_aimLogVp == _vp || item == null || item.m_shared == null)
		{
			return;
		}
		_aimLogVp = _vp;
		Attack a = item.m_shared.m_attack;
		if (CompanionsPlugin.LogNormal) LogInfo(a != null ? $"Pickaxe swing \"{item.m_shared.m_name}\": sweep {a.m_attackAngle:F0} deg ({a.m_attackType}), tilt up to {a.m_maxYAngle:F0} deg, range {a.m_attackRange:F2} m, hit height {a.m_attackHeight:F2} m, ray width {a.m_attackRayWidth:F2} m" : $"Pickaxe swing \"{item.m_shared.m_name}\": no attack data");
	}

	// J3: a bush it couldn't cut: given up (like the Wood job's own give-up), and the spot it was for is skipped this lap
	private void OvBushGiveUp(GameObject b, OvSpot q, string why)
	{
		if (b != null)
		{
			_blacklist[b.GetInstanceID()] = Time.time;
			OvPitLeftNote(b);
			if (q != null && _vp != null)
			{
				_vp.HandFor[b.GetInstanceID()] = q;
			}
		}
		_dgLastProg = true;
		if (q != null)
		{
			OvSkip(q, "the bush \"" + (b != null ? b.name : "?") + "\" is in the way and it couldn't cut it: " + why, normal: true);
		}
		else if (CompanionsPlugin.LogNormal)
		{
			LogInfo($"Gave up on the bush \"{(b != null ? b.name : "?")}\": {why}");
		}
	}

	private void OvDigJudge()
	{
		_dgSwung = false;
		_dgThingSwing = false;
		if (_dgBushOn && (_dgBush == null || !_dgBush.activeInHierarchy))
		{
			// B1: the swing took the bush down - cut, not a miss
			if (CompanionsPlugin.LogNormal) LogInfo($"Cut the bush at {MHR_Mover.Fmt(_dgPoint)} - back to the trench");
			OvSpotProgress();
			OvDigOff();
			_dgLastProg = true;
			return;
		}
		if (_dgBushOn)
		{
			// J3: did the swing land on the bush?
			bool hitB = _ovThingHitAt >= _dgSwungAt && OvSame(_ovThingHitGo, _dgBush);
			_dgBushMiss = hitB ? 0 : _dgBushMiss + 1;
			if (hitB)
			{
				OvSpotProgress();
			}
			if (CompanionsPlugin.LogDetailed) Log($"Axe swing at the bush \"{(_dgBush != null ? _dgBush.name : "?")}\": {(hitB ? "hit" : $"didn't hit it ({_dgBushMiss} in a row)")}, {Flat(base.transform.position, _dgPoint):F1} m away, feet {base.transform.position.y:F2}");
			if (_dgBushMiss >= OvBushMiss)
			{
				GameObject b = _dgBush;
				OvSpot q = _dgSpot;
				OvDigOff();
				OvBushGiveUp(b, q, $"{OvBushMiss} swings didn't hit it");
			}
			return;
		}
		float after = GroundAt(_dgPoint);
		bool prog = _dgBefore - after >= OvDigGain;
		bool hitThing = _ovThingHitAt >= _dgSwungAt && _ovThingHitGo != null && _ovThingHitGo.activeInHierarchy;
		bool hitOre = _ovOreHitAt >= _dgSwungAt;
		bool dug = _dgDugAt >= _dgSwungAt;   // V2: the game dug where this swing landed
		string dugAt = dug ? $", dug at {MHR_Mover.Fmt(_dgDugPt)}" : "";
		if (hitThing)
		{
			// H1: it hit a tree, log, stump, bush or a rock that isn't this vein: that goes to the Wood / Stone flow (a bush:
			// cut here) - unless it gave up on it, another companion has it, or it has no tool for it
			GameObject go = _ovThingHitGo;
			Collider c = go.GetComponentInChildren<Collider>();
			int mode = 0;
			string type = null;
			GameObject thing = c != null ? OvThingOf(c, out mode, out type) : null;
			if (thing != null)
			{
				int id = thing.GetInstanceID();
				bool free = !_blacklist.ContainsKey(id) && (!s_claimedTargets.Contains(id) || id == _claimedTargetId);
				if (free)
				{
					OvSpot hs = _dgSpot ?? (_rpActive ? _rpSpot : null) ?? OvNearest(_dgPoint);
					if (CompanionsPlugin.LogDetailed) Log($"Dirt swing at {MHR_Mover.Fmt(_dgPoint)} hit \"{thing.name}\"");
					if (OvTake(thing, mode, type, hs, canSkip: false, "A dirt swing hit it") != 0)
					{
						return;
					}
				}
			}
		}
		// H1 / H5: a swing that landed on this vein's ore is progress (it keeps hitting through it); only a swing that hit
		// nothing it could deal with and left the ground as it was counts as "no change"
		if (_dgPart)
		{
			// R2a: mining the part itself from where it stands - a swing on the vein's rock is progress, 3 that don't hit it
			// end it (its break is seen by OvClRound). Y7: a dig doesn't count here - the dirt there is at the 8 m limit, so
			// the dig lands somewhere else (test 9: the floor at its own feet, 30 s at a time)
			_dgNoProg = hitOre ? 0 : _dgNoProg + 1;
			if (CompanionsPlugin.LogDetailed) Log($"Swing at the part at {MHR_Mover.Fmt(_dgPoint)}: {(hitOre ? "hit the vein's rock" : $"didn't hit the rock ({_dgNoProg} in a row)")}{dugAt}, feet {base.transform.position.y:F2}");
			if (_dgNoProg >= OvNoProgSwings)
			{
				OvDigOff();
			}
			return;
		}
		if (_dgClear)
		{
			// N3: a clearing point - a swing on the vein's rock or the ground going down is progress; done = a swing that
			// met no rock with the ground at its feet (or at the game's limit); 3 swings with neither close it
			// R2: at the game's limit but more than a step (0.7 m) above its feet: not done - a shelf it can't dig or step onto
			float feet = base.transform.position.y;
			bool atLimit = after <= OvFloor(_dgPoint) + OvDigGain;
			bool shelf = !hitOre && atLimit && after > feet + OvStepUp;
			bool done = !hitOre && !shelf && (after <= _dgGoalY + OvDigGain || atLimit);
			bool cProg = prog || hitOre || dug;
			_dgNoProg = cProg ? 0 : _dgNoProg + 1;
			if (CompanionsPlugin.LogDetailed) Log($"Clearing swing at {MHR_Mover.Fmt(_dgPoint)}: ground {_dgBefore:F2} -> {after:F2}{(hitOre ? ", hit the vein's rock" : "")}{dugAt}{(shelf ? $" - at the game's 8 m dig limit, {after - feet:F1} m above its feet" : done ? " - done, down to its feet" : cProg ? "" : $" - nothing ({_dgNoProg} in a row)")}, feet {feet:F2}");
			if (shelf)
			{
				_dgLimit = true;
				OvDigOff();
				return;
			}
			if (done)
			{
				OvDigOff();
				_dgReached = true;
			}
			else if (_dgNoProg >= OvNoProgSwings)
			{
				OvDigOff();
			}
			return;
		}
		bool oreProg = !prog && hitOre;
		// AA5: a trench swing that hit a thing (one it left, another's, or one it has no tool for) and changed nothing isn't
		// "can't go lower": the spot is skipped this lap (a left thing is on it) and the next lap comes back to it
		if (hitThing && !prog && !oreProg && !dug && !_dgRamp && _dgSpot != null)
		{
			OvSpot ts = _dgSpot;
			string tn = _ovThingHitGo != null ? _ovThingHitGo.name : "?";
			OvDigOff();
			OvSkip(ts, $"its swing hit \"{tn}\", a thing it left, and changed nothing", normal: true);
			return;
		}
		// V2: a swing the game dug for counts as doing something (the 3-swing rule); only the point going down or the
		// vein's ore restarts the spot's 30 s clock
		_dgLastProg = prog || oreProg || dug;
		_dgNoProg = _dgLastProg ? 0 : _dgNoProg + 1;
		if (prog || oreProg)
		{
			OvSpotProgress();
		}
		if (CompanionsPlugin.LogDetailed) Log($"Dirt swing at {MHR_Mover.Fmt(_dgPoint)}{(_dgRamp ? " (ramp step)" : _dgSpot != null ? $" (spot {_dgSpot.Idx + 1})" : "")}: ground {_dgBefore:F2} -> {after:F2}{dugAt}{(oreProg ? " - hit the vein's ore" : prog || dug ? "" : $" - no change ({_dgNoProg} in a row{(hitThing ? ", it hit \"" + _ovThingHitGo.name + "\"" : "")})")}, feet {base.transform.position.y:F2}");
		if (_dgRamp)
		{
			// E4 (John's way): 2 swings at the wall in front, then it steps in and looks again; a swing that changed
			// nothing ends the step at once (the ramp counts it toward its 3)
			_dgRampN++;
			_dgRampProg |= _dgLastProg;
			if (_dgLastProg && _dgRampN < OvRampSwings)
			{
				_dgPoint.y = after;
				return;
			}
			_dgLastProg = _dgRampProg;
			OvDigOff();
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
		if (_vp != null && _rpBadLap != _vp.Lap)
		{
			_rpBadLap = _vp.Lap;
			_rpBad.Clear();
		}
		if (!outOfTrench && spot != null)
		{
			foreach (Vector3 b in _rpBad)
			{
				if (Flat(b, me) <= OvRampBadNear)
				{
					spot.RampTried = true;
					OvSkip(spot, $"it would need a way up from here, and the wall here is at the game's 8 m dig limit ({why})", normal: true);
					return;
				}
			}
		}
		_rpActive = true;
		_rpGoal = goal;
		_rpSpot = spot;
		_rpOut = outOfTrench;
		// R3: a way out (getting out, or the way out cut while it works) must reach the ground outside as it is now; its
		// goal is moved on out along the same line to (that height - its feet) + 2 m away, so its own steps can't dig the
		// goal down. Any other ramp saves the ground outside its nearest spot as the height a way out needs.
		_rpWay = outOfTrench || _exTry;
		if (_rpWay)
		{
			_rpNeed = goal.y;
			Vector3 d = goal - me;
			d.y = 0f;
			if (d.sqrMagnitude > 0.0001f)
			{
				_rpGoal = me + d.normalized * (Mathf.Max(0f, goal.y - me.y) + OvWayMore);
				_rpGoal.y = goal.y;
			}
		}
		else
		{
			OvSpot ns = _vp != null ? OvNearest(me) : null;
			_rpNeed = ns != null ? GroundAt(OvOutside(ns)) : goal.y;
		}
		_rpStart = me;
		_rpBestY = me.y;
		_rpBestAt = Time.time;
		_rpNoProg = 0;
		_rpSwings = 0;
		_rpStepPt = new Vector3(float.MaxValue, 0f, 0f);
		_rpHwBest = float.MaxValue;
		_rpHwTime = 0f;
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
				if (!_rpTried.Contains(r) && (!_rpWay || OvRampOut(r)) && Flat(r.Bottom, me) <= OvRampNear && Vector3.Distance(r.Top, goal) <= OvRampTopNear)
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
		if (_rpWay)
		{
			// R3: up when its feet are within a step of the height the ground outside had when it began
			if (me.y >= _rpNeed - OvStepUp)
			{
				OvRampFinish("up");
				return;
			}
		}
		else if (_rpThing != null)
		{
			// Y6d: up when its feet are within a step of the thing's base
			if (me.y >= _rpThingBase - OvStepUp)
			{
				OvRampFinish("up beside it");
				return;
			}
		}
		else
		{
			float gGoal = GroundAt(_rpGoal);
			if (gGoal <= me.y + OvStepUp || (Flat(me, _rpGoal) <= reach && gGoal <= me.y + OvStepUp))
			{
				OvRampFinish("up");
				return;
			}
		}
		if (me.y > _rpBestY + OvRampRise && (_character == null || _character.IsOnGround()))   // Y4: on the ground only
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
			if (_rpWay)
			{
				// Y3: at its goal point (well out past the wall, R3) having risen 1.5 x its height = out; its own steps can
				// have dug the ground there below the height it began with
				float hh = _character != null ? _character.GetHeight() : 1.8f;
				if (me.y - _rpStart.y >= OvRampRatio * hh)
				{
					_rpNeed = Mathf.Min(_rpNeed, me.y);
					OvRampFinish($"out: at its goal point having risen {me.y - _rpStart.y:F1} m");
					return;
				}
				// R3: at its goal but not up to the ground outside - the try failed
				OvRampFail($"got to {MHR_Mover.Fmt(_rpGoal)} without getting up to {_rpNeed:F1} (feet at {me.y:F1})");
				return;
			}
			OvRampFinish("there");
			return;
		}
		dir.Normalize();
		// E4 (John's way): right up against the wall - the ground 0.5 m in front
		Vector3 ahead = me + dir * OvRampAhead;
		float ga = GroundAt(ahead);
		ahead.y = ga;
		bool atLimit = ga <= OvFloor(ahead) + OvDigGain;
		float jump = MHR_Mover.JumpUp(_character);
		// E4: walking up to the wall, 1 s without getting 0.3 m closer (flat) to the goal = it is against it
		float fl = Flat(me, _rpGoal);
		if (fl < _rpHwBest - OvHeadwayGain)
		{
			_rpHwBest = fl;
			_rpHwTime = 0f;
		}
		else
		{
			_rpHwTime += dt;
		}
		bool against = _rpHwTime >= OvRampHeadway && ga > me.y + 0.1f;
		if (ga > me.y + OvStepUp || (against && !atLimit))
		{
			if (atLimit)
			{
				if (ga - me.y <= jump)
				{
					// E1: it can't be dug any lower but it is no higher than its jump: on toward it, the mover's own hop
					Vector3 hop = me + dir * 1.5f;
					hop.y = Mathf.Min(GroundAt(hop), me.y + jump);
					_moveTimer = 0f;
					WalkGo(hop, 0.3f, false, dt);
					return;
				}
				// G1: the game won't let that ground go any lower and it is too high to jump - no swing, and no new way up
				// from here this lap
				_rpBad.Add(_rpStart);
				OvRampFail($"the ground in front at {MHR_Mover.Fmt(ahead)} is at the game's 8 m dig limit ({ga - me.y:F1} m above its feet, more than its {jump:F1} m jump)");
				return;
			}
			// the wall in front: 2 swings at it (E4), then look again
			if (Flat(ahead, _rpStepPt) > 0.5f)
			{
				_rpStepPt = ahead;
				_rpNoProg = 0;
			}
			_rpHwTime = 0f;
			_ai?.StopMoving();
			OvDigBegin(ahead, null, ramp: true);
			return;
		}
		// low enough to walk or hop: on toward the goal (right up to the wall), the goal kept on its own level (the mover
		// hops, never climbs)
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
				_vp.Ramps.Add(new OvRamp { Bottom = _rpStart, Top = me, Need = _rpNeed });
				if (CompanionsPlugin.LogNormal) LogInfo($"A ramp for everyone on this vein: {MHR_Mover.Fmt(_rpStart)} up to {MHR_Mover.Fmt(me)} ({rise:F1} m; {_vp.Ramps.Count} on this vein)");
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"The way up is done ({how}): rose {rise:F1} m in {_rpSwings} swing(s), feet at {me.y:F1}");
		if (_exTry && _vp != null)
		{
			// E2: it got out - the trench has a way out
			_exTry = false;
			_vp.ExitBy = null;
			if (CompanionsPlugin.LogNormal) LogInfo($"A way out of the trench is cut, from {MHR_Mover.Fmt(_rpStart)} up to {MHR_Mover.Fmt(me)}");
		}
		_rpPart = null;
		if (_rpThing != null)
		{
			OvRampThingBack();   // Y6d
			return;
		}
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
		if (_rpThing != null)
		{
			OvRampThingBack();   // Y6d: the thing gets its try from here (V5's clock)
			return;
		}
		if (_rpPart != null)
		{
			_rpPart.Holder = null;   // build 2 (P5): the walk to the part comes next (RampTried is set)
			_rpPart = null;
			return;
		}
		if (_rpOut)
		{
			// E2: another place, up to 3 tries
			_outTried.Add(_rpStart);
			OvGetOutNext(why);
			return;
		}
		if (_exTry)
		{
			// E2: the try is counted (ExitAt); the next one comes from OvExitTick (R4: the next round's check)
			_exTry = false;
			if (_vp != null && _vp.ExitBy == this)
			{
				_vp.ExitBy = null;
			}
			return;
		}
		OvSkip(_rpSpot, "the way up to it failed: " + why, normal: true);
	}

	// ---------- Y1 / Y6: a way out first, then the things in the pit (John, test 9) ----------
	// Y1 (John: "If there is not a way out they should carve one before doing anything else"): before any piece of work
	// (a trench spot, a clearing round, a thing in the pit), the trench spot nearest it: no real way out within 15 m and the
	// wall there 1.5 x its height (Y2) = it cuts one there first (R4's walk and ramp). True = cutting.
	private bool OvWayOutFirst()
	{
		OvSpot ws = OvNearest(base.transform.position);
		if (ws == null || !OvWayOutNeeded(ws))
		{
			return false;
		}
		OvWayOutHere(ws);
		return true;
	}

	// Y1 + Y6a, the look before any piece of work; at most once a second when it finds nothing. True = busy with one.
	private bool OvFirst()
	{
		if (Time.time < _firstAt)
		{
			return false;
		}
		if (OvWayOutFirst() || OvPitFirst())
		{
			return true;
		}
		_firstAt = Time.time + 1f;
		return false;
	}

	// a thing's game box (its solid colliders); false = none
	private static bool OvThingBox(GameObject go, out Bounds b)
	{
		b = default;
		bool any = false;
		foreach (Collider c in go.GetComponentsInChildren<Collider>())
		{
			if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy)
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

	// Y6a (John: "Clearing trees, bushes, logs, should take priority. Same goes for any rocks that fall into the trench or
	// the area under the ore vein"): a tree, log, stump, bush or rock (not this vein's) in the pit - its game box within
	// 2 m (flat) of a trench spot or under the vein, its bottom no more than 2 m above the floor at its nearest spot - the
	// nearest (flat, from its feet to the box) is taken the Wood / Stone job's way. One companion per thing (claimed);
	// one left twice is left for this vein, the one left last waits for one other piece of work (Y6e). True = taken.
	private bool OvPitFirst()
	{
		OvPlan p = _vp;
		if (p == null || p.Spots.Count == 0)
		{
			return false;
		}
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
		foreach (OvSpot sp in p.Spots)
		{
			float g = GroundAt(sp.P);
			x0 = Mathf.Min(x0, sp.P.x);
			x1 = Mathf.Max(x1, sp.P.x);
			z0 = Mathf.Min(z0, sp.P.z);
			z1 = Mathf.Max(z1, sp.P.z);
			y0 = Mathf.Min(y0, g);
			y1 = Mathf.Max(y1, g);
		}
		y1 = Mathf.Max(y1, p.Box.max.y);
		Vector3 c0 = new Vector3((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
		Vector3 half = new Vector3((x1 - x0) * 0.5f + OvThingRadius + 0.5f, (y1 - y0) * 0.5f + OvThingAbove + 1f, (z1 - z0) * 0.5f + OvThingRadius + 0.5f);
		int k = Physics.OverlapBoxNonAlloc(c0, half, s_ovPitBuf, Quaternion.identity, s_ovThingMask, QueryTriggerInteraction.Ignore);
		Vector3 me = base.transform.position;
		HashSet<int> seen = new HashSet<int>();
		GameObject best = null;
		OvSpot bestSpot = null;
		string bestType = null;
		int bestMode = 0;
		float bd = float.MaxValue;
		for (int i = 0; i < k; i++)
		{
			GameObject go = OvThingOf(s_ovPitBuf[i], out int mode, out string type);
			if (go == null)
			{
				continue;
			}
			int id = go.GetInstanceID();
			if (!seen.Add(id) || id == _pitSkipId || (p.PitLeft.TryGetValue(id, out int left) && left >= 2))
			{
				continue;
			}
			if (s_claimedTargets.Contains(id) && id != _claimedTargetId)
			{
				continue;   // another companion is on it
			}
			if (!OvThingBox(go, out Bounds b))
			{
				continue;
			}
			OvSpot n = OvNearest(b.center);
			// AA7: one whose box hangs over a trench spot is taken however high it is (the Y6d way up reaches it)
			bool overSpot = OvOverSpot(b);
			if (n == null || (!overSpot && b.min.y > GroundAt(n.P) + OvThingAbove))
			{
				continue;   // on the wall top
			}
			if (n.Skip && !p.Done)
			{
				continue;   // Z1 (test 10): its spot is skipped this lap - it waits for the next lap (after the trench: any)
			}
			Vector3 np = b.ClosestPoint(new Vector3(n.P.x, b.center.y, n.P.z));
			bool under = b.center.x >= p.Box.min.x && b.center.x <= p.Box.max.x && b.center.z >= p.Box.min.z && b.center.z <= p.Box.max.z;
			if (Flat(np, n.P) > OvThingRadius && !under && !overSpot)
			{
				continue;
			}
			if (FindBestTool(mode) == null)
			{
				continue;   // no tool for it: nothing to do about it here
			}
			// CC4 (test 12: a fir 54.8 m away was picked): only one within 8 m in a straight line (height counts) - a farther
			// one is left for whoever works nearer it
			float d = Vector3.Distance(me, b.ClosestPoint(me));
			if (d > OvPitNear)
			{
				continue;
			}
			if (d < bd)
			{
				bd = d;
				best = go;
				bestSpot = n;
				bestType = type;
				bestMode = mode;
			}
		}
		if (best == null)
		{
			return false;
		}
		OvRelease();
		return OvTake(best, bestMode, bestType, bestSpot, canSkip: false, "In the pit (things first)") == 1;
	}

	// Y6e: a thing in the pit was left: it waits for one other piece of work; left twice = left for this vein
	private void OvPitLeftNote(GameObject thing)
	{
		if (_vp == null || thing == null)
		{
			return;
		}
		int id = thing.GetInstanceID();
		int n = (_vp.PitLeft.TryGetValue(id, out int was) ? was : 0) + 1;
		_vp.PitLeft[id] = n;
		_pitSkipId = id;
		if (n >= 2 && CompanionsPlugin.LogNormal)
		{
			LogInfo($"Left \"{thing.name}\" at {MHR_Mover.Fmt(thing.transform.position)} twice - it's left for this vein");
		}
	}

	// Y6d: the thing handed to the Wood / Stone flow sits more than a step above its feet and the walk gets no closer (3 s
	// without getting 0.3 m closer): the way up toward it first (the ramp the clearing uses toward a part), then it is
	// handed back. Once per try. True = the way up started.
	private bool OvHandLedge()
	{
		if (_hdRamped || _state != HarvestState.Moving || _ovHand == null || _vp == null || !OvThingBox(_ovHand, out Bounds b))
		{
			return false;
		}
		Vector3 me = base.transform.position;
		if (b.min.y <= me.y + OvStepUp)
		{
			_hdHwTime = 0f;
			return false;
		}
		Vector3 np = b.ClosestPoint(new Vector3(me.x, b.center.y, me.z));
		float d = Flat(me, np);
		if (d < _hdBest - OvHeadwayGain)
		{
			_hdBest = d;
			_hdHwTime = 0f;
			return false;
		}
		_hdHwTime += Time.deltaTime;
		if (_hdHwTime < OvNoHeadway)
		{
			return false;
		}
		// the ground beside the thing on its side: 0.5 m short of its box, toward it
		Vector3 dir = np - me;
		dir.y = 0f;
		dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : base.transform.forward;
		Vector3 goal = np - dir * 0.5f;
		goal.y = GroundAt(goal);
		_rpThing = _ovHand;
		_rpThingMode = _ovHandMode;
		_rpThingType = _targetType;
		_rpThingSpot = _ovHandSpot;
		_rpThingBase = b.min.y;
		string name = _ovHand.name;
		// back to the vein without giving the thing up (OvResume's tail, no blacklist); the thing stays claimed by it
		_rpThingClaim = _rpThing.GetInstanceID();
		_ovHand = null;
		_ovHandSpot = null;
		_ovHandMode = 0;
		_target = _vFor;
		_targetType = "MineRock5";
		ClaimTarget();
		s_claimedTargets.Add(_rpThingClaim);
		_ai?.StopMoving();
		_state = HarvestState.Vein;
		_ovHandAtOn = false;
		OvRampBegin(goal, null, outOfTrench: false, $"\"{name}\" is {b.min.y - me.y:F1} m up and the walk gets no closer");
		return true;
	}

	// Y6d: the way up toward a thing ended (up or not): the thing goes back to the Wood / Stone flow, the same try
	private void OvRampThingBack()
	{
		GameObject t = _rpThing;
		OvRampThingDrop();
		if (t == null || !t.activeInHierarchy || _vp == null)
		{
			return;
		}
		_hdRampNext = true;
		OvTake(t, _rpThingMode, _rpThingType, _rpThingSpot, canSkip: false, "Up beside it");
	}

	// ---------- Y5: a way out cut in a fight ----------
	private bool _woCombat;   // Y5: the way out being cut is for a fight (combat takes up no enemy it can't reach meanwhile)

	// Y5: it is cutting a way out for a fight now
	internal bool CuttingWayOut => _woCombat && _vp != null && _vp.ExitBy == this && (_woActive || _rpActive);

	// Y5 (John: "They need to carve a way out to get to the enemies"): stuck below its goal in a fight on a vein with no
	// way out to use (MHR_Mover, where it says "no cliff climb on a vein"): a way out is cut at once, from the trench spot
	// nearest the enemy (flat) when that is within 15 m of it, else the spot nearest it (R4's walk and ramp). One companion
	// per vein at a time; none within 15 m of an earlier try. True = started (the fight lets the enemy go for 20 s).
	internal bool CombatWayOut(Vector3 enemy)
	{
		OvPlan p = _vp;
		if (p == null || _vFor == null || _woActive || _rpActive || _ovHandMode != 0 || _state != HarvestState.Vein || p.Spots.Count == 0)
		{
			return false;
		}
		if (p.ExitBy != null && p.ExitBy != this && p.ExitBy._vp == p && OvActive().Contains(p.ExitBy))
		{
			return false;   // another companion is cutting one
		}
		Vector3 me = base.transform.position;
		OvSpot sp = OvNearest(enemy);
		if (sp == null || Flat(sp.P, me) > OvWayNear)
		{
			sp = OvNearest(me);
		}
		if (sp == null)
		{
			return false;
		}
		foreach (Vector3 t in p.ExitAt)
		{
			if (Flat(t, sp.P) <= OvWayNear)
			{
				return false;
			}
		}
		OvDigOff();
		OvPartRelease();
		OvRelease();
		OvWayOutHere(sp, $"In a fight, stuck {enemy.y - me.y:F1} m below the enemy with no way out of the trench", "a way out to the enemy");
		_woCombat = true;
		return true;
	}

	// ---------- V5: a thing handed to the Wood / Stone flow ----------
	// John: no hit on it in 10 s - it is left; once it has been hit, 30 s without a hit (only to catch it stuck). Left =
	// the flow ends (ResetToIdle) and OvResume keeps it out of the picks and skips the spot this lap, as for a bush.
	private const float OvHandFirstHit = 10f;

	private const float OvHandNextHit = 30f;

	internal bool OvHandClock()
	{
		if (_ovHandMode == 0 || _ovHand == null || !_ovHand.activeInHierarchy || (_state != HarvestState.Moving && _state != HarvestState.Attacking) || _target != _ovHand)
		{
			return false;
		}
		if (OvHandLedge())
		{
			return true;   // Y6d
		}
		// AA4: the clock counts only while it is in range swinging at the thing (and stepping in again after a swing, the
		// re-approach, so swing / step / swing can't run on for ever) - not on the walk to it (that has its own 10 s), not
		// catching its breath, not in a fight (this isn't called in one); a hit starts it again
		bool hit = _lastHitAt >= _hdSince;
		if (hit && _lastHitAt > _hdHitSeen)
		{
			_hdHitSeen = _lastHitAt;
			_hdClock = 0f;
		}
		if ((_state == HarvestState.Attacking || (_state == HarvestState.Moving && _reapproach)) && !_catchingBreath && !(_ai != null && _ai.IsInCombat))
		{
			_hdClock += Time.deltaTime;
		}
		float idle = _hdClock;
		float limit = hit ? OvHandNextHit : OvHandFirstHit;
		if (idle < limit)
		{
			return false;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"No hit on \"{_ovHand.name}\" in {limit:F0} s of swinging at it{(hit ? " since the last one" : "")} - leaving it");
		ResetToIdle();
		return true;
	}

	// ---------- W1b / V2: the game dug where this companion's swing landed (SpawnOnHitTerrain, never blocked) ----------
	internal void NotifyDig(Vector3 hitPoint)
	{
		_dgDugAt = Time.time;
		_dgDugPt = hitPoint;
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
		string what = _ovOut ? "getting out of the trench (" + _ovOutWhy + ")" : _rpActive ? (_rpSaved != null ? "walking up a saved ramp" : "cutting a ramp toward " + MHR_Mover.Fmt(_rpGoal)) : _dgActive ? (_dgBushOn ? (_dgWalking ? "walking to a bush at " : "cutting a bush at ") : _dgWalking ? "walking to dirt at " : "digging dirt at ") + MHR_Mover.Fmt(_dgPoint) : _ovHandMode != 0 ? "clearing \"" + (_ovHand != null ? _ovHand.name : "?") + "\" from the trench" : _vp != null && _vp.Done ? "standing by (the trench is done)" : "choosing the next spot";
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
