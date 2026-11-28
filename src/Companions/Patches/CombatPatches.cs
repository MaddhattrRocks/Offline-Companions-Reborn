using HarmonyLib;
using UnityEngine;

namespace Companions;

public static class CombatPatches
{
	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	private static class RPC_Damage_NoBackstab
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (hit != null && !(hit.m_backstabBonus <= 1f) && !(__instance.GetComponent<CompanionSetup>() == null))
			{
				hit.m_backstabBonus = 1f;
			}
		}
	}

	[HarmonyPatch(typeof(Character), "GetMaxHealth")]
	private static class GetMaxHealth_Patch
	{
		private static void Postfix(Character __instance, ref float __result)
		{
			BaseAI baseAI = __instance.GetBaseAI();
			if (baseAI == null || !(baseAI is CompanionAI))
			{
				return;
			}
			CompanionFood component = __instance.GetComponent<CompanionFood>();
			if (!(component == null))
			{
				float baseHealth = CompanionFood.BaseHealth;
				float totalHealthBonus = component.TotalHealthBonus;
				float num = baseHealth + totalHealthBonus;
				if (Time.time - _lastHealthLogTime > 5f)
				{
					_lastHealthLogTime = Time.time;
					if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug($"[Combat] GetMaxHealth — base={baseHealth:F0} + food={totalHealthBonus:F1} " + $"= {num:F1} (vanilla was {__result:F1}) " + "companion=\"" + __instance.m_name + "\"");
				}
				__result = num;
			}
		}
	}

	[HarmonyPatch(typeof(BaseAI), "CanUseAttack")]
	public static class ToolCombatExclude
	{
		private static void Postfix(BaseAI __instance, ItemDrop.ItemData item, ref bool __result)
		{
			if (__result && __instance is CompanionAI)
			{
				if (item.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod)
				{
					__result = false;
				}
				else if (item.GetDamage().m_pickaxe > 0f)
				{
					__result = false;
				}
				else if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool)
				{
					__result = false;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Humanoid), "EquipBestWeapon")]
	private static class EquipBestWeapon_PreferHighestDamage
	{
		[HarmonyPrefix]
		private static bool Prefix(Humanoid __instance)
		{
			return WeaponLogic.BalancedPrefix(__instance);
		}

		[HarmonyPostfix]
		private static void Postfix(Humanoid __instance)
		{
			if (WeaponLogic.IsManagedStance(__instance))
			{
				return;
			}
			BaseAI baseAI = __instance.GetBaseAI();
			if (baseAI == null || !(baseAI is CompanionAI))
			{
				return;
			}
			ItemDrop.ItemData currentWeapon = __instance.GetCurrentWeapon();
			if (currentWeapon == null)
			{
				return;
			}
			Character target = ((!baseAI.HaveTarget()) ? null : baseAI.GetTargetCreature());
			float effectiveCombatDamage = GetEffectiveCombatDamage(currentWeapon, target);
			Inventory inventory = __instance.GetInventory();
			if (inventory == null)
			{
				return;
			}
			ItemDrop.ItemData itemData = currentWeapon;
			float num = effectiveCombatDamage;
			foreach (ItemDrop.ItemData allItem in inventory.GetAllItems())
			{
				if (allItem != null && allItem.m_shared != null && allItem.IsWeapon() && baseAI.CanUseAttack(allItem) && allItem.m_shared.m_aiTargetType == ItemDrop.ItemData.AiTarget.Enemy && (!allItem.m_shared.m_useDurability || !(allItem.m_durability <= 0f)))
				{
					float effectiveCombatDamage2 = GetEffectiveCombatDamage(allItem, target);
					if (effectiveCombatDamage2 > num)
					{
						itemData = allItem;
						num = effectiveCombatDamage2;
					}
				}
			}
			if (itemData != currentWeapon && num > effectiveCombatDamage * WeaponLogic.SwitchFactor())
			{
				__instance.EquipItem(itemData);
			}
		}

		private static float GetEffectiveCombatDamage(ItemDrop.ItemData item, Character target)
		{
			return WeaponLogic.Score(item, target);
		}

		private static float ModifierToMultiplier(HitData.DamageModifier mod)
		{
			return mod switch
			{
				HitData.DamageModifier.Normal => 1f, 
				HitData.DamageModifier.Resistant => 0.5f, 
				HitData.DamageModifier.Weak => 1.5f, 
				HitData.DamageModifier.Immune => 0f, 
				HitData.DamageModifier.Ignore => 0f, 
				HitData.DamageModifier.VeryResistant => 0.25f, 
				HitData.DamageModifier.VeryWeak => 2f, 
				HitData.DamageModifier.SlightlyResistant => 0.75f, 
				HitData.DamageModifier.SlightlyWeak => 1.25f, 
				_ => 1f, 
			};
		}

		private static float GetCombatDamage(ItemDrop.ItemData item)
		{
			return WeaponLogic.Score(item, null);
		}
	}

	// 1.5.98 W1a (John: "use the pickaxe code and the hit code that the game uses"): a companion's swing does what the
	// game does - its damage on whatever it hits. The original mod's overrides (Destructible / MineRock / MineRock5 /
	// TreeBase / TreeLog Damage replaced by the job's own damage on its target) are gone. Only the player's own hits on a
	// vein are still noted for the Ore job's log.
	[HarmonyPatch(typeof(MineRock5), "Damage")]
	private static class MineRock5_PlayerHitLog
	{
		private static void Prefix(HitData hit, out bool __state)
		{
			// pop test D1: the player's own hits on a vein, logged beside a companion on the Ore job
			__state = hit != null && hit.GetAttacker() != null && PlayerCombatLog.NotePlayerRockHit(hit);
		}

		private static void Postfix(HitData hit, bool __state)
		{
			if (__state)
			{
				PlayerCombatLog.NotePlayerRockBroke(hit);
			}
		}
	}

	[HarmonyPatch(typeof(Attack), "SpawnOnHitTerrain")]
	private static class SpawnOnHitTerrain_Note
	{
		// 1.5.98 W1b: a companion's pickaxe digs whatever ground its swing hits, under the game's own rules, as a player's
		// does (the 1.4.150 block and the 1.5.95 "only near the planned point" gate are gone). The Ore job is told a dig
		// happened (V2); the player's own digs are logged beside a companion on the Ore job.
		private static void Prefix(Vector3 hitPoint, Character character)
		{
			if (character == null || character.IsPlayer() || character.GetComponent<CompanionSetup>() == null)
			{
				PlayerCombatLog.NotePlayerDig(character, hitPoint);
				return;
			}
			character.GetComponent<HarvestController>()?.NotifyDig(hitPoint);
		}
	}

	[HarmonyPatch(typeof(SE_Poison), "AddDamage")]
	private static class SE_Poison_AddDamage_Patch
	{
		private static void Prefix(SE_Poison __instance, ref float __state)
		{
			__state = __instance.m_TTLPerDamage;
			if (__instance.m_character != null && __instance.m_character.GetComponent<CompanionSetup>() != null)
			{
				__instance.m_TTLPerDamage = __instance.m_TTLPerDamagePlayer;
			}
		}

		private static void Postfix(SE_Poison __instance, float __state)
		{
			__instance.m_TTLPerDamage = __state;
		}
	}

	private static float _lastHealthLogTime;

	private const float HealthLogInterval = 5f;
}
