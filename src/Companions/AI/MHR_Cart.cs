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
    // 1.5.97 (cart list C1-C10, user: "The NPC's still ran into the carts all over the place and could not stay connected to
    // them"): the game unhitches EVERY loaded cart when one is hitched (Vagon.AttachTo -> DetachAll) - with companions all on
    // one game that meant only one cart could ever be pulled. Now only the cart the same puller already holds is dropped
    // (C1). A hitch is checked 1 s later (C2/C3). Every loaded cart within 30 m is kept clear of and walked round, pulled or
    // parked (C4), in the shared mover (C9); step-aside only for a cart that's pulled or rolling (C5); a cart isn't ground
    // (C7); a cart it's walking TO is never avoided (C10).
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
        static bool s_reflected;
        // 1.5.97: the game's own private parts of a cart - its list of loaded carts, a cart's hitch joint, letting go
        static FieldInfo s_instancesField, s_joinField;
        static MethodInfo s_detachMethod;
        static readonly List<A> s_near = new List<A>();
        static readonly List<Vagon> s_none = new List<Vagon>();
        class H { public Vagon Cart; public float At; }
        static readonly Dictionary<int, H> s_hitch = new Dictionary<int, H>();        // companion -> the hitch being checked (C2)
        static readonly Dictionary<int, H> s_attachedAt = new Dictionary<int, H>();   // puller -> the cart the game last hitched to it
        internal static GameObject Attaching;                                       // set while Vagon.AttachTo runs (C1)
        internal static Vagon HopExempt;                                            // the cart the mover is walking to (C10)
        const float NearCarts = 30f, GoalPad = 1.5f, RollSpeed = 0.5f;

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

        // twice a second: carts that are gone are forgotten (1.5.97: the list now holds every cart looked at, not only
        // the pulled ones; Seen still says when it was last seen pulled)
        static void Sync()
        {
            float now = Time.time;
            if (now < s_nextSync) return;
            s_nextSync = now + 0.5f;
            s_drop.Clear();
            foreach (KeyValuePair<int, A> kv in s_active)
                if (kv.Value.Cart == null) s_drop.Add(kv.Key);
            foreach (int id in s_drop) s_active.Remove(id);
        }

        static A Get(Vagon v)
        {
            int id = v.GetInstanceID();
            A a;
            if (!s_active.TryGetValue(id, out a)) { a = new A { Cart = v, Seen = -100f }; s_active[id] = a; }
            return a;
        }

        // 1.5.97 (C4): every cart the game has loaded (its own list), or none when that can't be read
        static List<Vagon> All()
        {
            Reflect();
            try { if (s_instancesField != null) { List<Vagon> l = s_instancesField.GetValue(null) as List<Vagon>; if (l != null) return l; } } catch { }
            return s_none;
        }

        static Vagon Mine(Character self)
        {
            C mine;
            if (self == null || !s_pulled.TryGetValue(self.gameObject.GetInstanceID(), out mine)) return null;
            return mine.Cart;
        }

        // 1.5.97 (C4): the loaded carts within 30 m of this point - not the one this character pulls, not the one it's
        // walking to
        static List<A> Near(Character self, Vector3 at, Vagon exempt)
        {
            s_near.Clear();
            List<Vagon> all = All();
            if (all.Count == 0) return s_near;
            Sync();
            Vagon mine = Mine(self);
            for (int i = 0; i < all.Count; i++)
            {
                Vagon v = all[i];
                if (v == null || v == mine || v == exempt) continue;
                Vector3 p = v.transform.position;
                float dx = p.x - at.x, dz = p.z - at.z;
                if (dx * dx + dz * dz > NearCarts * NearCarts) continue;
                s_near.Add(Get(v));
            }
            return s_near;
        }

        // being pulled (by anyone, on any game) or rolling
        static bool PulledOrRolling(A a)
        {
            try { if (a.Cart.IsAttached()) return true; } catch { }
            if (Time.time - a.Seen <= 1.5f) return true;
            Rigidbody body = a.Cart.GetComponent<Rigidbody>();
            return body != null && body.linearVelocity.magnitude > RollSpeed;
        }

        static bool InBox(Bounds b, Vector3 g, float pad, float up)
        {
            return g.x > b.min.x - pad && g.x < b.max.x + pad && g.z > b.min.z - pad && g.z < b.max.z + pad && g.y > b.min.y - 2f && g.y < b.max.y + up;
        }

        // a spot inside (or within pad of) a cart - not somewhere to stand (1.5.97: any loaded cart, not only a pulled one)
        public static bool InCartWay(Vector3 g, float pad) { return InCartWay(g, pad, null); }
        public static bool InCartWay(Vector3 g, float pad, Character self)
        {
            List<A> near = Near(self, g, null);
            for (int i = 0; i < near.Count; i++)
                if (InBox(Box(near[i]), g, pad, 1f)) return true;
            return false;
        }

        // 1.5.97 (C10): the cart this walk is heading for - the one it was told to hitch, or the one its goal lies on or
        // within 1.5 m of (the deposit order, a job's drop-off, standing by its cart). Never avoided on that walk.
        public static Vagon GoalCart(CompanionAI ai, Vector3 goal)
        {
            if (ai != null && ai.PendingCartAttach != null) return ai.PendingCartAttach;
            List<A> near = Near(null, goal, null);
            for (int i = 0; i < near.Count; i++)
                if (InBox(Box(near[i]), goal, GoalPad, GoalPad)) return near[i].Cart;
            return null;
        }

        // 1.5.97 (C7): this collider is part of a cart (not the one being walked to) - not ground to walk or hop on
        public static bool IsCartInWay(Collider col)
        {
            if (col == null) return false;
            Vagon v = col.GetComponentInParent<Vagon>();
            return v != null && v != HopExempt;
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
        public static bool AroundCart(Character self, Vector3 from, Vector3 to, float pad, out Vector3 via) { return AroundCart(self, from, to, pad, out via, null); }
        public static bool AroundCart(Character self, Vector3 from, Vector3 to, float pad, out Vector3 via, Vagon exempt)
        {
            via = to;
            List<A> near = Near(self, from, exempt);
            for (int n = 0; n < near.Count; n++)
            {
                A a = near[n];
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
            try { s_instancesField = AccessTools.Field(typeof(Vagon), "m_instances"); } catch { }
            try { s_joinField = AccessTools.Field(typeof(Vagon), "m_attachJoin"); } catch { }
            try { s_detachMethod = AccessTools.Method(typeof(Vagon), "Detach", Type.EmptyTypes); } catch { }
            if (s_instancesField == null || s_joinField == null || s_detachMethod == null)
                CompanionsPlugin.Log?.LogWarning("[Cart] couldn't read the game's cart list or hitch - carts fall back to the game's one-cart rule");
        }
        static float DetachDistance(Vagon v)
        {
            Reflect();
            try { if (s_detachField != null) return (float)s_detachField.GetValue(v); } catch { }
            return 2f;
        }
        static bool AnyoneAttached(Vagon v)
        {
            try { return v != null && v.IsAttached(); } catch { }
            return false;
        }

        // ---------- 1.5.97 (C1): one cart per puller, not one cart per game ----------
        // Stands in for the game's DetachAll while a cart is being hitched: only a cart the SAME puller already holds lets
        // go (the game's own rule for each player on their own game). False = done here, skip the game's version.
        internal static bool DetachOnlySamePuller()
        {
            GameObject who = Attaching;
            if (who == null) return true;
            Reflect();
            if (s_instancesField == null || s_joinField == null || s_detachMethod == null) return true;
            try
            {
                List<Vagon> all = All();
                int kept = 0, dropped = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    Vagon v = all[i];
                    if (v == null) continue;
                    ConfigurableJoint j = s_joinField.GetValue(v) as ConfigurableJoint;
                    if (j == null) continue;
                    if (j.connectedBody == null || j.connectedBody.gameObject == who) { s_detachMethod.Invoke(v, null); dropped++; }
                    else kept++;
                }
                if (CompanionsPlugin.LogDetailed && (kept > 0 || dropped > 0))
                    CompanionsPlugin.Log.LogDebug("[Cart] " + Name(who) + " hitches a cart: let go of " + dropped + " cart(s) of its own, " + kept + " cart(s) pulled by others stay hitched");
                return false;
            }
            catch (Exception e)
            {
                CompanionsPlugin.Log?.LogWarning("[Cart] one-cart-per-puller failed (" + e.Message + ") - the game's own rule this time");
                return true;
            }
        }

        static string Name(GameObject go)
        {
            Character ch = go != null ? go.GetComponent<Character>() : null;
            if (ch == null) return "?";
            return ch.IsPlayer() ? "you" : ch.m_name;
        }

        // the game hitched this cart to that puller just now
        internal static void NoteAttached(Vagon v, GameObject who)
        {
            if (v == null || who == null) return;
            if (s_attachedAt.Count > 200) s_attachedAt.Clear();
            s_attachedAt[who.GetInstanceID()] = new H { Cart = v, At = Time.time };
        }

        // ---------- 1.5.97 (C2/C3): a hitch is checked a second later ----------
        // called where a companion takes the shafts (the order, the re-hitch)
        internal static void NoteHitch(CompanionAI ai, Vagon v)
        {
            if (ai == null || v == null) return;
            if (s_hitch.Count > 200) s_hitch.Clear();
            s_hitch[ai.gameObject.GetInstanceID()] = new H { Cart = v, At = Time.time };
        }

        // every AI tick: 1 s after a hitch - on the shafts: it's pulling from now (so a loss is noticed even if the follow
        // code never saw it hitched). Was hitched but already lost: the same, the loss rules take it from there. Never
        // hitched: it says why and leaves it - no retry.
        internal static void HitchCheck(CompanionAI ai)
        {
            if (s_hitch.Count == 0 || ai == null) return;
            int id = ai.gameObject.GetInstanceID();
            H h;
            if (!s_hitch.TryGetValue(id, out h) || Time.time - h.At < 1f) return;
            s_hitch.Remove(id);
            Character ch = ai.GetComponent<Character>();
            if (ch == null) return;
            Vagon v = h.Cart;
            bool on = Attached(v, ch);
            H at;
            bool was = on || (s_attachedAt.TryGetValue(id, out at) && at.Cart == v && at.At >= h.At - 0.1f);
            if (was)
            {
                if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + ch.m_name + (on ? " is hitched to the cart" : " was hitched but the cart came off within a second (" + WhyLoose(v, ch) + ")"));
                MHR_Follow.MarkPulling(ai, ch, v);
                return;
            }
            string why = WhyNotHitched(v, ch);
            if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + ch.m_name + " couldn't take the cart (" + why + ") - leaving it");
            ai.GetComponent<CompanionTalk>()?.Speak(ModLocalization.LocFmt("hc_speech_cart_left", why), CompanionTalk.Priority.Event, "cart:left");
        }

        // why the game refused the hitch (Vagon.CanAttach, after the fact)
        static string WhyNotHitched(Vagon v, Character ch)
        {
            if (v == null) return "the cart is gone";
            if (v.transform.up.y < 0.1f) return "the cart tipped over";
            Humanoid hm = ch as Humanoid;
            if (hm != null && hm.InDodge()) return "it dodged";
            float d = FromShafts(v, ch), max = DetachDistance(v);
            if (d >= max - 0.1f) return "it was " + d.ToString("F1") + " m from the shafts (the game needs under " + max.ToString("F1") + " m)";
            return "the hitch didn't take";
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
            List<A> near = Near(self, me, null);
            for (int n = 0; n < near.Count; n++)
            {
                A a = near[n];
                // 1.5.97 (C5): only a cart that's being pulled or is rolling - a parked one is walked round, not fled from
                if (!PulledOrRolling(a)) continue;
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

    // 1.5.97 (C1): the game lets go of every loaded cart when one is hitched (AttachTo -> DetachAll) - fine with one puller
    // per game, but companions are all on one game. While a cart is being hitched only the same puller's other cart lets go.
    [HarmonyPatch(typeof(Vagon), "AttachTo")]
    static class MHR_CartAttachPatch
    {
        static void Prefix(GameObject __0) { MHR_Cart.Attaching = __0; }
        static void Postfix(Vagon __instance, GameObject __0) { MHR_Cart.Attaching = null; MHR_Cart.NoteAttached(__instance, __0); }
        static void Finalizer() { MHR_Cart.Attaching = null; }
    }

    [HarmonyPatch(typeof(Vagon), "DetachAll")]
    static class MHR_CartDetachAllPatch
    {
        static bool Prefix() { return MHR_Cart.DetachOnlySamePuller(); }
    }
}
