using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97 (plan v3, approved by John 2026-11-15 02:09, reviews/1.5.97-player-way-mining-plan-v3.md): a split vein outdoors
// mined the way a player does, built on the mod's own walking, climbing, door, water and fight handling.
//  S1  The plan (1.5.97 list v4: HarvestVeinPlan.cs): one for everyone on the vein. A grid over the seen pieces (+3 m), a
//      rubber band (convex outline) round every square where a seen piece stands, the trench line the squares 2 out from
//      the band (1 m outside it), traced round. Buildings, water it would swim in, wards and no-build areas are left out
//      of the line (it splits there); trees on it are marked. Each spot's depth: the bottom of the lowest seen piece
//      within 1 m, not below 31 m; across a gap, the depth of the nearest spot that has one. Made again when a new piece
//      is seen (at most once every 2 s).
//  S2  A start: on its own the nearest spot not done, toward the lower ground from there. With others on the vein: a spot
//      nobody is on (the farthest from the others on arriving and on meeting), going away from the nearest other; with
//      nothing free it helps on the nearest spot not done.
//  S3  Dig the spot ahead: before each swing a copy of the game's own swing (Attack.DoMeleeAttack) picks which way to face
//      (9 facings) so the swing meets dirt first. Done at its depth, at the water line, a ward / no-build area, "rock in
//      front" (no facing meets dirt first), or 3 swings without progress.
//  S4  The next spot: walked to with the normal walking code toward the spot just dug, on its own level.
//  S5  Can't move forward (6 s): one look ahead. Rock: mine it. Dirt: dig it or jump it. Tree or log: the axe. You or a
//      companion: wait 3 s, then the walking code's side step. A creature: the fight rules (the controller). A door: the
//      walking code opens it. A building or deep water: the line splits there. Anything else: turn round once per line,
//      then a new start (3 at most).
//  S6  Getting out: under 1.5 m walk out (3 s); else rest to full and the general climb (ClimbOutNow, 3 tries); too deep
//      for it: cut steps (HarvestPit.cs, the ladder head room and dirt-first checks on the way out).
//  S7  Clear it so it pops: the game's own support test (VeinScan) - the dirt holding each held part dug away where it can
//      be reached dirt-first, else that part mined; nothing held: one break pops the rest. 2 rounds.
//  S7b The mining lap round the line for whatever's left, 2 laps; then give up (S9: twice on one vein -> it waits for you).
public partial class HarvestController
{
	private sealed class TSpot
	{
		public Vector3 P;

		public Vector3 Out;

		public float Surface;

		public float Depth;

		public bool Done;

		public bool Tree;

		public int Line;

		public int Idx;

		// item B: who is walking to it or digging it, and who has given up on it (it's left for the others)
		public HarvestController Holder;

		public HashSet<int> Gave;

		// part 1 B1: its depth is the lowest the game lets this ground go (8 m under its first height), not the rock's base
		public bool AtFloor;

		// part 2 M2: how low this lap takes it (never under Depth), and done at that, not yet at Depth
		public float LapDepth;

		public bool LapOnly;
	}

	private sealed class TLine
	{
		public readonly List<TSpot> S = new List<TSpot>();

		public bool Loop;
	}

	private enum TFirst
	{
		None,
		Dirt,
		Vein,
		Other
	}

	private enum TFor
	{
		None,
		Dig,
		BlockDirt,
		BlockObj,
		PopDig,
		PopMine,
		Lap,
		ExitMine,
		Obstacle
	}

	private enum TBlock
	{
		Dirt,
		Rock,
		Tree,
		Person,
		Creature,
		Door,
		Built,
		Unknown
	}

	private const int TPhasePlan = 0;

	private const int TPhaseTrench = 1;

	private const int TPhasePop = 2;

	private const int TPhaseLap = 3;

	private const int TSubPick = 0;

	private const int TSubStart = 1;

	private const int TSubDig = 2;

	private const int TSubBlock = 3;

	// part 2 M1 (John 2026-11-17: floor at 29.5, half a metre of water; the water level is 30): a dig swing only at
	// ground this high or higher - one swing takes up to 0.5 m, so the ground ends between about 29.3 and 29.75
	private const float TWater = 29.75f;

	// part 2 M1: the floor where the water stops the digging
	private const float TWaterFloor = 29.5f;

	// part 2 M1: a shallow vein - the trench stops this far under the lowest part of the whole vein
	private const float TUnderVein = 1.0f;

	// part 2 M2: a lap lowers a spot by at most this from its ground when the lap began; at most this many laps; a lap
	// that lowered no spot by TLapMinGain ends the trench
	private const float TLapStep = 2.0f;

	private const int TMaxLaps = 5;

	private const float TLapMinGain = 0.3f;

	// part 2 M4/M5: a part whose top is less than this above the floor at its place is under the ground
	private const float TBuriedTop = 0.7f;

	private const int TGridMax = 80;

	private const int TCellsPerFrame = 200;

	private const int TMaxLines = 16;

	// close enough to dig the spot ahead: within this flat of it
	private const float TReach = 1.4f;

	// the swing's reach for the rock: its start within this of the rock
	private const float TMineReach = 1.6f;

	private const float TStepTime = 6f;

	private const float TStartTime = 10f;

	private const int TNoProgSwings = 3;

	private const int TMaxNewStarts = 3;

	private const int TMaxBlocksPerSpot = 10;

	private const int TMaxChops = 40;

	private const float THead = 2.2f;

	// in a hole: the place it's heading for is this much or more above its feet
	private const float TExitDepth = 1.5f;

	// list v2 H6: after a vein the watch for "the mover gave up below the outer rim" ends this far outside the vein's box
	private const float XAfterClear = 6f;

	private const int XMaxClimbs = 1;

	// test review B3: a next spot farther off than this is walked to under the start rules
	private const float TFarSpot = 3f;

	private static readonly float[] s_tOffsets = { 0f, 15f, -15f, 30f, -30f, 45f, -45f, 60f, -60f };

	private static int s_tArcMask;

	private static int s_tLookMask;

	private int _tPhase;

	private int _tSub;

	// the job's own clock: it only runs while it isn't catching its breath
	private float _tClock;

	private readonly HashSet<Collider> _tVisible = new HashSet<Collider>();

	private bool _tNoneVisible;

	private Vector3 _tCenter;

	// the plan's grid
	private Vector3 _gMin;

	private float _gCell;

	private int _gNx;

	private int _gNz;

	private float _gTop;

	private float _gBottom;

	private float[] _gGround;

	private bool[] _gRock;

	private int _gIdx;

	// the run along a line: where it started, which way, turned round yet
	private int _tLine = -1;

	private int _tRunStart = -1;

	private int _tDir = 1;

	private bool _tTurned;

	private TSpot _tQ;

	private int _tNewStarts;

	private bool _tStartExited;

	private float _tWalkSince;

	private int _tItemNoProg;

	private bool _tAnyWard;

	private bool _tAnyCantDig;

	private float _tDigMinY = TWater;

	// this swing: what it was for, may it dig, what's measured for progress
	private TFor _tSwingFor;

	private bool _tDigOK;

	private Vector3 _tQPoint;

	private float _tQBefore;

	private Vector3 _tQ2Point;

	private float _tQ2Before;

	private bool _vTerrainHit;

	private bool _tDigMeasured;

	// the swing check (a copy of the game's arc): what it said for this swing, and 3 misses in a row turn it off
	private bool _tPredTerrain;

	private int _tPredMiss;

	private bool _tCheckOff;

	private float _tFaceUntil;

	private Vector3 _tFaceDir;

	private Vector3 _tFaceWant;

	private Vector3 _tFaceFrom;

	// S5
	private TBlock _bKind;

	private GameObject _bGo;

	private Collider _bCol;

	private Vector3 _bPoint;

	private Vector3 _bDir;

	private float _bSince;

	private float _bHealth;

	private int _bNoProg;

	private bool _tChopping;

	private int _tChops;

	// S7
	private int _pRound;

	private Collider _pPart;

	// list v2 V6: the vein of the job being cleared now, and the last one logged
	private MineRock5 _pCur;

	private MineRock5 _pCurSaid;

	private MineRock5 TPopRock()
	{
		MineRock5 best = null;
		float by = float.MaxValue;
		foreach (VPart p in _vParts)
		{
			if (p.Rock == null || TBuried(p))
			{
				continue;
			}
			float y = p.Rock.transform.position.y;
			if (y < by)
			{
				by = y;
				best = p.Rock;
			}
		}
		return best;
	}

	private int _pStage;

	private bool _pHasSpot;

	private VSpot _pSpot;

	private readonly HashSet<Collider> _pLeft = new HashSet<Collider>();

	private float _pSince;

	// S7b
	private int _lLap;

	private int _lIdx;

	// S6
	private bool _xActive;

	// where this run along the line started (test review B2: found again on a new plan)
	private Vector3 _tRunP;

	private bool _tWasTrench;

	private int _xStage;

	private Vector3 _xRim;

	private float _xRimY;

	private Vector3 _xOut;

	private float _xSince;

	private int _xTries;

	private bool _xMineTried;

	private int _xEquipTries;

	private bool _xPendingAfter;

	// item D: the step directions tried in this getting-out (one bit each of the 16), and the one more pick after mining
	private int _xDirsTried;

	private bool _xFinalPick;

	// the swing check's last answer: what the swing would meet first, and where the last chosen facing expected dirt
	private GameObject _tPredGo;

	private Collider _tPredCol;

	private Vector3 _tPredPt;

	// S9 (L4): give-ups per vein with nothing broken in between
	private readonly Dictionary<int, int> _vGiveUps = new Dictionary<int, int>();

	private readonly Dictionary<int, string> _vGiveUpWhy = new Dictionary<int, string>();

	private string _lastGiveUpText = "";

	private void TReset()
	{
		_tPhase = TPhasePlan;
		_tSub = TSubPick;
		_tVisible.Clear();
		_tMine.Clear();
		_tPlanVer = 0;
		_tWasTrench = false;
		_tPickFar = false;
		_tPickWhy = "";
		_tStartLimit = TStartTime;
		_gGround = null;
		_gFoot = null;
		_gRock = null;
		_gIdx = 0;
		_tLine = -1;
		_tRunStart = -1;
		_tDir = 1;
		_tTurned = false;
		_tQ = null;
		_tNewStarts = 0;
		_tStartExited = false;
		_tAnyWard = false;
		_tAnyCantDig = false;
		_tDigMinY = TWater;
		_tSwingFor = TFor.None;
		_tDigOK = false;
		_tPredTerrain = false;
		_tPredMiss = 0;
		_tCheckOff = false;
		_tChopping = false;
		// part 1 T5/T7: the count of things cleared, the skip list and the one side step are per vein - a new plan
		// (or planning again from where it stands) doesn't reset them
		int veinId = _target != null ? _target.GetInstanceID() : 0;
		if (veinId != _tChopsFor)
		{
			_tChopsFor = veinId;
			_tChops = 0;
			_oSkip.Clear();
			_oNoAxe.Clear();
			_tSideHas = false;
			_pUnder.Clear();
		}
		_oActive = false;
		_oGo = null;
		_tSplitDone = false;
		_tSplitWasTrench = false;
		_pCur = null;
		_pCurSaid = null;
		_tSideUntil = 0f;
		_tAxeWaiting = false;
		_pRound = 0;
		_pPart = null;
		_pLeft.Clear();
		_lLap = 0;
		_lIdx = 0;
		_xActive = false;
		TBeginItem();
	}

	private void TBeginItem()
	{
		_tItemNoProg = 0;
		_tWalkSince = _tClock;
	}

