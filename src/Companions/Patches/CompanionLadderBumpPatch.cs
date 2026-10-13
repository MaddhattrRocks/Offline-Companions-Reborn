using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.3: a built ladder's AutoJumpLedge calls Character.OnAutoJump every physics step for anyone inside it. OnAutoJump
// stamps its 0.5 s spacing BEFORE it checks Dot(walking direction, ladder direction) >= 0.5. A companion's walking input
// is often weak or zero at that moment (the game's smooth movement scales it down while turning; anything that stops it
// zeroes it), so the call was refused but still blocked the next half-second - ladders 2-5 needed a hop. Switching the
// ladder off for companions entirely (first try) was worse: nothing lifted him up the last rungs onto the landing.
// Now, for a companion: walking roughly the ladder's way -> its walking input is set to full strength along that heading
// so the game's own bump goes through, every 0.5 s while it walks into the ladder, as for a player; not walking the
// ladder's way -> the call is skipped, so a refused attempt doesn't block the next one.
[HarmonyPatch(typeof(AutoJumpLedge), "OnTriggerStay")]
internal static class CompanionLadderBumpPatch
{
	private static bool Prefix(AutoJumpLedge __instance, Collider collider)
	{
		try
		{
			if (collider == null || __instance == null)
			{
				return true;
			}
			Character c = collider.GetComponent<Character>();
			if (c == null || c.IsPlayer() || c.GetComponent<CompanionSetup>() == null)
			{
				return true;
			}
			Vector3 move = c.GetMoveDir();
			move.y = 0f;
			Vector3 f = __instance.transform.forward;
			f.y = 0f;
			if (move.sqrMagnitude < 0.0025f || f.sqrMagnitude < 0.01f)
			{
				return false;
			}
			move.Normalize();
			if (Vector3.Dot(move, f.normalized) < 0.5f)
			{
				return false;
			}
			c.SetMoveDir(move);
			return true;
		}
		catch
		{
			return true;
		}
	}
}
