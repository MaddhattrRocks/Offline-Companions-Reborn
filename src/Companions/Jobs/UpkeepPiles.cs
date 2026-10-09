using System;
using System.Collections.Generic;
using Splatform;
using UnityEngine;

namespace Companions;

// 1.4.126: Repair and Restock also stacks wood and stone into piles (user: "should also make wood and stone piles as
// they are sorting the chests"). Agreed: only next to piles of the same kind the player already built (the game's Stack
// of Wood, Stone Pile, Fine Wood Stack... - any player-built piece named *stack* / *pile* made from one material), and
// only what's above [RepairAndRestock] PileKeep in the chests. Built like a player: hammer in hand, the material taken
// from the chests and used up, the piece's crafting station in range, ward access, stamina and hammer wear.
public partial class UpkeepController
{
	private sealed class PileKind
	{
		public string Item;          // material prefab, e.g. "Wood"

		public string Shared;        // its in-game token, e.g. "$item_wood"

		public int Cost;

		public GameObject Prefab;

		public readonly List<Piece> Piles = new List<Piece>();
	}

	private PileKind _pile;

	private bool _pileHasSpot;

	private Vector3 _pileSpot;

	private Quaternion _pileRot;

	private Piece _pileNextTo;

	private Vector3 _pileStand;

	private readonly Dictionary<string, float> _pileNoSpot = new Dictionary<string, float>();

	private readonly HashSet<Vector3Int> _pileBadSpots = new HashSet<Vector3Int>();

	private static int s_buildMask = -1;

	private bool PlanPiles()
	{
		if (ModConfig.UpkeepPiles != null && !ModConfig.UpkeepPiles.Value)
		{
			return false;
		}
		if (Hammer() == null)
		{
			return false;
		}
		MHR_List allow = MHR_List.Get(ModConfig.UpkeepPileItems?.Value ?? "*");
		int keepDefault = ModConfig.UpkeepPileKeep?.Value ?? 100;
		List<Container> chests = Chests();
		foreach (PileKind k in PileKinds())
		{
			float until;
			if (_pileNoSpot.TryGetValue(k.Item, out until) && Time.time < until)
			{
				continue;
			}
			if (!allow.Allows(k.Item, MHR_List.Local(k.Shared)))
			{
				continue;
			}
			int keep = allow.AmountFor(k.Item, keepDefault);
			int total = 0;
			Container from = null;
			int most = 0;
			foreach (Container c in chests)
			{
				int n = c.GetInventory().CountItems(k.Shared);
				total += n;
				if (n > most)
				{
					most = n;
					from = c;
				}
			}
			int piles = (total - keep) / Mathf.Max(1, k.Cost);
			if (piles <= 0 || from == null)
			{
				continue;
			}
			// as many as it can carry in one go, from the chest holding the most
			float w = WeightOf(from, k.Shared);
			int carry = w > 0f ? Mathf.FloorToInt((CompanionTierData.MaxCarryWeight - _humanoid.GetInventory().GetTotalWeight()) / (w * k.Cost)) : piles;
			int take = Mathf.Min(piles, Mathf.Min(carry, most / k.Cost)) * k.Cost;
			if (take < k.Cost)
			{
				continue;
			}
			_pile = k;
			_chest = from;
			_key = k.Shared;
			_amount = take;
			_pileHasSpot = false;
			Log("Stacking " + take + " " + MHR_List.Local(k.Shared) + " into " + (take / k.Cost) + " " + MHR_List.Local(k.Piles[0].m_name) + "(s) next to the existing ones (" + total + " in the chests, keeping " + keep + ")");
			Enter(Step.PileTake);
			return true;
		}
		return false;
	}

	private static float WeightOf(Container c, string shared)
	{
		foreach (ItemDrop.ItemData it in c.GetInventory().GetAllItems())
		{
			if (it.m_shared.m_name == shared)
			{
				return it.m_shared.m_weight;
			}
		}
		return 0f;
	}

