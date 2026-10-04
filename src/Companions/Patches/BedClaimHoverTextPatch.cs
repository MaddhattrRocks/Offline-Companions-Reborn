using HarmonyLib;

namespace Companions;

[HarmonyPatch(typeof(Bed), "GetHoverText")]
public static class BedClaimHoverTextPatch
{
	[HarmonyPostfix]
	private static void Postfix(Bed __instance, ref string __result)
	{
		if (CompanionRest.GetBedOwnerSafe(__instance) == 0)
		{
			string value = CompanionRest.MHR_GetBedClaimName(__instance);
			if (value != null || CompanionRest._bedClaimNames.TryGetValue(__instance.GetInstanceID(), out value))
			{
				__result = $"{value}'s Bed";
			}
		}
	}
}
