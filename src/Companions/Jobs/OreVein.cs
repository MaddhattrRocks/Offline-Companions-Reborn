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

		public float ClosedAt = -1f;   // I1b: when it was closed (a pit map change within 2 m after it reopens it)

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

		// H4: the pit map's check that it is a way out (0 = not checked yet / to check again, 1 = a path, 2 = none), when
		// it was checked, and the squares of that path (a change on any of them = checked again). Kept in the shared plan
		public int PathState;

		public float CheckedAt;

		public readonly List<int> Squares = new List<int>();

		public string PathWhy;

		public int Misses;        // Z3 (a): walks that couldn't reach its bottom this session (2 = dropped)
	}

	private sealed class OvPlan
	{
		public readonly List<MineRock5> Rocks = new List<MineRock5>();

		// D2: a silver vein (its deposit drops silver ore): 0 = not looked yet, 1 = silver, 2 = not
		public int SilverState;

		public int CopperState;   // CU1: 0 = not read yet, 1 = copper, 2 = not

		public readonly List<OvSpot> Spots = new List<OvSpot>();

		public readonly List<Vector2> Poly = new List<Vector2>();

		public readonly List<OvRamp> Ramps = new List<OvRamp>();

		public readonly Dictionary<HarvestController, float> Workers = new Dictionary<HarvestController, float>();

		// G6: the spot each thing was handed over for (a thing it couldn't clear skips only that spot)
		public readonly Dictionary<int, OvSpot> HandFor = new Dictionary<int, OvSpot>();

		// X2 (John, 2026-11-20: a burial chamber beside the vein can't be damaged - the trench goes beside it, through the
		// vein, as close to it as possible, never round it): the game boxes of the indestructible world objects found in
		// the trench's way (X1), each once per vein; ring spots inside one (body radius out) move toward the vein's centre
		public readonly List<Bounds> Blockers = new List<Bounds>();

		public readonly List<string> BlockerNames = new List<string>();
		public readonly Dictionary<int, float> PitCool = new Dictionary<int, float>();   // FF1: a thing someone couldn't clear: not taken again until then (60 s)

		// FF1: the vein's one pit scan (what is inside the trench's outer walls), its mode / type for the Wood / Stone flow
		public readonly List<GameObject> PitThings = new List<GameObject>();

		public readonly List<int> PitModes = new List<int>();

		public readonly List<string> PitTypes = new List<string>();

		public float PitScanAt;

		public readonly HashSet<int> PitAxeNo = new HashSet<int>();    // GG1: a thing that drops nothing known whose axe hits did nothing - the pickaxe next

		public readonly HashSet<int> PitIgnore = new HashSet<int>();   // GG1: neither the axe nor the pickaxe hurt it - ignored on this vein

		// H2: deposits of this vein's ore the pit map met that aren't in the plan (tested for the join every 2 s), and the
		// ones tested already (each once)
		public readonly HashSet<MineRock5> Found = new HashSet<MineRock5>();

		public readonly HashSet<int> FoundTried = new HashSet<int>();

		public float FoundAt;

		// P1 (John, 2026-11-19 19:47Z: "The pit should be measured as anything inside the trench including the trench. Its a
		// literal giant hole in the ground. It should not be measured from the ore vein"): the pit's ground, 1 m cells -
		// inside the ring, the trench spots, and every cell joined to them that the game's terrain record shows dug 0.5 m or
		// more below its original height (out to where the dug ground ends: the outer wall, at most 20 m past the ring)
		public readonly HashSet<long> PitCells = new HashSet<long>();

		public float PitX0, PitX1, PitZ0, PitZ1;

		public readonly HashSet<int> ScanSaid = new HashSet<int>();   // P2: things the pit scan has logged once

		public int PitCellsSaid = -1;   // P1: the pit's size at the last log line (logged when it changes)

		// FF3 / Q1: the vein's one ore dump, on the trench floor (set by the first companion that needs it)
		public bool DumpHas;

		public Vector3 Dump;

		public Vector3 Center;

		public float Goal;

		public int Seen;

		public int Lap = 1;

		public bool RingFixed;

		public bool Done;

		public bool DoneSaid;

		public bool DoneChecked;   // I1: a "done" read from the save (or set at a lap end) was checked against the ground

		public float ClosedChkAt;  // I1b: the next look at the closed spots (every 2 s, as H2)



		// A1: each companion's own stretch of the ring (first spot, how many spots), for the lap / spot count / workers in SKey
		public readonly Dictionary<HarvestController, int> SFrom = new Dictionary<HarvestController, int>();

		public readonly Dictionary<HarvestController, int> SLen = new Dictionary<HarvestController, int>();

		public string SKey;

		// E2: where a way out before "done" was tried (up to 3, 5 m apart), and who is trying one now
		public readonly List<Vector3> ExitAt = new List<Vector3>();

		public readonly List<float> ExitTime = new List<float>();   // K5: when each try in ExitAt was made (60 s each)

		public HarvestController ExitBy;

		public float LastWork;

		public float GrowAt;

		public string Name;

		public Bounds Box;

		public OvMap Map;   // R0: the vein's pit map (OreMap.cs) - data only, nothing reads it yet
	}

	private const float OvRingOut = 1.0f;          // C2.2: the ring 1 m outside the seen parts

	private const float OvSpacing = 1.0f;          // C2.2: a spot every 1 m

	private const float OvPitScanTrench = 1f;     // FF1: the vein's pit scan every 1 s while the trench is dug ...

	private const float OvPitScanAfter = 10f;     // ... every 10 s after

	private const float OvPitCool = 60f;          // FF1: a thing someone couldn't clear waits 60 s

	private const float OvJoin = 4.0f;             // C2.1: deposits within 4 m join

	private const float OvGrowEvery = 2.0f;        // C2.3

	private const float OvKeep = 0.5f;             // C2.3: work kept for a new spot within 0.5 m of an old one

	private const float OvFloorMargin = 0.15f;     // C2.5

	private const float OvWaterFloor = 29.5f;      // C2.5

	private const float OvBaseNear = 2f;           // R3b.1: the vein parts whose box comes within 2 m (flat) of a spot set its depth

	private const float OvLapStep = 2.0f;          // C2.7


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
	private const float OvRampSameY = 0.7f;        // G3 (John 11-22 OK): ends this close in height (and 5 m flat) join a saved ramp

	private const float OvThingAbove = 2f;        // V4: a thing more than 2 m above its feet is on the wall top

	private const float OvWayMore = 2f;           // R3: a way out's goal is (the height to climb) + 2 m out

	private const float OvWayNear = 15f;          // R4: a way out within 15 m (flat) of where it works is near enough

	private const float OvRampReach = 3f;          // G2: the way up only for a spot within 3 m (flat): the wall in front

	private const float OvRampBadNear = 2f;        // G1: no new way up within 2 m of where one met the dig limit, this lap

	private const float OvOutDeep = 1.5f;          // C6: 1.5 m under the ground outside the ring: in the trench

	private const float OvOutSide = 3f;            // C6: 3 m outside the ring


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

	private bool _ovPitOpen;   // F1: this vein's pit is open for its stone and wood piles (from the pop)

	// 1.5.98 F1 (log cdfe000a: after the pop every trip ended the cleanup - the way back needed the rock, gone with the pop):
	// a trip off the pit (ore to a chest, too heavy) during the cleanup keeps the plan here, and it goes back to the pit's
	// list once the trip is over
	private OvPlan _ovBack;

	private bool _ovTrip;   // F1: the OvLeaveOut being started is a trip (the chest trip, too heavy / bag full)

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

	// DD8: no working pickaxe on a vein - axe work there first (OvAxeTick); done = nothing left for the axe
	private bool _ovAxeDone;

	private bool _ovPickOk = true;

	private bool _ovNoPickSaid;   // L6: "no working pickaxe" said once

	private float _ovPickAt;

	// DD8: on a vein with axe work left (MHR_ToolKeeper lets it stay instead of following)
	internal bool OvAxeWork => _vFor != null && !_ovAxeDone && !_ovOut;

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

	// F7 (a): the ground at its feet and 4 points at its body radius when the swing started (where it stood then)
	private readonly float[] _dgFeetG = new float[5];

	private Vector3 _dgFeetAt;

	private int _dgNoProg;

	private bool _dgLastProg;

	private bool _dgThingChecked;

	// J1: the spot's own clock (work time on it in this lap without the ground going down)



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

	private float _dgBushHp;      // GG1: the bush's health at the last hit that lowered it

	private int _dgBushNoDmg;     // GG1: axe hits in a row that didn't lower it

	// H1: what its swings hit (HarvestHitRegister -> OnHitRegistered)
	private GameObject _ovThingHitGo;

	private float _ovThingHitAt = -100f;

	private float _ovOreHitAt = -100f;

	// E2 / E3: walking along the trench floor to where it tries a way out (a saved ramp's bottom, or a new place)
	private bool _woActive;
	private OvRamp _woFallback;   // U3: the saved way out used if a new one cut toward where it is going fails

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

	private bool _dgDirt;         // GG2: a dirt point of the vein's dirt list - only the ground there going down counts

	// 1.5.98 R2: a clearing point at the game's 8 m limit, more than a step above its feet (not "done"); and the part
	// itself being mined from where it stands (R2a)
	private bool _dgLimit;

	private bool _dgPart;

	private bool _dgDamage;      // M3: a swing of this dig lowered the ground or damaged a rock (reset by OvDigBegin)
	private float _rpFell;       // Y2: a fall of more than a step noted while a way up is active (reset by OvRampBegin)

	private int _dgPartMiss;   // K3: swings at a part that didn't hit it (digging toward it included), 20 at most

	private const int OvPartMissMax = 20;

	// V2: when and where the game last dug for one of its swings; the vein whose pickaxe sweep was logged
	private float _dgDugAt = -100f;

	private Vector3 _dgDugPt;

	private float _dgDugBefore;   // Q1: the ground at the dig's point before the game dug it (NotifyDig)

	private OvPlan _aimLogVp;

	// V5: when the thing handed to the Wood / Stone flow was handed over
	private float _hdSince;

	private float _pitAt;         // FF1: the next look at the pit list between swings

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

	private bool _rpThingUp;      // K8: the way up toward this thing came after its base dig (if it gives up: 60 s wait)

	// Y6d: the way up toward a thing is over or dropped: its claim goes
	private void OvRampThingDrop()
	{
		_rpThing = null;
		_rpThingUp = false;
		if (_rpThingClaim != 0 && _rpThingClaim != _claimedTargetId)
		{
			s_claimedTargets.Remove(_rpThingClaim);
		}
		_rpThingClaim = 0;
	}

	private bool _dgStrike;       // Z1: a swing at what the way up is against (the rock, not the ground): no floor check

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
	private readonly HashSet<int> _rpMet = new HashSet<int>();   // V4: the things this way up's swings met (handed over once)

	private int _rpBadLap = -1;

	// part 1 G3 (kept): more than 20 m outside the vein while the vein work moves it - back to it
	private const float LeashOut = 20f;

	private const float LeashNear = 10f;


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

	// F1: on the pit - the rock is there, or the rock is gone and its plan is kept for the cleanup (the pit is open)
	internal bool OvOnPit => _vFor != null || (_vp != null && _ovPitOpen);

	// T1: the vein it is on has popped (its pit is open, or the parts fell in the game's support drop)
	internal bool OvPilesOpen => _vp != null && (_ovPitOpen || OvSawPop());

	// ---------- the floor (C2.5) ----------
	// the lowest the game lets the ground at p go: 8 m under its first height (TerrainComp keeps the change within 8 m)
	private static float TFloorAt(Vector3 p)
	{
		return TFloorFrom(p, GroundAt(p));
	}

	// R0.1: the same, with the ground height at p already read (the pit map reads it once per square)
	private static float TFloorFrom(Vector3 p, float g)
	{
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

	// ---------- F3 (John, 2026-11-19: record the vein's progress between sessions) ----------
	// Kept in the vein's own saved game data (each deposit's ZDO, so it goes when the vein does): the trench done, its lap,
	// and the plan's ramps (the ways up and out that were cut - bottom, top, the height outside it needed). Written when
	// one of them changes; read when a plan is made for the vein (after a load, or an hour after everyone left it).
	private const string OvKeyDone = "MHR_OvDone";

	private const string OvKeyLap = "MHR_OvLap";

	private const string OvKeyWays = "MHR_OvWays";

	private static void OvSave(OvPlan p)
	{
		if (p == null)
		{
			return;
		}
		System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
		StringBuilder sb = new StringBuilder();
		foreach (OvRamp r in p.Ramps)
		{
			if (sb.Length > 0)
			{
				sb.Append(';');
			}
			float[] v = { r.Bottom.x, r.Bottom.y, r.Bottom.z, r.Top.x, r.Top.y, r.Top.z, r.Need };
			for (int i = 0; i < v.Length; i++)
			{
				if (i > 0)
				{
					sb.Append(',');
				}
				sb.Append(v[i].ToString("F2", ic));
			}
		}
		string ways = sb.ToString();
		foreach (MineRock5 rock in p.Rocks)
		{
			ZNetView nv = rock != null ? rock.GetComponent<ZNetView>() : null;
			ZDO z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
			if (z == null)
			{
				continue;
			}
			z.Set(OvKeyDone, p.Done ? 1 : 0);
			z.Set(OvKeyLap, p.Lap);
			z.Set(OvKeyWays, ways);
		}
	}

	private void OvLoad(OvPlan p)
	{
		System.Globalization.CultureInfo ic = System.Globalization.CultureInfo.InvariantCulture;
		// H1 (John 11-22: "yes"): a deposit in the plan with nothing saved (a vein joined since the save) = the trench round
		// it isn't dug: the saved ways out are loaded, the trench is not done and the laps start at 1 (the ring may grow)
		bool unsaved = false;
		foreach (MineRock5 rock in p.Rocks)
		{
			ZNetView nv0 = rock != null ? rock.GetComponent<ZNetView>() : null;
			ZDO z0 = nv0 != null && nv0.IsValid() ? nv0.GetZDO() : null;
			if (z0 != null && z0.GetInt(OvKeyLap, 0) <= 0)
			{
				unsaved = true;
				break;
			}
		}
		foreach (MineRock5 rock in p.Rocks)
		{
			ZNetView nv = rock != null ? rock.GetComponent<ZNetView>() : null;
			ZDO z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
			if (z == null)
			{
				continue;
			}
			int lap = z.GetInt(OvKeyLap, 0);
			if (lap <= 0)
			{
				continue;   // nothing saved on this deposit
			}
			p.Lap = Mathf.Max(lap, 1);   // I1: no lap cap
			p.Done = z.GetInt(OvKeyDone, 0) == 1;
			p.DoneChecked = false;       // I1: checked against the ground on the first vein tick
			p.RingFixed = p.RingFixed || p.Done || p.Lap > 1;
			p.Ramps.Clear();
			foreach (string w in z.GetString(OvKeyWays, "").Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
			{
				string[] f = w.Split(',');
				if (f.Length != 7)
				{
					continue;
				}
				float[] v = new float[7];
				bool ok = true;
				for (int i = 0; i < 7 && ok; i++)
				{
					ok = float.TryParse(f[i], System.Globalization.NumberStyles.Float, ic, out v[i]);
				}
				if (ok)
				{
					p.Ramps.Add(new OvRamp { Bottom = new Vector3(v[0], v[1], v[2]), Top = new Vector3(v[3], v[4], v[5]), Need = v[6] });
				}
			}
			if (unsaved)
			{
				p.Done = false;
				p.Lap = 1;
				p.RingFixed = false;
				if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{p.Name}\" has a deposit with nothing saved (joined since the save) - the trench round it is dug: lap 1 again (spots already down count as done), {p.Ramps.Count} way(s) up / out kept (H1)");
				return;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"The vein \"{p.Name}\" carries on from its saved progress: {(p.Done ? "the trench is done" : $"trench lap {p.Lap}")}, {p.Ramps.Count} way(s) up / out cut before");
			return;
		}
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
		_ovBack = null;   // F1: a new vein - no way back to the last pit
		_ovPitOpen = false;
		MHR_GatherRules.ClosePit(this);   // F1: the last vein's pit is closed for stone piles
		_ovAxeDone = false;
		_ovPickOk = true;
		_ovPickAt = 0f;
		_hvDrops.Clear();
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
		OvDigOff();
		_rpActive = false;
		_ai?.StopMoving();
		MHR_Mover.Release(_ai);
		_ai?.SetFollowTarget(null);
		EquipTool(pick);
		_state = HarvestState.Vein;
		p.Workers[this] = Time.time;
		p.LastWork = Time.time;
		if (CompanionsPlugin.LogNormal) LogInfo(OvIsCopper(p) ? $"On the vein \"{p.Name}\" ({p.Rocks.Count} deposit(s), {p.Spots.Count} trench spots mapped) - copper: no trench (CU2)" : $"On the vein \"{p.Name}\" ({p.Rocks.Count} deposit(s), {p.Spots.Count} trench spots, lap {p.Lap}{(p.Done ? ", the trench is done" : "")}) - the trench round it first");
	}

	// C3.2: the thing handed to the Wood / Stone flow is over (gone, or that flow gave up on it): back to the vein
	private void OvResume()
	{
		if (_vp == null || _vRock == null || _vFor == null)
		{
			// K1: the vein is gone - its pit's loose ore, stone and wood first (as OvTick), from the saved plan
			if (_vp != null)
			{
				_state = HarvestState.Vein;
				return;
			}
			OvClearGone();
			OvLeave("the vein is gone", quiet: true);
			MHR_GatherRules.AfterVein(this);   // Q2: the ore after the vein (its own, then the ore dump's)
			return;
		}
		if (_ovHandMode != 0)
		{
			GameObject thing = _ovHand;
			OvSpot q = _ovHandSpot;
			if (thing != null && thing.activeInHierarchy && _hdNoCool)
			{
				// GG1: left for the pickaxe (or ignored on this vein) - no cooldown, no skip
				_hdNoCool = false;
			}
			else if (thing != null && thing.activeInHierarchy && OvThingWayOut(thing, _ovHandMode, _targetType, q, "the walk to it gave up"))
			{
				return;   // Z2: in the trench - a way out first, then the thing again
			}
			else if (thing != null && thing.activeInHierarchy)
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
		// F1: a trip off the pit during the cleanup (the rock gone, the pit open) - the plan is kept for the way back
		OvPlan back = _ovTrip && _vFor == null && _vp != null && _ovPitOpen ? _vp : null;
		_ovTrip = false;
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
		_hvDrops.Clear();
		// F1 (John: "once they make piles from the existing stone then they can move on"): the after-pop work is over -
		// the pit is closed for stone and wood piles; what it mines later is piled or tossed where it is
		if (_ovPitOpen)
		{
			_ovPitOpen = false;
			MHR_GatherRules.ClosePit(this);
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
		_ovBack = back;
		if (back != null && CompanionsPlugin.LogNormal) LogInfo($"A trip off the pit of \"{back.Name}\" ({why}) - back to the pit's list after it");
	}

	// F1: the trip off the pit is over (no deposit, pile, toss or ore trip going on) - back to the pit's list
	internal void OvBackToPit()
	{
		OvPlan p = _ovBack;
		_ovBack = null;
		if (p == null || !s_ovPlans.Contains(p))
		{
			return;
		}
		_vp = p;
		_vFor = null;
		_vRock = null;
		_ovPitOpen = false;
		p.Workers[this] = Time.time;
		p.LastWork = Time.time;
		_ai?.SetFollowTarget(null);
		_state = HarvestState.Vein;
		if (CompanionsPlugin.LogNormal) LogInfo($"Back to the pit of \"{p.Name}\" after its trip - the pit's list again");
	}

	// R1 (John 11-22 21:05Z Q3 yes, 21:25Z): when it left each kept pit's cleanup (it joins that pit again only after a change)
	private readonly Dictionary<OvPlan, float> _ovJoinLeft = new Dictionary<OvPlan, float>();

	private const float OvJoinNear = 20f;   // R1 (Q3: 20 m): within 20 m (flat) of the pit's nearest trench spot

	// R1: in the Ore job, idle, on no vein and on no trip (the caller's checks), a kept plan within 20 m whose rock is gone
	// and whose pit's item list holds an item it would take (S2): it joins that pit's cleanup by the back-to-pit path
	// (OvBackToPit). It joins a pit it left only after that pit's item list changed since. True = it joined.
	internal bool OvJoinPit()
	{
		Vector3 me = base.transform.position;
		OvPlan best = null;
		float bd = float.MaxValue;
		foreach (OvPlan p in s_ovPlans)
		{
			if (p == null || p.Spots.Count == 0 || !s_ovClear.TryGetValue(p, out OvClear c))
			{
				continue;
			}
			bool live = false;
			foreach (MineRock5 r in p.Rocks)
			{
				if (r != null && r.gameObject.activeInHierarchy)
				{
					live = true;
					break;
				}
			}
			if (live)
			{
				continue;   // the rock is there: the scan finds it and joins the plan as before
			}
			// S2 (log ebd571a0: 362 joins, each with nothing it could take): only items the cleanup would take - still there,
			// no other companion's, not set aside, not stone on the stone dump (the dump may stay, Q1)
			int items = 0;
			foreach (OvLoose l in c.Loose.Values)
			{
				if (!OvLooseHere(l) || l.AsideAt >= 0f || (l.Holder != null && l.Holder != this && l.Holder._lsItem == l))
				{
					continue;
				}
				if (l.Name == "Stone" && OvAtStoneDump(c, l.D.transform.position))
				{
					continue;
				}
				items++;
			}
			if (items == 0)
			{
				continue;
			}
			// S2: a pit it left is joined again only after its item list changed (the pit map changes all the time)
			if (_ovJoinLeft.TryGetValue(p, out float left) && c.LooseChangedAt <= left)
			{
				continue;
			}
			float d = float.MaxValue;
			foreach (OvSpot sp in p.Spots)
			{
				d = Mathf.Min(d, Flat(me, sp.P));
			}
			if (d <= OvJoinNear && d < bd)
			{
				bd = d;
				best = p;
			}
		}
		if (best == null)
		{
			return false;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Joining the cleanup of the pit of \"{best.Name}\" ({bd:F0} m away): its rock is gone and items are still on its list (R1)");
		_ovBack = best;
		OvBackToPit();
		return true;
	}

	// build 2: lets go of the part it holds or mines
	private void OvPartRelease()
	{
		OvPartUnclaim(_rpPart);   // F9
		OvPartUnclaim(_clFor);
		_rpPart = null;
		OvLooseOff();   // F2: and the loose item it had
		OvClOff();
		OvDrOff();   // GG2: and the dirt point it had
	}

	// C6: leaving the vein - out of the trench first when it's in it, toward dest
	private void OvLeaveOut(Vector3 dest, string why)
	{
		if ((_vFor == null && _vp == null) || _ovOut)
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
			OvGetOut(goal, why, dest);
			return;
		}
		OvLeave(why);
	}

	// ExitGatherMode: the job changed (or the gather job ended) while on the vein
	private void OvJobOff()
	{
		_stuckInHole = false;
		_ovBack = null;   // F1: the job changed - no way back to the pit
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
			OvGetOut(goal, "the job changed", dest);
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
		// Y3 (John 11-25, log e891dcd2: the job change 26 m down the mountain read "in the trench"): off the pit area is not
		// in the trench - the job change and leaving go ahead as normal
		if (!OvInPitPt(me))
		{
			return false;
		}
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

	// R3b.1: this companion's height (the depth under the vein a spot goes)
	private float OvBodyH()
	{
		return _character != null ? _character.GetHeight() : 1.8f;
	}

	// D2 (John, 2026-11-21: "when mining silver I think 1-1.5m under is probably enough"; silver veins only): how far under
	// the vein each trench spot goes - 1.5 m on a silver vein, one companion height on any other
	private const float OvSilverUnder = 1.5f;

	private float OvUnder(OvPlan p)
	{
		return OvIsSilver(p) ? OvSilverUnder : OvBodyH();
	}

	// D1/D2: the vein's deposit drops silver ore (by its drops, never its name); looked at once per plan
	private static bool OvIsSilver(OvPlan p)
	{
		if (p == null)
		{
			return false;
		}
		if (p.SilverState == 0)
		{
			MineRock5 r = p.Rocks.Count > 0 ? p.Rocks[0] : null;
			if (r == null)
			{
				return false;
			}
			p.SilverState = MHR_Drops.DropsItem(r.gameObject, "SilverOre") ? 1 : 2;
		}
		return p.SilverState == 1;
	}

	// CU1 (John 11-25: "For Copper I want to remove the trench and popping of the ore vein. Just mine the ore directly"):
	// a copper deposit - the plan's first rock drops copper ore (read once, as for silver)
	private static bool OvIsCopper(OvPlan p)
	{
		if (p == null)
		{
			return false;
		}
		if (p.CopperState == 0)
		{
			MineRock5 r = p.Rocks.Count > 0 ? p.Rocks[0] : null;
			if (r == null)
			{
				return false;
			}
			p.CopperState = MHR_Drops.DropsItem(r.gameObject, "CopperOre") ? 1 : 2;
		}
		return p.CopperState == 1;
	}

	// R3b.1 (John, 2026-11-21: "no more than a NPC height under the ore vein"): the vein's base beside a spot - the lowest
	// bottom of this vein's live part boxes (the same boxes the plan's ring is made from) that come within 2 m (flat) of
	// it; none = the whole vein's lowest part
	private float OvSpotBase(OvPlan p, Vector3 pos)
	{
		float low = float.MaxValue;
		foreach (MineRock5 rock in p.Rocks)
		{
			if (rock == null || !MHR_Vein.Areas(rock, s_ovJoinAreas, out bool haveBounds))
			{
				continue;
			}
			Vector3 origin = rock.transform.position;
			foreach (MHR_Vein.Area ha in s_ovJoinAreas)
			{
				if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
				{
					continue;
				}
				Vector3 c = haveBounds ? origin + ha.BoundPos : ha.Collider.bounds.center;
				Vector3 h = haveBounds ? ha.BoundSize : ha.Collider.bounds.size * 0.5f;
				float dx = Mathf.Max(0f, Mathf.Abs(pos.x - c.x) - h.x), dz = Mathf.Max(0f, Mathf.Abs(pos.z - c.z) - h.z);
				if (dx * dx + dz * dz <= OvBaseNear * OvBaseNear)
				{
					low = Mathf.Min(low, c.y - h.y);
				}
			}
		}
		return low < float.MaxValue ? low : p.Goal + OvUnder(p);
	}

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
		// C2.1 / CC2 / DD1: the vein, and deposits of the same ore with a part within 4 m (3D, part box to part box) of a part of it (and of those)
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
					// DD1 (test 13: plain stone boulders joined): only the same ore - the game's own name for the deposit
					// (MineRock5.m_name, e.g. the copper deposit's) must match the vein's
					if (m == null || p.Rocks.Contains(m) || string.IsNullOrEmpty(rock.m_name) || m.m_name != rock.m_name || GetHarvestCandidateWithType(c, 3, out _) == null || InDungeonAt(m.transform.position))
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
		OvLoad(p);   // F3: the vein's saved progress (a plan made after a load)
		if (CompanionsPlugin.LogNormal) LogInfo($"Plan for the vein \"{p.Name}\": {p.Rocks.Count} deposit(s), {p.Seen} part(s) seen, a ring of {p.Spots.Count} spots 1 m out from them; each spot down to {(OvIsSilver(p) ? "1.5 m (a silver vein)" : "one companion height")} ({OvUnder(p):F1} m) under the vein beside it (never under the floor; {p.Goal:F1} where no part is within {OvBaseNear:F0} m); lap 1 takes each spot down 2 m at most{joined}");
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
				// U5 (John 2026-11-15: the trench goes round the outside edge of the ENTIRE vein; test 25: rebuilt on a reload
				// from 34 uncovered parts of 103, buried east parts sat under the trench line): every live part, buried too -
				// the same ring after every reload
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
		p.Goal = lowest - OvUnder(p);   // R3b.1 (D2: 1.5 m on silver): the fallback - one companion height under the whole vein's lowest part
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
			if (OvBesideBlocker(p, pos, out Vector3 bpos))
			{
				pos = bpos;   // X2
				s.P = pos;
			}
			s.Floor = OvFloor(pos);
			s.Target = Mathf.Max(s.Floor, OvSpotBase(p, pos) - OvUnder(p));   // R3b.1 (D2: 1.5 m on silver)
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
			s.ClosedAt = Time.time;   // I1b
			return;
		}
		if (!PrivateArea.CheckAccess(s.P, 0f, flash: false) || Location.IsInsideNoBuildLocation(s.P))
		{
			s.Closed = true;
			s.ClosedWhy = "a ward or a no-build area";
			s.ClosedAt = Time.time;   // I1b
			return;
		}
		foreach (Collider c in Physics.OverlapSphere(s.P + Vector3.up * 0.5f, 1.0f, LayerMask.GetMask("piece", "piece_nonsolid"), QueryTriggerInteraction.Ignore))
		{
			if (c != null && c.GetComponentInParent<Piece>() != null)
			{
				s.Closed = true;
				s.ClosedWhy = "a building";
				s.ClosedAt = Time.time;   // I1b
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

	// H2: a deposit of this vein's own ore (the game's MineRock5.m_name, DD1's test) that isn't in this plan
	private static bool OvSameOre(OvPlan p, MineRock5 m)
	{
		if (p == null || m == null || p.Rocks.Contains(m))
		{
			return false;
		}
		string name = null;
		foreach (MineRock5 r in p.Rocks)
		{
			if (r != null && !string.IsNullOrEmpty(r.m_name))
			{
				name = r.m_name;
				break;
			}
		}
		return name != null && m.m_name == name;
	}

	// H2 (John 11-22 Q-H2): an uncracked deposit of this vein's ore - the game's Destructible whose m_spawnWhenDestroyed is a
	// MineRock5 with the vein's own name (the swing that destroys it is what cracks it)
	private static bool OvUncracked(OvPlan p, GameObject go)
	{
		Destructible d = go != null ? go.GetComponent<Destructible>() : null;
		MineRock5 m = d != null && d.m_spawnWhenDestroyed != null ? d.m_spawnWhenDestroyed.GetComponent<MineRock5>() : null;
		if (m == null || p == null)
		{
			return false;
		}
		foreach (MineRock5 r in p.Rocks)
		{
			if (r != null && !string.IsNullOrEmpty(r.m_name))
			{
				return m.m_name == r.m_name;
			}
		}
		return false;
	}

	// H2 (John 11-22: "they should cut a trench around that part as well and incorporate it into the existing plan"): every
	// 2 s (OvGrowEvery), each deposit the pit map noted is tested once by plan creation's join rules (DD1 the same ore, a
	// harvest candidate, not in a dungeon, a part within 4 m (OvJoin, part box to part box) of the vein's). Joined: the map
	// reads its squares again (it is the vein now), the ring is made again keeping the work done, and the trench is dug
	// round it (not done, lap 1). Not joined: another ore vein - left alone (the map keeps it as a thing it can't damage)
	private void OvFoundJoin(OvPlan p)
	{
		if (p.Found.Count == 0 || Time.time < p.FoundAt)
		{
			return;
		}
		p.FoundAt = Time.time + OvGrowEvery;
		List<Bounds> have = new List<Bounds>();
		List<Bounds> theirs = new List<Bounds>();
		foreach (MineRock5 r in p.Rocks)
		{
			OvPartBoxes(r, theirs);
			have.AddRange(theirs);
		}
		List<MineRock5> joined = new List<MineRock5>();
		foreach (MineRock5 m in new List<MineRock5>(p.Found))
		{
			if (m == null || !p.FoundTried.Add(m.GetInstanceID()))
			{
				continue;
			}
			bool otherPlan = false;
			foreach (OvPlan q in s_ovPlans)
			{
				if (q != p && q.Rocks.Contains(m))
				{
					otherPlan = true;
					break;
				}
			}
			Collider any = m.GetComponentInChildren<Collider>();
			string why = otherPlan ? "it is another vein's" : any == null || GetHarvestCandidateWithType(any, 3, out _) == null ? "it isn't an ore target" : InDungeonAt(m.transform.position) ? "it is in a dungeon" : null;
			float gap = float.MaxValue;
			if (why == null)
			{
				OvPartBoxes(m, theirs);
				foreach (Bounds tb in theirs)
				{
					foreach (Bounds hb in have)
					{
						gap = Mathf.Min(gap, OvGap3D(tb, hb));
					}
				}
				if (gap > OvJoin)
				{
					why = $"its nearest part is {(gap == float.MaxValue ? "?" : gap.ToString("F1"))} m from the vein's (more than {OvJoin:F0} m)";
				}
			}
			if (why != null)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"A deposit of the vein's ore at {MHR_Mover.Fmt(m.transform.position)} doesn't join \"{p.Name}\": {why} - another ore vein, never swung at (H2)");
				continue;
			}
			p.Rocks.Add(m);
			have.AddRange(theirs);
			joined.Add(m);
			if (CompanionsPlugin.LogNormal) LogInfo($"A deposit of the vein's ore at {MHR_Mover.Fmt(m.transform.position)} joins \"{p.Name}\" (its nearest part {gap:F1} m from the vein's) - the trench goes round it too (H2)");
		}
		p.Found.Clear();
		if (joined.Count == 0)
		{
			return;
		}
		if (p.Map != null)
		{
			p.Map.KindCache.Clear();   // its colliders are read as the vein now
			foreach (MineRock5 m in joined)
			{
				OvPartBoxes(m, theirs);
				foreach (Bounds b in theirs)
				{
					OvMapDirtyBox(p.Map, b.min.x - OvMapCell, b.min.z - OvMapCell, b.max.x + OvMapCell, b.max.z + OvMapCell, default(Vector3), 0f);
				}
			}
		}
		List<OvSpot> old = new List<OvSpot>(p.Spots);
		int before = old.Count;
		if (!OvRing(p, old))
		{
			p.Spots.Clear();
			p.Spots.AddRange(old);
		}
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h != null)
			{
				h._ovSpot = null;
				h._ovIdx = -1;
				h.OvDrOff();
				h.OvClOff();
			}
		}
		p.Done = false;
		p.DoneSaid = false;
		p.Lap = 1;
		p.RingFixed = false;
		OvSave(p);
		if (CompanionsPlugin.LogNormal) LogInfo($"The ring round \"{p.Name}\" is made again with {joined.Count} more deposit(s): {before} -> {p.Spots.Count} spots (work done kept); the trench is dug round it from lap 1 (H2)");
	}

	// I1 (John 11-22 Q4: "I dont think the trench should ever be fully 'closed'"): any spot not closed and more than 0.3 m
	// above its goal reopens the trench - lap 1 again, each spot above its goal down 2 m at most this lap, the workers'
	// current work dropped (as H2). It ends: a lap where no spot went down 0.3 m is done and doesn't reopen (OvLapEnd).
	private bool OvReopenCheck(OvPlan p, string why)
	{
		int high = 0;
		foreach (OvSpot s in p.Spots)
		{
			if (!s.Closed && GroundAt(s.P) > s.Target + 0.3f)
			{
				high++;
			}
		}
		if (high == 0)
		{
			return false;
		}
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h != null)
			{
				h._ovSpot = null;
				h._ovIdx = -1;
				h.OvDrOff();
				h.OvClOff();
			}
		}
		p.Done = false;
		p.DoneSaid = false;
		p.Lap = 1;
		p.RingFixed = false;
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
		OvSave(p);
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{p.Name}\" is open again ({why}): {high} spot(s) more than 0.3 m above the goal, {open} spot(s) opened - lap 1, each down 2 m at most this lap (I1)");
		return true;
	}

	// I1b (John 11-22 18:06Z: "They should be able to re-examine a location if something changes"): every 2 s, a spot
	// closed for good is opened again when a pit map square within 2 m of it changed after it was closed; the closed
	// checks run again at once (still true = closed again, from now). One above its goal on a done trench reopens it (I1).
	private void OvClosedRecheck(OvPlan p)
	{
		if (Time.time < p.ClosedChkAt)
		{
			return;
		}
		p.ClosedChkAt = Time.time + OvGrowEvery;
		bool reopen = false;
		foreach (OvSpot s in p.Spots)
		{
			if (!s.Closed || s.ClosedAt < 0f || !OvChangedNearPt(s.P, s.ClosedAt))
			{
				continue;
			}
			string was = s.ClosedWhy;
			string chg = OvChgSaid();
			s.Closed = false;
			s.ClosedWhy = null;
			s.ClosedAt = -1f;
			OvClosedCheck(s);
			if (s.Closed)
			{
				if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {s.Idx + 1} looked at again (something within 2 m changed{chg}): still closed - {s.ClosedWhy} (I1b)");
				continue;
			}
			float g = GroundAt(s.P);
			s.Skip = false;
			s.SkipWhy = null;
			s.Holder = null;
			if (g > s.Target + OvDigGain)
			{
				s.Done = false;
				s.DoneWhy = null;
				s.LapStart = g;
				s.OpenLap = p.Lap;
				s.LapDepth = Mathf.Max(s.Target, g - OvLapStep);
			}
			else
			{
				s.Done = true;
				s.DoneWhy = "at the trench goal";
			}
			string left = s.Done ? "at its goal" : (g - s.Target).ToString("F1") + " m above its goal";
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {s.Idx + 1} looked at again (something within 2 m changed{chg}): open again (it was closed: {was}), {left} (I1b)");
			reopen |= p.Done && g > s.Target + 0.3f;
		}
		if (reopen)
		{
			OvReopenCheck(p, "a closed spot opened again");
		}
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
				if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)   // U5: every live part, as OvRing
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
		// I1 (John 11-22 Q-I1a): no lap cap - a lap where no spot went down 0.3 m ends the trench
		if (best < OvLapMinGain)
		{
			p.Done = true;
			p.DoneChecked = true;
			OvSave(p);   // F3
			return;
		}
		p.Lap++;
		OvSave(p);   // F3
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
			p.DoneChecked = true;
			OvSave(p);   // F3
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
		// K5 (the SV1 take-over, log 58064830: Comp stuck below spot 28 kept it): a holder that is stuck on its way to the
		// spot (the mover's stuck ladder, MHR_Mover.Stuck) doesn't hold it - another companion may take it
		// Y4 (John 11-25, log e891dcd2: Gunter, down the mountain, kept spot 17 and the lap could not end): nor one off the
		// pit area
		if (!h.OvHolderWorking(true) || !OvInPitPt(h.transform.position))
		{
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
			if (h == null || h == this || Time.time - w.Value > 5f || h._vp != _vp || h._ovSpot == null || h._ovSpot.Holder != h || !h.OvHolderWorking(true) || !OvInPitPt(h.transform.position))   // Y4
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

	// L1: the plan's nearest deposit that is still there (alive, active) becomes its rock. False = none left
	private bool OvNextRock()
	{
		MineRock5 best = null;
		float bd = float.MaxValue;
		foreach (MineRock5 r in _vp.Rocks)
		{
			if (r == null || !r.gameObject.activeInHierarchy || r == _vRock)
			{
				continue;
			}
			float d = Vector3.Distance(base.transform.position, r.transform.position);
			if (d < bd)
			{
				bd = d;
				best = r;
			}
		}
		if (best == null)
		{
			return false;
		}
		_vRock = best;
		_vFor = best.gameObject;
		_target = _vFor;
		if (CompanionsPlugin.LogNormal) LogInfo($"A deposit of \"{_vp.Name}\" is gone - on to its next deposit at {MHR_Mover.Fmt(best.transform.position)} ({bd:F0} m away), the cleanup after the last one (L1)");
		return true;
	}

	// ---------- C3: the chooser (HarvestState.Vein) ----------
	private void OvTick()
	{
		if (_vp == null || _vRock == null || _vFor == null || !_vFor.activeInHierarchy)
		{
			// L1 (John 11-22 20:11Z, Q1 yes: the next deposit, then one cleanup at the end): its rock is gone but the plan
			// still has a live deposit - the nearest one is its rock now and the plan's work goes on
			if (_vp != null && OvNextRock())
			{
				return;
			}
			// F2: a vein that popped to the last part (the rock is gone): the loose ore, stone and wood in its pit first
			// K1 (log 58064830: all 85 parts fell, the game removed the rock, so _vFor was gone too and this was skipped -
			// 65 items left in the pit): only the saved plan is needed
			if (_vp != null && OvGoneLoose())
			{
				return;
			}
			OvClearGone();
			if (_vp != null)
			{
				_ovJoinLeft[_vp] = Time.time;   // R1/S2: it joins this pit again only after its item list changed
			}
			OvLeave("the vein is gone");
			MHR_GatherRules.AfterVein(this);   // Q2: the ore after the vein (its own, then the ore dump's)
			return;
		}
		OvPlan p = _vp;
		float now = Time.time;
		p.Workers[this] = now;
		p.LastWork = now;
		// K2 (log 58064830: Comp hopped up a 1.5 m bank every time its stamina reached 10, so it sat at 8-10/65): below the
		// existing rest line (swing cost + GatherRestStamina, set by the swing) it stands still, no walking or hopping,
		// until the existing GatherResumeStamina, like a player getting its breath back
		if (_catchingBreath && _stamina != null)
		{
			float max = Mathf.Max(1f, _stamina.MaxStamina);
			if (_stamina.Stamina < (ModConfig.GatherResumeStamina?.Value ?? 0.6f) * max)
			{
				_ai?.StopMoving();
				return;
			}
			_catchingBreath = false;
			if (CompanionsPlugin.LogDetailed) Log($"Got its breath back ({_stamina.Stamina:F0}/{max:F0}) - back to the vein");
		}
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
		// DD8: no working pickaxe - only axe work on this vein (looked at once a second)
		if (now >= _ovPickAt)
		{
			_ovPickAt = now + 1f;
			_ovPickOk = HasWorkingPick();
			if (_ovPickOk)
			{
				_ovNoPickSaid = false;   // L6: said again only after it had a working one
			}
		}
		if (!_ovPickOk)
		{
			OvAxeTick();
			return;
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
		// P2: digging the ground at a thing's base (OvHandLedge) - in the trench or the clearing alike
		if (_drForThing && _drSpot != null)
		{
			OvDrTick(OvC());
			return;
		}
		// N1: a clearing dig point is ticked by the clearing itself (OvClRound), which then sees how it ended
		if (_dgActive && !_dgClear)
		{
			// FF1: between swings of a trench dig, the pit list first
			if (OvPitBetween())
			{
				return;
			}
			OvDigTick();
			return;
		}
		OvGrow(p);
		OvFoundJoin(p);   // H2
		OvClosedRecheck(p);   // I1b
		OvRampChecks(p);  // H4
		// I1 (John 11-22 Q4): a "done" from the save is checked against the ground once
		// CU2 (John 11-25, C6: the ring is mapped, never dug): a copper deposit goes straight to the clearing - no trench, and
		// the saved "done" is never checked against the ground (that would reopen the trench)
		bool copper = OvIsCopper(p);
		if (p.Done && !p.DoneChecked && !copper)
		{
			p.DoneChecked = true;
			OvReopenCheck(p, "the saved trench said done");
		}
		if (p.Done || copper)
		{
			OvDoneTick();
			return;
		}
		// CC3 (test 12): no separate first pass round the ring (J4 removed) - a thing on a spot is cleared when the spot is
		// reached (1. below), one in the pit by the pit pick near it (Y6a / CC4)
		// K5: its spot taken over by another companion while it was stuck on the way (the SV1 take-over): it lets go
		if (_ovSpot != null && _ovSpot.Holder != null && _ovSpot.Holder != this)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {_ovSpot.Idx + 1} was taken over by {_ovSpot.Holder.OvName()} while it was stuck on the way - letting it go");
			_ovSpot = null;
		}
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
				// F11 (c) (John 11-21 23:30Z: no waiting - work on something else; 11-22 04:24Z Q6): no open spot left - the
				// loose items in the pit it can take (the pit's list); none = it stands by
				if (!p.Done && _humanoid != null && !_humanoid.InAttack() && OvLooseTick(OvC(), standBy: false))
				{
					return;
				}
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
			// K5: a companion that isn't working (stuck on its walk, the SV1 rule) doesn't keep the spots near it
			if (h == this || !h.OvHolderWorking(true))
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
		bool near4 = false;
		if (found < 0)
		{
			// A2: its stretch is done - the nearest open spot 4 m from every other companion and its spot; F11 (b): none -
			// the nearest open spot no one has, even within 4 m (the board's 1.2 m still keeps them apart); else it stands by
			float bd = float.MaxValue, bn = float.MaxValue;
			int foundNear = -1;
			for (int i = 0; i < n; i++)
			{
				if (OvOpen(spots[i]) && !OvTaken(spots[i]))
				{
					float d = Flat(me, spots[i].P);
					if (OvApartFromOthers(spots[i].P))
					{
						if (d < bd)
						{
							bd = d;
							found = i;
						}
					}
					else if (d < bn)
					{
						bn = d;
						foundNear = i;
					}
				}
			}
			if (found < 0 && foundNear >= 0)
			{
				found = foundNear;
				near4 = true;
			}
			other = found >= 0;
		}
		if (found < 0)
		{
			if (!_ovStandSaid || _ovStandLap != _vp.Lap)
			{
				_ovStandSaid = true;
				_ovStandLap = _vp.Lap;
				if (CompanionsPlugin.LogNormal) LogInfo($"Its stretch of the trench is done for lap {_vp.Lap} and every open spot is another companion's - the pit's loose items, else standing by until the lap ends");
			}
			return false;
		}
		_ovStandSaid = false;
		OvSpot s = spots[found];
		s.Holder = this;
		_ovSpot = s;
		_ovIdx = found;
		if (CompanionsPlugin.LogDetailed) Log($"Trench spot {found + 1}/{n} (lap {_vp.Lap}) at {MHR_Mover.Fmt(s.P)}, {Flat(me, s.P):F1} m away{(other ? (near4 ? " - its stretch is done, the nearest open spot (within 4 m of another companion, F11)" : " - its stretch is done, the nearest open spot 4 m from the others") : "")}; ground {GroundAt(s.P):F2}, this lap down to {s.LapDepth:F2}");
		return true;
	}

	// C3.2: anything within 2 m of the spot that the Wood job (tree, log, stump, bush) or the Stone job (rock) would take is
	// handed to that job's own flow (a bush: J3, cut where it stands). True = handed over (or the spot skipped for it).
	private bool OvThing(OvSpot q)
	{
		return OvThingAt(q.P, q, canSkip: true) != 0;
	}

	// GG1 (John, 2026-11-19: "everything should base the logic off what is dropped by the object"): the thing a collider
	// belongs to, by what it drops (MHR_Drops) - wood = the Wood job's way with the axe (mode 1), stone / flint = the Stone
	// job's way with the pickaxe (mode 2), anything else = the hit test: the axe first, cut where it stands ("Destructible"
	// with mode 1, the J3 way), then the pickaxe (mode 2) once 3 axe hits didn't lower its health or the game says the axe
	// does nothing to it. Never ore (the Ore job mines it), a rock of several parts (carved through), a character, anything
	// built, or a thing neither tool hurt (ignored on this vein).
	private GameObject OvThingOf(Collider c, out int mode, out string type)
	{
		mode = 0;
		type = null;
		if (c == null || c.GetComponentInParent<Character>() != null || c.GetComponentInParent<MineRock5>() != null)
		{
			return null;
		}
		GameObject go = MHR_Drops.Root(c);
		if (go == null || !go.activeInHierarchy || IsBuilt(go))
		{
			return null;
		}
		if (OvUncracked(_vp, go))
		{
			// H2 (John 11-22: "it will still get the first swing to 'crack' it so it can be properly monitored"): an
			// uncracked deposit of this vein's ore is handed to the Stone job's way - the game's hit cracks it (the
			// Destructible gives way to its MineRock5), which the pit map then notes for the join test
			mode = 2;
			type = "Destructible";
			return go;
		}
		int kind = MHR_Drops.KindOf(go);
		bool rock = go.GetComponent<MineRock>() != null;
		switch (kind)
		{
		case MHR_Drops.Ore:
			return null;
		case MHR_Drops.Wood:
			mode = 1;
			type = go.GetComponent<TreeBase>() != null ? "TreeBase" : go.GetComponent<TreeLog>() != null ? "TreeLog" : rock ? "MineRock" : "Stump";
			return go;
		case MHR_Drops.Stone:
			mode = 2;
			type = rock ? "MineRock" : "Destructible";
			return go;
		}
		int id = go.GetInstanceID();
		if (_vp != null && _vp.PitIgnore.Contains(id))
		{
			return null;
		}
		bool axeNo = (_vp != null && _vp.PitAxeNo.Contains(id)) || !MHR_Drops.ToolHurts(go, axe: true);
		if (!axeNo)
		{
			mode = 1;
			type = "Destructible";
			return go;
		}
		if (!MHR_Drops.ToolHurts(go, axe: false))
		{
			OvPitIgnore(go, "the game's own damage rules say neither the axe nor the pickaxe hurts it");
			return null;
		}
		mode = 2;
		type = rock ? "MineRock" : "Destructible";
		return go;
	}

	// GG1: neither tool hurt it - left for the rest of this vein (John: "If both do no dmg then ignore it")
	private void OvPitIgnore(GameObject go, string why)
	{
		if (_vp == null || go == null || !_vp.PitIgnore.Add(go.GetInstanceID()))
		{
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Ignoring \"{MHR_Drops.Tooltip(go)}\" ({go.name}) at {MHR_Mover.Fmt(go.transform.position)} on this vein: {why}");
	}

	// GG1: the axe did nothing to a thing that drops nothing known - the pickaxe next (no cooldown)
	private void OvPitAxeNo(GameObject go, string why)
	{
		if (_vp == null || go == null || !_vp.PitAxeNo.Add(go.GetInstanceID()))
		{
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"\"{MHR_Drops.Tooltip(go)}\" ({go.name}): {why} - trying the pickaxe on it");
	}

	private static readonly HashSet<int> s_ovSeenThing = new HashSet<int>();

	// a thing found in the trench (or hit by a dirt swing): a bush is cut where it stands (J3), the rest go to the Wood /
	// Stone flow. 1 = taken, 2 = the spot was skipped (no tool for it), 0 = left alone (no tool, and canSkip is off).
	private int OvTake(GameObject best, int bestMode, string bestType, OvSpot q, bool canSkip, string how)
	{
		// GG1: wherever the thing came from (the pit list, a part resting on it, a swing that hit it), its hit test so far
		// decides the tool: ignored on this vein = left alone; the axe did nothing = the pickaxe
		if (_vp != null && best != null)
		{
			int bid = best.GetInstanceID();
			if (_vp.PitIgnore.Contains(bid))
			{
				return 0;
			}
			if (bestMode == 1 && bestType == "Destructible" && _vp.PitAxeNo.Contains(bid))
			{
				bestMode = 2;
				bestType = best.GetComponent<MineRock>() != null ? "MineRock" : "Destructible";
			}
		}
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
		// GG1: the first time a thing is taken - what the game calls it, its prefab and its drops
		if (s_ovSeenThing.Add(best.GetInstanceID()))
		{
			if (s_ovSeenThing.Count > 2000)
			{
				s_ovSeenThing.Clear();
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Thing \"{MHR_Drops.Tooltip(best)}\" ({best.name}, {bestType}) at {MHR_Mover.Fmt(best.transform.position)}: drops {MHR_Drops.DropList(best)} = {MHR_Drops.KindName(MHR_Drops.KindOf(best))} - the {(bestMode == 1 ? "axe" : "pickaxe")}");
		}
		// GG1: the game's tool tier check first (the thing's m_minToolTier against the tool's m_toolTier, as HitData.CheckToolTier)
		int need = GetMinToolTier(best);
		if (tool.m_shared.m_toolTier < need)
		{
			if (bestMode == 1 && bestType == "Destructible" && MHR_Drops.KindOf(best) == MHR_Drops.Unknown)
			{
				OvPitAxeNo(best, $"too hard for its {tool.m_shared.m_name} (needs tier {need}, the axe is {tool.m_shared.m_toolTier})");
			}
			else if (_vp != null && !OvPitCooling(best.GetInstanceID()))
			{
				_vp.PitCool[best.GetInstanceID()] = Time.time + OvPitCool;
				if (CompanionsPlugin.LogNormal) LogInfo($"\"{MHR_Drops.Tooltip(best)}\" ({best.name}) is too hard for its {tool.m_shared.m_name} (needs tier {need}, it is {tool.m_shared.m_toolTier}) - any companion may look at it again in {OvPitCool:F0} s");
			}
			if (!canSkip)
			{
				return 0;
			}
			OvDigOff();
			OvSkip(q, "\"" + best.name + "\" is in the way and it is too hard for its " + tool.m_shared.m_name, normal: true);
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
			_dgBushHp = MHR_Drops.Health(best);   // GG1: the hit test - its health now
			_dgBushNoDmg = 0;
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
		_hdHp = MHR_Drops.Health(best);   // GG1: the hit test - its health now
		_hdNoDmg = 0;
		_hdJudgeAt = -1f;
		_hdNoCool = false;
		_hdRamped = _hdRampNext;
		_hdRampNext = false;
		_hdBest = float.MaxValue;
		_hdHwTime = 0f;
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
			if (OvPitCooling(go.GetInstanceID()))
			{
				continue;   // FF1: cooling down
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
		OvMapDirty(_vp?.Map, go.transform.position, OvMapEventNear);   // R0.3: the pit map's squares within 2 m are read again
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

	// R3: a saved ramp is a way out only if its top reached the ground outside as it was when it began. K5 (test 18: ways
	// out cut in laps 1-2 were still counted after the trench went 5 m deeper): and only while its bottom is less than
	// 1.5 x its height above the trench floor now (the ground at the trench spot nearest its bottom)
	private bool OvRampOut(OvRamp r)
	{
		OvRampCheck(r);   // H4: checked on the map first when it needs it
		return OvRampUsable(_vp, r, _character != null ? _character.GetHeight() : 1.8f);
	}

	// U3 / H4: OvRampOut for any plan (the mover's walks in and out of a pit use it too). H4 (John 11-22: "We have a full
	// map of the area so it should be easy to track that and keep it in the shared report"): (a) its top reached the
	// ground outside (as before); (b) its top is at least 1 m farther from the vein than the trench spot nearest it - past
	// the trench, not on the bank by the vein; (c) on a vein with a built pit map, the map's route search found a path
	// from the trench floor at the spot nearest its bottom up to its top (OvRampCheck - kept on the ramp; a change on any
	// of its squares, or the ramp made deeper / higher, = checked again; not checked yet = not a way out yet). No built
	// map: (c) is the old K5 test (its bottom within 1.5 x its height of that floor). Replaces G3 (c) and MP6
	private static bool OvRampUsable(OvPlan p, OvRamp r, float h)
	{
		if (r.Top.y < r.Need - OvStepUp)
		{
			return false;
		}
		if (!OvRampPastTrench(p, r))
		{
			return false;
		}
		OvSpot q = p != null ? OvNearestIn(p, r.Bottom) : null;
		if (q == null)
		{
			return true;
		}
		if (p.Map != null && p.Map.Built)
		{
			return r.PathState == 1 && !OvRampStale(p.Map, r);
		}
		return r.Bottom.y - GroundAt(q.P) < OvRampRatio * h;
	}

	private const float OvRampPast = 1f;     // H4 (new, John 11-22 OK): a way out's top this much farther from the vein than the trench there

	// H4 (b): its top's flat distance to the nearest live vein part (the pit map's boxes) is at least 1 m more than the
	// trench spot nearest the top's. No map or no parts: true (as before)
	private static bool OvRampPastTrench(OvPlan p, OvRamp r)
	{
		List<Bounds> parts = p?.Map?.Parts;
		OvSpot s = p != null ? OvNearestIn(p, r.Top) : null;
		if (parts == null || parts.Count == 0 || s == null)
		{
			return true;
		}
		return OvBoxesFlat(parts, r.Top) >= OvBoxesFlat(parts, s.P) + OvRampPast;
	}

	// H4: a change on the map on one of its path's squares since the check
	private static bool OvRampStale(OvMap m, OvRamp r)
	{
		foreach (int i in r.Squares)
		{
			if (i >= m.ChangedAt.Length || m.ChangedAt[i] > r.CheckedAt)
			{
				return true;
			}
		}
		return false;
	}

	private static readonly List<Vector3> s_rcPath = new List<Vector3>();

	private static readonly List<int> s_rcSquares = new List<int>();

	// H4 (c): the map's route search (OvRtSearch: the walk rules of every map route - steps, drops, head room, the G2
	// vein cost) from the trench floor at the spot nearest its bottom to its top; a path that crosses a square over the
	// vein doesn't count (a way out never goes through the vein). Run by a companion on the vein (the search needs its
	// body): for this ramp only when it isn't checked or is stale
	private void OvRampCheck(OvRamp r)
	{
		OvPlan p = _vp;
		OvMap m = p?.Map;
		if (m == null || !m.Built || (r.PathState != 0 && !OvRampStale(m, r)))
		{
			return;
		}
		OvSpot q = OvNearestIn(p, r.Bottom);
		if (q == null)
		{
			return;
		}
		Vector3 from = new Vector3(q.P.x, GroundAt(q.P), q.P.z);
		// the MP4 step remembered for this companion's own walks is kept as it was
		Vector3 bf = _rtBlockFoot, bt = _rtBlockTop, bg = _rtBlockGoal;
		float ba = _rtBlockAt;
		bool lh = s_rtLastHadBlock;
		s_rcPath.Clear();
		s_rcSquares.Clear();
		int res = OvRtSearch(p, m, from, r.Top, s_rcPath, s_rcSquares, out string info);
		_rtBlockFoot = bf;
		_rtBlockTop = bt;
		_rtBlockGoal = bg;
		_rtBlockAt = ba;
		s_rtLastHadBlock = lh;
		int was = r.PathState;
		r.CheckedAt = Time.time;
		r.Squares.Clear();
		string why;
		if (res == 1)
		{
			bool overVein = false;
			foreach (int i in s_rcSquares)
			{
				if ((m.Flags[i] & MfVein) != 0)
				{
					overVein = true;
					break;
				}
			}
			r.Squares.AddRange(s_rcSquares);
			r.PathState = overVein ? 2 : 1;
			why = overVein ? "the only path crosses the vein" : $"a path of {s_rcSquares.Count} squares";
		}
		else
		{
			// no path (or the map can't tell yet): checked again when the map changes on the squares round its ends
			r.PathState = 2;
			r.Squares.Add(OvMapIndex(m, from));
			r.Squares.Add(OvMapIndex(m, r.Bottom));
			r.Squares.Add(OvMapIndex(m, r.Top));
			r.Squares.RemoveAll((int i) => i < 0);
			why = info ?? "no path";
		}
		if ((was != r.PathState || r.PathWhy != why) && CompanionsPlugin.LogNormal)
		{
			LogInfo($"The way out {MHR_Mover.Fmt(r.Bottom)} -> {MHR_Mover.Fmt(r.Top)} on the pit map: {(r.PathState == 1 ? "a way out" : "not a way out")} - {why}, from the trench floor at spot {q.Idx + 1} (H4)");
		}
		r.PathWhy = why;
	}

	// H4: every saved ramp of the vein that reached the outside height and is past the trench is checked when it needs it
	// (one per call, so a burst of map changes spreads over the ticks)
	private void OvRampChecks(OvPlan p)
	{
		if (p?.Map == null || !p.Map.Built)
		{
			return;
		}
		foreach (OvRamp r in p.Ramps)
		{
			if (r.Top.y >= r.Need - OvStepUp && OvRampPastTrench(p, r) && (r.PathState == 0 || OvRampStale(p.Map, r)))
			{
				OvRampCheck(r);
				return;
			}
		}
	}

	private static float OvBoxesFlat(List<Bounds> boxes, Vector3 q)
	{
		float best = float.MaxValue;
		foreach (Bounds b in boxes)
		{
			float dx = Mathf.Max(0f, Mathf.Max(b.min.x - q.x, q.x - b.max.x));
			float dz = Mathf.Max(0f, Mathf.Max(b.min.z - q.z, q.z - b.max.z));
			best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
		}
		return best;
	}

	private static OvSpot OvNearestIn(OvPlan p, Vector3 at)
	{
		OvSpot best = null;
		float bd = float.MaxValue;
		foreach (OvSpot sp in p.Spots)
		{
			float d = Flat(sp.P, at);
			if (d < bd)
			{
				bd = d;
				best = sp;
			}
		}
		return best;
	}

	// U3 (John 2026-11-20: "anytime they leave or go into the trench or pit they should use the cut out path"): for any
	// walk (the mover, every task): leaving a vein's pit (it is in the pit, the goal outside it and 1.5 m or more up) - the
	// saved way out whose top is nearest the goal, bottom first then top; going in (it is outside, the goal in the pit and
	// 1.5 m or more down) - the one whose top is nearest it, top first then bottom. Ways out tried in this walk are skipped.
	internal static bool PitWay(Vector3 pos, Vector3 goal, float height, HashSet<int> tried, out bool down, out Vector3 first, out Vector3 second, out int id)
	{
		down = false;
		first = second = pos;
		id = -1;
		float bd = float.MaxValue;
		for (int pi = 0; pi < s_ovPlans.Count; pi++)
		{
			OvPlan p = s_ovPlans[pi];
			if (p == null || p.Ramps.Count == 0)
			{
				continue;
			}
			bool posIn = OvInPit(p, new Bounds(pos, Vector3.zero)), goalIn = OvInPit(p, new Bounds(goal, Vector3.zero));
			bool leave = posIn && !goalIn && goal.y > pos.y + 1.5f;
			bool enter = !posIn && goalIn && goal.y < pos.y - 1.5f;
			if (!leave && !enter)
			{
				continue;
			}
			for (int i = 0; i < p.Ramps.Count; i++)
			{
				OvRamp r = p.Ramps[i];
				int key = pi * 1000 + i;
				if (tried.Contains(key) || !OvRampUsable(p, r, height))
				{
					continue;
				}
				float d = Flat(r.Top, leave ? goal : pos);
				if (d < bd)
				{
					bd = d;
					down = enter;
					first = enter ? r.Top : r.Bottom;
					second = enter ? r.Bottom : r.Top;
					id = key;
				}
			}
		}
		return id >= 0;
	}

	// U3: the job is walking its own way out (to it, or cutting / climbing it) - the mover leaves it alone
	internal bool CutsWayOut => _woActive || _rpActive;

	// K5 (John: cooldowns, never permanent skips): a try at a way out within 15 m of p in the last 60 s
	private bool OvExitTriedNear(Vector3 p)
	{
		OvPlan v = _vp;
		float now = Time.time;
		for (int i = v.ExitAt.Count - 1; i >= 0; i--)
		{
			float t = i < v.ExitTime.Count ? v.ExitTime[i] : 0f;
			if (now - t >= OvPitCool)
			{
				v.ExitAt.RemoveAt(i);
				if (i < v.ExitTime.Count)
				{
					v.ExitTime.RemoveAt(i);
				}
				continue;
			}
			if (Flat(v.ExitAt[i], p) <= OvWayNear)
			{
				return true;
			}
		}
		return false;
	}

	private void OvExitTry(Vector3 p)
	{
		while (_vp.ExitTime.Count < _vp.ExitAt.Count)
		{
			_vp.ExitTime.Add(Time.time);
		}
		_vp.ExitAt.Add(p);
		_vp.ExitTime.Add(Time.time);
	}

	// R5: the nearest way out of this vein's trench (a saved ramp that is really out, R3) not tried on this trip, for the
	// mover when it is stuck below its goal on a vein (where it would otherwise climb). False = none.
	internal bool VeinWayOut(Vector3 pos, Vector3 goal, HashSet<int> tried, out Vector3 bottom, out Vector3 top, out int id)
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
			float d = Flat(goal, r.Top);   // U3: the way out whose top is nearest where it is headed
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
			if (far && d < bd && !OvOutBlocked(sp))   // Z5
			{
				bd = d;
				best = sp;
			}
		}
		return best;
	}

	// E2 / E3: getting out of the trench (leaving): a saved way out of the trench first (walked to along the floor), else
	// a new one cut from here
	private void OvGetOut(Vector3 goal, string why, Vector3? dest = null)
	{
		_outTried.Clear();
		_woActive = false;
		_woFallback = null;
		Vector3 me = base.transform.position;
		// U3: the saved way out whose top is nearest where it is headed (was: whose bottom was nearest it)
		Vector3 to = dest ?? goal;
		OvRamp best = null;
		float bd = float.MaxValue;
		foreach (OvRamp r in _vp.Ramps)
		{
			if (_rpTried.Contains(r) || !OvRampOut(r))
			{
				continue;
			}
			float d = Flat(to, r.Top);
			if (d < bd)
			{
				bd = d;
				best = r;
			}
		}
		// U3 (John: "They can carve a 2nd or 3rd one on the other sides"): that top is more than 15 m farther from where it
		// is headed than the trench spot nearest there - a new one is cut at that spot (E2's way: its floor, then up to the
		// wall top); if that cut fails, the saved one is used (once)
		OvSpot side = OvNearestIn(_vp, to);
		if (best != null && side != null && !OvOutBlocked(side) && bd - Flat(side.P, to) > OvWayNear)   // Z5
		{
			_woFallback = best;
			_woActive = true;
			_woOut = true;
			_woFloor = side.P;
			_woFloor.y = GroundAt(side.P);
			_woGoal = OvOutside(side);
			_woGoal.y = OvWallTop(side);
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Getting out ({why}): the nearest way out comes up {bd:F0} m from where it is going - cutting one on that side at {MHR_Mover.Fmt(_woFloor)} (spot {side.Idx + 1})");
			return;
		}
		if (best != null)
		{
			_woActive = true;
			_woOut = true;
			_woFloor = best.Bottom;
			_woGoal = best.Top;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Getting out ({why}) by the way out at {MHR_Mover.Fmt(best.Bottom)} (its top {bd:F0} m from where it is going)");
			return;
		}
		// T4: a saved way out within 15 m that the trench has gone below - extended down to the floor, then up it
		OvRamp ext = OvStrandedNear(me);
		if (ext != null && OvExtendFloor(ext, out Vector3 extFloor))
		{
			_woActive = true;
			_woOut = true;
			_woFloor = extFloor;   // W1: on the way out's own line, downhill from its old bottom
			_woGoal = ext.Bottom;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Getting out ({why}): extending the way out at {MHR_Mover.Fmt(ext.Bottom)} down its own line to the floor at {MHR_Mover.Fmt(_woFloor)} ({ext.Bottom.y - _woFloor.y:F1} m up to its bottom)");
			return;
		}
		OvRampBegin(goal, null, outOfTrench: true, why);
	}

	// E2: getting out failed at one place: the next (5 m from the places tried), up to 3; then it is stuck and waits for you
	private void OvGetOutNext(string why)
	{
		// U3: the new way out on the far side failed - the saved one instead (once)
		if (_woFallback != null)
		{
			OvRamp fb = _woFallback;
			_woFallback = null;
			_woActive = true;
			_woOut = true;
			_woFloor = fb.Bottom;
			_woGoal = fb.Top;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Getting out by the saved way out at {MHR_Mover.Fmt(fb.Bottom)} instead - {why}");
			return;
		}
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
				OvExitTry(me);   // R4: counted, so no other try is made within 15 m of here
				_vp.ExitBy = null;
				if (CompanionsPlugin.LogNormal) LogInfo("The trench is shallow here - no way out needed");
				if (_rpThing != null)
				{
					OvRampThingBack();   // Z2: the thing it cut the way out for gets its try from here
				}
				return;
			}
			OvExitTry(me);
			_exTry = true;
			OvRampBegin(_woGoal, null, outOfTrench: false, _woWhy);
			return;
		}
		if (OvWalkGaveUp(dt, _woFloor))
		{
			_woActive = false;
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to {MHR_Mover.Fmt(_woFloor)} along the trench (the walk gave up)");
			OvWayOutMissed(_woFloor, OvName());   // Z3 (a): a saved way out's bottom (no ramp there = nothing)
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
			OvExitTry(_woFloor);   // counts as a try
			if (_rpThing != null)
			{
				OvRampThingBack();   // Z2: the thing it went to cut the way out for gets its try from here
			}
			return;
		}
		OvWalk(_woFloor, 0.5f, Flat(me, _woFloor) > 8f, dt);
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
			OvStandBy("stopped - every dirt point and part left failed; it starts again when anything in the pit changes");   // SV5
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
		// D1: the after-vein steps (ore to a chest within 20 m or the ore spot, Q2's ore dump carry, piles in the pit) for a
		// vein that is done while parts are left in the ground - before, only a vein that was gone started them
		if (!_ovOut)
		{
			MHR_GatherRules.AfterVein(this);
		}
		OvLeaveOut(MHR_GatherRules.AfterVeinGoal(this, out Vector3 dvGoal, out _) ? dvGoal : base.transform.position, "the vein is done");
	}

	private void OvTrenchDoneSay()
	{
		_ai?.StopMoving();
		_ovDoneSaid = true;
		if (OvIsCopper(_vp))
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Copper deposit \"{_vp.Name}\": no trench - mining what it can reach, then the dirt to reach the rest (CU2)");
			return;
		}
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
		_dgPartMiss = 0;
		_dgLastProg = false;
		_dgDamage = false;
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
		_dgDirt = false;
		_dgStrike = false;
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

	private float _dgFaceFrom = -1f;   // L2: when it started turning for this swing (-1 = not turning)

	// L2 (game: Attack.GetMeleeAttackDir swings along the body's facing, transform.forward - the pickaxe can't tilt - and
	// Character.UpdateRotation turns the body gradually): a swing starts once the body faces the aim within 10 degrees
	// (flat); after 1 s of turning it swings anyway. True = swing now.
	private bool OvFacing(Vector3 point)
	{
		Vector3 d = point - base.transform.position;
		d.y = 0f;
		Vector3 f = base.transform.forward;
		f.y = 0f;
		if (d.sqrMagnitude < 0.01f || f.sqrMagnitude < 0.01f || Vector3.Angle(f, d) <= 10f)
		{
			_dgFaceFrom = -1f;
			return true;
		}
		if (_dgFaceFrom < 0f)
		{
			_dgFaceFrom = Time.time;
		}
		if (Time.time - _dgFaceFrom >= 1f)
		{
			_dgFaceFrom = -1f;
			return true;
		}
		return false;
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
			// GG2: a dirt point of the dirt list, or a part, is swung at from where it stands (its place was chosen outside
			// the parts' boxes) - never walked closer
			// R3b.2 (test of 83bb2a6: Gunter swung at spot 76 from the edge 3 m above it and dug out its own footing): a spot
			// more than 1.2 m below its feet is always walked to (the mover goes in by a way out), never swung at from above
			if (!_dgDirt && !_dgPart && (flat > reach * 0.9f || below))
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
				// R1.3 (the radar): the map's squares in front read a wall - the way up starts now, not after 3 s with no
				// headway (that test below stays for a clear or unknown map answer)
				if (!_dgBushOn && q != null && !q.RampTried && flat <= OvRampReach && up >= OvStepUp && OvMapWallAhead(_dgPoint, out byte dk, out _, out _) == 1)
				{
					OvDigOff();
					OvRampBegin(_dgPoint, q, outOfTrench: false, $"the map reads {OvMapKindName(dk)} in front, toward spot {q.Idx + 1}, {up:F1} m up");
					return;
				}
				if (!_dgBushOn && q != null && !q.RampTried && flat <= OvRampReach && up >= OvStepUp && _dgHwTime >= OvNoHeadway)
				{
					OvDigOff();
					OvRampBegin(_dgPoint, q, outOfTrench: false, $"no headway for {OvNoHeadway:F0} s toward spot {q.Idx + 1} (not 0.3 m closer), {up:F1} m up");
					return;
				}
				if (OvWalkGaveUp(dt, _dgPoint))
				{
					if (_dgBushOn)
					{
						GameObject b = _dgBush;
						OvDigOff();
						OvBushGiveUp(b, q, $"couldn't get to it (the walk gave up, {flat:F1} m away, {up:F1} m up)");
						return;
					}
					OvDigOff();
					if (q != null && up > 0f && !q.RampTried && flat <= OvRampReach)
					{
						OvRampBegin(_dgPoint, q, outOfTrench: false, $"the walk to spot {q.Idx + 1} gave up, {up:F1} m up");
					}
					else
					{
						OvSkip(q, $"couldn't get to it (the walk gave up, {flat:F1} m away, {up:F1} m up)");
					}
					return;
				}
				OvWalk(_dgPoint, below ? 0.3f : reach * 0.8f, flat > 8f, dt);
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
			if (!OvFacing(_dgBush.transform.position))
			{
				return;   // L2: turned first
			}
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
		if (!_dgClear && !_dgStrike && g <= OvFloor(_dgPoint) + OvDigGain)
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
		if (!OvFacing(_dgPoint))
		{
			return;   // L2: turned first
		}
		// E2 (silver, a point of the dirt list or E3's place): right before each swing, the E1 check from where it actually
		// stands - it fails: no swing, it repositions (OvDrTick reads _drRepos once the dig is off)
		// J2 (silver): a rock piece's swing - from where it actually stands no ray may meet the vein first
		if (_dgPart && _clFor != null && _clFor.IsRock && OvIsSilver(_vp) && !OvDirtDone(OvC()) && !OvSwingClean(FindBestTool(2)?.m_shared?.m_attack, base.transform.position, _dgPoint, out _, _clFor.C))
		{
			_clRockWhy = "from where it stands the swing at the rock piece would meet the vein first";
			OvDigOff();
			return;
		}
		if (_dgDirt && !_drForThing && OvIsSilver(_vp))
		{
			bool clean = OvSwingClean(FindBestTool(2)?.m_shared?.m_attack, base.transform.position, _dgPoint, out Vector3 sdg);
			// H3: a step dig also never swings where the game's dig would lower its own feet (G1)
			OvDigOp sop = _sdOn && clean ? OvDigOpFor(FindBestTool(2)) : null;
			if (!clean || (sop != null && OvDigLowersFeet(sop, base.transform.position, _dgPoint, sdg)))
			{
				_drRepos = clean ? "from where it stands the game's dig would lower the ground at its own feet" : "from where it stands the swing would meet the vein, or would not meet the dirt first";
				OvDigOff();
				return;
			}
		}
		int sw = SwingNow(out ItemDrop.ItemData swItem, out _);
		if (sw == -1)
		{
			// DD8: no working pickaxe - the next tick goes to the axe work (OvAxeTick)
			OvDigOff();
			_ovPickOk = false;
			_ovPickAt = Time.time + 1f;
			return;
		}
		if (sw == 1)
		{
			_dgSwung = true;
			_dgSwungAt = Time.time;
			_dgBefore = g;
			OvFeetRead(_dgFeetG, out _dgFeetAt);   // F7 (a)
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
				// GG1: the hit test - a hit that doesn't lower its health (the game's s_health) isn't progress; 3 in a row =
				// the axe does nothing to it, the pickaxe next
				float hp = MHR_Drops.Health(_dgBush);
				if (hp >= 0f && _dgBushHp >= 0f && hp >= _dgBushHp - 0.01f)
				{
					_dgBushNoDmg++;
				}
				else
				{
					_dgBushNoDmg = 0;
					_dgBushHp = hp;
				}
				if (_dgBushNoDmg >= OvBushMiss)
				{
					GameObject nb = _dgBush;
					OvDigOff();
					_dgLastProg = true;
					OvPitAxeNo(nb, $"{OvBushMiss} axe hits didn't lower its health ({hp:F0})");
					return;
				}
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
		// DD3 (John 2026-11-19: "A rock does not matter and should be carved through"): a swing that hit a plain rock (a
		// MineRock5 that isn't this vein) is work on the spot - the game breaks its parts until the dirt is reached; it
		// counts like a dig (the 3-swing rule)
		bool rockHit = hitThing && _ovThingHitGo.GetComponentInParent<MineRock5>() != null;
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
			if (thing != null && _drForThing && thing == _rpThing)
			{
				// P2: the swing at the ground under a thing reached the thing itself - it is within reach now: handed back
				_dgReached = true;
				OvDigOff();
				return;
			}
			// V4 (log 2f290f42: Gunter's way up struck Rock_4, handed it to the Stone flow, got it back and struck it again,
			// 1232 times in 24 s): a thing this way up's swing meets is handed to the Wood / Stone flow once; when the same
			// way up meets it a second time, the way up gives up on this spot (another spot's way up is cut instead)
			if (thing != null && _rpActive && _dgRamp && !_rpMet.Add(thing.GetInstanceID()))
			{
				_rpBad.Add(_rpStart);
				OvRampFail($"its swing met \"{thing.name}\" a second time (it was handed to the {(mode == 1 ? "Wood" : "Stone")} job once already)");
				return;
			}
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
		if (_dgDirt)
		{
			// GG2 (John: "It should be like 2-3 swings of the pickaxe"): a swing that lowered the ground at the point, hit the
			// vein's rock (HH1) or carved a plain rock is work; down to its goal (0.3 m under the part's bottom) = done; 3
			// swings in a row that did none of those = the point waits 1 min.
			bool drop = _dgBefore - after >= OvDigGain;
			bool atFloor = after <= OvFloor(_dgPoint) + OvDigGain;
			// DD3 (kept): a swing that hit a plain rock (not this vein) carves through it - not one of the 3
			// (only one its pickaxe can hurt: the game's tier check and damage rules - else it would never end)
			GameObject rk = rockHit ? _ovThingHitGo.GetComponentInParent<MineRock5>()?.gameObject : null;
			ItemDrop.ItemData pk = rk != null ? FindBestTool(2) : null;
			bool carve = rk != null && pk != null && pk.m_shared.m_toolTier >= GetMinToolTier(rk) && MHR_Drops.ToolHurts(rk, axe: false) && !OvSameOre(_vp, rk.GetComponent<MineRock5>());   // H2: another vein of this ore isn't carved
			// HH1 (John: "it was ok to hit the ore"): a swing that hit the vein's rock is work too (the game damages the part)
			// P4: a dirt point higher than its swing reaches is cut from below - the game dug into the bank between it and
			// the point, 0.3 m or more above its feet (not the floor), is work too
			Vector3 ft = base.transform.position;
			bool bank = dug && _dgDugPt.y >= ft.y + 0.3f && Flat(ft, _dgDugPt) <= Flat(ft, _dgPoint) + 0.3f;
			// D1 (John, 2026-11-21, silver veins only: "avoid the ore other than to clear dirt off it"): on a silver vein a swing
			// that only hit the vein's rock is not work - 3 in a row end the round as any other
			bool silver = OvIsSilver(_vp) && !_drForThing;
			bool oreWork = hitOre && !silver;
			// Q1 (John 2026-11-21, silver: "as long as the pickaxe hits the dirt and causes a change in the dirt it should
			// count"): the game dug and the ground where it dug went down 0.05 m or more, at any height - not a dig within
			// 0.5 m (flat) of its own feet (5c: no digging itself into a hole), nor where the ground was already at its lowest
			// allowed (2 m under the part's bottom, or the game's 8 m limit). The 0.3 m-above-its-feet bank test is not used.
			bool feetDig = false;
			// F7 (a) (log cdfe000a: Je's digs 0.5-0.7 m in front took its own floor from 147.4 down to 145.8): the ground at
			// its feet or at its body radius went down 0.05 m or more with this swing = it dug its own floor (every vein)
			bool floorDug = dug && OvFeetWentDown(_dgFeetG, _dgFeetAt);
			bool tooDeep = false;
			if (silver)
			{
				feetDig = dug && (Flat(ft, _dgDugPt) <= OvFeetDig || floorDug);
				// P1 step 2 (John 11-22 21:36Z, Q-P1d (a)): lowering a square, a swing may lower its own feet while they stay
				// at or above the square's goal
				if (_lwOn && feetDig && GroundAt(ft) >= _dgGoalY - OvDigGain)
				{
					feetDig = false;
					floorDug = false;
				}
				// U2 (John 11-23 00:30Z: "We dont want them to dig a pit at their feet, but if they are striking the base of
				// nearby dirt so they can get close to the remaining dirt that is fine"): from a last-resort place only a dig
				// within 0.5 m (flat) of its feet is its own feet - its floor going down from a dig further off is fine
				bool u2 = _u2On && _drSpot != null && !_drDeep && !_lwOn;
				if (u2)
				{
					feetDig = dug && Flat(ft, _dgDugPt) <= OvFeetDig;
					floorDug = false;
				}
				// U2: work only while the ground it dug is still above the point's goal (never deeper than needed; the game's
				// 8 m floor as always)
				float lowest = u2 ? Mathf.Max(_drSpot.Goal, OvFloor(_dgDugPt)) : Mathf.Max(_drSpot != null && _drSpot.Part != null ? OvPartBottom(_drSpot.Part) - OvDeepMax : float.MinValue, OvFloor(_dgDugPt));
				bank = dug && !feetDig && _dgDugBefore > lowest + OvDigGain && GroundAt(_dgDugPt) <= _dgDugBefore - OvDigGain;
				// F7 (b): the dug point is now under the lowest allowed (2 m under the part's bottom, or the game's 8 m limit)
				tooDeep = !u2 && dug && _drSpot != null && _drSpot.Part != null && GroundAt(_dgDugPt) < lowest;
			}
			_dgNoProg = drop || bank || carve || oreWork ? 0 : _dgNoProg + 1;
			// rule 5 (silver): reposition at once when a swing hit the vein (1 hit is the limit - no part breaks from dirt work)
			// or dug at its own feet; after 2 swings in a row with no work (OvDirtNoProg, every vein)
			// F7 (a), every vein: it dug its own floor - another place at once
			if (!silver && floorDug && after > _dgGoalY + OvDigGain)
			{
				_drRepos = "a swing dug its own floor";
			}
			if (silver && after > _dgGoalY + OvDigGain)
			{
				if (tooDeep)
				{
					_drRepos = $"a swing dug the ground at {MHR_Mover.Fmt(_dgDugPt)} lower than 2 m under the part (or the game's 8 m limit)";
				}
				else if (hitOre)
				{
					_drRepos = "a swing hit the vein";
				}
				else if (feetDig)
				{
					_drRepos = floorDug ? $"a swing dug its own floor ({MHR_Mover.Fmt(_dgDugPt)})" : $"a swing dug at its own feet ({MHR_Mover.Fmt(_dgDugPt)})";
				}
				else if (_dgNoProg >= OvDirtNoProg)
				{
					_drRepos = $"{OvDirtNoProg} swings in a row did no work";
				}
			}
			// P1 step 2: lowering a square is not progress on the dirt points (they come back only when something within 2 m
			// of them changes), so the lowering goes on square by square
			if ((drop || bank) && _vp != null && !_lwOn)
			{
				OvC().ProgAt = Time.time;
			}
			if (CompanionsPlugin.LogDetailed) Log($"Dirt swing at {MHR_Mover.Fmt(_dgPoint)} (holds up a part): ground {_dgBefore:F2} -> {after:F2}, goal {_dgGoalY:F2}{(bank ? (silver ? $", dug the dirt ({_dgDugBefore:F2} -> {GroundAt(_dgDugPt):F2})" : ", cut into the bank toward it") : "")}{(floorDug ? ", dug its own floor" : feetDig ? ", dug at its own feet" : "")}{(tooDeep ? ", under the lowest allowed" : "")}{(hitOre ? ", hit the vein's rock" : "")}{(carve ? $", carving the rock \"{_ovThingHitGo.name}\"" : "")}{dugAt}{(drop || bank || carve || oreWork ? "" : $" - nothing  ({_dgNoProg} in a row)")}, feet {base.transform.position.y:F2}");
			if (after <= _dgGoalY + OvDigGain)
			{
				_dgReached = true;
				OvDigOff();
			}
			else if (atFloor)
			{
				_dgLimit = true;
				OvDigOff();
			}
			else if (_drRepos != null || _dgNoProg >= OvDirtNoProg)
			{
				OvDigOff();
			}
			return;
		}
		if (_dgPart && _clFor != null && _clFor.IsRock)
		{
			// J2: a rock piece's round - a swing on that rock is progress; on silver a swing that met the vein ends the round
			if (hitOre && OvIsSilver(_vp))
			{
				_clRockWhy = "a swing at the rock piece hit the vein";
				OvDigOff();
				return;
			}
			hitOre = rockHit && _ovThingHitGo.GetComponentInParent<MineRock5>() == _clFor.Rock;
		}
		if (_dgPart)
		{
			// R2a: mining the part itself from where it stands - a swing on the vein's rock is progress (its break is seen by
			// OvClRound). K3 (test 18: 403 of 416 misses dug the dirt bank in front of the part): a swing the game dug into the
			// dirt between it and the part, 0.3 m or more above its feet (P4's bank test - not the floor at its own feet, Y7),
			// is work too. Ends: 3 swings in a row that did neither (a dig at the game's 8 m limit is one of them, G5), or 20 swings that
			// didn't hit the part.
			Vector3 pf = base.transform.position;
			bool toward = !hitOre && dug && _dgDugPt.y >= pf.y + 0.3f && Flat(pf, _dgDugPt) <= Flat(pf, _dgPoint) + 0.3f;
			bool pLimit = toward && GroundAt(_dgDugPt) <= OvFloor(_dgDugPt) + OvDigGain;
			_dgNoProg = hitOre || (toward && !pLimit) ? 0 : _dgNoProg + 1;
			_dgPartMiss = hitOre ? _dgPartMiss : _dgPartMiss + 1;
			if (toward && !pLimit && _vp != null)
			{
				OvC().ProgAt = Time.time;
			}
			if (CompanionsPlugin.LogDetailed) Log($"Swing at the part at {MHR_Mover.Fmt(_dgPoint)}: {(hitOre ? "hit the vein's rock" : toward ? (pLimit ? "dug toward it, at the game's 8 m dig limit there" : "dug the dirt toward it") : $"didn't hit the rock ({_dgNoProg} in a row)")}{dugAt}, {_dgPartMiss} swing(s) without hitting it, feet {pf.y:F2}");
			// G5 (test 28: 17 of 28 part waits came from one swing landing in dirt at the game's 8 m limit right after hits on
			// the rock - half-carved parts): a swing at the limit is a swing that did nothing; 3 in a row end it as before
			if (_dgNoProg >= OvNoProgSwings || _dgPartMiss >= OvPartMissMax)
			{
				_dgLimit = pLimit;
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
			bool cProg = prog || hitOre || dug || rockHit;
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
		if (hitThing && !rockHit && !prog && !oreProg && !dug && !_dgRamp && _dgSpot != null)
		{
			OvSpot ts = _dgSpot;
			string tn = _ovThingHitGo != null ? _ovThingHitGo.name : "?";
			OvDigOff();
			OvSkip(ts, $"its swing hit \"{tn}\", a thing it left, and changed nothing", normal: true);
			return;
		}
		// R3a.2 (test of 9dfc40e: 9 swings at spot 28 dug 0.2-0.4 m beside it, the spot never went down): for a trench
		// spot, only the spot's own ground going down, the vein's ore or a plain rock carved counts - "the game dug
		// somewhere" counts only for ramp steps
		bool dugCounts = dug && (_dgRamp || _dgSpot == null);
		_dgLastProg = prog || oreProg || dugCounts || rockHit;
		if (_dgRamp)
		{
			// Y1 (John 11-25: "what about if they are mining something hard that takes a lot of strikes?" - log e891dcd2:
			// 132 way-up swings at the slope never changed the ground, the game "dug" at the same point every time): a way-up
			// swing is progress only when (a) the ground at the swing point went down, (b) the game dug and the ground where
			// it dug went down (Q1's test), or (c) it hit a rock or the vein's ore its pickaxe can damage by the game's rules
			// (DD3's test) - a hard rock counts on every strike until it breaks. M3 uses the same test.
			bool dugDown = dug && GroundAt(_dgDugPt) <= _dgDugBefore - OvDigGain;
			bool hurt = (hitOre && OvPickHurts(_vRock != null ? _vRock.gameObject : null)) || (rockHit && OvPickHurts(_ovThingHitGo.GetComponentInParent<MineRock5>()?.gameObject));
			_dgLastProg = prog || dugDown || hurt;
		}
		_dgDamage |= _dgRamp ? _dgLastProg : prog || oreProg || dug || rockHit;   // M3: this dig lowered the ground or damaged a rock
		_dgNoProg = _dgLastProg ? 0 : _dgNoProg + 1;
		if (CompanionsPlugin.LogDetailed) Log($"Dirt swing at {MHR_Mover.Fmt(_dgPoint)}{(_dgRamp ? " (ramp step)" : _dgSpot != null ? $" (spot {_dgSpot.Idx + 1})" : "")}: ground {_dgBefore:F2} -> {after:F2}{dugAt}{(oreProg ? " - hit the vein's ore" : prog || dug ? "" : rockHit ? $" - carving through the rock \"{_ovThingHitGo.name}\"" : $" - no change ({_dgNoProg} in a row{(hitThing ? ", it hit \"" + _ovThingHitGo.name + "\"" : "")})")}, feet {base.transform.position.y:F2}");
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
				// X1: the first thing on the swing's line is an indestructible world object - the spots it blocks go beside it
				if (_vp != null && OvWorldBlocker(_dgPoint, out Collider wb))
				{
					OvAddBlocker(_vp, wb, _dgSpot);
					return;
				}
				OvSpotDone(_dgSpot, $"can't go lower ({OvNoProgSwings} swings with no change)");
			}
		}
	}

	// X1: an indestructible world object - solid, not the terrain, nothing the game can damage anywhere on it (no
	// IDestructible), not a piece a player built, not a creature
	private static bool OvIsWorldSolid(Collider o)
	{
		if (o == null || o.isTrigger || o.attachedRigidbody != null || o.gameObject.layer == LayerMask.NameToLayer("terrain"))
		{
			return false;
		}
		if (o.GetComponentInParent<Character>() != null || o.GetComponentInParent<IDestructible>() != null)
		{
			return false;
		}
		Piece pc = o.GetComponentInParent<Piece>();
		return pc == null || !pc.IsPlacedByPlayer();
	}

	private static readonly RaycastHit[] s_wbHits = new RaycastHit[32];

	// X1 / Z1: where the game's swing starts (its swing height above the feet)
	private Vector3 OvSwingFrom()
	{
		Attack a = FindBestTool(2)?.m_shared?.m_attack;
		return base.transform.position + Vector3.up * (a != null ? a.m_attackHeight : 1f);
	}

	// X1 / Z1: the game's swing line (from its swing height toward the point, the pickaxe's range + 1 m, its ray width, the
	// game's attack layers): the first solid thing on it (not a creature) and where it was hit. Null = nothing.
	private Collider OvFirstOnSwing(Vector3 point, out Vector3 at)
	{
		at = point;
		Attack a = FindBestTool(2)?.m_shared?.m_attack;
		float range = (a != null ? a.m_attackRange : 1.8f) + 1f;
		float width = a != null ? Mathf.Max(0.05f, a.m_attackRayWidth) : 0.2f;
		Vector3 o = OvSwingFrom();
		Vector3 dir = point - o;
		if (dir.sqrMagnitude < 0.0001f)
		{
			return null;
		}
		dir.Normalize();
		int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
		int n = Physics.SphereCastNonAlloc(o, width, dir, s_wbHits, range, mask, QueryTriggerInteraction.Ignore);
		float best = float.MaxValue;
		Collider first = null;
		for (int i = 0; i < n; i++)
		{
			RaycastHit h = s_wbHits[i];
			if (h.collider == null || h.collider.isTrigger || h.collider.GetComponentInParent<Character>() != null || h.distance >= best)
			{
				continue;
			}
			best = h.distance;
			first = h.collider;
			// a cast that starts inside the collider reports no point: the start of the line then
			at = h.distance <= 0f && h.point == Vector3.zero ? o : h.point;
		}
		return first;
	}

	// X1: the first thing on the swing line, when that is an indestructible world object
	private bool OvWorldBlocker(Vector3 point, out Collider blocker)
	{
		Collider first = OvFirstOnSwing(point, out _);
		blocker = OvIsWorldSolid(first) ? first : null;
		return blocker != null;
	}

	// X1 / Z1: the object's game box is kept for this vein (once)
	private static void OvNoteBlocker(OvPlan p, Collider wb)
	{
		Bounds b = wb.bounds;
		foreach (Bounds kb in p.Blockers)
		{
			if (Vector3.Distance(kb.center, b.center) < 0.1f && Vector3.Distance(kb.size, b.size) < 0.1f)
			{
				return;
			}
		}
		p.Blockers.Add(b);
		p.BlockerNames.Add(wb.name);
	}

	// X1 / X2: the object's game box is kept for this vein (once), and every ring spot inside it moves beside it
	private void OvAddBlocker(OvPlan p, Collider wb, OvSpot hit)
	{
		Bounds b = wb.bounds;
		OvNoteBlocker(p, wb);
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {hit.Idx + 1}/{p.Spots.Count} at {MHR_Mover.Fmt(hit.P)}: blocked by \"{wb.name}\" (it can't be damaged), box {MHR_Mover.Fmt(b.min)} to {MHR_Mover.Fmt(b.max)} - the trench goes beside it, toward the vein (X2)");
		bool movedHit = false;
		foreach (OvSpot sp in p.Spots)
		{
			if (sp.Closed || !OvBesideBlocker(p, sp.P, out Vector3 np))
			{
				continue;
			}
			sp.P = np;
			sp.Floor = OvFloor(np);
			sp.Target = Mathf.Max(sp.Floor, OvSpotBase(p, np) - OvUnder(p));   // R3b.1 (D2: 1.5 m on silver)
			float g = GroundAt(np);
			sp.LapStart = g;
			sp.OpenLap = p.Lap;
			sp.LapDepth = Mathf.Max(sp.Target, g - OvLapStep);
			sp.Done = g <= sp.LapDepth + OvDigGain;
			sp.DoneWhy = sp.Done ? "already at this lap's depth" : null;
			sp.Skip = false;
			sp.SkipWhy = null;
			movedHit |= sp == hit;
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {sp.Idx + 1} moved beside \"{wb.name}\" to {MHR_Mover.Fmt(np)}");
		}
		if (!movedHit)
		{
			// X2: no place beside it toward the vein's centre - this spot is left out (I1b: until something near it changes)
			hit.Closed = true;
			hit.ClosedAt = Time.time;   // I1b
			hit.ClosedWhy = $"blocked by \"{wb.name}\" (it can't be damaged) with no place beside it toward the vein";
			if (hit.Holder == this)
			{
				hit.Holder = null;
			}
			if (_ovSpot == hit)
			{
				_ovSpot = null;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {hit.Idx + 1} left out: {hit.ClosedWhy}");
		}
		else
		{
			if (hit.Holder == this)
			{
				hit.Holder = null;
			}
			if (_ovSpot == hit)
			{
				_ovSpot = null;
			}
		}
		OvSave(p);
	}

	// X2: a ring point inside a blocker's box (flat, the body radius out) - moved toward the vein's centre in 0.5 m steps
	// to the first point outside every blocker box where a body (its capsule) touches nothing solid but the ground and
	// the vein's own rock (the trench goes through the vein there). False = not in a box, or no such point before the
	// centre (out = unchanged).
	private bool OvBesideBlocker(OvPlan p, Vector3 pos, out Vector3 outPos)
	{
		outPos = pos;
		if (p.Blockers.Count == 0)
		{
			return false;
		}
		float rad = _character != null ? _character.GetRadius() : 0.4f;
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		if (!OvInBlocker(p, pos, rad))
		{
			return false;
		}
		Vector3 dir = p.Center - pos;
		dir.y = 0f;
		float len = dir.magnitude;
		if (len < 0.5f)
		{
			return false;
		}
		dir /= len;
		for (float d = 0.5f; d < len; d += 0.5f)
		{
			Vector3 pt = pos + dir * d;
			pt.y = GroundAt(pt);
			if (OvInBlocker(p, pt, rad) || !OvBodyFitsVein(p, pt, rad, hh))
			{
				continue;
			}
			outPos = pt;
			return true;
		}
		return false;
	}

	private static bool OvInBlocker(OvPlan p, Vector3 pt, float rad)
	{
		foreach (Bounds b in p.Blockers)
		{
			if (pt.x >= b.min.x - rad && pt.x <= b.max.x + rad && pt.z >= b.min.z - rad && pt.z <= b.max.z + rad && pt.y <= b.max.y && pt.y + 2f >= b.min.y)
			{
				return true;
			}
		}
		return false;
	}

	private static readonly Collider[] s_bfHits = new Collider[32];

	// X2: OvBodyFits, the vein's own rock not counted (the trench is cut through it there)
	private bool OvBodyFitsVein(OvPlan p, Vector3 pt, float rad, float hh)
	{
		float r = rad * 0.9f;
		int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece");
		int n = Physics.OverlapCapsuleNonAlloc(pt + Vector3.up * (r + 0.15f), pt + Vector3.up * Mathf.Max(r + 0.2f, hh - r), r, s_bfHits, mask, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < n; i++)
		{
			MineRock5 m = s_bfHits[i] != null ? s_bfHits[i].GetComponentInParent<MineRock5>() : null;
			if (m != null && p.Rocks.Contains(m))
			{
				continue;
			}
			return false;
		}
		return true;
	}

	// ---------- C5: the way up (a ramp) ----------
	private const float OvRampVeinGap = 0.5f;   // F12: a silver way up's line keeps 0.5 m from every part's box (flat)

	// F12: the way up from `from` to `to` goes away from the vein (its goal farther from the nearest part's box than its
	// start) and its line keeps 0.5 m (flat) from every part's box, looked at every 0.25 m
	private bool OvRampOuterOk(Vector3 from, Vector3 to, out string why)
	{
		why = null;
		if (_vp == null || !s_ovClear.TryGetValue(_vp, out OvClear c) || c.ById.Count == 0)
		{
			return true;
		}
		float d0 = OvNearPartFlat(c, from), d1 = OvNearPartFlat(c, to);
		if (d1 <= d0)
		{
			why = $"its goal is no farther from the vein than where it starts ({d1:F1} m vs {d0:F1} m)";
			return false;
		}
		float len = Flat(from, to);
		for (float t = 0f; t <= len + 0.001f; t += 0.25f)
		{
			Vector3 q = Vector3.Lerp(from, to, len > 0.001f ? t / len : 0f);
			float dq = OvNearPartFlat(c, q);
			if (dq < OvRampVeinGap)
			{
				why = $"its line passes {dq:F1} m from the vein's rock at {MHR_Mover.Fmt(q)}";
				return false;
			}
		}
		return true;
	}

	// F12: the flat distance from q to the nearest part's game box
	private static float OvNearPartFlat(OvClear c, Vector3 q)
	{
		float best = float.MaxValue;
		foreach (OvPart part in c.ById.Values)
		{
			Bounds b = new Bounds(part.Centre, part.Half * 2f);
			Vector3 k = b.ClosestPoint(new Vector3(q.x, b.center.y, q.z));
			best = Mathf.Min(best, Flat(k, q));
		}
		return best;
	}

	private void OvRampBegin(Vector3 goal, OvSpot spot, bool outOfTrench, string why)
	{
		Vector3 me = base.transform.position;
		_rpMet.Clear();   // V4: a new way up
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
					OvSkip(spot, $"it would need a way up from here, and a way up from here already met ground at the game's 8 m dig limit or something it can't damage ({why})", normal: true);
					return;
				}
			}
		}
		// F12 (John 11-21 23:30Z, silver only: "no reason to be carving a way up from under the silver ... from the outside
		// wall of the trench"): a way up inside the pit is cut only when its goal is farther from the vein than its start and
		// its line (flat) never comes within 0.5 m of a part's box; else it isn't cut - the goal is reached by the way out on
		// the trench's outer wall nearest it (R4: cut there when there is none near, else the walk takes the one there is).
		// Getting out (outOfTrench) and the R4 way out's own cut aren't held to it (they go straight out from the floor).
		if (!outOfTrench && !_woActive && OvIsSilver(_vp) && !OvRampOuterOk(me, goal, out string ow))
		{
			if (spot != null)
			{
				spot.RampTried = true;
			}
			OvSpot ns = OvNearest(goal);
			bool cut = ns != null && OvWayOutNeeded(ns);
			if (CompanionsPlugin.LogNormal) LogInfo($"No way up cut toward {MHR_Mover.Fmt(goal)} ({why}): {ow} - silver: only from the trench's outer wall; {(cut ? $"cutting the way out at trench spot {ns.Idx + 1}" : "the way out there is walked")}");
			if (cut)
			{
				OvWayOutHere(ns, null, "a way up on silver only from the trench's outer wall");
			}
			return;
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
		_rpFell = 0f;   // Y2
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
		if (_rpFell > 0f)
		{
			// Y2 (John 11-25, log e891dcd2: Gunter fell 7.6 m then 15 m in a fight and kept the way up's old goal 26 m
			// above): a fall of more than a step ends the way up, by each kind's own give-up; getting out and no longer in
			// the trench = it is out, it leaves as normal (OvOutTick -> OvLeave)
			float fell = _rpFell;
			_rpFell = 0f;
			if (_rpOut && !OvInTrench(me, out _))
			{
				_rpActive = false;
				OvDigOff();
				_ai?.StopMoving();
				if (CompanionsPlugin.LogNormal) LogInfo($"The way up ended: it fell {fell:F1} m and is no longer in the trench (feet at {me.y:F1})");
				return;
			}
			OvRampFail($"it fell {fell:F1} m (feet at {me.y:F1})");
			return;
		}
		if (_dgActive)
		{
			OvDigTick();
			if (!_dgActive)
			{
				_rpSwings++;
				_rpNoProg = _dgLastProg ? 0 : _rpNoProg + 1;
				if (_dgDamage)
				{
					// M3 (John, 2026-11-20: keep working on the way out as long as they are doing damage): a swing that
					// lowered the ground or damaged a rock starts the 60 s "no higher" clock again; 3 swings in a row that do
					// nothing still end it
					_rpBestAt = Time.time;
				}
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
			if (OvWalkGaveUp(dt, _rpSaved.Top))
			{
				_rpTried.Add(_rpSaved);
				if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get up the ramp at {MHR_Mover.Fmt(_rpSaved.Bottom)} (the walk gave up) - cutting its own");
				_rpSaved = null;
				_rpStart = me;
				_rpBestY = me.y;
				_rpBestAt = Time.time;
				_moveTimer = 0f;
				return;
			}
			OvWalk(_rpSaved.Top, 0.5f, false, dt);
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
		bool wall = ga > me.y + OvStepUp || (against && !atLimit);
		// R1.1 / R1.2 (the radar): not a ground step - the map's squares in front decide at once, before the 1 s headway
		// test below (which stays for a clear or unknown map answer; other companions are never on the map, so G6 still
		// goes round them there). What the game's swing line meets first decides what is done.
		if (ga <= me.y + OvStepUp && OvMapWallAhead(_rpGoal, out byte mk, out Collider mc, out Vector3 mat) == 1)
		{
			byte hk = OvMapKind(_vp, _vp.Map, mc);
			if (hk == MkFixed)
			{
				OvRampWorldStop(mc.name, mc);
				return;
			}
			if (OvRampVeinStop(mc))
			{
				return;
			}
			if (hk == MkBuilt)
			{
				// R1.2 (John, 2026-11-21): a player-built piece - no swing (companions never damage builds), this way up gives up
				_rpBad.Add(_rpStart);
				OvRampFail($"the step in front is \"{mc.name}\", a built piece (no swing at builds)");
				return;
			}
			OvRampStrike(mc, mat, $"the map reads {OvMapKindName(mk)} in front");
			return;
		}
		// G6: not when what it is up against is another companion (G1 goes round it)
		if (!wall && _rpHwTime >= OvRampHeadway && !OvOtherAhead(me, _rpGoal))
		{
			// Z1 (test 27: Gunter walked 2 x 60 s into the vein's rock - the ground height is the terrain's only, so a rock,
			// the vein or a chamber in front reads as flat ground): up against something (1 s without getting 0.3 m
			// closer), the first thing on its swing line toward the goal decides
			Collider fc = OvFirstOnSwing(OvSwingFrom() + dir, out Vector3 fat);
			if (fc != null)
			{
				if (OvIsWorldSolid(fc))
				{
					OvRampWorldStop(fc.name, fc);
					return;
				}
				if (OvRampVeinStop(fc))
				{
					return;
				}
				// the vein's rock, the ground, or a thing (the dig's H1 hands a thing to the Wood / Stone flow): a swing at
				// the point it was hit, the rock itself (not its box)
				OvRampStrike(fc, fat, $"the ground in front reads {ga - me.y:F1} m above its feet");
				return;
			}
		}
		if (wall)
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
			// Z1: an object that can't be damaged first on the swing line (or the step inside a known one's box): no swing,
			// this way up gives up now
			Collider sc = OvFirstOnSwing(ahead, out _);
			if (OvIsWorldSolid(sc))
			{
				OvRampWorldStop(sc.name, sc);
				return;
			}
			if (OvRampVeinStop(sc))
			{
				return;
			}
			if (_vp != null && OvInBlocker(_vp, ahead, 0f))
			{
				OvRampWorldStop(OvBlockerAt(_vp, ahead), null);
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

	// Y1: its pickaxe can damage this rock by the game's rules - tier at least the rock's and the rock's damage modifiers
	// let a pickaxe hurt it (DD3's test, OvDigTick)
	private bool OvPickHurts(GameObject rk)
	{
		ItemDrop.ItemData pk = rk != null ? FindBestTool(2) : null;
		return pk != null && pk.m_shared.m_toolTier >= GetMinToolTier(rk) && MHR_Drops.ToolHurts(rk, axe: false);
	}

	// Z1: the way up meets an object that can't be damaged - its box is kept (X1), no new way up from where this one began
	// this lap, and this one gives up
	// R3a.1 (John, 2026-11-18: a way out must never go through the vein): the first thing on the swing line is this vein's
	// own rock - no swing, this way up gives up (not noted as a blocker); plain boulders are still carved through
	private bool OvRampVeinStop(Collider c)
	{
		MineRock5 m = c != null ? c.GetComponentInParent<MineRock5>() : null;
		bool other = m != null && OvSameOre(_vp, m);   // H2: another vein of this ore - never swung at either
		if (m == null || _vp == null || (!_vp.Rocks.Contains(m) && !other))
		{
			return false;
		}
		_rpBad.Add(_rpStart);
		OvRampFail(other ? $"the step in front is \"{c.name}\", another vein of this ore (never swung at)" : $"the step in front is the vein's own rock \"{c.name}\" (a way up never goes through the vein)");
		return true;
	}

	// Z1 / R1.2: a swing at the first thing on the swing line where it was hit (the rock itself, not its box)
	private void OvRampStrike(Collider fc, Vector3 fat, string why)
	{
		if (Flat(fat, _rpStepPt) > 0.5f)
		{
			_rpStepPt = fat;
			_rpNoProg = 0;
		}
		_rpHwTime = 0f;
		_ai?.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"The way up: up against \"{fc.name}\" at {MHR_Mover.Fmt(fat)} ({why}) - swinging at it");
		OvDigBegin(fat, null, ramp: true);
		_dgStrike = true;
	}

	private void OvRampWorldStop(string name, Collider c)
	{
		if (c != null && _vp != null)
		{
			OvNoteBlocker(_vp, c);
		}
		_rpBad.Add(_rpStart);
		OvRampFail($"the step in front is blocked by \"{name}\" (it can't be damaged)");
	}

	private static string OvBlockerAt(OvPlan p, Vector3 pt)
	{
		for (int i = 0; i < p.Blockers.Count; i++)
		{
			Bounds b = p.Blockers[i];
			if (pt.x >= b.min.x && pt.x <= b.max.x && pt.z >= b.min.z && pt.z <= b.max.z)
			{
				return i < p.BlockerNames.Count ? p.BlockerNames[i] : "?";
			}
		}
		return p.BlockerNames.Count > 0 ? p.BlockerNames[0] : "?";
	}

	// Y2: CompanionFallDamagePatch - it landed more than a step lower than the ground it last stood on. Z2 (John 11-25, log
	// 20dd55fd: a 1.8 m walk down to a saved ramp's bottom and a 1 m step as a way up began each used a getting-out try):
	// it counts only when it landed more than a step below the lowest point the way up had to go - where it began, or the
	// bottom of the saved ramp it is using
	internal void OnFell(float drop, float landY)
	{
		if (!_rpActive || drop <= OvStepUp)
		{
			return;
		}
		float lowest = _rpSaved != null ? Mathf.Min(_rpStart.y, _rpSaved.Bottom.y) : _rpStart.y;
		if (landY < lowest - OvStepUp)
		{
			_rpFell = Mathf.Max(_rpFell, drop);
		}
	}

	// Z3 (a) (John 11-25, log 20dd55fd): a walk couldn't reach the bottom of a saved way out (matched by its bottom within
	// 0.1 m) - the second miss drops it from the vein's ramp list (saved)
	internal static void OvWayOutMissed(Vector3 bottom, string who)
	{
		foreach (OvPlan p in s_ovPlans)
		{
			if (p == null)
			{
				continue;
			}
			for (int i = 0; i < p.Ramps.Count; i++)
			{
				OvRamp r = p.Ramps[i];
				if ((r.Bottom - bottom).sqrMagnitude > 0.01f)
				{
					continue;
				}
				r.Misses++;
				if (r.Misses >= OvRampMissMax)
				{
					p.Ramps.RemoveAt(i);
					OvSave(p);
					if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo($"[Gather|{who}] The way out at {MHR_Mover.Fmt(r.Bottom)} -> {MHR_Mover.Fmt(r.Top)} is dropped: its bottom couldn't be reached twice (Z3)");
				}
				return;
			}
		}
	}

	private const int OvRampMissMax = 2;   // Z3 (a)

	private void OvRampFinish(string how)
	{
		Vector3 me = base.transform.position;
		float rise = me.y - _rpStart.y;
		_rpActive = false;
		OvDigOff();
		_ai?.StopMoving();
		if (_vp != null && _rpSaved == null && rise >= OvRampSave)
		{
			// G3 (John 11-22, replaces W2's join of either end within 5 m): a saved ramp changes in only two ways -
			// (a) made deeper: this ramp's top within 5 m (flat) and 0.7 m (height) of its bottom - only the bottom moves;
			// (b) made higher: this ramp's bottom within 5 m and 0.7 m of its top - only the top moves. Else a new ramp
			OvRamp same = null;
			bool deeper = false;
			foreach (OvRamp r in _vp.Ramps)
			{
				if (Flat(me, r.Bottom) <= OvRampTopNear && Mathf.Abs(me.y - r.Bottom.y) <= OvRampSameY)
				{
					same = r;
					deeper = true;
					break;
				}
				if (Flat(_rpStart, r.Top) <= OvRampTopNear && Mathf.Abs(_rpStart.y - r.Top.y) <= OvRampSameY)
				{
					same = r;
					break;
				}
			}
			if (same != null)
			{
				if (deeper && _rpStart.y < same.Bottom.y)
				{
					same.Bottom = _rpStart;
				}
				if (!deeper && me.y > same.Top.y)
				{
					same.Top = me;
				}
				same.PathState = 0;   // H4: checked on the map again
				if (CompanionsPlugin.LogNormal) LogInfo($"The ramp at {MHR_Mover.Fmt(same.Bottom)} now goes to {MHR_Mover.Fmt(same.Top)} ({(deeper ? "made deeper" : "made higher")})");
				OvSave(_vp);   // F3
			}
			else
			{
				// Z3 (b) (John 11-25, log 20dd55fd: a way out whose bottom sat on a ledge 10 m above the floor): a new ramp is
				// saved only if its bottom is no more than a step above the trench floor (the ground at the trench spot
				// nearest its bottom)
				OvSpot fs = OvNearestIn(_vp, _rpStart);
				float overFloor = fs != null ? _rpStart.y - GroundAt(fs.P) : 0f;
				if (overFloor > OvStepUp)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"Not saved as a way out: its bottom {MHR_Mover.Fmt(_rpStart)} is {overFloor:F1} m above the trench floor at spot {fs.Idx + 1} (Z3)");
				}
				else
				{
					_vp.Ramps.Add(new OvRamp { Bottom = _rpStart, Top = me, Need = _rpNeed });
					OvSave(_vp);   // F3
					if (CompanionsPlugin.LogNormal) LogInfo($"A ramp for everyone on this vein: {MHR_Mover.Fmt(_rpStart)} up to {MHR_Mover.Fmt(me)} ({rise:F1} m; {_vp.Ramps.Count} on this vein)");
				}
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
		if (_exTry && _rpThing != null)
		{
			// Z2: a way out cut for a thing - the try is counted as below (E2 / M3), then the thing gets its try from here
			_exTry = false;
			if (_vp != null)
			{
				OvExitTry(_rpStart);
				if (_vp.ExitBy == this)
				{
					_vp.ExitBy = null;
				}
			}
		}
		if (_rpThing != null && _rpThingUp)
		{
			// K8: the way up after the base dig gave up - in the trench, a way out first (Z2, once per try); else the thing
			// waits 60 s, any companion may try it again then
			GameObject t = _rpThing;
			int tm = _rpThingMode;
			string tt = _rpThingType;
			OvSpot ts = _rpThingSpot;
			OvRampThingDrop();
			if (OvThingWayOut(t, tm, tt, ts, "the way up beside it gave up"))
			{
				return;
			}
			OvPitLeftNote(t);
			return;
		}
		if (_rpThing != null)
		{
			OvRampThingBack();   // Y6d: the thing gets its try from here (V5's clock)
			return;
		}
		if (_rpPart != null)
		{
			OvPartUnclaim(_rpPart);   // build 2 (P5): the walk to the part comes next (RampTried is set); F9: its place
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
			// E2: the try is counted (ExitAt); the next one comes from OvExitTick (R4: the next round's check). M3: counted
			// again now, so its 60 s runs from when the try ended, not from when it began
			_exTry = false;
			if (_vp != null)
			{
				OvExitTry(_rpStart);
			}
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
		if (OvPitFirst() || OvWayOutFirst())
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

	// FF1 (John, test 14: "anything inside the outside walls of the trench needs to be cleared. That comes above everything
	// else besides defending themselves"): ONE scan per vein, kept in the shared plan - run by whichever companion on the
	// vein finds it due, every 1 s while the trench is being dug and every 10 s after; the others only read the list. In the
	// pit = a tree, log, stump, bush or rock (not this vein's; one the Wood / Stone job takes, so never a dungeon piece that
	// can't be broken) whose game box is inside the trench ring or within 2 m (flat) of a trench spot (the outer wall); no
	// height rule.
	private void OvPitScan(OvPlan p)
	{
		float now = Time.time;
		if (now < p.PitScanAt)
		{
			return;
		}
		p.PitScanAt = now + (p.Done || OvIsCopper(p) ? OvPitScanAfter : OvPitScanTrench);   // CU2
		p.PitThings.Clear();
		p.PitModes.Clear();
		p.PitTypes.Clear();
		if (p.Spots.Count == 0)
		{
			return;
		}
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		// P1: the pit's ground first (the same scan, the same companion)
		OvPitBuild(p);
		float y0 = float.MaxValue, y1 = float.MinValue;
		foreach (OvSpot sp in p.Spots)
		{
			float g = GroundAt(sp.P);
			y0 = Mathf.Min(y0, g);
			y1 = Mathf.Max(y1, Mathf.Max(g, sp.P.y));
		}
		y1 = Mathf.Max(y1, p.Box.max.y);
		// P2: the whole pit (P1's cells) plus 2 m, the whole column over it (a standing tree's trunk starts at the ground),
		// from 1 m under the lowest trench floor
		float x0 = p.PitX0 - 0.5f, x1 = p.PitX1 + 0.5f, z0 = p.PitZ0 - 0.5f, z1 = p.PitZ1 + 0.5f;
		Vector3 c0 = new Vector3((x0 + x1) * 0.5f, (y0 - 1f + y1 + 2f) * 0.5f, (z0 + z1) * 0.5f);
		Vector3 half = new Vector3((x1 - x0) * 0.5f + OvThingRadius, (y1 + 2f - (y0 - 1f)) * 0.5f, (z1 - z0) * 0.5f + OvThingRadius);
		int k = Physics.OverlapBoxNonAlloc(c0, half, s_ovPitBuf, Quaternion.identity, s_ovThingMask, QueryTriggerInteraction.Ignore);
		HashSet<int> seen = new HashSet<int>();
		for (int i = 0; i < k; i++)
		{
			Collider col = s_ovPitBuf[i];
			if (col == null || col.GetComponentInParent<Character>() != null)
			{
				continue;
			}
			GameObject root = MHR_Drops.Root(col);
			if (root == null || !seen.Add(root.GetInstanceID()))
			{
				continue;
			}
			GameObject go = OvThingOf(col, out int mode, out string type);
			string off = null;
			if (go == null)
			{
				off = OvThingWhyNot(p, col, root);
			}
			else if (!OvThingBox(go, out Bounds b))
			{
				off = "it has no solid collider";
			}
			else if (!OvInPit(p, b))
			{
				off = "it is not on the pit's ground";
			}
			// P2: once per thing per vein - on the list, or why not (the vein's own deposits are not logged)
			if (off != OvThisVein && p.ScanSaid.Add(root.GetInstanceID()))
			{
				if (p.ScanSaid.Count > 2000)
				{
					p.ScanSaid.Clear();
				}
				if (CompanionsPlugin.LogNormal) LogInfo($"Pit scan: \"{MHR_Drops.Tooltip(root)}\" ({root.name}) at {MHR_Mover.Fmt(root.transform.position)}, drops {MHR_Drops.DropList(root)} - " + (off == null ? $"on the list ({MHR_Drops.KindName(MHR_Drops.KindOf(root))}, the {(mode == 1 ? "axe" : "pickaxe")})" : "left off: " + off));
			}
			if (off != null)
			{
				continue;
			}
			p.PitThings.Add(go);
			p.PitModes.Add(mode);
			p.PitTypes.Add(type);
		}
	}

	private const string OvThisVein = "this vein";

	// P2: why OvThingOf left a thing off (for the scan's log)
	private string OvThingWhyNot(OvPlan p, Collider c, GameObject root)
	{
		MineRock5 m5 = c.GetComponentInParent<MineRock5>();
		if (m5 != null)
		{
			return p.Rocks.Contains(m5) ? OvThisVein : "a big rock (MineRock5) - carved through where it is in the way, never taken";
		}
		if (!root.activeInHierarchy)
		{
			return "not active";
		}
		string built = BuiltWhy(root);
		if (built != null)
		{
			return built;
		}
		if (MHR_Drops.KindOf(root) == MHR_Drops.Ore)
		{
			return "it drops ore";
		}
		if (p.PitIgnore.Contains(root.GetInstanceID()))
		{
			return "ignored on this vein (neither the axe nor the pickaxe hurt it)";
		}
		return "neither the axe nor the pickaxe can hurt it (the game's damage rules)";
	}

	// P1: the cell key of a point (1 m cells, centred on whole metres)
	private static long OvPitKey(float x, float z)
	{
		long ix = Mathf.RoundToInt(x), iz = Mathf.RoundToInt(z);
		return (ix << 32) ^ (iz & 0xffffffffL);
	}

	// K7 (John, 2026-11-19 19:47Z: the pit is everything inside the trench's outer wall, nothing more): the pit's ground -
	// every 1 m cell inside the ring and every trench spot's cell. (P1's spread out into joined dug ground is removed: it
	// pulled the hillside in through their own ways out and base digs in test 18.)
	private void OvPitBuild(OvPlan p)
	{
		p.PitCells.Clear();
		float rx0 = float.MaxValue, rx1 = float.MinValue, rz0 = float.MaxValue, rz1 = float.MinValue;
		foreach (OvSpot sp in p.Spots)
		{
			rx0 = Mathf.Min(rx0, sp.P.x);
			rx1 = Mathf.Max(rx1, sp.P.x);
			rz0 = Mathf.Min(rz0, sp.P.z);
			rz1 = Mathf.Max(rz1, sp.P.z);
		}
		if (p.Poly.Count >= 3)
		{
			for (int x = Mathf.FloorToInt(rx0); x <= Mathf.CeilToInt(rx1); x++)
			{
				for (int z = Mathf.FloorToInt(rz0); z <= Mathf.CeilToInt(rz1); z++)
				{
					if (OvInside(p.Poly, new Vector2(x, z)))
					{
						OvPitAdd(p, x, z);
					}
				}
			}
		}
		float px0 = rx0, px1 = rx1, pz0 = rz0, pz1 = rz1;
		foreach (OvSpot sp in p.Spots)
		{
			int sx = Mathf.RoundToInt(sp.P.x), sz = Mathf.RoundToInt(sp.P.z);
			OvPitAdd(p, sx, sz);
			px0 = Mathf.Min(px0, sx);
			px1 = Mathf.Max(px1, sx);
			pz0 = Mathf.Min(pz0, sz);
			pz1 = Mathf.Max(pz1, sz);
		}
		p.PitX0 = px0;
		p.PitX1 = px1;
		p.PitZ0 = pz0;
		p.PitZ1 = pz1;
		if (p.PitCells.Count != p.PitCellsSaid)
		{
			p.PitCellsSaid = p.PitCells.Count;
			if (CompanionsPlugin.LogNormal) LogInfo($"The pit of \"{p.Name}\": {p.PitCells.Count} m2 of ground (inside the trench ring and the trench), x {px0:F0} to {px1:F0}, z {pz0:F0} to {pz1:F0}");
		}
	}

	private static void OvPitAdd(OvPlan p, int x, int z)
	{
		p.PitCells.Add(OvPitKey(x, z));
	}

	// P1 / P2: on the pit's ground - the box's centre or any of its bottom corners (flat) in a pit cell. Before the pit's
	// ground is first worked out: inside the ring or within 2 m of a trench spot (the old test).
	private static bool OvInPit(OvPlan p, Bounds b)
	{
		if (p.PitCells.Count > 0)
		{
			return p.PitCells.Contains(OvPitKey(b.center.x, b.center.z)) || p.PitCells.Contains(OvPitKey(b.min.x, b.min.z)) || p.PitCells.Contains(OvPitKey(b.max.x, b.min.z)) || p.PitCells.Contains(OvPitKey(b.max.x, b.max.z)) || p.PitCells.Contains(OvPitKey(b.min.x, b.max.z));
		}
		if (p.Poly.Count >= 3)
		{
			if (OvInside(p.Poly, new Vector2(b.center.x, b.center.z)) || OvInside(p.Poly, new Vector2(b.min.x, b.min.z)) || OvInside(p.Poly, new Vector2(b.max.x, b.min.z)) || OvInside(p.Poly, new Vector2(b.max.x, b.max.z)) || OvInside(p.Poly, new Vector2(b.min.x, b.max.z)))
			{
				return true;
			}
		}
		foreach (OvSpot sp in p.Spots)
		{
			Vector3 np = b.ClosestPoint(new Vector3(sp.P.x, b.center.y, sp.P.z));
			if (Flat(np, sp.P) <= OvThingRadius)
			{
				return true;
			}
		}
		return false;
	}

	// FF1: the nearest thing on the vein's pit list that no other companion has claimed, isn't cooling down (60 s after
	// someone couldn't clear it) and that it has a tool for - taken the Wood / Stone job's way. With stopWork (a trench dig
	// or a clearing round under way) that work is ended first. True = taken.
	private bool OvPitFirst(bool stopWork = false)
	{
		OvPlan p = _vp;
		if (p == null || p.Spots.Count == 0)
		{
			return false;
		}
		// K1 (John 11-28 01:54Z / 01:59Z: "Clear the trees and rocks around the area"): copper's pit list is worked too (CU6
		// removed)
		OvPitScan(p);
		Vector3 me = base.transform.position;
		GameObject best = null;
		string bestType = null;
		int bestMode = 0;
		Bounds bestBox = default;
		float bd = float.MaxValue;
		for (int i = 0; i < p.PitThings.Count; i++)
		{
			GameObject go = p.PitThings[i];
			if (go == null || !go.activeInHierarchy)
			{
				continue;
			}
			int id = go.GetInstanceID();
			if (OvPitCooling(id) || (s_claimedTargets.Contains(id) && id != _claimedTargetId) || p.PitIgnore.Contains(id))
			{
				continue;   // cooling down, another companion is on it, or ignored (GG1)
			}
			int mode = p.PitModes[i];
			string type = p.PitTypes[i];
			if (mode == 1 && type == "Destructible" && p.PitAxeNo.Contains(id))
			{
				mode = 2;   // GG1: the axe did nothing - the pickaxe now (the list is from the last scan)
				type = go.GetComponent<MineRock>() != null ? "MineRock" : "Destructible";
			}
			if (FindBestTool(mode) == null || !OvThingBox(go, out Bounds b))
			{
				continue;   // no tool for it: nothing to do about it here
			}
			float d = Vector3.Distance(me, b.ClosestPoint(me));
			if (d < bd)
			{
				bd = d;
				best = go;
				bestType = type;
				bestMode = mode;
				bestBox = b;
			}
		}
		if (best == null)
		{
			return false;
		}
		if (stopWork)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Stopping its {(_clFor != null ? "round on a part" : _drSpot != null ? "dirt dig" : "trench dig")} - \"{best.name}\" is in the pit");
			OvDigOff();
			OvPartRelease();
		}
		OvRelease();
		return OvTake(best, bestMode, bestType, OvNearest(bestBox.center), canSkip: false, "In the pit (things first)") == 1;
	}

	// FF1: between swings of a trench dig or a clearing round (not mid-swing, not a ladder round, not a bush it is already
	// digging out): the pit list, at most once a second (a read of the shared list; the scan itself is the vein's)
	private bool OvPitBetween()
	{
		if (Time.time < _pitAt || (_humanoid != null && _humanoid.InAttack()) || _clLadder || _dgBushOn)
		{
			return false;
		}
		_pitAt = Time.time + 1f;
		return OvPitFirst(stopWork: true);
	}

	private bool OvPitCooling(int id)
	{
		return _vp != null && _vp.PitCool.TryGetValue(id, out float until) && Time.time < until;
	}

	// FF1 (John: "If something gets skipped it needs to have a cool down not a permanent skip"): a thing in the pit someone
	// couldn't clear isn't taken again by any companion for 60 s, then it is back on the list
	private void OvPitLeftNote(GameObject thing)
	{
		if (_vp == null || thing == null)
		{
			return;
		}
		_vp.PitCool[thing.GetInstanceID()] = Time.time + OvPitCool;
		if (_twId == thing.GetInstanceID())
		{
			_twId = 0;   // Z2: this try is over - after the wait, a new try may cut a way out again
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't clear \"{thing.name}\" at {MHR_Mover.Fmt(thing.transform.position)} - any companion may try it again in {OvPitCool:F0} s");
	}

	// P2 (John, 2026-11-19: trees, plants and rocks drop down as the dirt under them is removed): the thing handed to the
	// Wood / Stone flow sits more than 0.8 m above its feet (the axe swings flat, about 1 m up) and the walk gets no closer
	// (3 s without getting 0.3 m closer): back in the vein, it stands at the bottom of the pit (P3) and digs the ground at
	// the thing's base from there (P4) until the thing has come down to 0.8 m above that place, then it is handed back to
	// the Wood / Stone flow (the same try). Once per try. True = started.
	private const float OvThingLow = 0.8f;

	private bool OvHandLedge()
	{
		if (_hdRamped || _state != HarvestState.Moving || _ovHand == null || _vp == null || !OvThingBox(_ovHand, out Bounds b))
		{
			return false;
		}
		Vector3 me = base.transform.position;
		if (b.min.y <= me.y + OvThingLow)
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
		// Z3 (test 27: a pine's box corner put the dig 8 m from its trunk, a log's put it on the trench floor under its
		// overhang): the ground at its root - where the trunk or log itself is
		Vector3 bp = _ovHand.transform.position;
		bp.y = GroundAt(bp);
		OvClear c = OvC();
		if (OvStandFor(c, bp, null, out Vector3 stand, out string why) != 0)
		{
			_hdRamped = true;   // no pit ground within its swing of the base: the walk goes on, the hand clock decides
			if (CompanionsPlugin.LogNormal) LogInfo($"\"{_ovHand.name}\" is {b.min.y - me.y:F1} m up and the walk gets no closer; its base can't be dug from the pit: {why}");
			return false;
		}
		float need = b.min.y - (stand.y + OvThingLow);
		if (need <= 0f)
		{
			_hdRamped = true;   // from the bottom of the pit it is within reach: only the walk; the hand clock decides
			return false;
		}
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
		_drSpot = new OvDirt { Key = 0L, P = bp, Goal = Mathf.Max(bp.y - need, OvFloor(bp) + OvDigGain) };
		_drForThing = true;
		_drStand = stand;
		_drWalk = true;
		_moveTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo($"\"{name}\" sits {b.min.y - stand.y:F1} m above the bottom of the pit beside it - digging the ground at its base {MHR_Mover.Fmt(bp)} down to {_drSpot.Goal:F2} from {MHR_Mover.Fmt(stand)} so it comes down into reach");
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
		if (OvExitTriedNear(sp.P))
		{
			return false;
		}
		OvDigOff();
		OvPartRelease();
		OvRelease();
		OvWayOutHere(sp, $"In a fight, stuck {enemy.y - me.y:F1} m below the enemy with no way out of the trench", "a way out to the enemy");
		_woCombat = true;
		return true;
	}

	// ---------- Z4: a vein walk ends on arrival or when the mover is done (John: "Timers break things") ----------
	private bool _wgArrived;      // Z4: the mover said "there" on the last step of this walk

	// one step of a vein walk (WalkGo), noting whether the mover says it is there
	private void OvWalk(Vector3 to, float stop, bool run, float dt)
	{
		_wgArrived = WalkGo(to, stop, run, dt);
	}

	// Z4 (replaces the 10 s walk clocks): the walk is over without getting there when the mover gave up (its stuck steps,
	// measured on its route's length) or the mover stopped at the goal while the job's own "there" test still fails (it
	// can't get any closer). The 0.5 s from the walk's start keeps a give-up left from the last walk from counting.
	private bool OvWalkGaveUp(float dt, Vector3 toward)
	{
		WalkTimer(dt, toward);
		return _moveTimer > 0.5f && (MHR_Mover.GaveUp(_ai) || _wgArrived);
	}

	// ---------- Z2: can't reach something while in the trench - a way out (John, 2026-11-20) ----------
	// John: "If they cannot reach a location and are in a trench they should carve a way out."
	private int _twId;            // Z2: the thing this try cut its way out for (once per try)

	// in the trench: the ground outside the trench spot nearest it is 1.5 x its height or more above its feet (Y2)
	private bool OvInTrench(out OvSpot ns)
	{
		Vector3 me = base.transform.position;
		ns = _vp != null && _vp.Spots.Count > 0 ? OvNearest(me) : null;
		float h = _character != null ? _character.GetHeight() : 1.8f;
		return ns != null && OvWallTop(ns) - me.y >= OvRampRatio * h;
	}

	// the trench spot to cut from: nearest the target (within 15 m of it), else nearest itself; not one whose line out
	// crosses an object that can't be damaged (Z5) nor one tried in the last 60 s (R4). Null = none.
	private OvSpot OvWayOutToward(Vector3 target, OvSpot ns)
	{
		OvSpot sp = OvNearest(target);
		if (sp == null || Flat(sp.P, base.transform.position) > OvWayNear || OvOutBlocked(sp))
		{
			sp = ns;
		}
		if (sp == null || OvOutBlocked(sp) || OvExitTriedNear(sp.P))
		{
			return null;
		}
		return sp;
	}

	private bool OvOtherCutting()
	{
		OvPlan p = _vp;
		return p.ExitBy != null && p.ExitBy != this && p.ExitBy._vp == p && OvActive().Contains(p.ExitBy);
	}

	// Z2: a thing in the pit (or the ground at its base) it couldn't get to, while it is in the trench: it keeps the thing,
	// goes back to the vein and cuts a way out at the trench spot nearest it (R4's walk and ramp), then the thing gets its
	// try again from there (OvRampThingBack, "Up beside it"). Once per try: the next time this thing fails, it waits its
	// 60 s (FF1). False = no way out now (not in the trench, another companion is cutting one, a try was made near there
	// in the last 60 s): the caller's 60 s wait.
	private bool OvThingWayOut(GameObject t, int mode, string type, OvSpot spot, string why)
	{
		if (t == null || !t.activeInHierarchy || _vp == null || _vFor == null || _vRock == null || _woActive || _rpActive)
		{
			return false;
		}
		int id = t.GetInstanceID();
		if (_twId == id)
		{
			return false;   // this try had its way out: the 60 s wait now (OvPitLeftNote ends the try)
		}
		if (!OvInTrench(out OvSpot ns) || OvOtherCutting())
		{
			return false;
		}
		OvSpot sp = OvWayOutToward(t.transform.position, ns);
		if (sp == null)
		{
			return false;
		}
		_twId = id;
		OvDigOff();
		OvPartRelease();
		OvRelease();
		OvRampThingDrop();
		// back to the vein holding the thing (OvHandLedge's way)
		_ovHand = null;
		_ovHandSpot = null;
		_ovHandMode = 0;
		_rpThing = t;
		_rpThingMode = mode;
		_rpThingType = type;
		_rpThingSpot = spot;
		_rpThingBase = float.MaxValue;
		_rpThingUp = false;
		_rpThingClaim = id;
		_target = _vFor;
		_targetType = "MineRock5";
		ClaimTarget();
		s_claimedTargets.Add(_rpThingClaim);
		_ai?.SetFollowTarget(null);
		_ai?.StopMoving();
		_state = HarvestState.Vein;
		OvWayOutHere(sp, $"Couldn't get to \"{t.name}\" from the trench ({why})", "a way out to reach it");
		return true;
	}

	// Z2: the mover tried every saved way out on a walk on this vein and it is still stuck below where it is going (the
	// fight's Y5, for work): the job cuts one now - for a thing it walks to, OvThingWayOut; for the trench, a part or the
	// dirt, at the trench spot nearest where it is going. True = cutting.
	internal bool WorkWayOut(Vector3 goal)
	{
		OvPlan p = _vp;
		if (p == null || _vFor == null || _woActive || _rpActive || _ovOut || p.Spots.Count == 0)
		{
			return false;
		}
		if (_ovHandMode != 0 && _ovHand != null && (_state == HarvestState.Moving || _state == HarvestState.Attacking))
		{
			return OvThingWayOut(_ovHand, _ovHandMode, _targetType, _ovHandSpot, $"stuck {goal.y - base.transform.position.y:F1} m below it with no way out to use");
		}
		if (_state != HarvestState.Vein || !OvInTrench(out OvSpot ns) || OvOtherCutting())
		{
			return false;
		}
		OvSpot sp = OvWayOutToward(goal, ns);
		if (sp == null)
		{
			return false;
		}
		if (_drForThing && _rpThing != null)
		{
			// the walk to the ground at a thing's base: the thing keeps its try (OvThingWayOut's way)
			GameObject t = _rpThing;
			if (t == null || _twId == t.GetInstanceID())
			{
				return false;   // this try had its way out: the walk gives up as before (OvDrFail, the 60 s wait)
			}
			int m = _rpThingMode;
			string ty = _rpThingType;
			OvSpot ts = _rpThingSpot;
			_drForThing = false;
			OvDrOff();
			if (OvThingWayOut(t, m, ty, ts, "stuck below the ground at its base with no way out to use"))
			{
				return true;
			}
			OvRampThingDrop();
			OvPitLeftNote(t);
			return false;
		}
		OvDigOff();
		OvPartRelease();
		OvRelease();
		if (_drSpot != null)
		{
			OvDrOff();
		}
		OvWayOutHere(sp, $"Stuck {goal.y - base.transform.position.y:F1} m below where it is going with no way out of the trench to use", "a way out first");
		return true;
	}

	// Z5 (test 27: a way out's goal was inside the burial chamber's box): the line out from a trench spot (to the wall
	// top's 8 m) crosses the box of an object that can't be damaged
	private bool OvOutBlocked(OvSpot q)
	{
		if (_vp == null || _vp.Blockers.Count == 0)
		{
			return false;
		}
		Vector3 d = q.P - _vp.Center;
		d.y = 0f;
		d = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.forward;
		float rad = _character != null ? _character.GetRadius() : 0.4f;
		for (float k = 0.5f; k <= OvWallOut; k += 0.5f)
		{
			Vector3 pt = q.P + d * k;
			pt.y = GroundAt(pt);
			if (OvInBlocker(_vp, pt, rad))
			{
				return true;
			}
		}
		return false;
	}

	// ---------- V5: a thing handed to the Wood / Stone flow ----------
	// John: no hit on it in 10 s - it is left; once it has been hit, 30 s without a hit (only to catch it stuck). Left =
	// the flow ends (ResetToIdle) and OvResume keeps it out of the picks and skips the spot this lap, as for a bush.
	private const float OvHandFirstHit = 10f;

	private const float OvHandNextHit = 30f;

	private float _hdHp;          // GG1: the thing's health at the last hit that lowered it (-1 = the game keeps none)

	private int _hdNoDmg;         // GG1: hits in a row that didn't lower it

	private float _hdJudgeAt;     // GG1: a hit to judge 0.5 s after it landed (the game's damage is in by then)

	private bool _hdNoCool;       // GG1: left for the next tool, not for a cooldown

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
			_hdJudgeAt = _lastHitAt + 0.5f;
		}
		// GG1 (John: "go up to it and hit it with an axe, if dmg is done, clear it, if not, switch to a pickaxe"): each hit is
		// judged by the game's own health value 0.5 s after it lands; 3 hits in a row that don't lower it = this tool does
		// nothing to it. A thing that drops nothing known: the axe -> the pickaxe next, the pickaxe -> ignored on this vein.
		// A tree, log, stump or rock (known drops): left with the 60 s cooldown.
		if (_hdJudgeAt > 0f && Time.time >= _hdJudgeAt)
		{
			_hdJudgeAt = -1f;
			float hp = MHR_Drops.Health(_ovHand);
			if (hp >= 0f && _hdHp >= 0f && hp >= _hdHp - 0.01f)
			{
				_hdNoDmg++;
			}
			else
			{
				_hdNoDmg = 0;
				_hdHp = hp;
			}
			if (_hdNoDmg >= OvNoProgSwings)
			{
				GameObject t = _ovHand;
				string w = $"{OvNoProgSwings} hits with its {(_ovHandMode == 1 ? "axe" : "pickaxe")} didn't lower its health ({hp:F0})";
				if (MHR_Drops.KindOf(t) == MHR_Drops.Unknown)
				{
					_hdNoCool = true;
					if (_ovHandMode == 1)
					{
						OvPitAxeNo(t, w);
					}
					else
					{
						OvPitIgnore(t, w + ", and the axe did nothing either");
					}
				}
				else if (CompanionsPlugin.LogNormal)
				{
					LogInfo($"\"{MHR_Drops.Tooltip(t)}\" ({t.name}): {w} - leaving it");
				}
				ResetToIdle();
				return true;
			}
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
		_dgDugBefore = GroundAt(hitPoint);   // Q1: the ground there before the game's dig (this runs before it digs)
		OvMapDirty(_vp?.Map, hitPoint, OvMapEventNear);   // R0.3: the pit map's squares within 2 m are read again
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
			// R3b.4 (John: no timers on walks): the mover's own progress-based give-up decides, not 60 s (after a moment, so
			// a give-up left over from the walk before doesn't count)
			if (Time.time - _leashSince > 0.5f && MHR_Mover.GaveUp(_ai))
			{
				_leashBack = false;
				SayGaveUp(GiveUpWhy.OutOfReach, "couldn't get back to the vein (the walk back gave up)");
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
		// R3b.4 (test of edb0a24: Comp's route to spot 67, 17 m along the trench, ran 102 m round outside the pit and the
		// leash made it leave): walking to a work point inside this vein's pit is not wandering off
		if (_bdGoalOn && Time.time - _bdWalkAt < 0.5f && OvInPitPt(_bdGoal))
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
		if (CompanionsPlugin.LogNormal) LogInfo($"{dist:F0} m outside the vein while working it (at {MHR_Mover.Fmt(me)}) - coming back");
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

	// DD8 (test 13: three pickaxes broke and they stood following): with no working pickaxe on a vein it takes the axe
	// things in the pit near it (the pit pick, 8 m - pickaxe things have no tool and are skipped) through the Wood job;
	// nothing left = out of the vein, and MHR_ToolKeeper has it follow (a repair station within 30 m comes first there)
	private void OvAxeTick()
	{
		if (_rpActive)
		{
			OvRampFail("no working pickaxe");
		}
		_woActive = false;
		OvDigOff();
		OvPartRelease();
		if (!_ovAxeDone && FindBestTool(1) != null && OvPitFirst())
		{
			return;
		}
		// L6 (John 11-22 20:17Z: "he says once that his pickaxe is broken, then helps with the pit's loose items and stone"):
		// no other working pickaxe in its bag - it says so once, then the pit's loose items (OvLooseTick, no tool needed);
		// nothing there it can take = out of the vein, as before. A working pickaxe in the bag (the 1 s check) sends it back
		if (!_ovNoPickSaid)
		{
			_ovNoPickSaid = true;
			if (CompanionsPlugin.LogNormal) LogInfo("No working pickaxe left in its bag - the pit's loose items and stone only (L6)");
			_talk?.SayProblem("tool:none:3", string.Format(ModLocalization.Loc("hc_speech_no_working_tool"), ModLocalization.Loc("hc_tool_pickaxe")));
		}
		OvClear lc = OvC();
		if (lc != null && OvLooseTick(lc, standBy: false))
		{
			return;
		}
		if (!_ovAxeDone && CompanionsPlugin.LogNormal) LogInfo("No working pickaxe and nothing near on the vein for its axe");
		_ovAxeDone = true;
		_noToolRock = _vFor;
		_noToolNext = Time.time + 1f;
		OvLeaveOut(base.transform.position, "no working pickaxe");
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
