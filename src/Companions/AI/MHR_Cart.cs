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
    // (1.5.94: no longer pass through each other - they collide, as two players' carts do).
    // 1.5.91/1.5.92: (user: "The npcs run into it all the time. They should not run into the cart. They also seem to stop
    // pulling the cart randomly.") A cart that the player or a companion is pulling is a "party cart" for as long as it's
    // pulled. Companions don't pass through it (user: they should act like a player): their spots keep clear of it
    // (InCartWay) and they walk round it (AroundCart). The game lets go of a cart when the puller is more than
    // m_detachDistance from the shafts, dodges, teleports, the cart tips over or the hitch snaps under a hard jolt
    // (Vagon.Update/CanAttach); a bump from another companion or a side-step did it. When it comes loose without anyone
    // using the cart, the companion takes it again only when that's safe (SafeToRehitch), at most 3 times a minute, and the
    // log says why it came loose and, if it leaves it, why.
    public static class MHR_Cart
    {
        class C { public Vagon Cart; public float NextScan; }
        class A { public Vagon Cart; public float Seen; public Bounds Box; public int BoxFrame = -1; public string By; }

        static readonly Dictionary<int, C> s_pulled = new Dictionary<int, C>();
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

        // 1.5.94: Apart() (1.5.89: the companion's cart and the player's cart, and each with the other's puller, passed
        // through each other) is gone - in the game two carts pulled by two players collide (user: "They should behave like
        // multiple players pulling a cart").

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

        // twice a second: a cart nobody has pulled for 1.5 s is no longer a party cart
        static void Sync()
        {
            float now = Time.time;
            if (now < s_nextSync) return;
            s_nextSync = now + 0.5f;
            s_drop.Clear();
            foreach (KeyValuePair<int, A> kv in s_active)
                if (kv.Value.Cart == null || now - kv.Value.Seen > 1.5f) s_drop.Add(kv.Key);
            foreach (int id in s_drop) s_active.Remove(id);
        }

        // a spot inside (or within pad of) a cart the party is pulling - not somewhere to stand
        public static bool InCartWay(Vector3 g, float pad)
        {
            if (s_active.Count == 0) return false;
            float now = Time.time;
            foreach (A a in s_active.Values)
            {
                if (a.Cart == null || now - a.Seen > 1.5f) continue;
                Bounds b = Box(a);
                if (g.x > b.min.x - pad && g.x < b.max.x + pad && g.z > b.min.z - pad && g.z < b.max.z + pad && g.y > b.min.y - 2f && g.y < b.max.y + 1f) return true;
            }
            return false;
        }

        static Bounds Box(A a)
        {
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
            return a.Box;
        }

        // 1.5.92: the way from here to there runs through a party cart (not the one this character pulls): a point past the
        // nearer end of it to walk to first, like a player stepping round it. False when the way is clear, or when it's
        // already standing inside the box (then it just walks on).
        public static bool AroundCart(Character self, Vector3 from, Vector3 to, float pad, out Vector3 via)
        {
            via = to;
            if (s_active.Count == 0) return false;
            float now = Time.time;
            C mine = null;
            if (self != null) s_pulled.TryGetValue(self.gameObject.GetInstanceID(), out mine);
            foreach (A a in s_active.Values)
            {
                if (a.Cart == null || now - a.Seen > 1.5f) continue;
                if (mine != null && mine.Cart == a.Cart) continue;
                Bounds b = Box(a);
                if (from.y < b.min.y - 2f || from.y > b.max.y + 1f) continue;
                float x0 = b.min.x - pad, x1 = b.max.x + pad, z0 = b.min.z - pad, z1 = b.max.z + pad;
                if (from.x > x0 && from.x < x1 && from.z > z0 && from.z < z1) continue;
                if (!SegmentHitsRect(from.x, from.z, to.x, to.z, x0, z0, x1, z1)) continue;
                // the corner that makes the shorter way round (corners a little further out, so it clears them)
                float best = float.MaxValue;
                float[] xs = { x0 - 0.3f, x1 + 0.3f }, zs = { z0 - 0.3f, z1 + 0.3f };
                foreach (float cx in xs)
                    foreach (float cz in zs)
                    {
                        if (SegmentHitsRect(from.x, from.z, cx, cz, x0, z0, x1, z1)) continue;   // that corner is behind the cart
                        float d = Mathf.Sqrt((cx - from.x) * (cx - from.x) + (cz - from.z) * (cz - from.z)) + Mathf.Sqrt((to.x - cx) * (to.x - cx) + (to.z - cz) * (to.z - cz));
                        if (d < best) { best = d; via = new Vector3(cx, from.y, cz); }
                    }
                if (best < float.MaxValue) return true;
            }
            return false;
        }

        static bool SegmentHitsRect(float ax, float az, float bx, float bz, float x0, float z0, float x1, float z1)
        {
            // Liang-Barsky clip of the segment against the rectangle
            float t0 = 0f, t1 = 1f, dx = bx - ax, dz = bz - az;
            float[] p = { -dx, dx, -dz, dz }, q = { ax - x0, x1 - ax, az - z0, z1 - az };
            for (int i = 0; i < 4; i++)
            {
                if (Mathf.Abs(p[i]) < 1e-6f) { if (q[i] < 0f) return false; continue; }
                float r = q[i] / p[i];
                if (p[i] < 0f) { if (r > t1) return false; if (r > t0) t0 = r; }
                else { if (r < t0) return false; if (r < t1) t1 = r; }
            }
            return t0 < t1;
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

        // 1.5.92: (user: re-hitch "only if it is safe to do so") all of: Follow on, nobody used the cart, it's upright and has
        // stopped rolling, the companion isn't fighting, nobody else is on the shafts, it's within 6 m. Null when safe,
        // otherwise the reason it isn't.
        public static string NotSafeToRehitch(CompanionAI ai, Character ch, Vagon v, out bool wait)
        {
            wait = false;
            if (v == null) return "the cart is gone";
            if (UsedLately(v)) return "someone used the cart";
            CompanionSetup setup = ai != null ? ai.GetComponent<CompanionSetup>() : null;
            if (setup == null || !setup.GetFollow()) return "it isn't following";
            if (v.transform.up.y < 0.94f) return "the cart isn't level";   // 1.5.94: tilted 20 degrees or more (on a slope it tipped right after)
            Rigidbody body = v.GetComponent<Rigidbody>();
            if (body != null && (body.linearVelocity.magnitude > 0.3f || body.angularVelocity.magnitude > 0.3f)) { wait = true; return "the cart is still moving"; }
            if (ai.IsInCombat) return "it's fighting";
            if (AnyoneAttached(v)) return "someone else is pulling the cart";
            if (FromShafts(v, ch) > 6f) return "it's too far from the cart";
            return null;
        }

        // 1.5.94: a second loss within 30 s of taking it back: not again (the slope or the load keeps pulling it off)
        public static string TooManyLosses(Character ch)
        {
            List<float> tries;
            if (ch == null || !s_rehitch.TryGetValue(ch.gameObject.GetInstanceID(), out tries)) return null;
            float now = Time.time;
            tries.RemoveAll(t => now - t > 30f);
            return tries.Count >= 1 ? "it came loose again within 30 s" : null;
        }

        // the cart came loose without an order and NotSafeToRehitch said it's safe: walk back to the shafts and take it
        // again (CompanionAI's cart order)
        public static bool Rehitch(CompanionAI ai, Character ch, Vagon v, string why)
        {
            Humanoid h = ch as Humanoid;
            if (ai == null || h == null || v == null) return false;
            int id = ch.gameObject.GetInstanceID();
            List<float> tries;
            if (!s_rehitch.TryGetValue(id, out tries)) { tries = new List<float>(); s_rehitch[id] = tries; }
            tries.Add(Time.time);
            ai.SetPendingCart(v, h);
            if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + ch.m_name + "'s cart came loose (" + why + ") - it's safe, hitching back on");
            return true;
        }

        // 1.5.94: (user: "It was pure chaos when I was trying to pull a cart") standing in (or within pad of) a cart someone
        // else is pulling: a spot to step to, out of its way - to the side the cart isn't heading for when it rolls, else
        // straight away from its middle. Like a player stepping aside.
        public static bool InTheWay(Character self, Vector3 me, float pad, out Vector3 away)
        {
            away = me;
            if (s_active.Count == 0) return false;
            float now = Time.time;
            C mine = null;
            if (self != null) s_pulled.TryGetValue(self.gameObject.GetInstanceID(), out mine);
            foreach (A a in s_active.Values)
            {
                if (a.Cart == null || now - a.Seen > 1.5f) continue;
                if (mine != null && mine.Cart == a.Cart) continue;
                Bounds b = Box(a);
                if (me.y < b.min.y - 2f || me.y > b.max.y + 1f) continue;
                if (me.x < b.min.x - pad || me.x > b.max.x + pad || me.z < b.min.z - pad || me.z > b.max.z + pad) continue;
                Vector3 rel = me - b.center; rel.y = 0f;
                float reach = Mathf.Max(b.extents.x, b.extents.z) + pad + 0.8f;
                Rigidbody body = a.Cart.GetComponent<Rigidbody>();
                Vector3 v = body != null ? body.linearVelocity : Vector3.zero; v.y = 0f;
                if (v.sqrMagnitude > 0.25f)
                {
                    Vector3 side = new Vector3(-v.z, 0f, v.x).normalized;
                    float off = Vector3.Dot(rel, side);
                    float sign = off >= 0f ? 1f : -1f;
                    away = me + side * sign * Mathf.Max(1f, reach - Mathf.Abs(off));
                }
                else
                {
                    Vector3 dir = rel.sqrMagnitude > 0.01f ? rel.normalized : (self != null ? -self.transform.forward : Vector3.forward);
                    away = new Vector3(b.center.x, me.y, b.center.z) + dir * reach;
                }
                away.y = me.y;
                return true;
            }
            return false;
        }
    }

    // 1.5.91: anyone using a cart (the release order, a player taking it over) - so that let-go isn't undone
    [HarmonyPatch(typeof(Vagon), nameof(Vagon.Interact))]
    static class MHR_CartUsePatch
    {
        static void Prefix(Vagon __instance) { MHR_Cart.NoteUsed(__instance); }
    }
}