	// the kinds of pile the player has built in the work area
	private List<PileKind> PileKinds()
	{
		Dictionary<string, PileKind> kinds = new Dictionary<string, PileKind>();
		s_pieces.Clear();
		Piece.GetAllPiecesInRadius(Center(), Range(), s_pieces);
		foreach (Piece p in s_pieces)
		{
			if (!IsPile(p))
			{
				continue;
			}
			Piece.Requirement req = p.m_resources[0];
			string item = req.m_resItem.gameObject.name;
			PileKind k;
			if (!kinds.TryGetValue(item, out k))
			{
				GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(Utils.GetPrefabName(p.gameObject)) : null;
				if (prefab == null)
				{
					continue;
				}
				k = new PileKind
				{
					Item = item,
					Shared = req.m_resItem.m_itemData.m_shared.m_name,
					Cost = Mathf.Max(1, req.m_amount),
					Prefab = prefab
				};
				kinds[item] = k;
			}
			k.Piles.Add(p);
		}
		return new List<PileKind>(kinds.Values);
	}

	private static bool IsPile(Piece p)
	{
		if (p == null || !p.IsPlacedByPlayer() || p.m_resources == null || p.m_resources.Length != 1 || p.m_resources[0].m_resItem == null)
		{
			return false;
		}
		string n = Utils.GetPrefabName(p.gameObject).ToLowerInvariant();
		if (!n.Contains("stack") && !n.Contains("pile"))
		{
			return false;
		}
		return p.GetComponentInChildren<Container>() == null && p.GetComponentInChildren<Fireplace>() == null && p.GetComponentInChildren<CraftingStation>() == null && p.GetComponentInChildren<Bed>() == null && p.GetComponentInChildren<Door>() == null;
	}

	private void PileTaken(Container c, Inventory inv, Inventory bag)
	{
		int got = Move(inv, bag, _key, byName: true, _amount);
		Took(_key, got);
		CloseChest(c);
		PileKind k = _pile;
		if (k == null || got < k.Cost)
		{
			Done("Not enough " + MHR_List.Local(_key) + " for a pile after all (" + got + ")");
			return;
		}
		LogDebug("Took " + got + " " + MHR_List.Local(_key) + " for piles");
		Done(null);
		_pile = k;
		_pileHasSpot = false;
		Enter(Step.PileBuild);
	}

	private void UpdatePileBuild()
	{
		PileKind k = _pile;
		Inventory bag = _humanoid.GetInventory();
		if (k == null || bag.CountItems(k.Shared) < k.Cost)
		{
			_pile = null;
			Done(null);   // leftovers go back with the next put-away
			return;
		}
		ItemDrop.ItemData hammer = Hammer();
		if (hammer == null)
		{
			_pile = null;
			Done("No working hammer - no piles for now");
			return;
		}
		if (!_pileHasSpot)
		{
			if (!PileSpot(k))
			{
				_pileNoSpot[k.Item] = Time.time + 600f;
				Log("No free spot next to the existing " + MHR_List.Local(k.Shared) + " piles - trying again in 10 minutes");
				_pile = null;
				Done(null);
				return;
			}
			_pileHasSpot = true;
			_stepStart = Time.time;
			_arrived = false;
			_arrivedAt = 0f;
			Vector3 stand;
			_pileStand = MHR_Chests.PickApproachPoint(base.transform.position, _pileSpot, _pileNextTo, out stand) ? stand : _pileSpot;
		}
		Vector3 me = base.transform.position;
		Vector3 aim = _pileSpot + Vector3.up * 0.4f;
		if (!PointInReach(me, aim, _pileNextTo) || Vector3.Distance(me, _pileSpot) > Reach)
		{
			bool far = Time.time - _stepStart > GiveUp;
			_arrived = MHR_Nav.Move(_ai, Time.deltaTime, _pileStand, _pileStand == _pileSpot ? 1.5f : 0.6f, run: true, Tag);
			if (_arrived && _arrivedAt == 0f)
			{
				_arrivedAt = Time.time;
			}
			if (far || MHR_Mover.GaveUp(_ai) || (_arrived && Time.time - _arrivedAt > 3f))
			{
				LogDebug("Can't get to the pile spot " + MHR_Mover.Fmt(_pileSpot) + " - trying another");
				_pileBadSpots.Add(Cell(_pileSpot));
				_pileHasSpot = false;
				MHR_Mover.Release(_ai);
			}
			return;
		}
		_ai.StopMoving();
		MHR_Mover.Release(_ai);
		if (!_humanoid.IsItemEquiped(hammer))
		{
			_setup.SuppressAutoEquip = true;
			_holdingHammer = true;
			_humanoid.EquipItem(hammer);
			_nextAct = Time.time + 0.6f;
			return;
		}
		_ai.LookAtPoint(aim);
		if (Time.time < _nextAct || _character.InAttack() || !_ai.IsLookingAtPoint(aim, 15f))
		{
			return;
		}
		float cost = SwingStamina(hammer);
		if (_stamina != null && _stamina.Stamina < cost)
		{
			return;
		}
		// still free? (someone may have put something there meanwhile)
		Piece prefabPiece = k.Prefab.GetComponent<Piece>();
		if (!SpotFree(k, _pileSpot, _pileRot))
		{
			_pileBadSpots.Add(Cell(_pileSpot));
			_pileHasSpot = false;
			return;
		}
		bool noBench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
		if (!noBench && prefabPiece != null && prefabPiece.m_craftingStation != null && CraftingStation.HaveBuildStationInRange(prefabPiece.m_craftingStation.m_name, _pileSpot) == null)
		{
			_pileBadSpots.Add(Cell(_pileSpot));
			_pileHasSpot = false;
			LogDebug("No " + MHR_List.Local(prefabPiece.m_craftingStation.m_name) + " in range of " + MHR_Mover.Fmt(_pileSpot));
			return;
		}
		if (!PrivateArea.CheckAccess(_pileSpot, 0f, flash: false))
		{
			_pileBadSpots.Add(Cell(_pileSpot));
			_pileHasSpot = false;
			return;
		}
		_nextAct = Time.time + WorkTime;
		PlacePile(k, hammer, cost);
		_pileHasSpot = false;
	}

