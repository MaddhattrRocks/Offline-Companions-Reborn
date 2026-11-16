using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97 (build list v4, items A and B, approved by John 2026-11-15 16:22, reviews/1.5.97-build-list-v4.md):
//  A  The trench starts from what it sees and grows. A piece is seen when part of it shows above the ground, or one of
//     its swings or its swing check meets it; once seen, always seen. The plan is a rubber band (the convex outline)
//     round all seen pieces, the trench the ring of squares 1 m outside it. When a new piece is seen the plan is made
//     again (at most once every 2 s); spots already at depth read as done from the ground.
//  B  Several companions on one vein share ONE plan, kept here for everyone the way MHR_World keeps the tombstone list:
//     the seen pieces, the ring and its spots, and who is on which spot. It is kept for 1 hour after the last companion
//     worked the vein (by the vein's own id, so it survives the area unloading), and dropped when the vein is gone.
//     Spreading out is a preference: a spot nobody is on first, the one farthest from the others on arriving and on
//     meeting; with nothing free it helps on the nearest spot that isn't done.
public partial class HarvestController
{
	private struct VClosed
	{
		public Vector3 P;

		// 0 = closed for everyone (a building, deep water, the ground won't go lower); else the companion that gave up on it
		public int Who;
	}

	// what one companion has tried at a spot (its own limits, not shared)
	private sealed class TMy
	{
		public int Blocks;

		public bool PersonWaited;

		public bool DoorTried;

		public bool ExitTried;

		public bool Jumped;
	}

	private sealed class VPlan
	{
		public string Key;

		public string Name;

		// the seen pieces, by their place in the game's own list of the vein's parts (the same after the area reloads)
		public readonly HashSet<int> Seen = new HashSet<int>();

		public List<TLine> Lines = new List<TLine>();

		public int Version;

		// how many pieces were seen when the lines were made; a seen piece outside the ring asks for a new plan in any phase
		public int PlannedSeen;

		public bool NeedOutside;

		// pop test A1: every spot of the trench was done once - no new ring for this vein, arrivals go straight to clearing
		public bool TrenchDone;

		public float NextReplan;

		public float NextSeeCheck;

		public HarvestController Planner;

		public float PlannerAt;

		public Vector3 GMin;

		public float GCell;

		public int GNx;

		public int GNz;

		public bool[] GRock;

		public Vector3 Center;

		public bool NoneVisible;

		public readonly Dictionary<HarvestController, float> Workers = new Dictionary<HarvestController, float>();

		public readonly Dictionary<Collider, HarvestController> PartHolds = new Dictionary<Collider, HarvestController>();

		public readonly List<VClosed> Closed = new List<VClosed>();

		// part 1 T6: spots a companion with no axe left because of a tree (kept across new plans, by place)
		public readonly List<Vector3> AxeSpots = new List<Vector3>();

		public float LastWork;
	}

	// kept 1 hour after the last companion worked the vein (John 2026-11-15 16:05)
	private const float VPlanKeep = 3600f;

	// a companion counts as working the vein (and its hold on a spot or piece stands) for this long after its last update
	private const float VHoldTime = 10f;

	private const float VReplanGap = 2f;

	private static readonly Dictionary<string, VPlan> s_vPlans = new Dictionary<string, VPlan>();

	private static readonly List<TLine> s_noLines = new List<TLine>();

	private VPlan _vp;

	private int _tPlanVer;

	private int _tPlanSeen;

	// the next start: on arriving and on meeting another companion, the free spot farthest from the others
	private bool _tPickFar;

	private string _tPickWhy = "";

	private float _tStartLimit = TStartTime;

	private bool[] _gFoot;

	private readonly Dictionary<TSpot, TMy> _tMine = new Dictionary<TSpot, TMy>();

	private List<TLine> _tLines => _vp != null ? _vp.Lines : s_noLines;

	private int MyId => GetInstanceID();