	private static float Flat(Vector3 a, Vector3 b)
	{
		float dx = a.x - b.x, dz = a.z - b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private static Vector3 FlatNorm(Vector3 v, Vector3 fallback)
	{
		v.y = 0f;
		return v.sqrMagnitude > 0.0001f ? v.normalized : fallback;
	}

	// ---------- the rock you can see ----------
	private bool PartVisible(VPart p)
	{
		if (p.C == null)
		{
			return false;
		}
		int nx = Mathf.Clamp(Mathf.CeilToInt(p.Half.x * 2f / 0.5f) + 1, 2, 12);
		int nz = Mathf.Clamp(Mathf.CeilToInt(p.Half.z * 2f / 0.5f) + 1, 2, 12);
		float top = p.Center.y + p.Half.y + 0.5f;
		float len = p.Half.y * 2f + 1f;
		for (int ix = 0; ix < nx; ix++)
		{
			for (int iz = 0; iz < nz; iz++)
			{
				float x = p.Center.x - p.Half.x + p.Half.x * 2f * ix / (nx - 1);
				float z = p.Center.z - p.Half.z + p.Half.z * 2f * iz / (nz - 1);
				if (p.C.Raycast(new Ray(new Vector3(x, top, z), Vector3.down), out RaycastHit hit, len) && hit.point.y > GroundAt(hit.point) + 0.05f)
				{
					return true;
				}
			}
		}
		return false;
	}

	// the nearest hit on the given vein parts along a ray (terrain and everything else ignored)
	private static bool RayParts(Vector3 o, Vector3 d, float len, IEnumerable<Collider> parts, out Vector3 point, out float dist)
	{
		point = Vector3.zero;
		dist = float.MaxValue;
		Ray ray = new Ray(o, d);
		foreach (Collider c in parts)
		{
			if (c == null || !c.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (c.Raycast(ray, out RaycastHit hit, len) && hit.distance < dist)
			{
				dist = hit.distance;
				point = hit.point;
			}
		}
		return dist < float.MaxValue;
	}

	// ---------- S1: the plan ----------
	private void TPlanBegin()
	{
		bool wasTrench = _tPhase == TPhaseTrench || _tSplitWasTrench;
		_tSplitWasTrench = false;
		// list v2 V2/V3: deposits touching the vein join the job first (a whole one gets its splitting hit)
		if (VJoinStep())
		{
			_tSplitWasTrench = wasTrench;
			return;
		}
		_tWasTrench = wasTrench;   // B2: the planner's own place is kept only from the trench
		_vp.Planner = this;
		_vp.PlannerAt = Time.time;
		_vp.NextReplan = Time.time + VReplanGap;
		TSeenToVisible();
		_tNoneVisible = _tVisible.Count == 0;
		Bounds vb = default(Bounds);
		bool any = false;
		Vector3 sum = Vector3.zero;
		int n = 0;
		foreach (VPart p in _vParts)
		{
			if (!_tNoneVisible && !_tVisible.Contains(p.C))
			{
				continue;
			}
			if (_tNoneVisible)
			{
				_tVisible.Add(p.C);
				_vp.Seen.Add(p.Idx);
			}
			Bounds b = new Bounds(p.Center, p.Half * 2f);
			if (!any)
			{
				vb = b;
				any = true;
			}
			else
			{
				vb.Encapsulate(b);
			}
			sum += p.Center;
			n++;
		}
		_tPlanSeen = _vp.Seen.Count;
		_tCenter = n > 0 ? sum / n : _vUnion.center;
		_tCenter.y = GroundAt(_tCenter);
		if (!any)
		{
			vb = _vUnion;
		}
		float sx = vb.size.x + 6f, sz = vb.size.z + 6f;
		_gCell = Mathf.Max(0.5f, (Mathf.Max(sx, sz) + 1f) / (TGridMax - 1));
		// the squares sit on the same places from plan to plan (so a spot dug in one plan is the same spot in the next)
		_gMin = new Vector3(Mathf.Floor((vb.min.x - 3f) / _gCell) * _gCell, 0f, Mathf.Floor((vb.min.z - 3f) / _gCell) * _gCell);
		_gNx = Mathf.Clamp(Mathf.CeilToInt((vb.max.x + 3f - _gMin.x) / _gCell) + 1, 2, TGridMax);
		_gNz = Mathf.Clamp(Mathf.CeilToInt((vb.max.z + 3f - _gMin.z) / _gCell) + 1, 2, TGridMax);
		_gTop = vb.max.y + 1f;
		_gBottom = vb.min.y - 1f;
		_gGround = new float[_gNx * _gNz];
		_gFoot = new bool[_gNx * _gNz];
		_gRock = new bool[_gNx * _gNz];
		_gIdx = 0;
		_tPhase = TPhasePlan;
	}

	private Vector3 GCellPos(int i)
	{
		int ix = i % _gNx, iz = i / _gNx;
		return new Vector3(_gMin.x + ix * _gCell, 0f, _gMin.z + iz * _gCell);
	}

	private int GCellAt(Vector3 p)
	{
		int ix = Mathf.RoundToInt((p.x - _gMin.x) / _gCell), iz = Mathf.RoundToInt((p.z - _gMin.z) / _gCell);
		if (ix < 0 || iz < 0 || ix >= _gNx || iz >= _gNz)
		{
			return -1;
		}
		return iz * _gNx + ix;
	}

	private bool GRockAt(Vector3 p)
	{
		if (_gRock == null)
		{
			return false;
		}
		int i = GCellAt(p);
		return i >= 0 && _gRock[i];
	}

	// a slice of the grid each frame (at most 200 squares): the ground, and the squares where a seen piece stands
	private void TPlanTick()
	{
		if (_vp == null)
		{
			return;
		}
		if (_vp.Planner != this || _gGround == null)
		{
			// someone else is making the plan: wait for it, then work to it; nobody is: make it
			_ai.StopMoving();
			if (VPlannerBusy())
			{
				return;
			}
			// list v2 V3: after a splitting hit the plan is made again (the vein it became joins)
			if (!_tSplitDone && _vp.Version > 0 && (_vp.TrenchDone || _vp.RingFixed || _vp.Seen.Count <= _vp.PlannedSeen))
			{
				TPlanAdopt("joined the others");
				return;
			}
			_tSplitDone = false;
			TPlanBegin();
			return;
		}
		_vp.PlannerAt = Time.time;
		int total = _gNx * _gNz;
		int end = Mathf.Min(total, _gIdx + TCellsPerFrame);
		float len = _gTop - _gBottom;
		for (; _gIdx < end; _gIdx++)
		{
			Vector3 c = GCellPos(_gIdx);
			_gGround[_gIdx] = GroundAt(c);
			_gFoot[_gIdx] = RayParts(new Vector3(c.x, _gTop, c.z), Vector3.down, len, _tVisible, out _, out _);
		}
		if (_gIdx >= total)
		{
			TPlanFinish();
		}
	}

	private void TPlanFinish()
	{
		int total = _gNx * _gNz;
		// item A: the rubber band round every square where a seen piece stands; everything inside it counts as rock
		int corners = TBandFill();
		int[] cheb = new int[total];
		int rockCount = 0;
		for (int i = 0; i < total; i++)
		{
			cheb[i] = 99;
		}
		for (int i = 0; i < total; i++)
		{
			if (!_gRock[i])
			{
				continue;
			}
			rockCount++;
			int ix = i % _gNx, iz = i / _gNx;
			for (int dz = -2; dz <= 2; dz++)
			{
				for (int dx = -2; dx <= 2; dx++)
				{
					int jx = ix + dx, jz = iz + dz;
					if (jx < 0 || jz < 0 || jx >= _gNx || jz >= _gNz)
					{
						continue;
					}
					int j = jz * _gNx + jx;
					cheb[j] = Mathf.Min(cheb[j], Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)));
				}
			}
		}
		// the line: the squares 2 out from the rock. Left out: water it would swim in, wards / no-build, buildings
		bool[] usable = new bool[total];
		bool[] tree = new bool[total];
		int nWater = 0, nWard = 0, nBuilt = 0, nTree = 0, nRing = 0;
		int lookMask = LookMask();
		Collider[] buf = new Collider[32];
		for (int i = 0; i < total; i++)
		{
			if (_gRock[i] || cheb[i] != 2)
			{
				continue;
			}
			nRing++;
			Vector3 c = GCellPos(i);
			c.y = _gGround[i];
			if (_character != null && MHR_Water.WouldSwim(_character, c))
			{
				nWater++;
				continue;
			}
			if (!PrivateArea.CheckAccess(c, 0f, flash: false) || Location.IsInsideNoBuildLocation(c))
			{
				nWard++;
				continue;
			}
			bool built = false, isTree = false;
			int k = Physics.OverlapBoxNonAlloc(c + Vector3.up * 1.0f, new Vector3(_gCell * 0.5f, 0.9f, _gCell * 0.5f), buf, Quaternion.identity, lookMask, QueryTriggerInteraction.Ignore);
			for (int j = 0; j < k; j++)
			{
				Collider col = buf[j];
				if (col == null || col.gameObject.layer == s_vTerrain || col.GetComponentInParent<Character>() != null)
				{
					continue;
				}
				if (VInGroup(col.GetComponentInParent<MineRock5>()))
				{
					continue;
				}
				if (col.GetComponentInParent<WearNTear>() != null)
				{
					built = true;
				}
				else if (col.GetComponentInParent<TreeBase>() != null || col.GetComponentInParent<TreeLog>() != null)
				{
					isTree = true;
				}
			}
			if (built)
			{
				nBuilt++;
				continue;
			}
			usable[i] = true;
			tree[i] = isTree;
			if (isTree)
			{
				nTree++;
			}
		}
		// traced into lines: from an end (fewest neighbours) square by square, straight neighbours first
		bool[] seen = new bool[total];
		List<List<int>> chains = new List<List<int>>();
		int[] nb4x = { 1, -1, 0, 0 }, nb4z = { 0, 0, 1, -1 }, nbdx = { 1, 1, -1, -1 }, nbdz = { 1, -1, 1, -1 };
		while (true)
		{
			int start = -1, fewest = 99;
			for (int i = 0; i < total; i++)
			{
				if (!usable[i] || seen[i])
				{
					continue;
				}
				int nb = 0;
				int ix = i % _gNx, iz = i / _gNx;
				for (int dz = -1; dz <= 1; dz++)
				{
					for (int dx = -1; dx <= 1; dx++)
					{
						int jx = ix + dx, jz = iz + dz;
						if ((dx != 0 || dz != 0) && jx >= 0 && jz >= 0 && jx < _gNx && jz < _gNz && usable[jz * _gNx + jx] && !seen[jz * _gNx + jx])
						{
							nb++;
						}
					}
				}
				if (nb < fewest)
				{
					fewest = nb;
					start = i;
				}
			}
			if (start < 0)
			{
				break;
			}
			List<int> chain = new List<int> { start };
			seen[start] = true;
			int cur = start;
			while (true)
			{
				int cx = cur % _gNx, cz = cur / _gNx, next = -1;
				for (int pass = 0; pass < 2 && next < 0; pass++)
				{
					for (int k = 0; k < 4 && next < 0; k++)
					{
						int jx = cx + (pass == 0 ? nb4x[k] : nbdx[k]), jz = cz + (pass == 0 ? nb4z[k] : nbdz[k]);
						if (jx < 0 || jz < 0 || jx >= _gNx || jz >= _gNz)
						{
							continue;
						}
						int j = jz * _gNx + jx;
						if (usable[j] && !seen[j])
						{
							next = j;
						}
					}
				}
				if (next < 0)
				{
					break;
				}
				seen[next] = true;
				chain.Add(next);
				cur = next;
			}
			chains.Add(chain);
		}
		chains.Sort((a, b) => b.Count.CompareTo(a.Count));
		int dropped = 0;
		if (chains.Count > TMaxLines)
		{
			dropped = chains.Count - TMaxLines;
			chains.RemoveRange(TMaxLines, dropped);
		}
		int step = Mathf.Max(1, Mathf.RoundToInt(1.0f / _gCell));
		List<TLine> lines = new List<TLine>();
		int spots = 0, floored = 0;
		float deepest = 0f;
		// part 2 M1: 1 m under the lowest part of the whole vein (the game's own list of its parts)
		float under = (_vLowest < float.MaxValue ? _vLowest : float.MinValue) - TUnderVein;
		foreach (List<int> chain in chains)
		{
			TLine line = new TLine();
			int a0 = chain[0], a1 = chain[chain.Count - 1];
			line.Loop = chain.Count >= 4 && Mathf.Abs(a0 % _gNx - a1 % _gNx) <= 1 && Mathf.Abs(a0 / _gNx - a1 / _gNx) <= 1;
			for (int ci = 0; ci < chain.Count; ci += step)
			{
				int i = chain[ci];
				TSpot s = new TSpot();
				s.P = GCellPos(i);
				s.P.y = _gGround[i];
				s.Surface = s.P.y;
				s.Tree = tree[i];
				s.Line = lines.Count;
				s.Idx = line.S.Count;
				// out: away from the nearest rock square
				int ix = i % _gNx, iz = i / _gNx;
				float bestD = float.MaxValue;
				Vector3 nearRock = s.P;
				for (int dz = -2; dz <= 2; dz++)
				{
					for (int dx = -2; dx <= 2; dx++)
					{
						int jx = ix + dx, jz = iz + dz;
						if (jx < 0 || jz < 0 || jx >= _gNx || jz >= _gNz || !_gRock[jz * _gNx + jx])
						{
							continue;
						}
						float d = dx * dx + dz * dz;
						if (d < bestD)
						{
							bestD = d;
							nearRock = GCellPos(jz * _gNx + jx);
						}
					}
				}
				s.Out = FlatNorm(s.P - nearRock, FlatNorm(s.P - _tCenter, Vector3.forward));
				// part 2 M1: one floor for the whole vein - the lowest the game lets this ground go or half a metre of
				// water, whichever is higher; a shallow vein: 1 m under its lowest part. Not from the pieces seen.
				float floor = TFloorHard(s.P);
				s.AtFloor = floor >= under;
				s.Depth = Mathf.Min(s.Surface, Mathf.Max(floor, under));
				if (s.AtFloor)
				{
					floored++;
				}
				deepest = Mathf.Max(deepest, s.Surface - s.Depth);
				TSetLapDepth(s);
				line.S.Add(s);
			}
			spots += line.S.Count;
			lines.Add(line);
		}
		foreach (TLine line in lines)
		{
			foreach (TSpot s in line.S)
			{
				// what was closed in the plan before stays closed (a building, deep water, ground that won't go lower, a
				// companion's own give-ups); a spot at its depth reads as done from the ground
				foreach (VClosed c in _vp.Closed)
				{
					if (Flat(c.P, s.P) > 0.6f)
					{
						continue;
					}
					if (c.Who == 0)
					{
						s.Done = true;
					}
					else
					{
						(s.Gave ?? (s.Gave = new HashSet<int>())).Add(c.Who);
					}
				}
			}
		}
		_gGround = null;
		_gFoot = null;
		// the one plan for everyone on this vein
		_vp.Lines = lines;
		_vp.GMin = _gMin;
		_vp.GCell = _gCell;
		_vp.GNx = _gNx;
		_vp.GNz = _gNz;
		_vp.GRock = _gRock;
		_vp.Center = _tCenter;
		_vp.NoneVisible = _tNoneVisible;
		_vp.PlannedSeen = _tPlanSeen;
		// a piece seen while this plan was being made asks for the next one
		_vp.NeedOutside = _vp.Seen.Count > _tPlanSeen;
		_vp.Version++;
		_vp.Planner = null;
		_vp.NextReplan = Time.time + VReplanGap;
		if (CompanionsPlugin.LogNormal) LogInfo($"Plan {_vp.Version} for \"{_target.name}\": {(_tNoneVisible ? "no part shows above the ground (using all " + _vParts.Count + ")" : _tVisible.Count + " of " + _vParts.Count + " parts seen")}, {_vDirtCount} held by dirt; a {_gNx} x {_gNz} grid of {_gCell:F1} m squares, the band round them has {corners} corners and {rockCount} squares inside; {lines.Count} line(s), {spots} spots ({nRing} trench squares, left out: {nBuilt} built, {nWater} water, {nWard} ward or no-build; {nTree} with a tree{(dropped > 0 ? "; " + dropped + " short piece(s) dropped" : "")}), deepest spot {deepest:F1} m down, {floored} spot(s) at the floor (the 8 m the game allows, or the water at {TWaterFloor:F1}), the rest 1 m under the vein's lowest part ({_vLowest:F1}); lap {_vp.Lap} of at most {TMaxLaps}, {TLapStep:F0} m a lap{VJoinedText()}");
		if (CompanionsPlugin.LogDetailed)
		{
			for (int l = 0; l < lines.Count; l++)
			{
				TLine line = lines[l];
				Log($"  line {l + 1}: {line.S.Count} spots{(line.Loop ? ", all the way round" : "")}, from {MHR_Mover.Fmt(line.S[0].P)} to {MHR_Mover.Fmt(line.S[line.S.Count - 1].P)}");
			}
		}
		if (_tPickWhy.Length == 0)
		{
			_tPickWhy = "the plan grew";
		}
		TPlanAdopt("made it");
	}

	private int LookMask()
	{
		if (s_tLookMask == 0)
		{
			s_tLookMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
		}
		return s_tLookMask;
	}

