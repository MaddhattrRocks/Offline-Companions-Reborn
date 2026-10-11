using HarmonyLib;

namespace Companions;

[HarmonyPatch(typeof(Bed), "GetHoverText")]
public static class BedClaimHoverTextPatch
{
	[HarmonyPostfix]
	private static void Postfix(Bed __instance, ref string __result)
	{
		long bedOwnerSafe = CompanionRest.GetBedOwnerSafe(__instance);
		if (bedOwnerSafe != 0)
		{
			// 1.5.1: a bed of yours a companion sleeps in - not once it's your spawn point (the companion gives it up then)
			string text = CompanionRest.MHR_GetBedClaimName(__instance);
			if (text != null && Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == bedOwnerSafe && !__instance.IsCurrent())
			{
				__result = __result + "\n(" + text + " sleeps here)";
			}
		}
		else
		{
			string value = CompanionRest.MHR_GetBedClaimName(__instance);
			if (value != null || CompanionRest._bedClaimNames.TryGetValue(__instance.GetInstanceID(), out value))
			{
				__result = $"{value}'s Bed";
			}
		}
	}
}