	private string MyName => _character != null ? _character.m_name : base.name;

	private TMy My(TSpot q)
	{
		if (!_tMine.TryGetValue(q, out TMy m))
		{
			m = new TMy();
			_tMine[q] = m;
		}
		return m;
	}

	private static string VPlanKey(MineRock5 rock)
	{
		ZNetView nv = rock != null ? rock.GetComponent<ZNetView>() : null;
		ZDO z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
		return z != null ? z.m_uid.ToString() : "i" + (rock != null ? rock.GetInstanceID() : 0);
	}

	private static VPlan VPlanFor(MineRock5 rock, string name)
	{
		List<string> old = null;
		foreach (KeyValuePair<string, VPlan> kv in s_vPlans)
		{
			if (Time.time - kv.Value.LastWork > VPlanKeep)
			{
				(old ?? (old = new List<string>())).Add(kv.Key);
			}
		}
		if (old != null)
		{
			foreach (string k in old)
			{
				if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug($"[Mining] The plan for \"{s_vPlans[k].Name}\" is forgotten (nobody worked it for an hour)");
				s_vPlans.Remove(k);
			}
		}
		string key = VPlanKey(rock);
		if (!s_vPlans.TryGetValue(key, out VPlan p))
		{
			p = new VPlan { Key = key, Name = name, LastWork = Time.time };
			s_vPlans[key] = p;
		}
		return p;
	}

	// the vein is gone: its plan with it
	private void VPlanDrop()
	{
		if (_vp != null)
		{
			s_vPlans.Remove(_vp.Key);
			_vp = null;
		}
	}

	private void VPlanLeave()
	{
		if (_vp != null)
		{
			_vp.Workers.Remove(this);
			if (_vp.Planner == this)
			{
				_vp.Planner = null;
			}
		}
	}

	private bool VActive(HarvestController h)
	{
		return h != null && h != this && _vp != null && h._vp == _vp && _vp.Workers.TryGetValue(h, out float at) && Time.time - at <= VHoldTime;
	}

	// the other companions working this vein now
	private List<HarvestController> TOthers()
	{
		List<HarvestController> list = new List<HarvestController>();
		if (_vp == null)
		{
			return list;
		}
		foreach (KeyValuePair<HarvestController, float> kv in _vp.Workers)
		{
			if (VActive(kv.Key))
			{
				list.Add(kv.Key);
			}
		}
		return list;
	}

	private bool VPlannerBusy()
	{
		if (_vp == null || _vp.Planner == null || _vp.Planner == this)
		{
			return false;
		}
		if (Time.time - _vp.PlannerAt > 5f || _vp.Planner._vp != _vp)
		{
			_vp.Planner = null;   // it left or was called away mid-plan
			return false;
		}
		return true;
	}

	// ---------- seen pieces ----------
	private void TSeenToVisible()
	{
		_tVisible.Clear();
		if (_vp == null)
		{
			return;
		}
		foreach (VPart p in _vParts)
		{
			if (_vp.Seen.Contains(p.Idx))
			{
				_tVisible.Add(p.C);
			}
		}
	}

	private void TSee(VPart p, string how)
	{
		if (_vp == null || p.C == null || !_vp.Seen.Add(p.Idx))
		{
			return;
		}
		_tVisible.Add(p.C);
		// outside the ring? its middle, or a point halfway to a corner of its box, outside the band
		bool outside = false;
		if (_gRock != null && _vp.Version > 0)
		{
			outside = !GRockAt(p.Center);
			for (int k = 0; k < 4 && !outside; k++)
			{
				Vector3 q = p.Center + new Vector3((k & 1) == 0 ? p.Half.x : -p.Half.x, 0f, k < 2 ? p.Half.z : -p.Half.z) * 0.5f;
				outside = !GRockAt(q);
			}
			if (outside && !_vp.TrenchDone)
			{
				_vp.NeedOutside = true;
			}
		}
		if (CompanionsPlugin.LogDetailed) Log($"Seen a new piece of \"{_vp.Name}\": \"{p.C.name}\" ({how}) at {MHR_Mover.Fmt(p.Center)}{(outside ? ", outside the ring" : "")} - {_vp.Seen.Count} of {_vParts.Count} seen");
	}

