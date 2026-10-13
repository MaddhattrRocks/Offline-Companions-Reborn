using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.3: a built ladder's AutoJumpLedge calls Character.OnAutoJump every physics step for anyone inside it. OnAutoJump
// stamps its 0.5 s spacing BEFORE checking the walking direction, and at that point a companion's walking direction reads
// as nothing - so the game's own call was refused but still blocked the next half-second, and the mover's bump (which sets
// the direction first) mostly landed inside that blocked window. Result: the first ladder lifted him, ladders 2-5 needed a
// hop (user: "It works just fine when I use it. I walk into the ladder, continue to walk into it and it moves me up").
// For companions the mover does the bump (MHR_Mover.LadderBump, while it is walking into a ladder); the game's call is skipped.
[HarmonyPatch(typeof(AutoJumpLedge), "OnTriggerStay")]
internal static class CompanionLadderBumpPatch
{
	private static bool Prefix(Collider collider)
	{
		try
		{
			if (collider == null)
			{
				return true;
			}
			Character c = collider.GetComponent<Character>();
			if (c == null || c.IsPlayer())
			{
				return true;
			}
			return c.GetComponent<CompanionSetup>() == null;
		}
		catch
		{
			return true;
		}
	}
}
