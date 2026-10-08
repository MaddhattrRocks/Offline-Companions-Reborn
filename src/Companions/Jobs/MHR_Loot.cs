using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace Companions
{
    // ---- 1.4.94: after a companion kills something it goes over and picks up what the creature dropped, like a player
    // (user: "Right now if the NPC kills something it just goes on its way"). Only the kill's own drops: anything that
    // was already lying there is left alone. Drops that come out of the body a few seconds later (ragdolls) are waited for.
    public static class MHR_Loot
    {
        class Kill
        {
            public Vector3 Pos; public float Time; public string Name;
            public HashSet<int> Before = new HashSet<int>();
            public bool SawDrop; public float LastDrop; public float NextBodyCheck; public bool Body;
        }
        class State { public Kill Current; public readonly List<Kill> Queue = new List<Kill>(); public ItemDrop Target; public float TargetSince; public float NextLog; }

        static readonly Dictionary<int, State> s_state = new Dictionary<int, State>();
        static readonly Collider[] s_buf = new Collider[128];
        static int s_mask = -1;

        static int Mask() { if (s_mask == -1) s_mask = LayerMask.GetMask("item"); return s_mask; }
        static float Radius() { return ModConfig.LootRadius != null ? ModConfig.LootRadius.Value : 6f; }
        static float Wait() { return ModConfig.LootWait != null ? ModConfig.LootWait.Value : 10f; }
        static float MaxDist() { return ModConfig.LootMaxDistance != null ? ModConfig.LootMaxDistance.Value : 30f; }

        public static bool Active(Component c)
        {
            CompanionAI ai = c != null ? c.GetComponent<CompanionAI>() : null;
            State st;
            return ai != null && s_state.TryGetValue(ai.GetInstanceID(), out st) && st.Current != null;
        }

        // called when a creature this companion was after dies (its last hit, or Hunt's prey)
        public static void OnKill(CompanionSetup cs, Character victim)
        {
            if (cs == null || victim == null || victim.IsPlayer() || victim.GetComponent<CompanionSetup>() != null) return;
            if (ModConfig.LootKills != null && !ModConfig.LootKills.Value) return;
            CompanionAI ai = cs.GetComponent<CompanionAI>();
            if (ai == null) return;
            int id = ai.GetInstanceID();
            State st;
            if (!s_state.TryGetValue(id, out st)) { st = new State(); s_state[id] = st; }
            Vector3 pos = victim.transform.position;
            if (st.Current != null && Vector3.Distance(st.Current.Pos, pos) < 1f) return;
            for (int i = 0; i < st.Queue.Count; i++) if (Vector3.Distance(st.Queue[i].Pos, pos) < 1f) return;
            Kill k = new Kill { Pos = pos, Time = Time.time, Name = victim.m_name };
            int n = Physics.OverlapSphereNonAlloc(pos, Radius() + 2f, s_buf, Mask());
            for (int i = 0; i < n && i < s_buf.Length; i++)
            {
                ItemDrop d = DropOf(s_buf[i]);
                if (d != null) k.Before.Add(d.GetInstanceID());
            }
            if (st.Queue.Count > 6) st.Queue.RemoveAt(0);
            st.Queue.Add(k);
        }

        // CompanionAI.UpdateAI, after combat: true while it's walking over to pick things up
        public static bool Drive(CompanionAI ai, float dt)
        {
            if (ai == null) return false;
            State st;
            if (!s_state.TryGetValue(ai.GetInstanceID(), out st)) return false;
            if (st.Current == null)
            {
                if (st.Queue.Count == 0) return false;
                st.Current = st.Queue[0]; st.Queue.RemoveAt(0); st.Target = null;
                st.Current.Time = Mathf.Max(st.Current.Time, Time.time - 2f);
                Log("Going to pick up what " + Loc(st.Current.Name) + " dropped (" + Vector3.Distance(ai.transform.position, st.Current.Pos).ToString("F0") + " m away)");
            }
            Kill k = st.Current;
            CompanionSetup cs = ai.GetComponent<CompanionSetup>();
            Humanoid hum = ai.GetComponent<Humanoid>();
            Inventory inv = hum != null ? hum.GetInventory() : null;
            ZNetView nv = ai.GetComponent<ZNetView>();
            ZDO zdo = nv != null ? nv.GetZDO() : null;
            string why = null;
            Vector3 me = ai.transform.position;
            if (inv == null || zdo == null) why = "no pack";
            else if (!zdo.GetBool(CompanionSetup.AutoPickupHash, true)) why = "auto pickup is off";
            else if (Vector3.Distance(me, k.Pos) > MaxDist() + 5f) why = "too far away";
            else if (Player.m_localPlayer != null && cs != null && cs.GetFollow() && ai.GetFollowTarget() == Player.m_localPlayer.gameObject &&
                     Vector3.Distance(Player.m_localPlayer.transform.position, k.Pos) > MaxDist()) why = "you've moved on";
            else if (inv.GetTotalWeight() >= CompanionTierData.MaxCarryWeight || inv.GetEmptySlots() == 0 && !RoomForAny(inv, k)) why = "pack is full";
            else if (Time.time - k.Time > Wait() + 15f) why = "took too long";
            if (why != null) { Finish(ai, st, "stopped picking up (" + why + ")"); return false; }

            // the body may still be lying there; its drops come out when it fades
            if (Time.time >= k.NextBodyCheck)
            {
                k.NextBodyCheck = Time.time + 1f;
                k.Body = false;
                try
                {
                    foreach (Ragdoll r in UnityEngine.Object.FindObjectsOfType<Ragdoll>())
                    {
                        if (r == null || !r.m_dropItems) continue;
                        Vector3 bp = r.GetAverageBodyPosition();
                        if (Vector3.Distance(bp, k.Pos) < Radius() + 3f) { k.Body = true; break; }
                    }
                }
                catch { }
            }

            if (st.Target != null)
            {
                ZNetView tv = st.Target.GetComponent<ZNetView>();
                if (tv == null || !tv.IsValid() || Time.time - st.TargetSince > 12f) st.Target = null;
            }
            if (st.Target == null)
            {
                st.Target = NextDrop(k, me, inv);
                st.TargetSince = Time.time;
                if (st.Target != null) { k.SawDrop = true; k.LastDrop = Time.time; }
            }
            if (st.Target != null)
            {
                Vector3 tp = st.Target.transform.position;
                if (Vector3.Distance(me, tp) <= 1.6f)
                {
                    string name = st.Target.m_itemData != null ? st.Target.m_itemData.m_shared.m_name : "?";
                    int count = st.Target.m_itemData != null ? st.Target.m_itemData.m_stack : 0;
                    if (!st.Target.CanPickup()) { st.Target.RequestOwn(); ai.StopMoving(); return true; }
                    st.Target.Load();
                    if (hum.Pickup(st.Target.gameObject, false, false)) LogDebug("  picked up " + Loc(name) + " x" + count);
                    k.LastDrop = Time.time;
                    st.Target = null;
                    return true;
                }
                MHR_Mover.Go(ai, dt, tp, 0.8f, true, "Loot");
                if (MHR_Mover.GaveUp(ai)) { LogDebug("  can't get to " + Loc(st.Target.m_itemData != null ? st.Target.m_itemData.m_shared.m_name : "?") + " - leaving it"); k.Before.Add(st.Target.GetInstanceID()); st.Target = null; MHR_Mover.Release(ai); }
                return true;
            }

            // nothing to pick up right now: done, or wait by the body for its drops
            float age = Time.time - k.Time;
            bool done = (k.SawDrop && Time.time - k.LastDrop > 1.5f && !k.Body) || (!k.SawDrop && !k.Body && age > 3f) || age > Wait();
            if (done) { Finish(ai, st, k.SawDrop ? "picked up everything" : "nothing dropped"); return false; }
            if (Vector3.Distance(me, k.Pos) > 3f) MHR_Mover.Go(ai, dt, k.Pos, 2f, true, "Loot");
            else ai.StopMoving();
            return true;
        }

        static void Finish(CompanionAI ai, State st, string what)
        {
            LogDebug(what);
            st.Current = null; st.Target = null;
            ai.StopMoving();
            MHR_Mover.Release(ai);
            if (st.Queue.Count == 0)
            {
                CompanionSetup cs = ai.GetComponent<CompanionSetup>();
                ZNetView nv = ai.GetComponent<ZNetView>();
                if (cs != null && nv != null && nv.GetZDO() != null) cs.ApplyFollowMode(nv.GetZDO().GetInt(CompanionSetup.ActionModeHash, 0), true);
            }
        }

        static ItemDrop DropOf(Collider col)
        {
            if (col == null || col.attachedRigidbody == null) return null;
            ItemDrop d = col.attachedRigidbody.GetComponent<ItemDrop>();
            if (d == null || !d.m_autoPickup) return null;
            ZNetView v = d.GetComponent<ZNetView>();
            return v != null && v.IsValid() ? d : null;
        }

        static bool RoomForAny(Inventory inv, Kill k)
        {
            int n = Physics.OverlapSphereNonAlloc(k.Pos, Radius(), s_buf, Mask());
            for (int i = 0; i < n && i < s_buf.Length; i++)
            {
                ItemDrop d = DropOf(s_buf[i]);
                if (d != null && !k.Before.Contains(d.GetInstanceID()) && d.m_itemData != null && inv.CanAddItem(d.m_itemData, 1)) return true;
            }
            return false;
        }

        static ItemDrop NextDrop(Kill k, Vector3 me, Inventory inv)
        {
            int n = Physics.OverlapSphereNonAlloc(k.Pos, Radius(), s_buf, Mask());
            ItemDrop best = null; float bd = float.MaxValue;
            for (int i = 0; i < n && i < s_buf.Length; i++)
            {
                ItemDrop d = DropOf(s_buf[i]);
                if (d == null || k.Before.Contains(d.GetInstanceID()) || d.InTar()) continue;
                if (d.m_itemData == null || !inv.CanAddItem(d.m_itemData, 1)) continue;
                if (inv.GetTotalWeight() + d.m_itemData.GetWeight() > CompanionTierData.MaxCarryWeight) continue;
                float dist = Vector3.Distance(me, d.transform.position);
                if (dist < bd) { bd = dist; best = d; }
            }
            return best;
        }

        static string Loc(string s) { try { return Localization.instance != null ? Localization.instance.Localize(s) : s; } catch { return s; } }
        static void Log(string msg) { if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Loot] " + msg); }
        static void LogDebug(string msg) { if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogDebug("[Loot] " + msg); }
    }

    // a creature dies: if a companion landed the last hit, it goes to collect the drops
    [HarmonyPatch(typeof(Character), "OnDeath")]
    public static class MHR_Loot_OnDeath_Patch
    {
        static void Prefix(Character __instance, HitData ___m_lastHit)
        {
            try
            {
                Character attacker = ___m_lastHit != null ? ___m_lastHit.GetAttacker() : null;
                if (attacker == null) return;
                CompanionSetup cs = attacker.GetComponent<CompanionSetup>();
                if (cs == null) return;
                ZNetView nv = cs.GetComponent<ZNetView>();
                if (nv == null || !nv.IsValid() || !nv.IsOwner()) return;
                MHR_Loot.OnKill(cs, __instance);
            }
            catch (Exception e) { if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogWarning("[Loot] " + e.Message); }
        }
    }
}
