using HarmonyLib;

namespace Companions;

// 1.5.1: setting your spawn point (sleeping in a bed of yours) makes companions re-check their bed right away - a
// companion doesn't sleep in its owner's spawn bed
[HarmonyPatch(typeof(PlayerProfile), "SetCustomSpawnPoint")]
public static class SpawnPointBedPatch
{
	[HarmonyPostfix]
	private static void Postfix()
	{
		CompanionRest.MHR_RecheckBedsSoon();
	}
}
