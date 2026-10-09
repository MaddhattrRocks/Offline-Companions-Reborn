using System;
using HarmonyLib;
using UnityEngine;

namespace Companions
{
    // ---- 1.4.106: companions fish with the game's own float and fish (user: "it should fish for real"). The float
    // only knows players: it finds its owner in the player list by the caster's user id - for a companion that's the
    // player, so the float looked for a rod on the player and destroyed itself (and could even replace the player's own
    // float). The float now remembers the companion that cast it. ----
    public static class MHR_FishingFloat
    {
        internal const string OwnerHash = "HC_FloatOwner";

        internal static readonly AccessTools.FieldRef<FishingFloat, Fish> Nibbler = AccessTools.FieldRefAccess<FishingFloat, Fish>("m_nibbler");
        internal static readonly AccessTools.FieldRef<FishingFloat, float> NibbleTime = AccessTools.FieldRefAccess<FishingFloat, float>("m_nibbleTime");
        internal static readonly AccessTools.FieldRef<FishingFloat, float> LineLength = AccessTools.FieldRefAccess<FishingFloat, float>("m_lineLength");
        internal static readonly AccessTools.FieldRef<FishingFloat, ZNetView> NView = AccessTools.FieldRefAccess<FishingFloat, ZNetView>("m_nview");

        // the float this companion cast, if it's out
        public static FishingFloat Of(Character c)
        {
            if (c == null) return null;
            ZDOID me = c.GetZDOID();
            foreach (FishingFloat f in FishingFloat.GetAllInstances())
            {
                if (f == null) continue;
                ZNetView nv = NView(f);
                if (nv == null || !nv.IsValid()) continue;
                if (nv.GetZDO().GetZDOID(OwnerHash) == me) return f;
            }
            return null;
        }

        internal static Character CompanionOwner(FishingFloat f)
        {
            ZNetView nv = NView(f);
            if (nv == null || !nv.IsValid()) return null;
            ZDOID id = nv.GetZDO().GetZDOID(OwnerHash);
            if (id.IsNone() || ZNetScene.instance == null) return null;
            GameObject go = ZNetScene.instance.FindInstance(id);
            return go != null ? go.GetComponent<Character>() : null;
        }

        [HarmonyPatch(typeof(FishingFloat), "Setup")]
        static class Setup_Patch
        {
            static void Prefix(FishingFloat __instance, Character owner)
            {
                try
                {
                    if (owner == null || owner.GetComponent<CompanionSetup>() == null) return;
                    ZNetView nv = NView(__instance);
                    if (nv != null && nv.IsValid()) nv.GetZDO().Set(OwnerHash, owner.GetZDOID());
                }
                catch (Exception e) { CompanionsPlugin.Log?.LogDebug("[Fish] float setup: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(FishingFloat), "GetOwner")]
        static class GetOwner_Patch
        {
            static bool Prefix(FishingFloat __instance, ref Character __result)
            {
                try
                {
                    ZNetView nv = NView(__instance);
                    if (nv == null || !nv.IsValid() || nv.GetZDO().GetZDOID(OwnerHash).IsNone()) return true;
                    __result = CompanionOwner(__instance);
                    return false;
                }
                catch { return true; }
            }
        }

        // a cast that caught nothing gives the bait back - vanilla only does that for players
        [HarmonyPatch(typeof(FishingFloat), "ReturnBait")]
        static class ReturnBait_Patch
        {
            static bool Prefix(FishingFloat __instance, bool ___m_baitConsumed)
            {
                try
                {
                    Character owner = CompanionOwner(__instance);
                    if (owner == null) return true;
                    if (!___m_baitConsumed)
                    {
                        Humanoid h = owner as Humanoid;
                        GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(__instance.GetBait()) : null;
                        if (h != null && prefab != null) h.GetInventory().AddItem(prefab, 1);
                    }
                    return false;
                }
                catch { return true; }
            }
        }
    }
}