	// ---------- each frame (VeinTick, when no swing is running) ----------
	private void TTick()
	{
		if (!_catchingBreath)
		{
			_tClock += Time.deltaTime;
		}
		_vSide = SideOf(base.transform.position);
		if (_vp != null)
		{
			_vp.Workers[this] = Time.time;
			_vp.LastWork = Time.time;
		}
		if (_xActive)
		{
			XTick();
			return;
		}
		// part 1 T4: a side step being carried out (the mover does it on the same goal)
		if (Time.time < _tSideUntil)
		{
			MHR_Mover.Go(_ai, Time.deltaTime, _tSideGoal, 0.05f, false, "Vein");
			return;
		}
		// part 1 T3: a thing in the way is cleared first, through a new plan too (T7)
		if (_oActive)
		{
			OTick();
			return;
		}
		if (TPlanSync())
		{
			return;
		}
		switch (_tPhase)
		{
		case TPhasePlan:
			TPlanTick();
			break;
		case TPhaseTrench:
			TTrenchTick();
			break;
		case TPhasePop:
			TPopTick();
			break;
		default:
			TLapTick();
			break;
		}
	}

	// out of breath (15 % + one swing): it stands until 60 % before walking on, like a player
	private bool TRest()
	{
		if (_stamina == null)
		{
			return false;
		}
		float max = Mathf.Max(1f, _stamina.MaxStamina);
		if (_catchingBreath)
		{
			if (_stamina.Stamina < (ModConfig.GatherResumeStamina?.Value ?? 0.6f) * max)
			{
				_ai.StopMoving();
				return true;
			}
			_catchingBreath = false;
			if (CompanionsPlugin.LogDetailed) Log($"Got its breath back ({_stamina.Stamina:F0}/{max:F0}) - back to work");
			return false;
		}
		if (_stamina.Stamina < 10f + (ModConfig.GatherRestStamina?.Value ?? 0.15f) * max)
		{
			_catchingBreath = true;
			_ai.StopMoving();
			if (CompanionsPlugin.LogDetailed) Log($"Catching its breath ({_stamina.Stamina:F0}/{max:F0} stamina) before walking on");
			return true;
		}
		return false;
	}

	// walking inside the trench: the normal walking code, the goal kept on its own level (never more than 1 m up, so the
	// walking code steps or hops but never starts a climb, which needs 1.5 m)
	private bool TWalkLevel(Vector3 goal, float dist)
	{
		Vector3 me = base.transform.position;
		goal.y = Mathf.Min(GroundAt(goal), me.y + 1.0f);
		return MHR_Mover.Go(_ai, Time.deltaTime, goal, dist, false, "Vein");
	}

	private Vector3 TOrigin()
	{
		ItemDrop.ItemData tool = ReflectionHelper.GetRightItem(_humanoid);
		float h = tool?.m_shared?.m_attack != null ? tool.m_shared.m_attack.m_attackHeight : 1f;
		return base.transform.position + Vector3.up * h;
	}

	// ---------- the swing check: the game's own arc (Attack.DoMeleeAttack), copied ----------
	// 31 rays top first (or the tool's own angle and step), each a sphere cast of the tool's width and range from the
	// tool's start point; each ray keeps its nearest thing (unless the tool hits through walls); friends and anything
	// behind are skipped; a pickaxe counts every vein part on its own. What comes first in that list decides the swing:
	// the ground first = it digs, anything else first = no dig.
	private TFirst TPredict(Vector3 feet, Vector3 fwd, out Vector3 point)
	{
		point = Vector3.zero;
		_tPredGo = null;
		_tPredCol = null;
		ItemDrop.ItemData tool = ReflectionHelper.GetRightItem(_humanoid);
		Attack a = tool?.m_shared?.m_attack;
		if (a == null)
		{
			return TFirst.None;
		}
		fwd = FlatNorm(fwd, base.transform.forward);
		Quaternion rot = Quaternion.LookRotation(fwd);
		Vector3 origin = feet;
		if (!string.IsNullOrEmpty(a.m_attackOriginJoint) && _character != null && _character.GetVisual() != null)
		{
			Transform j = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
			if (j != null)
			{
				origin = j.position + (feet - base.transform.position);
			}
		}
		Vector3 o = origin + Vector3.up * a.m_attackHeight + rot * Vector3.right * a.m_attackOffset;
		if (s_tArcMask == 0)
		{
			s_tArcMask = LookMask();   // the same layers as the game's Attack.m_attackMaskTerrain (private there)
		}
		float half = a.m_attackAngle / 2f, range = a.m_attackRange, w = a.m_attackRayWidth;
		List<GameObject> gos = new List<GameObject>();
		List<Collider> cols = new List<Collider>();
		List<Vector3> sums = new List<Vector3>();
		List<int> counts = new List<int>();
		for (float ang = -half; ang <= half; ang += 4f)
		{
			Quaternion q2 = a.m_attackType == Attack.AttackType.Horizontal ? Quaternion.Euler(0f, -ang, 0f) : Quaternion.Euler(ang, 0f, 0f);
			Vector3 d = rot * (q2 * Vector3.forward);
			RaycastHit[] hits = w > 0f ? Physics.SphereCastAll(o, w, d, Mathf.Max(0f, range - w), s_tArcMask, QueryTriggerInteraction.Ignore) : Physics.RaycastAll(o, d, range, s_tArcMask, QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
			foreach (RaycastHit h in hits)
			{
				if (h.collider == null || h.collider.gameObject == base.gameObject)
				{
					continue;
				}
				Vector3 pt = h.point;
				if (h.normal == -d && h.point == Vector3.zero)
				{
					pt = h.collider is MeshCollider ? o + d * range : h.collider.ClosestPoint(o);
				}
				if (a.m_attackAngle < 180f && Vector3.Dot(pt - o, fwd) <= 0f)
				{
					continue;
				}
				GameObject go = Projectile.FindHitObject(h.collider);
				if (go == null || go == base.gameObject)
				{
					continue;
				}
				Character ch = go.GetComponent<Character>();
				if (ch != null && !BaseAI.IsEnemy(_character, ch))
				{
					continue;   // a friend: the game's swing goes on past them
				}
				bool multi = a.m_pickaxeSpecial && (go.GetComponent<MineRock5>() != null || go.GetComponent<MineRock>() != null);
				int at = -1;
				for (int k = gos.Count - 1; k >= 0; k--)
				{
					if ((!multi && gos[k] == go) || (multi && cols[k] == h.collider))
					{
						at = k;
						break;
					}
				}
				if (at < 0)
				{
					gos.Add(go);
					cols.Add(h.collider);
					sums.Add(Vector3.zero);
					counts.Add(0);
					at = gos.Count - 1;
				}
				sums[at] += pt;
				counts[at]++;
				if (!a.m_hitThroughWalls)
				{
					break;
				}
			}
		}
		if (gos.Count == 0)
		{
			return TFirst.None;
		}
		// item A: a piece the swing check meets is seen
		if (_vRock != null && _vp != null)
		{
			for (int k = 0; k < gos.Count; k++)
			{
				if (VInGroup(gos[k].GetComponent<MineRock5>()))
				{
					TSee(cols[k], "the swing check met it");
				}
			}
		}
		point = sums[0] / Mathf.Max(1, counts[0]);
		_tPredGo = gos[0];
		_tPredCol = cols[0];
		if (gos[0].GetComponent<Heightmap>() != null)
		{
			return TFirst.Dirt;
		}
		if (VInGroup(gos[0].GetComponent<MineRock5>()))
		{
			return TFirst.Vein;
		}
		return TFirst.Other;
	}

	// which way to face for a swing that meets dirt first, near `want` (within `near` flat) and above `minY`: straight at
	// baseDir first, then 15, 30, 45, 60 degrees either side. With the check off (3 misses), straight at baseDir.
	private bool TPickFacing(Vector3 baseDir, Vector3 want, float near, float minY, out Vector3 facing, bool overRock = false)
	{
		baseDir = FlatNorm(baseDir, base.transform.forward);
		facing = baseDir;
		if (_tCheckOff)
		{
			return true;
		}
		Vector3 me = base.transform.position;
		// the same answer for half a second while it waits to swing (the check is 9 x 31 casts)
		if (Time.time < _tFaceUntil && Flat(want, _tFaceWant) < 0.05f && Flat(me, _tFaceFrom) < 0.1f)
		{
			facing = _tFaceDir;
			return true;
		}
		foreach (float off in s_tOffsets)
		{
			Vector3 d = Quaternion.Euler(0f, off, 0f) * baseDir;
			if (TPredict(me, d, out Vector3 pt) != TFirst.Dirt)
			{
				continue;
			}
			float g = GroundAt(pt);
			if (Flat(pt, want) <= near && g > minY + 0.05f && g >= TWater && (overRock || !GRockAt(pt)))
			{
				facing = d;
				_tFaceDir = d;
				_tFaceWant = want;
				_tFaceFrom = me;
				_tFaceUntil = Time.time + 0.5f;
				_tPredPt = pt;
				if (CompanionsPlugin.LogDetailed && off != 0f) Log($"Facing {off:F0} deg off straight so the swing meets dirt first (at {MHR_Mover.Fmt(pt)})");
				return true;
			}
		}
		return false;
	}

	// a dig swing straight along `facing`, level from the swing's start; the progress is measured at `at` (and under it)
	private void TDigSwing(Vector3 facing, Vector3 at, TFor why)
	{
		Vector3 me = base.transform.position;
		_tQPoint = at;
		_tDigMeasured = true;
		_tQBefore = GroundAt(at);
		_tQ2Point = me;
		_tQ2Before = GroundAt(me);
		_tSwingFor = why;
		_tDigOK = true;
		_tPredTerrain = !_tCheckOff;
		// the game turns a player straight to where they look as the swing starts (Attack.Start): the same here
		facing = FlatNorm(facing, base.transform.forward);
		base.transform.rotation = Quaternion.LookRotation(facing);
		VeinSwing(TOrigin() + facing * 1.2f, dig: true, -1L);
	}

	private void TRockSwing(Vector3 aim, TFor why)
	{
		_tSwingFor = why;
		_tDigOK = false;
		_tPredTerrain = false;
		Vector3 f = aim - base.transform.position;
		f.y = 0f;
		if (f.sqrMagnitude > 0.0001f)
		{
			base.transform.rotation = Quaternion.LookRotation(f.normalized);
		}
		VeinSwing(aim, dig: false, 0L);
	}

	// ---------- S2 / S3 / S4 / S5: the trench ----------
	private void TTrenchTick()
	{
		switch (_tSub)
		{
		case TSubPick:
			TStartPick();
			break;
		case TSubStart:
			TStartTick();
			break;
		case TSubBlock:
			TBlockTick();
			break;
		default:
			TDigTick();
			break;
		}
	}

	private TSpot TSpotAt(int line, int idx)
	{
		if (line < 0 || line >= _tLines.Count)
		{
			return null;
		}
		TLine l = _tLines[line];
		int n = l.S.Count;
		if (l.Loop)
		{
			idx = (idx % n + n) % n;
		}
		return idx >= 0 && idx < n ? l.S[idx] : null;
	}

	// the next spot not done from idx going dir (round and round on a loop, to the end on an open line)
	private TSpot TNextUndone(int line, int idx, int dir)
	{
		if (line < 0 || line >= _tLines.Count)
		{
			return null;
		}
		int n = _tLines[line].S.Count;
		for (int i = 1; i <= n; i++)
		{
			TSpot s = TSpotAt(line, idx + dir * i);
			if (s == null)
			{
				return null;
			}
			if (TOpen(s))
			{
				return s;
			}
		}
		return null;
	}

	// S2: a start. On its own: the nearest spot not done, then toward the lower ground. With other companions on the vein
	// (item B): a spot nobody is on - the one farthest from the others on arriving and on meeting one of them, else the
	// nearest - then along the ring away from the nearest other. Nothing free: it helps on the nearest spot not done.
	private void TStartPick()
	{
		Vector3 me = base.transform.position;
		List<HarvestController> others = TOthers();
		// test review B1: "far" is measured from the spot each other companion is on or walking to (not where it
		// stands); one with no spot yet isn't counted, and with none of them on a spot it's the nearest spot not done
		List<Vector3> otherSpots = new List<Vector3>();
		foreach (HarvestController o in others)
		{
			if (o._tPhase == TPhaseTrench && o._tQ != null)
			{
				otherSpots.Add(o._tQ.P);
			}
		}
		bool far = _tPickFar && otherSpots.Count > 0;
		string why = _tPickWhy;
		_tPickFar = false;
		_tPickWhy = "";
		TSpot best = null, bestHeld = null;
		float bd = float.MaxValue, bs = float.MinValue, bh = float.MaxValue;
		int open = 0, free = 0;
		foreach (TLine l in _tLines)
		{
			foreach (TSpot s in l.S)
			{
				if (!TOpen(s))
				{
					continue;
				}
				open++;
				float d = Flat(s.P, me);
				if (THeldBy(s) != null)
				{
					if (d < bh)
					{
						bh = d;
						bestHeld = s;
					}
					continue;
				}
				free++;
				if (far)
				{
					float near = float.MaxValue;
					foreach (Vector3 op in otherSpots)
					{
						near = Mathf.Min(near, Flat(s.P, op));
					}
					if (near > bs)
					{
						bs = near;
						best = s;
						bd = d;
					}
				}
				else if (d < bd)
				{
					bd = d;
					best = s;
				}
			}
		}
		bool helping = false;
		if (best == null && bestHeld != null)
		{
			best = bestHeld;
			bd = bh;
			helping = true;
		}
		if (best == null)
		{
			// part 1 T6: tree spots left for a companion with an axe who is still on the trench
			if (TAxeWait())
			{
				_ai.StopMoving();
				return;
			}
			// part 2 M2/M3: the next lap, or wait for a companion still on a spot, before the trench counts as over
			if (TLapNext())
			{
				return;
			}
			TTrenchDone("every spot is done", allDone: true);
			return;
		}
		_tLine = best.Line;
		_tRunStart = best.Idx;
		_tRunP = best.P;
		TTake(best);
		_tTurned = false;
		TSpot next = TSpotAt(_tLine, best.Idx + 1), prev = TSpotAt(_tLine, best.Idx - 1);
		if (next == best)
		{
			next = null;
		}
		if (prev == best)
		{
			prev = null;
		}
		string way;
		HarvestController nearOther = null;
		float no = float.MaxValue;
		foreach (HarvestController o in others)
		{
			float d = Flat(o.transform.position, best.P);
			if (d < no)
			{
				no = d;
				nearOther = o;
			}
		}
		if (next == null && prev == null)
		{
			_tDir = 1;
			way = "a single spot";
		}
		else if (next == null || prev == null)
		{
			_tDir = next == null ? -1 : 1;
			way = "the only way from the end of the line";
		}
		else if (nearOther != null)
		{
			// item B: along the ring, away from the nearest other companion
			Vector3 op = nearOther.transform.position;
			_tDir = Flat(prev.P, op) > Flat(next.P, op) ? -1 : 1;
			way = $"away from {nearOther.MyName}, {no:F1} m off";
		}
		else
		{
			float gn = GroundAt(next.P), gp = GroundAt(prev.P);
			_tDir = gp < gn - 0.01f ? -1 : 1;
			way = $"the ground is lower that way ({Mathf.Min(gn, gp):F1} against {Mathf.Max(gn, gp):F1})";
		}
		_tStartExited = false;
		// 10 s to get there, and 1 s more for every metre it is away (a start on the far side of the vein)
		_tStartLimit = TStartTime + bd;
		_tSub = TSubStart;
		TBeginItem();
		if (CompanionsPlugin.LogNormal) LogInfo($"Starting the trench at spot {best.Idx + 1}/{_tLines[_tLine].S.Count} of line {_tLine + 1} at {MHR_Mover.Fmt(best.P)} ({bd:F1} m away), going {(_tDir > 0 ? "one way" : "the other way")} - {way}");
		if (CompanionsPlugin.LogDetailed && others.Count > 0)
		{
			string pick = helping ? $"every spot not done has a companion on it - helping {THeldBy(best)?.MyName ?? "them"} on the nearest" : far ? $"the free spot farthest from the others' spots ({bs:F1} m from the nearest of them)" : "the nearest free spot";
			Log($"Start picked{(why.Length > 0 ? " (" + why + ")" : "")}: {pick}; {others.Count} other companion(s) on the vein, {open} spot(s) not done, {free} free");
		}
	}

	private void TStartTick()
	{
		TSpot q = _tQ;
		if (q == null || !TOpen(q))
		{
			_tSub = TSubPick;   // someone finished it on the way there
			return;
		}
		Vector3 me = base.transform.position;
		float g = GroundAt(q.P);
		// helping on a spot another companion is on: beside them (a swing's reach) is close enough
		if (Flat(me, q.P) <= (THeldBy(q) != null ? TReach : 1.0f))
		{
			_tSub = TSubDig;
			TBeginItem();
			return;
		}
		// list v2 H1/H2 (log ff6c8861: on a hillside the height of a spot 20 m away read as a 9 m pit): a plain walk by
		// the normal route first, the mover's own climb included. Its own getting-out only once the mover has given up on
		// this walk, and only up to the trench's outer rim beside it (XBeginOuter) - once per walk.
		if (!_tStartExited && _tClock - _tWalkSince > 2f && MHR_Mover.GaveUp(_ai))
		{
			_tStartExited = true;
			if (XBeginOuter("the walk to the next start found no way", _tCenter))
			{
				return;
			}
		}
		if (TRest())
		{
			return;
		}
		if (_tClock - _tWalkSince > _tStartLimit)
		{
			TSpotGave(q, $"couldn't get to it in {_tStartLimit:F0}s");
			TNewStart($"couldn't get to the start spot in {_tStartLimit:F0}s");
			return;
		}
		MHR_Mover.Go(_ai, Time.deltaTime, new Vector3(q.P.x, g, q.P.z), 0.8f, false, "Vein");
	}

	// done for everyone. keep = it stays closed when the plan is made again (not for a depth read from the ground)
	private void TSpotDone(TSpot q, string why, bool keep = false)
	{
		if (q.Done)
		{
			return;
		}
		q.Done = true;
		q.Holder = null;
		if (keep)
		{
			_vp?.Closed.Add(new VClosed { P = q.P, Who = 0 });
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {q.Idx + 1}/{_tLines[q.Line].S.Count} (line {q.Line + 1}) at {MHR_Mover.Fmt(q.P)}: {why}");
	}

	// the spot is done: on along the line; at the end, back the other way from the start once; then a new start.
	// Item B: the next spot has another companion on it - they've met: a new start, the free spot farthest from the others.
	private void TNextSpot()
	{
		TSpot q = _tQ;
		TSpot n = q != null ? TNextUndone(_tLine, q.Idx, _tDir) : null;
		if (n == null && !_tTurned)
		{
			n = TNextUndone(_tLine, _tRunStart, -_tDir);
			if (n != null)
			{
				_tTurned = true;
				_tDir = -_tDir;
				if (CompanionsPlugin.LogNormal) LogInfo($"End of this part of line {_tLine + 1} - working it the other way from where it started");
			}
		}
		if (n == null)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Line {_tLine + 1} done this way - the next start");
			_tSub = TSubPick;   // S2 gets out toward the start it picks
			return;
		}
		HarvestController other = THeldBy(n);
		if (other != null && !_tTurned)
		{
			// pop test C1: the other way round from where this run started first (once per run), before a new start
			TSpot back = TNextUndone(_tLine, _tRunStart, -_tDir);
			if (back != null && THeldBy(back) == null)
			{
				_tTurned = true;
				_tDir = -_tDir;
				if (CompanionsPlugin.LogNormal) LogInfo($"Met {other.MyName} at spot {n.Idx + 1} of line {_tLine + 1} - working the other way");
				TTake(back);
				_tSub = TSubDig;
				TBeginItem();
				return;
			}
		}
		if (other != null)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Met {other.MyName} at spot {n.Idx + 1} of line {_tLine + 1} - a new start away from the others");
			_tPickFar = true;
			_tPickWhy = "met " + other.MyName;
			_tSub = TSubPick;
			return;
		}
		TTake(n);
		_tSub = TSubDig;
		TBeginItem();
	}

