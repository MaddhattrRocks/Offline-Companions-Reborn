using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Companions
{
    // ---- 1.5.98 (W1, W1b, W5): Ashlands water burns ----
    // User report: "They seem to love the water in Ashlands. Companion died at the edge, resurrect and it runs straight down
    // the edge for 5 meters or so right along the water and dies." The game (Character.UpdateAshlandsWater /
    // UpdateHeatDamage): any water where WorldGenerator.GetAshlandsOceanGradient > 0 builds "ocean heat" while the character is
    // in it - m_heatBuildupWater (2) x gradient (0 at the border, 1 at 300 m in) x (1 - gear heat resistance) a second,
    // x m_heatWaterTouchMultiplier (0.2) when wading instead of swimming. It only cools out of the water (1 a second). At
    // m_heatLevelFirstDamageThreshold (0.7) it hits for 10 every 0.2 s (x (1 - gear)). No debuff: the hits stop once out.
    public static class MHR_Heat
    {
        static readonly AccessTools.FieldRef<Character, float> s_oceanHeat = AccessTools.FieldRefAccess<Character, float>("m_ashlandsOceanHeatLevel");

        // the game's own depth-into-the-Ashlands factor, 0..1 (0 or less = the water there doesn't burn)
        public static float Gradient(Vector3 p)
        {
            try { return Mathf.Clamp01(WorldGenerator.GetAshlandsOceanGradient(p)); } catch { return 0f; }
        }

        // water there that burns (any depth, wading counts too - Character.InWater is any liquid depth > 0). Standing on
        // something built over it (a dock) is dry: the depth is measured from what is under the feet (MHR_Water.DepthAt).
        public static bool HotWaterAt(Vector3 p)
        {
            if (Gradient(p) <= 0f) return false;
            return MHR_Water.DepthAt(p) > 0.05f;
        }

        // W5: the gear's heat resistance (the game's own call - the patch below makes it count a companion's gear)
        public static float Resist(Character c)
        {
            try { return c != null ? c.GetEquipmentHeatResistanceModifier() : 0f; } catch { return 0f; }
        }

        // 100 % or more (or a fire-proof character): the water can't hurt it - ordinary water for this one
        public static bool HeatProof(Character c)
        {
            return c != null && (c.m_tolerateFire || Resist(c) >= 1f);
        }

        // heat gained a second at p (the game's formula)
        public static float Rate(Character c, Vector3 p, bool swimming)
        {
            if (c == null) return 0f;
            float g = Gradient(p);
            if (g <= 0f) return 0f;
            float r = g * c.m_heatBuildupWater * Mathf.Max(0f, 1f - Resist(c));
            if (!swimming) r *= c.m_heatWaterTouchMultiplier;
            return r;
        }

        public static float Level(Character c)
        {
            try { return c != null ? s_oceanHeat(c) : 0f; } catch { return 0f; }
        }

        public static float Threshold(Character c)
        {
            return c != null ? c.m_heatLevelFirstDamageThreshold : 0.7f;
        }

        // W1: the highest ocean heat it would reach following pos -> path corners (sampled every metre, up to maxLen),
        // starting from the heat it has now. Swims at its swim speed, wades and walks at walking speed (the slower, so the
        // estimate errs on the hot side), cools out of the water like the game.
        public static float RouteHeatPeak(Character c, Vector3 pos, List<Vector3> path, float maxLen)
        {
            float heat = Level(c), peak = heat, walked = 0f;
            if (c == null || path == null) return peak;
            float swimLim = MHR_Water.SwimLimit(c);
            float swimSpeed = c.m_swimSpeed > 0.1f ? c.m_swimSpeed : 2f;
            float walkSpeed = c.m_walkSpeed > 0.1f ? c.m_walkSpeed : 1.6f;
            Vector3 prev = pos;
            for (int i = 0; i < path.Count && walked < maxLen; i++)
            {
                Vector3 seg = path[i] - prev;
                float len = new Vector2(seg.x, seg.z).magnitude;
                for (float d = 0.5f; d < len && walked < maxLen; d += 1f, walked += 1f)
                {
                    Vector3 p = prev + seg * (d / len);
                    float depth = MHR_Water.DepthAt(p);
                    if (depth > 0.05f)
                    {
                        bool swim = depth > swimLim;
                        heat += Rate(c, p, swim) / (swim ? swimSpeed : walkSpeed);
                    }
                    else
                    {
                        heat = Mathf.Max(0f, heat - c.m_heatCooldownBase / walkSpeed);
                    }
                    if (heat > peak) peak = heat;
                }
                prev = path[i];
            }
            return peak;
        }

        // W1: a route through hot water is only taken when it stays under 70 % of the heat of the first hit
        public const float RouteMargin = 0.7f;

        // W1b: after getting out of hot water it won't take a route through it again for a minute (no in-out-in)
        static readonly Dictionary<int, float> s_noGoUntil = new Dictionary<int, float>();
        public static bool NoGo(Character c)
        {
            float t;
            return c != null && s_noGoUntil.TryGetValue(c.GetInstanceID(), out t) && Time.time < t;
        }
        public static void SetNoGo(Character c, float seconds)
        {
            if (c != null) s_noGoUntil[c.GetInstanceID()] = Time.time + seconds;
        }

        // W5: the sum of every worn item's heat resistance, the same as the player's (Player.UpdateEquipmentModifiers adds
        // the right/left hand, chest, legs, helmet, shoulder, utility and trinket items). Read again every half second.
        static readonly Dictionary<int, KeyValuePair<float, float>> s_gear = new Dictionary<int, KeyValuePair<float, float>>();
        public static float GearResist(Humanoid h)
        {
            int id = h.GetInstanceID();
            KeyValuePair<float, float> cached;
            float now = Time.time;
            if (s_gear.TryGetValue(id, out cached) && now - cached.Key < 0.5f) return cached.Value;
            float sum = 0f;
            try
            {
                Inventory inv = h.GetInventory();
                if (inv != null)
                    foreach (ItemDrop.ItemData it in inv.GetEquippedItems())
                        if (it != null && it.m_shared != null) sum += it.m_shared.m_heatResistanceModifier;
            }
            catch { }
            s_gear[id] = new KeyValuePair<float, float>(now, sum);
            return sum;
        }
    }

    // W5: companions are a Humanoid, not a Player, and only Player counts the gear's heat resistance (Character returns 0),
    // so heat gear did nothing for them. Same sum as the player's.
    [HarmonyPatch(typeof(Character), nameof(Character.GetEquipmentHeatResistanceModifier))]
    internal static class CompanionHeatResistPatch
    {
        private static void Postfix(Character __instance, ref float __result)
        {
            try
            {
                if (__instance == null || __instance.IsPlayer() || !(__instance.GetBaseAI() is CompanionAI)) return;
                Humanoid h = __instance as Humanoid;
                if (h != null) __result = MHR_Heat.GearResist(h);
            }
            catch { }
        }
    }
}
