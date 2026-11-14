using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.96: the Ore job on a split vein outdoors (MineRock5: copper, silver, flametal...), mined like a player (John's
// approved list, 2026-11-14):
//  - The game pops a vein when a part breaks (MineRock5.CheckSupport): every part whose box (its collider's bounds, set
//    once) touches no ground, no solid thing that can't be broken, nothing breakable lower than the vein's origin and no
//    held-up neighbour part, breaks at once and drops its ore. This copies that test (OverlapBox, same layers) to see which
//    parts the dirt still holds.
//  - Dig: the dirt inside every held part's box is dug away (the highest dirt first), working from one of 4 sides (N, E,
//    S, W of the whole vein), standing 1.5 m out from the vein (stepping in toward the dirt, up to 0.3 m from the vein,
//    when the dirt is out of reach from there). A spot the dig can't lower by 5 cm in 3 tries is left (the game's own
//    limit); with its feet below the bottom of the vein it stops digging.
//  - Progress: ground lowered 5 cm or more, health taken off a part, a part freed of dirt, or a part broken. A side
//    fails after 12 swings or 60 s without progress (whichever first), or 10 s without getting to it, or straight away
//    inside someone else's ward / a no-build area. It then works the nearest side that hasn't failed.
//  - Break: when no side has dirt left to dig, it breaks the parts that still hold the rest (nearest first), else the
//    free part nearest to it - the rest then pops. A part it can't make progress on (12 swings / 60 s) is left; when
//    every part left is like that it gives up the vein and says why.
public partial class HarvestController
{
	private struct VPart
	{
		public Collider C;

		public Vector3 Center;

		public Vector3 Half;

		public float Health;

		public bool Dirt;

		public bool Solid;

		public bool Low;

		public bool Direct => Dirt || Solid || Low;

		public float MinY => Center.y - Half.y;
	}

	private struct VSpot
	{
		public Vector3 P;

		public long Key;

		public int Side;
	}

	private const float VStandOut = 1.5f;

	private const float VDigMin = 0.05f;

	private const int VSpotTries = 3;

	private const int VNoProgSwings = 12;

	private const float VNoProgTime = 60f;

	private const float VWalkTime = 10f;

	private GameObject _vFor;

	private MineRock5 _vRock;

	private static readonly List<MHR_Vein.Area> s_vAreas = new List<MHR_Vein.Area>();

	private readonly List<VPart> _vParts = new List<VPart>();

	private readonly List<VSpot> _vSpots = new List<VSpot>();

	private Bounds _vUnion;

	private float _vLowest;

	private float _vHealth;

	private int _vDirtCount;

	private readonly bool[] _vSideFailed = new bool[4];

	private readonly GiveUpWhy[] _vSideWhy = new GiveUpWhy[4];

	private int _vSide = -1;

	private int _vFirstSide = -1;

	private bool _vBreak;

	private Collider _vPart;

	private readonly Dictionary<Collider, GiveUpWhy> _vPartLeft = new Dictionary<Collider, GiveUpWhy>();

	private readonly Dictionary<long, int> _vSpotTried = new Dictionary<long, int>();

	private readonly HashSet<long> _vStuck = new HashSet<long>();

	private float _vProgAt;

	private int _vNoProg;

	private float _vWalkSince = -1f;

	private Vector3 _vWalkFor;

	private bool _vSwung;

	private float _vSwungAt;

	private bool _vSwingDig;

	private long _vSwingKey;

	private Vector3 _vSwingAim;

	private float _vLastHealth;

	private int _vLastAlive;

	private int _vLastDirt;

	private bool _vDigPending;

	private Vector3 _vDigAt;

	private float _vDigGround;

	private float _vDigAimDeg;

	private int _vDigLowered;

	private static readonly Collider[] s_vBuf = new Collider[64];

	private static int s_vMask = -1;

	private static int s_vTerrain = -1;

	private static readonly HashSet<string> s_toolLogged = new HashSet<string>();

	private float _vUnionAt = -100f;

	// the vein it's working (or just finished): its box, the side it started on and the side it's on now (-1 = none)
	internal bool VeinBox(out Bounds box, out int firstSide, out int side)
	{
		box = _vUnion;
		firstSide = _vFirstSide;
		side = _vSide;
		return Time.time - _vUnionAt < 30f;
	}

	internal static Vector3 SideDirOf(int s)
	{
		return SideDir(s < 0 ? 0 : s);
	}

	// vein work is on: the swing's hit on the ground may dig (SpawnOnHitTerrain asks AllowDig)
	internal bool VeinWorking => _state == HarvestState.Attacking && _vFor != null && _vFor == _target && DigMode();

	private void VeinReset(GameObject target)
	{
		_vFor = target;
		_vRock = target != null ? target.GetComponent<MineRock5>() : null;
		for (int i = 0; i < 4; i++)
		{
			_vSideFailed[i] = false;
		}
		_vSide = -1;
		_vFirstSide = -1;
		_vBreak = false;
		_vPart = null;
		_vPartLeft.Clear();
		_vSpotTried.Clear();
		_vStuck.Clear();
		_vProgAt = Time.time;
		_vNoProg = 0;
		_vWalkSince = -1f;
		_vSwung = false;
		_vDigPending = false;
		_vDigLowered = 0;
		_vSpotsAt = -1f;
		_vPickAt = -1f;
		VeinScan();
		_vLastHealth = _vHealth;
		_vLastAlive = _vParts.Count;
		_vLastDirt = _vDirtCount;
	}

	// ---------- what the dirt still holds (the game's support test) ----------
	private void VeinScan(bool force = true)
	{
		if (!force && Time.time < _vScanAt)
		{
			return;
		}
		_vScanAt = Time.time + 0.25f;
		_vParts.Clear();
		_vHealth = 0f;
		_vDirtCount = 0;
		if (_vRock == null || !MHR_Vein.Areas(_vRock, s_vAreas, out bool haveBounds))
		{
			return;
		}
		if (s_vMask < 0)
		{
			s_vMask = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small", "terrain");
			s_vTerrain = LayerMask.NameToLayer("terrain");
		}
		Vector3 origin = _vRock.transform.position;
		bool first = true;
		_vLowest = float.MaxValue;
		foreach (MHR_Vein.Area ha in s_vAreas)
		{
			if (ha.Health <= 0f || ha.Collider == null || !ha.Collider.gameObject.activeSelf)
			{
				continue;
			}
			VPart p = default(VPart);
			p.C = ha.Collider;
			p.Health = ha.Health;
			if (haveBounds)
			{
				p.Center = origin + ha.BoundPos;
				p.Half = ha.BoundSize;
			}
			else
			{
				p.Center = ha.Collider.bounds.center;
				p.Half = ha.Collider.bounds.size * 0.5f;
			}
			int n = Physics.OverlapBoxNonAlloc(p.Center, p.Half, s_vBuf, Quaternion.identity, s_vMask);
			for (int i = 0; i < n; i++)
			{
				Collider c = s_vBuf[i];
				if (c == null || c == ha.Collider || c.attachedRigidbody != null || c.isTrigger)
				{
					continue;
				}
				if (c.gameObject.layer == s_vTerrain)
				{
					p.Dirt = true;
					continue;
				}
				IDestructible d = c.gameObject.GetComponentInParent<IDestructible>();
				if (d != null)
				{
					if ((object)d == _vRock)
					{
						continue;   // a neighbour part: held only if it is
					}
					if (c.transform.position.y < origin.y)
					{
						p.Low = true;
					}
					continue;
				}
				p.Solid = true;
			}
			_vParts.Add(p);
			_vHealth += p.Health;
			if (p.Dirt)
			{
				_vDirtCount++;
			}
			_vLowest = Mathf.Min(_vLowest, p.MinY);
			Bounds b = new Bounds(p.Center, p.Half * 2f);
			if (first)
			{
				_vUnion = b;
				first = false;
				_vUnionAt = Time.time;
			}
			else
			{
				_vUnion.Encapsulate(b);
			}
		}
	}

	// the dirt to dig: the ground inside each dirt-held part's box (a 0.5 m grid over its footprint) that is above the
	// box's bottom, skipping spots that wouldn't go lower
	private float _vSpotsAt = -1f;

	private float _vScanAt = -1f;

	private void VeinSpots(bool force = false)
	{
		// at most twice a second (it samples the ground over every part) unless a swing just changed things
		if (!force && Time.time < _vSpotsAt)
		{
			return;
		}
		_vSpotsAt = Time.time + 0.5f;
		_vSpots.Clear();
		if (base.transform.position.y < _vLowest - 0.1f)
		{
			return;   // its feet are below the vein: no more digging
		}
		HashSet<long> seen = new HashSet<long>();
		foreach (VPart p in _vParts)
		{
			if (!p.Dirt)
			{
				continue;
			}
			int nx = Mathf.Clamp(Mathf.CeilToInt(p.Half.x * 2f / 0.5f) + 1, 2, 12);
			int nz = Mathf.Clamp(Mathf.CeilToInt(p.Half.z * 2f / 0.5f) + 1, 2, 12);
			for (int ix = 0; ix < nx; ix++)
			{
				for (int iz = 0; iz < nz; iz++)
				{
					float x = p.Center.x - p.Half.x + p.Half.x * 2f * ix / (nx - 1);
					float z = p.Center.z - p.Half.z + p.Half.z * 2f * iz / (nz - 1);
					long key = SpotKey(x, z);
					if (!seen.Add(key) || _vStuck.Contains(key))
					{
						continue;
					}
					Vector3 q = new Vector3(x, p.Center.y, z);
					float h = GroundAt(q);
					if (h <= p.MinY - VDigMin)
					{
						continue;
					}
					q.y = h;
					_vSpots.Add(new VSpot
					{
						P = q,
						Key = key,
						Side = SideOf(q)
					});
				}
			}
		}
	}

	private static long SpotKey(float x, float z)
	{
		long a = Mathf.RoundToInt(x * 2f);
		long b = Mathf.RoundToInt(z * 2f);
		return (a << 32) ^ (b & 0xFFFFFFFFL);
	}

	// 0 N (+z), 1 E (+x), 2 S (-z), 3 W (-x) of the whole vein
	private int SideOf(Vector3 p)
	{
		Vector3 d = p - _vUnion.center;
		float ex = Mathf.Max(0.1f, _vUnion.extents.x);
		float ez = Mathf.Max(0.1f, _vUnion.extents.z);
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

	private static readonly string[] s_sideName = new string[4] { "north", "east", "south", "west" };

	// where it stands on side s to dig at q: 1.5 m out from the vein, level with q along the side; if q is out of reach
	// from there, stepped in toward it (up to 0.3 m from the vein)
	private bool StandFor(int s, Vector3 q, float reach, out Vector3 stand)
	{
		Vector3 n = SideDir(s);
		Vector3 edge = _vUnion.center + Vector3.Scale(n, _vUnion.extents);
		Vector3 baseP;
		if (s == 0 || s == 2)
		{
			baseP = new Vector3(Mathf.Clamp(q.x, _vUnion.min.x, _vUnion.max.x), 0f, edge.z);
		}
		else
		{
			baseP = new Vector3(edge.x, 0f, Mathf.Clamp(q.z, _vUnion.min.z, _vUnion.max.z));
		}
		float eyeH = Mathf.Max(1.2f, Eye().y - base.transform.position.y);
		for (float out1 = VStandOut; out1 >= 0.3f - 0.001f; out1 -= 0.4f)
		{
			Vector3 p = baseP + n * out1;
			p.y = GroundAt(p);
			if (Vector3.Distance(p + Vector3.up * eyeH, q) <= reach)
			{
				stand = p;
				return true;
			}
		}
		stand = baseP + n * VStandOut;
		stand.y = GroundAt(stand);
		return false;
	}

	private float Reach()
	{
		return GetAttackRange();
	}

	// ---------- each frame while working the vein ----------
	private void VeinTick()
	{
		if (_vFor != _target)
		{
			VeinReset(_target);
			if (_vRock == null)
			{
				ResetToIdle();
				return;
			}
			ItemDrop.ItemData best = FindBestTool(3);
			if (best != null && best.m_shared.m_toolTier < _vRock.m_minToolTier)
			{
				SayGaveUp(GiveUpWhy.TooHard, $"best pickaxe tier {best.m_shared.m_toolTier}, the vein needs {_vRock.m_minToolTier}");
				VeinLeave();
				return;
			}
			VeinLogParts();
		}
		if (_humanoid == null || _character == null || _ai == null)
		{
			return;
		}
		bool busy = _humanoid.InAttack() || _humanoid.InDodge() || _character.IsStaggering();
		// a swing has finished: what did it do?
		if (_vSwung && !busy && Time.time - _vSwungAt > 0.35f)
		{
			_vSwung = false;
			VeinJudge();
			_vSpotsAt = -1f;
			_vPickAt = -1f;
			if (_vFor == null || _target == null)
			{
				return;
			}
		}
		if (busy || _vSwung)
		{
			VeinFace(_vSwingAim);
			return;
		}
		VeinScan(force: false);
		if (_vParts.Count == 0)
		{
			return;   // all broken: the next frame sees the target gone and collects the ore
		}
		// ---- dig phase ----
		if (!_vBreak)
		{
			VeinSpots();
			VSpot spot;
			Vector3 stand;
			if (VeinPickSpot(out spot, out stand))
			{
				if (!VeinGetTo(stand))
				{
					return;
				}
				if (!PrivateArea.CheckAccess(spot.P, 0f, flash: false))
				{
					VeinSideFail(GiveUpWhy.Ward, "someone else's ward covers " + MHR_Mover.Fmt(spot.P));
					return;
				}
				if (Location.IsInsideNoBuildLocation(spot.P))
				{
					VeinSideFail(GiveUpWhy.CantDig, "no-build area at " + MHR_Mover.Fmt(spot.P));
					return;
				}
				VeinSwing(spot.P, dig: true, spot.Key);
				return;
			}
			_vBreak = true;
			_vPart = null;
			if (CompanionsPlugin.LogNormal) LogInfo($"Dug out \"{_target.name}\" as far as it can ({_vDirtCount} of {_vParts.Count} parts still touch dirt) - breaking it out");
		}
		// ---- break phase ----
		VeinSpots();
		if (_vSpots.Count > 0 && VeinPickSpot(out VSpot again, out _))
		{
			_ = again;
			_vBreak = false;   // a break changed the vein: there's dirt it can get at again
			return;
		}
		if (_vPart == null || !_vPart.gameObject.activeSelf || _vPartLeft.ContainsKey(_vPart))
		{
			_vPart = VeinPickPart();
			_vProgAt = Time.time;
			_vNoProg = 0;
			_vWalkSince = -1f;
			if (_vPart == null)
			{
				VeinGiveUp();
				return;
			}
		}
		Vector3 eye = Eye();
		Vector3 aim = SafeClosestPoint(_vPart, eye);
		if (Vector3.Distance(eye, aim) > Reach() * 0.9f)
		{
			Vector3 to = aim - base.transform.position;
			to.y = 0f;
			Vector3 standP = aim - (to.sqrMagnitude > 0.01f ? to.normalized : Vector3.forward) * (Reach() * 0.6f);
			standP.y = GroundAt(standP);
			if (!VeinGetTo(standP, partWalk: true))
			{
				return;
			}
		}
		VeinSwing(aim, dig: false, 0L);
	}

	// the dirt to dig next: on the side it's working, the highest spot it can reach (not behind the rock if it has a
	// choice); else the nearest side that hasn't failed and has dirt
	private float _vPickAt = -1f;

	private bool _vPickOk;

	private VSpot _vPickSpot;

	private Vector3 _vPickStand;

	private int _vPickSide = -1;

	private bool VeinPickSpot(out VSpot spot, out Vector3 stand)
	{
		// the choice holds for half a second (it checks reach from each spot's standing place) unless a swing landed
		if (Time.time < _vPickAt && _vPickSide == _vSide && (_vSide < 0 || !_vSideFailed[_vSide]))
		{
			spot = _vPickSpot;
			stand = _vPickStand;
			return _vPickOk;
		}
		_vPickOk = VeinPickSpotNow(out spot, out stand);
		_vPickSpot = spot;
		_vPickStand = stand;
		_vPickSide = _vSide;
		_vPickAt = Time.time + 0.5f;
		return _vPickOk;
	}

	private bool VeinPickSpotNow(out VSpot spot, out Vector3 stand)
	{
		spot = default(VSpot);
		stand = Vector3.zero;
		float reach = Reach();
		for (int round = 0; round < 2; round++)
		{
			if (_vSide < 0 || _vSideFailed[_vSide] || !SideHasWork(_vSide, reach))
			{
				int next = NearestSide(reach);
				if (next < 0)
				{
					return false;
				}
				if (_vSide != next && CompanionsPlugin.LogNormal)
				{
					LogInfo("Digging out \"" + _target.name + "\" from the " + s_sideName[next] + " side");
				}
				_vSide = next;
				if (_vFirstSide < 0)
				{
					_vFirstSide = next;
				}
				_vProgAt = Time.time;
				_vNoProg = 0;
				_vWalkSince = -1f;
			}
			float bestScore = float.MinValue;
			bool found = false;
			Vector3 eye = Eye();
			foreach (VSpot s in _vSpots)
			{
				if (s.Side != _vSide)
				{
					continue;
				}
				if (!StandFor(_vSide, s.P, reach, out Vector3 st))
				{
					continue;
				}
				float score = s.P.y * 10f - Vector3.Distance(base.transform.position, st);
				if (Blocked(st + Vector3.up * Mathf.Max(1.2f, eye.y - base.transform.position.y), s.P))
				{
					score -= 1000f;
				}
				if (score > bestScore)
				{
					bestScore = score;
					spot = s;
					stand = st;
					found = true;
				}
			}
			if (found)
			{
				return true;
			}
		}
		return false;
	}

	private bool SideHasWork(int side, float reach)
	{
		foreach (VSpot s in _vSpots)
		{
			if (s.Side == side && StandFor(side, s.P, reach, out _))
			{
				return true;
			}
		}
		return false;
	}

	private int NearestSide(float reach)
	{
		int best = -1;
		float bd = float.MaxValue;
		for (int side = 0; side < 4; side++)
		{
			if (_vSideFailed[side] || !SideHasWork(side, reach))
			{
				continue;
			}
			Vector3 p = _vUnion.center + Vector3.Scale(SideDir(side), _vUnion.extents + Vector3.one * VStandOut);
			p.y = base.transform.position.y;
			float d = Vector3.Distance(base.transform.position, p);
			if (d < bd)
			{
				bd = d;
				best = side;
			}
		}
		return best;
	}

	// the rock is between it and the dirt there (the swing would hit the rock first)
	private bool Blocked(Vector3 from, Vector3 to)
	{
		Vector3 d = to - from;
		float len = d.magnitude;
		if (len < 0.2f || _target == null)
		{
			return false;
		}
		var hits = MHR_Phys.Ray(ref MHR_Phys.HarvestController471, from, d / len, len - 0.15f, -1, QueryTriggerInteraction.Ignore);
		for (int i = 0; i < hits.Length; i++)
		{
			if (hits[i].collider != null && hits[i].collider.transform.IsChildOf(_target.transform))
			{
				return true;
			}
		}
		return false;
	}

	// break phase: a part that still holds the rest (touching dirt, rock or a building), nearest first; else the free
	// part nearest to it
	private Collider VeinPickPart()
	{
		Collider best = null;
		float bd = float.MaxValue;
		bool bestHolds = false;
		foreach (VPart p in _vParts)
		{
			if (_vPartLeft.ContainsKey(p.C))
			{
				continue;
			}
			float d = Vector3.Distance(base.transform.position, p.Center);
			bool holds = p.Direct;
			if ((holds && !bestHolds) || (holds == bestHolds && d < bd))
			{
				best = p.C;
				bd = d;
				bestHolds = holds;
			}
		}
		if (best != null && CompanionsPlugin.LogDetailed)
		{
			Log($"Breaking out part \"{best.name}\" ({(bestHolds ? "it holds the rest" : "free - the rest pops with it")}, {bd:F1}m away)");
		}
		return best;
	}

	// walk to a spot (no edge check: it may have to step down into the hole it's digging). False while still walking.
	private bool VeinGetTo(Vector3 stand, bool partWalk = false)
	{
		Vector3 flat = stand - base.transform.position;
		flat.y = 0f;
		if (flat.magnitude <= 0.6f)
		{
			_ai.StopMoving();
			_vWalkSince = -1f;
			return true;
		}
		if (_vWalkSince < 0f || Vector3.Distance(_vWalkFor, stand) > 3f)
		{
			if (_vWalkSince < 0f)
			{
				_vWalkSince = Time.time;
			}
			_vWalkFor = stand;
		}
		if (Time.time - _vWalkSince > VWalkTime && flat.magnitude > 1.5f)
		{
			_vWalkSince = -1f;
			if (partWalk)
			{
				VeinPartFail(GiveUpWhy.OutOfReach, $"couldn't get to it in {VWalkTime:F0}s");
			}
			else
			{
				VeinSideFail(GiveUpWhy.OutOfReach, $"couldn't get to {MHR_Mover.Fmt(stand)} in {VWalkTime:F0}s");
			}
			return false;
		}
		bool ok = MHR_Mover.Go(_ai, Time.deltaTime, stand, 0.4f, false, "Vein");
		if ((!ok || (_character.GetVelocity().magnitude < 0.3f)) && flat.magnitude < 3f && !MHR_Mover.Climbing(_ai))
		{
			_ai.MoveTowards(flat.normalized, run: false);
		}
		return false;
	}

	private void VeinFace(Vector3 aim)
	{
		Vector3 flat = aim - base.transform.position;
		flat.y = 0f;
		_ai.LookAtPoint(aim);
		if (flat.sqrMagnitude > 0.001f)
		{
			MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(flat.normalized));
		}
		Vector3 look = aim - Eye();
		if (look.sqrMagnitude > 0.01f)
		{
			_character.SetLookDir(look.normalized);
		}
	}

	// one swing at a spot: the tool, a breather when low on stamina, then the game's own attack
	private void VeinSwing(Vector3 aim, bool dig, long key)
	{
		// the progress the swing is measured against
		float health = _vHealth;
		int alive = _vParts.Count, dirt = _vDirtCount;
		bool leave;
		if (!ToolSwing(aim, out leave))
		{
			if (leave)
			{
				VeinLeave();
			}
			return;
		}
		_vLastHealth = health;
		_vLastAlive = alive;
		_vLastDirt = dirt;
		_vDigPending = false;
		_vDigAimDeg = Mathf.Asin(Mathf.Clamp((aim - Eye()).normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
		_vSwung = true;
		_vSwungAt = Time.time;
		_vSwingDig = dig;
		_vSwingKey = key;
		_vSwingAim = aim;
	}

	// face the spot and swing the pickaxe like a player: a working pickaxe in hand (a worn one swapped), a breather when
	// low on stamina (the swing's own cost, by Pickaxes skill), then the game's attack. True = a swing started; leave =
	// no working pickaxe left (it has said so).
	private bool ToolSwing(Vector3 aim, out bool leave)
	{
		leave = false;
		VeinFace(aim);
		_attackTimer -= Time.deltaTime;
		if (_attackTimer > 0f)
		{
			return false;
		}
		ItemDrop.ItemData tool = ReflectionHelper.GetRightItem(_humanoid);
		if (tool == null || tool.GetDamage().m_pickaxe <= 0f || Worn(tool))
		{
			ItemDrop.ItemData best = FindBestTool(3);
			if (best == null)
			{
				SayGaveUp(GiveUpWhy.NoTool, "in hand: " + (tool?.m_shared?.m_name ?? "nothing") + (Worn(tool) ? " (worn out)" : ""));
				leave = true;
				return false;
			}
			EquipTool(best);
			_attackTimer = 0.25f;
			return false;
		}
		LogToolOnce(tool);
		float cost = SwingStamina(tool, secondary: false);
		if (_stamina != null && cost > 0f)
		{
			float max = Mathf.Max(1f, _stamina.MaxStamina);
			if (_catchingBreath)
			{
				if (_stamina.Stamina < (ModConfig.GatherResumeStamina?.Value ?? 0.6f) * max)
				{
					_attackTimer = 0.5f;
					return false;
				}
				_catchingBreath = false;
				if (CompanionsPlugin.LogDetailed) Log($"Got its breath back ({_stamina.Stamina:F0}/{max:F0}) - back to work");
			}
			if (_stamina.Stamina < cost + (ModConfig.GatherRestStamina?.Value ?? 0.15f) * max)
			{
				_catchingBreath = true;
				_attackTimer = 0.5f;
				if (CompanionsPlugin.LogDetailed) Log($"Catching its breath ({_stamina.Stamina:F0}/{max:F0} stamina) before swinging again");
				return false;
			}
		}
		bool started = _humanoid.StartAttack(null, secondaryAttack: false);
		_totalAttempts++;
		_attackTimer = started ? 0.4f : 0.5f;
		if (started)
		{
			_swingCount++;
		}
		return started;
	}

	// after a swing: did it take dirt away, take health off a part, free or break a part?
	private void VeinJudge()
	{
		VeinScan();
		bool prog = false;
		string what = "";
		if (_vParts.Count < _vLastAlive)
		{
			prog = true;
			what = (_vLastAlive - _vParts.Count) + " part(s) broke";
			_vPart = null;
		}
		else if (_vHealth < _vLastHealth - 0.01f)
		{
			prog = true;
			what = "hit the rock";
		}
		if (_vDirtCount < _vLastDirt)
		{
			prog = true;
			what += (what.Length > 0 ? ", " : "") + (_vLastDirt - _vDirtCount) + " part(s) freed of dirt";
		}
		float lowered = 0f;
		if (_vDigPending)
		{
			_vDigPending = false;
			float now = GroundAt(_vDigAt);
			lowered = _vDigGround - now;
			if (CompanionsPlugin.LogDetailed) Log($"Dig at {MHR_Mover.Fmt(_vDigAt)}: ground {_vDigGround:F2} -> {now:F2} ({lowered * 100f:F0} cm), aimed {_vDigAimDeg:F0} deg");
			if (lowered >= VDigMin)
			{
				prog = true;
				_vDigLowered++;
				what += (what.Length > 0 ? ", " : "") + "dug " + (lowered * 100f).ToString("F0") + " cm";
			}
		}
		if (_vSwingDig && lowered < VDigMin)
		{
			_vSpotTried.TryGetValue(_vSwingKey, out int tries);
			tries++;
			_vSpotTried[_vSwingKey] = tries;
			if (tries >= VSpotTries)
			{
				_vStuck.Add(_vSwingKey);
				if (CompanionsPlugin.LogDetailed) Log($"The dirt at {MHR_Mover.Fmt(_vSwingAim)} won't go lower ({VSpotTries} tries) - leaving that spot");
			}
		}
		if (prog)
		{
			_vNoProg = 0;
			_vProgAt = Time.time;
			if (CompanionsPlugin.LogDetailed) Log("Vein progress: " + what);
			return;
		}
		_vNoProg++;
		if (_vNoProg >= VNoProgSwings || Time.time - _vProgAt >= VNoProgTime)
		{
			string detail = $"{_vNoProg} swings / {Time.time - _vProgAt:F0}s without progress";
			if (_vBreak)
			{
				VeinPartFail(_vSwingDig || _vDigLowered > 0 ? GiveUpWhy.Buried : GiveUpWhy.OutOfReach, detail);
			}
			else
			{
				VeinSideFail(GiveUpWhy.Buried, detail);
			}
		}
	}

	private void VeinSideFail(GiveUpWhy why, string detail)
	{
		if (_vSide < 0)
		{
			return;
		}
		_vSideFailed[_vSide] = true;
		_vSideWhy[_vSide] = why;
		if (CompanionsPlugin.LogNormal) LogInfo("Can't get further on the " + s_sideName[_vSide] + " side of \"" + _target.name + "\" (" + detail + ") - trying another side");
		_vSide = -1;
		_vNoProg = 0;
		_vProgAt = Time.time;
		_vWalkSince = -1f;
	}

	private void VeinPartFail(GiveUpWhy why, string detail)
	{
		if (_vPart != null)
		{
			_vPartLeft[_vPart] = why;
			if (CompanionsPlugin.LogNormal) LogInfo("Leaving part \"" + _vPart.name + "\" of \"" + _target.name + "\" (" + detail + ")");
		}
		_vPart = null;
		_vNoProg = 0;
		_vProgAt = Time.time;
		_vWalkSince = -1f;
	}

	// every part left is one it can't get at: say why and move on
	private void VeinGiveUp()
	{
		GiveUpWhy why = GiveUpWhy.Buried;
		bool anyWard = false, allReach = true, anyCantDig = false;
		for (int i = 0; i < 4; i++)
		{
			if (!_vSideFailed[i])
			{
				continue;
			}
			anyWard |= _vSideWhy[i] == GiveUpWhy.Ward;
			anyCantDig |= _vSideWhy[i] == GiveUpWhy.CantDig;
			allReach &= _vSideWhy[i] == GiveUpWhy.OutOfReach;
		}
		foreach (GiveUpWhy w in _vPartLeft.Values)
		{
			allReach &= w == GiveUpWhy.OutOfReach;
		}
		if (anyWard)
		{
			why = GiveUpWhy.Ward;
		}
		else if (anyCantDig)
		{
			why = GiveUpWhy.CantDig;
		}
		else if (allReach && _vPartLeft.Count > 0)
		{
			why = GiveUpWhy.OutOfReach;
		}
		SayGaveUp(why, $"{_vParts.Count} part(s) left, {_vDirtCount} touching dirt, {_vStuck.Count} dirt spot(s) that won't go lower, lowest part bottom {_vLowest:F1}, its feet {base.transform.position.y:F1}");
		VeinLeave();
	}

	private void VeinLeave()
	{
		if (_target != null)
		{
			_blacklist[_target.GetInstanceID()] = Time.time;
		}
		_vFor = null;
		_vRock = null;
		MHR_GatherRules.AfterVein(this);
		ResetToIdle();
	}

	// SpawnOnHitTerrain asks: may this swing's hit on the ground dig? Over and around the vein it's working (within 2.5 m
	// of it), and when it digs its way out of a hole (HarvestPit.cs)
	internal bool AllowDig(Vector3 hitPoint)
	{
		if (PitDigging && Vector3.Distance(hitPoint, base.transform.position) < 3f)
		{
			return true;
		}
		if (!VeinWorking || _vParts.Count == 0)
		{
			return false;
		}
		Bounds b = _vUnion;
		float dx = Mathf.Max(0f, Mathf.Max(b.min.x - hitPoint.x, hitPoint.x - b.max.x));
		float dz = Mathf.Max(0f, Mathf.Max(b.min.z - hitPoint.z, hitPoint.z - b.max.z));
		return Mathf.Sqrt(dx * dx + dz * dz) <= 2.5f;
	}

	internal void NotifyDig(Vector3 hitPoint)
	{
		_lastHitAt = Time.time;
		_noDamageSwings = 0;
		if (PitDigging)
		{
			PitNotifyDig(hitPoint);
			return;
		}
		_vDigPending = true;
		_vDigAt = hitPoint;
		_vDigGround = GroundAt(hitPoint);
	}

	// ---------- Detailed log: the vein's parts, the tool's swing and dig settings ----------
	private void VeinLogParts()
	{
		if (!CompanionsPlugin.LogDetailed || _vRock == null)
		{
			return;
		}
		Log($"Vein \"{_target.name}\": {_vParts.Count} parts, {_vDirtCount} touching dirt, needs tool tier {_vRock.m_minToolTier}, part health {_vRock.m_health:F0}, box {MHR_Mover.Fmt(_vUnion.min)} to {MHR_Mover.Fmt(_vUnion.max)}");
		int i = 0;
		foreach (VPart p in _vParts)
		{
			Log($"  part {i++} \"{p.C.name}\": centre {MHR_Mover.Fmt(p.Center)}, size {p.Half.x * 2f:F1} x {p.Half.y * 2f:F1} x {p.Half.z * 2f:F1}, bottom {p.MinY:F1}, ground {GroundAt(p.Center):F1}, held by {(p.Dirt ? "dirt " : "")}{(p.Solid ? "solid " : "")}{(p.Low ? "low-rock " : "")}{(p.Direct ? "" : "neighbours only")}, health {p.Health:F0}");
		}
	}

	private void LogToolOnce(ItemDrop.ItemData tool)
	{
		if (!CompanionsPlugin.LogDetailed || tool?.m_shared == null || !s_toolLogged.Add(tool.m_shared.m_name))
		{
			return;
		}
		Attack a = tool.m_shared.m_attack;
		string dig = "no dig";
		GameObject op = tool.m_shared.m_spawnOnHitTerrain;
		if (op != null)
		{
			TerrainOp t = op.GetComponent<TerrainOp>();
			if (t != null && t.m_settings != null)
			{
				TerrainOp.Settings st = t.m_settings;
				dig = $"\"{op.name}\": level={st.m_level} r{st.m_levelRadius:F1} offset {st.m_levelOffset:F2}, raise={st.m_raise} r{st.m_raiseRadius:F1} power {st.m_raisePower:F2} delta {st.m_raiseDelta:F2}, smooth={st.m_smooth} r{st.m_smoothRadius:F1} power {st.m_smoothPower:F1}";
			}
			else
			{
				dig = "\"" + op.name + "\" (no TerrainOp on it)";
			}
		}
		if (a != null)
		{
			Log($"Tool \"{tool.m_shared.m_name}\": swing {a.m_attackType}, range {a.m_attackRange:F2}, height {a.m_attackHeight:F2}, angle {a.m_attackAngle:F0}, max up/down {a.m_maxYAngle:F0} deg, ray width {a.m_attackRayWidth:F2}, stamina {a.m_attackStamina:F0}, hits terrain {a.m_hitTerrain}, pickaxe rule {a.m_pickaxeSpecial}; dig: {dig}");
		}
	}
}
