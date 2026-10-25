using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.73: a tick latched on is shaken off with a dodge roll, like a player (user, log 93298113: "Tick sucked NPC blood and
// NPC did nothing?" - it latched on and he spent 8 s "stepping back to get its breath back" while it fed; a latched tick
// moves with you). The game (Attack.UpdateAttach) only lets go when the one it's on dies or is dodge-invincible - a
// player's roll - and it sits at your back or side, out of a forward swing. Companions are Humanoids, whose InDodge and
// IsDodgeInvincible are always false, so the roll is the player's own: the "dodge" animation (its root motion moves him
// while InDodge is true), the stamina, and the invincible start of it, which is what makes the tick let go.
internal static class MHR_TickShake
{
	private static readonly AccessTools.FieldRef<Humanoid, Attack> s_currentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");

	private static readonly AccessTools.FieldRef<Attack, bool> s_isAttached = AccessTools.FieldRefAccess<Attack, bool>("m_isAttached");

	private static readonly AccessTools.FieldRef<Attack, Transform> s_attachTarget = AccessTools.FieldRefAccess<Attack, Transform>("m_attachTarget");

	private class S
	{
		public float NextCheck;
		public float RollUntil;
		public float InvincibleUntil;
		public float NextRoll;
		public float NextLog;
	}

	private static readonly Dictionary<int, S> s_s = new Dictionary<int, S>();

	// a dodge roll lasts about as long as the player's animation; the tick is checked for dodge-invincibility every 0.3 s
	private const float RollTime = 0.75f;

	private const float InvincibleTime = 0.45f;

	private static S Get(Character c)
	{
		int id = c.GetInstanceID();
		if (!s_s.TryGetValue(id, out S s))
		{
			if (s_s.Count > 200)
			{
				s_s.Clear();
			}
			s = new S();
			s_s[id] = s;
		}
		return s;
	}

	// a creature latched on to c (a tick feeding), or null
	internal static Character LatchedOn(Character c)
	{
		if (c == null)
		{
			return null;
		}
		Vector3 me = c.transform.position;
		foreach (Character o in Character.GetAllCharacters())
		{
			if (o == null || o == c || o.IsDead() || !(o is Humanoid h) || (o.transform.position - me).sqrMagnitude > 16f)
			{
				continue;
			}
			Attack a = s_currentAttack(h);
			if (a != null && s_isAttached(a) && s_attachTarget(a) == c.transform)
			{
				return o;
			}
		}
		return null;
	}

	internal static bool Rolling(Character c)
	{
		return c != null && s_s.TryGetValue(c.GetInstanceID(), out S s) && Time.time < s.RollUntil;
	}

	// every frame from CompanionCombatAI.Update: something latched on -> roll, when it can (on the ground, not mid-swing,
	// not staggered, the stamina for it)
	internal static void Tick(Humanoid h, CompanionStamina stam)
	{
		if (h == null || !(ModConfig.CombatShakeOffTicks?.Value ?? true))
		{
			return;
		}
		S s = Get(h);
		float now = Time.time;
		if (now < s.NextCheck || now < s.RollUntil)
		{
			return;
		}
		s.NextCheck = now + 0.25f;
		Character tick = LatchedOn(h);
		if (tick == null || now < s.NextRoll)
		{
			return;
		}
		if (h.IsDead() || !h.IsOnGround() || h.InAttack() || h.IsStaggering() || h.IsSwimming() || h.IsAttached())
		{
			return;
		}
		float cost = ModConfig.CombatDodgeStamina?.Value ?? 10f;
		if (stam != null && stam.Stamina < cost)
		{
			Log(s, "[Tactics] " + tick.m_name + " latched on - too tired to roll it off, hitting it instead");
			return;
		}
		Vector3 dir = PickDir(h, tick);
		if (dir == Vector3.zero)
		{
			Log(s, "[Tactics] " + tick.m_name + " latched on - no safe way to roll (a drop, water, fire or spikes), hitting it instead");
			return;
		}
		if (stam != null)
		{
			stam.UseStamina(cost);
		}
		h.transform.rotation = Quaternion.LookRotation(dir);
		h.GetComponent<ZSyncAnimation>()?.SetTrigger("dodge");
		s.RollUntil = now + RollTime;
		s.InvincibleUntil = now + InvincibleTime;
		s.NextRoll = now + 1.2f;
		CompanionsPlugin.Log.LogDebug("[Tactics] " + tick.m_name + " latched on - dodge roll to shake it off");
	}

	// a roll is about 3 m: forward first (away from a tick at its back), then the sides, then back - the first one safe
	private static Vector3 PickDir(Character c, Character tick)
	{
		Vector3 away = c.transform.position - tick.transform.position;
		away.y = 0f;
		if (away.sqrMagnitude < 0.01f)
		{
			away = c.transform.forward;
		}
		away.Normalize();
		for (int k = 0; k < 8; k++)
		{
			float ang = (k % 2 == 0 ? 1 : -1) * ((k + 1) / 2) * 45f;
			Vector3 d = Quaternion.Euler(0f, ang, 0f) * away;
			if (MHR_Mover.SafeStep(c.transform.position, d, 3f))
			{
				return d;
			}
		}
		return Vector3.zero;
	}

	private static void Log(S s, string msg)
	{
		if (Time.time >= s.NextLog)
		{
			s.NextLog = Time.time + 3f;
			CompanionsPlugin.Log.LogDebug(msg);
		}
	}

	[HarmonyPatch(typeof(Character), "InDodge")]
	private static class InDodgePatch
	{
		private static void Postfix(Character __instance, ref bool __result)
		{
			if (!__result && s_s.Count > 0 && !(__instance is Player) && s_s.TryGetValue(__instance.GetInstanceID(), out S s) && Time.time < s.RollUntil)
			{
				__result = true;
			}
		}
	}

	[HarmonyPatch(typeof(Character), "IsDodgeInvincible")]
	private static class InvinciblePatch
	{
		private static void Postfix(Character __instance, ref bool __result)
		{
			if (!__result && s_s.Count > 0 && !(__instance is Player) && s_s.TryGetValue(__instance.GetInstanceID(), out S s) && Time.time < s.InvincibleUntil)
			{
				__result = true;
			}
		}
	}
}
