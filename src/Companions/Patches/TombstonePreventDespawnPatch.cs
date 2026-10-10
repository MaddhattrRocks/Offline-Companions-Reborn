using HarmonyLib;

namespace Companions;

[HarmonyPatch(typeof(TombStone), "UpdateDespawn")]
public static class TombstonePreventDespawnPatch
{
	private static readonly System.Reflection.MethodInfo s_floater = AccessTools.Method(typeof(TombStone), "UpdateFloater");

	private static readonly System.Reflection.MethodInfo s_positionCheck = AccessTools.Method(typeof(TombStone), "PositionCheck");

	[HarmonyPrefix]
	private static bool Prefix(TombStone __instance)
	{
		CompanionTombstoneResurrect component = __instance.GetComponent<CompanionTombstoneResurrect>();
		if (component == null)
		{
			return true;
		}
		// 1.4.150: only the "empty -> remove" part is skipped; the game's floating in water and the fall-through-the-
		// ground check still run (a companion's tombstone sank or got lost under the terrain)
		try
		{
			if (__instance.m_floater != null)
			{
				s_floater?.Invoke(__instance, null);
			}
			ZNetView nv = __instance.GetComponent<ZNetView>();
			if (nv != null && nv.IsValid() && nv.IsOwner())
			{
				s_positionCheck?.Invoke(__instance, null);
			}
		}
		catch
		{
		}
		return false;
	}
}
