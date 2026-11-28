using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.96: the Ore job in a dungeon (muddy scrap piles in sunken crypts) - no digging there (John's approved list):
//  - It mines while it has room and it is safe: not fighting, and no enemy that has spotted it or you within 20 m. It
//    goes back to mining 5 s after that.
//  - Over its limit (the Gathering > OverweightThreshold point where it stops picking things up) or with a full bag: it
//    stops mining and puts just enough ore in a chest within 20 m to get back under (John: "A dungeon you are trying to
//    take it all with you back to a base"). With no chest, it keeps carrying it and follows you.
//  - Overweight it walks slowly and its stamina drains like a player's (at 0 it can't move, 1.5.62). Only when it is
//    stuck at 0 stamina and you are more than 10 m away by walking route (no route counts as away) for 5 s does it drop
//    just enough ore to get under its limit (the player's own drop). It never leaves ore on the ground otherwise.
//  - Out of the dungeon still over its limit with ore from it: if there's a chest within 15 m of the entrance with a
//    workbench or portal within 5 m of it, it puts just enough ore in there to get under, then follows you again. Back in the dungeon it mines again.
public partial class HarvestController
{
	private float _dgSafeAt;

	private bool _dgWasUnsafe;

	private bool _dgCarrying;

	private bool _dgHasEntrance;

	private Vector3 _dgEntrance;

	private float _dgNextEntrance;

	private float _dgAwaySince = -1f;

	private float _dgNextAwayCheck;

	private bool _dgAway;

	private bool _dgLoggedFull;

	// called each frame in the Ore job; true = the job waits this frame
	private bool DungeonGate()
	{
		if (_character == null || _ai == null || _humanoid == null)
		{
			return false;
		}
		bool inside = InDungeonAt(base.transform.position);
		if (!inside)
		{
			return DungeonAfterExit();
		}
		if (Time.time >= _dgNextEntrance)
		{
			_dgNextEntrance = Time.time + 10f;
			FindEntrance();
		}
		// safe to mine?
		if (DungeonUnsafe())
		{
			if (!_dgWasUnsafe)
			{
				_dgWasUnsafe = true;
				if (CompanionsPlugin.LogNormal) LogInfo("Enemies about - mining paused");
				if (_state != HarvestState.Idle) ResetToIdle();
				RestoreLoadout();
			}
			_dgSafeAt = Time.time + 5f;
			return true;
		}
		if (Time.time < _dgSafeAt)
		{
			return true;
		}
		if (_dgWasUnsafe)
		{
			_dgWasUnsafe = false;
			if (CompanionsPlugin.LogNormal) LogInfo("All quiet again - back to mining");
		}
		bool heavy = IsOverweight();
		if (HasOre())
		{
			_dgCarrying = true;
		}
		if (!heavy)
		{
			_dgLoggedFull = false;
			_dgAwaySince = -1f;
			return false;
		}
		if (_ai.PendingDepositContainer != null)
		{
			return true;
		}
		Container c = MHR_GatherRules.DropOff(this, base.transform.position, 20f);
		if (c != null && HasOre())
		{
			// John: in a dungeon only enough to get under its carry capacity - the rest goes back to base with it
			float cap = Mathf.Min(OverweightThreshold, CompanionTierData.CarryLimit(_character)) - 1f;
			if (CompanionsPlugin.LogNormal) LogInfo("Full - putting just enough ore in \"" + c.m_name + "\" " + Vector3.Distance(base.transform.position, c.transform.position).ToString("F0") + "m away to get under its limit");
			ResetToIdle();
			RestoreLoadout();
			_ai.SetPendingDeposit(c, _humanoid, MHR_GatherRules.IsOreItem, cap);
			return true;
		}
		if (!_dgLoggedFull)
		{
			_dgLoggedFull = true;
			if (CompanionsPlugin.LogNormal) LogInfo("Full and no chest nearby - stopping mining and following you with the ore");
			if (_state != HarvestState.Idle) ResetToIdle();
			RestoreLoadout();
			GameObject owner = MHR_Owner.Go(this);
			if (owner != null) _ai.SetFollowTarget(owner);
		}
		DungeonStuckDrop();
		return true;
	}

	private bool HasOre()
	{
		Inventory inv = _humanoid?.GetInventory();
		if (inv == null) return false;
		foreach (ItemDrop.ItemData it in inv.GetAllItems())
		{
			if (MHR_GatherRules.IsOreItem(it)) return true;
		}
		return false;
	}

	// fighting, or an enemy within 20 m that has spotted it or you
	private bool DungeonUnsafe()
	{
		if (_ai.IsInCombat)
		{
			return true;
		}
		Player owner = MHR_Owner.Of(this);
		Vector3 me = base.transform.position;
		List<Character> all = Character.GetAllCharacters();
		for (int i = 0; i < all.Count; i++)
		{
			Character c = all[i];
			if (c == null || c == _character || c.IsDead() || c.IsPlayer() || c.IsTamed())
			{
				continue;
			}
			if (Vector3.Distance(me, c.transform.position) > 20f || !BaseAI.IsEnemy(_character, c))
			{
				continue;
			}
			BaseAI bai = c.GetBaseAI();
			if (bai == null)
			{
				continue;
			}
			Character t = bai.GetTargetCreature();
			if (bai.IsAlerted() || t == _character || (owner != null && t == owner))
			{
				return true;
			}
		}
		return false;
	}

	// the dungeon's way out: the door nearest to it that leads outside
	private void FindEntrance()
	{
		Teleport best = null;
		float bd = float.MaxValue;
		foreach (Teleport t in Object.FindObjectsByType<Teleport>(FindObjectsSortMode.None))
		{
			if (t == null || t.m_targetPoint == null || InDungeonAt(t.m_targetPoint.transform.position))
			{
				continue;
			}
			float d = Vector3.Distance(base.transform.position, t.transform.position);
			if (d < bd)
			{
				bd = d;
				best = t;
			}
		}
		if (best != null)
		{
			_dgEntrance = best.m_targetPoint.transform.position;
			_dgHasEntrance = true;
		}
	}

	// overweight, out of stamina and you've gone on (more than 10 m by walking route, for 5 s): just enough ore out to
	// get under its limit
	private void DungeonStuckDrop()
	{
		Player owner = MHR_Owner.Of(this);
		if (_stamina == null || _stamina.Stamina > 0f || owner == null)
		{
			_dgAwaySince = -1f;
			return;
		}
		if (Time.time >= _dgNextAwayCheck)
		{
			_dgNextAwayCheck = Time.time + 0.5f;
			_dgAway = !MHR_Mover.WalkRoute(base.transform.position, owner.transform.position, out float len) || len > 10f;
		}
		if (!_dgAway)
		{
			_dgAwaySince = -1f;
			return;
		}
		if (_dgAwaySince < 0f)
		{
			_dgAwaySince = Time.time;
			return;
		}
		if (Time.time - _dgAwaySince < 5f)
		{
			return;
		}
		_dgAwaySince = -1f;
		Inventory inv = _humanoid.GetInventory();
		float limit = CompanionTierData.CarryLimit(_character);
		float over = inv.GetTotalWeight() - limit;
		if (over <= 0f)
		{
			return;
		}
		List<ItemDrop.ItemData> ores = new List<ItemDrop.ItemData>();
		foreach (ItemDrop.ItemData it in inv.GetAllItems())
		{
			if (MHR_GatherRules.IsOreItem(it)) ores.Add(it);
		}
		ores.Sort((a, b) => b.m_shared.m_weight.CompareTo(a.m_shared.m_weight));
		int dropped = 0;
		for (int i = 0; i < ores.Count && over > 0f; i++)
		{
			ItemDrop.ItemData it = ores[i];
			float w = Mathf.Max(0.01f, it.m_shared.m_weight);
			int n = Mathf.Min(it.m_stack, Mathf.CeilToInt(over / w));
			if (n <= 0) continue;
			if (MHR_GatherRules.PlayerDrop(_humanoid, it, n))
			{
				over -= n * w;
				dropped += n;
			}
		}
		if (dropped > 0 && CompanionsPlugin.LogNormal) LogInfo($"Stuck under the weight with you gone on - dropped {dropped} ore to get moving");
	}

	// out of the dungeon with its ore: the chest by the entrance (with a workbench or portal by it), then follow you
	private bool DungeonAfterExit()
	{
		if (!_dgCarrying)
		{
			return false;
		}
		if (!HasOre() || !IsOverweight() || !_dgHasEntrance || Vector3.Distance(base.transform.position, _dgEntrance) > 40f)
		{
			_dgCarrying = false;
			return false;
		}
		if (_ai.PendingDepositContainer != null)
		{
			return true;
		}
		_dgCarrying = false;
		Container c = EntranceChest();
		if (c == null)
		{
			if (CompanionsPlugin.LogDetailed) Log("Out of the dungeon - no chest with a workbench or portal by the entrance, keeping the ore");
			return false;
		}
		float cap = Mathf.Min(OverweightThreshold, CompanionTierData.CarryLimit(_character)) - 1f;
		if (CompanionsPlugin.LogNormal) LogInfo("Out of the dungeon - putting just enough ore in \"" + c.m_name + "\" by the entrance to get under its limit");
		ResetToIdle();
		RestoreLoadout();
		_ai.SetPendingDeposit(c, _humanoid, MHR_GatherRules.IsOreItem, cap);
		return true;
	}

	private Container EntranceChest()
	{
		Container best = null;
		float bd = float.MaxValue;
		foreach (Container c in MHR_Chests.Near(_dgEntrance, 15f))
		{
			if (c == null || Vector3.Distance(c.transform.position, _dgEntrance) > 15f)
			{
				continue;
			}
			if (!MHR_GatherRules.UsableChest(this, c))
			{
				continue;
			}
			if (!HasStationOrPortal(c.transform.position, 5f))
			{
				continue;
			}
			float d = Vector3.Distance(base.transform.position, c.transform.position);
			if (d < bd)
			{
				bd = d;
				best = c;
			}
		}
		return best;
	}

	private static bool HasStationOrPortal(Vector3 p, float r)
	{
		Collider[] cols = Physics.OverlapSphere(p, r, LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid"), QueryTriggerInteraction.Collide);
		foreach (Collider col in cols)
		{
			if (col == null) continue;
			if (col.GetComponentInParent<CraftingStation>() != null || col.GetComponentInParent<TeleportWorld>() != null)
			{
				return true;
			}
		}
		return false;
	}
}
