using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97: a split vein outdoors mined the way a player does. The simple way John approved on 2026-11-15
// (reviews/1.5.97-test2-review-and-simple-plan.md, revised: no laps):
//  - The pickaxe in hand first, checked every update (VeinTick).
//  - The rock you can see: a part is visible when its own rock surface (a ray down onto that part's collider, 0.5 m grid
//    over its box) is above the ground. 16 level rays from 20 m out find its edge; the trench line runs 0.75 m outside
//    it, a spot about every 1 m.
//  - Start at the nearest spot (skipping spots at the water line) and dig it straight down to the base of the rock there
//    (a level ray at the floor + 0.3 m toward the rock finds none). Deeper than 1.2 m: 1 m steps cut out there.
//  - Then round the ore, toward the side with the lower ground, each spot down to its own base, standing in the trench
//    already dug.
//  - Then the dirt under the parts still held by dirt, then mine it (held parts first). Every break makes the game pop
//    whatever is no longer held.
//  - No swing check before swinging: it walks until it is beside the spot (within 1.3 m flat, its feet no more than 0.7 m
//    above the spot; for the rock, the swing's start within 1.6 m of it), faces it, and swings. A spot or part is left
//    after 3 swings in a row without progress, or when it can't get beside it in 5 s. AllowDig still refuses digs below
//    the water line (31 m) and below the depth that spot is dug to, and away from the vein.
public partial class HarvestController
{
	private sealed class TPt
	{
		public Vector3 P;

		public Vector3 Edge;

		public Vector3 Out;

		public float Surface;

		public bool Done;
	}

	private const int TPhaseTrench = 0;

	private const int TPhaseStair = 1;

	private const int TPhaseUnder = 2;

	private const int TPhaseMine = 3;

	// a swing only at ground this high or higher: one swing takes up to 0.5 m, so nothing goes below 30.5 (water at 30)
	private const float TWater = 31f;

	private const float TStairDepth = 1.2f;

	private const float TUnderReach = 1.6f;

	// W5 (John 2026-11-14): 5 s to get to a stand spot before trying the next
	private const float TWalkTime = 5f;

	// W2: it swings once it faces within this of the spot (after 1 s of turning it swings anyway)
	private const float TFaceDeg = 10f;

	// "there" for digging: within this flat of the spot, and its feet no more than TAbove over the spot's ground
	private const float TNear = 1.3f;

	private const float TAbove = 0.7f;

	// "there" for mining: the swing's start (1 m up) within this of the rock
	private const float TMineReach = 1.6f;

	// a spot or part is left after this many swings in a row without progress
	private const int TNoProgSwings = 3;

	// round the ore: +1 clockwise, -1 anticlockwise, 0 = still on the start spot
	private int _tDir;

	// this circle: spots it got to and swung at, spots it couldn't get to
	private int _tCircleReached;

	private int _tCircleMissed;

	private int _tLastVisited = -1;

	private int _tPhase;

	private readonly List<TPt> _tPts = new List<TPt>();

	private readonly HashSet<Collider> _tVisible = new HashSet<Collider>();

	private Vector3 _tCenter;

	private int _tStart = -1;

	private int _tCur = -1;

	private int _tDoneCount;

	private bool _tStairDone;

	private int _tStairStep;

	private float _tStairBase;

	private int _tUnderEmpty;

	private bool _tHasSpot;

	private VSpot _tSpot;

	private bool _tAnyWard;

	private bool _tAnyCantDig;

	private int _tItemNoProg;

	private Vector3 _tQ;

	private float _tQBefore;

	private float _tDigMinY = TWater;

	private float _tFaceSince = -1f;

	private void TReset()
	{
		_tPhase = TPhaseTrench;
		_tPts.Clear();
		_tVisible.Clear();
		_tStart = -1;
		_tCur = -1;
		_tDoneCount = 0;
		_tStairDone = false;
		_tStairStep = 0;
		_tUnderEmpty = 0;
		_tHasSpot = false;
		_tAnyWard = false;
		_tAnyCantDig = false;
		_tDigMinY = TWater;
		_tDir = 0;
		_tCircleReached = 0;
		_tCircleMissed = 0;
		_tLastVisited = -1;
		TBeginItem();
	}