	private void TSee(Collider c, string how)
	{
		if (c == null || _vp == null)
		{
			return;
		}
		foreach (VPart p in _vParts)
		{
			if (p.C == c)
			{
				TSee(p, how);
				return;
			}
		}
	}

	// the pieces near where it just dug that now show above the ground (at most once every 2 s for the vein)
	private void TSeeCheck(Vector3 near, bool all = false)
	{
		if (_vp == null)
		{
			return;
		}
		if (!all)
		{
			if (Time.time < _vp.NextSeeCheck)
			{
				return;
			}
			_vp.NextSeeCheck = Time.time + VReplanGap;
		}
		List<VPart> found = null;
		foreach (VPart p in _vParts)
		{
			if (_vp.Seen.Contains(p.Idx))
			{
				continue;
			}
			if (!all)
			{
				float dx = Mathf.Max(0f, Mathf.Abs(near.x - p.Center.x) - p.Half.x), dz = Mathf.Max(0f, Mathf.Abs(near.z - p.Center.z) - p.Half.z);
				if (dx * dx + dz * dz > 25f)
				{
					continue;
				}
			}
			if (PartVisible(p))
			{
				(found ?? (found = new List<VPart>())).Add(p);
			}
		}
		if (found != null)
		{
			foreach (VPart p in found)
			{
				TSee(p, "it shows above the ground");
			}
		}
	}

	// ---------- arriving at a vein ----------
	private void TPlanEnter()
	{
		_vp = VPlanFor(_vRock, _target != null ? _target.name : "?");
		_vp.Workers[this] = Time.time;
		_vp.LastWork = Time.time;
		_tMine.Clear();
		// test review A2: back with a working pickaxe - the nearest spot not done, not the far side
		_tPickFar = !_tEnterNear;
		_tPickWhy = _tEnterNear ? "back with a working pickaxe" : "arriving";
		_tEnterNear = false;
		List<HarvestController> others = TOthers();
		int before = _vp.Seen.Count;
		TSeenToVisible();
		TSeeCheck(base.transform.position, all: true);
		// what this companion gave up on before is tried again, as on every new go at a vein
		_vp.Closed.RemoveAll(c => c.Who == MyId);
		foreach (TLine l in _vp.Lines)
		{
			foreach (TSpot s in l.S)
			{
				s.Gave?.Remove(MyId);
			}
		}
		// pop test A3: the trench round this vein was finished - the plan there is, and straight to clearing
		if (_vp.TrenchDone && _vp.Version > 0 && !VPlannerBusy())
		{
			TPlanAdopt("the trench is already done");
			return;
		}
		if (others.Count == 0 || _vp.Version == 0)
		{
			if (others.Count == 0)
			{
				_vp.Closed.Clear();
			}
			if (CompanionsPlugin.LogNormal && before > 0 && others.Count == 0 && _vp.Version > 0) LogInfo($"\"{_vp.Name}\" has a plan from before ({before} piece(s) seen then, {_vp.Seen.Count} now) - making the ring again from them");
			if (!VPlannerBusy())
			{
				TPlanBegin();
				return;
			}
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Joining {others.Count} other companion(s) on \"{_vp.Name}\" - using the same plan ({_vp.Seen.Count} piece(s) seen)");
		_tPhase = TPhasePlan;   // TPlanTick waits for the planner, or takes the plan there is
		_gGround = null;
	}

