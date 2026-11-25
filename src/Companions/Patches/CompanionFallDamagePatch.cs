using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.2: companions take fall damage like a player (user: "They should take fall damage just like a player, and try to
// avoid falling that far if they can"). The game only hurts players (Character.UpdateGroundContact: IsPlayer() && fall
// > 4 m -> (fall - 4) / 16 x 100 damage, after status effects like Feather Fall). Same rule, same numbers, for companions.
[HarmonyPatch(typeof(Character), "UpdateGroundContact")]
internal static class CompanionFallDamagePatch
{
	private static readonly AccessTools.FieldRef<Character, bool> s_groundContact = AccessTools.FieldRefAccess<Character, bool>("m_groundContact");

	private static readonly AccessTools.FieldRef<Character, float> s_maxAirAltitude = AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");

	// last position per companion: a jump of several meters between two physics steps is a warp (portal, catching up with
	// the player, a respawn), not a fall - the highest point is reset then so it isn't counted as one
	private static readonly System.Collections.Generic.Dictionary<int, Vector3> s_lastPos = new System.Collections.Generic.Dictionary<int, Vector3>();

	// Y2: the height of the ground it last stood on, per companion - a landing more than a step lower is told to its job
	// (measured from the ground it left, not the top of a jump, so a hop back onto the same level isn't a fall)
	private static readonly System.Collections.Generic.Dictionary<int, float> s_lastGroundY = new System.Collections.Generic.Dictionary<int, float>();

	private static readonly System.Collections.Generic.HashSet<int> s_fallSaid = new System.Collections.Generic.HashSet<int>();

	private const float FallSayAt = 2.5f;

	private static void Prefix(Character __instance, out float __state)
	{
		__state = 0f;
		try
		{
			if (__instance.IsPlayer() || __instance.GetComponent<CompanionSetup>() == null)
			{
				return;
			}
			int instanceID = __instance.GetInstanceID();
			Vector3 position = __instance.transform.position;
			if (s_lastPos.TryGetValue(instanceID, out var value) && (position - value).sqrMagnitude > 36f)
			{
				s_maxAirAltitude(__instance) = position.y;
				s_lastGroundY.Remove(instanceID);   // Y2: a warp isn't a fall
			}
			s_lastPos[instanceID] = position;
			if (s_groundContact(__instance))
			{
				__state = Mathf.Max(0f, s_maxAirAltitude(__instance) - position.y);
				s_fallSaid.Remove(instanceID);
				if (s_lastGroundY.TryGetValue(instanceID, out float lastY) && lastY - position.y > 0.7f)
				{
					__instance.GetComponent<HarvestController>()?.OnFell(lastY - position.y, position.y);
				}
				s_lastGroundY[instanceID] = position.y;
			}
			else if (CompanionsPlugin.LogDetailed && s_maxAirAltitude(__instance) - position.y > FallSayAt && !__instance.InLiquid() && s_fallSaid.Add(instanceID))
			{
				// trench test F2 (log of 11-17: five falls into the trench with nothing in the log to say how): one line when a
				// fall passes FallSayAt - where from, and what it was doing
				CompanionAI ai = __instance.GetComponent<CompanionAI>();
				HarvestController hc = __instance.GetComponent<HarvestController>();
				CompanionsPlugin.Log.LogDebug($"[Move] \"{__instance.m_name}\" is falling: {s_maxAirAltitude(__instance) - position.y:F1} m down so far from the height {s_maxAirAltitude(__instance):F1}, now at {MHR_Mover.Fmt(position)}; mover: {(ai != null ? MHR_Mover.Describe(ai) : "none")}{(hc != null ? "; job: " + hc.FallNote() : "")}");
			}
		}
		catch
		{
		}
	}

	private static void Postfix(Character __instance, float __state)
	{
		if (__state <= 4f)
		{
			return;
		}
		try
		{
			ZNetView component = __instance.GetComponent<ZNetView>();
			if (component == null || !component.IsOwner() || __instance.IsDead() || __instance.InLiquid())
			{
				return;
			}
			float damage = Mathf.Clamp01((__state - 4f) / 16f) * 100f;
			__instance.GetSEMan()?.ModifyFallDamage(damage, ref damage);
			if (damage > 0f)
			{
				HitData hitData = new HitData();
				hitData.m_damage.m_damage = damage;
				hitData.m_point = __instance.transform.position;
				hitData.m_dir = Vector3.up;
				hitData.m_hitType = HitData.HitType.Fall;
				__instance.Damage(hitData);
				if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo($"[Move] \"{__instance.m_name}\" fell {__state:F1} m and took {damage:F0} fall damage");
			}
		}
		catch
		{
		}
	}

	// how far it can drop for no more than `share` of its current health (and never below 4 m, which is free)
	internal static float SafeDrop(Character c, float share)
	{
		if (c == null)
		{
			return 4f;
		}
		return 4f + 16f * Mathf.Clamp01(c.GetHealth() * share / 100f);
	}
}
