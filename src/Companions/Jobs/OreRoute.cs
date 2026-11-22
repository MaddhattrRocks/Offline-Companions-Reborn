using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Companions;

// 1.5.98 MP1 (John, 2026-11-21: "use the map data to make the NPC work smarter and move smarter since they now know whats
// around them"; the map-path list approved 19:05Z): a walk inside a vein's pit is planned over the pit map's 0.5 m squares,
// which see stone, vein parts, trench steps, ramps and the other companions - the game's walking route sees none of them.
// Only where the route comes from changes: the mover walks it with its own steering, hops, drops and stuck steps (MP2).
public partial class HarvestController
{
	private const float OvRtStepUp = 0.7f;          // MP1: a step walked up
	private const float OvRtHopUp = 1.1f;           // MP1: a step hopped up (the mover's HopLine limit for a step)
	private const float OvRtBigDrop = 1.2f;         // MP1: a drop this big costs more than walking
	private const float OvRtCrowd = 1.2f;           // MP1: kept this far from where others stand or walk to
	private const int OvRtMaxNodes = 6000;          // MP1: the most squares one search looks at
	private const float OvRtVeinCost = 10f;         // G2 (new, John 11-22 OK): extra cost of a square over a vein part (0.5 m squares: 20 per metre)
	private const float OvRtOffRoute = 1.5f;        // MP1: this far off its route = a new search
	private const float OvRtGoalMove = 1f;          // MP1: the goal moved this far = a new search
	private const int OvRtProbeMax = 5;            // MP3: the most places asked about per stand choice

	private const float OvRtSteep = 0.39f;          // MP1: the mover's SteepStep: a 0.5 m step down steeper than this adds to a fall

	// MP1: this companion's last route (kept while it holds)
	private readonly List<Vector3> _rtPath = new List<Vector3>();

	private readonly List<int> _rtSquares = new List<int>();

	private Vector3 _rtGoal;

	private float _rtAt = -10f;

	private OvPlan _rtPlan;

	// MP4: the last search that found no route only because of a step too high: its foot and top
	private Vector3 _rtBlockFoot, _rtBlockTop;

	private float _rtBlockAt = -10f;

	// MP1: the search's work arrays, shared (one search at a time, on the main thread)
	private static float[] s_rtG;

	private static float[] s_rtRun;

	private static int[] s_rtFrom;

	private static int[] s_rtSeen;

	private static int s_rtStamp;

	private static readonly List<int> s_rtHeap = new List<int>();

	private static readonly List<float> s_rtHeapF = new List<float>();

	private static readonly List<Vector3> s_rtOthers = new List<Vector3>();

	private static readonly List<int> s_rtTmp = new List<int>();

	private static readonly HashSet<int> s_rtOnRoute = new HashSet<int>();

	// MP1: the cost, in the pit map's once-a-minute line
	private static long s_rtTicks;

	private static int s_rtSearches, s_rtFound, s_rtNone, s_rtUnknown, s_rtKept;

	// MP1: where its feet go on square i (the map's stand rule, OvMapStand, from what is already read: the ground with
	// its height of open space above it, or the top of a player-built piece with that room above it); NaN = no place to
	// stand or not read yet
	private static float OvRtFeet(OvMap m, int i, float hh, float radius = 0f)
	{
		if (m.ReadAt[i] < 0f)
		{
			return float.NaN;
		}
		float g = m.Ground[i];
		int li = i * OvMapLayers, lc = m.LCount[i];
		if (lc == 0 || m.LBot[li] - g >= hh)
		{
			return OvRoomOverBody(m, i, g, hh, radius) ? g : float.NaN;
		}
		for (int j = 0; j < lc; j++)
		{
			if (m.LKind[li + j] != MkBuilt)
			{
				continue;
			}
			float top = m.LTop[li + j];
			float above = j + 1 < lc ? m.LBot[li + j + 1] - top : float.MaxValue;
			return above >= hh && OvRoomOverBody(m, i, top, hh, radius) ? top : float.NaN;
		}
		return float.NaN;
	}