	// S3: the spot ahead. Done checks first, then close enough, then the swing that meets dirt first.
	private void TDigTick()
	{
		TSpot q = _tQ;
		if (q == null || !TOpen(q))
		{
			TNextSpot();
			return;
		}
		Vector3 me = base.transform.position;
		float g = GroundAt(q.P);
		if (g < TWater)
		{
			TSpotDone(q, $"at the water (ground {g:F1}, the water level is 30, the floor there {TWaterFloor:F1}) - counts as dug down");
			TNextSpot();
			return;
		}
		if (g <= q.LapDepth + 0.05f && q.LapDepth > q.Depth + 0.05f)
		{
			// part 2 M2: this lap's 2 m; the next lap opens it again
			q.LapOnly = true;
			TSpotDone(q, $"down to lap {(_vp != null ? _vp.Lap : 1)}'s depth, {g:F1} (its target is {q.Depth:F1})");
			TNextSpot();
			return;
		}
		if (g <= q.Depth + 0.05f)
		{
			TSpotDone(q, q.AtFloor ? $"as low as the game lets this ground go, {g:F1} (8 m under its first height; the rock's base is lower)" : $"down to its target, {g:F1} (1 m under the vein's lowest part)");
			TNextSpot();
			return;
		}
		// (fix 1: the depth comes from the plan only - the level-ray "base of the visible rock" test closed spots on
		// untouched ground where the ground beside the vein is higher than the rock, or the rock is set back)
		if (!PrivateArea.CheckAccess(q.P, 0f, flash: false))
		{
			_tAnyWard = true;
			TSpotDone(q, "someone else's ward covers it", keep: true);
			TNextSpot();
			return;
		}
		if (Location.IsInsideNoBuildLocation(q.P))
		{
			_tAnyCantDig = true;
			TSpotDone(q, "a no-build area", keep: true);
			TNextSpot();
			return;
		}
		// a tree marked on the plan right by this spot: the axe first
		if (q.Tree && TTreeNear(q.P, 2.0f, out GameObject treeGo, out Vector3 treePt))
		{
			if (ONoAxeSpot(treeGo, q))
			{
				return;
			}
			if (OCan(treeGo))
			{
				OBegin(treeGo, OCtx.Trench, "a tree on the trench line");
				return;
			}
		}
		if (Flat(me, q.P) > TFarSpot)
		{
			// test review B3: the spots between are done and this one is a walk away: under the start rules (out of the
			// trench first if it's below, 10 s and 1 s more a metre) - not the few seconds a step to the next spot gets.
			// It isn't one of the new starts. S2's walk ends at the spot (then S3), or gives the spot up at its limit.
			float far = Flat(me, q.P);
			_tStartExited = false;
			_tStartLimit = TStartTime + far;
			_tSub = TSubStart;
			TBeginItem();
			if (CompanionsPlugin.LogNormal) LogInfo($"The next spot not done is {q.Idx + 1}/{_tLines[q.Line].S.Count} (line {q.Line + 1}), {far:F1} m away - walking there (up to {_tStartLimit:F0} s)");
			return;
		}
		if (Flat(me, q.P) > TReach)
		{
			// fix 3: the spot is up above it (it's down in a pit or its own trench): get out toward the spot first, once
			if (!My(q).ExitTried && g >= me.y + TExitDepth)
			{
				My(q).ExitTried = true;
				if (XBegin("to reach the next spot", new Vector3(q.P.x, g, q.P.z)))
				{
					return;
				}
			}
			if (TRest())
			{
				return;
			}
			if (_tClock - _tWalkSince > TStepTime)
			{
				TBlockStart(q);
				return;
			}
			// toward the spot just dug (on its own level), else toward the spot itself
			TSpot w = TSpotAt(_tLine, q.Idx - _tDir);
			Vector3 goal = w != null && w.Done && Flat(w.P, q.P) <= 1.6f && Flat(me, w.P) > 0.5f ? w.P : q.P;
			TWalkLevel(goal, 0.4f);
			return;
		}
		_ai.StopMoving();
		_tWalkSince = _tClock;
		Vector3 baseDir = q.P - me;
		baseDir.y = 0f;
		if (baseDir.sqrMagnitude < 0.09f)
		{
			TSpot ahead = TSpotAt(_tLine, q.Idx + _tDir);
			baseDir = ahead != null ? ahead.P - me : Vector3.Cross(Vector3.up, q.Out);
		}
		if (!TPickFacing(baseDir, q.P, 1.5f, q.LapDepth, out Vector3 facing))
		{
			// part 1 T2: no swing from here meets dirt first, and what a straight one meets is a thing it can clear
			if (TPredict(me, FlatNorm(baseDir, base.transform.forward), out _) == TFirst.Other && OCan(_tPredGo))
			{
				OBegin(_tPredGo, OCtx.Trench, "a straight swing meets it before the dirt");
				return;
			}
			TSpotDone(q, $"rock in front ({VRockWhat(_tPredGo, _tPredCol)}): no way to face it that meets dirt first - left for the mining", keep: true);
			TNextSpot();
			return;
		}
		_tKey = q.P;
		TDigSwing(facing, q.P, TFor.Dig);
	}

	private bool TTreeNear(Vector3 p, float r, out GameObject go, out Vector3 pt)
	{
		go = null;
		pt = p;
		Collider[] buf = new Collider[16];
		int k = Physics.OverlapSphereNonAlloc(p + Vector3.up, r, buf, LookMask(), QueryTriggerInteraction.Ignore);
		float bd = float.MaxValue;
		for (int i = 0; i < k; i++)
		{
			Component t = (Component)buf[i].GetComponentInParent<TreeBase>() ?? buf[i].GetComponentInParent<TreeLog>();
			if (t == null)
			{
				continue;
			}
			Vector3 c = buf[i].ClosestPoint(base.transform.position + Vector3.up);
			float d = Vector3.Distance(c, base.transform.position);
			if (d < bd)
			{
				bd = d;
				go = t.gameObject;
				pt = c;
			}
		}
		return go != null;
	}

	// S5: it can't move forward - one look ahead, at the swing's height, then at knee height
	private void TBlockStart(TSpot q)
	{
		My(q).Blocks++;
		Vector3 me = base.transform.position;
		TSpot w = TSpotAt(_tLine, q.Idx - _tDir);
		Vector3 goal = w != null && w.Done && Flat(w.P, q.P) <= 1.6f && Flat(me, w.P) > 0.5f ? w.P : q.P;
		Vector3 dir = FlatNorm(goal - me, FlatNorm(q.P - me, base.transform.forward));
		if (My(q).Blocks > TMaxBlocksPerSpot)
		{
			TBlockUnknown(q, $"blocked {TMaxBlocksPerSpot} times on the way to this spot");
			return;
		}
		if (_character != null && MHR_Water.WouldSwim(_character, me + dir))
		{
			TSplit(q, me + dir, "water it would swim in");
			return;
		}
		RaycastHit hit = default(RaycastHit);
		bool any = false;
		foreach (float hgt in new[] { 1.0f, 0.3f })
		{
			RaycastHit[] hits = Physics.RaycastAll(me + Vector3.up * hgt, dir, 1.6f, LookMask(), QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
			foreach (RaycastHit h in hits)
			{
				if (h.collider != null && h.collider.gameObject != base.gameObject && !h.collider.transform.IsChildOf(base.transform))
				{
					hit = h;
					any = true;
					break;
				}
			}
			if (any)
			{
				break;
			}
		}
		if (!any)
		{
			TBlockBegin(TBlock.Unknown, null, me + dir, dir, "nothing it can see in front");
			return;
		}
		Collider col = hit.collider;
		GameObject go = Projectile.FindHitObject(col) ?? col.gameObject;
		Character ch = col.GetComponentInParent<Character>();
		Destructible des = col.GetComponentInParent<Destructible>();
		if (ch != null)
		{
			TBlockBegin(BaseAI.IsEnemy(_character, ch) ? TBlock.Creature : TBlock.Person, ch.gameObject, hit.point, dir, ch.m_name);
		}
		else if (col.gameObject.layer == s_vTerrain || col.GetComponentInParent<Heightmap>() != null)
		{
			TBlockBegin(TBlock.Dirt, null, hit.point, dir, "dirt");
		}
		else if (col.GetComponentInParent<MineRock5>() != null || col.GetComponentInParent<MineRock>() != null)
		{
			TBlockBegin(TBlock.Rock, go, hit.point, dir, "rock (" + VRockWhat(go, col) + ")", col);
		}
		else if (OCan(col.gameObject) && (col.GetComponentInParent<TreeBase>() != null || col.GetComponentInParent<TreeLog>() != null))
		{
			OBegin(col.gameObject, OCtx.Trench, "it can't move forward past it");
		}
		else if (col.GetComponentInParent<Door>() != null)
		{
			TBlockBegin(TBlock.Door, go, hit.point, dir, "a door");
		}
		else if (col.GetComponentInParent<WearNTear>() != null)
		{
			TBlockBegin(TBlock.Built, go, hit.point, dir, "a building piece (" + go.name + ")");
		}
		else if (OCan(col.gameObject))
		{
			OBegin(col.gameObject, OCtx.Trench, "it can't move forward past it");
		}
		else
		{
			TBlockBegin(TBlock.Unknown, go, hit.point, dir, "something it can't clear (" + go.name + ")");
		}
	}

	private void TBlockBegin(TBlock kind, GameObject go, Vector3 point, Vector3 dir, string what, Collider col = null)
	{
		_bKind = kind;
		_bGo = go;
		_bCol = col;
		_bPoint = point;
		_bDir = dir;
		_bSince = _tClock;
		_bNoProg = 0;
		_bHealth = TObjHealth(go);
		_tSub = TSubBlock;
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"Can't move forward to trench spot {(_tQ != null ? _tQ.Idx + 1 : 0)} (line {_tLine + 1}): {what} in front at {MHR_Mover.Fmt(point)} - {TBlockPlan(kind)}");
	}

