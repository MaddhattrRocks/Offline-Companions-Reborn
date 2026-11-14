using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Companions
{
    // ---- 1.5.89: carts. ----
    // A companion pulling a cart follows straight behind the player on the player's own path (MHR_Follow.CartFollow),
    // far enough back to clear the player's cart when the player pulls one too. While both pull a cart the two carts
    // (and each cart with the other one's puller) don't collide, so they can't snag on each other in a turn.
    // 1.5.91: (user: "The npcs run into it all the time. They should not run into the cart. They also seem to stop pulling the
    // cart randomly.") A cart that the player or a companion is pulling is a "party cart" for as long as it's pulled: every
    // companion passes through it (its puller too, so the cart rolling on when it stops can't shove it off the shafts), and
    // the follow spots keep clear of it (InCartWay). The game lets go of a cart when the puller is more than m_detachDistance
    // from the shafts, dodges, teleports, the cart tips over or the hitch snaps under a hard jolt (Vagon.Update/CanAttach);
    // a bump from another companion or a side-step did it. When it comes loose without anyone using the cart, the
    // companion hitches back on (up to 3 times a minute) and the log says why it came loose.
    public static class MHR_Cart
    {
        class C { public Vagon Cart; public float NextScan; }
        class P { public Character Player; public Vagon Mine, Theirs; }
        class A { public Vagon Cart; public float Seen; public Bounds Box; public int BoxFrame = -1; public string By; public readonly Dictionary<int, Collider> Ignored = new Dictionary<int, Collider>(); }

        static readonly Dictionary<int, C> s_pulled = new Dictionary<int, C>();
        static readonly Dictionary<int, P> s_apart = new Dictionary<int, P>();
        static readonly Dictionary<int, Collider[]> s_cols = new Dictionary<int, Collider[]>();
        static readonly Collider[] s_hits = new Collider[32];
        static int s_mask = -1;
        static readonly Dictionary<int, A> s_active = new Dictionary<int, A>();
        static readonly List<int> s_drop = new List<int>();
        static float s_nextSync;
        static readonly Dictionary<int, float> s_usedAt = new Dictionary<int, float>();       // cart -> last Vagon.Interact
        static readonly Dictionary<int, List<float>> s_rehitch = new Dictionary<int, List<float>>();
        static FieldInfo s_detachField;
        static MethodInfo s_anyAttached;
        static bool s_reflected;

        static int Mask()
        {
            if (s_mask == -1) s_mask = LayerMask.GetMask("vehicle", "piece", "Default", "Default_small");
            return s_mask;
        }

        static bool Attached(Vagon v, Character ch)
        {
            try { return v != null && ch != null && v.IsAttached(ch); }
            catch { return false; }
        }

        // the cart this character is pulling, or null (looked for around it every 0.5 s while it pulls none)
        public static Vagon Pulled(Character ch)
        {
            if (ch == null) return null;
            int id = ch.gameObject.GetInstanceID();
            C c;
            if (!s_pulled.TryGetValue(id, out c)) { c = new C(); s_pulled[id] = c; }
            if (c.Cart != null && Attached(c.Cart, ch)) { Seen(c.Cart, ch); return c.Cart; }
            c.Cart = null;
            float now = Time.time;
            if (now < c.NextScan) return null;
            c.NextScan = now + 0.5f;
            int n = Physics.OverlapSphereNonAlloc(ch.transform.position, 4f, s_hits, Mask(), QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider h = s_hits[i];
                if (h == null) continue;
                Vagon v = h.GetComponentInParent<Vagon>();
                if (v != null && Attached(v, ch)) { c.Cart = v; Seen(v, ch); return v; }
            }
            return null;
        }

        static Collider[] Cols(Vagon v)
        {
            int id = v.GetInstanceID();
            Collider[] cols;
            if (!s_cols.TryGetValue(id, out cols) || cols == null)
            {
                List<Collider> list = new List<Collider>();
                foreach (Collider col in v.GetComponentsInChildren<Collider>(true))
                    if (col != null && !col.isTrigger) list.Add(col);
                cols = list.ToArray();
                s_cols[id] = cols;
            }
            return cols;
        }

        // how far the cart reaches behind the one pulling it (flat), from its colliders; 1.5 - 8 m
        public static float Reach(Vagon v, Vector3 puller)
        {
            float best = 0f;
            foreach (Collider col in Cols(v))
            {
                if (col == null || !col.enabled) continue;
                Bounds b = col.bounds;
                Vector2 d = new Vector2(b.center.x - puller.x, b.center.z - puller.z);
                float r = d.magnitude + Mathf.Max(b.extents.x, b.extents.z);
                if (r > best) best = r;
            }
            return Mathf.Clamp(best, 1.5f, 8f);
        }

        // keep the companion's cart and the player's cart apart in the physics (or put it back once either lets go)
        public static void Apart(Character comp, Vagon mine, Character player, Vagon theirs)
        {
            if (comp == null) return;
            int id = comp.gameObject.GetInstanceID();
            P p;
            s_apart.TryGetValue(id, out p);
            if (p != null && p.Mine == mine && p.Theirs == theirs && p.Player == player) return;
            if (p != null) { Ignore(p, false); s_apart.Remove(id); }
            if (mine == null || theirs == null || player == null || mine == theirs) return;
            p = new P { Player = player, Mine = mine, Theirs = theirs };
            Ignore(p, true);
            s_apart[id] = p;
            if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + comp.m_name + " and the player both pull a cart - the carts pass through each other");
        }

        static void Ignore(P p, bool ignore)
        {
            if (p.Mine == null || p.Theirs == null) return;   // a cart was destroyed: its pairs went with it
            Collider[] a = Cols(p.Mine), b = Cols(p.Theirs);
            Collider cb = p.Player != null ? p.Player.GetComponent<Collider>() : null;
            foreach (Collider x in a)
            {
                if (x == null) continue;
                foreach (Collider y in b) if (y != null) Physics.IgnoreCollision(x, y, ignore);
                if (cb != null) Physics.IgnoreCollision(x, cb, ignore);
            }
            // (the companion and the player's cart: both carts are party carts, every companion passes through them)
        }

        // ---------- 1.5.91: party carts ----------
        // a character is pulling this cart (from Pulled, so only while a companion follows someone)
        static void Seen(Vagon v, Character by)
        {
            int id = v.GetInstanceID();
            A a;
            if (!s_active.TryGetValue(id, out a)) { a = new A { Cart = v }; s_active[id] = a; }
            a.Seen = Time.time; a.By = by != null ? by.m_name : "?";
            Sync();
        }

        // twice a second: every companion passes through every party cart; a cart nobody has pulled for 1.5 s is solid again
        static void Sync()
        {
            float now = Time.time;
            if (now < s_nextSync) return;
            s_nextSync = now + 0.5f;
            s_drop.Clear();
            foreach (KeyValuePair<int, A> kv in s_active)
            {
                A a = kv.Value;
                if (a.Cart == null || now - a.Seen > 1.5f) { s_drop.Add(kv.Key); continue; }
                Collider[] cols = Cols(a.Cart);
                int added = 0;
                foreach (CompanionSetup cs in CompanionSetup.AllCompanions)
                {
                    if (cs == null) continue;
                    int cid = cs.gameObject.GetInstanceID();
                    if (a.Ignored.ContainsKey(cid)) continue;
                    Collider me = cs.GetComponent<Collider>();
                    if (me == null) continue;
                    foreach (Collider x in cols) if (x != null) Physics.IgnoreCollision(me, x, true);
                    a.Ignored[cid] = me;
                    added++;
                }
                if (added > 0 && CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + added + " companion(s) pass through the cart " + a.By + " is pulling");
            }
            foreach (int id in s_drop)
            {
                A a = s_active[id];
                s_active.Remove(id);
                if (a.Cart == null) continue;   // destroyed: its pairs went with it
                Collider[] cols = Cols(a.Cart);
                foreach (Collider me in a.Ignored.Values)
                    if (me != null) foreach (Collider x in cols) if (x != null) Physics.IgnoreCollision(me, x, false);
                if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] nobody pulls the cart " + a.By + " had - solid again");
            }
        }

        // a spot inside (or within pad of) a cart the party is pulling - not somewhere to stand
        public static bool InCartWay(Vector3 g, float pad)
        {
            if (s_active.Count == 0) return false;
            float now = Time.time;
            foreach (A a in s_active.Values)
            {
                if (a.Cart == null || now - a.Seen > 1.5f) continue;
                if (a.BoxFrame != Time.frameCount)
                {
                    a.BoxFrame = Time.frameCount;
                    bool any = false;
                    foreach (Collider col in Cols(a.Cart))
                    {
                        if (col == null || !col.enabled) continue;
                        if (!any) { a.Box = col.bounds; any = true; } else a.Box.Encapsulate(col.bounds);
                    }
                    if (!any) a.Box = new Bounds(a.Cart.transform.position, Vector3.one * 2f);
                }
                Bounds b = a.Box;
                if (g.x > b.min.x - pad && g.x < b.max.x + pad && g.z > b.min.z - pad && g.z < b.max.z + pad && g.y > b.min.y - 2f && g.y < b.max.y + 1f) return true;
            }
            return false;
        }

        // pulling a cart right now (no search: only the cart Pulled last found)
        public static bool Pulling(Character ch)
        {
            if (ch == null) return false;
            C c;
            return s_pulled.TryGetValue(ch.gameObject.GetInstanceID(), out c) && c.Cart != null && Attached(c.Cart, ch);
        }

        // Vagon.Interact: someone used the cart (the release order, the player taking it) - that let-go was meant
        internal static void NoteUsed(Vagon v)
        {
            if (v == null) return;
            if (s_usedAt.Count > 200) s_usedAt.Clear();
            s_usedAt[v.GetInstanceID()] = Time.time;
        }
        static bool UsedLately(Vagon v)
        {
            float at;
            return v != null && s_usedAt.TryGetValue(v.GetInstanceID(), out at) && Time.time - at < 2f;
        }

        static void Reflect()
        {
            if (s_reflected) return;
            s_reflected = true;
            try { s_detachField = AccessTools.Field(typeof(Vagon), "m_detachDistance"); } catch { }
            try { s_anyAttached = AccessTools.Method(typeof(Vagon), "IsAttached", Type.EmptyTypes); } catch { }
        }
        static float DetachDistance(Vagon v)
        {
            Reflect();
            try { if (s_detachField != null) return (float)s_detachField.GetValue(v); } catch { }
            return 2f;
        }
        static bool AnyoneAttached(Vagon v)
        {
            Reflect();
            try { if (s_anyAttached != null) return (bool)s_anyAttached.Invoke(v, null); } catch { }
            return false;
        }
        static float FromShafts(Vagon v, Character ch)
        {
            return Vector3.Distance(ch.transform.position + v.m_attachOffset, v.m_attachPoint.position);
        }

        // why the game let go of the cart (the same checks as Vagon.CanAttach, after the fact)
        public static string WhyLoose(Vagon v, Character ch)
        {
            if (v == null) return "the cart is gone";
            if (UsedLately(v)) return "the cart was used";
            if (v.transform.up.y < 0.1f) return "the cart tipped over";
            Humanoid h = ch as Humanoid;
            if (h != null && h.InDodge()) return "it dodged";
            if (ch is Player pl && pl.IsTeleporting()) return "it teleported";
            float d = FromShafts(v, ch), max = DetachDistance(v);
            if (d >= max - 0.1f) return "it got " + d.ToString("F1") + " m from the shafts (the game lets go past " + max.ToString("F1") + " m)";
            return "the hitch snapped under a hard jolt";
        }

        // the cart came loose without an order: walk back to the shafts and take it again (CompanionAI's cart order)
        public static bool TryRehitch(CompanionAI ai, Character ch, Vagon v, string why)
        {
            if (ai == null || ch == null || v == null || UsedLately(v)) return false;
            if (v.transform.up.y < 0.1f || AnyoneAttached(v)) return false;
            Humanoid h = ch as Humanoid;
            CompanionSetup setup = ai.GetComponent<CompanionSetup>();
            if (h == null || setup == null || !setup.GetFollow() || FromShafts(v, ch) > 6f) return false;
            int id = ch.gameObject.GetInstanceID();
            float now = Time.time;
            List<float> tries;
            if (!s_rehitch.TryGetValue(id, out tries)) { tries = new List<float>(); s_rehitch[id] = tries; }
            tries.RemoveAll(t => now - t > 60f);
            if (tries.Count >= 3)
            {
                if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Cart] " + ch.m_name + "'s cart came loose again (" + why + ") - leaving it after 3 tries this minute");
                return false;
            }
            tries.Add(now);
            ai.SetPendingCart(v, h);
            if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Cart] " + ch.m_name + "'s cart came loose (" + why + ") - hitching back on");
            return true;
        }
    }

    // 1.5.91: anyone using a cart (the release order, a player taking it over) - so that let-go isn't undone
    [HarmonyPatch(typeof(Vagon), nameof(Vagon.Interact))]
    static class MHR_CartUsePatch
    {
        static void Prefix(Vagon __instance) { MHR_Cart.NoteUsed(__instance); }
    }
}
