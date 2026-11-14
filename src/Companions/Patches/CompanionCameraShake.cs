using System;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.82: companions' block, parry and weapon-hit effects don't shake your camera (user: "Does the NPC blocking, and
// getting a parry, cause the screen to shake or stutter for the player?"). The game's effects can carry a CamShaker,
// which shakes the camera of anyone within its range whoever caused it (GameCamera.AddShake) - a companion is a copy of
// the player and plays the same block / parry / hit effects. While a companion's block or swing runs, the shakers in the
// effects it creates are switched off before they start (a disabled MonoBehaviour never runs Start); the sound and the
// sparks stay. [Combat] CompanionCameraShake on keeps the game's shake. On Detailed, every camera shake near you is
// logged ([Shake]) so a fight shows which effects shake.
internal static class CompanionCameraShake
{
	private static int s_depth;

	private static float s_nextLog;

	private static bool Suppress => s_depth > 0 && !(ModConfig.CompanionCameraShake?.Value ?? false);

	private static bool IsCompanion(Character c)
	{
		return c != null && c.GetComponent<CompanionSetup>() != null;
	}

	[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
	private static class BlockPatch
	{
		private static void Prefix(Humanoid __instance, out bool __state)
		{
			__state = IsCompanion(__instance);
			if (__state)
			{
				s_depth++;
			}
		}

		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state && s_depth > 0)
			{
				s_depth--;
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
	private static class MeleePatch
	{
		private static void Prefix(Attack __instance, out bool __state)
		{
			__state = IsCompanion(AttackOwner(__instance));
			if (__state)
			{
				s_depth++;
			}
		}

		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state && s_depth > 0)
			{
				s_depth--;
			}
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Attack), "DoAreaAttack")]
	private static class AreaPatch
	{
		private static void Prefix(Attack __instance, out bool __state)
		{
			__state = IsCompanion(AttackOwner(__instance));
			if (__state)
			{
				s_depth++;
			}
		}

		private static Exception Finalizer(Exception __exception, bool __state)
		{
			if (__state && s_depth > 0)
			{
				s_depth--;
			}
			return __exception;
		}
	}

	private static readonly AccessTools.FieldRef<Attack, Humanoid> s_attackOwner = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

	private static Humanoid AttackOwner(Attack a)
	{
		try
		{
			return a != null ? s_attackOwner(a) : null;
		}
		catch
		{
			return null;
		}
	}

	[HarmonyPatch(typeof(EffectList), "Create")]
	private static class EffectPatch
	{
		private static void Postfix(GameObject[] __result)
		{
			if (!Suppress || __result == null)
			{
				return;
			}
			foreach (GameObject go in __result)
			{
				if (go == null)
				{
					continue;
				}
				foreach (CamShaker shaker in go.GetComponentsInChildren<CamShaker>(includeInactive: true))
				{
					if (shaker != null && shaker.enabled)
					{
						shaker.enabled = false;
						if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Shake] no camera shake from a companion's " + go.name + " (strength " + shaker.m_strength.ToString("F2") + ", range " + shaker.m_range.ToString("F0") + " m)");
					}
				}
			}
		}
	}

	// the test line: every shake that reaches your camera, what made it and whether a companion was there
	[HarmonyPatch(typeof(CamShaker), "Start")]
	private static class ShakeLogPatch
	{
		private static void Prefix(CamShaker __instance)
		{
			if (!CompanionsPlugin.LogDetailed || __instance == null || GameCamera.instance == null || Time.time < s_nextLog)
			{
				return;
			}
			Vector3 at = __instance.transform.position;
			float dist = Vector3.Distance(at, GameCamera.instance.transform.position);
			if (dist > __instance.m_range)
			{
				return;
			}
			s_nextLog = Time.time + 0.5f;
			string near = "none";
			float best = 3f;
			foreach (CompanionSetup cs in CompanionSetup.AllCompanions)
			{
				if (cs == null)
				{
					continue;
				}
				float d = Vector3.Distance(cs.transform.position, at);
				if (d < best)
				{
					best = d;
					near = cs.GetComponent<Character>()?.m_name ?? "?";
				}
			}
			CompanionsPlugin.Log.LogDebug("[Shake] camera shake from " + __instance.transform.root.name + " " + dist.ToString("F1") + " m from your camera (strength " + __instance.m_strength.ToString("F2") + ", range " + __instance.m_range.ToString("F0") + " m), companion close by: " + near);
		}
	}
}