	// SV8 (John 11-21: they hit their heads and get stuck under the vein; head room = full height x body width): standing
	// on square i with its feet at `feet`, nothing solid hangs into the space its body takes - its height, over every
	// square its body (the game collider's radius round the square's middle) covers. Only what hangs overhead counts: a
	// layer whose bottom is more than a step (0.7 m) over its feet and less than its height over them. Ground and things
	// standing on the ground beside it are walls and steps, left to the walk and the way-up rules as now.
	private static bool OvRoomOverBody(OvMap m, int i, float feet, float hh, float radius)
	{
		if (radius <= 0f)
		{
			return true;
		}
		int cx = i % m.W, cz = i / m.W;
		int r = Mathf.CeilToInt(radius / OvMapCell);
		for (int dz = -r; dz <= r; dz++)
		{
			for (int dx = -r; dx <= r; dx++)
			{
				int x = cx + dx, z = cz + dz;
				if (x < 0 || z < 0 || x >= m.W || z >= m.H)
				{
					continue;
				}
				// the nearest point of square (x, z) to the middle of square i
				float nx = Mathf.Max(0f, Mathf.Abs(dx) * OvMapCell - OvMapCell * 0.5f), nz = Mathf.Max(0f, Mathf.Abs(dz) * OvMapCell - OvMapCell * 0.5f);
				if (nx * nx + nz * nz >= radius * radius)
				{
					continue;
				}
				int j = z * m.W + x;
				if (m.ReadAt[j] < 0f)
				{
					continue;
				}
				int lj = j * OvMapLayers, lc = m.LCount[j];
				for (int k = 0; k < lc; k++)
				{
					float bot = m.LBot[lj + k];
					if (bot > feet + OvStepUp && bot < feet + hh)
					{
						return false;
					}
				}
			}
		}
		return true;
	}

	// SV8: the companion's body radius, from its game collider
	private float OvBodyRadius()
	{
		CapsuleCollider cc = _character != null ? _character.GetComponent<CapsuleCollider>() : null;
		return cc != null ? cc.radius * Mathf.Max(Mathf.Abs(cc.transform.lossyScale.x), Mathf.Abs(cc.transform.lossyScale.z)) : 0.5f;
	}

	private static bool OvRtOnBuilt(OvMap m, int i, float feet)
	{
		return Mathf.Abs(feet - m.Ground[i]) > 0.01f;
	}

	private static Vector3 OvRtPoint(OvMap m, int i, float y)
	{
		return new Vector3(m.X0 + (i % m.W + 0.5f) * OvMapCell, y, m.Z0 + (i / m.W + 0.5f) * OvMapCell);
	}

	// MP1: a square it may stand on (feet not NaN, not water it would swim in)
	private float OvRtStandOn(OvMap m, int i, float hh, float water, float swim)
	{
		// K3: under a thing a companion got stuck on = not walked
		if (m.StuckBox.Count > 0)
		{
			float sx = m.X0 + (i % m.W + 0.5f) * OvMapCell, sz = m.Z0 + (i / m.W + 0.5f) * OvMapCell;
			foreach (Bounds sb in m.StuckBox)
			{
				if (sx >= sb.min.x && sx <= sb.max.x && sz >= sb.min.z && sz <= sb.max.z)
				{
					return float.NaN;
				}
			}
		}
		float f = OvRtFeet(m, i, hh, OvBodyRadius());
		if (float.IsNaN(f))
		{
			return f;
		}
		if ((m.Flags[i] & MfWater) != 0 && water - f > swim)
		{
			return float.NaN;
		}
		return f;
	}

