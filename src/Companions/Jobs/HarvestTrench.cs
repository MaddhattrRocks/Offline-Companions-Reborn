using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97: a split vein outdoors mined the way a player does (John's approved plan T1-T7, 2026-11-14,
// reviews/1.5.97-vein-trench-plan.md):
//  T1 The rock you can see: a part is visible when its own rock surface (a ray down onto that part's collider, 0.5 m
//     grid over its box) is above the ground. From the visible parts' centre, a level ray along each of 16 bearings
//     (22.5 deg), starting 20 m out at the ground + 0.5 m and hitting only visible parts, finds the edge of the rock.
//     The trench line runs 0.75 m outside those 16 edge points (1.5 m wide, one swing's bowl), a point about every 1 m.
//  T2 Down at the start (the trench point nearest to it): before every swing it works out with the game's own swing
//     (origin, a ray every 4 deg over the arc, reach) what it will hit first, and swings only when that's the dirt it
//     wants. After each swing a level ray at the trench floor + 0.3 m, from 1.5 m outside the edge point toward the rock,
//     looks for the rock below: a vein part within 1.5 m past the edge = the rock carries on down, so it digs on; none =
//     the base you can see. Never below 30.5 m (it only swings at ground 31 m or higher: one swing takes up to 0.5 m;
//     the water level is 30) and it stops at the game's 8 m limit (3 swings that don't lower it).
//     Deeper than 1.2 m at the start: a stair of 1 m steps cut out of the trench there, so everyone can climb out.
//  T3 The trench round the rock: clockwise, each point down to its own visible base. A point is done at its base, at a
//     hard stop, after 3 tries without going lower, or 12 swings / 60 s without progress.
//  T4 Under it (trench done): the dirt inside the box of every part still held by dirt (the game's support test), that it
//     can reach from the trench (1.6 m), no deeper than the trench floor there. The held part nearest to it first, the
//     dirt nearest the trench first, round clockwise. Progress = dirt down 5 cm or more, or a part freed of dirt; a swing
//     that digs nothing or a wall caving in (more than 0.6 m from one swing) doesn't count.
//  T5 Mine it: a part still held first, nearest first, only from a spot where the swing meets the rock first (no
//     digging while mining). Every break makes the game pop whatever is no longer held.
//  T6/T7 What it can't free or reach is left; with nothing left it says why (buried too deep, out of reach, ward).
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

	private const float TCave = 0.6f;

	private const float TUnderReach = 1.6f;

	// W5 (John 2026-11-14): 5 s to get to a stand spot before trying the next
	private const float TWalkTime = 5f;

	// W2: it swings only once it faces within this of the spot, and gives a spot 1 s to turn
	private const float TFaceDeg = 10f;

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

	private int _tCand;

	private int _tItemTries;

	private int _tItemNoProg;

	private float _tItemProgAt;

	private Vector3 _tQ;

	private float _tQBefore;

	private float _tDigMinY = TWater;

	private readonly List<Vector3> _tMineStands = new List<Vector3>();

	private float _tPredAt = -1f;

	private int _tPredCand = -1;

	private Vector3 _tPredQ;

	private bool _tPredOk;

	private static int s_tSwingMask = -1;

	// the last swing check: its start height above the feet, and rays that hit nothing / the ground / this vein / other
	private float _tPredOrigin;

	private readonly int[] _tPredRays = new int[4];

	// W3: why each stand spot of the current point failed: hit nothing, ground elsewhere, rock first, other first,
	// couldn't get there, would have to swim, not facing in time
	private readonly int[] _tWhy = new int[7];

	private float _tFaceSince = -1f;

	private Vector3[] _tStands;

	private int _tStandsFor = -1;

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
		_tMineStands.Clear();
		TBeginItem();
	}

	private void TBeginItem()
	{
		_tCand = 0;
		_tItemTries = 0;
		_tItemNoProg = 0;
		_tItemProgAt = Time.time;
		_vWalkSince = -1f;
		_tPredAt = -1f;
		_tFaceSince = -1f;
		_tStandsFor = -1;
		System.Array.Clear(_tWhy, 0, _tWhy.Length);
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
		float bd = float.MaxValue;
		for (int i = 0; i < _tPts.Count; i++)
		{
			float d = Flat(_tPts[i].P, base.transform.position);
			if (d < bd)
			{
				bd = d;
				_tStart = i;
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

	// ---------- the game's own swing, worked out before swinging ----------
	// kind: 0 nothing, 1 the ground first, 2 this vein's rock first, 3 something else first. digPoint: where the game would
	// dig (its hit-point rule for the ground's hits), when the ground comes first.
	private bool PredictSwing(Vector3 face, out int kind, out Vector3 digPoint)
	{
		kind = 0;
		digPoint = Vector3.zero;
		ItemDrop.ItemData tool = ReflectionHelper.GetRightItem(_humanoid);
		Attack a = tool?.m_shared?.m_attack;
		if (a == null || _character == null)
		{
			return false;
		}
		if (s_tSwingMask < 0)
		{
			s_tSwingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
		}
		face.y = 0f;
		if (face.sqrMagnitude < 0.001f)
		{
			face = base.transform.forward;
			face.y = 0f;
		}
		Quaternion rot = Quaternion.LookRotation(face.normalized, Vector3.up);
		Transform joint = null;
		if (!string.IsNullOrEmpty(a.m_attackOriginJoint) && _character.GetVisual() != null)
		{
			joint = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
		}
		// the game's own start point (Attack.GetAttackOrigin): the origin joint on the model when the tool names one, else
		// the character's own position (its feet); then up by the attack height and sideways by the offset
		Vector3 jp = joint != null ? joint.position : base.transform.position;
		Vector3 origin = jp + Vector3.up * a.m_attackHeight + rot * Vector3.right * a.m_attackOffset;
		_tPredOrigin = origin.y - base.transform.position.y;
		_tPredRays[0] = _tPredRays[1] = _tPredRays[2] = _tPredRays[3] = 0;
		float half = a.m_attackAngle / 2f;
		float range = a.m_attackRange;
		Heightmap terrain = null;
		Vector3 first = Vector3.zero, sum = Vector3.zero, closest = Vector3.zero;
		float closestD = float.MaxValue;
		int count = 0;
		for (float ang = -half; ang <= half; ang += 4f)
		{
			Quaternion q2 = Quaternion.identity;
			if (a.m_attackType == Attack.AttackType.Vertical)
			{
				q2 = Quaternion.Euler(ang, 0f, 0f);
			}
			else if (a.m_attackType == Attack.AttackType.Horizontal)
			{
				q2 = Quaternion.Euler(0f, -ang, 0f);
			}
			Vector3 d = rot * (q2 * Vector3.forward);
			RaycastHit[] hits = a.m_attackRayWidth > 0f
				? Physics.SphereCastAll(origin, a.m_attackRayWidth, d, Mathf.Max(0f, range - a.m_attackRayWidth), s_tSwingMask, QueryTriggerInteraction.Ignore)
				: Physics.RaycastAll(origin, d, range, s_tSwingMask, QueryTriggerInteraction.Ignore);
			System.Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
			bool rayHit = false;
			foreach (RaycastHit h in hits)
			{
				if (h.collider == null || h.collider.transform.IsChildOf(base.transform))
				{
					continue;
				}
				if (h.collider.GetComponentInParent<Character>() != null)
				{
					continue;   // the game lets a swing pass friends (companions, you)
				}
				Vector3 pt = h.point;
				if (pt == Vector3.zero && h.distance == 0f)
				{
					pt = origin;   // started inside it
				}
				Heightmap hm = h.collider.GetComponent<Heightmap>();
				int k;
				if (hm != null)
				{
					k = 1;
				}
				else if (_target != null && h.collider.transform.IsChildOf(_target.transform))
				{
					k = 2;
				}
				else
				{
					k = 3;
				}
				if (kind == 0)
				{
					kind = k;
				}
				_tPredRays[k]++;
				rayHit = true;
				if (k == 1 && kind == 1 && (terrain == null || terrain == hm))
				{
					if (terrain == null)
					{
						terrain = hm;
						first = pt;
					}
					sum += pt;
					count++;
					if (h.distance < closestD)
					{
						closestD = h.distance;
						closest = pt;
					}
				}
				break;   // the swing stops at the first thing on each ray
			}
			if (!rayHit)
			{
				_tPredRays[0]++;
			}
		}
		if (kind == 1 && count > 0)
		{
			switch (a.m_hitPointtype)
			{
			case Attack.HitPointType.First:
				digPoint = first;
				break;
			case Attack.HitPointType.Closest:
				digPoint = closest;
				break;
			default:
				digPoint = sum / count;
				break;
			}
		}
		return true;
	}

	// walk to a spot (no edge check: it steps down into the trench it digs). 1 = there, 0 = on the way, -1 = couldn't
	// get there in 10 s
	private int TGetTo(Vector3 stand)
	{
		Vector3 flat = stand - base.transform.position;
		flat.y = 0f;
		if (flat.magnitude <= 0.5f)
		{
			_ai.StopMoving();
			_vWalkSince = -1f;
			return 1;
		}
		if (_vWalkSince < 0f || Vector3.Distance(_vWalkFor, stand) > 1.5f)
		{
			_vWalkSince = Time.time;
			_vWalkFor = stand;
		}
		if (Time.time - _vWalkSince > TWalkTime)
		{
			_vWalkSince = -1f;
			if (CompanionsPlugin.LogDetailed) Log($"Couldn't get to {MHR_Mover.Fmt(stand)} in {TWalkTime:F0}s - trying another spot");
			return -1;
		}
		bool ok = MHR_Mover.Go(_ai, Time.deltaTime, stand, 0.4f, false, "Vein");
		if ((!ok || _character.GetVelocity().magnitude < 0.3f) && flat.magnitude < 3f && !MHR_Mover.Climbing(_ai))
		{
			_ai.MoveTowards(flat.normalized, run: false);
		}
		return 0;
	}

	// dig the dirt at q from one of the stands: walks there, faces q and swings only when the game's swing would meet the
	// ground first, at a point within maxErr of q and not below minY. False = no stand works (every one tried; _tWhy
	// says why each failed).
	private bool TDig(Vector3 q, Vector3[] stands, float minY, float maxErr, long key)
	{
		while (_tCand < stands.Length)
		{
			Vector3 st = stands[_tCand];
			st.y = GroundAt(st);
			// W5: standing in shallow water is fine; a spot where it would have to swim (the game's own swim depth:
			// Character.m_swimDepth - 0.4) is skipped without walking there
			if (MHR_Water.DepthAt(st) > Mathf.Max(0f, (_character != null ? _character.m_swimDepth : 2f) - 0.4f))
			{
				_tWhy[5]++;
				if (CompanionsPlugin.LogDetailed) Log($"Stand spot {MHR_Mover.Fmt(st)} is too deep in the water to stand - trying another spot");
				TNextCand();
				continue;
			}
			int r = TGetTo(st);
			if (r == 0)
			{
				return true;
			}
			if (r < 0)
			{
				_tWhy[4]++;
				TNextCand();
				continue;
			}
			Vector3 face = q - base.transform.position;
			face.y = 0f;
			if (_tPredAt < Time.time || _tPredCand != _tCand || Flat(_tPredQ, q) > 0.05f)
			{
				_tPredAt = Time.time + 0.3f;
				_tPredCand = _tCand;
				_tPredQ = q;
				_tPredOk = false;
				int kind = 0;
				Vector3 dp = Vector3.zero;
				if (PredictSwing(face, out kind, out dp))
				{
					_tPredOk = kind == 1 && Flat(dp, q) <= maxErr && dp.y >= minY - 0.05f && dp.y >= TWater;
					if (!_tPredOk)
					{
						_tWhy[kind == 1 ? 1 : kind == 2 ? 2 : kind == 3 ? 3 : 0]++;
						if (CompanionsPlugin.LogDetailed) Log($"From {MHR_Mover.Fmt(base.transform.position)} a swing at {MHR_Mover.Fmt(q)} would {(kind == 1 ? $"dig at {MHR_Mover.Fmt(dp)} instead" : kind == 2 ? "hit the rock first" : kind == 3 ? "hit something else first" : "hit nothing")} (swing starts {_tPredOrigin:F2} m above its feet; rays: {_tPredRays[1]} dirt, {_tPredRays[2]} rock, {_tPredRays[3]} other, {_tPredRays[0]} nothing) - trying another spot");
					}
				}
				if (!_tPredOk)
				{
					TNextCand();
					continue;
				}
			}
			if (!_tPredOk)
			{
				TNextCand();
				continue;
			}
			// W2: turn to it first; the swing goes where it faces
			if (!TFacing(q))
			{
				if (_tFaceSince < 0f)
				{
					_tFaceSince = Time.time;
				}
				if (Time.time - _tFaceSince > 1f)
				{
					_tWhy[6]++;
					if (CompanionsPlugin.LogDetailed) Log($"Couldn't turn to face {MHR_Mover.Fmt(q)} in 1 s - trying another spot");
					TNextCand();
					continue;
				}
				VeinFace(q);
				return true;
			}
			_tFaceSince = -1f;
			_tQ = q;
			_tQBefore = GroundAt(q);
			_tDigMinY = Mathf.Max(minY, TWater);
			VeinSwing(new Vector3(q.x, Mathf.Max(_tQBefore, base.transform.position.y), q.z), dig: true, key);
			return true;
		}
		return false;
	}

	private void TNextCand()
	{
		_tCand++;
		_vWalkSince = -1f;
		_tFaceSince = -1f;
		_tPredAt = -1f;
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

	// W3: the reasons no stand spot worked, for the log
	private string TWhyText(int stands)
	{
		string[] names = { "hit nothing", "would dig somewhere else", "the rock first", "something else first", "couldn't get there", "too deep in the water", "couldn't face it in time" };
		System.Text.StringBuilder sb = new System.Text.StringBuilder();
		for (int i = 0; i < names.Length; i++)
		{
			if (_tWhy[i] > 0)
			{
				sb.Append(sb.Length > 0 ? ", " : "").Append(_tWhy[i]).Append(' ').Append(names[i]);
			}
		}
		return $"no swing from {stands} stand spot(s): " + (sb.Length > 0 ? sb.ToString() : "none tried");
	}

	// ---------- T2/T3: the trench ----------
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
		float g = GroundAt(c.P);
		if (g < TWater)
		{
			TPointDone(c, $"already at the water line (ground {g:F1}, the water level is 30) - counts as dug down");
			return;
		}
		Vector3 o = c.Edge + c.Out * 1.5f;
		o.y = g + 0.3f;
		if (!RayParts(o, -c.Out, 3f, AllParts(), out _, out _))
		{
			TPointDone(c, $"the rock stops here - its base at {g:F1} ({c.Surface - g:F1} m down)");
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
		// the stand spots are worked out once per point (digging moves the ground beside it, which mustn't reshuffle them)
		if (_tStandsFor != _tCur || _tStands == null)
		{
			_tStands = TrenchStands(c, g);
			_tStandsFor = _tCur;
		}
		if (!TDig(c.P, _tStands, TWater, 1.0f, -1L))
		{
			TPointDone(c, TWhyText(_tStands.Length));
		}
	}

	private Vector3[] TrenchStands(TPt c, float g)
	{
		Vector3 tan = Vector3.Cross(Vector3.up, c.Out);
		// W10: a trench point beside it that is already 0.5 m or more lower (dug, or an existing hole) is stood in first -
		// it jumps in and digs the side of the hole outward along the trench line, like a player
		List<Vector3> stands = new List<Vector3>(6);
		int n = _tPts.Count;
		if (n > 1)
		{
			TPt prev = _tPts[(_tCur - 1 + n) % n], next = _tPts[(_tCur + 1) % n];
			float gp = GroundAt(prev.P), gn = GroundAt(next.P);
			TPt lo = gp <= gn ? prev : next, hi = gp <= gn ? next : prev;
			float glo = Mathf.Min(gp, gn), ghi = Mathf.Max(gp, gn);
			if (glo <= g - 0.5f)
			{
				stands.Add(lo.P);
			}
			if (hi != lo && ghi <= g - 0.5f)
			{
				stands.Add(hi.P);
			}
		}
		stands.Add(c.P + c.Out * 1.1f);
		stands.Add(c.P + c.Out * 0.6f);
		stands.Add(c.P + tan * 1.1f);
		stands.Add(c.P - tan * 1.1f);
		return stands.ToArray();
	}

	private void TPointDone(TPt c, string why)
	{
		c.Done = true;
		_tDoneCount++;
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench point {_tCur + 1}/{_tPts.Count} at {MHR_Mover.Fmt(c.P)}: {why}");
		if (_tCur == _tStart && !_tStairDone && c.Surface - GroundAt(c.P) > TStairDepth)
		{
			_tStairDone = true;
			_tStairStep = 1;
			_tStairBase = GroundAt(c.P);
			_tPhase = TPhaseStair;
			TBeginItem();
			if (CompanionsPlugin.LogNormal) LogInfo($"The trench at {MHR_Mover.Fmt(c.P)} is {c.Surface - _tStairBase:F1} m deep - cutting 1 m steps out of it");
			return;
		}
		TNextPoint();
	}

	private void TNextPoint()
	{
		int n = _tPts.Count;
		for (int i = 1; i <= n; i++)
		{
			int j = (_tCur + i) % n;
			if (!_tPts[j].Done)
			{
				_tCur = j;
				TBeginItem();
				return;
			}
		}
		_tPhase = TPhaseUnder;
		_tUnderEmpty = 0;
		_tHasSpot = false;
		TBeginItem();
		if (CompanionsPlugin.LogNormal) LogInfo($"The trench round \"{_target.name}\" is done ({n} points) - clearing under it");
	}

	// the stair: step n is the ground 0.25 + n m out from the start point, cut down to the trench floor + n m, until a
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
		TPt c = _tPts[_tStart];
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
		Vector3 from = _tStairStep == 1 ? c.P : c.P + c.Out * (_tStairStep - 1 + 0.25f);
		Vector3[] stands = new Vector3[2]
		{
			from,
			from - c.Out * 0.4f
		};
		if (!TDig(sc, stands, target, 0.8f, -1L))
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't cut step {_tStairStep} out of the trench ({TWhyText(stands.Length)}) - carrying on");
			TStairEnd();
		}
	}

	private void TStairEnd()
	{
		_tPhase = TPhaseTrench;
		_tCur = _tStart;
		TNextPoint();
	}

	// ---------- T4: clearing under it ----------
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
				if (fd > TUnderReach || s.P.y <= floor + VDigMin || s.P.y < TWater)
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
				_tCur = (_tCur + 1) % _tPts.Count;
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
		Vector3[] stands = new Vector3[2]
		{
			c.P,
			c.P + c.Out * 0.5f
		};
		if (!TDig(_tSpot.P, stands, floor, 1.0f, _tSpot.Key))
		{
			if (CompanionsPlugin.LogDetailed) Log($"Can't get a swing at the dirt at {MHR_Mover.Fmt(_tSpot.P)} under \"{_tSpot.Part.name}\" from the trench ({TWhyText(stands.Length)}) - leaving it");
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

	// ---------- T5: mining it ----------
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
			// where it can swing at it from: here, else the 3 trench points nearest to it within reach
			_tMineStands.Clear();
			_tMineStands.Add(base.transform.position);
			List<KeyValuePair<float, Vector3>> near = new List<KeyValuePair<float, Vector3>>();
			foreach (TPt t in _tPts)
			{
				Vector3 cp = _vPart.ClosestPointOnBounds(t.P + Vector3.up * 1.5f);
				float d = Flat(t.P, cp);
				if (d <= Reach() + 1f)
				{
					near.Add(new KeyValuePair<float, Vector3>(d, t.P));
				}
			}
			near.Sort((x, y) => x.Key.CompareTo(y.Key));
			for (int i = 0; i < near.Count && i < 3; i++)
			{
				_tMineStands.Add(near[i].Value);
			}
		}
		while (_tCand < _tMineStands.Count)
		{
			Vector3 st = _tMineStands[_tCand];
			st.y = GroundAt(st);
			if (_tCand > 0 && MHR_Water.DepthAt(st) > Mathf.Max(0f, (_character != null ? _character.m_swimDepth : 2f) - 0.4f))
			{
				_tWhy[5]++;
				TNextCand();
				continue;
			}
			int r = _tCand == 0 ? 1 : TGetTo(st);
			if (r == 0)
			{
				return;
			}
			if (r < 0)
			{
				_tWhy[4]++;
				TNextCand();
				continue;
			}
			Vector3 aim = SafeClosestPoint(_vPart, Eye());
			Vector3 face = aim - base.transform.position;
			if (_tPredAt < Time.time || _tPredCand != _tCand)
			{
				_tPredAt = Time.time + 0.3f;
				_tPredCand = _tCand;
				int kind = 0;
				_tPredOk = PredictSwing(face, out kind, out _) && kind == 2;
				if (!_tPredOk)
				{
					_tWhy[kind == 1 ? 1 : kind == 3 ? 3 : 0]++;
					if (CompanionsPlugin.LogDetailed) Log($"From {MHR_Mover.Fmt(base.transform.position)} the swing wouldn't meet the rock of \"{_vPart.name}\" first ({(kind == 1 ? "dirt" : kind == 3 ? "something else" : "nothing")} first; swing starts {_tPredOrigin:F2} m above its feet; rays: {_tPredRays[1]} dirt, {_tPredRays[2]} rock, {_tPredRays[3]} other, {_tPredRays[0]} nothing) - trying another spot");
				}
			}
			if (!_tPredOk)
			{
				TNextCand();
				continue;
			}
			// W2: turn to it first
			if (!TFacing(aim))
			{
				if (_tFaceSince < 0f)
				{
					_tFaceSince = Time.time;
				}
				if (Time.time - _tFaceSince > 1f)
				{
					_tWhy[6]++;
					TNextCand();
					continue;
				}
				VeinFace(aim);
				return;
			}
			_tFaceSince = -1f;
			VeinSwing(aim, dig: false, 0L);
			return;
		}
		VeinPartFail(GiveUpWhy.OutOfReach, TWhyText(_tMineStands.Count));
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
			if (lowered >= VDigMin && lowered <= TCave)
			{
				digProg = true;
				_tItemTries = 0;
				what += (what.Length > 0 ? ", " : "") + "dug " + (lowered * 100f).ToString("F0") + " cm";
				if (_tPhase == TPhaseUnder)
				{
					_tUnderEmpty = 0;
				}
			}
			else if (lowered > TCave)
			{
				if (CompanionsPlugin.LogDetailed) Log($"The ground at {MHR_Mover.Fmt(_tQ)} dropped {lowered:F1} m in one swing - the wall caved in, not counted");
			}
			else
			{
				_tItemTries++;
			}
		}
		bool prog = _tPhase == TPhaseMine ? rock : (rock || digProg);
		if (prog)
		{
			_tItemNoProg = 0;
			_tItemProgAt = Time.time;
			if (CompanionsPlugin.LogDetailed) Log("Vein progress: " + what);
		}
		else
		{
			_tItemNoProg++;
		}
		bool tries = _vSwingDig && _tItemTries >= VSpotTries;
		bool noProg = _tItemNoProg >= VNoProgSwings || Time.time - _tItemProgAt >= VNoProgTime;
		if (!tries && !noProg)
		{
			return;
		}
		string detail = tries ? $"won't go lower ({VSpotTries} tries)" : $"{_tItemNoProg} swings / {Time.time - _tItemProgAt:F0}s without progress";
		switch (_tPhase)
		{
		case TPhaseTrench:
			if (_tCur >= 0 && _tCur < _tPts.Count && !_tPts[_tCur].Done)
			{
				TPointDone(_tPts[_tCur], detail);
			}
			break;
		case TPhaseStair:
			if (CompanionsPlugin.LogNormal) LogInfo($"Step {_tStairStep} out of the trench {detail} - carrying on");
			TStairEnd();
			break;
		case TPhaseUnder:
			if (_tHasSpot)
			{
				_vStuck.Add(_tSpot.Key);
				if (CompanionsPlugin.LogDetailed) Log($"The dirt at {MHR_Mover.Fmt(_tSpot.P)} {detail} - leaving that spot");
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
