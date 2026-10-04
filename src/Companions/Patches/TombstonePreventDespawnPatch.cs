using HarmonyLib;

namespace Companions;

[HarmonyPatch(typeof(TombStone), "UpdateDespawn")]
public static class TombstonePreventDespawnPatch
{
	[HarmonyPrefix]
	private static bool Prefix(TombStone __instance)
	{
		CompanionTombstoneResurrect component = __instance.GetComponent<CompanionTombstoneResurrect>();
		if (component == null)
		{
			return true;
		}
		return false;
	}
}