	private static string TBlockPlan(TBlock k)
	{
		switch (k)
		{
		case TBlock.Dirt:
			return "digging it (or jumping a low step)";
		case TBlock.Rock:
			return "mining it";
		case TBlock.Tree:
			return "chopping it";
		case TBlock.Person:
			return "waiting for them to move, then stepping round";
		case TBlock.Creature:
			return "waiting (the fight rules take over if it attacks)";
		case TBlock.Door:
			return "letting the walking code open it";
		case TBlock.Built:
			return "working round it";
		default:
			return "turning round, or a new start";
		}
	}

	// back to walking toward the spot, with a fresh 6 s
	private void TBlockCleared(string why)
	{
		if (CompanionsPlugin.LogDetailed) Log("Cleared the way: " + why);
		_tChopping = false;
		_tSub = TSubDig;
		_tWalkSince = _tClock;
		_tItemNoProg = 0;
	}

	private static bool TGone(GameObject go)
	{
		return go == null || !go.activeInHierarchy;
	}

	private void TBlockTick()
	{
		TSpot q = _tQ;
		if (q == null)
		{
			_tSub = TSubPick;
			return;
		}
		Vector3 me = base.transform.position;
		switch (_bKind)
		{
		case TBlock.Dirt:
		{
			Vector3 ahead = me + _bDir * 0.8f;
			float rise = GroundAt(ahead) - me.y;
			if (!My(q).Jumped && rise > 0.4f && rise <= 1.0f && MHR_Mover.HeadRoomAt(new Vector3(ahead.x, GroundAt(ahead), ahead.z), THead))
			{
				if (TRest() || !_character.IsOnGround())
				{
					return;
				}
				My(q).Jumped = true;
				MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(_bDir));
				_ai.MoveTowards(_bDir, run: false);
				_character.Jump();
				TBlockCleared($"jumped the {rise:F1} m step");
				return;
			}
			// fix 2: the real swing check here too (the same 9 facings as a dig): dirt higher than its feet, near the way ahead
			if (!TPickFacing(_bDir, ahead, 1.5f, me.y, out Vector3 face, overRock: true))
			{
				// no facing meets dirt first: what the straight swing meets first is what's in the way
				TFirst first = TPredict(me, _bDir, out Vector3 fp);
				if (first == TFirst.Vein && _tPredGo != null && _tPredCol != null)
				{
					TBlockBegin(TBlock.Rock, _tPredGo, fp, _bDir, "rock, not dirt (no swing from here meets dirt first; a straight one meets " + _tPredCol.name + ")", _tPredCol);
					return;
				}
				TBlockUnknown(q, "dirt in front, but no swing from here meets dirt first" + (first == TFirst.Other && _tPredGo != null ? " (a straight one meets " + _tPredGo.name + ")" : ""));
				return;
			}
			_tKey = q.P;
			TDigSwing(face, ahead, TFor.BlockDirt);
			return;
		}
		case TBlock.Rock:
			// a vein part breaks on its own (the rest of the vein stays): the part, not the whole rock, is what was in the way
			if (TGone(_bGo) || (_bCol != null && (!_bCol.enabled || !_bCol.gameObject.activeInHierarchy)))
			{
				TBlockCleared("the rock broke");
				return;
			}
			if (!TToolCan(_bGo, chop: false))
			{
				TBlockUnknown(q, "rock its pickaxe can't break");
				return;
			}
			TRockSwing(_bPoint, TFor.BlockObj);
			return;
		case TBlock.Tree:
		{
			if (TGone(_bGo))
			{
				_tChops++;
				TBlockCleared("the tree or log is gone");
				return;
			}
			if (_tChops >= TMaxChops)
			{
				TBlockUnknown(q, $"chopped {TMaxChops} trees or logs on this vein already");
				return;
			}
			ItemDrop.ItemData axe = FindBestTool(1);
			if (axe == null || !TToolCan(_bGo, chop: true))
			{
				TBlockUnknown(q, axe == null ? "a tree and no axe" : "a tree its axe can't chop");
				return;
			}
			_tChopping = true;
			TChopSwing(axe, _bPoint, TFor.BlockObj, _bGo);
			return;
		}
		case TBlock.Person:
		case TBlock.Creature:
			if (My(q).PersonWaited)
			{
				TBlockUnknown(q, "still in the way after waiting and stepping round");
				return;
			}
			_ai.StopMoving();
			if (_tClock - _bSince < 3f)
			{
				return;
			}
			My(q).PersonWaited = true;
			TWalkLevel(q.P, 0.4f);
			MHR_Mover.SidestepNow(_ai, q.P, "Vein");
			TBlockCleared("waited 3 s, stepping round");
			return;
		case TBlock.Door:
			if (!My(q).DoorTried)
			{
				My(q).DoorTried = true;
				TBlockCleared("the walking code opens doors on the way");
				return;
			}
			TSplit(q, _bPoint, "a door it couldn't get through");
			return;
		case TBlock.Built:
			TSplit(q, _bPoint, "a building");
			return;
		default:
			TBlockUnknown(q, "nothing it can clear");
			return;
		}
	}

	// a building or deep water on the line: the spots by it are left out, as if the plan had left them out
	private void TSplit(TSpot q, Vector3 at, string what)
	{
		int n = 0;
		foreach (TSpot s in _tLines[_tLine].S)
		{
			if (!s.Done && Flat(s.P, at) <= 1.5f)
			{
				s.Done = true;
				s.Holder = null;
				_vp?.Closed.Add(new VClosed { P = s.P, Who = 0 });
				n++;
			}
		}
		if (!q.Done)
		{
			q.Done = true;
			q.Holder = null;
			_vp?.Closed.Add(new VClosed { P = q.P, Who = 0 });
			n++;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Line {_tLine + 1} split at {MHR_Mover.Fmt(at)}: {what} - {n} spot(s) by it left out; carrying on past it");
		_tChopping = false;
		TNextSpot();
	}

	// anything else: turn round once per line, then a new start
	private void TBlockUnknown(TSpot q, string why)
	{
		_tChopping = false;
		if (!_tTurned)
		{
			TSpot n = TNextUndone(_tLine, _tRunStart, -_tDir);
			_tTurned = true;
			if (n != null && n != q && THeldBy(n) == null)
			{
				_tDir = -_tDir;
				if (q.Holder == this)
				{
					q.Holder = null;
				}
				TTake(n);
				_tSub = TSubDig;
				TBeginItem();
				if (CompanionsPlugin.LogNormal) LogInfo($"Blocked ({why}) - turning round and working line {_tLine + 1} the other way from where it started");
				return;
			}
		}
		TSpotGave(q, "blocked: " + why);
		TNewStart(why);
	}

	private void TNewStart(string why, bool quiet = false)
	{
		_tNewStarts++;
		if (_tNewStarts > TMaxNewStarts)
		{
			TTrenchDone($"blocked again ({why}) after {TMaxNewStarts} new starts");
			return;
		}
		if (quiet)
		{
			if (CompanionsPlugin.LogDetailed) Log($"A new start ({_tNewStarts} of {TMaxNewStarts}): {why}");
		}
		else if (CompanionsPlugin.LogNormal) LogInfo($"A new start ({_tNewStarts} of {TMaxNewStarts}): {why}");
		_tSub = TSubPick;   // S2 gets out toward the start it picks
	}

	private void TTrenchDone(string why, bool allDone = false)
	{
		int done = 0, all = 0, shut = 0;
		foreach (TLine l in _tLines)
		{
			foreach (TSpot s in l.S)
			{
				all++;
				if (s.Done)
				{
					shut++;
				}
				if (!TOpen(s))
				{
					done++;
				}
			}
		}
		if (why == null)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_target.name}\" is already done - clearing it so it pops");
		}
		else
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_target.name}\" is done ({why}; {done} of {all} spots) - clearing it so it pops");
			if (allDone && shut == all && _vp != null)
			{
				_vp.TrenchDone = true;   // pop test A1
			}
			if (_vp != null)
			{
				// part 2 M2: the trench is over for this companion - with nobody else still on it the ring stays as it is
				if (TOtherTrenching() == null)
				{
					_vp.RingFixed = true;
				}
				// part 2 M3 (John 2026-11-17: carry on past a stretch that can't be dug): said once for the vein
				if (shut < all && !_vp.GapSaid)
				{
					_vp.GapSaid = true;
					string where = "";
					int shown = 0;
					foreach (TLine l in _tLines)
					{
						foreach (TSpot s in l.S)
						{
							if (!s.Done && shown < 6)
							{
								where += (shown++ > 0 ? ", " : "") + MHR_Mover.Fmt(s.P);
							}
						}
					}
					if (CompanionsPlugin.LogNormal) LogInfo($"{all - shut} of the {all} trench spots round \"{_target.name}\" couldn't be dug (at {where}{(all - shut > shown ? ", ..." : "")}) - carrying on with the clearing from the trench there is");
					_talk?.SayProblem("vein:gap", string.Format(ModLocalization.Loc("hc_speech_vein_trench_gap"), MHR_Items.NiceName(_target)));
				}
			}
		}
		// part 2 M7: what the clearing starts with
		if (CompanionsPlugin.LogNormal)
		{
			int held = 0, under = 0, hang = 0;
			foreach (VPart p in _vParts)
			{
				if (TBuried(p))
				{
					under++;
				}
				else if (p.Direct)
				{
					held++;
				}
				else
				{
					hang++;
				}
			}
			LogInfo($"Clearing \"{_target.name}\": {_vParts.Count} parts - {held} held by the ground or something solid (their dirt is dug to the floor, the ones standing in the floor are mined), {hang} hanging on other parts (left alone, they fall), {under} under the ground (top less than {TBuriedTop:F1} m above the floor - ignored)");
		}
		_tPhase = TPhasePop;
		_pRound = 1;
		_pPart = null;
		_pLeft.Clear();
		_tChopping = false;
		TBeginItem();
	}

	// the tool's level against the thing in the way (the game refuses a hit below its m_minToolTier)
	private bool TToolCan(GameObject go, bool chop)
	{
		if (go == null)
		{
			return true;
		}
		ItemDrop.ItemData tool = FindBestTool(chop ? 1 : 3);
		if (tool == null)
		{
			return false;
		}
		int tier = tool.m_shared.m_toolTier, need = 0;
		MineRock5 mr5 = go.GetComponentInParent<MineRock5>();
		MineRock mr = go.GetComponentInParent<MineRock>();
		TreeBase tb = go.GetComponentInParent<TreeBase>();
		TreeLog tl = go.GetComponentInParent<TreeLog>();
		Destructible d = go.GetComponentInParent<Destructible>();
		if (mr5 != null)
		{
			need = mr5.m_minToolTier;
		}
		else if (mr != null)
		{
			need = mr.m_minToolTier;
		}
		else if (tb != null)
		{
			need = tb.m_minToolTier;
		}
		else if (tl != null)
		{
			need = tl.m_minToolTier;
		}
		else if (d != null)
		{
			need = d.m_minToolTier;
		}
		return tier >= need;
	}

	// where the game keeps a thing's health (trees, logs, rocks); -1 = full (not hit yet), -2 = can't be read
	private float TObjHealth(GameObject go)
	{
		if (go == null)
		{
			return -2f;
		}
		if (VInGroup(go.GetComponentInParent<MineRock5>()))
		{
			return _vHealth;   // list v2 V5: any vein of the job counts
		}
		ZNetView nv = go.GetComponentInParent<ZNetView>();
		ZDO z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
		return z != null ? z.GetFloat(ZDOVars.s_health, -1f) : -2f;
	}

	// the axe swing: the axe in hand, a breather when low (the same numbers as the pickaxe), then the game's attack
	// list v2 T1: a stump (the wood job's own test: a Destructible named "stub") - the wood job's downward power chop
	// when the axe has one (WantsPowerChop, HarvestController.cs); the sideways swing passes over a low stump (log
	// ff6c8861: Gunter's bronze axe, "the last hit nothing it reported")
	private static bool TIsStump(GameObject go)
	{
		Destructible d = go != null ? go.GetComponentInParent<Destructible>() : null;
		return d != null && d.gameObject.name.IndexOf("stub", System.StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private void TChopSwing(ItemDrop.ItemData axe, Vector3 aim, TFor why = TFor.BlockObj, GameObject thing = null)
	{
		bool power = TIsStump(thing) && axe != null && axe.HaveSecondaryAttack();
		VeinFace(aim);
		_attackTimer -= Time.deltaTime;
		if (_attackTimer > 0f)
		{
			return;
		}
		if (ReflectionHelper.GetRightItem(_humanoid) != axe)
		{
			EquipTool(axe);
			_attackTimer = 0.25f;
			return;
		}
		float cost = SwingStamina(axe, secondary: power);
		if (_stamina != null && cost > 0f)
		{
			float max = Mathf.Max(1f, _stamina.MaxStamina);
			if (_catchingBreath && _stamina.Stamina < (ModConfig.GatherResumeStamina?.Value ?? 0.6f) * max)
			{
				_attackTimer = 0.5f;
				return;
			}
			_catchingBreath = false;
			if (_stamina.Stamina < cost + (ModConfig.GatherRestStamina?.Value ?? 0.15f) * max)
			{
				_catchingBreath = true;
				_attackTimer = 0.5f;
				return;
			}
		}
		bool started = _humanoid.StartAttack(null, secondaryAttack: power);
		if (started && power && CompanionsPlugin.LogDetailed) Log($"A stump: the downward power chop with {axe.m_shared.m_name} (the wood job's way)");
		_attackTimer = started ? 0.4f : 0.5f;
		if (!started)
		{
			return;
		}
		_swingCount++;
		_vLastHealth = _vHealth;
		_vLastAlive = _vParts.Count;
		_vLastDirt = _vDirtCount;
		_vDigPending = false;
		_vSwung = true;
		_vSwungAt = Time.time;
		_vSwingDig = false;
		_vSwingAim = aim;
		_tHitObj = null;
		_tSwingFor = why;
		_tDigOK = false;
		_tPredTerrain = false;
	}

	// ---------- S7: clear it so it pops ----------
	private bool TPartAlive(Collider c, out VPart part)
	{
		foreach (VPart p in _vParts)
		{
			if (p.C == c)
			{
				part = p;
				return true;
			}
		}
		part = default(VPart);
		return false;
	}

	// part 2 M4/M5: a part under the ground - its top is less than 0.7 m above the floor at its place (the lowest the
	// game lets that ground go, or half a metre of water). Neither the top nor that floor changes, so it's kept per part.
	private readonly Dictionary<Collider, bool> _pUnder = new Dictionary<Collider, bool>();

	private bool TBuried(VPart p)
	{
		if (p.C == null)
		{
			return false;
		}
		if (!_pUnder.TryGetValue(p.C, out bool under))
		{
			float floor = TFloorHard(p.Center), top = p.Center.y + p.Half.y;
			under = top < floor + TBuriedTop;
			_pUnder[p.C] = under;
			if (under && CompanionsPlugin.LogDetailed) Log($"Part \"{p.C.name}\" is under the ground: its top {top:F1} is less than {TBuriedTop:F1} m above the floor there ({floor:F1}) - ignored");
		}
		return under;
	}

	// part 2 M5: only parts under the ground are left - the vein is finished (not a give-up: it doesn't count toward
	// "gave up twice"), said once with the "buried" line there already is
	private void TVeinFinished()
	{
		string count = _vp != null ? $"about {_vp.ByHand} part(s) mined by hand, {_vp.Fell} fell, " : "";
		if (CompanionsPlugin.LogNormal) LogInfo($"\"{_target.name}\" is finished: {count}{_vParts.Count} left under the ground (top less than {TBuriedTop:F1} m above the floor)");
		SayGaveUp(GiveUpWhy.Buried, $"{_vParts.Count} part(s) left, all under the ground");
		_leaveFinished = true;
		VeinLeave();
	}

	private void TPopTick()
	{
		VeinSpots();
		Vector3 me = base.transform.position;
		VPart part = default(VPart);
		// list v2 V6: vein by vein, the lowest vein of the job first (each is finished by the rules below)
		_pCur = TPopRock();
		if (_pCur != null && _pCur != _pCurSaid)
		{
			// a new vein of the job: its own clearing rounds (the order only moves on, so no vein comes back)
			if (_pCurSaid != null)
			{
				_pRound = 1;
				_pLeft.Clear();
				_pPart = null;
			}
			_pCurSaid = _pCur;
			if (_vp != null && _vp.Rocks.Count > 1 && CompanionsPlugin.LogNormal) LogInfo($"Clearing vein {_vp.Rocks.IndexOf(_pCur) + 1} of {_vp.Rocks.Count} of this job (at {MHR_Mover.Fmt(_pCur.transform.position)}, the lowest with parts above the ground)");
		}
		if (_pPart != null && (!TPartAlive(_pPart, out part) || !part.Direct || _pLeft.Contains(_pPart) || TBuried(part) || (_pCur != null && part.Rock != _pCur)))
		{
			_pPart = null;
		}
		if (_pPart == null)
		{
			// item B: the nearest held part nobody is on; when every one has a companion on it, it helps on the nearest
			bool anyDirect = false;
			float bd = float.MaxValue, bh = float.MaxValue;
			Collider heldPart = null;
			int notUnder = 0, heldUnder = 0;
			foreach (VPart p in _vParts)
			{
				if (_pCur != null && p.Rock != _pCur)
				{
					continue;
				}
				bool under = TBuried(p);
				if (!under)
				{
					notUnder++;
				}
				if (!p.Direct)
				{
					continue;
				}
				anyDirect = true;
				// part 2 M4.1: a part under the ground is never tried
				if (under)
				{
					heldUnder++;
					continue;
				}
				if (_pLeft.Contains(p.C))
				{
					continue;
				}
				float d = Vector3.Distance(me, p.Center);
				if (TPartHeldBy(p.C) != null)
				{
					if (d < bh)
					{
						bh = d;
						heldPart = p.C;
					}
					continue;
				}
				if (d < bd)
				{
					bd = d;
					_pPart = p.C;
				}
			}
			if (_pPart == null && heldPart != null)
			{
				_pPart = heldPart;
				if (CompanionsPlugin.LogDetailed) Log($"Every held part has a companion on it - helping {TPartHeldBy(heldPart)?.MyName ?? "them"} on \"{heldPart.name}\"");
			}
			else if (_pPart != null && _vp != null)
			{
				_vp.PartHolds[_pPart] = this;
			}
			if (notUnder == 0 && _vParts.Count > 0)
			{
				TVeinFinished();
				return;
			}
			if (!anyDirect)
			{
				// nothing holds it up any more: one break and the game pops the rest
				if (_pStage != 3)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"Nothing holds \"{_target.name}\" up any more - one break pops it");
					_pStage = 3;
					TBeginItem();
				}
				TPopBreak();
				return;
			}
			if (_pPart == null && _pLeft.Count == 0)
			{
				// part 2 M6: what's left hangs on parts under the ground - the mining lap is the last resort
				if (CompanionsPlugin.LogNormal) LogInfo($"Nothing more to clear round \"{_target.name}\": the {heldUnder} held part(s) left are under the ground, {notUnder} part(s) still hang on them - the mining lap for what it can reach");
				TLapStart();
				return;
			}
			if (_pPart == null)
			{
				_pRound++;
				if (_pRound > 2)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"Cleared what it could round \"{_target.name}\" ({_pLeft.Count} held part(s) it couldn't free) - the mining lap");
					TLapStart();
					return;
				}
				if (CompanionsPlugin.LogNormal) LogInfo($"Clearing round 2: trying the {_pLeft.Count} held part(s) again");
				_pLeft.Clear();
				return;
			}
			TPartAlive(_pPart, out part);
			_pStage = part.Dirt ? 0 : 2;
			_pHasSpot = false;
			_pSince = _tClock;
			TBeginItem();
			if (!part.Dirt)
			{
				if (!part.Join || part.Solid || part.Low)
				{
					TPopLeave($"held by {(part.Solid ? "something solid that isn't the vein" : "rock lower down")}, not dirt");
					return;
				}
				// list v2 V6: held only by another vein of the job lying lower - mined by hand, as a part standing in the
				// floor, so the veins come apart
				if (CompanionsPlugin.LogDetailed) Log($"Clearing part \"{_pPart.name}\": held by another vein of this job lower down - mining it to separate them");
				return;
			}
			if (CompanionsPlugin.LogDetailed) Log($"Clearing part \"{_pPart.name}\": held by dirt, {Vector3.Distance(me, part.Center):F1} m away");
		}
		if (!TPartAlive(_pPart, out part))
		{
			_pPart = null;
			return;
		}
		if (_pStage == 0)
		{
			TPopDig(part);
		}
		else
		{
			TPopMine(part.C, TFor.PopMine);
		}
	}

	// pop test B2: where to aim at a vein piece. The shape's own nearest point where the engine has one (B1); for a
	// hollow mesh, where a line from the eye to the middle of its box first meets that piece (10 m); else the box point
	private Vector3 TPartAim(Collider part)
	{
		Vector3 eye = Eye();
		if (part is MeshCollider mc && !mc.convex)
		{
			Vector3 to = part.bounds.center - eye;
			if (to.sqrMagnitude > 0.0001f && part.Raycast(new Ray(eye, to.normalized), out RaycastHit hit, 10f))
			{
				return hit.point;
			}
		}
		return SafeClosestPoint(part, eye);
	}

	// pop test D2: how far the game has already lowered (-) or raised (+) the ground here from its first height
	private static readonly System.Reflection.FieldInfo s_tLevelDelta = HarmonyLib.AccessTools.Field(typeof(TerrainComp), "m_levelDelta");

	private static readonly System.Reflection.FieldInfo s_tSmoothDelta = HarmonyLib.AccessTools.Field(typeof(TerrainComp), "m_smoothDelta");

	// part 1 B1: the lowest the game lets the ground at p go - its level change is held to 8 m down (TerrainComp), so the
	// floor is the ground now less what is left of those 8 m. Ground nobody has changed: 8 m under where it is.
	private const float TFloorMargin = 0.15f;

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
		// the game holds the changed height between 8 m under and 8 m over the first height (TerrainComp.ApplyToHeightmap)
		return g - Mathf.Max(0f, 8f + Mathf.Clamp(level[i] + smooth[i], -8f, 8f));
	}

	// part 2 M1: the floor at p - the lowest the game lets that ground go, or half a metre of water, whichever is higher
	private static float TFloorHard(Vector3 p)
	{
		return Mathf.Max(TFloorAt(p) + TFloorMargin, TWaterFloor);
	}

	// part 2 M2: the ground at this place when the lap being dug began (kept by place, so a ring made again in lap 1
	// keeps it); a place not known yet starts from its ground now
	private float TLapBaseFor(Vector3 p, float groundNow)
	{
		if (_vp == null)
		{
			return groundNow;
		}
		foreach (Vector3 b in _vp.LapBase)
		{
			if (Flat(b, p) <= 0.6f)
			{
				return b.y;
			}
		}
		_vp.LapBase.Add(new Vector3(p.x, groundNow, p.z));
		return groundNow;
	}

	private void TSetLapDepth(TSpot s)
	{
		s.LapDepth = Mathf.Max(s.Depth, TLapBaseFor(s.P, GroundAt(s.P)) - TLapStep);
	}

	// part 2 M3: another companion is still digging a spot of the trench
	private HarvestController TOtherTrenching()
	{
		foreach (HarvestController o in TOthers())
		{
			if (o._tPhase == TPhaseTrench && !o._stuckInHole && o._tSub != TSubPick && o._tQ != null && o.TOpen(o._tQ))
			{
				return o;
			}
		}
		return null;
	}

	// part 2 M2: no spot is open for this companion. True = not the end of the trench yet: it waits for a companion
	// still on a spot, or the next lap has begun (S2 picks a start in it). False = the trench has ended.
	// A new lap only while under TMaxLaps, and only if this lap lowered some spot by TLapMinGain and some spot closed
	// at the lap's depth is still above its target. It reopens just those spots; give-ups, spots closed for a fixed
	// reason, the chop count and the new-start count are not reset.
	private float _tLapWaitLog;

	private bool TLapNext()
	{
		if (_vp == null)
		{
			return false;
		}
		HarvestController busy = TOtherTrenching();
		if (busy != null)
		{
			_ai.StopMoving();
			if (Time.time >= _tLapWaitLog)
			{
				_tLapWaitLog = Time.time + 10f;
				if (CompanionsPlugin.LogDetailed) Log($"Nothing left for it on lap {_vp.Lap} - waiting for {busy.MyName} to finish its spot before the next step");
			}
			return true;
		}
		int all = 0, atTarget = 0, lapOnly = 0, shut = 0, left = 0;
		bool gained = false;
		foreach (TLine l in _tLines)
		{
			foreach (TSpot s in l.S)
			{
				all++;
				float g = GroundAt(s.P);
				if (TLapBaseFor(s.P, g) - g >= TLapMinGain)
				{
					gained = true;
				}
				if (!s.Done)
				{
					left++;
				}
				else if (s.LapOnly)
				{
					lapOnly++;
				}
				else if (g <= s.Depth + 0.05f || g < TWater)
				{
					atTarget++;
				}
				else
				{
					shut++;
				}
			}
		}
		if (!_vp.RingFixed)
		{
			_vp.RingFixed = true;
			if (CompanionsPlugin.LogNormal) LogInfo($"Lap {_vp.Lap} of the trench round \"{_vp.Name}\" is done - the ring stays where it is from here on");
		}
		string sum = $"{all} spots: {atTarget} at their target, {lapOnly} at this lap's depth, {shut} closed for a fixed reason, {left} given up";
		if (_vp.Lap < TMaxLaps && gained && lapOnly > 0)
		{
			_vp.Lap++;
			_vp.LapBase.Clear();
			foreach (TLine l in _tLines)
			{
				foreach (TSpot s in l.S)
				{
					float g = GroundAt(s.P);
					_vp.LapBase.Add(new Vector3(s.P.x, g, s.P.z));
					s.LapDepth = Mathf.Max(s.Depth, g - TLapStep);
					if (s.Done && s.LapOnly)
					{
						s.Done = false;
						s.LapOnly = false;
					}
				}
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Trench lap {_vp.Lap} of at most {TMaxLaps} round \"{_vp.Name}\": {lapOnly} spot(s) open again, up to {TLapStep:F0} m lower (after lap {_vp.Lap - 1}: {sum})");
			_tPickWhy = "lap " + _vp.Lap;
			_tSub = TSubPick;
			TBeginItem();
			return true;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench lap {_vp.Lap} was the last ({(lapOnly == 0 ? "no spot is left above its target" : !gained ? $"it lowered no spot by {TLapMinGain:F1} m" : $"{TMaxLaps} laps is the limit")}; {sum})");
		return false;
	}

	private static string TLoweredSoFar(Vector3 p)
	{
		Heightmap hm = Heightmap.FindHeightmap(p);
		TerrainComp tc = TerrainComp.FindTerrainCompiler(p);
		if (hm == null || tc == null)
		{
			return "not changed by anyone yet";
		}
		float[] level = s_tLevelDelta?.GetValue(tc) as float[], smooth = s_tSmoothDelta?.GetValue(tc) as float[];
		if (level == null || smooth == null)
		{
			return "unknown";
		}
		hm.WorldToVertex(p, out int x, out int y);
		int i = y * (hm.m_width + 1) + x;
		if (i < 0 || i >= level.Length || i >= smooth.Length)
		{
			return "unknown";
		}
		return (level[i] + smooth[i]).ToString("F1") + " m from its first height (the game's limit is 8 m down)";
	}

	private void TPopLeave(string why)
	{
		if (_pPart != null)
		{
			_pLeft.Add(_pPart);
			if (CompanionsPlugin.LogNormal) LogInfo($"Leaving part \"{_pPart.name}\" for now: {why}");
		}
		_pPart = null;
	}

	// the dirt that holds this part: the nearest dirt spot under it, dug from where the swing meets dirt first
	private void TPopDig(VPart part)
	{
		Vector3 me = base.transform.position;
		if (_pHasSpot && (_vStuck.Contains(_pSpot.Key) || !_vSpots.Exists(s => s.Key == _pSpot.Key)))
		{
			_pHasSpot = false;
		}
		if (!_pHasSpot)
		{
			float bd = float.MaxValue;
			foreach (VSpot s in _vSpots)
			{
				// part 2 M4.2: dirt already at the floor (the water, or within 0.15 m of the game's 8 m limit) isn't swung at
				if (s.Part != part.C || _vStuck.Contains(s.Key) || s.P.y < TWater || s.P.y <= TFloorAt(s.P) + TFloorMargin)
				{
					continue;
				}
				float d = Flat(s.P, me);
				if (d < bd)
				{
					bd = d;
					_pSpot = s;
					_pHasSpot = true;
				}
			}
			if (!_pHasSpot)
			{
				_pStage = 1;
				TBeginItem();
				// part 2 M4.3: no dirt above the floor touches it any more and the game still says the ground holds it
				if (CompanionsPlugin.LogDetailed) Log($"Part \"{part.C.name}\": no dirt spot above the floor is left for it and the ground still holds it (its bottom {part.MinY:F1}, the floor there {TFloorHard(part.Center):F1}) - mining it");
				return;
			}
			TBeginItem();
		}
		if (Flat(me, _pSpot.P) > TReach)
		{
			if (TRest())
			{
				return;
			}
			if (_tClock - _tWalkSince > TStepTime)
			{
				// part 2 M4: that spot is left, the next one for this part (the part is mined only with none left)
				_vStuck.Add(_pSpot.Key);
				_pHasSpot = false;
				TBeginItem();
				if (CompanionsPlugin.LogDetailed) Log($"Couldn't get beside the dirt at {MHR_Mover.Fmt(_pSpot.P)} under \"{part.C.name}\" in {TStepTime:F0}s - leaving that spot");
				return;
			}
			TWalkLevel(_pSpot.P, 0.4f);
			return;
		}
		_ai.StopMoving();
		if (!TPickFacing(_pSpot.P - me, _pSpot.P, 1.0f, Mathf.Max(part.MinY - 0.5f, TFloorAt(_pSpot.P)), out Vector3 facing, overRock: true))
		{
			_pStage = 1;
			TBeginItem();
			if (CompanionsPlugin.LogDetailed) Log($"\"{part.C.name}\" is in the way of the dirt under it (no swing from here meets the dirt first) - mining it");
			return;
		}
		_tKey = part.C.bounds.center;
		TDigSwing(facing, _pSpot.P, TFor.PopDig);
	}

	// a vein part: walk until the swing's start is within reach of it, then swing
	private void TPopMine(Collider part, TFor why)
	{
		Vector3 aim = TPartAim(part);
		if (Vector3.Distance(TOrigin(), aim) > TMineReach)
		{
			if (TRest())
			{
				return;
			}
			if (_tClock - _tWalkSince > TStepTime)
			{
				if (why == TFor.Lap)
				{
					_lIdx++;
					TBeginItem();
				}
				else if (_pStage == 3)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get within a swing of \"{part.name}\" to pop it - the mining lap");
					TLapStart();
				}
				else
				{
					TPopLeave($"couldn't get within a swing of it in {TStepTime:F0}s (aiming at {MHR_Mover.Fmt(aim)}, {Vector3.Distance(TOrigin(), aim):F1} m from the swing's start; no swing)");
				}
				return;
			}
			TWalkLevel(aim, 0.6f);
			return;
		}
		_ai.StopMoving();
		_tKey = part.bounds.center;
		TRockSwing(aim, why);
	}

	private void TPopBreak()
	{
		Collider best = null;
		float bd = float.MaxValue;
		foreach (VPart p in _vParts)
		{
			if (_pCur != null && p.Rock != _pCur)
			{
				continue;   // list v2 V6
			}
			if (TBuried(p))
			{
				continue;   // part 2 M4.1
			}
			float d = Vector3.Distance(TOrigin(), TPartAim(p.C));
			if (d < bd)
			{
				bd = d;
				best = p.C;
			}
		}
		if (best == null)
		{
			return;
		}
		_pPart = best;
		TPopMine(best, TFor.PopMine);
	}

	// ---------- S7b: the mining lap ----------
	private void TLapStart()
	{
		_tPhase = TPhaseLap;
		_lLap = 1;
		_lIdx = 0;
		_tChopping = false;
		TBeginItem();
		if (CompanionsPlugin.LogNormal) LogInfo($"Mining lap 1 round \"{_target.name}\" ({_vParts.Count} part(s) left)");
	}

	private TSpot TLapSpot(int i)
	{
		foreach (TLine l in _tLines)
		{
			if (i < l.S.Count)
			{
				return l.S[i];
			}
			i -= l.S.Count;
		}
		return null;
	}

	private void TLapTick()
	{
		// part 2 M5: only parts under the ground are left - finished
		bool anyAbove = false;
		foreach (VPart p in _vParts)
		{
			if (!TBuried(p))
			{
				anyAbove = true;
				break;
			}
		}
		if (!anyAbove && _vParts.Count > 0)
		{
			TVeinFinished();
			return;
		}
		TSpot s = TLapSpot(_lIdx);
		Vector3 me = base.transform.position;
		if (s == null)
		{
			// no line (couldn't find the rock's edge): mine whatever's in reach from where it stands, once per lap
			if (_tLines.Count == 0 && _lIdx == 0)
			{
				TLapSwingNearest(me);
				return;
			}
			_lLap++;
			_lIdx = 0;
			TBeginItem();
			if (_lLap > 2)
			{
				VeinGiveUp();
				return;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"Mining lap {_lLap} round \"{_target.name}\" ({_vParts.Count} part(s) left)");
			return;
		}
		if (Flat(me, s.P) > 0.8f)
		{
			if (TRest())
			{
				return;
			}
			if (_tClock - _tWalkSince > TStepTime)
			{
				_lIdx++;
				TBeginItem();
				return;
			}
			TWalkLevel(s.P, 0.5f);
			return;
		}
		TLapSwingNearest(me);
	}

	private void TLapSwingNearest(Vector3 me)
	{
		Collider best = null;
		float bd = float.MaxValue;
		Vector3 o = TOrigin();
		foreach (VPart p in _vParts)
		{
			if (TBuried(p))
			{
				continue;   // part 2 M6: the mining lap skips parts under the ground
			}
			float d = Vector3.Distance(o, TPartAim(p.C));
			if (d < bd)
			{
				bd = d;
				best = p.C;
			}
		}
		if (best == null || bd > TMineReach)
		{
			_lIdx++;
			TBeginItem();
			return;
		}
		_ai.StopMoving();
		_tKey = best.bounds.center;
		TRockSwing(TPartAim(best), TFor.Lap);
	}

	// ---------- after each swing: what did the game do? ----------
	private void TJudge()
	{
		// item A: a piece one of its swings meets is seen (its health went down)
		Dictionary<Collider, float> before = new Dictionary<Collider, float>();
		foreach (VPart p in _vParts)
		{
			before[p.C] = p.Health;
		}
		VeinScan();
		foreach (VPart p in _vParts)
		{
			if (before.TryGetValue(p.C, out float hb) && p.Health < hb - 0.01f)
			{
				TSee(p, "a swing hit it");
			}
		}
		bool broke = _vParts.Count < _vLastAlive;
		bool rock = broke || _vHealth < _vLastHealth - 0.01f;
		// part 2 M7: one part of a break is the one mined, the others fell (kept for the vein, all companions together)
		if (_vp != null)
		{
			if (_vp.LastAlive > _vParts.Count)
			{
				int went = _vp.LastAlive - _vParts.Count;
				_vp.ByHand++;
				_vp.Fell += went - 1;
				if (went > 1 && CompanionsPlugin.LogNormal) LogInfo($"{went} parts of \"{_vp.Name}\" went with that break: {went - 1} fell ({_vParts.Count} left)");
			}
			_vp.LastAlive = _vParts.Count;
		}
		if (broke && _target != null)
		{
			_vGiveUps.Remove(_target.GetInstanceID());
		}
		bool dugCall = _vDigPending;
		bool terrain = dugCall || _vTerrainHit;
		if (_vDigPending)
		{
			_vDigPending = false;
			if (CompanionsPlugin.LogDetailed) Log($"Dig at {MHR_Mover.Fmt(_vDigAt)}: ground {_vDigGround:F2} -> {GroundAt(_vDigAt):F2}");
		}
		_vTerrainHit = false;
		float lowered = _tQBefore - GroundAt(_tQPoint);
		float lowered2 = _tQ2Before - GroundAt(_tQ2Point);
		bool digProg = lowered >= VDigMin;
		// item A: after a swing that lowered the ground, the pieces near it that now show are seen
		if (digProg || lowered2 >= VDigMin)
		{
			TSeeCheck(_tQPoint);
		}
		if (_tPredTerrain)
		{
			_tPredTerrain = false;
			bool agree = terrain && !rock;
			if (CompanionsPlugin.LogDetailed) Log($"Swing check said dirt first; the game {(rock ? "hit the rock" : terrain ? "hit the ground" : "hit nothing it reported")}{(agree ? "" : $" - a miss (the check expected dirt at {MHR_Mover.Fmt(_tPredPt)}; it stood at {MHR_Mover.Fmt(base.transform.position)} facing {base.transform.eulerAngles.y:F0} deg{(broke ? ", a part broke" : "")})")}");
			if (agree)
			{
				_tPredMiss = 0;
			}
			else if (++_tPredMiss >= 3 && !_tCheckOff)
			{
				_tCheckOff = true;
				if (CompanionsPlugin.LogNormal) LogInfo("The swing check got it wrong 3 times in a row - turned off for this vein (facing straight along the trench)");
			}
		}
		bool digShown = digProg && _tDigMeasured;   // pop test B4: not an old number on a swing that didn't dig
		_tDigMeasured = false;
		string what = (broke ? (_vLastAlive - _vParts.Count) + " part(s) broke" : rock ? "hit the rock" : "") + (digShown ? (rock ? ", " : "") + "dug " + (lowered * 100f).ToString("F0") + " cm" : "");
		if (CompanionsPlugin.LogDetailed && what.Length > 0) Log("Vein progress: " + what);
		_tDigOK = false;
		TFor why = _tSwingFor;
		_tSwingFor = TFor.None;
		switch (why)
		{
		case TFor.Obstacle:
			OJudge();
			break;
		case TFor.Dig:
		{
			bool prog = digProg || lowered2 >= VDigMin || rock;
			_tItemNoProg = prog ? 0 : _tItemNoProg + 1;
			// part 1 T2: a tree, bush, stump or loose rock ate the swing - that goes first
			if (!prog && OEaten(OCtx.Trench))
			{
				_tItemNoProg = 0;
				break;
			}
			// part 1 T6: it was a tree it already left for lack of an axe - this spot is left for an axe too
			if (!prog && ONoAxeSpot(_tHitObj, _tQ))
			{
				break;
			}
			// part 1 T4: one step to the side before the spot is closed
			if (_tItemNoProg >= TNoProgSwings && _tQ != null && !_tQ.Done && TSideOnce(_tKey, _tQ.P, $"{TNoProgSwings} dig swings at spot {_tQ.Idx + 1} did nothing; the last hit {(terrain ? "the ground" : "nothing it reported")}"))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings && _tQ != null && !_tQ.Done)
			{
				TSpotDone(_tQ, $"won't go lower ({TNoProgSwings} swings in a row without progress) - leaving it at {GroundAt(_tQ.P):F1}", keep: true);
				TNextSpot();
			}
			break;
		}
		case TFor.BlockDirt:
			if (digProg)
			{
				TBlockCleared($"dug the dirt in front {lowered * 100f:F0} cm");
			}
			else if (!rock && OEaten(OCtx.Trench))
			{
				_bNoProg = 0;
			}
			else if (_bNoProg + 1 >= TNoProgSwings && _tQ != null && TSideOnce(_tKey, _tQ.P, $"{TNoProgSwings} swings at the dirt in front did nothing"))
			{
				_bNoProg = 0;
			}
			else if (++_bNoProg >= TNoProgSwings && _tQ != null)
			{
				TBlockUnknown(_tQ, $"dirt in front that won't dig ({TNoProgSwings} swings)");
			}
			break;
		case TFor.BlockObj:
		{
			float h = TObjHealth(_bGo);
			bool prog = TGone(_bGo) || rock || (_bHealth == -1f && h >= 0f) || (h >= 0f && _bHealth >= 0f && h < _bHealth - 0.01f);
			_bHealth = h;
			if (prog)
			{
				_bNoProg = 0;
			}
			else if (++_bNoProg >= TNoProgSwings && _tQ != null)
			{
				// list v2 S1 (log ff6c8861: Comp gave spot 37 up for itself and the other two walked to it in turn): rock in
				// front that takes no damage closes the spot for everyone, and it stays closed when the plan is made again
				if (_bKind == TBlock.Rock)
				{
					TSpotDone(_tQ, $"rock in front that takes no damage ({TNoProgSwings} swings) - closed for everyone", keep: true);
				}
				TBlockUnknown(_tQ, $"{_bKind.ToString().ToLowerInvariant()} in front that takes no damage ({TNoProgSwings} swings)");
			}
			break;
		}
		case TFor.PopDig:
		{
			bool freed = _pPart == null || !TPartAlive(_pPart, out VPart pp) || !pp.Dirt;
			bool prog = digProg || freed || rock;
			_tItemNoProg = prog ? 0 : _tItemNoProg + 1;
			if (!prog && OEaten(OCtx.Other))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings && TSideOnce(_tKey, _tQPoint, $"{TNoProgSwings} dig swings under \"{_pPart?.name}\" did nothing; the last hit {(terrain ? "the ground" : "nothing it reported")}"))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings)
			{
				// part 2 M4: that spot is left, the next one for this part (the part is mined only with none left)
				if (_pHasSpot)
				{
					_vStuck.Add(_pSpot.Key);
				}
				_pHasSpot = false;
				TBeginItem();
				if (CompanionsPlugin.LogDetailed) Log($"The dirt under \"{_pPart?.name}\" won't go lower ({TNoProgSwings} swings) - leaving that spot (the ground at the spot {MHR_Mover.Fmt(_tQPoint)} is {GroundAt(_tQPoint):F2}, the piece's bottom {(_pPart != null && TPartAlive(_pPart, out VPart pb) ? pb.MinY.ToString("F2") : "?")}, the water floor {TWaterFloor:F1}; the ground there is {TLoweredSoFar(_tQPoint)})");
			}
			break;
		}
		case TFor.PopMine:
			_tItemNoProg = rock ? 0 : _tItemNoProg + 1;
			if (!rock && OEaten(OCtx.Other))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings && TSideOnce(_tKey, _vSwingAim, $"{TNoProgSwings} swings at \"{_pPart?.name}\" took no health off it; the last hit {(terrain ? "the ground" : "nothing it reported")}"))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings)
			{
				if (_pStage == 3)
				{
					if (CompanionsPlugin.LogNormal) LogInfo($"The part to pop it with takes no damage ({TNoProgSwings} swings) - the mining lap");
					TLapStart();
				}
				else
				{
					TPopLeave($"{TNoProgSwings} swings with no health off it (aimed at {MHR_Mover.Fmt(_vSwingAim)}, {Vector3.Distance(TOrigin(), _vSwingAim):F1} m from the swing's start; the last swing hit {(rock ? "the rock" : terrain ? "the ground" : "nothing it reported")})");
				}
			}
			break;
		case TFor.Lap:
			_tItemNoProg = rock ? 0 : _tItemNoProg + 1;
			if (!rock && OEaten(OCtx.Other))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings && TSideOnce(_tKey, _vSwingAim, $"{TNoProgSwings} swings at a part took no health off it; the last hit {(terrain ? "the ground" : "nothing it reported")}"))
			{
				_tItemNoProg = 0;
				break;
			}
			if (_tItemNoProg >= TNoProgSwings)
			{
				_lIdx++;
				TBeginItem();
			}
			break;
		case TFor.ExitMine:
			_tItemNoProg = rock ? 0 : _tItemNoProg + 1;
			if (broke || _tItemNoProg >= TNoProgSwings)
			{
				XAfterMine();
			}
			break;
		}
	}

	// ---------- S6: getting out ----------
	private const int XRestFull = 1;

	private const int XClimbTry = 2;

	private const int XClimbing = 3;

	private const int XSteps = 4;

	private const int XStepsRunning = 5;

	private const int XMine = 6;

	private const string XTag = "VeinOut";

	// fix 4: the height it has to get up to. The place it's heading for, but never above the highest ground in the first
	// 16 m toward it (16 m is how far the general climb looks): a chest or a person up a hill beyond the pit's edge
	// doesn't make the pit look deeper than it is.
	private float XTargetY(Vector3 me, Vector3 dest)
	{
		Vector3 d = dest - me;
		d.y = 0f;
		float dist = d.magnitude;
		if (dist < 1f)
		{
			return dest.y;
		}
		d /= dist;
		float top = dist <= 16f ? dest.y : float.MinValue;
		for (float t = 1f; t <= Mathf.Min(dist, 16f); t += 1f)
		{
			top = Mathf.Max(top, GroundAt(me + d * t));
		}
		return Mathf.Min(dest.y, top);
	}

	// the highest ground in the first 16 m along dir
	private float XRiseAlong(Vector3 me, Vector3 dir)
	{
		float top = float.MinValue;
		for (float t = 1f; t <= 16f; t += 1f)
		{
			top = Mathf.Max(top, GroundAt(me + dir * t));
		}
		return top;
	}

	// S6. In a hole = the place it's heading for (dest) is 1.5 m or more above its feet. Out = its feet within 0.5 m of the
	// target height. True = getting out has started; false = not in a hole (plain walking).
	private bool XBegin(string why, Vector3 dest)
	{
		Vector3 me = base.transform.position;
		return XBeginAt(why, me, FlatNorm(dest - me, base.transform.forward), XTargetY(me, dest), dest);
	}

	// list v2 H2: the trench's outer rim beside it - the highest ground 1, 2 and 3 m out on the side away from the
	// vein's middle
	private float XOuterRim(Vector3 me, Vector3 mid, out Vector3 outDir)
	{
		outDir = FlatNorm(me - mid, base.transform.forward);
		float top = float.MinValue;
		for (float t = 1f; t <= 3f; t += 1f)
		{
			top = Mathf.Max(top, GroundAt(me + outDir * t));
		}
		return top;
	}

	// list v2 H2: getting out to the outer rim beside it (never to the height of a far place). True = it has started;
	// false = its feet are less than TExitDepth below that rim (plain walking)
	private bool XBeginOuter(string why, Vector3 mid)
	{
		Vector3 me = base.transform.position;
		float rim = XOuterRim(me, mid, out Vector3 outDir);
		if (rim - me.y < TExitDepth)
		{
			return false;
		}
		MHR_Mover.Release(_ai);   // the walk that gave up is over - the mover starts clean after the getting-out
		return XBeginAt(why, me, outDir, rim, me + outDir * 3f);
	}

	private bool XBeginAt(string why, Vector3 me, Vector3 outDir, float rimY, Vector3 dest)
	{
		_xOut = outDir;
		_xRimY = rimY;
		_xRim = new Vector3(dest.x, _xRimY, dest.z);
		float depth = _xRimY - me.y;
		if (depth < TExitDepth)
		{
			_xActive = false;
			return false;
		}
		_xActive = true;
		_xTries = 0;
		_xMineTried = false;
		_xEquipTries = 0;
		_xDirsTried = 0;
		_xFinalPick = false;
		_xSince = _tClock;
		_xStage = XRestFull;
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"Getting out {why}: at {MHR_Mover.Fmt(me)}, {depth:F1} m below where it has to get up to (height {_xRimY:F1}, heading for {MHR_Mover.Fmt(dest)}) - resting to full, then the climb");
		return true;
	}

	private void XTick()
	{
		Vector3 me = base.transform.position;
		if (me.y >= _xRimY - 0.5f && _xStage != XStepsRunning)
		{
			_xActive = false;
			_tWalkSince = _tClock;   // the walk to the spot starts its time again
			if (CompanionsPlugin.LogNormal) LogInfo($"Out at {MHR_Mover.Fmt(me)} (the height to reach was {_xRimY:F1})");
			return;
		}
		switch (_xStage)
		{
		case XRestFull:
			_ai.StopMoving();
			if (_stamina == null || _stamina.Stamina >= 0.99f * Mathf.Max(1f, _stamina.MaxStamina))
			{
				_xStage = XClimbTry;
			}
			return;
		case XClimbTry:
			// test review B4: one try of the general climb, then the steps (log ceead815: it stopped short 20 times in 24)
			if (_xTries >= XMaxClimbs)
			{
				if (CompanionsPlugin.LogNormal) LogInfo("The general climb didn't get it out - cutting steps");
				_xStage = XSteps;
				return;
			}
			if (MHR_Mover.ClimbOutNow(_ai, _xRim, XTag))
			{
				_xTries++;
				_xSince = _tClock;
				_xStage = XClimbing;
				if (CompanionsPlugin.LogNormal) LogInfo("Climbing out (the general climb, one try)");
				return;
			}
			if (CompanionsPlugin.LogNormal) LogInfo(_xTries == 0 ? "The general climb finds no way up from here (too deep or rock all round for it) - cutting steps" : "The general climb has no other way up - cutting steps");
			_xStage = XSteps;
			return;
		case XClimbing:
			if (MHR_Mover.ForcedClimbing(_ai) && _tClock - _xSince < 25f)
			{
				MHR_Mover.Go(_ai, Time.deltaTime, _xRim, 1.0f, true, XTag);
				return;
			}
			// part 1 G1: after the climb it's out when it's less than TExitDepth below the height to reach - the number
			// that starts a getting-out (log 97d599fc: 1.0 m below a spot 15 m away sent Gunter cutting steps on open ground)
			if (_xRimY - me.y < TExitDepth)
			{
				_xActive = false;
				_tWalkSince = _tClock;
				_ai.StopMoving();
				if (CompanionsPlugin.LogNormal) LogInfo($"Out after the climb at {MHR_Mover.Fmt(me)}: {_xRimY - me.y:F1} m below the height to reach ({_xRimY:F1}), under {TExitDepth:F1} m - walking on");
				return;
			}
			if (CompanionsPlugin.LogNormal) LogInfo($"The climb didn't get it out (feet {me.y:F1}, the rim {_xRimY:F1}) - resting to full first");
			_xStage = XRestFull;
			return;
		case XSteps:
			XStartSteps(me);
			return;
		case XStepsRunning:
			if (!_pitActive)
			{
				if (_pitDropped)
				{
					// item D: that direction didn't work - pick again from where it stands
					_pitDropped = false;
					if (CompanionsPlugin.LogNormal) LogInfo($"Steps that way didn't get it out ({_pitDropWhy}) - picking another direction from {MHR_Mover.Fmt(me)}");
					_xStage = XSteps;
					return;
				}
				_xActive = false;   // out (PitStep said so), or stuck and waiting (PitStuck)
				_tWalkSince = _tClock;
			}
			return;
		default:
			XMineTick();
			return;
		}
	}

	// the way up for the steps: outward, the ladder head room at 1 m and 2 m out, the wall dirt first; the lowest rim
	private void XStartSteps(Vector3 me)
	{
		// the steps are cut with the pickaxe: in hand first (after a vein the loadout may have gone back)
		ItemDrop.ItemData inHand = ReflectionHelper.GetRightItem(_humanoid);
		if (inHand == null || inHand.GetDamage().m_pickaxe <= 0f || Worn(inHand))
		{
			ItemDrop.ItemData pick = FindBestTool(3);
			if (pick == null || ++_xEquipTries > 10)
			{
				_pitVein = true;
				PitStuck(me, pick == null ? "no pickaxe to cut steps out with" : "couldn't get the pickaxe in hand to cut steps");
				_xActive = false;
				return;
			}
			EquipTool(pick);
			return;
		}
		// item D: after the one more pick that follows mining, there's nothing else to try
		if (_xFinalPick && _xDirsTried != 0)
		{
			_pitVein = true;
			PitStuck(me, "boxed in: the steps didn't get it out in any direction, even after mining the rock in the way");
			_xActive = false;
			return;
		}
		// item D: only directions pointing away from the nearest rock piece (the way out of a trench is its outer wall,
		// which is dirt), each tried once per getting-out. Among those, the one nearest the place it's heading for whose
		// ground (16 m) gets up to the height it needs; if none does, the one whose ground gets highest (1.5 m up at least).
		Vector3 fromRock = Vector3.zero;
		float rockD = float.MaxValue;
		foreach (VPart p in _vParts)
		{
			if (p.C == null)
			{
				continue;
			}
			Vector3 cp = SafeClosestPoint(p.C, me + Vector3.up);
			float d = Flat(cp, me);
			if (d < rockD)
			{
				rockD = d;
				fromRock = me - cp;
			}
		}
		fromRock.y = 0f;
		bool haveRock = rockD < float.MaxValue && fromRock.sqrMagnitude > 0.0001f;
		if (haveRock)
		{
			fromRock.Normalize();
		}
		Vector3 best = Vector3.zero, bestLow = Vector3.zero;
		int bestK = -1, bestLowK = -1, nTried = 0, nToward = 0, nBlocked = 0;
		float bestDot = float.MinValue, bestTop = float.MinValue, bestRim = _xRimY;
		for (int k = 0; k < 16; k++)
		{
			if ((_xDirsTried & (1 << k)) != 0)
			{
				nTried++;
				continue;
			}
			Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
			if (haveRock && Vector3.Dot(d, fromRock) <= 0f)
			{
				nToward++;
				continue;
			}
			Vector3 p1 = me + d, p2 = me + d * 2f;
			p1.y = GroundAt(p1);
			p2.y = GroundAt(p2);
			if (!MHR_Mover.HeadRoomAt(p1, THead) || !MHR_Mover.HeadRoomAt(p2, THead))
			{
				nBlocked++;
				continue;
			}
			if (TPredict(me, d, out _) != TFirst.Dirt)
			{
				nBlocked++;
				continue;
			}
			float top = XRiseAlong(me, d);
			float dot = Vector3.Dot(d, _xOut);
			if (top >= _xRimY - 0.5f)
			{
				if (dot > bestDot)
				{
					bestDot = dot;
					best = d;
					bestK = k;
				}
			}
			else if (top >= me.y + TExitDepth && top > bestTop)
			{
				bestTop = top;
				bestLow = d;
				bestLowK = k;
			}
		}
		if (best == Vector3.zero && bestLow != Vector3.zero)
		{
			best = bestLow;
			bestK = bestLowK;
			bestRim = bestTop;
			if (CompanionsPlugin.LogNormal) LogInfo($"No way up to {_xRimY:F1} that's dirt with room overhead - taking the highest there is, up to {bestTop:F1}");
		}
		if (best != Vector3.zero)
		{
			_xDirsTried |= 1 << bestK;
			_xStage = XStepsRunning;
			if (CompanionsPlugin.LogDetailed) Log($"Step direction {bestK * 22.5f:F0} deg: away from the nearest rock{(haveRock ? $" ({rockD:F1} m off)" : " (none left)")}, nearest of those to where it's heading ({nTried} tried before, {nToward} point at the rock, {nBlocked} no head room or not dirt first)");
			PitBegin(best, vein: true, Mathf.Min(bestRim, _xRimY));
			return;
		}
		if (!_xMineTried)
		{
			_xMineTried = true;
			_xStage = XMine;
			TBeginItem();
			if (CompanionsPlugin.LogNormal) LogInfo($"No step direction left ({nTried} tried, {nToward} point at the rock, {nBlocked} no head room or not dirt first) - mining the rock in the way first, then one more pick");
			return;
		}
		_pitVein = true;
		PitStuck(me, "boxed in: no way up that's dirt with room overhead, even after mining the rock in the way");
		_xActive = false;
	}

	// the rock in the way out (over it or in front): the nearest vein part within a swing
	private void XMineTick()
	{
		Collider best = null;
		float bd = float.MaxValue;
		Vector3 o = TOrigin();
		foreach (VPart p in _vParts)
		{
			float d = Vector3.Distance(o, TPartAim(p.C));
			if (d < bd)
			{
				bd = d;
				best = p.C;
			}
		}
		if (best == null || bd > TMineReach)
		{
			XAfterMine();
			return;
		}
		_ai.StopMoving();
		TRockSwing(TPartAim(best), TFor.ExitMine);
	}

	// item D: the rock in the way was mined (once): one more pick, from all 16 directions again
	private void XAfterMine()
	{
		_xDirsTried = 0;
		_xFinalPick = true;
		_xStage = XSteps;
	}

	// after a vein (all broken, or given up) and the ore is picked up: out of the trench first, before anything else
	internal bool VeinExitTick()
	{
		if (!_xPendingAfter && !_xActive)
		{
			return false;
		}
		if (VeinWorking || _ai == null || _character == null || _humanoid == null)
		{
			return false;
		}
		if (_humanoid.InAttack())
		{
			return true;
		}
		if (!_xActive)
		{
			// list v2 H6: the same order as a walk to a spot - whatever moves it next walks first (the normal route, the
			// mover's own climb); its own getting-out only once the mover has given up, up to the outer rim beside it.
			// Over once it is XAfterClear outside the vein's box.
			Vector3 at = base.transform.position;
			if (Flat(_vUnion.ClosestPoint(at), at) > XAfterClear)
			{
				_xPendingAfter = false;
				return false;
			}
			if (!MHR_Mover.GaveUp(_ai) || !XBeginOuter("after the vein: no way out by walking", _vUnion.center))
			{
				return false;   // still watching (a give-up on open ground starts nothing)
			}
			_xPendingAfter = false;
		}
		if (!_catchingBreath)
		{
			_tClock += Time.deltaTime;
		}
		if (_xStage == XMine)
		{
			XAfterMine();   // the vein's gone: nothing to mine
		}
		XTick();
		return _xActive;
	}
}
