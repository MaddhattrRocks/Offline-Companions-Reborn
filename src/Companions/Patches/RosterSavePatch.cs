using HarmonyLib;

namespace Companions;

// 1.5.1: whenever the game saves your character (autosave, logging out, quitting), the companion roster is brought up to
// date first, so it holds their gear as it was when you left
[HarmonyPatch(typeof(Game), "SavePlayerProfile")]
public static class RosterSavePatch
{
	[HarmonyPrefix]
	private static void Prefix()
	{
		MHR_Roster.OnProfileSave();
	}
}