	// MP1: the square of pt or the nearest one within 1 m it can stand on; -1 = none
	private int OvRtSnap(OvMap m, Vector3 pt, float hh, float water, float swim, out bool unknown)
	{
		unknown = false;
		int c = OvMapIndex(m, pt);
		if (c < 0)
		{
			return -1;
		}
		int cx = c % m.W, cz = c / m.W, best = -1;
		float bd = float.MaxValue;
		for (int dz = -2; dz <= 2; dz++)
		{
			for (int dx = -2; dx <= 2; dx++)
			{
				int x = cx + dx, z = cz + dz;
				if (x < 0 || z < 0 || x >= m.W || z >= m.H)
				{
					continue;
				}
				int i = z * m.W + x;
				if (m.ReadAt[i] < 0f)
				{
					unknown = true;
					continue;
				}
				float f = OvRtStandOn(m, i, hh, water, swim);
				if (float.IsNaN(f) || Mathf.Abs(f - pt.y) > 2.5f)
				{
					continue;
				}
				float d = dx * dx + dz * dz + Mathf.Abs(f - pt.y);
				if (d < bd)
				{
					bd = d;
					best = i;
				}
			}
		}
		return best;
	}

	// MP1: the route to goal on this vein's map. 1 = found (path = its corners, the last one the goal), 0 = the map says
	// there is no way there (and, when only a step too high is in the way, MP4's step is kept), -1 = the map can't tell
	// (no map, an end off it or with no place to stand, squares not read yet on the way, or the search hit its limit).
	internal int OvMapRoute(Vector3 from, Vector3 goal, List<Vector3> path, out string info)
	{
		info = null;
		path.Clear();
		OvPlan p = _vp;
		OvMap m = p?.Map;
		if (m == null || !m.Built || OvMapIndex(m, from) < 0 || OvMapIndex(m, goal) < 0)
		{
			return -1;
		}
		float now = Time.time;
		// kept: same plan, the goal within 1 m, it is within 1.5 m of the route, no square on it changed since
		if (_rtPlan == p && _rtPath.Count > 0 && Vector3.Distance(_rtGoal, goal) <= OvRtGoalMove && OvRtNearRoute(from, out int next) && !OvRtChanged(m))
		{
			// the corners still ahead of it (from the end of the stretch it is nearest)
			s_rtKept++;
			for (int k = next; k < _rtPath.Count; k++)
			{
				path.Add(_rtPath[k]);
			}
			return 1;
		}
		_rtPath.Clear();
		_rtSquares.Clear();
		_rtPlan = null;
		long t0 = Stopwatch.GetTimestamp();
		int r = OvRtSearch(p, m, from, goal, _rtPath, _rtSquares, out info);
		s_rtTicks += Stopwatch.GetTimestamp() - t0;
		s_rtSearches++;
		if (r == 1)
		{
			s_rtFound++;
			_rtPlan = p;
			_rtGoal = goal;
			_rtFrom = from;
			_rtAt = now;
			path.AddRange(_rtPath);
		}
		else if (r == 0)
		{
			s_rtNone++;
		}
		else
		{
			s_rtUnknown++;
		}
		return r;
	}

