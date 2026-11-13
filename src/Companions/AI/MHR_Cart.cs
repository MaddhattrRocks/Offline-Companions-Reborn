using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    // ---- 1.5.89: carts. ----
    // A companion pulling a cart follows straight behind the player on the player's own path (MHR_Follow.CartFollow),
    // far enough back to clear the player's cart when the player pulls one too. While both pull a cart the two carts
    // (and each cart with the other one's puller) don't collide, so they can't snag on each other in a turn.
    public static class MHR_Cart
    {
        class C { public Vagon Cart; public float NextScan; }
        class P { public Character Comp, Player; public Vagon Mine, Theirs; }

        static readonly Dictionary<int, C> s_pulled = new Dictionary<int, C>();
        static readonly Dictionary<int, P> s_apart = new Dictionary<int, P>();
        static readonly Dictionary<int, Collider[]> s_cols = new Dictionary<int, Collider[]>();
        static readonly Collider[] s_hits = new Collider[32];
        static int s_mask = -1;

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
            if (c.Cart != null && Attached(c.Cart, ch)) return c.Cart;
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
                if (v != null && Attached(v, ch)) { c.Cart = v; return v; }
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
            p = new P { Comp = comp, Player = player, Mine = mine, Theirs = theirs };
            Ignore(p, true);
            s_apart[id] = p;
            if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[Cart] " + comp.m_name + " and the player both pull a cart - the carts pass through each other");
        }

        static void Ignore(P p, bool ignore)
        {
            if (p.Mine == null || p.Theirs == null) return;   // a cart was destroyed: its pairs went with it
            Collider[] a = Cols(p.Mine), b = Cols(p.Theirs);
            Collider ca = p.Comp != null ? p.Comp.GetComponent<Collider>() : null;
            Collider cb = p.Player != null ? p.Player.GetComponent<Collider>() : null;
            foreach (Collider x in a)
            {
                if (x == null) continue;
                foreach (Collider y in b) if (y != null) Physics.IgnoreCollision(x, y, ignore);
                if (cb != null) Physics.IgnoreCollision(x, cb, ignore);
            }
            if (ca != null) foreach (Collider y in b) if (y != null) Physics.IgnoreCollision(ca, y, ignore);
        }
    }
}