	// what Player.PlacePiece does, then the material, stamina and hammer wear
	private void PlacePile(PileKind k, ItemDrop.ItemData hammer, float stamina)
	{
		Inventory bag = _humanoid.GetInventory();
		TerrainModifier.SetTriggerOnPlaced(trigger: true);
		GameObject go = UnityEngine.Object.Instantiate(k.Prefab, _pileSpot, _pileRot);
		TerrainModifier.SetTriggerOnPlaced(trigger: false);
		Piece piece = go.GetComponent<Piece>();
		if (piece != null && Player.m_localPlayer != null)
		{
			PlatformUserID user = default(PlatformUserID);
			try
			{
				user = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
			}
			catch
			{
			}
			piece.SetCreator(Player.m_localPlayer.GetPlayerID(), user);
		}
		go.GetComponent<WearNTear>()?.OnPlaced();
		string anim = hammer.m_shared.m_attack?.m_attackAnimation;
		GetComponent<ZSyncAnimation>()?.SetTrigger(!string.IsNullOrEmpty(anim) ? anim : "swing_hammer");
		if (piece != null)
		{
			piece.m_placeEffect.Create(_pileSpot, _pileRot, go.transform, 1f, -1, _character.GetZDOID());
		}
		bag.RemoveItem(k.Shared, k.Cost);
		Took(k.Shared, -k.Cost);
		_stamina?.UseStamina(stamina);
		if (hammer.m_shared.m_useDurability)
		{
			hammer.m_durability = Mathf.Max(0f, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain * Game.m_durabilityRate);
		}
		if (piece != null)
		{
			k.Piles.Add(piece);
		}
		Log("Built a " + (piece != null ? MHR_List.Local(piece.m_name) : k.Prefab.name) + " at " + MHR_Mover.Fmt(_pileSpot) + " from " + k.Cost + " " + MHR_List.Local(k.Shared));
	}

	// ---- where: right beside (or on top of) one of the same kind ----

	private bool PileSpot(PileKind k)
	{
		Vector3 me = base.transform.position;
		List<Piece> piles = new List<Piece>(k.Piles);
		piles.RemoveAll(p => p == null);
		piles.Sort((a, b) => Vector3.Distance(me, a.transform.position).CompareTo(Vector3.Distance(me, b.transform.position)));
		foreach (Piece pile in piles)
		{
			Vector3 center, half;
			if (!LocalBox(pile, out center, out half))
			{
				continue;
			}
			Vector3[] offsets =
			{
				new Vector3(half.x * 2f + 0.02f, 0f, 0f),
				new Vector3(-half.x * 2f - 0.02f, 0f, 0f),
				new Vector3(0f, 0f, half.z * 2f + 0.02f),
				new Vector3(0f, 0f, -half.z * 2f - 0.02f),
				new Vector3(0f, half.y * 2f + 0.02f, 0f)
			};
			for (int i = 0; i < offsets.Length; i++)
			{
				Vector3 pos = pile.transform.TransformPoint(offsets[i]);
				Quaternion rot = pile.transform.rotation;
				if (_pileBadSpots.Contains(Cell(pos)) || !InArea(pos))
				{
					continue;
				}
				if (i == 4 && offsets[i].y + half.y * 2f > 3.2f)
				{
					continue;   // not stacked higher than it can reach
				}
				if (!SpotFree(k, pos, rot))
				{
					continue;
				}
				if (i < 4 && !Supported(pos, rot, center, half))
				{
					continue;
				}
				_pileSpot = pos;
				_pileRot = rot;
				_pileNextTo = pile;
				return true;
			}
		}
		return false;
	}