	// MP1: within 1.5 m of its route (from where the route started, through each corner); next = the first corner after
	// the stretch it is nearest
	private bool OvRtNearRoute(Vector3 me, out int next)
	{
		next = 0;
		float bd = float.MaxValue;
		Vector3 prev = _rtFrom;
		for (int i = 0; i < _rtPath.Count; i++)
		{
			Vector3 a = prev, b = _rtPath[i];
			prev = b;
			Vector2 ab = new Vector2(b.x - a.x, b.z - a.z), am = new Vector2(me.x - a.x, me.z - a.z);
			float t = ab.sqrMagnitude < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(am, ab) / ab.sqrMagnitude);
			float d = (am - ab * t).magnitude;
			if (d < bd)
			{
				bd = d;
				next = i;
			}
		}
		return bd <= OvRtOffRoute;
	}

	private Vector3 _rtFrom;

	private bool OvRtChanged(OvMap m)
	{
		foreach (int i in _rtSquares)
		{
			if (i >= m.ChangedAt.Length || m.ChangedAt[i] > _rtAt)
			{
				return true;
			}
		}
		return false;
	}

	private const float OvStuckMaxSize = 2f;   // K3: only a thing up to 2 m across (flat) is marked

	private const int OvStuckMax = 16;         // K3: at most 16 things kept per map

	// K3 (John 11-22 19:35Z, Q-K3 ok: routes only): the mover got stuck on a map route with `col` the first thing ahead:
	// unless it is the ground, a creature or a rock / vein (MineRock, MineRock5 - the map sees those), or more than 2 m
	// across, the squares under it (its box, + the body radius) are kept out of every map route on this map
	internal void OvMapStuckOn(Collider col)
	{
		OvMap m = _vp?.Map;
		if (m == null || !m.Built || col == null || m.StuckOn.Contains(col) || m.StuckOn.Count >= OvStuckMax)
		{
			return;
		}
		if (col.gameObject.layer == LayerMask.NameToLayer("terrain") || col.GetComponentInParent<Character>() != null || col.GetComponentInParent<MineRock5>() != null || col.GetComponentInParent<MineRock>() != null)
		{
			return;
		}
		Bounds b = col.bounds;
		if (b.size.x > OvStuckMaxSize || b.size.z > OvStuckMaxSize || OvMapIndex(m, b.center) < 0)
		{
			return;
		}
		float r = OvBodyRadius();
		b.Expand(new Vector3(r * 2f, 0f, r * 2f));
		m.StuckOn.Add(col);
		m.StuckBox.Add(b);
		if (CompanionsPlugin.LogNormal) LogInfo($"Got stuck on \"{col.transform.root.name}\" at {MHR_Mover.Fmt(col.bounds.center)} on the map route - map routes on \"{_vp.Name}\" go round it now (K3)");
	}

	// K3: a marked thing that is gone (destroyed, cut down, turned off) is dropped
	private static void OvStuckPrune(OvMap m)
	{
		for (int j = m.StuckOn.Count - 1; j >= 0; j--)
		{
			Collider col = m.StuckOn[j];
			if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
			{
				m.StuckOn.RemoveAt(j);
				m.StuckBox.RemoveAt(j);
			}
		}
	}

	private int OvRtSearch(OvPlan p, OvMap m, Vector3 from, Vector3 goal, List<Vector3> outPath, List<int> outSquares, out string info)
	{
		info = null;
		bool hadBlock = false;
		OvStuckPrune(m);   // K3
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : -10000f;
		float swim = MHR_Water.SwimLimit(_character);
		float maxDrop = ModConfig.MaxDrop != null ? ModConfig.MaxDrop.Value : 3f;
		int start = OvRtSnap(m, from, hh, water, swim, out bool su);
		int end = OvRtSnap(m, goal, hh, water, swim, out bool eu);
		if (start < 0 || end < 0)
		{
			info = start < 0 ? "no place to stand by the map where it is" : "no place to stand by the map at the goal";
			return -1;
		}
		// the others: where they stand and where they walk to (the board)
		s_rtOthers.Clear();
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h == null || h == this || h._vp != p)
			{
				continue;
			}
			s_rtOthers.Add(h.transform.position);
			if (h._bdGoalOn)
			{
				s_rtOthers.Add(h._bdGoal);
			}
		}
		int n = m.W * m.H;
		if (s_rtG == null || s_rtG.Length < n)
		{
			s_rtG = new float[n];
			s_rtRun = new float[n];
			s_rtFrom = new int[n];
			s_rtSeen = new int[n];
			s_rtStamp = 0;
		}
		s_rtStamp++;
		if (s_rtStamp == int.MaxValue)
		{
			System.Array.Clear(s_rtSeen, 0, s_rtSeen.Length);
			s_rtStamp = 1;
		}
		int stamp = s_rtStamp, closed = -stamp;
		s_rtHeap.Clear();
		s_rtHeapF.Clear();
		Vector3 ep = OvRtPoint(m, end, 0f);
		s_rtG[start] = 0f;
		s_rtRun[start] = 0f;
		s_rtFrom[start] = -1;
		s_rtSeen[start] = stamp;
		OvRtPush(start, OvRtH(m, start, ep));
		int looked = 0;
		bool unknown = su || eu, capped = false;
		int blkA = -1, blkB = -1;
		float blkCost = float.MaxValue;
		while (s_rtHeap.Count > 0)
		{
			int a = OvRtPop();
			if (s_rtSeen[a] == closed)
			{
				continue;
			}
			s_rtSeen[a] = closed;
			if (a == end)
			{
				break;
			}
			if (++looked > OvRtMaxNodes)
			{
				capped = true;
				break;
			}
			int ax = a % m.W, az = a / m.W;
			float fa = OvRtStandOn(m, a, hh, water, swim);
			if (a == start && float.IsNaN(fa))
			{
				fa = from.y;
			}
			for (int dz = -1; dz <= 1; dz++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					if (dx == 0 && dz == 0)
					{
						continue;
					}
					int bx = ax + dx, bz = az + dz;
					if (bx < 0 || bz < 0 || bx >= m.W || bz >= m.H)
					{
						continue;
					}
					int b = bz * m.W + bx;
					if (s_rtSeen[b] == closed)
					{
						continue;
					}
					if (m.ReadAt[b] < 0f)
					{
						unknown = true;
						continue;
					}
					float fb = OvRtStandOn(m, b, hh, water, swim);
					if (float.IsNaN(fb))
					{
						continue;
					}
					float rise = fb - fa;
					// diagonal: not across the corner of a square it can't stand on
					if (dx != 0 && dz != 0)
					{
						float f1 = OvRtStandOn(m, az * m.W + bx, hh, water, swim), f2 = OvRtStandOn(m, bz * m.W + ax, hh, water, swim);
						if (float.IsNaN(f1) || float.IsNaN(f2) || f1 - fa > OvRtHopUp || f2 - fa > OvRtHopUp)
						{
							continue;
						}
					}
					float step = dx != 0 && dz != 0 ? OvMapCell * 1.4142f : OvMapCell;
					if (rise > OvRtHopUp)
					{
						// MP4: a step too high - the cheapest one is kept (its foot's cost so far + the rest to the goal)
						float bc = s_rtG[a] + OvRtH(m, b, ep);
						if (rise < 8f && bc < blkCost)
						{
							blkCost = bc;
							blkA = a;
							blkB = b;
						}
						continue;
					}
					if (rise < -maxDrop)
					{
						continue;
					}
					// the mover's DD4 rule: half-metre steps down steeper than a player stands on add up into one fall
					float run = rise < -OvRtSteep && !OvRtOnBuilt(m, b, fb) ? s_rtRun[a] - rise : 0f;
					if (run > maxDrop)
					{
						continue;
					}
					float cost = step;
					if ((m.Flags[b] & MfVein) != 0)
					{
						cost += OvRtVeinCost;   // G2: over the vein - round it through the trench unless that is far longer
					}
					if (rise > OvRtStepUp)
					{
						cost += 1f;
					}
					if (-rise > OvRtBigDrop)
					{
						cost += 2f;
					}
					if (s_rtOthers.Count > 0 && b != end)
					{
						Vector3 bp = OvRtPoint(m, b, fb);
						foreach (Vector3 o in s_rtOthers)
						{
							if (Flat(o, bp) <= OvRtCrowd && Mathf.Abs(o.y - fb) < 2f)
							{
								cost += 3f;
								break;
							}
						}
					}
					float g = s_rtG[a] + cost;
					if (s_rtSeen[b] == stamp && g >= s_rtG[b])
					{
						continue;
					}
					s_rtSeen[b] = stamp;
					s_rtG[b] = g;
					s_rtRun[b] = run;
					s_rtFrom[b] = a;
					OvRtPush(b, g + OvRtH(m, b, ep));
				}
			}
		}
		if (s_rtSeen[end] != closed)
		{
			if (capped || unknown)
			{
				info = capped ? $"the map search stopped after {OvRtMaxNodes} squares" : "squares not read yet on the way";
				return -1;
			}
			if (blkA >= 0)
			{
				_rtBlockFoot = OvRtPoint(m, blkA, OvRtStandOn(m, blkA, hh, water, swim));
				_rtBlockTop = OvRtPoint(m, blkB, OvRtStandOn(m, blkB, hh, water, swim));
				_rtBlockAt = Time.time;
				_rtBlockGoal = goal;
				hadBlock = true;
				info = $"a step of {_rtBlockTop.y - _rtBlockFoot.y:F1} m at {MHR_Mover.Fmt(_rtBlockFoot)}";
			}
			else
			{
				info = $"no way there by the map ({looked} squares looked at)";
			}
			s_rtLastHadBlock = hadBlock;
			return 0;
		}
		// the squares, start to end
		s_rtTmp.Clear();
		for (int i = end; i >= 0; i = s_rtFrom[i])
		{
			s_rtTmp.Add(i);
			if (i == start)
			{
				break;
			}
		}
		s_rtTmp.Reverse();
		outSquares.AddRange(s_rtTmp);
		s_rtOnRoute.Clear();
		foreach (int q in s_rtTmp)
		{
			s_rtOnRoute.Add(q);
		}
		// the corners: from each corner, the furthest square on the route it can walk to in a straight line by the same
		// rules; every hop and drop square stays a corner (the mover steers straight at it and hops or steps down there)
		int c0 = 0;
		while (c0 < s_rtTmp.Count - 1)
		{
			int best = c0 + 1;
			for (int j = c0 + 2; j < s_rtTmp.Count && j <= c0 + 40; j++)
			{
				if (!OvRtLine(m, s_rtTmp, c0, j, hh, water, swim))
				{
					break;
				}
				best = j;
			}
			int sq = s_rtTmp[best];
			float fy = OvRtStandOn(m, sq, hh, water, swim);
			outPath.Add(best == s_rtTmp.Count - 1 ? new Vector3(goal.x, float.IsNaN(fy) ? goal.y : fy, goal.z) : OvRtPoint(m, sq, fy));
			c0 = best;
		}
		if (outPath.Count == 0)
		{
			outPath.Add(goal);
		}
		info = $"{outPath.Count} corner(s), {s_rtTmp.Count} squares, {looked} looked at";
		return 1;
	}

	// MP1: the straight line from route square i to route square j: every square it crosses is one it can stand on, each
	// step is walked (no hop and no drop past a walked step), and no square near another companion that the route itself
	// kept clear of
	private bool OvRtLine(OvMap m, List<int> sq, int i, int j, float hh, float water, float swim)
	{
		for (int k = i + 1; k <= j; k++)
		{
			float a = OvRtStandOn(m, sq[k - 1], hh, water, swim), b = OvRtStandOn(m, sq[k], hh, water, swim);
			if (float.IsNaN(a) || float.IsNaN(b) || Mathf.Abs(b - a) > OvRtStepUp)
			{
				return false;
			}
		}
		Vector3 pa = OvRtPoint(m, sq[i], 0f), pb = OvRtPoint(m, sq[j], 0f);
		float len = Flat(pa, pb);
		float prev = OvRtStandOn(m, sq[i], hh, water, swim);
		for (float t = 0.25f; t < len; t += 0.25f)
		{
			Vector3 q = Vector3.Lerp(pa, pb, t / len);
			int qi = OvMapIndex(m, q);
			if (qi < 0)
			{
				return false;
			}
			float f = OvRtStandOn(m, qi, hh, water, swim);
			if (float.IsNaN(f) || Mathf.Abs(f - prev) > OvRtStepUp)
			{
				return false;
			}
			if ((m.Flags[qi] & MfVein) != 0 && !s_rtOnRoute.Contains(qi))
			{
				return false;   // G2: a short cut never crosses vein squares the route itself kept off
			}
			if (s_rtOthers.Count > 0 && !s_rtOnRoute.Contains(qi))
			{
				Vector3 qp = OvRtPoint(m, qi, f);
				foreach (Vector3 o in s_rtOthers)
				{
					if (Flat(o, qp) <= OvRtCrowd && Mathf.Abs(o.y - f) < 2f)
					{
						return false;
					}
				}
			}
			prev = f;
		}
		return true;
	}

	private static float OvRtH(OvMap m, int i, Vector3 ep)
	{
		Vector3 p = OvRtPoint(m, i, 0f);
		return Flat(p, ep);
	}

	// a binary heap of squares by their estimated cost
	private static void OvRtPush(int i, float f)
	{
		s_rtHeap.Add(i);
		s_rtHeapF.Add(f);
		int c = s_rtHeap.Count - 1;
		while (c > 0)
		{
			int pr = (c - 1) / 2;
			if (s_rtHeapF[pr] <= s_rtHeapF[c])
			{
				break;
			}
			OvRtSwap(c, pr);
			c = pr;
		}
	}

	private static int OvRtPop()
	{
		int top = s_rtHeap[0];
		int last = s_rtHeap.Count - 1;
		s_rtHeap[0] = s_rtHeap[last];
		s_rtHeapF[0] = s_rtHeapF[last];
		s_rtHeap.RemoveAt(last);
		s_rtHeapF.RemoveAt(last);
		int c = 0;
		while (true)
		{
			int l = c * 2 + 1, r = l + 1, s = c;
			if (l < s_rtHeap.Count && s_rtHeapF[l] < s_rtHeapF[s])
			{
				s = l;
			}
			if (r < s_rtHeap.Count && s_rtHeapF[r] < s_rtHeapF[s])
			{
				s = r;
			}
			if (s == c)
			{
				break;
			}
			OvRtSwap(c, s);
			c = s;
		}
		return top;
	}

	private static void OvRtSwap(int a, int b)
	{
		int t = s_rtHeap[a];
		s_rtHeap[a] = s_rtHeap[b];
		s_rtHeap[b] = t;
		float tf = s_rtHeapF[a];
		s_rtHeapF[a] = s_rtHeapF[b];
		s_rtHeapF[b] = tf;
	}

	private static bool s_rtLastHadBlock;

	private static readonly List<Vector3> s_rtProbePath = new List<Vector3>();

	private static readonly List<int> s_rtProbeSquares = new List<int>();

	// MP3: true = the map says there is no way from where it stands to `to`, and not just one step too high (a step is
	// what MP4's way up cuts on the walk); false = there is a way, or one step in the way, or the map can't tell. Its own
	// route (the mover's) is left as it is.
	internal bool OvRtNoWay(Vector3 to, out string info)
	{
		info = null;
		OvPlan p = _vp;
		OvMap m = p?.Map;
		if (m == null || !m.Built || OvMapIndex(m, base.transform.position) < 0 || OvMapIndex(m, to) < 0)
		{
			return false;
		}
		s_rtProbePath.Clear();
		s_rtProbeSquares.Clear();
		s_rtLastHadBlock = false;
		long t0 = Stopwatch.GetTimestamp();
		int r = OvRtSearch(p, m, base.transform.position, to, s_rtProbePath, s_rtProbeSquares, out info);
		s_rtTicks += Stopwatch.GetTimestamp() - t0;
		s_rtSearches++;
		if (r == 1)
		{
			s_rtFound++;
		}
		else if (r == 0)
		{
			s_rtNone++;
		}
		else
		{
			s_rtUnknown++;
		}
		return r == 0 && !s_rtLastHadBlock;
	}

	private Vector3 _rtBlockGoal;

	// H3: the dig of a step on the way to a work place (silver)
	private bool _sdOn;

	private Vector3 _sdPt;

	private float _sdStartG, _sdGoal;

	// MP4: the step that blocked a search toward `goal` (within 1.5 m) in the last 2 s
	private bool OvRtBlockFor(Vector3 goal, out Vector3 foot, out Vector3 top)
	{
		foot = _rtBlockFoot;
		top = _rtBlockTop;
		return Time.time - _rtBlockAt <= 2f && Vector3.Distance(_rtBlockGoal, goal) <= 1.5f;
	}

	// MP4 (the map-path list, John 2026-11-21): on the walk to a part's or a dirt point's place, the map found no route
	// only because of one step too high: the walk goes to that step's foot (latched for the round), and there the way up
	// (OvRampBegin, the existing ramp code with its own give-ups) is cut toward the step's top. Once per round (tried).
	// The walk to the foot giving up (the mover's give-up) ends it and the walk to the place goes on as before. True = it
	// handled the walk this frame.
	private bool OvRtWayUpWalk(Vector3 goal, ref bool tried, ref bool latched, ref Vector3 foot, ref Vector3 top, float dt, string what)
	{
		if (_sdOn)
		{
			// H3: the step dig ended (the vein tick ran its swings): how far its point went down decides whether a next step
			// may be dug - only after one that made progress (0.05 m, OvDigGain), so it always ends
			if (_dgActive)
			{
				return true;
			}
			float gain = _sdStartG - GroundAt(_sdPt);
			string end = _drRepos;
			_sdOn = false;
			_dgDirt = false;
			_drRepos = null;
			latched = false;
			tried = gain < OvDigGain;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"The step at {MHR_Mover.Fmt(_sdPt)} on the way to {what}: dug down {Mathf.Max(0f, gain):F2} m to {GroundAt(_sdPt):F2} (the floor there {_sdGoal:F2}){(end != null ? " - " + end : "")} - {(tried ? "no progress, on to " + what + " as before" : "the way there looked at again")} (H3)");
			return false;
		}
		if (tried)
		{
			return false;
		}
		if (!latched)
		{
			if (!OvRtBlockFor(goal, out foot, out top))
			{
				return false;
			}
			latched = true;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"The pit map's way to {what} is blocked by a step of {top.y - foot.y:F1} m at {MHR_Mover.Fmt(foot)} - going to its foot to cut the way up there");
		}
		Vector3 me = base.transform.position;
		if (Flat(me, foot) <= 1f && Mathf.Abs(me.y - foot.y) <= 1f)
		{
			latched = false;
			_ai?.StopMoving();
			if (OvIsSilver(_vp))
			{
				// H3 (John 11-22: "yes" - they clear the high dirt with the pickaxe; F12 is for ways out only): on silver the step is
				// dug DOWN to the floor at its foot, standing at the foot - a dirt dig (every silver swing check: OvSwingClean,
				// no swing that meets the vein; G1, no swing that would lower its own feet; the floor / too-deep repositions;
				// 2 swings without work end it). One point: the game's dig (1.5 m radius) opens about a body's width
				_sdOn = true;
				_sdPt = new Vector3(top.x, GroundAt(top), top.z);
				_sdStartG = _sdPt.y;
				_sdGoal = foot.y;
				OvDigBegin(_sdPt, null, ramp: false);
				_dgDirt = true;
				_dgGoalY = foot.y;
				if (CompanionsPlugin.LogNormal) LogInfo($"The pit map's way to {what} has a step of {top.y - foot.y:F1} m at {MHR_Mover.Fmt(foot)} - digging it down to the floor ({foot.y:F2}) from its foot (H3)");
				return true;
			}
			tried = true;
			OvRampBegin(top, null, outOfTrench: false, $"the pit map's way to {what} has a step of {top.y - foot.y:F1} m here");
			return true;
		}
		if (OvWalkGaveUp(dt, foot))
		{
			tried = true;
			latched = false;
			_moveTimer = 0f;
			if (CompanionsPlugin.LogNormal) LogInfo($"Couldn't get to the foot of the step at {MHR_Mover.Fmt(foot)} - on to {what} as before");
			return false;
		}
		OvWalk(foot, 0.25f, Flat(me, foot) > 8f, dt);
		return true;
	}

	// MP7: the route the overlay draws for this companion (empty = none held)
	internal List<Vector3> OvRtShown()
	{
		return _rtPlan != null && _rtPath.Count > 0 && Time.time - _rtUsedAt < 2f ? _rtPath : null;
	}

	internal float _rtUsedAt = -10f;

	// MP1: the part of the pit map's once-a-minute line
	private static string OvRtStats()
	{
		double ms = s_rtTicks * 1000.0 / Stopwatch.Frequency;
		string s = $"; map routes: {s_rtSearches} search(es) ({s_rtFound} found, {s_rtNone} no way, {s_rtUnknown} can't tell) in {ms:F1} ms, {s_rtKept} kept";
		s_rtTicks = 0L;
		s_rtSearches = s_rtFound = s_rtNone = s_rtUnknown = s_rtKept = 0;
		return s;
	}
}
