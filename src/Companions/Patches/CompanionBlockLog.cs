using System.Reflection;
using HarmonyLib;

namespace Companions;

// 1.5.51: debug log for each block a companion makes - parried or a plain block, and what got through - to check the
// parry chance in game. Temporary, like the [Attack] lines.
[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
internal static class CompanionBlockLog
{
	private static readonly MethodInfo s_blocker = AccessTools.Method(typeof(Humanoid), "GetCurrentBlocker");

	private static void Prefix(Humanoid __instance, HitData hit, out float __state)
	{
		__state = -1f;
		if (__instance == null || hit == null || __instance.GetComponent<CompanionCombatAI>() == null)
		{
			return;
		}
		__state = hit.GetTotalBlockableDamage();
	}

	private static void Postfix(Humanoid __instance, HitData hit, Character attacker, bool __result, float __state)
	{
		if (__state < 0f || !__result)
		{
			return;
		}
		try
		{
			ItemDrop.ItemData blocker = s_blocker?.Invoke(__instance, null) as ItemDrop.ItemData;
			float timer = ReflectionHelper.GetBlockTimer(__instance);
			bool parry = blocker != null && blocker.m_shared.m_timedBlockBonus > 1f && timer != -1f && timer < 0.25f;
			CompanionsPlugin.Log.LogDebug($"[Block] {(parry ? "Parried" : "Blocked")} {(attacker != null ? attacker.m_name : "a hit")} with {blocker?.m_shared?.m_name ?? "?"}: {__state:F0} damage, {hit.GetTotalBlockableDamage():F0} got through");
		}
		catch
		{
		}
	}
}
