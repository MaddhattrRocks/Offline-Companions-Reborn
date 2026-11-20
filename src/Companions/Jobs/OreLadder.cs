using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 Z4 (John, test 10: "If they cant reach the ore anymore they should build a workbench and then a ladder"; his
// answers 2026-11-18 22:44Z / 22:47Z): a part too high to hit from the floor gets its round from a wood_stepladder on the
// pit floor - one already there that does, else one it builds (a workbench first when none is in range), with its own
// hammer and wood, chopping trees within 20 m of the vein when it is short of wood. S1: the mover's own ladder climb takes
// it up (walk goal = the ladder's top) and it stops at the top; S2: it swings where the game's swing rays meet the rock. Game facts: Player.TryPlacePiece / UpdatePlacementGhost / PlacePiece, Character.OnAutoJump, MineRock5.GetSupport
// (a piece lower than the vein inside a part's box holds the vein up - so the ladder stays clear of the other parts' boxes).
public partial class HarvestController
{
	private const string OvLnPiece = "wood_stepladder";

	private const float OvLnWoodRange = 20f;   // trees for its wood: within 20 m (flat) of the vein's centre

	private const int OvLnTreesMax = 3;         // at most 3 trees for one ladder (one it gives up on counts)

	private const float OvLnGrid = 0.5f;        // the spots looked at: every 0.5 m under the part's box ...

	private const float OvLnOut = 2f;           // ... and up to 2 m out from it

	private const float OvLnBuildDist = 4f;     // it builds from within 4 m (the game's place distance is 5 m)

	private const float OvLnBuildTime = 30f;    // a piece not placed within 30 s of being in place: the round ends

	private const float OvLnFallTime = 3f;      // 3 s off the ladder below its stop height: fell (one more climb a round)

	private bool _clLadder;

	private int _lnStage;                        // 0 plan, 2 build, 3 climb, 4 swing (the wood is a hand-off to the Wood flow)

	private Vector3 _lnSpot;

	private Quaternion _lnRot;

	private bool _lnBench;

	private Vector3 _lnBenchSpot;

	private Quaternion _lnBenchRot;

	private GameObject _lnLadder;

	private float _lnStopY;

	private Vector3 _lnTop;                      // S1: the ladder's top (the mover's walk goal)

	private bool _lnPushing;

	private bool _lnPushingOrUp => _lnPushing || _lnStage == 4;   // AA6: on the ladder (climbing it, or swinging from it)

	private readonly HashSet<int> _lnSkip = new HashSet<int>();   // AA6: ladders it waited 20 s for this round

	private float _lnWaitFrom = -1f;

	private float _lnStageAt;

	private float _lnLowFor;

	private float _lnNextAct;

	private bool _lnReclimbed;

	private bool _lnWood;                        // the Ore job picks up wood while its ladder is short of it

	private readonly HashSet<Vector3Int> _lnBad = new HashSet<Vector3Int>();   // spots a piece couldn't be placed on

	private static readonly Collider[] s_lnBuf = new Collider[256];

	private static int s_lnSolid = -1;

	private static int s_lnGround = -1;

	internal bool OvWantsWood => _lnWood;

	// Z3: the pickaxe's reach above its feet - the top of its sweep (Attack.DoMeleeAttack: the attack height plus the range
	// at half the sweep angle; 1.0 + 1.8 x sin 60 = 2.56 m for the blackmetal pickaxe). No pickaxe: no limit.
	private float OvPickReach()
	{
		ItemDrop.ItemData t = FindBestTool(2);
		Attack a = t?.m_shared?.m_attack;
		if (a == null)
		{
			return float.MaxValue;
		}
		return a.m_attackHeight + a.m_attackRange * Mathf.Sin(Mathf.Clamp(a.m_attackAngle * 0.5f, 0f, 90f) * Mathf.Deg2Rad);
	}

	private void OvLnBegin(bool ladder)
	{
		_clLadder = ladder;
		_lnStage = 0;
		_lnPushing = false;
		_lnReclimbed = false;
		_lnLadder = null;
		_lnBench = false;
		_lnWood = false;
		_lnSkip.Clear();
		_lnWaitFrom = -1f;
	}

	private void OvLnOff()
	{
		if (_clLadder && _lnPushing)
		{
			_ai?.StopMoving();
		}
		_clLadder = false;
		_lnStage = 0;
		_lnPushing = false;
		_lnWood = false;
	}

	// one frame of a ladder round. True = this frame is handled; false = the part swing (OvClRound's dig block) runs.
	private bool OvLnTick(OvClear c, OvPart part)
	{
		switch (_lnStage)
		{
		case 0:
			OvLnPlan(c, part);
			return true;
		case 2:
			OvLnBuild(c, part);
			return true;
		case 3:
			OvLnClimb(c, part);
			return true;
		default:
			if (!(_dgActive && _dgClear))
			{
				OvClFail("its swings from the ladder were cut short");
				return true;
			}
			// (c) off the ladder below its stop height for 3 s: one more climb in this round, then the round ends
			float y = base.transform.position.y;
			_lnLowFor = y < _lnStopY - 1f ? _lnLowFor + Time.deltaTime : 0f;
			if (_lnLowFor >= OvLnFallTime)
			{
				if (_lnReclimbed)
				{
					OvClFail("it came off the ladder twice");
					return true;
				}
				_lnReclimbed = true;
				OvDigOff();
				_dgClear = false;
				if (CompanionsPlugin.LogNormal) LogInfo($"Came off the ladder (feet {y:F1}, it stops at {_lnStopY:F1}) - climbing it again");
				OvLnClimbStart(part);
				return true;
			}
			return false;
		}
	}

	// GG3a: no ladder for it now (no hammer, no spot, short of materials) - not held against the part: it waits 1 min and
	// the stuck rule doesn't count it
	private void OvLnGiveUp(OvPart part, string why)
	{
		OvClFail(why, wait: true);
	}

	private void OvLnSay(string text)
	{
		_talk?.SayProblem("vein:ladder", text);
	}

	// ---- plan: a ladder that does, else where to build one, the workbench, the hammer and the materials ----

	private void OvLnPlan(OvClear c, OvPart part)
	{
		GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(OvLnPiece) : null;
		Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
		if (piece == null)
		{
			OvLnGiveUp(part, "the game has no " + OvLnPiece + " piece");
			return;
		}
		float reach = OvPickReach();
		GameObject have = OvLnExisting(c, part, reach);
		if (have != null)
		{
			_lnLadder = have;
			if (CompanionsPlugin.LogNormal) LogInfo($"Using the ladder at {MHR_Mover.Fmt(have.transform.position)} for \"{part.C.name}\"");
			OvLnClimbStart(part);
			return;
		}
		if (part.LadderTried)
		{
			OvLnGiveUp(part, "its ladder is gone and it had its one try at building one");
			return;
		}
		if (!OvLnLocalBox(prefab, out Bounds lb))
		{
			OvLnGiveUp(part, "the ladder's size can't be read");
			return;
		}
		if (!OvLnFindSpot(c, part, piece, lb, OvLnLocalFwd(prefab), reach, out Vector3 spot, out Quaternion rot, out string why))
		{
			OvLnGiveUp(part, "no spot for a ladder (" + why + ")");
			return;
		}
		// the workbench: only when none has the spot in its build range (CraftingStation.HaveBuildStationInRange)
		CraftingStation station = piece.m_craftingStation;
		bool noBench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
		bool bench = station != null && !noBench && CraftingStation.HaveBuildStationInRange(station.m_name, spot) == null;
		Piece benchPiece = bench ? station.GetComponent<Piece>() : null;
		Vector3 bSpot = Vector3.zero;
		Quaternion bRot = Quaternion.identity;
		if (bench)
		{
			if (benchPiece == null || !OvLnLocalBox(station.gameObject, out Bounds bb))
			{
				OvLnGiveUp(part, "the workbench's size can't be read");
				return;
			}
			Bounds ladderBox = OvLnWorldBox(lb, spot, rot);
			if (!OvLnBenchSpot(c, benchPiece, bb, station.m_rangeBuild, spot, ladderBox, out bSpot, out bRot, out string bwhy))
			{
				OvLnGiveUp(part, "no spot for a workbench (" + bwhy + ")");
				return;
			}
		}
		// the hammer: from its own bag, one whose piece list has the ladder (and the workbench)
		ItemDrop.ItemData hammer = OvLnHammer(prefab, bench ? station.gameObject : null);
		if (hammer == null)
		{
			// GG3a: said once on this vein
			if (OvC().HammerSaid.Add(this))
			{
				OvLnSay(string.Format(ModLocalization.Loc("hc_speech_vein_ladder_need"), "a hammer"));
			}
			OvLnGiveUp(part, "no hammer in its bag");
			return;
		}
		// the materials, from its own bag (Player.HaveRequirements); short of wood: trees nearby (John, 22:47Z)
		Dictionary<string, int> need = new Dictionary<string, int>();
		OvLnNeeds(piece, need);
		if (bench)
		{
			OvLnNeeds(benchPiece, need);
		}
		Inventory bag = _humanoid.GetInventory();
		string woodName = OvLnWoodName();
		int woodShort = 0;
		foreach (KeyValuePair<string, int> kv in need)
		{
			int miss = kv.Value - bag.CountItems(kv.Key);
			if (miss <= 0)
			{
				continue;
			}
			if (kv.Key != woodName)
			{
				OvLnSay(string.Format(ModLocalization.Loc("hc_speech_vein_ladder_need"), miss + " " + MHR_List.Local(kv.Key)));
				OvLnGiveUp(part, $"{miss} {MHR_List.Local(kv.Key)} short for a ladder");
				return;
			}
			woodShort = miss;
		}
		if (woodShort > 0)
		{
			if (OvLnWoodTake(part, woodShort))
			{
				return;
			}
			OvLnSay(string.Format(ModLocalization.Loc("hc_speech_vein_ladder_wood"), woodShort));
			OvLnGiveUp(part, $"{woodShort} wood short for a ladder and no tree left within {OvLnWoodRange:F0} m (it took {part.LadderTrees} of {OvLnTreesMax})");
			return;
		}
		_lnWood = false;
		_lnSpot = spot;
		_lnRot = rot;
		_lnBench = bench;
		_lnBenchSpot = bSpot;
		_lnBenchRot = bRot;
		_lnStage = 2;
		_lnStageAt = Time.time;
		_lnNextAct = 0f;
		_moveTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo($"Building a ladder for \"{part.C.name}\" at {MHR_Mover.Fmt(spot)}{(bench ? $" (a workbench first at {MHR_Mover.Fmt(bSpot)})" : "")}");
	}

	private static void OvLnNeeds(Piece piece, Dictionary<string, int> need)
	{
		if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey()))
		{
			return;
		}
		foreach (Piece.Requirement r in piece.m_resources)
		{
			if (r == null || r.m_resItem == null || r.m_amount <= 0)
			{
				continue;
			}
			string k = r.m_resItem.m_itemData.m_shared.m_name;
			need[k] = (need.TryGetValue(k, out int n) ? n : 0) + r.m_amount;
		}
	}

	private static string OvLnWoodName()
	{
		GameObject w = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Wood") : null;
		ItemDrop d = w != null ? w.GetComponent<ItemDrop>() : null;
		return d != null ? d.m_itemData.m_shared.m_name : "$item_wood";
	}

	private ItemDrop.ItemData OvLnHammer(GameObject ladder, GameObject bench)
	{
		foreach (ItemDrop.ItemData it in _humanoid.GetInventory().GetAllItems())
		{
			PieceTable t = it?.m_shared?.m_buildPieces;
			if (t != null && !Worn(it) && t.m_pieces.Contains(ladder) && (bench == null || t.m_pieces.Contains(bench)))
			{
				return it;
			}
		}
		return null;
	}

	// short of wood: the nearest standing tree (at most 3 for this ladder) or log within 20 m of the vein's centre, handed to
	// the Wood flow (OvTake) - never one a part of the vein rests on (Z2 has those), one it left, or another companion's
	private bool OvLnWoodTake(OvPart part, int woodShort)
	{
		if (s_ovThingMask == 0)
		{
			s_ovThingMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
		}
		OvClear c = OvC();
		Vector3 vc = _vp.Box.center;
		Vector3 me = base.transform.position;
		int k = Physics.OverlapSphereNonAlloc(vc, OvLnWoodRange + 5f, s_lnBuf, s_ovThingMask, QueryTriggerInteraction.Ignore);
		HashSet<int> seen = new HashSet<int>();
		GameObject best = null;
		string bestType = null;
		float bd = float.MaxValue;
		for (int i = 0; i < k; i++)
		{
			GameObject go = OvThingOf(s_lnBuf[i], out int mode, out string type);
			if (go == null || mode != 1 || (type != "TreeBase" && type != "TreeLog") || !seen.Add(go.GetInstanceID()))
			{
				continue;
			}
			int id = go.GetInstanceID();
			if (Flat(go.transform.position, vc) > OvLnWoodRange || _blacklist.ContainsKey(id) || OvPitCooling(id) || (s_claimedTargets.Contains(id) && id != _claimedTargetId))
			{
				continue;
			}
			if (type == "TreeBase" && part.LadderTrees >= OvLnTreesMax)
			{
				continue;
			}
			bool holds = false;
			foreach (OvPart p in c.Parts)
			{
				if (p.HoldThing == go)
				{
					holds = true;
					break;
				}
			}
			if (holds)
			{
				continue;
			}
			float d = Flat(me, go.transform.position);
			if (d < bd)
			{
				bd = d;
				best = go;
				bestType = type;
			}
		}
		if (best == null || FindBestTool(1) == null)
		{
			return false;
		}
		if (bestType == "TreeBase")
		{
			part.LadderTrees++;
		}
		_lnWood = true;
		OvRelease();
		return OvTake(best, 1, bestType, OvNearest(best.transform.position), canSkip: false, $"Wood for a ladder ({woodShort} short{(bestType == "TreeBase" ? $", tree {part.LadderTrees} of {OvLnTreesMax}" : "")})") == 1;
	}

	// ---- where ----

	// the prefab's solid colliders as one box in its own space (a prefab's collider bounds can't be asked)
	private static bool OvLnLocalBox(GameObject prefab, out Bounds lb)
	{
		lb = default(Bounds);
		bool any = false;
		Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
		foreach (Collider col in prefab.GetComponentsInChildren<Collider>(true))
		{
			if (col == null || col.isTrigger)
			{
				continue;
			}
			Bounds b;
			if (col is BoxCollider bc)
			{
				b = new Bounds(bc.center, bc.size);
			}
			else if (col is MeshCollider mc && mc.sharedMesh != null)
			{
				b = mc.sharedMesh.bounds;
			}
			else if (col is CapsuleCollider cc)
			{
				Vector3 sz = new Vector3(cc.radius * 2f, cc.radius * 2f, cc.radius * 2f);
				sz[cc.direction] = Mathf.Max(cc.height, cc.radius * 2f);
				b = new Bounds(cc.center, sz);
			}
			else if (col is SphereCollider sc)
			{
				b = new Bounds(sc.center, Vector3.one * sc.radius * 2f);
			}
			else
			{
				continue;
			}
			Matrix4x4 m = toRoot * col.transform.localToWorldMatrix;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
				Vector3 p = m.MultiplyPoint3x4(corner);
				if (!any)
				{
					lb = new Bounds(p, Vector3.zero);
					any = true;
				}
				else
				{
					lb.Encapsulate(p);
				}
			}
		}
		return any;
	}

	private static Bounds OvLnWorldBox(Bounds lb, Vector3 pos, Quaternion rot)
	{
		Bounds w = new Bounds(pos + rot * lb.center, Vector3.zero);
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = new Vector3((i & 1) == 0 ? lb.min.x : lb.max.x, (i & 2) == 0 ? lb.min.y : lb.max.y, (i & 4) == 0 ? lb.min.z : lb.max.z);
			w.Encapsulate(pos + rot * corner);
		}
		return w;
	}

	// the way its AutoJumpLedge pushes (in the prefab's own space, flat)
	private static Vector3 OvLnLocalFwd(GameObject prefab)
	{
		AutoJumpLedge l = prefab.GetComponentInChildren<AutoJumpLedge>(true);
		if (l == null)
		{
			return Vector3.forward;
		}
		Vector3 f = prefab.transform.InverseTransformDirection(l.transform.forward);
		f.y = 0f;
		return f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
	}

	// (a): clear of every other standing part's game box (the target's own box goes with it when it breaks)
	private static bool OvLnClearOfParts(OvClear c, OvPart target, Bounds wb)
	{
		Bounds w = wb;
		w.Expand(-0.1f);
		foreach (OvPart p in c.Parts)
		{
			if (p == target || p.Out || p.C == null || !p.C.gameObject.activeSelf)
			{
				continue;
			}
			if (new Bounds(p.Centre, p.Half * 2f).Intersects(w))
			{
				return false;
			}
		}
		return true;
	}

	// (c): the game's placing checks (Player.UpdatePlacementGhost) that apply to a piece put on the ground
	private bool OvLnPlaceOk(Piece piece, Bounds wb, Vector3 pos, out string why)
	{
		why = null;
		if (s_lnSolid < 0)
		{
			s_lnSolid = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small");
			s_lnGround = LayerMask.GetMask("terrain", "piece", "Default", "static_solid", "Default_small");
		}
		if (Location.IsInsideNoBuildLocation(pos))
		{
			why = "no-build place";
			return false;
		}
		if (!PrivateArea.CheckAccess(pos, 0f, flash: false))
		{
			why = "a ward";
			return false;
		}
		if (piece.m_onlyInBiome != 0 && (Heightmap.FindBiome(pos) & piece.m_onlyInBiome) == 0)
		{
			why = "wrong biome";
			return false;
		}
		if (!Physics.Raycast(pos + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, s_lnGround, QueryTriggerInteraction.Ignore))
		{
			why = "no ground";
			return false;
		}
		if (hit.collider.attachedRigidbody != null)
		{
			why = "on something loose";
			return false;
		}
		if (piece.m_groundOnly && hit.collider.GetComponent<Heightmap>() == null)
		{
			why = "not on the ground";
			return false;
		}
		if (piece.m_notOnTiltingSurface && hit.normal.y < 0.8f)
		{
			why = "too steep";
			return false;
		}
		foreach (Player p in Player.GetAllPlayers())
		{
			if (p != null && new Bounds(p.transform.position + Vector3.up, new Vector3(0.8f, 2f, 0.8f)).Intersects(wb))
			{
				why = "a player there";
				return false;
			}
		}
		if (piece.m_noClipping)
		{
			Vector3 half = wb.extents - new Vector3(0.1f, 0.15f, 0.1f);
			if (half.x > 0f && half.y > 0f && half.z > 0f && Physics.CheckBox(wb.center + Vector3.up * 0.15f, half, Quaternion.identity, s_lnSolid, QueryTriggerInteraction.Ignore))
			{
				why = "something in the way";
				return false;
			}
		}
		return true;
	}

	private static Vector3Int OvLnCell(Vector3 p)
	{
		return new Vector3Int(Mathf.RoundToInt(p.x * 2f), 0, Mathf.RoundToInt(p.z * 2f));
	}

	// the ladder's spot: every 0.5 m under the part's box and up to 2 m out, nearest to it first, facing the part's centre;
	// (b) its top + reach gets to the part's box bottom and the part is within the swing from its top, (a), (c)
	private bool OvLnFindSpot(OvClear c, OvPart part, Piece piece, Bounds lb, Vector3 fwdL, float reach, out Vector3 spot, out Quaternion rot, out string why)
	{
		spot = Vector3.zero;
		rot = Quaternion.identity;
		why = null;
		Vector3 me = base.transform.position;
		Bounds pb = new Bounds(part.Centre, part.Half * 2f);
		float bottom = OvPartBottom(part);
		float range = GetAttackRange();
		ItemDrop.ItemData pick = FindBestTool(2);
		if (pick?.m_shared?.m_attack != null)
		{
			range = pick.m_shared.m_attack.m_attackRange;
		}
		List<Vector3> pts = new List<Vector3>();
		for (float x = pb.min.x - OvLnOut; x <= pb.max.x + OvLnOut; x += OvLnGrid)
		{
			for (float z = pb.min.z - OvLnOut; z <= pb.max.z + OvLnOut; z += OvLnGrid)
			{
				Vector3 p = new Vector3(x, 0f, z);
				p.y = GroundAt(p);
				if (p.y < bottom)
				{
					pts.Add(p);
				}
			}
		}
		pts.Sort((Vector3 a, Vector3 b) => Flat(me, a).CompareTo(Flat(me, b)));
		Quaternion fromL = Quaternion.Inverse(Quaternion.LookRotation(fwdL));
		int low = 0, far = 0, inBox = 0, bad = 0;
		string lastBad = null;
		foreach (Vector3 p in pts)
		{
			if (_lnBad.Contains(OvLnCell(p)))
			{
				bad++;
				continue;
			}
			Vector3 d = part.Centre - p;
			d.y = 0f;
			if (d.sqrMagnitude < 0.01f)
			{
				d = me - p;
				d.y = 0f;
				d = d.sqrMagnitude < 0.01f ? Vector3.forward : -d;
			}
			Quaternion r = Quaternion.LookRotation(d.normalized) * fromL;
			Bounds wb = OvLnWorldBox(lb, p, r);
			if (wb.max.y + reach < bottom)
			{
				low++;
				continue;
			}
			Vector3 top = new Vector3(wb.center.x, wb.max.y, wb.center.z);
			if (Flat(top, pb.ClosestPoint(top)) > range * 0.8f)
			{
				far++;
				continue;
			}
			if (!OvLnClearOfParts(c, part, wb))
			{
				inBox++;
				continue;
			}
			if (!OvLnPlaceOk(piece, wb, p, out string w))
			{
				bad++;
				lastBad = w;
				continue;
			}
			spot = p;
			rot = r;
			return true;
		}
		why = $"{pts.Count} looked at: {low} too low, {far} too far from it, {inBox} inside another part's box, {bad} not placeable{(lastBad != null ? " (" + lastBad + ")" : "")}";
		return false;
	}

	// the workbench: on the ground 2-4 m from the ladder spot (every 30 deg, every 0.5 m), within its build range of the ladder
	// spot, clear of every standing part's box and the ladder, nearest the ladder first
	private bool OvLnBenchSpot(OvClear c, Piece bench, Bounds lb, float range, Vector3 ladder, Bounds ladderBox, out Vector3 spot, out Quaternion rot, out string why)
	{
		spot = Vector3.zero;
		rot = Quaternion.identity;
		why = null;
		int inBox = 0, bad = 0, outRange = 0;
		for (float rad = 2f; rad <= 4f; rad += 0.5f)
		{
			for (int a = 0; a < 12; a++)
			{
				Vector3 dir = Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward;
				Vector3 p = ladder + dir * rad;
				p.y = GroundAt(p);
				if (_lnBad.Contains(OvLnCell(p)))
				{
					bad++;
					continue;
				}
				if (Flat(p, ladder) >= range)
				{
					outRange++;
					continue;
				}
				Quaternion r = Quaternion.LookRotation(-dir);
				Bounds wb = OvLnWorldBox(lb, p, r);
				if (!OvLnClearOfParts(c, null, wb) || wb.Intersects(ladderBox))
				{
					inBox++;
					continue;
				}
				if (!OvLnPlaceOk(bench, wb, p, out _))
				{
					bad++;
					continue;
				}
				spot = p;
				rot = r;
				return true;
			}
		}
		why = $"{inBox} inside a part's box or the ladder's spot, {bad} not placeable, {outRange} out of its range";
		return false;
	}

	// a ladder already standing (its own or another's - the mover's test: a piece with an AutoJumpLedge) that passes (a), (b)
	private GameObject OvLnExisting(OvClear c, OvPart part, float reach)
	{
		if (s_lnSolid < 0)
		{
			s_lnSolid = LayerMask.GetMask("piece", "Default", "static_solid", "Default_small");
			s_lnGround = LayerMask.GetMask("terrain", "piece", "Default", "static_solid", "Default_small");
		}
		Bounds pb = new Bounds(part.Centre, part.Half * 2f);
		float floor = GroundAt(part.Centre) - 1f;
		Vector3 half = new Vector3(pb.extents.x + OvLnOut, (pb.max.y - floor) * 0.5f, pb.extents.z + OvLnOut);
		Vector3 ctr = new Vector3(pb.center.x, (pb.max.y + floor) * 0.5f, pb.center.z);
		int n = Physics.OverlapBoxNonAlloc(ctr, half, s_lnBuf, Quaternion.identity, LayerMask.GetMask("piece"), QueryTriggerInteraction.Ignore);
		float bottom = OvPartBottom(part);
		float range = GetAttackRange();
		ItemDrop.ItemData pick = FindBestTool(2);
		if (pick?.m_shared?.m_attack != null)
		{
			range = pick.m_shared.m_attack.m_attackRange;
		}
		Vector3 me = base.transform.position;
		HashSet<int> seen = new HashSet<int>();
		GameObject best = null;
		float bd = float.MaxValue;
		for (int i = 0; i < n; i++)
		{
			Piece pc = s_lnBuf[i] != null ? s_lnBuf[i].GetComponentInParent<Piece>() : null;
			if (pc == null || !seen.Add(pc.GetInstanceID()) || pc.GetComponentInChildren<AutoJumpLedge>(true) == null)
			{
				continue;
			}
			if (_lnSkip.Contains(pc.gameObject.GetInstanceID()))
			{
				continue;   // AA6: it waited 20 s for this one this round
			}
			bool any = false;
			Bounds wb = default(Bounds);
			foreach (Collider col in pc.GetComponentsInChildren<Collider>())
			{
				if (col.isTrigger)
				{
					continue;
				}
				if (!any)
				{
					wb = col.bounds;
					any = true;
				}
				else
				{
					wb.Encapsulate(col.bounds);
				}
			}
			if (!any || wb.max.y + reach < bottom)
			{
				continue;
			}
			Vector3 top = new Vector3(wb.center.x, wb.max.y, wb.center.z);
			if (Flat(top, pb.ClosestPoint(top)) > range * 0.8f || !OvLnClearOfParts(c, part, wb))
			{
				continue;
			}
			float d = Flat(me, wb.center);
			if (d < bd)
			{
				bd = d;
				best = pc.gameObject;
			}
		}
		return best;
	}

	// ---- build ----

	private void OvLnBuild(OvClear c, OvPart part)
	{
		bool bench = _lnBench;
		Vector3 at = bench ? _lnBenchSpot : _lnSpot;
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(OvLnPiece) : null;
		Piece ladderPiece = prefab != null ? prefab.GetComponent<Piece>() : null;
		CraftingStation station = ladderPiece != null ? ladderPiece.m_craftingStation : null;
		GameObject what = bench ? station?.gameObject : prefab;
		Piece piece = what != null ? what.GetComponent<Piece>() : null;
		if (piece == null)
		{
			OvLnGiveUp(part, "the piece to build is gone from the game");
			return;
		}
		float flat = Flat(me, at);
		if (flat > OvLnBuildDist)
		{
			if (WalkTimer(dt, at) && _moveTimer > 10f)
			{
				OvClFail($"couldn't get within {OvLnBuildDist:F0} m of {MHR_Mover.Fmt(at)} to build in 10 s ({flat:F1} m away)");
				return;
			}
			WalkGo(at, OvLnBuildDist - 1f, flat > 8f, dt);
			_lnStageAt = Time.time;
			return;
		}
		_ai?.StopMoving();
		if (Time.time - _lnStageAt > OvLnBuildTime)
		{
			OvClFail($"couldn't place the {(bench ? "workbench" : "ladder")} in {OvLnBuildTime:F0} s");
			return;
		}
		ItemDrop.ItemData hammer = OvLnHammer(prefab, bench ? what : null);
		if (hammer == null)
		{
			OvLnGiveUp(part, "no hammer in its bag any more");
			return;
		}
		if (ReflectionHelper.GetRightItem(_humanoid) != hammer)
		{
			_humanoid.EquipItem(hammer);
			_lnNextAct = Time.time + 0.6f;
			return;
		}
		Vector3 look = at - me;
		look.y = 0f;
		MHR_Mover.Face(_ai, look);
		if (Time.time < _lnNextAct || _character.InAttack())
		{
			return;
		}
		// the game asks for the hammer's attack stamina to be there (Player.UpdatePlaceMode: HaveStamina)
		float cost = hammer.m_shared.m_attack != null ? hammer.m_shared.m_attack.m_attackStamina : 0f;
		if (_stamina != null && _stamina.Stamina < cost)
		{
			return;
		}
		// still free, still in a station's range, still the materials (someone may have changed things meanwhile)
		if (!OvLnLocalBox(what, out Bounds lb))
		{
			OvLnGiveUp(part, "its size can't be read");
			return;
		}
		Quaternion rot = bench ? _lnBenchRot : _lnRot;
		Bounds wb = OvLnWorldBox(lb, at, rot);
		bool stationOk = bench || station == null || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)) || CraftingStation.HaveBuildStationInRange(station.m_name, at) != null;
		string why = null;
		if (!OvLnClearOfParts(c, bench ? null : part, wb) || !OvLnPlaceOk(piece, wb, at, out why) || !stationOk)
		{
			_lnBad.Add(OvLnCell(at));
			if (CompanionsPlugin.LogNormal) LogInfo($"Can't place the {(bench ? "workbench" : "ladder")} at {MHR_Mover.Fmt(at)} any more ({why ?? (stationOk ? "a part's box" : "no workbench in range")}) - looking again");
			_lnStage = 0;
			return;
		}
		Dictionary<string, int> need = new Dictionary<string, int>();
		OvLnNeeds(piece, need);
		Inventory bag = _humanoid.GetInventory();
		foreach (KeyValuePair<string, int> kv in need)
		{
			if (bag.CountItems(kv.Key) < kv.Value)
			{
				_lnStage = 0;   // its bag changed: the plan again (wood, or give up)
				return;
			}
		}
		GameObject go = MHR_Build.Place(this, what, at, rot, hammer);
		// Player.ConsumeResources, the build stamina (Player.GetBuildStamina) and the hammer's wear (GetPlaceDurability)
		foreach (KeyValuePair<string, int> kv in need)
		{
			bag.RemoveItem(kv.Key, kv.Value);
		}
		Skills.SkillType sk = hammer.m_shared.m_buildPieces != null ? hammer.m_shared.m_buildPieces.m_skill : Skills.SkillType.None;
		float stam = cost;
		if (sk != Skills.SkillType.None)
		{
			stam -= stam * 0.5f * _character.GetSkillFactor(sk);
		}
		_stamina?.UseStamina(stam);
		if (hammer.m_shared.m_useDurability)
		{
			float wear = hammer.m_shared.m_useDurabilityDrain;
			if (hammer.m_shared.m_placementDurabilitySkill != Skills.SkillType.None)
			{
				wear -= wear * hammer.m_shared.m_placementDurabilityMax * _character.GetSkillFactor(hammer.m_shared.m_placementDurabilitySkill);
			}
			hammer.m_durability = Mathf.Max(0f, hammer.m_durability - wear * Game.m_durabilityRate);
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Built a {(bench ? "workbench" : "ladder")} ({go.name}) at {MHR_Mover.Fmt(at)}");
		_lnNextAct = Time.time + 0.6f;
		_lnStageAt = Time.time;
		if (bench)
		{
			_lnBench = false;
			return;
		}
		part.LadderTried = true;
		_lnLadder = go;
		OvLnClimbStart(part);
	}

	// ---- climb, then the part swing ----

	// S1 (John, 2026-11-20: "Use what is already built ... All it needs is for them to stop at the top of the ladder"): the
	// ladder is climbed by the mover's own ladder climb (MHR_Mover: the foot, jumping onto it, its own limits) - the walk
	// goal is the ladder's top; the only thing added is the stop once the feet are at the top.
	private void OvLnClimbStart(OvPart part)
	{
		ItemDrop.ItemData pick = FindBestTool(2);
		if (pick != null)
		{
			EquipTool(pick);
		}
		if (!OvLnBounds(out Bounds wb))
		{
			OvClFail("its ladder is gone");
			return;
		}
		_lnTop = new Vector3(wb.center.x, wb.max.y, wb.center.z);
		_lnStopY = wb.max.y - 0.3f;
		_lnPushing = false;
		_lnLowFor = 0f;
		_lnStage = 3;
		_lnStageAt = Time.time;
		_moveTimer = 0f;
		if (CompanionsPlugin.LogNormal) LogInfo($"Climbing the ladder at {MHR_Mover.Fmt(wb.center)} to its top (feet {_lnStopY:F1}) for \"{part.C.name}\" (bottom {OvPartBottom(part):F1})");
	}

	// the ladder's solid colliders together (its world box)
	private bool OvLnBounds(out Bounds wb)
	{
		wb = default(Bounds);
		if (_lnLadder == null || !_lnLadder.activeInHierarchy)
		{
			return false;
		}
		bool any = false;
		foreach (Collider col in _lnLadder.GetComponentsInChildren<Collider>())
		{
			if (col.isTrigger)
			{
				continue;
			}
			if (!any)
			{
				wb = col.bounds;
				any = true;
			}
			else
			{
				wb.Encapsulate(col.bounds);
			}
		}
		return any;
	}

	private void OvLnClimb(OvClear c, OvPart part)
	{
		Vector3 me = base.transform.position;
		float dt = Time.deltaTime;
		if (!OvLnBounds(out Bounds wb))
		{
			OvClFail("its ladder is gone");
			return;
		}
		// AA6: another companion is on this ladder - it waits 2 m from it, up to 20 s; then another ladder spot
		if (OvLnInUse(_lnLadder))
		{
			if (_lnWaitFrom < 0f)
			{
				_lnWaitFrom = Time.time;
				if (CompanionsPlugin.LogNormal) LogInfo($"Another companion is on the ladder at {MHR_Mover.Fmt(_lnLadder.transform.position)} - waiting (up to {OvBoardLadderWait:F0} s)");
			}
			if (Time.time - _lnWaitFrom >= OvBoardLadderWait)
			{
				_lnSkip.Add(_lnLadder.GetInstanceID());
				_lnWaitFrom = -1f;
				_lnLadder = null;
				_lnStage = 0;
				if (CompanionsPlugin.LogNormal) LogInfo($"The ladder was in use for {OvBoardLadderWait:F0} s - looking for another ladder spot");
				return;
			}
			if (Flat(me, wb.center) > 2.5f)
			{
				WalkGo(_lnTop, 2f, false, dt);
			}
			else
			{
				_ai?.StopMoving();
			}
			_moveTimer = 0f;
			return;
		}
		_lnWaitFrom = -1f;
		// on the ladder (AA6's "in use"): over it, above its foot
		_lnPushing = Flat(me, wb.center) < Mathf.Max(wb.extents.x, wb.extents.z) + 0.5f && me.y > wb.min.y + 0.3f;
		if (me.y >= _lnStopY && _lnPushing)
		{
			// the stop: it stops moving at the top (Character.OnAutoJump lifts only while it moves into the ladder)
			_ai?.StopMoving();
			OvLnSwingFromHere(part, me);
			return;
		}
		// the mover's own limits: its give-up (MHR_Mover.GaveUp) or 10 s without getting closer
		if (WalkTimer(dt, _lnTop) && _moveTimer > 10f)
		{
			_lnPushing = false;
			_ai?.StopMoving();
			OvClFail($"couldn't get up the ladder (feet {me.y:F1}, its top {_lnStopY:F1})");
			return;
		}
		WalkGo(_lnTop, 0.3f, false, dt);
	}

	// S2 (John: "They should be aiming at the part, not the box"): from the ladder's top, the same aim as on the ground (M1) -
	// the game's own pickaxe swing rays from its feet; only a ray whose first hit is the part's own rock counts, and the swing
	// aims there. None does: the part waits 60 s.
	private void OvLnSwingFromHere(OvPart part, Vector3 me)
	{
		Attack a = FindBestTool(2)?.m_shared?.m_attack;
		if (!OvSwingReaches(a, me, part, out Vector3 aim))
		{
			_lnPushing = false;
			OvClFail($"no swing from the ladder's top at {MHR_Mover.Fmt(me)} meets its rock first");
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo($"Up the ladder (feet {me.y:F1}) - swinging at \"{part.C.name}\" (bottom {OvPartBottom(part):F1}), at its rock at {MHR_Mover.Fmt(aim)}");
		_lnPushing = true;
		OvDigBegin(aim, null, ramp: false);
		_dgClear = true;
		_dgPart = true;
		_dgGoalY = me.y;
		_lnStage = 4;
		_lnLowFor = 0f;
	}
}
