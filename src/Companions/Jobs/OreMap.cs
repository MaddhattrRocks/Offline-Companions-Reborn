using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Companions;

// 1.5.98 R0 (John, 2026-11-20 23:39Z: "move forward with the radar version instead of what we are currently doing. We keep
// running into the same issues"; list approved 23:48Z): the vein's pit map. One map per vein plan, shared by every
// companion on it: the pit (everything inside the trench's outer wall, the plan's pit cells) and 8 m round it, in squares
// of 0.5 m. Each square: the terrain ground, the top solid surface and what it is (one ray down), the open space above the
// ground and what closes it (one ray up), and how far the game still lets the ground be dug (8 m under its first height).
// R0.9 (John 11-21 00:02Z: "the map only is hitting the top of things"): the same two rays keep every solid thing they
// pass, joined into up to 4 layers per square (bottom, top, kind), so overhangs, open space under the vein and stacked
// parts are there; a side is where a neighbour square has nothing solid at that height. R0.8: the vein has a worker while
// any companion is still assigned to it. The companions' squares come from the board. R0 is data only: nothing reads the map yet (R1-R3 will, each its own list).
// Cost: work only while the vein has a worker, a fixed slice a frame (200 squares building / after events, 40 sweeping);
// the overlay (config Advanced/PitMapOverlay, off by default) is redrawn at most once a second.
public partial class HarvestController
{
	private const float OvMapCell = 0.5f;          // R0.1: 0.5 m squares

	private const float OvMapMargin = 8f;          // R0.1: 8 m round the pit (John 23:45Z: enough)

	private const float OvMapMaxSide = 100f;       // R0.1: safety cap on a side (200 x 200 squares)

	private const int OvMapBuildPerFrame = 200;    // R0.2 / R0.3: squares read a frame while building or after events

	private const int OvMapSweepPerFrame = 40;     // R0.4: squares read a frame by the rolling sweep

	private const float OvMapEventNear = 2f;       // R0.3: squares within 2 m of a dig / hit

	private const float OvMapHeadMax = 10f;        // R0.1: the up-ray's length (open space counted to 10 m)

	private const float OvMapAbove = 3f;           // R0.1: the down-ray starts 3 m above the vein's top (or the ground)

	private const float OvMapCheckEvery = 1f;      // R0.1 / R0.3: area and part list looked at once a second

	private const float OvMapCrewEvery = 0.25f;    // R0.5: the companions' squares 4 times a second

	private const float OvMapDrawEvery = 1f;       // R0.6: the overlay redrawn at most once a second

	private const int OvMapLayers = 4;             // R0.9: solid layers kept per square (the lowest 4 above the ground)

	private const float OvMapJoin = 0.05f;         // R0.9: two solid spans less than 5 cm apart are one layer

	// what a square's top (or the thing above its ground) is
	internal const byte MkNone = 0, MkGround = 1, MkOre = 2, MkRock = 3, MkThing = 4, MkBuilt = 5, MkFixed = 6, MkSkip = 255;

	// square flags
	private const byte MfWater = 1, MfCrew = 2, MfGoal = 4;

	private sealed class OvMap
	{
		public float X0, Z0;

		public int W, H;

		public float TopY;

		public float[] Ground, Top, Head, Floor, ReadAt;

		// MP1: when each square's read last changed what it holds (a map route over a changed square is searched again)
		public float[] ChangedAt;

		public byte[] Kind, Over, Flags;

		// R0.9: the solid layers above the ground at each square, lowest first: square i's layer j is at i * OvMapLayers + j
		public float[] LBot, LTop;

		public byte[] LKind, LCount;

		public bool[] Dirty;

		public readonly List<int> DirtyList = new List<int>();

		public readonly List<int> CrewMarked = new List<int>();

		public readonly List<Vector3> CrewAt3 = new List<Vector3>();   // R0.9: where each companion stands (white) ...

		public readonly List<Vector3> GoalAt3 = new List<Vector3>();   // ... and walks to (yellow), drawn at that height

		public readonly List<Bounds> Parts = new List<Bounds>();

		// MP7: each companion's map route for the overlay (its position first, then the corners), and their sum to tell a change
		public readonly List<List<Vector3>> Routes = new List<List<Vector3>>();

		public float RouteSig, RouteAt;

		public readonly Dictionary<int, byte> KindCache = new Dictionary<int, byte>();

		public int BuildAt, SweepAt, Builds, BuildFrames;

		public long BuildRays;

		public bool Built, Idle = true, Changed;

		public float BuildStart, CheckAt, CrewAt, DrawAt;

		public string BuildWhy = "built";   // for the log line when the build pass ends

		public OvMapView View;
	}

	private static readonly RaycastHit[] s_mapHits = new RaycastHit[32];

	private static readonly RaycastHit[] s_mapUpHits = new RaycastHit[32];

	// R0.9: one square's spans while it is read (bottom, top, kind), before they are joined into layers
	private static readonly List<Vector3> s_mapSpans = new List<Vector3>();

	private static readonly List<Bounds> s_mapParts = new List<Bounds>();

	private static readonly List<Bounds> s_mapRockParts = new List<Bounds>();

	private static readonly List<OvPlan> s_mapShown = new List<OvPlan>();

	private static int s_mapMask, s_mapTerrain = -1;

	// R0.7: the cost, written once a minute (Detailed)
	private static long s_mapRays, s_mapEvCells, s_mapBuildCells, s_mapSweepCells, s_mapMaxTicks;

	private static int s_mapFrames, s_mapMaxMaps;

	private static float s_mapStatAt = -1f;

	// ---------- the tick (CompanionsPlugin.Update, once a frame) ----------
	internal static void OvMapTickAll()
	{
		float now = Time.time;
		long t0 = Stopwatch.GetTimestamp();
		int maps = 0;
		for (int pi = 0; pi < s_ovPlans.Count; pi++)
		{
			OvPlan p = s_ovPlans[pi];
			if (OvMapTick(p, now))
			{
				maps++;
			}
		}
		// R0.6: overlays of plans that are gone or idle, or all of them when the toggle is off
		for (int i = s_mapShown.Count - 1; i >= 0; i--)
		{
			OvPlan p = s_mapShown[i];
			if (!s_ovPlans.Contains(p) || p.Map == null || p.Map.Idle || !OvMapOverlayOn())
			{
				p.Map?.View?.Destroy();
				if (p.Map != null)
				{
					p.Map.View = null;
				}
				s_mapShown.RemoveAt(i);
			}
		}
		long dt = Stopwatch.GetTimestamp() - t0;
		OvMapStats(now, dt, maps);
	}

