using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.4.96: a crouching companion sneaks like a player. Vanilla only gives the Player class a stealth factor and quiet
// crouched footsteps; for every other character IsCrouching() is false and GetStealthFactor() is 1, so a companion
// sneaking (after a deer, or copying you) only looked like it.
public static class CompanionSneak
{
	internal static readonly HashSet<Character> Crouching = new HashSet<Character>();

	[HarmonyPatch(typeof(Character), "IsCrouching")]
	private static class IsCrouching_Patch
	{
		private static void Postfix(Character __instance, ref bool __result)
		{
			if (!__result && Crouching.Count > 0 && Crouching.Contains(__instance))
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Character), "GetStealthFactor")]
	private static class GetStealthFactor_Patch
	{
		private static void Postfix(Character __instance, ref float __result)
		{
			if (Crouching.Count == 0 || !Crouching.Contains(__instance))
			{
				return;
			}
			// same formula as Player.UpdateStealth: darker and more skilled = harder to see
			float light = 1f;
			try
			{
				if (StealthSystem.instance != null)
				{
					light = StealthSystem.instance.GetLightFactor(__instance.GetCenterPoint());
				}
			}
			catch
			{
			}
			float skill = __instance.GetSkillFactor(Skills.SkillType.Sneak);
			__result = Mathf.Clamp01(Mathf.Lerp(0.5f + light * 0.5f, 0.2f + light * 0.4f, skill));
		}
	}
}
