using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.67: hit register - the game tells the companion's HarvestController when one of its swings landed on a tree, log,
// stump or rock (user, log 9e9a7e57: "Maybe we do need a hit register on the logs. Would that help with the npc figuring
// out where to stand and not wander around so much?"). Every one of these has a public Damage(HitData) called by the
// attacker's swing.
[HarmonyPatch]
internal static class HarvestHitRegister
{
	[HarmonyPatch(typeof(TreeLog), "Damage")]
	[HarmonyPostfix]
	private static void TreeLogHit(TreeLog __instance, HitData hit) => Register(__instance, hit);

	[HarmonyPatch(typeof(TreeBase), "Damage")]
	[HarmonyPostfix]
	private static void TreeBaseHit(TreeBase __instance, HitData hit) => Register(__instance, hit);

	[HarmonyPatch(typeof(Destructible), "Damage")]
	[HarmonyPostfix]
	private static void DestructibleHit(Destructible __instance, HitData hit) => Register(__instance, hit);

	[HarmonyPatch(typeof(MineRock), "Damage")]
	[HarmonyPostfix]
	private static void MineRockHit(MineRock __instance, HitData hit) => Register(__instance, hit);

	[HarmonyPatch(typeof(MineRock5), "Damage")]
	[HarmonyPostfix]
	private static void MineRock5Hit(MineRock5 __instance, HitData hit) => Register(__instance, hit);

	// part 1 L3: your own axe and pickaxe hits on trees, logs, stumps, bushes and loose rocks near a vein, read before
	// the game applies them (the health after is read half a second later)
	[HarmonyPatch(typeof(TreeLog), "Damage")]
	[HarmonyPrefix]
	private static void TreeLogHitBefore(TreeLog __instance, HitData hit) => Before(__instance, hit);

	[HarmonyPatch(typeof(TreeBase), "Damage")]
	[HarmonyPrefix]
	private static void TreeBaseHitBefore(TreeBase __instance, HitData hit) => Before(__instance, hit);

	[HarmonyPatch(typeof(Destructible), "Damage")]
	[HarmonyPrefix]
	private static void DestructibleHitBefore(Destructible __instance, HitData hit) => Before(__instance, hit);

	private static void Before(Component target, HitData hit)
	{
		try
		{
			if (target != null && hit != null && hit.GetAttacker() is Player)
			{
				PlayerCombatLog.NotePlayerObjHit(target, hit);
			}
		}
		catch
		{
		}
	}

	private static void Register(Component target, HitData hit)
	{
		try
		{
			if (target == null || hit == null)
			{
				return;
			}
			Character attacker = hit.GetAttacker();
			if (attacker == null || attacker is Player)
			{
				return;
			}
			attacker.GetComponent<HarvestController>()?.OnHitRegistered(target.gameObject);
		}
		catch
		{
		}
	}
}