	private bool InArea(Vector3 p)
	{
		return Vector3.Distance(Center(), p) <= Range();
	}

	private static Vector3Int Cell(Vector3 p)
	{
		return new Vector3Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.y * 2f), Mathf.RoundToInt(p.z * 2f));
	}

	// the pile's size in its own space, from its colliders
	private static bool LocalBox(Piece pile, out Vector3 center, out Vector3 half)
	{
		center = Vector3.zero;
		half = Vector3.zero;
		Transform t = pile.transform;
		bool any = false;
		Bounds lb = default(Bounds);
		foreach (Collider col in pile.GetComponentsInChildren<Collider>())
		{
			if (col == null || !col.enabled || col.isTrigger)
			{
				continue;
			}
			Bounds cb;
			if (col is BoxCollider bc)
			{
				cb = new Bounds(bc.center, bc.size);
			}
			else if (col is MeshCollider mc && mc.sharedMesh != null)
			{
				cb = mc.sharedMesh.bounds;
			}
			else
			{
				continue;
			}
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = cb.center + Vector3.Scale(cb.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
				Vector3 local = t.InverseTransformPoint(col.transform.TransformPoint(corner));
				if (!any)
				{
					lb = new Bounds(local, Vector3.zero);
					any = true;
				}
				else
				{
					lb.Encapsulate(local);
				}
			}
		}
		if (!any || lb.size.x < 0.2f || lb.size.z < 0.2f)
		{
			return false;
		}
		center = lb.center;
		half = lb.extents;
		return true;
	}

	private static int BuildMask()
	{
		if (s_buildMask == -1)
		{
			s_buildMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "vehicle", "character", "character_net", "character_noenv");
		}
		return s_buildMask;
	}

	// nothing in the way where the new pile would go (the ground itself doesn't count)
	private static bool SpotFree(PileKind k, Vector3 pos, Quaternion rot)
	{
		Piece prefabPiece = k.Prefab.GetComponent<Piece>();
		Vector3 center, half;
		if (prefabPiece == null || !LocalBoxOf(k, out center, out half))
		{
			return false;
		}
		// a little smaller than the pile and lifted off its base, so the floor or ground it stands on doesn't count
		Vector3 box = new Vector3(half.x * 0.9f, half.y * 0.8f, half.z * 0.9f);
		Collider[] hits = Physics.OverlapBox(pos + rot * (center + Vector3.up * half.y * 0.15f), box, rot, BuildMask());
		foreach (Collider h in hits)
		{
			if (h != null && !h.isTrigger)
			{
				return false;
			}
		}
		return true;
	}

	private static readonly Dictionary<string, KeyValuePair<Vector3, Vector3>> s_prefabBox = new Dictionary<string, KeyValuePair<Vector3, Vector3>>();

	private static bool LocalBoxOf(PileKind k, out Vector3 center, out Vector3 half)
	{
		KeyValuePair<Vector3, Vector3> box;
		if (s_prefabBox.TryGetValue(k.Item, out box))
		{
			center = box.Key;
			half = box.Value;
			return true;
		}
		center = Vector3.zero;
		half = Vector3.zero;
		foreach (Piece p in k.Piles)
		{
			if (p != null && LocalBox(p, out center, out half))
			{
				s_prefabBox[k.Item] = new KeyValuePair<Vector3, Vector3>(center, half);
				return true;
			}
		}
		return false;
	}

	// something to stand on right under where the bottom of the new pile would be
	private static bool Supported(Vector3 pos, Quaternion rot, Vector3 center, Vector3 half)
	{
		Vector3 bottom = pos + rot * new Vector3(center.x, center.y - half.y, center.z);
		RaycastHit hit;
		if (!Physics.Raycast(bottom + Vector3.up * 0.5f, Vector3.down, out hit, 1.0f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain")))
		{
			return false;
		}
		return Mathf.Abs(hit.point.y - bottom.y) <= 0.3f;
	}
}