	private void TBeginItem()
	{
		_tItemNoProg = 0;
		_vWalkSince = -1f;
		_tFaceSince = -1f;
	}

	private static float Flat(Vector3 a, Vector3 b)
	{
		float dx = a.x - b.x, dz = a.z - b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	// ---------- T1: the rock you can see and the trench line round it ----------
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

	private IEnumerable<Collider> AllParts()
	{
		foreach (VPart p in _vParts)
		{
			yield return p.C;
		}
	}

	private void TOutline()
	{
		_tPts.Clear();
		_tVisible.Clear();
		foreach (VPart p in _vParts)
		{
			if (PartVisible(p))
			{
				_tVisible.Add(p.C);
			}
		}
		bool noneVisible = _tVisible.Count == 0;
		if (noneVisible)
		{
			foreach (VPart p in _vParts)
			{
				_tVisible.Add(p.C);
			}
		}
		Vector3 sum = Vector3.zero;
		int n = 0;
		foreach (VPart p in _vParts)
		{
			if (_tVisible.Contains(p.C))
			{
				sum += p.Center;
				n++;
			}
		}
		_tCenter = n > 0 ? sum / n : _vUnion.center;
		_tCenter.y = GroundAt(_tCenter);
		Vector3[] edge = new Vector3[16];
		bool[] has = new bool[16];
		int found = 0;
		for (int k = 0; k < 16; k++)
		{
			Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
			Vector3 start = _tCenter + dir * 20f;
			start.y = GroundAt(start) + 0.5f;
			bool ok = RayParts(start, -dir, 20f, _tVisible, out Vector3 hp, out _);
			if (!ok)
			{
				// the ground slopes: the ray from 20 m out passed over or under the rock - try level with the centre's ground
				start.y = _tCenter.y + 0.5f;
				ok = RayParts(start, -dir, 20f, _tVisible, out hp, out _);
			}
			if (ok)
			{
				hp.y = GroundAt(hp);
				edge[k] = hp;
				has[k] = true;
				found++;
			}
		}
		// the trench line: 0.75 m out from each edge point, clockwise, a point about every 1 m between them
		List<int> ks = new List<int>();
		for (int k = 0; k < 16; k++)
		{
			if (has[k])
			{
				ks.Add(k);
			}
		}
		for (int i = 0; i < ks.Count; i++)
		{
			Vector3 ea = edge[ks[i]], eb = edge[ks[(i + 1) % ks.Count]];
			Vector3 oa = FlatDir(ea - _tCenter, ks[i]), ob = FlatDir(eb - _tCenter, ks[(i + 1) % ks.Count]);
			Vector3 pa = ea + oa * 0.75f, pb = eb + ob * 0.75f;
			int steps = ks.Count == 1 ? 1 : Mathf.Max(1, Mathf.CeilToInt(Flat(pa, pb) / 1.0f));
			for (int j = 0; j < steps; j++)
			{
				float t = (float)j / steps;
				TPt pt = new TPt
				{
					Edge = Vector3.Lerp(ea, eb, t),
					Out = Vector3.Slerp(oa, ob, t).normalized
				};
				pt.P = Vector3.Lerp(pa, pb, t);
				pt.P.y = GroundAt(pt.P);
				pt.Surface = pt.P.y;
				_tPts.Add(pt);
			}
		}
		if (_tPts.Count == 0)
		{
			_tPhase = TPhaseMine;
			if (CompanionsPlugin.LogNormal) LogInfo($"Vein \"{_target.name}\": couldn't find the edge of the rock - mining it from where it can reach");
			return;
		}
		// the start: the nearest spot (straight line), skipping spots already at the water line
		_tStart = -1;
		float bd = float.MaxValue;
		for (int pass = 0; pass < 2 && _tStart < 0; pass++)
		{
			for (int i = 0; i < _tPts.Count; i++)
			{
				if (pass == 0 && _tPts[i].P.y < TWater)
				{
					continue;
				}
				float d = Flat(_tPts[i].P, base.transform.position);
				if (d < bd)
				{
					bd = d;
					_tStart = i;
				}
			}
		}
		_tCur = _tStart;
		_vFirstSide = SideOf(_tPts[_tStart].P);
		_vSide = _vFirstSide;
		_vUnionAt = Time.time;
		_tPhase = TPhaseTrench;
		TBeginItem();
		if (CompanionsPlugin.LogNormal) LogInfo($"Vein \"{_target.name}\": {(noneVisible ? "no part shows above the ground (using all " + _vParts.Count + ")" : _tVisible.Count + " of " + _vParts.Count + " parts show above the ground")}, {_vDirtCount} held by dirt; its edge found on {found} of 16 bearings - a trench of {_tPts.Count} points round it, starting at {MHR_Mover.Fmt(_tPts[_tStart].P)}");
		if (CompanionsPlugin.LogDetailed)
		{
			for (int k = 0; k < 16; k++)
			{
				Log(has[k] ? $"  edge {k * 22.5f:F0} deg: {MHR_Mover.Fmt(edge[k])}" : $"  edge {k * 22.5f:F0} deg: no rock found");
			}
		}
	}

	private static Vector3 FlatDir(Vector3 v, int k)
	{
		v.y = 0f;
		return v.sqrMagnitude > 0.01f ? v.normalized : Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
	}

	// ---------- the simple way (John 2026-11-15): walk to the spot, face it, swing like a player ----------
	// "There" = beside the spot, within one swing: within 1.3 m flat, and its feet no more than 0.7 m above the spot's
	// ground (a swing from the rim can't reach deeper than that - it steps down into the hole instead). It walks toward
	// the spot itself with the normal movement. 1 = swung (or swinging), 0 = on its way / turning, -1 = it couldn't get
	// there in 5 s.
	private int TWork(Vector3 q, bool dig, long key)
	{
		Vector3 me = base.transform.position;
		bool there = dig ? Flat(me, q) <= TNear && me.y - q.y <= TAbove : Vector3.Distance(me + Vector3.up, q) <= TMineReach;
		if (!there)
		{
			_tFaceSince = -1f;
			if (_vWalkSince < 0f || Vector3.Distance(_vWalkFor, q) > 1.5f)
			{
				_vWalkSince = Time.time;
				_vWalkFor = q;
			}
			if (Time.time - _vWalkSince > TWalkTime)
			{
				_vWalkSince = -1f;
				return -1;
			}
			MHR_Mover.Go(_ai, Time.deltaTime, q, 0.4f, false, "Vein");
			return 0;
		}
		_ai.StopMoving();
		_vWalkSince = -1f;
		// turn to it, then swing (after 1 s of turning it swings anyway)
		if (!TFacing(q))
		{
			if (_tFaceSince < 0f)
			{
				_tFaceSince = Time.time;
			}
			if (Time.time - _tFaceSince < 1f)
			{
				VeinFace(q);
				return 0;
			}
		}
		_tFaceSince = -1f;
		if (dig)
		{
			_tQ = q;
			_tQBefore = GroundAt(q);
		}
		VeinSwing(q, dig, key);
		return 1;
	}

	// W2: facing the spot within 10 degrees (flat)
	private bool TFacing(Vector3 q)
	{
		Vector3 d = q - base.transform.position;
		d.y = 0f;
		if (d.sqrMagnitude < 0.0001f)
		{
			return true;
		}
		Vector3 f = base.transform.forward;
		f.y = 0f;
		return Vector3.Angle(f, d) <= TFaceDeg;
	}

	// ---------- the trench: the start spot straight down, then round the ore ----------
	private void TrenchTick()
	{
		if (_tPts.Count == 0 || _tCur < 0)
		{
			_tPhase = TPhaseMine;
			return;
		}
		TPt c = _tPts[_tCur];
		if (c.Done)
		{
			TNextPoint();
			return;
		}
		_vSide = SideOf(c.P);
		Vector3 q = c.P;
		float g = GroundAt(q);
		q.y = g;
		if (g < TWater)
		{
			TPointDone(c, $"at the water line (ground {g:F1}, the water level is 30) - counts as dug down");
			return;
		}
		// the base of the rock you can see here: a level ray at the floor + 0.3 m toward the rock
		Vector3 o = c.Edge + c.Out * 1.5f;
		o.y = g + 0.3f;
		if (!RayParts(o, -c.Out, 3f, AllParts(), out _, out _))
		{
			TPointDone(c, $"down to the base of the rock here, {g:F1} ({c.Surface - g:F1} m down)");
			return;
		}
		if (!PrivateArea.CheckAccess(c.P, 0f, flash: false))
		{
			_tAnyWard = true;
			TPointDone(c, "someone else's ward covers it");
			return;
		}
		if (Location.IsInsideNoBuildLocation(c.P))
		{
			_tAnyCantDig = true;
			TPointDone(c, "a no-build area");
			return;
		}
		_tDigMinY = TWater;
		int r = TWork(q, dig: true, -1L);
		if (r < 0)
		{
			_tCircleMissed++;
			TPointDone(c, $"couldn't get to it in {TWalkTime:F0}s - leaving it");
			return;
		}
		if (r > 0)
		{
			_tLastVisited = _tCur;
		}
	}

	private void TPointDone(TPt c, string why)
	{
		c.Done = true;
		_tDoneCount++;
		if (_tLastVisited == _tCur)
		{
			_tCircleReached++;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {_tCur + 1}/{_tPts.Count} at {MHR_Mover.Fmt(c.P)}: {why}");
		if (_tCur == _tStart && _tDir == 0)
		{
			// the start spot is down: the stair if it's deep, then round the ore toward the easier side
			if (!_tStairDone && c.Surface - GroundAt(c.P) > TStairDepth)
			{
				_tStairDone = true;
				_tStairStep = 1;
				_tStairBase = GroundAt(c.P);
				_tPhase = TPhaseStair;
				TBeginItem();
				if (CompanionsPlugin.LogNormal) LogInfo($"The start spot at {MHR_Mover.Fmt(c.P)} is {c.Surface - _tStairBase:F1} m deep - cutting 1 m steps out of it");
				return;
			}
			TPickDir();
		}
		TNextPoint();
	}

	// left or right round the ore: toward the neighbour with the lower ground (less dirt to dig); clockwise on a tie
	private void TPickDir()
	{
		int n = _tPts.Count;
		if (n < 2)
		{
			_tDir = 1;
			return;
		}
		float gNext = GroundAt(_tPts[(_tStart + 1) % n].P), gPrev = GroundAt(_tPts[(_tStart - 1 + n) % n].P);
		_tDir = gPrev < gNext - 0.01f ? -1 : 1;
		if (CompanionsPlugin.LogNormal) LogInfo($"Going round the ore {(_tDir > 0 ? "clockwise" : "anticlockwise")} - the ground is lower that way ({Mathf.Min(gNext, gPrev):F1} against {Mathf.Max(gNext, gPrev):F1})");
	}

	private void TNextPoint()
	{
		int n = _tPts.Count;
		if (_tDir == 0)
		{
			TPickDir();
		}
		int j = _tCur;
		for (int i = 1; i <= n; i++)
		{
			j = ((j + _tDir) % n + n) % n;
			if (!_tPts[j].Done)
			{
				_tCur = j;
				TBeginItem();
				return;
			}
		}
		// all the way round. Not one spot it could get to, and in a hole: the pit exit (it scans for the vein again after)
		if (_tCircleReached == 0 && _tCircleMissed > 0 && _character != null && _character.IsOnGround() && !InDungeonAt(base.transform.position) && InHole())
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to any spot round \"{_target.name}\" from the hole it's in - getting out of the hole first");
			_vFor = null;   // once out it picks the vein again and starts a new ring
			PitStart();
			return;
		}
		if (_tCircleReached == 0 && _tCircleMissed > 0)
		{
			SayGaveUp(GiveUpWhy.OutOfReach, $"couldn't get to any of the {n} spots round it");
			VeinLeave();
			return;
		}
		_tPhase = TPhaseUnder;
		_tUnderEmpty = 0;
		_tHasSpot = false;
		TBeginItem();
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_target.name}\" is done ({n} spots, {_tCircleMissed} it couldn't get to) - clearing under it");
	}

	// the stair: step n is the ground 0.25 + n m out from the start spot, cut down to the trench floor + n m, until a
	// step would be within 0.3 m of the ground there
	private Vector3 TStairPoint()
	{
		if (_tStart < 0 || _tStart >= _tPts.Count)
		{
			return Vector3.zero;
		}
		TPt c = _tPts[_tStart];
		return c.P + c.Out * (_tStairStep + 0.25f);
	}

	private void StairTick()
	{
		float target = _tStairBase + _tStairStep;
		Vector3 sc = TStairPoint();
		float g = GroundAt(sc);
		if (target >= g - 0.3f)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Steps out of the trench done ({_tStairStep - 1} step(s))");
			TStairEnd();
			return;
		}
		if (g <= target + 0.15f)
		{
			_tStairStep++;
			TBeginItem();
			return;
		}
		sc.y = g;
		_tDigMinY = Mathf.Max(target, TWater);
		if (TWork(sc, dig: true, -1L) < 0)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to step {_tStairStep} in {TWalkTime:F0}s - carrying on round the ore");
			TStairEnd();
		}
	}

	private void TStairEnd()
	{
		_tPhase = TPhaseTrench;
		_tCur = _tStart;
		TPickDir();
		TNextPoint();
	}

	// ---------- clearing under it ----------
	private void UnderTick()
	{
		if (_tPts.Count == 0)
		{
			_tPhase = TPhaseMine;
			return;
		}
		VeinSpots();
		TPt c = _tPts[_tCur];
		float floor = GroundAt(c.P);
		if (_tHasSpot && (_vStuck.Contains(_tSpot.Key) || GroundAt(_tSpot.P) <= floor + VDigMin || _tSpot.Part == null || !_tSpot.Part.gameObject.activeSelf || !StillHeld(_tSpot.Part)))
		{
			_tHasSpot = false;
		}
		if (!_tHasSpot)
		{
			float bestPart = float.MaxValue, bestSpot = float.MaxValue;
			foreach (VSpot s in _vSpots)
			{
				float fd = Flat(s.P, c.P);
				// a spot it has left is never picked again
				if (_vStuck.Contains(s.Key) || fd > TUnderReach || s.P.y <= floor + VDigMin || s.P.y < TWater)
				{
					continue;
				}
				float pd = Vector3.Distance(base.transform.position, s.PartCenter);
				if (pd < bestPart - 0.01f || (Mathf.Abs(pd - bestPart) <= 0.01f && fd < bestSpot))
				{
					bestPart = pd;
					bestSpot = fd;
					_tSpot = s;
					_tHasSpot = true;
				}
			}
			if (!_tHasSpot)
			{
				_tUnderEmpty++;
				if (_tUnderEmpty >= _tPts.Count)
				{
					_tPhase = TPhaseMine;
					_vPart = null;
					TBeginItem();
					if (CompanionsPlugin.LogNormal) LogInfo($"Nothing more to clear under \"{_target.name}\" from the trench ({_vDirtCount} part(s) still held by dirt) - mining it");
					return;
				}
				_tCur = ((_tCur + (_tDir == 0 ? 1 : _tDir)) % _tPts.Count + _tPts.Count) % _tPts.Count;
				TBeginItem();
				return;
			}
			TBeginItem();
		}
		if (!PrivateArea.CheckAccess(_tSpot.P, 0f, flash: false))
		{
			_tAnyWard = true;
			_vStuck.Add(_tSpot.Key);
			_tHasSpot = false;
			return;
		}
		_vSide = SideOf(c.P);
		_tDigMinY = Mathf.Max(floor, TWater);
		if (TWork(_tSpot.P, dig: true, _tSpot.Key) < 0)
		{
			if (CompanionsPlugin.LogDetailed) Log($"Couldn't get to the dirt at {MHR_Mover.Fmt(_tSpot.P)} under \"{_tSpot.Part.name}\" in {TWalkTime:F0}s - leaving it");
			_vStuck.Add(_tSpot.Key);
			_tHasSpot = false;
		}
	}

	private bool StillHeld(Collider part)
	{
		foreach (VPart p in _vParts)
		{
			if (p.C == part)
			{
				return p.Dirt;
			}
		}
		return false;
	}

	// ---------- mining it ----------
	private void MineTick()
	{
		if (_vPart == null || !_vPart.gameObject.activeSelf || _vPartLeft.ContainsKey(_vPart))
		{
			_vPart = VeinPickPart();
			TBeginItem();
			if (_vPart == null)
			{
				VeinGiveUp();
				return;
			}
		}
		Vector3 aim = SafeClosestPoint(_vPart, Eye());
		if (TWork(aim, dig: false, 0L) < 0)
		{
			VeinPartFail(GiveUpWhy.OutOfReach, $"couldn't get within a swing of it in {TWalkTime:F0}s");
		}
	}

	// ---------- after each swing: what did it do? ----------
	private void TJudge()
	{
		VeinScan();
		bool rock = false;
		string what = "";
		if (_vParts.Count < _vLastAlive)
		{
			rock = true;
			what = (_vLastAlive - _vParts.Count) + " part(s) broke";
			_vPart = null;
		}
		else if (_vHealth < _vLastHealth - 0.01f)
		{
			rock = true;
			what = "hit the rock";
		}
		if (_vDirtCount < _vLastDirt)
		{
			rock = true;
			what += (what.Length > 0 ? ", " : "") + (_vLastDirt - _vDirtCount) + " part(s) freed of dirt";
		}
		if (_vDigPending)
		{
			_vDigPending = false;
			float nowHit = GroundAt(_vDigAt);
			if (CompanionsPlugin.LogDetailed) Log($"Dig at {MHR_Mover.Fmt(_vDigAt)}: ground {_vDigGround:F2} -> {nowHit:F2} ({(_vDigGround - nowHit) * 100f:F0} cm)");
		}
		bool digProg = false;
		if (_vSwingDig)
		{
			float lowered = _tQBefore - GroundAt(_tQ);
			if (lowered >= VDigMin)
			{
				digProg = true;
				what += (what.Length > 0 ? ", " : "") + "dug " + (lowered * 100f).ToString("F0") + " cm";
				if (_tPhase == TPhaseUnder)
				{
					_tUnderEmpty = 0;
				}
			}
		}
		// mining counts only the rock; digging counts the dirt at the spot going down (or the rock, if the swing met it)
		bool prog = _tPhase == TPhaseMine ? rock : (rock || digProg);
		if (prog)
		{
			_tItemNoProg = 0;
			if (CompanionsPlugin.LogDetailed) Log("Vein progress: " + what);
		}
		else
		{
			_tItemNoProg++;
		}
		if (_tItemNoProg < TNoProgSwings)
		{
			return;
		}
		string detail = $"{TNoProgSwings} swings in a row without progress";
		switch (_tPhase)
		{
		case TPhaseTrench:
			if (_tCur >= 0 && _tCur < _tPts.Count && !_tPts[_tCur].Done)
			{
				TPointDone(_tPts[_tCur], $"won't go lower ({detail}) - leaving it at {GroundAt(_tPts[_tCur].P):F1}");
			}
			break;
		case TPhaseStair:
			if (CompanionsPlugin.LogNormal) LogInfo($"Step {_tStairStep} out of the trench: {detail} - carrying on round the ore");
			TStairEnd();
			break;
		case TPhaseUnder:
			if (_tHasSpot)
			{
				_vStuck.Add(_tSpot.Key);
				if (CompanionsPlugin.LogDetailed) Log($"The dirt at {MHR_Mover.Fmt(_tSpot.P)}: {detail} - leaving that spot");
				_tHasSpot = false;
			}
			TBeginItem();
			break;
		default:
			VeinPartFail(GiveUpWhy.OutOfReach, detail);
			break;
		}
	}
}
