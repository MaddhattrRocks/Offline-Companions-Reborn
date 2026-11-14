using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.97: the Ore job on a split vein outdoors (MineRock5: copper, silver, flametal...), mined like a player (John's
// approved plan T1-T8, 2026-11-14, reviews/1.5.97-vein-trench-plan.md):
//  - The game pops a vein when a part breaks (MineRock5.CheckSupport): every part whose box (its collider's bounds, set
//    once) touches no ground, no solid thing that can't be broken, nothing breakable lower than the vein's origin and no
//    held-up neighbour part, breaks at once and drops its ore. VeinScan copies that test (OverlapBox, same layers).
//  - The trench work (outline of the rock you can see, trench round it to each spot's own base, stair out, undercut, then
//    mining held parts first) is in HarvestTrench.cs. This file keeps the parts scan, the dirt spots, the swing and the
//    dig rules the game asks for (AllowDig).
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

		public Collider Part;

		public Vector3 PartCenter;
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

	private int _vSide = -1;

	private int _vFirstSide = -1;

	private Collider _vPart;

	private readonly Dictionary<Collider, GiveUpWhy> _vPartLeft = new Dictionary<Collider, GiveUpWhy>();

	private readonly Dictionary<long, int> _vSpotTried = new Dictionary<long, int>();

	private readonly HashSet<long> _vStuck = new HashSet<long>();

	private float _vProgAt;

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
		_vSide = -1;
		_vFirstSide = -1;
		_vPart = null;
		_vPartLeft.Clear();
		_vSpotTried.Clear();
		_vStuck.Clear();
		_vProgAt = Time.time;
		_vWalkSince = -1f;
		_vSwung = false;
		_vDigPending = false;
		_vSpotsAt = -1f;
		TReset();
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

	// the dirt that holds parts: the ground inside each dirt-held part's box (a 0.5 m grid over its footprint) that is
	// above the box's bottom, skipping spots that wouldn't go lower
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
						Part = p.C,
						PartCenter = p.Center
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

	// 0 N (+z), 1 E (+x), 2 S (-z), 3 W (-x) of the whole vein (the stone toss and the ore spot go by it)
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
			LogToolOnce(ReflectionHelper.GetRightItem(_humanoid));
			TOutline();
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
			TJudge();
			_vSpotsAt = -1f;
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
		switch (_tPhase)
		{
		case TPhaseTrench:
			TrenchTick();
			break;
		case TPhaseStair:
			StairTick();
			break;
		case TPhaseUnder:
			UnderTick();
			break;
		default:
			MineTick();
			break;
		}
	}

	// mining: a part that still holds the rest (touching dirt, rock or a building), nearest first; else the free part
	// nearest to it
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

	private void VeinPartFail(GiveUpWhy why, string detail)
	{
		if (_vPart != null)
		{
			_vPartLeft[_vPart] = why;
			if (CompanionsPlugin.LogNormal) LogInfo("Leaving part \"" + _vPart.name + "\" of \"" + _target.name + "\" (" + detail + ")");
		}
		_vPart = null;
		_tItemNoProg = 0;
		_tItemProgAt = Time.time;
		_vWalkSince = -1f;
		_tCand = 0;
	}

	// nothing left it can dig or break from the trench: say why and move on
	private void VeinGiveUp()
	{
		GiveUpWhy why = GiveUpWhy.Buried;
		bool allReach = _vPartLeft.Count > 0;
		foreach (GiveUpWhy w in _vPartLeft.Values)
		{
			allReach &= w == GiveUpWhy.OutOfReach;
		}
		// a part still held by dirt it can't dig (below the water, or at the game's 8 m limit): buried too deep
		bool heldLeft = false;
		foreach (VPart p in _vParts)
		{
			if (p.Dirt)
			{
				heldLeft = true;
				break;
			}
		}
		if (_tAnyWard)
		{
			why = GiveUpWhy.Ward;
		}
		else if (_tAnyCantDig)
		{
			why = GiveUpWhy.CantDig;
		}
		else if (!heldLeft && allReach)
		{
			why = GiveUpWhy.OutOfReach;
		}
		SayGaveUp(why, $"{_vParts.Count} part(s) left, {_vDirtCount} touching dirt, {_vStuck.Count} dirt spot(s) that won't go lower, {_vPartLeft.Count} part(s) it couldn't get a swing at, its feet {base.transform.position.y:F1}");
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
	// of it, not while mining, not below the water level or the depth the swing was meant for), and when it digs its way
	// out of a hole (HarvestPit.cs)
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
		// 1.5.97: no digging in the mining phase, never into the water level, and never below the depth it meant to dig to
		if (_tPhase == TPhaseMine || hitPoint.y < TWater || hitPoint.y < _tDigMinY - 0.05f)
		{
			return false;
		}
		if (_tPhase == TPhaseStair && Flat(hitPoint, TStairPoint()) <= 1.5f)
		{
			return true;   // the stair out of the trench can reach past the vein's box
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
			// 1.5.97 W1: where the game starts the swing (Attack.GetAttackOrigin): the origin joint, or its feet when none
			string joint = string.IsNullOrEmpty(a.m_attackOriginJoint) ? "none - its feet" : a.m_attackOriginJoint;
			float startY = a.m_attackHeight;
			if (!string.IsNullOrEmpty(a.m_attackOriginJoint) && _character != null && _character.GetVisual() != null)
			{
				Transform j = Utils.FindChild(_character.GetVisual().transform, a.m_attackOriginJoint);
				if (j != null)
				{
					startY += j.position.y - base.transform.position.y;
				}
				else
				{
					joint += " (not found on its model)";
				}
			}
			Log($"Tool \"{tool.m_shared.m_name}\": swing starts at joint {joint}, {startY:F2} m above its feet; swing {a.m_attackType}, range {a.m_attackRange:F2}, height {a.m_attackHeight:F2}, angle {a.m_attackAngle:F0}, max up/down {a.m_maxYAngle:F0} deg, ray width {a.m_attackRayWidth:F2}, stamina {a.m_attackStamina:F0}, hits terrain {a.m_hitTerrain}, pickaxe rule {a.m_pickaxeSpecial}; dig: {dig}");
		}
	}
}
