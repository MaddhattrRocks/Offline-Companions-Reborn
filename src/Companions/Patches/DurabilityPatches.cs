using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace Companions;

public static class DurabilityPatches
{
	// 1.5.98 D1-D3 (John: use the game's code): a companion's weapon wears down by the game's own code, as a player's
	// does. Attack.DoMeleeAttack (a swing that hit something), DoAreaAttack (an area swing that hit something),
	// ProjectileAttackTriggered (each shot) and DoNonAttack (each use) drain m_useDurabilityDrain (area: 1) x
	// Game.m_durabilityRate - only "if IsPlayer()". That one check reads as true for a companion too; the rest is the
	// game's. (The old per-swing drain on Humanoid.StartAttack - misses included, no durability rate - is gone.) When the
	// game's drain takes it to 0, the companion's broken-tool handling runs as before.
	[HarmonyPatch]
	private static class WeaponDurability_Patch
	{
		private static readonly FieldInfo s_useDurability = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), nameof(ItemDrop.ItemData.SharedData.m_useDurability));

		private static readonly MethodInfo s_isPlayer = AccessTools.Method(typeof(Character), nameof(Character.IsPlayer));

		private static readonly FieldInfo s_weapon = AccessTools.Field(typeof(Attack), "m_weapon");

		private static readonly FieldInfo s_character = AccessTools.Field(typeof(Attack), "m_character");

		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Attack), "DoMeleeAttack");
			yield return AccessTools.Method(typeof(Attack), "DoAreaAttack");
			yield return AccessTools.Method(typeof(Attack), "ProjectileAttackTriggered");
			yield return AccessTools.Method(typeof(Attack), "DoNonAttack");
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
		{
			int replaced = 0;
			bool afterDurability = false;
			foreach (CodeInstruction ci in instructions)
			{
				if (ci.opcode == OpCodes.Ldfld && Equals(ci.operand, s_useDurability))
				{
					afterDurability = true;
				}
				else if (afterDurability && (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call) && Equals(ci.operand, s_isPlayer))
				{
					// the IsPlayer() right after "m_useDurability &&": the same stack (a Character in, a bool out)
					CodeInstruction call = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WeaponDurability_Patch), nameof(DrainsLikePlayer)));
					call.labels.AddRange(ci.labels);
					call.blocks.AddRange(ci.blocks);
					yield return call;
					afterDurability = false;
					replaced++;
					continue;
				}
				yield return ci;
			}
			if (replaced == 0)
			{
				CompanionsPlugin.Log?.LogWarning("[Durability] " + __originalMethod?.Name + ": the game's weapon wear check wasn't found - companions' weapons won't wear there");
			}
		}

		internal static bool DrainsLikePlayer(Character c)
		{
			return c != null && (c.IsPlayer() || c.GetComponent<CompanionSetup>() != null);
		}

		private static void Prefix(Attack __instance, out float __state)
		{
			ItemDrop.ItemData w = s_weapon?.GetValue(__instance) as ItemDrop.ItemData;
			__state = w != null ? w.m_durability : -1f;
		}

		private static void Postfix(Attack __instance, float __state)
		{
			Humanoid h = s_character?.GetValue(__instance) as Humanoid;
			ItemDrop.ItemData itemData = s_weapon?.GetValue(__instance) as ItemDrop.ItemData;
			if (h == null || itemData == null || __state < 0f || h.GetComponent<CompanionSetup>() == null || itemData.m_durability >= __state)
			{
				return;
			}
			float maxDurability = itemData.GetMaxDurability();
			float num = ((maxDurability > 0f) ? (itemData.m_durability / maxDurability * 100f) : 0f);
			if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Durability] Weapon drain (the game's) — \"" + itemData.m_shared.m_name + "\" " + $"durability {__state:F1} → {itemData.m_durability:F1} / {maxDurability:F0} " + $"({num:F0}%) " + "companion=\"" + h.m_name + "\"");
			if (itemData.m_durability <= 0f && __state > 0f)
			{
				itemData.m_durability = 0f;
				if (h.IsItemEquiped(itemData))
				{
					h.UnequipItem(itemData, triggerEquipEffects: false);
				}
				// 1.5.8: items the game destroys when they break (torches) go, as for a player (Humanoid.DrainEquipedItemDurability)
				if (itemData.m_shared.m_destroyBroken)
				{
					h.GetInventory()?.RemoveItem(itemData);
				}
				MHR_Equip.OnToolBroke(h);
				CompanionsPlugin.Log.LogWarning("[Durability] Weapon BROKEN — \"" + itemData.m_shared.m_name + "\" unequipped, companion=\"" + h.m_name + "\"");
			}
		}
	}

	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	private static class ArmorDurability_Patch
	{
		private static readonly List<ItemDrop.ItemData> _armorPieces = new List<ItemDrop.ItemData>(4);

		private static void Prefix(Character __instance, HitData hit)
		{
			CompanionSetup component = __instance.GetComponent<CompanionSetup>();
			if (!(component == null))
			{
				float totalArmor = component.GetTotalArmor();
				if (totalArmor > 0f)
				{
					float totalDamage = hit.GetTotalDamage();
					hit.ApplyArmor(totalArmor);
					float totalDamage2 = hit.GetTotalDamage();
					if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug($"[Durability] Armor reduction — totalArmor={totalArmor:F1} " + $"dmg {totalDamage:F1} → {totalDamage2:F1} " + $"(blocked {totalDamage - totalDamage2:F1}) " + "companion=\"" + __instance.m_name + "\"");
				}
			}
		}

		private static void Postfix(Character __instance, HitData hit)
		{
			CompanionSetup component = __instance.GetComponent<CompanionSetup>();
			if (component == null)
			{
				return;
			}
			Humanoid humanoid = __instance as Humanoid;
			if (humanoid == null)
			{
				return;
			}
			_armorPieces.Clear();
			AddIfValid(_armorPieces, _chestItem?.GetValue(humanoid) as ItemDrop.ItemData);
			AddIfValid(_armorPieces, _legItem?.GetValue(humanoid) as ItemDrop.ItemData);
			AddIfValid(_armorPieces, _helmetItem?.GetValue(humanoid) as ItemDrop.ItemData);
			AddIfValid(_armorPieces, _shoulderItem?.GetValue(humanoid) as ItemDrop.ItemData);
			if (_armorPieces.Count == 0)
			{
				if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Durability] No armor with durability equipped — skipping drain companion=\"" + __instance.m_name + "\"");
				return;
			}
			float num = hit.GetTotalPhysicalDamage() + hit.GetTotalElementalDamage();
			if (!(num <= 0f))
			{
				ItemDrop.ItemData itemData = _armorPieces[Random.Range(0, _armorPieces.Count)];
				float durability = itemData.m_durability;
				itemData.m_durability = Mathf.Max(0f, itemData.m_durability - num);
				float maxDurability = itemData.GetMaxDurability();
				float num2 = ((maxDurability > 0f) ? (itemData.m_durability / maxDurability * 100f) : 0f);
				if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Durability] Armor drain — \"" + itemData.m_shared.m_name + "\" " + $"durability {durability:F1} → {itemData.m_durability:F1} / {maxDurability:F0} " + $"({num2:F0}%) dmgTaken={num:F1} " + $"armorPieces={_armorPieces.Count} companion=\"{__instance.m_name}\"");
				if (itemData.m_durability <= 0f)
				{
					humanoid.UnequipItem(itemData, triggerEquipEffects: false);
					CompanionsPlugin.Log.LogWarning("[Durability] Armor BROKEN — \"" + itemData.m_shared.m_name + "\" unequipped, companion=\"" + __instance.m_name + "\"");
				}
			}
		}
	}

	private static readonly FieldInfo _chestItem = AccessTools.Field(typeof(Humanoid), "m_chestItem");

	private static readonly FieldInfo _legItem = AccessTools.Field(typeof(Humanoid), "m_legItem");

	private static readonly FieldInfo _helmetItem = AccessTools.Field(typeof(Humanoid), "m_helmetItem");

	private static readonly FieldInfo _shoulderItem = AccessTools.Field(typeof(Humanoid), "m_shoulderItem");

	private static void AddIfValid(List<ItemDrop.ItemData> list, ItemDrop.ItemData item)
	{
		if (item != null && item.m_shared != null && item.m_shared.m_useDurability)
		{
			list.Add(item);
		}
	}
}
