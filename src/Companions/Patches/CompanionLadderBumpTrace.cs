using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.3: ladder trace (user: "We need to look at the ladders more closely. It was still wonky."). Records every OnAutoJump
// call on a companion - who made it (the game's ladder trigger or the mover), whether the game accepted it and, if not, why -
// so MHR_Mover's chain trace can print what happened rung by rung. Debug only; nothing here changes behaviour.
[HarmonyPatch(typeof(Character), "OnAutoJump")]
internal static class CompanionLadderBumpTrace
{
	private static readonly AccessTools.FieldRef<Character, float> s_lastAutoJump = AccessTools.FieldRefAccess<Character, float>("m_lastAutoJumpTime");

	internal static bool FromMover;   // set by MHR_Mover around its own call

	internal class Counts
	{
		public int Game, GameOk, Mover, MoverOk;
		public string LastRefusal = "";
	}

	internal static readonly System.Collections.Generic.Dictionary<int, Counts> s_counts = new System.Collections.Generic.Dictionary<int, Counts>();

	internal static Counts Take(Character c)
	{
		Counts k;
		int id = c.GetInstanceID();
		if (!s_counts.TryGetValue(id, out k)) return null;
		s_counts.Remove(id);
		return k;
	}

	private static void Prefix(Character __instance, Vector3 dir, out string __state)
	{
		__state = null;
		try
		{
			if (__instance == null || __instance.IsPlayer() || __instance.GetComponent<CompanionSetup>() == null) return;
			// the game's own checks, in its order, to say why a call does nothing
			if (!__instance.IsOnGround()) __state = "not on the ground";
			else if (__instance.InAttack()) __state = "mid-swing";
			else if (__instance.InDodge()) __state = "dodging";
			else if (__instance.IsKnockedBack()) __state = "knocked back";
			else if (Time.time - s_lastAutoJump(__instance) < 0.5f) __state = "within 0.5 s of the last one";
			else if (Vector3.Dot(__instance.GetMoveDir(), dir) < 0.5f) __state = "walking-direction mismatch " + Vector3.Dot(__instance.GetMoveDir(), dir).ToString("F2");
			else __state = "";
		}
		catch { __state = null; }
	}

	private static void Postfix(Character __instance, string __state)
	{
		if (__state == null) return;
		try
		{
			int id = __instance.GetInstanceID();
			Counts k;
			if (!s_counts.TryGetValue(id, out k)) { k = new Counts(); s_counts[id] = k; }
			bool ok = __state.Length == 0;
			if (FromMover) { k.Mover++; if (ok) k.MoverOk++; }
			else { k.Game++; if (ok) k.GameOk++; }
			if (!ok) k.LastRefusal = (FromMover ? "mine: " : "game: ") + __state;
		}
		catch { }
	}
}