	// the plan there is: its grid and lines, a start picked again
	private void TPlanAdopt(string why)
	{
		// test review B2: it was on a spot of the plan before - it keeps its place and its way round
		bool carry = _tPlanVer > 0 && _tQ != null && _tLine >= 0 && (_tPhase == TPhaseTrench || (_tPhase == TPhasePlan && _tWasTrench));
		_tWasTrench = false;
		Vector3 oldP = carry ? _tQ.P : Vector3.zero, oldRun = _tRunP;
		int oldDir = _tDir;
		bool oldTurned = _tTurned;
		_tPlanVer = _vp.Version;
		_gMin = _vp.GMin;
		_gCell = _vp.GCell;
		_gNx = _vp.GNx;
		_gNz = _vp.GNz;
		_gRock = _vp.GRock;
		_tCenter = _vp.Center;
		_tNoneVisible = _vp.NoneVisible;
		_gGround = null;
		_gFoot = null;
		_tMine.Clear();
		TSeenToVisible();
		_tChopping = false;
		_tQ = null;
		_tLine = -1;
		_pPart = null;
		if (_tLines.Count == 0)
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"Vein \"{_target.name}\": couldn't find the edge of the rock - mining it from where it can reach");
			TLapStart();
			return;
		}
		if (CompanionsPlugin.LogDetailed) Log($"Working to plan {_vp.Version} of \"{_vp.Name}\" ({why})");
		if (_vp.TrenchDone)
		{
			// pop test A2/A3: never back to the trench once it was finished
			TTrenchDone(null);
			return;
		}
		_tPhase = TPhaseTrench;
		_tSub = TSubPick;
		TBeginItem();
		if (carry)
		{
			// the new plan's spot nearest the one it was on, within 1 m (done or not: S3 moves on from a done one the
			// same way round); else the nearest spot not done to where it stands; none: S2 picks (and finds the end)
			Vector3 me = base.transform.position;
			TSpot same = null, open = null, run = null;
			float sd = 1.0f, od = float.MaxValue;
			foreach (TLine l in _tLines)
			{
				foreach (TSpot s in l.S)
				{
					float d = Flat(s.P, oldP);
					if (d <= sd)
					{
						sd = d;
						same = s;
					}
					d = Flat(s.P, me);
					if (TOpen(s) && d < od)
					{
						od = d;
						open = s;
					}
				}
			}
			TSpot at = same ?? open;
			if (at != null)
			{
				float rd = float.MaxValue;
				foreach (TSpot s in _tLines[at.Line].S)
				{
					float d = Flat(s.P, oldRun);
					if (d < rd)
					{
						rd = d;
						run = s;
					}
				}
				_tLine = at.Line;
				_tRunStart = run != null ? run.Idx : at.Idx;
				_tRunP = run != null ? run.P : at.P;
				_tDir = oldDir;
				_tTurned = oldTurned;
				if (TOpen(at))
				{
					TTake(at);
				}
				else
				{
					_tQ = at;
				}
				_tPickFar = false;
				_tPickWhy = "";
				_tStartExited = false;
				_tSub = TSubDig;
				TBeginItem();
				if (CompanionsPlugin.LogNormal) LogInfo($"Plan {_vp.Version}: carrying on at spot {at.Idx + 1}/{_tLines[at.Line].S.Count} of line {at.Line + 1} at {MHR_Mover.Fmt(at.P)} ({(same != null ? "the spot it was on" : "the nearest spot not done, its own spot isn't on the new ring")}), the same way round");
			}
		}
	}

	// each update: someone made the plan again, or a new piece wants it made again
	private bool TPlanSync()
	{
		if (_vp == null || _tPhase == TPhasePlan)
		{
			return false;
		}
		if (_vp.TrenchDone && _vp.Version == _tPlanVer)
		{
			return false;   // pop test A2: the plan isn't made again once the trench was finished
		}
		if (_vp.Version != _tPlanVer)
		{
			if (_tPickWhy.Length == 0)
			{
				_tPickWhy = "the plan grew";
			}
			TPlanAdopt("the plan was made again");
			return true;
		}
		bool grew = _vp.Seen.Count > _vp.PlannedSeen;
		if ((_vp.NeedOutside || (grew && _tPhase == TPhaseTrench)) && Time.time >= _vp.NextReplan && !VPlannerBusy())
		{
			if (CompanionsPlugin.LogNormal) LogInfo($"{_vp.Seen.Count - _vp.PlannedSeen} new piece(s) of \"{_vp.Name}\" seen{(_vp.NeedOutside ? " (outside the ring)" : "")} - making the plan again round all {_vp.Seen.Count}");
			_ai.StopMoving();
			TPlanBegin();
			return true;
		}
		return false;
	}

	// ---------- spots: open for me, held by another ----------
	private bool TOpen(TSpot s)
	{
		return s != null && !s.Done && (s.Gave == null || !s.Gave.Contains(MyId));
	}

	private HarvestController THeldBy(TSpot s)
	{
		HarvestController h = s != null ? s.Holder : null;
		return h != null && h != this && VActive(h) && h._tPhase == TPhaseTrench && h._tQ == s ? h : null;
	}

	private void TTake(TSpot s)
	{
		_tQ = s;
		if (s != null && THeldBy(s) == null)
		{
			s.Holder = this;
		}
	}

	// this companion gives up on a spot: it's left for the others (the hold ends), and counts as done for this one
	private void TSpotGave(TSpot q, string why)
	{
		if (q == null || !TOpen(q))
		{
			return;
		}
		(q.Gave ?? (q.Gave = new HashSet<int>())).Add(MyId);
		if (q.Holder == this)
		{
			q.Holder = null;
		}
		_vp?.Closed.Add(new VClosed { P = q.P, Who = MyId });
		if (CompanionsPlugin.LogNormal) LogInfo($"Trench spot {q.Idx + 1}/{_tLines[q.Line].S.Count} (line {q.Line + 1}) at {MHR_Mover.Fmt(q.P)}: giving up on it ({why})");
	}

	private HarvestController TPartHeldBy(Collider c)
	{
		if (_vp == null || c == null || !_vp.PartHolds.TryGetValue(c, out HarvestController h))
		{
			return null;
		}
		return h != null && h != this && VActive(h) && h._tPhase == TPhasePop && h._pPart == c ? h : null;
	}

	// ---------- the rubber band: the convex outline of the squares where a seen piece stands ----------
	private static float Cross2(Vector2 o, Vector2 a, Vector2 b)
	{
		return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
	}

	// the band round the footprint squares, filled in: every square inside it counts as rock for the plan
	private int TBandFill()
	{
		int total = _gNx * _gNz;
		List<Vector2> pts = new List<Vector2>();
		for (int i = 0; i < total; i++)
		{
			_gRock[i] = _gFoot[i];
			if (_gFoot[i])
			{
				pts.Add(new Vector2(i % _gNx, i / _gNx));
			}
		}
		if (pts.Count < 3)
		{
			return pts.Count;
		}
		pts.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
		List<Vector2> hull = new List<Vector2>();
		for (int pass = 0; pass < 2; pass++)
		{
			int start = hull.Count;
			for (int k = 0; k < pts.Count; k++)
			{
				Vector2 p = pass == 0 ? pts[k] : pts[pts.Count - 1 - k];
				while (hull.Count >= start + 2 && Cross2(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
				{
					hull.RemoveAt(hull.Count - 1);
				}
				hull.Add(p);
			}
			hull.RemoveAt(hull.Count - 1);
		}
		if (hull.Count < 3)
		{
			return hull.Count;
		}
		for (int i = 0; i < total; i++)
		{
			if (_gRock[i])
			{
				continue;
			}
			Vector2 c = new Vector2(i % _gNx, i / _gNx);
			bool inside = true;
			for (int k = 0; k < hull.Count && inside; k++)
			{
				inside = Cross2(hull[k], hull[(k + 1) % hull.Count], c) >= 0f;
			}
			_gRock[i] = inside;
		}
		return hull.Count;
	}
}