	private static bool OvMapOverlayOn()
	{
		return ModConfig.PitMapOverlay != null && ModConfig.PitMapOverlay.Value;
	}

	// one plan's map for this frame; true = it worked (the vein has a worker)
	private static bool OvMapTick(OvPlan p, float now)
	{
		// R0.8 (log 58676f4a: the map read itself again 3 times while all three were clearing pit things): on the vein =
		// any companion still assigned to it (its plan is this one), whatever it is doing for it - not "worked it lately"
		bool active = false;
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h != null && h._vp == p)
			{
				active = true;
				break;
			}
		}
		OvMap m = p.Map;
		if (!active)
		{
			// R0 (lifetime): nobody on the vein - no work; the data stays with the plan (kept 1 hour, OvPlanKeep)
			if (m != null)
			{
				m.Idle = true;
			}
			return false;
		}
		if (m == null || now >= m.CheckAt)
		{
			m = OvMapArea(p, m);
			if (m == null)
			{
				return false;
			}
			m.CheckAt = now + OvMapCheckEvery;
			if (!m.Idle)
			{
				OvMapPartsCheck(p, m);
			}
		}
		if (m.Idle)
		{
			// R0 (lifetime): a worker again (or the first one) - every square read again, the ground may have changed
			m.Idle = false;
			for (int i = 0; i < m.ReadAt.Length; i++)
			{
				m.ReadAt[i] = -1f;
			}
			m.Built = false;
			m.BuildAt = 0;
			m.BuildStart = now;
			m.BuildFrames = 0;
			m.BuildRays = 0;
			m.BuildWhy = m.Builds == 0 ? "built" : "read again (a companion came back to it)";
			OvMapPartsCheck(p, m);
		}
		if (now >= m.CrewAt)
		{
			m.CrewAt = now + OvMapCrewEvery;
			OvMapCrew(p, m);
		}
		// R0.3: squares near a dig / hit / a part that went first, then R0.2 building or R0.4 the sweep
		int budget = OvMapBuildPerFrame;
		while (budget > 0 && m.DirtyList.Count > 0)
		{
			int last = m.DirtyList.Count - 1;
			int i = m.DirtyList[last];
			m.DirtyList.RemoveAt(last);
			m.Dirty[i] = false;
			OvMapRead(p, m, i);
			budget--;
			s_mapEvCells++;
		}
		int n = m.W * m.H;
		if (!m.Built)
		{
			m.BuildFrames++;
			while (budget > 0 && m.BuildAt < n)
			{
				if (m.ReadAt[m.BuildAt] < 0f)
				{
					OvMapRead(p, m, m.BuildAt);
					m.BuildRays += 3;
					budget--;
					s_mapBuildCells++;
				}
				m.BuildAt++;
			}
			if (m.BuildAt >= n)
			{
				m.Built = true;
				m.Builds++;
				OvMapBuiltLog(p, m, now);
			}
		}
		else
		{
			for (int k = 0; k < OvMapSweepPerFrame; k++)
			{
				m.SweepAt = (m.SweepAt + 1) % n;
				OvMapRead(p, m, m.SweepAt);
				s_mapSweepCells++;
			}
		}
		if (OvMapOverlayOn())
		{
			if (m.View == null)
			{
				m.View = new OvMapView();
				m.DrawAt = 0f;
				m.Changed = true;
				if (!s_mapShown.Contains(p))
				{
					s_mapShown.Add(p);
				}
			}
			if (now >= m.RouteAt)
			{
				m.RouteAt = now + 0.5f;
				OvMapRoutesShown(p, m);   // MP7: twice a second, only with the overlay on
			}
			if (m.Changed && now >= m.DrawAt)
			{
				m.DrawAt = now + OvMapDrawEvery;
				m.Changed = false;
				m.View.Draw(m);
			}
		}
		return true;
	}

	// ---------- R0.1: the map's area (the pit + 8 m), made or grown once a second ----------
	private static OvMap OvMapArea(OvPlan p, OvMap m)
	{
		float x0 = p.Box.min.x, x1 = p.Box.max.x, z0 = p.Box.min.z, z1 = p.Box.max.z;
		if (p.PitCells.Count > 0)
		{
			// the pit's cells are 1 m, centred on whole metres
			x0 = Mathf.Min(x0, p.PitX0 - 0.5f);
			x1 = Mathf.Max(x1, p.PitX1 + 0.5f);
			z0 = Mathf.Min(z0, p.PitZ0 - 0.5f);
			z1 = Mathf.Max(z1, p.PitZ1 + 0.5f);
		}
		foreach (OvSpot sp in p.Spots)
		{
			x0 = Mathf.Min(x0, sp.P.x);
			x1 = Mathf.Max(x1, sp.P.x);
			z0 = Mathf.Min(z0, sp.P.z);
			z1 = Mathf.Max(z1, sp.P.z);
		}
		x0 -= OvMapMargin;
		x1 += OvMapMargin;
		z0 -= OvMapMargin;
		z1 += OvMapMargin;
		if (m != null)
		{
			m.TopY = p.Box.max.y;
			// never shrinks: the new area holds the old one
			if (x0 >= m.X0 && z0 >= m.Z0 && x1 <= m.X0 + m.W * OvMapCell && z1 <= m.Z0 + m.H * OvMapCell)
			{
				return m;
			}
			x0 = Mathf.Min(x0, m.X0);
			z0 = Mathf.Min(z0, m.Z0);
			x1 = Mathf.Max(x1, m.X0 + m.W * OvMapCell);
			z1 = Mathf.Max(z1, m.Z0 + m.H * OvMapCell);
		}
		// the safety cap: a side longer than 100 m is cut, round the vein's centre
		Vector3 c = p.Box.center;
		if (x1 - x0 > OvMapMaxSide)
		{
			x0 = Mathf.Max(x0, c.x - OvMapMaxSide * 0.5f);
			x1 = x0 + OvMapMaxSide;
		}
		if (z1 - z0 > OvMapMaxSide)
		{
			z0 = Mathf.Max(z0, c.z - OvMapMaxSide * 0.5f);
			z1 = z0 + OvMapMaxSide;
		}
		// on the 0.5 m grid, so an old map's squares copy straight across
		x0 = Mathf.Floor(x0 / OvMapCell) * OvMapCell;
		z0 = Mathf.Floor(z0 / OvMapCell) * OvMapCell;
		int w = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / OvMapCell));
		int h = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / OvMapCell));
		if (m != null && Mathf.Approximately(x0, m.X0) && Mathf.Approximately(z0, m.Z0) && w == m.W && h == m.H)
		{
			return m;   // capped: it can't grow further
		}
		OvMap nm = new OvMap
		{
			X0 = x0,
			Z0 = z0,
			W = w,
			H = h,
			TopY = p.Box.max.y,
		};
		int n = w * h;
		nm.Ground = new float[n];
		nm.Top = new float[n];
		nm.Head = new float[n];
		nm.Floor = new float[n];
		nm.ReadAt = new float[n];
		nm.ChangedAt = new float[n];
		nm.Kind = new byte[n];
		nm.Over = new byte[n];
		nm.Flags = new byte[n];
		nm.Dirty = new bool[n];
		nm.LBot = new float[n * OvMapLayers];
		nm.LTop = new float[n * OvMapLayers];
		nm.LKind = new byte[n * OvMapLayers];
		nm.LCount = new byte[n];
		for (int i = 0; i < n; i++)
		{
			nm.ReadAt[i] = -1f;
		}
		if (m != null)
		{
			// grown: the squares already read are kept, only the new ones are read
			int ox = Mathf.RoundToInt((m.X0 - x0) / OvMapCell), oz = Mathf.RoundToInt((m.Z0 - z0) / OvMapCell);
			for (int zz = 0; zz < m.H; zz++)
			{
				for (int xx = 0; xx < m.W; xx++)
				{
					int nx = xx + ox, nz = zz + oz;
					if (nx < 0 || nz < 0 || nx >= w || nz >= h)
					{
						continue;
					}
					int a = zz * m.W + xx, b = nz * w + nx;
					nm.Ground[b] = m.Ground[a];
					nm.Top[b] = m.Top[a];
					nm.Head[b] = m.Head[a];
					nm.Floor[b] = m.Floor[a];
					nm.ReadAt[b] = m.ReadAt[a];
					nm.ChangedAt[b] = m.ChangedAt[a];
					nm.Kind[b] = m.Kind[a];
					nm.Over[b] = m.Over[a];
					nm.Flags[b] = (byte)(m.Flags[a] & MfWater);
					nm.LCount[b] = m.LCount[a];
					for (int j = 0; j < OvMapLayers; j++)
					{
						nm.LBot[b * OvMapLayers + j] = m.LBot[a * OvMapLayers + j];
						nm.LTop[b * OvMapLayers + j] = m.LTop[a * OvMapLayers + j];
						nm.LKind[b * OvMapLayers + j] = m.LKind[a * OvMapLayers + j];
					}
					if (m.Dirty[a])
					{
						nm.Dirty[b] = true;
						nm.DirtyList.Add(b);
					}
				}
			}
			foreach (KeyValuePair<int, byte> kv in m.KindCache)
			{
				nm.KindCache[kv.Key] = kv.Value;
			}
			nm.Parts.AddRange(m.Parts);
			nm.Idle = m.Idle;
			nm.Builds = m.Builds;
			nm.BuildStart = Time.time;
			nm.BuildWhy = "grown (the pit got bigger)";
			nm.View = m.View;
			nm.Changed = true;
			if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogInfo($"[Gather] Pit map of \"{p.Name}\": grew from {m.W} x {m.H} to {w} x {h} squares (the pit got bigger) - the new squares are read now");
		}
		p.Map = nm;
		return nm;
	}

	private static int OvMapIndex(OvMap m, Vector3 pt)
	{
		if (m == null)
		{
			return -1;
		}
		int x = Mathf.FloorToInt((pt.x - m.X0) / OvMapCell), z = Mathf.FloorToInt((pt.z - m.Z0) / OvMapCell);
		return x < 0 || z < 0 || x >= m.W || z >= m.H ? -1 : z * m.W + x;
	}

	// ---------- R2.1: is this square a place to stand ----------

	// R2.1: the map's answer for standing at pt (the square is read again first). 1 = yes, feet = where its feet go: the
	// ground when nothing solid stands on it and there is its height of open space above it; otherwise the top of the
	// lowest built piece on the square (John, 2026-11-21: a floor he built in the pit may be stood on) with its height of
	// open space above that. 0 = no (why says what is there) - never on stone, rock, a vein part, a thing or what can't be
	// damaged. -1 = don't know (no map, off the map, a square not read yet).
	private int OvMapStand(Vector3 pt, out float feet, out string why)
	{
		feet = pt.y;
		why = null;
		OvMap m = _vp?.Map;
		int i = OvMapIndex(m, pt);
		if (i < 0 || m.ReadAt[i] < 0f)
		{
			return -1;
		}
		OvMapRead(_vp, m, i);
		float g = m.Ground[i];
		float hh = _character != null ? _character.GetHeight() : 1.8f;
		int li = i * OvMapLayers, lc = m.LCount[i];
		if (lc == 0 || m.LBot[li] - g >= hh)
		{
			feet = g;
			return 1;
		}
		for (int j = 0; j < lc; j++)
		{
			if (m.LKind[li + j] != MkBuilt)
			{
				continue;
			}
			float top = m.LTop[li + j];
			float above = j + 1 < lc ? m.LBot[li + j + 1] - top : float.MaxValue;
			if (above >= hh)
			{
				feet = top;
				return 1;
			}
			why = $"a built piece with {above:F1} m of room above it (its height is {hh:F1} m)";
			return 0;
		}
		float room = m.LBot[li] - g;
		why = room > OvMapJoin ? $"{room:F1} m of room under {OvMapKindName(m.LKind[li])} (its height is {hh:F1} m)" : $"{OvMapKindName(m.LKind[li])} on it";
		return 0;
	}

	// ---------- S4: the lowest ground round a drop-down's landing, from the map ----------

	// S4 (test of 05e19c2: Je's 1.7 m drop-down landed on a shelf by the trench wall and it went on down, 8.1 m in all): the
	// lowest top (what a fall would land on) of the read squares within r of `land` on its goal's side (and the landing's
	// own square). NaN = don't know (no map, landing off the map, nothing read there): the mover's own checks decide.
	internal float OvMapLowNear(Vector3 land, Vector3 goal, float r)
	{
		OvMap m = _vp?.Map;
		if (m == null || OvMapIndex(m, land) < 0)
		{
			return float.NaN;
		}
		Vector3 tg = goal - land;
		tg.y = 0f;
		bool anyWay = tg.sqrMagnitude < 0.25f;
		float low = float.MaxValue;
		for (float dx = -r; dx <= r + 0.01f; dx += OvMapCell)
		{
			for (float dz = -r; dz <= r + 0.01f; dz += OvMapCell)
			{
				if (dx * dx + dz * dz > r * r || (!anyWay && dx * tg.x + dz * tg.z < 0f))
				{
					continue;
				}
				int i = OvMapIndex(m, new Vector3(land.x + dx, land.y, land.z + dz));
				if (i < 0 || m.ReadAt[i] < 0f)
				{
					continue;
				}
				low = Mathf.Min(low, Mathf.Max(m.Ground[i], m.Top[i]));
			}
		}
		return low == float.MaxValue ? float.NaN : low;
	}

	// ---------- R1.1: what is in front, from the map ----------

	private const float OvMapAheadNear = 0.5f;     // R1.1: the squares 0.5 m ...

	private const float OvMapAheadFar = 1f;        // ... and 1 m in front of where it faces

	// R1.1: the map's answer for the squares 0.5 m and 1 m in front of where this companion faces (only while it faces
	// within 60 degrees of toward): 1 = a wall (a layer whose bottom is below its head and whose top is more than a step
	// above its feet; the square is read again first), 0 = clear, -1 = don't know (no map, off the map, a square not
	// read yet, not facing toward, or the game's swing line toward it meets nothing). kind = the layer's kind, fc / fat =
	// the first thing on the game's swing line toward it and where it was hit (the rock itself, never its box).
	private int OvMapWallAhead(Vector3 toward, out byte kind, out Collider fc, out Vector3 fat)
	{
		kind = MkNone;
		fc = null;
		fat = toward;
		OvMap m = _vp?.Map;
		if (m == null)
		{
			return -1;
		}
		Vector3 me = base.transform.position;
		Vector3 face = base.transform.forward;
		face.y = 0f;
		Vector3 to = toward - me;
		to.y = 0f;
		if (face.sqrMagnitude < 0.0001f || to.sqrMagnitude < 0.0001f)
		{
			return -1;
		}
		face.Normalize();
		if (Vector3.Dot(face, to.normalized) <= 0.5f)
		{
			return -1;
		}
		float head = me.y + (_character != null ? _character.GetHeight() : 1.8f);
		float step = me.y + OvStepUp;
		float swingY = OvSwingFrom().y;
		for (int s = 0; s < 2; s++)
		{
			Vector3 pt = me + face * (s == 0 ? OvMapAheadNear : OvMapAheadFar);
			int i = OvMapIndex(m, pt);
			if (i < 0 || m.ReadAt[i] < 0f)
			{
				return -1;
			}
			OvMapRead(_vp, m, i);
			int li = i * OvMapLayers;
			for (int j = 0; j < m.LCount[i]; j++)
			{
				float b = m.LBot[li + j], t = m.LTop[li + j];
				if (b >= head || t <= step)
				{
					continue;
				}
				kind = m.LKind[li + j];
				int cx = i % m.W, cz = i / m.W;
				Vector3 aim = new Vector3(m.X0 + (cx + 0.5f) * OvMapCell, Mathf.Clamp(swingY, b, t), m.Z0 + (cz + 0.5f) * OvMapCell);
				fc = OvFirstOnSwing(aim, out fat);
				return fc != null ? 1 : -1;
			}
		}
		return 0;
	}

	// R1.1: a kind's name for the log
	private static string OvMapKindName(byte k)
	{
		switch (k)
		{
			case MkGround: return "ground";
			case MkOre: return "the vein's rock";
			case MkRock: return "rock";
			case MkThing: return "a thing";
			case MkBuilt: return "a built piece";
			case MkFixed: return "something that can't be damaged";
			default: return "nothing";
		}
	}

	// ---------- R0.1: read one square ----------
	private static void OvMapRead(OvPlan p, OvMap m, int i)
	{
		if (s_mapMask == 0)
		{
			s_mapMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
			s_mapTerrain = LayerMask.NameToLayer("terrain");
		}
		int cx = i % m.W, cz = i / m.W;
		Vector3 c = new Vector3(m.X0 + (cx + 0.5f) * OvMapCell, 0f, m.Z0 + (cz + 0.5f) * OvMapCell);
		// (a) the terrain ground (GroundAt: the terrain only) and (d) the lowest the game lets it be dug
		float g = GroundAt(c);
		c.y = g;
		float floor = TFloorFrom(c, g);
		// (b) R0.9: every solid thing on the square's line, from 3 m above the vein's top (or the ground, if higher) down to
		// the ground: one ray down gives each thing's top face, one ray up from just above the ground gives each thing's
		// bottom face (the game reports each collider once per ray: where the ray first meets it)
		float from = Mathf.Max(g, m.TopY) + OvMapAbove;
		float top = g;
		byte kind = MkNone;
		int k = Physics.RaycastNonAlloc(new Vector3(c.x, from, c.z), Vector3.down, s_mapHits, from - g + 1f, s_mapMask, QueryTriggerInteraction.Ignore);
		int ku = Physics.RaycastNonAlloc(new Vector3(c.x, g + 0.05f, c.z), Vector3.up, s_mapUpHits, from - g, s_mapMask, QueryTriggerInteraction.Ignore);
		s_mapSpans.Clear();
		float best = float.MaxValue;
		for (int h = 0; h < k; h++)
		{
			RaycastHit rh = s_mapHits[h];
			if (rh.collider == null)
			{
				continue;
			}
			byte kk = OvMapKind(p, m, rh.collider);
			if (kk == MkSkip)
			{
				continue;
			}
			if (rh.distance < best)
			{
				// the top surface (R0.1, as before)
				best = rh.distance;
				top = rh.point.y;
				kind = kk;
			}
			if (kk == MkGround)
			{
				continue;
			}
			// its bottom: where the up-ray met the same collider; not met = the up-ray started inside it, so it reaches
			// down into the ground (its bottom is the ground)
			float bot = g;
			bool met = false;
			for (int u = 0; u < ku; u++)
			{
				if (s_mapUpHits[u].collider == rh.collider)
				{
					bot = Mathf.Min(s_mapUpHits[u].point.y, rh.point.y);
					met = true;
					break;
				}
			}
			if (!met && kk == MkOre)
			{
				// R0.11 (log f7ee9ca6: 10 squares of open space under the vein, 311 before): a vein part the up-ray didn't
				// meet (its rock may have no bottom face) - its bottom is its own game box's bottom, never below the ground
				bot = Mathf.Clamp(rh.collider.bounds.min.y, g, rh.point.y);
			}
			s_mapSpans.Add(new Vector3(bot, rh.point.y, kk));
		}
		// a thing the up-ray met that the down-ray didn't: the down-ray started inside it (it rises above the ray's start)
		for (int u = 0; u < ku; u++)
		{
			RaycastHit uh = s_mapUpHits[u];
			if (uh.collider == null || uh.collider.gameObject.layer == s_mapTerrain)
			{
				continue;
			}
			bool seen = false;
			for (int h = 0; h < k; h++)
			{
				if (s_mapHits[h].collider == uh.collider)
				{
					seen = true;
					break;
				}
			}
			byte kk = seen ? MkSkip : OvMapKind(p, m, uh.collider);
			if (kk != MkSkip)
			{
				s_mapSpans.Add(new Vector3(uh.point.y, from, kk));
			}
		}
		// joined into layers, lowest first: spans that overlap or are under 5 cm apart are one layer; its kind, when they
		// differ: this vein's part first, then can't be damaged, built, rock / stone, a thing
		s_mapSpans.Sort((Vector3 a, Vector3 b) => a.x.CompareTo(b.x));
		int li = i * OvMapLayers, lc = 0;
		bool layersChanged = false;
		float lb = 0f, lt = 0f;
		byte lk = MkNone;
		for (int sIdx = 0; sIdx <= s_mapSpans.Count; sIdx++)
		{
			bool last = sIdx == s_mapSpans.Count;
			Vector3 sp = last ? default(Vector3) : s_mapSpans[sIdx];
			if (!last && sIdx > 0 && sp.x <= lt + OvMapJoin)
			{
				lt = Mathf.Max(lt, sp.y);
				lk = OvMapKindFirst(lk, (byte)sp.z);
				continue;
			}
			if (sIdx > 0 && lc < OvMapLayers)
			{
				layersChanged |= lc >= m.LCount[i] || Mathf.Abs(m.LBot[li + lc] - lb) > 0.05f || Mathf.Abs(m.LTop[li + lc] - lt) > 0.05f || m.LKind[li + lc] != lk;
				m.LBot[li + lc] = lb;
				m.LTop[li + lc] = lt;
				m.LKind[li + lc] = lk;
				lc++;
			}
			if (!last)
			{
				lb = sp.x;
				lt = sp.y;
				lk = (byte)sp.z;
			}
		}
		layersChanged |= lc != m.LCount[i];
		m.LCount[i] = (byte)lc;
		// (c) the open space above the ground: up to the lowest layer (none = open, counted to 10 m); a lowest layer that
		// starts at the ground = no open space under it
		float head = OvMapHeadMax;
		byte over = MkNone;
		if (lc > 0)
		{
			head = Mathf.Min(OvMapHeadMax, m.LBot[li] - g <= OvMapJoin ? 0f : m.LBot[li] - g);
			over = m.LKind[li];
		}
		if (layersChanged)
		{
			m.Changed = true;
		}
		byte flags = (byte)(m.Flags[i] & (MfCrew | MfGoal));
		if (ZoneSystem.instance != null && top < ZoneSystem.instance.m_waterLevel)
		{
			flags |= MfWater;
		}
		if (layersChanged || m.Kind[i] != kind || m.Over[i] != over || m.Flags[i] != flags || Mathf.Abs(m.Top[i] - top) > 0.05f || Mathf.Abs(m.Head[i] - head) > 0.05f || Mathf.Abs(m.Ground[i] - g) > 0.05f || m.ReadAt[i] < 0f)
		{
			m.Changed = true;
			m.ChangedAt[i] = Time.time;   // MP1
		}
		m.Ground[i] = g;
		m.Floor[i] = floor;
		m.Top[i] = top;
		m.Kind[i] = kind;
		m.Head[i] = head;
		m.Over[i] = over;
		m.Flags[i] = flags;
		m.ReadAt[i] = Time.time;
		s_mapRays += 3;
	}

	// R0.9: which kind a joined layer is, when its spans differ
	private static byte OvMapKindFirst(byte a, byte b)
	{
		return OvMapKindRank(b) > OvMapKindRank(a) ? b : a;
	}

	private static int OvMapKindRank(byte k)
	{
		switch (k)
		{
		case MkOre: return 5;
		case MkFixed: return 4;
		case MkBuilt: return 3;
		case MkRock: return 2;
		case MkThing: return 1;
		default: return 0;
		}
	}

	// what a collider is (kept per collider for this vein); MkSkip = not counted (a creature or companion)
	private static byte OvMapKind(OvPlan p, OvMap m, Collider col)
	{
		int id = col.GetInstanceID();
		if (m.KindCache.TryGetValue(id, out byte kc))
		{
			return kc;
		}
		byte k;
		if (col.isTrigger || col.GetComponentInParent<Character>() != null)
		{
			k = MkSkip;
		}
		else if (col.gameObject.layer == s_mapTerrain)
		{
			k = MkGround;
		}
		else
		{
			MineRock5 m5 = col.GetComponentInParent<MineRock5>();
			if (m5 != null)
			{
				k = p.Rocks.Contains(m5) ? MkOre : MkRock;
			}
			else
			{
				GameObject root = MHR_Drops.Root(col) ?? col.gameObject;
				if (BuiltWhy(root) != null)
				{
					k = MkBuilt;
				}
				else if (OvIsWorldSolid(col))
				{
					k = MkFixed;
				}
				else
				{
					// by its drops (John 11-19: what a thing is comes from what it drops)
					int dk = MHR_Drops.KindOf(root);
					k = dk == MHR_Drops.Stone || dk == MHR_Drops.Ore || root.GetComponent<MineRock>() != null ? MkRock : MkThing;
				}
			}
		}
		m.KindCache[id] = k;
		return k;
	}

	// ---------- R0.3: squares to read again ----------
	private static void OvMapDirty(OvMap m, Vector3 pt, float r)
	{
		if (m == null || m.Idle)
		{
			return;
		}
		OvMapDirtyBox(m, pt.x - r, pt.z - r, pt.x + r, pt.z + r, pt, r);
	}

	private static void OvMapDirtyBox(OvMap m, float x0, float z0, float x1, float z1, Vector3 round, float r)
	{
		int ix0 = Mathf.Max(0, Mathf.FloorToInt((x0 - m.X0) / OvMapCell)), ix1 = Mathf.Min(m.W - 1, Mathf.FloorToInt((x1 - m.X0) / OvMapCell));
		int iz0 = Mathf.Max(0, Mathf.FloorToInt((z0 - m.Z0) / OvMapCell)), iz1 = Mathf.Min(m.H - 1, Mathf.FloorToInt((z1 - m.Z0) / OvMapCell));
		for (int z = iz0; z <= iz1; z++)
		{
			for (int x = ix0; x <= ix1; x++)
			{
				int i = z * m.W + x;
				if (m.Dirty[i])
				{
					continue;
				}
				if (r > 0f)
				{
					float dx = m.X0 + (x + 0.5f) * OvMapCell - round.x, dz = m.Z0 + (z + 0.5f) * OvMapCell - round.z;
					if (dx * dx + dz * dz > r * r)
					{
						continue;
					}
				}
				m.Dirty[i] = true;
				m.DirtyList.Add(i);
			}
		}
	}

	// R0.3: once a second, the vein's live parts (MHR_Vein.Areas, the game's box of each part); a part that has gone: the
	// squares under its box are read again
	private static void OvMapPartsCheck(OvPlan p, OvMap m)
	{
		s_mapParts.Clear();
		foreach (MineRock5 r in p.Rocks)
		{
			if (r == null)
			{
				continue;
			}
			OvPartBoxes(r, s_mapRockParts);
			s_mapParts.AddRange(s_mapRockParts);
		}
		foreach (Bounds old in m.Parts)
		{
			bool still = false;
			foreach (Bounds b in s_mapParts)
			{
				if ((b.center - old.center).sqrMagnitude < 0.0025f)
				{
					still = true;
					break;
				}
			}
			if (!still)
			{
				OvMapDirtyBox(m, old.min.x - OvMapCell, old.min.z - OvMapCell, old.max.x + OvMapCell, old.max.z + OvMapCell, default(Vector3), 0f);
			}
		}
		m.Parts.Clear();
		m.Parts.AddRange(s_mapParts);
	}

	// ---------- R0.5: the companions' squares (the board's positions and walk goals) ----------
	private static void OvMapCrew(OvPlan p, OvMap m)
	{
		foreach (int i in m.CrewMarked)
		{
			m.Flags[i] = (byte)(m.Flags[i] & ~(MfCrew | MfGoal));
		}
		int before = m.CrewMarked.Count;
		int sum = 0;
		foreach (int i in m.CrewMarked)
		{
			sum += i;
		}
		m.CrewMarked.Clear();
		m.CrewAt3.Clear();
		m.GoalAt3.Clear();
		foreach (HarvestController h in p.Workers.Keys)
		{
			if (h == null || h._vp != p)
			{
				continue;
			}
			int i = OvMapIndex(m, h.transform.position);
			if (i >= 0)
			{
				m.Flags[i] |= MfCrew;
				m.CrewMarked.Add(i);
				m.CrewAt3.Add(h.transform.position);
			}
			if (h._bdGoalOn)
			{
				int g = OvMapIndex(m, h._bdGoal);
				if (g >= 0)
				{
					m.Flags[g] |= MfGoal;
					m.CrewMarked.Add(g);
					m.GoalAt3.Add(h._bdGoal);
				}
			}
		}
		int after = 0;
		foreach (int i in m.CrewMarked)
		{
			after += i;
		}
		if (before != m.CrewMarked.Count || sum != after)
		{
			m.Changed = true;
		}
	}

	// MP7: the companions' map routes for the overlay (only while it is on); a change redraws it
	private static void OvMapRoutesShown(OvPlan p, OvMap m)
	{
		m.Routes.Clear();
		float sig = 0f;
		foreach (HarvestController h in p.Workers.Keys)
		{
			List<Vector3> rt = h != null && h._vp == p ? h.OvRtShown() : null;
			if (rt == null)
			{
				continue;
			}
			List<Vector3> line = new List<Vector3>(rt.Count + 1) { h.transform.position };
			line.AddRange(rt);
			m.Routes.Add(line);
			foreach (Vector3 v in rt)
			{
				sig += v.x * 0.37f + v.z * 0.73f + v.y;
			}
			sig += rt.Count * 101f;
		}
		if (Mathf.Abs(sig - m.RouteSig) > 0.01f)
		{
			m.RouteSig = sig;
			m.Changed = true;
		}
	}

	// ---------- R0.7: the log ----------
	private static void OvMapBuiltLog(OvPlan p, OvMap m, float now)
	{
		bool grown = m.BuildWhy.StartsWith("grown");
		if (grown ? !CompanionsPlugin.LogDetailed : !CompanionsPlugin.LogNormal)
		{
			return;
		}
		int n = m.W * m.H;
		int[] kinds = new int[7];
		int water = 0, oreDown = 0, gapUnder = 0;
		for (int i = 0; i < n; i++)
		{
			if (m.Kind[i] < kinds.Length)
			{
				kinds[m.Kind[i]]++;
			}
			if ((m.Flags[i] & MfWater) != 0)
			{
				water++;
			}
			if (m.Over[i] == MkOre)
			{
				if (m.Head[i] <= 0.05f)
				{
					oreDown++;
				}
				else
				{
					gapUnder++;
				}
			}
		}
		long bytes = (long)n * (5 * 4 + 3 + 1 + 1 + OvMapLayers * 9);
		int layered = 0, multi = 0;
		for (int i = 0; i < n; i++)
		{
			if (m.LCount[i] > 0)
			{
				layered++;
			}
			if (m.LCount[i] > 1)
			{
				multi++;
			}
		}
		string what = m.BuildWhy;
		CompanionsPlugin.Log.LogInfo($"[Gather] Pit map of \"{p.Name}\" {what}: {m.W} x {m.H} squares ({m.W * OvMapCell:F0} x {m.H * OvMapCell:F0} m, x {m.X0:F0} to {m.X0 + m.W * OvMapCell:F0}, z {m.Z0:F0} to {m.Z0 + m.H * OvMapCell:F0}), in {now - m.BuildStart:F1} s over {m.BuildFrames} frames, {m.BuildRays} rays, {bytes / 1024} KB. Tops: ground {kinds[MkGround]}, vein part {kinds[MkOre]}, rock/stone {kinds[MkRock]}, tree/thing {kinds[MkThing]}, built {kinds[MkBuilt]}, can't be damaged {kinds[MkFixed]}, nothing found {kinds[MkNone]}; under water {water}. Vein parts reaching down to the ground: {oreDown} squares; open space under a vein part: {gapUnder} squares. Squares with something standing above the ground: {layered} ({multi} with 2 or more layers)");
	}

	private static void OvMapStats(float now, long ticks, int maps)
	{
		if (!CompanionsPlugin.LogDetailed)
		{
			s_mapStatAt = -1f;
			return;
		}
		if (s_mapStatAt < 0f)
		{
			s_mapStatAt = now;
			s_mapRays = s_mapEvCells = s_mapBuildCells = s_mapSweepCells = s_mapMaxTicks = s_mapTotalTicks = 0L;
			s_mapFrames = s_mapMaxMaps = 0;
			return;
		}
		if (maps > 0)
		{
			s_mapFrames++;
			s_mapMaxMaps = Mathf.Max(s_mapMaxMaps, maps);
			s_mapMaxTicks = System.Math.Max(s_mapMaxTicks, ticks);
		}
		s_mapTotalTicks += ticks;
		if (now - s_mapStatAt < 60f)
		{
			return;
		}
		if (s_mapFrames > 0)
		{
			double ms = s_mapTotalTicks * 1000.0 / Stopwatch.Frequency, max = s_mapMaxTicks * 1000.0 / Stopwatch.Frequency;
			CompanionsPlugin.Log.LogInfo($"[PitMap] last {now - s_mapStatAt:F0} s: up to {s_mapMaxMaps} map(s) working over {s_mapFrames} frames, {s_mapRays} rays, squares read: {s_mapBuildCells} building, {s_mapEvCells} after digs/hits/parts gone, {s_mapSweepCells} sweeping; time {ms:F1} ms in all ({ms / s_mapFrames:F3} ms a frame), the most in one frame {max:F2} ms{OvRtStats()}");
		}
		s_mapStatAt = now;
		s_mapRays = s_mapEvCells = s_mapBuildCells = s_mapSweepCells = s_mapMaxTicks = s_mapTotalTicks = 0L;
		s_mapFrames = s_mapMaxMaps = 0;
	}

	private static long s_mapTotalTicks;

	// ---------- R0.6: the overlay (the FarmZoneVisual way: its own object, the Sprites/Default material, no shadows) ----------
	// One mesh of small squares (thousands of LineRenderers would cost far more), redrawn at most once a second.
	private sealed class OvMapView
	{
		private GameObject _go;

		private Mesh _mesh;

		private Material _mat;

		private readonly List<Vector3> _v = new List<Vector3>();

		private readonly List<Color> _c = new List<Color>();

		private readonly List<int> _t = new List<int>();

		private static readonly Color CGround = new Color(0.3f, 0.85f, 0.3f, 0.25f);

		private static readonly Color COre = new Color(1f, 0.6f, 0.1f, 0.85f);

		private static readonly Color COreDown = new Color(1f, 0.25f, 0.05f, 0.9f);

		private static readonly Color CRock = new Color(0.6f, 0.6f, 0.6f, 0.75f);

		private static readonly Color CThing = new Color(0.55f, 0.35f, 0.15f, 0.8f);

		private static readonly Color CBuilt = new Color(0.2f, 0.45f, 1f, 0.8f);

		private static readonly Color CFixed = new Color(0.9f, 0.05f, 0.05f, 0.85f);

		private static readonly Color CUnder = new Color(0.65f, 0.2f, 0.9f, 0.6f);

		private static readonly Color CCrew = new Color(1f, 1f, 1f, 0.95f);

		private static readonly Color CGoal = new Color(1f, 0.95f, 0.1f, 0.95f);

		private static readonly Color CWater = new Color(0.1f, 0.4f, 1f, 1f);

		private static readonly Color CRoute = new Color(0.1f, 1f, 1f, 0.95f);   // MP7

		public OvMapView()
		{
			_go = new GameObject("MHR_PitMapOverlay");
			_mat = new Material(Shader.Find("Sprites/Default"));
			_mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
			_go.AddComponent<MeshFilter>().sharedMesh = _mesh;
			MeshRenderer mr = _go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = _mat;
			mr.shadowCastingMode = ShadowCastingMode.Off;
			mr.receiveShadows = false;
		}

		public void Destroy()
		{
			if (_mesh != null)
			{
				Object.Destroy(_mesh);
			}
			if (_mat != null)
			{
				Object.Destroy(_mat);
			}
			if (_go != null)
			{
				Object.Destroy(_go);
			}
			_mesh = null;
			_mat = null;
			_go = null;
		}

		// R0.9: per square the ground, then each solid layer: its top, its underside (when there is open space under it),
		// and a side face toward each neighbour square that doesn't have solid stuff at those heights; a ground step of more
		// than 0.5 m to a neighbour is a wall face too (the trench walls)
		public void Draw(OvMap m)
		{
			if (_mesh == null)
			{
				return;
			}
			_v.Clear();
			_c.Clear();
			_t.Clear();
			float half = OvMapCell * 0.45f;
			for (int z = 0; z < m.H; z++)
			{
				for (int x = 0; x < m.W; x++)
				{
					int i = z * m.W + x;
					if (m.ReadAt[i] < 0f)
					{
						continue;
					}
					float cx = m.X0 + (x + 0.5f) * OvMapCell, cz = m.Z0 + (z + 0.5f) * OvMapCell;
					float g = m.Ground[i];
					byte f = m.Flags[i];
					int li = i * OvMapLayers, lc = m.LCount[i];
					bool touching = lc > 0 && m.LBot[li] <= g + OvMapJoin;
					// the ground: the companions' squares on it, purple where something hangs over it, else green
					Color gc = lc > 0 && !touching ? CUnder : CGround;
					if (!touching)
					{
						Quad(cx, g + 0.05f, cz, half, Wet(gc, f));
					}
					// the ground's wall faces (a step over 0.5 m down to a neighbour)
					for (int d = 0; d < 4; d++)
					{
						int nx = x + DX[d], nz = z + DZ[d];
						if (nx < 0 || nz < 0 || nx >= m.W || nz >= m.H)
						{
							continue;
						}
						int ni = nz * m.W + nx;
						if (m.ReadAt[ni] >= 0f && g - m.Ground[ni] > 0.5f)
						{
							Side(cx, cz, d, m.Ground[ni], g, CGroundWall);
						}
					}
					// the layers
					for (int j = 0; j < lc; j++)
					{
						float b = m.LBot[li + j], t = m.LTop[li + j];
						byte k = m.LKind[li + j];
						Color col = KindColor(k, j == 0 && touching);
						Quad(cx, t + 0.05f, cz, half, Wet(col, t < WaterLevel() ? MfWater : (byte)0));
						if (!(j == 0 && touching))
						{
							Quad(cx, b - 0.05f, cz, half, Color.Lerp(col, Color.black, 0.35f));
						}
						for (int d = 0; d < 4; d++)
						{
							int nx = x + DX[d], nz = z + DZ[d];
							if (nx < 0 || nz < 0 || nx >= m.W || nz >= m.H)
							{
								continue;
							}
							int ni = nz * m.W + nx;
							if (m.ReadAt[ni] < 0f)
							{
								continue;
							}
							// the heights of this layer the neighbour has nothing solid at (its ground counts as solid
							// below its ground height)
							_open.Clear();
							_open.Add(new Vector2(Mathf.Max(b, m.Ground[ni]), t));
							int nl = ni * OvMapLayers;
							for (int q = 0; q < m.LCount[ni]; q++)
							{
								Cut(m.LBot[nl + q], m.LTop[nl + q]);
							}
							foreach (Vector2 o in _open)
							{
								if (o.y - o.x > 0.05f)
								{
									Side(cx, cz, d, o.x, o.y, Color.Lerp(col, Color.black, 0.15f));
								}
							}
						}
					}
				}
			}
			// the companions (white) and their walk goals (yellow), at their own heights
			foreach (Vector3 c in m.CrewAt3)
			{
				Quad(c.x, c.y + 0.1f, c.z, half, CCrew);
			}
			foreach (Vector3 c in m.GoalAt3)
			{
				Quad(c.x, c.y + 0.1f, c.z, half, CGoal);
			}
			// MP7: each companion's map route (cyan dots every 0.25 m)
			foreach (List<Vector3> rt in m.Routes)
			{
				for (int k = 1; k < rt.Count; k++)
				{
					Vector3 a = rt[k - 1], b = rt[k];
					float len = Vector3.Distance(a, b);
					for (float t = 0f; t < len; t += 0.25f)
					{
						Vector3 q = Vector3.Lerp(a, b, t / len);
						Quad(q.x, q.y + 0.15f, q.z, half * 0.35f, CRoute);
					}
				}
			}
			_mesh.Clear();
			_mesh.SetVertices(_v);
			_mesh.SetColors(_c);
			_mesh.SetTriangles(_t, 0);
			_mesh.RecalculateBounds();
		}

		private static readonly int[] DX = { 1, -1, 0, 0 };

		private static readonly int[] DZ = { 0, 0, 1, -1 };

		private static readonly Color CGroundWall = new Color(0.45f, 0.35f, 0.2f, 0.45f);

		private readonly List<Vector2> _open = new List<Vector2>();

		private static float WaterLevel()
		{
			return ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : float.MinValue;
		}

		private static Color Wet(Color c, byte f)
		{
			return (f & MfWater) != 0 ? Color.Lerp(c, CWater, 0.4f) : c;
		}

		private static Color KindColor(byte k, bool down)
		{
			switch (k)
			{
			case MkOre: return down ? COreDown : COre;
			case MkRock: return CRock;
			case MkThing: return CThing;
			case MkBuilt: return CBuilt;
			case MkFixed: return CFixed;
			default: return CGround;
			}
		}

		// removes [b, t] from the open heights
		private void Cut(float b, float t)
		{
			for (int o = _open.Count - 1; o >= 0; o--)
			{
				Vector2 r = _open[o];
				if (t <= r.x || b >= r.y)
				{
					continue;
				}
				_open.RemoveAt(o);
				if (b > r.x)
				{
					_open.Add(new Vector2(r.x, b));
				}
				if (t < r.y)
				{
					_open.Add(new Vector2(t, r.y));
				}
			}
		}

		// a vertical face on the square's edge toward neighbour d, from height y0 to y1 (both faces drawn)
		private void Side(float cx, float cz, int d, float y0, float y1, Color col)
		{
			float h = OvMapCell * 0.5f;
			float ex = cx + DX[d] * h, ez = cz + DZ[d] * h;
			float ax = DZ[d] != 0 ? h : 0f, az = DX[d] != 0 ? h : 0f;
			int b = _v.Count;
			_v.Add(new Vector3(ex - ax, y0, ez - az));
			_v.Add(new Vector3(ex - ax, y1, ez - az));
			_v.Add(new Vector3(ex + ax, y1, ez + az));
			_v.Add(new Vector3(ex + ax, y0, ez + az));
			for (int k = 0; k < 4; k++)
			{
				_c.Add(col);
			}
			_t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
			_t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
			_t.Add(b); _t.Add(b + 2); _t.Add(b + 1);
			_t.Add(b); _t.Add(b + 3); _t.Add(b + 2);
		}

		private void Quad(float x, float y, float z, float h, Color col)
		{
			int b = _v.Count;
			_v.Add(new Vector3(x - h, y, z - h));
			_v.Add(new Vector3(x - h, y, z + h));
			_v.Add(new Vector3(x + h, y, z + h));
			_v.Add(new Vector3(x + h, y, z - h));
			for (int k = 0; k < 4; k++)
			{
				_c.Add(col);
			}
			// both faces, so it shows from below too (under a vein part)
			_t.Add(b); _t.Add(b + 1); _t.Add(b + 2);
			_t.Add(b); _t.Add(b + 2); _t.Add(b + 3);
			_t.Add(b); _t.Add(b + 2); _t.Add(b + 1);
			_t.Add(b); _t.Add(b + 3); _t.Add(b + 2);
		}
	}
}
