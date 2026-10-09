using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.4.130: taking a wood/stone pile down for its material, for any job (user: "the smelting job should also be able to
// pull from wood piles"). Like a player: walk up to the pile, hammer in hand, remove it (crafting station in range,
// ward access), then pick up what falls out - or empty the little loot box some piles leave.
internal sealed class MHR_PileTaker
{
	public Piece Pile;

	public string Shared;          // the material's in-game token

	public string Why;             // why it failed

	public int Got;

	private readonly string _tag;

	private int _state;

	private float _start;

	private float _arrivedAt;

	private float _downAt;

	private float _nextAct;

	private float _nextStand;

	private bool _hasStand;

	private Vector3 _stand;

	private Vector3 _at;

	private bool _hammerOut;

	private HashSet<int> _before = new HashSet<int>();

	private const float Reach = 4.5f;

	public MHR_PileTaker(Piece pile, string tag)
	{
		Pile = pile;
		Shared = pile.m_resources[0].m_resItem.m_itemData.m_shared.m_name;
		_tag = tag;
		_start = Time.time;
	}

	// the nearest pile made of this material (top of a stack first) that can be taken down
	public static Piece Find(Vector3 center, float radius, string itemPrefab, Vector3 me, Dictionary<int, float> skip)
	{
		List<Piece> pieces = new List<Piece>();
		Piece.GetAllPiecesInRadius(center, radius, pieces);
		Piece best = null;
		float bestScore = float.MaxValue;
		foreach (Piece p in pieces)
		{
			if (!UpkeepController.IsPile(p) || p.m_resources[0].m_resItem.gameObject.name != itemPrefab || !p.m_canBeRemoved)
			{
				continue;
			}
			float until;
			if (skip != null && skip.TryGetValue(p.GetInstanceID(), out until) && Time.time < until)
			{
				continue;
			}
			if (!PrivateArea.CheckAccess(p.transform.position, 0f, flash: false))
			{
				continue;
			}
			float score = Vector3.Distance(me, p.transform.position) - HeightAboveGround(p.transform.position) * 20f;
			if (score < bestScore)
			{
				bestScore = score;
				best = p;
			}
		}
		return best;
	}

	private static float HeightAboveGround(Vector3 p)
	{
		RaycastHit hit;
		if (Physics.Raycast(p + Vector3.down * 0.05f, Vector3.down, out hit, 10f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain")))
		{
			return p.y - hit.point.y;
		}
		return 0f;
	}

	public static ItemDrop.ItemData Hammer(Humanoid h)
	{
		Inventory inv = h != null ? h.GetInventory() : null;
		if (inv == null)
		{
			return null;
		}
		foreach (ItemDrop.ItemData it in inv.GetAllItems())
		{
			ItemDrop.ItemData.SharedData s = it?.m_shared;
			if (s?.m_buildPieces != null && s.m_buildPieces.name != null && s.m_buildPieces.name.IndexOf("Hammer", System.StringComparison.OrdinalIgnoreCase) >= 0 && (!s.m_useDurability || it.m_durability > 0f))
			{
				return it;
			}
		}
		return null;
	}

	// 0 = still going, 1 = done (Got = how many it picked up), -1 = gave up (Why says why)
	public int Tick(CompanionAI ai, Humanoid hum, CompanionSetup cs)
	{
		float now = Time.time;
		if (_state == 1)
		{
			return Collect(ai, hum, now);
		}
		if (Pile == null || Pile.GetComponent<ZNetView>() == null || !Pile.GetComponent<ZNetView>().IsValid())
		{
			Why = "the pile is gone";
			return -1;
		}
		ItemDrop.ItemData hammer = Hammer(hum);
		if (hammer == null)
		{
			Why = "no working hammer";
			return -1;
		}
		Vector3 me = ai.transform.position;
		Vector3 aim;
		if (!InReach(me, out aim))
		{
			if (now - _start > 90f)
			{
				Why = "couldn't get within reach of it";
				return -1;
			}
			if (!_hasStand || now >= _nextStand)
			{
				_nextStand = now + 4f;
				Vector3 s;
				if (MHR_Chests.PickApproachPiece(me, Pile, out s))
				{
					_stand = s;
					_hasStand = true;
				}
				else if (!_hasStand)
				{
					_stand = Pile.transform.position;
					_hasStand = true;
				}
			}
			bool arrived = MHR_Nav.Move(ai, Time.deltaTime, _stand, _stand == Pile.transform.position ? 1.5f : 0.6f, true, _tag);
			if (MHR_Mover.GaveUp(ai))
			{
				Why = "no way to get to it";
				return -1;
			}
			if (!arrived)
			{
				_arrivedAt = 0f;
			}
			else if (_arrivedAt == 0f)
			{
				_arrivedAt = now;
			}
			else if (now - _arrivedAt > 3f)
			{
				Why = "can't reach it from where it can stand";
				return -1;
			}
			return 0;
		}
		ai.StopMoving();
		MHR_Mover.Release(ai);
		if (!hum.IsItemEquiped(hammer))
		{
			if (cs != null)
			{
				cs.SuppressAutoEquip = true;
			}
			_hammerOut = true;
			hum.EquipItem(hammer);
			_nextAct = now + 0.6f;
			return 0;
		}
		ai.LookAtPoint(aim);
		if (now < _nextAct || hum.InAttack() || !ai.IsLookingAtPoint(aim, 15f))
		{
			return 0;
		}
		bool noBench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
		if (!noBench && Pile.m_craftingStation != null && CraftingStation.HaveBuildStationInRange(Pile.m_craftingStation.m_name, me) == null)
		{
			Why = "no " + MHR_List.Local(Pile.m_craftingStation.m_name) + " in range to take it down";
			return -1;
		}
		if (!PrivateArea.CheckAccess(Pile.transform.position, 0f, flash: false) || !Pile.CanBeRemoved())
		{
			Why = "it can't be taken down right now";
			return -1;
		}
		_at = Pile.transform.position;
		_before = MHR_Chests.DropsNear(_at, 4f);   // 1.4.152: only what this pile drops is picked up
		ZNetView nv = Pile.GetComponent<ZNetView>();
		if (!nv.IsOwner())
		{
			nv.ClaimOwnership();
		}
		Pile.GetComponent<IRemoved>()?.OnRemoved();
		WearNTear w = Pile.GetComponent<WearNTear>();
		if (w != null)
		{
			w.Remove();
		}
		else
		{
			Pile.DropResources();
			Pile.m_placeEffect.Create(_at, Pile.transform.rotation, Pile.transform, 1f, -1, ai.GetComponent<Character>().GetZDOID());
			ZNetScene.instance.Destroy(Pile.gameObject);
		}
		string anim = hammer.m_shared.m_attack?.m_attackAnimation;
		ai.GetComponent<ZSyncAnimation>()?.SetTrigger(!string.IsNullOrEmpty(anim) ? anim : "swing_hammer");
		_state = 1;
		_downAt = now;
		_start = now;
		return 0;
	}

	private bool InReach(Vector3 feet, out Vector3 aim)
	{
		Vector3 eye = feet + Vector3.up * MHR_Chests.EyeHeight;
		aim = Pile.transform.position + Vector3.up * 0.4f;
		float best = float.MaxValue;
		foreach (Collider col in Pile.GetComponentsInChildren<Collider>())
		{
			if (col == null || !col.enabled || col.isTrigger)
			{
				continue;
			}
			Vector3 p = col.ClosestPoint(eye);
			float d = Vector3.Distance(eye, p);
			if (d < best)
			{
				best = d;
				aim = p;
			}
		}
		return best <= Reach;
	}

	private int Collect(CompanionAI ai, Humanoid hum, float now)
	{
		if (now - _start > 30f)
		{
			return 1;
		}
		if (now - _downAt < 0.6f)
		{
			return 0;
		}
		Inventory bag = hum.GetInventory();
		Vector3 me = ai.transform.position;
		ItemDrop nearest = null;
		float nd = float.MaxValue;
		foreach (Collider c in Physics.OverlapSphere(_at, 4f))
		{
			if (c == null)
			{
				continue;
			}
			ItemDrop d = c.GetComponentInParent<ItemDrop>();
			if (d != null && d.m_itemData != null && d.m_itemData.m_shared.m_name == Shared && !_before.Contains(d.GetInstanceID()) && bag.CanAddItem(d.m_itemData, 1))
			{
				float dist = Vector3.Distance(me, d.transform.position);
				if (dist < nd)
				{
					nd = dist;
					nearest = d;
				}
				continue;
			}
			Container box = c.GetComponentInParent<Container>();
			if (box != null && box.GetComponent<Piece>() == null && box.GetComponentInParent<Character>() == null && box.GetInventory() != null && box.GetInventory().CountItems(Shared) > 0 && Vector3.Distance(me, box.transform.position) < 2.5f)
			{
				ZNetView bnv = box.GetComponent<ZNetView>();
				if (bnv != null && !bnv.IsOwner())
				{
					bnv.ClaimOwnership();
				}
				ai.GetComponent<ZSyncAnimation>()?.SetTrigger("interact");
				foreach (ItemDrop.ItemData it in new List<ItemDrop.ItemData>(box.GetInventory().GetAllItems()))
				{
					if (it.m_shared.m_name != Shared)
					{
						continue;
					}
					int n = it.m_stack;
					while (n > 0 && !bag.CanAddItem(it, n))
					{
						n--;
					}
					if (n <= 0)
					{
						break;
					}
					ItemDrop.ItemData copy = it.Clone();
					copy.m_stack = n;
					if (bag.AddItem(copy))
					{
						box.GetInventory().RemoveItem(it, n);
						Got += n;
					}
				}
				return 0;
			}
		}
		if (nearest == null)
		{
			return 1;
		}
		if (nd > 1.5f)
		{
			MHR_Nav.Move(ai, Time.deltaTime, nearest.transform.position, 1.0f, false, _tag);
			return 0;
		}
		if (!nearest.CanPickup())
		{
			nearest.RequestOwn();
			ai.StopMoving();
			return 0;
		}
		int stack = nearest.m_itemData.m_stack;
		if (hum.Pickup(nearest.gameObject, autoequip: false, autoPickupDelay: false))
		{
			Got += stack;
		}
		return 0;
	}

	public void PutAway(Humanoid hum, CompanionSetup cs)
	{
		if (!_hammerOut || hum == null)
		{
			return;
		}
		_hammerOut = false;
		Inventory inv = hum.GetInventory();
		if (inv != null)
		{
			foreach (ItemDrop.ItemData it in inv.GetAllItems())
			{
				if (it?.m_shared?.m_buildPieces != null && hum.IsItemEquiped(it))
				{
					hum.UnequipItem(it);
				}
			}
		}
		if (cs != null && cs.SuppressAutoEquip)
		{
			cs.SuppressAutoEquip = false;
			cs.SyncEquipmentToInventory();
		}
	}
}
