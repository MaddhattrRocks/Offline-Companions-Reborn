using System;
using System.Collections.Generic;
using HarmonyLib;
using Splatform;
using UnityEngine;

namespace Companions
{
    internal enum YardTrip { Stone, Build, Ore, Toss }

    // ---- 1.5.97 (build list v4, items F2 and G, John 2026-11-15) ----
    //  - Wood in the Ore job is handled just like stone: a wood pile (the game's wood_stack piece, its cost read from the
    //    game) whenever it carries a pile's worth and has a hammer; else tossed when too heavy. Stone first, then wood.
    //  - Too heavy with the stone, wood and ore dealt with: the other junk (what the full-pack clean-up calls junk: IsJunk,
    //    with Gathering > JunkCleanup and NeverDiscard) is tossed the same way, the heaviest stack first, until it's under
    //    the limit. Then it keeps the rest, pauses and says it needs another chest: nothing but ore goes into a chest by
    //    the vein, and its whole load only into a chest at home.

    // ---- 1.5.95/1.5.96: the Ore job's stone and ore by the vein (John's approved list, 2026-11-14) ----
    //  - Stone, with a hammer: whenever it carries a Stone pile's worth (the piece's own cost, read from the game) it builds
    //    one, using the hammer's stamina and wear like a player: 2-4 m outside the edge of the hole on the side it's
    //    working (the hole = the vein's box + 1.5 m), or in the hole the vein left once it has popped. The game's own
    //    placement checks (no other ward, no no-build area, the piece's station if it lists one; flat open ground). With no
    //    spot that passes, it tosses the stone instead.
    //  - Stone, no hammer: when it reaches its weight limit (Gathering > OverweightThreshold, where it stops picking things
    //    up) it tosses ALL its stone, one stack every 0.5 s, with the player's own drop (1 m in front, thrown, the drop
    //    animation, the no-auto-pickup mark - not saved, exactly like a player's). It faces straight out from the vein,
    //    turned to 90 degrees from the ore spot if that would face it. No walking. Stone that lands in the hole is fine.
    //    Leftover stone (less than a pile, or under the limit) stays in its bag.
    //  - Ore: when it reaches its weight limit AND after each finished vein: ALL its ore into a chest, cart or ship within
    //    20 m; with none, ALL of it on the ore spot: 3 m out from the vein on the side it started working, the same spot
    //    for every unload while it's within 25 m. Dropped there the player's way, facing away from the vein.
    public static partial class MHR_GatherRules
    {
        const float YardReuse = 25f;

        static readonly string[] s_pileItems = { "Stone", "Wood" };
        static readonly Dictionary<string, KeyValuePair<GameObject, int>> s_pilePiece = new Dictionary<string, KeyValuePair<GameObject, int>>();

        static string PilePrefab(string item) { return item == "Wood" ? "wood_stack" : "stone_pile"; }
        static string PileWord(string item) { return item == "Wood" ? "wood" : "stone"; }

        // the game's pile piece for stone or wood and how much of it one takes
        static GameObject PilePiece(string item, out int cost)
        {
            KeyValuePair<GameObject, int> kv;
            if (!s_pilePiece.TryGetValue(item, out kv) && ZNetScene.instance != null)
            {
                GameObject found = null; int amount = 0;
                GameObject go = ZNetScene.instance.GetPrefab(PilePrefab(item));
                Piece pc = go != null ? go.GetComponent<Piece>() : null;
                if (pc != null && pc.m_resources != null && pc.m_resources.Length == 1 && pc.m_resources[0].m_resItem != null && pc.m_resources[0].m_resItem.gameObject.name == item)
                {
                    found = go; amount = Mathf.Max(1, pc.m_resources[0].m_amount);
                }
                else Warn("No " + PileWord(item) + " pile piece found (" + PilePrefab(item) + ") - " + PileWord(item) + " is tossed instead");
                // 1.5.96: what the game says the piece needs (John: check it in the game, don't assume)
                if (CompanionsPlugin.LogDetailed && pc != null)
                {
                    string res = "";
                    if (pc.m_resources != null) foreach (Piece.Requirement r in pc.m_resources) if (r != null && r.m_resItem != null) res += (res.Length > 0 ? ", " : "") + r.m_amount + " " + r.m_resItem.gameObject.name;
                    Debug("Pile piece for " + PileWord(item) + " \"" + pc.m_name + "\": costs " + res + ", crafting station " + (pc.m_craftingStation != null ? pc.m_craftingStation.m_name : "none") + ", ground only " + pc.m_groundOnly + ", in dungeons " + pc.m_allowedInDungeons);
                }
                kv = new KeyValuePair<GameObject, int>(found, amount);
                s_pilePiece[item] = kv;
            }
            cost = kv.Value;
            return kv.Key;
        }

        // is the stone due to be dealt with? (about once a second while mining, and before each scan)
        internal static bool StoneDue(HarvestController h, bool now = false)
        {
            try
            {
                if (Mode(h) != 3) return false;
                if (InDungeon(h)) return false;   // dungeons: no piles, no tossing
                Per p = P(h);
                if (p.PileActive) return false;
                if (!now && Time.time < p.NextStoneCheck) return false;
                p.NextStoneCheck = Time.time + 1f;
                Humanoid hum = h.GetComponent<Humanoid>();
                Inventory inv = hum != null ? hum.GetInventory() : null;
                if (inv == null) return false;
                // stone first, then wood (1.5.97): a pile's worth with a hammer, or any of it when too heavy
                bool any = false;
                foreach (string item in s_pileItems)
                {
                    int have = CountIn(inv, item);
                    if (have <= 0) continue;
                    any = true;
                    int cost;
                    if (h.OvOnPit && CanBuild(h, p, hum, item, out cost) && have >= cost) return true;   // L5: a pile only on its pit
                }
                return any && h.IsOverweight();
            }
            catch (Exception e) { Warn("StoneDue: " + e.Message); return false; }
        }

        static bool InDungeon(HarvestController h)
        {
            Character ch = h.GetComponent<Character>();
            if (ch != null && ch.InInterior()) return true;
            Vector3 p = h.transform.position;
            return ZoneSystem.instance != null && p.y - ZoneSystem.instance.GetGroundHeight(p) > 50f;
        }

        static bool CanBuild(HarvestController h, Per p, Humanoid hum, string item, out int cost)
        {
            GameObject prefab = PilePiece(item, out cost);
            return prefab != null && Time.time >= p.NoBuildUntil && MHR_PileTaker.Hammer(hum) != null;
        }

        // the vein's box (the one it's working or just finished), else a 1 m box at the rock
        static Bounds VeinBounds(HarvestController h, out int firstSide, out int side)
        {
            Bounds b;
            if (h.VeinBox(out b, out firstSide, out side)) return b;
            firstSide = -1; side = -1;
            Vector3 c = h._attackNodePos != Vector3.zero ? h._attackNodePos : (h._target != null ? h._target.transform.position : h.transform.position);
            return new Bounds(c, Vector3.one);
        }

        // the ore spot: 3 m out from the vein on the side it started working (the side it's on if it never picked one),
        // the same spot while it's within 25 m
        static void EnsureOreSpot(HarvestController h, Per p)
        {
            // FF3 / Q1 (John: "the ore dump should be inside of the 'pit'"): the vein's one ore dump on the trench floor
            Vector3 dump;
            if (h.PitDumpFor(out dump))
            {
                int f0, s0;
                p.OreSpot = dump; p.OreHas = true; p.YardFor = VeinBounds(h, out f0, out s0).center;
                return;
            }
            if (p.OreHas && Vector3.Distance(h.transform.position, p.OreSpot) <= YardReuse) return;
            int first, side;
            Bounds b = VeinBounds(h, out first, out side);
            Vector3 n;
            if (first >= 0) n = HarvestController.SideDirOf(first);
            else
            {
                n = h.transform.position - b.center; n.y = 0f;
                if (n.sqrMagnitude < 0.01f) n = Vector3.forward;
                n.Normalize();
            }
            Vector3 edge = b.center + Vector3.Scale(n, b.extents);
            Vector3 spot = new Vector3(edge.x, 0f, edge.z) + n * 3f;
            if (first < 0) spot = b.center + n * (Mathf.Max(b.extents.x, b.extents.z) + 3f);
            spot.y = Ground(spot);
            p.OreSpot = spot; p.OreHas = true; p.YardFor = b.center;
            if (CompanionsPlugin.LogDetailed) Debug("Ore spot for the vein at " + MHR_Mover.Fmt(b.center) + ": " + MHR_Mover.Fmt(spot));
        }

        // Ore job: deal with the stone it carries - a stone pile with a hammer (enough for one), else toss it when heavy
        public static bool StartPile(HarvestController h, bool leftover = false)
        {
            Per p = P(h);
            if (p.PileActive) return true;
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null) return false;
            foreach (string item in s_pileItems)
            {
                int have = CountIn(hum.GetInventory(), item);
                if (have <= 0) continue;
                int cost;
                // F2: after the pop, no stone pile is tried until the pit changes once one couldn't be placed - to the dump
                if (item == "Stone" && h.OvNoPileNow() && h.OvStoneDumpAt(out Vector3 nd)) return TossAt(h, item, nd, "no stone pile now - its stone to the stone dump");
                // L5 (log a6063c19: 138 pile starts off the vein, spots chosen next to the companion, 1 built): a pile only on
                // its pit; off it, only the toss when too heavy (below)
                if (h.OvOnPit && CanBuild(h, p, hum, item, out cost) && have >= cost)
                {
                    string why;
                    p.PileItem = item;
                    if (FindBuildSpot(h, p, out why))
                    {
                        if (item == "Stone") h.OvStonePileStarted(p.BuildSpot);   // F2
                        p.Trip = YardTrip.Build; p.Arrived = false;
                        p.PilePoint = StandFor(h, p.BuildSpot);
                        p.PileActive = true; p.PileUntil = Time.time + 25f;
                        if (CompanionsPlugin.LogNormal) Info("Building a " + PileWord(item) + " pile (" + have + " " + PileWord(item) + ", " + cost + " a pile)");
                        return true;
                    }
                    // F2 (John 11-22 04:24Z: "Toss the stone in a stone dump location. All NPC should use the same location"):
                    // after the pop, no 60 s wait - no pile tried until the pit changes, and the stone goes to the stone dump
                    Vector3 sd;
                    if (item == "Stone" && h.OvStoneDumpAt(out sd))
                    {
                        h.OvNoPile();
                        return TossAt(h, item, sd, "no place for a stone pile (" + why + ") - its stone to the stone dump");
                    }
                    p.NoBuildUntil = Time.time + 60f;
                    if (Time.time >= p.NextNoBuildLog)
                    {
                        p.NextNoBuildLog = Time.time + 120f;
                        if (CompanionsPlugin.LogNormal) Info("Can't build a " + PileWord(item) + " pile here (" + why + ") - tossing the " + PileWord(item) + " instead");
                    }
                    // stone: tossed, as before. Wood with no place for a pile: only when it's too heavy.
                    if (item == "Stone" || h.IsOverweight()) return StartToss(h, p, item);
                    continue;
                }
                if (h.IsOverweight()) return StartToss(h, p, item);
            }
            return false;
        }

        // 1.5.98 (G8): too heavy (or the bag full) on a vein - its stone, then its wood, tossed off to one side right where it
        // works (the toss above); the toss ends in ResetToIdle and the vein work carries on. False = no stone or wood.
        public static bool TossOnVein(HarvestController h)
        {
            Per p = P(h);
            if (p.PileActive) return true;
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null) return false;
            foreach (string item in s_pileItems)
            {
                if (item == p.VeinTossItem && Time.time - p.VeinTossAt < 5f) continue;
                if (CountIn(hum.GetInventory(), item) > 0) { p.VeinTossAt = Time.time; p.VeinTossItem = item; return StartToss(h, p, item); }
            }
            return false;
        }

        static bool StartToss(HarvestController h, Per p, string item)
        {
            // F2: after the pop the stone goes to the pit's one stone dump (walked there, then dropped), not anywhere
            Vector3 sd;
            if (item == "Stone" && h.OvStoneDumpAt(out sd)) return TossAt(h, item, sd, "too heavy - its stone to the stone dump");
            p.Trip = YardTrip.Toss; p.PileItem = item; p.TossJunk = false; p.TossToCap = false;
            p.PileActive = true; p.PileUntil = Time.time + 30f; p.NextToss = Time.time + 0.2f;
            p.PilePoint = h.transform.position;
            if (CompanionsPlugin.LogNormal) Info("Too heavy - tossing its " + PileWord(item) + " off to one side");
            return true;
        }

        // pop test E1 (John 2026-11-16: "for the stone job they should drop some of the stone to get under their carry
        // capacity"): over what it can carry (General > MaxCarryWeight plus its gear) with stone in the bag - the existing
        // toss, only as much stone as takes it to that weight or under. False = not over it, or no stone.
        static bool StartCapToss(HarvestController h, Per p)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            Inventory inv = hum != null ? hum.GetInventory() : null;
            if (inv == null || Time.time < p.NoCapTossUntil) return false;
            float limit = CompanionTierData.CarryLimit(h), w = inv.GetTotalWeight();
            if (w <= limit || CountIn(inv, "Stone") <= 0) return false;
            p.Trip = YardTrip.Toss; p.PileItem = "Stone"; p.TossJunk = false; p.TossToCap = true;
            p.PileActive = true; p.PileUntil = Time.time + 30f; p.NextToss = Time.time + 0.2f;
            p.PilePoint = h.transform.position;
            if (CompanionsPlugin.LogNormal) Info("Over what it can carry (" + w.ToString("F0") + " of " + limit.ToString("F0") + ") - tossing some of its stone off to one side");
            return true;
        }

        // item G: the heaviest stack of what the full-pack clean-up calls junk (null = none)
        static ItemDrop.ItemData HeaviestJunk(Humanoid hum)
        {
            ItemDrop.ItemData best = null; float bw = -1f;
            foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems())
            {
                if (!IsJunk(hum, it)) continue;
                float w = it.GetWeight();
                if (w > bw) { bw = w; best = it; }
            }
            return best;
        }

        // item G: too heavy with the stone, wood and ore dealt with - the other junk goes, the heaviest stack first, until
        // it's under the limit. False = nothing to toss (or Gathering > JunkCleanup is off).
        static bool StartJunkToss(HarvestController h, Per p, Humanoid hum)
        {
            if (ModConfig.JunkCleanup == null || !ModConfig.JunkCleanup.Value) return false;
            if (Time.time < p.NoJunkUntil || hum == null || hum.GetInventory() == null || HeaviestJunk(hum) == null) return false;
            p.Trip = YardTrip.Toss; p.TossJunk = true; p.TossToCap = false;
            p.PileActive = true; p.PileUntil = Time.time + 30f; p.NextToss = Time.time + 0.2f;
            p.PilePoint = h.transform.position;
            if (CompanionsPlugin.LogNormal) Info("Still too heavy - tossing other junk off to one side, the heaviest first");
            return true;
        }

        // the way it faces to toss: straight out from the vein; turned to 90 degrees from the ore spot if that's within 90
        static Vector3 TossDir(HarvestController h, Per p)
        {
            int first, side;
            Bounds b = VeinBounds(h, out first, out side);
            Vector3 me = h.transform.position;
            Vector3 d = me - b.center; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = h.transform.forward;
            d.y = 0f; d.Normalize();
            if (p.OreHas && Vector3.Distance(me, p.OreSpot) <= YardReuse)
            {
                Vector3 o = p.OreSpot - me; o.y = 0f;
                if (o.sqrMagnitude > 0.01f)
                {
                    o.Normalize();
                    if (Vector3.Dot(d, o) > 0f)
                    {
                        Vector3 a = new Vector3(-o.z, 0f, o.x), c = -a;   // the two directions 90 degrees from the ore spot
                        d = Vector3.Dot(a, d) >= Vector3.Dot(c, d) ? a : c;
                    }
                }
            }
            return d;
        }

        // one stone stack every 0.5 s, the player's way. False = done.
        static bool TossTick(HarvestController h, Per p, CompanionAI ai)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            Inventory inv = hum != null ? hum.GetInventory() : null;
            if (inv == null || Time.time >= p.PileUntil) return false;
            ai.StopMoving();
            if (Time.time < p.NextToss || hum.InAttack()) return true;
            ItemDrop.ItemData stack = null;
            float capOver = 0f;
            if (p.TossJunk)
            {
                // item G: only until it's under the limit again (weight, or a free slot when the pack was full)
                if (!h.IsOverweight()) return false;
                stack = HeaviestJunk(hum);
                if (stack == null) return false;
            }
            else
            {
                if (p.TossToCap)
                {
                    capOver = inv.GetTotalWeight() - CompanionTierData.CarryLimit(h);
                    if (capOver <= 0f) return false;   // at or under what it can carry
                }
                foreach (ItemDrop.ItemData it in inv.GetAllItems())
                    if (it != null && it.m_dropPrefab != null && it.m_dropPrefab.name == p.PileItem && !it.m_equipped) { stack = it; break; }
                if (stack == null)
                {
                    p.PackFull = false;
                    return false;
                }
            }
            Vector3 dir = TossDir(h, p);
            h.transform.rotation = Quaternion.LookRotation(dir);
            int n = stack.m_stack;
            if (!p.TossJunk && p.TossToCap)
            {
                // only as many as it takes
                n = Mathf.Clamp(Mathf.CeilToInt(capOver / Mathf.Max(0.01f, stack.GetWeight() / Mathf.Max(1, stack.m_stack))), 1, stack.m_stack);
            }
            string tossed = p.TossJunk ? (stack.m_dropPrefab != null ? stack.m_dropPrefab.name : stack.m_shared.m_name) : PileWord(p.PileItem);
            // F2 (John 11-22 05:49Z: can't get to the stone dump - "Just toss it"): what it tosses then is left where it
            // lands (not on the pit's list again), so it isn't carried back and forth
            List<ItemDrop> left = p.TossLeave ? new List<ItemDrop>() : null;
            s_dropInto = left;
            bool dropped;
            try { dropped = PlayerDrop(hum, stack, n); }
            finally { s_dropInto = null; }
            if (left != null) foreach (ItemDrop ld in left) if (ld != null) s_leftStone.Add(ld.gameObject.GetInstanceID());
            if (dropped)
            {
                if (p.TossJunk) { if (CompanionsPlugin.LogNormal) Info("Tossed " + n + " " + tossed + " (junk) to get under its weight limit"); }
                else if (p.TossToCap) { if (CompanionsPlugin.LogNormal) Info("Tossed " + n + " " + tossed + " to get to what it can carry"); }
                else if (CompanionsPlugin.LogDetailed) Debug("Tossed " + n + " " + tossed + " toward " + MHR_Mover.Fmt(h.transform.position + dir * 3f));
            }
            else if (p.TossToCap)
            {
                // the game refused the drop: no more of this for a minute, so it can't go round on one stack
                p.NoCapTossUntil = Time.time + 60f;
                return false;
            }
            else if (p.TossJunk)
            {
                // the game refused the drop: no more junk tossing for a minute, so it can't go round on one stack
                p.NoJunkUntil = Time.time + 60f;
                return false;
            }
            p.NextToss = Time.time + 0.5f;
            return true;
        }

        // F2: the toss (the player's drop, one stack every 0.5 s) at `at`: walked there first (the mover), then tossed. The
        // walk ends there (within 1.5 m), or where the mover gives up on it (tossed where it is then).
        internal static bool TossAt(HarvestController h, string item, Vector3 at, string why)
        {
            Per p = P(h);
            if (p.PileActive) return true;
            p.Trip = YardTrip.Toss; p.PileItem = item; p.TossJunk = false; p.TossToCap = false;
            p.PileActive = true; p.PileUntil = float.MaxValue; p.NextToss = Time.time + 0.2f;
            p.PilePoint = at; p.TossWalk = true; p.TossWalkGo = false; p.TossLeave = false;
            if (CompanionsPlugin.LogNormal) Info("Taking " + why + " at " + MHR_Mover.Fmt(at));
            return true;
        }

        // F2: stone tossed where the walk to the stone dump gave up - left where it is (the pit's list skips it)
        static readonly HashSet<int> s_leftStone = new HashSet<int>();
        internal static bool LeftStone(GameObject go) { return go != null && s_leftStone.Contains(go.GetInstanceID()); }

        // F2: how much stone is in the bag
        internal static int CountStone(Inventory inv) { return inv == null ? 0 : CountIn(inv, "Stone"); }

        // F2: the first place the game allows a stone pile in the pit (FindBuildSpot's own checks), without building it
        internal static bool StonePileSpot(HarvestController h, out Vector3 spot)
        {
            spot = Vector3.zero;
            Per p = P(h);
            string was = p.PileItem; p.PileItem = "Stone";
            bool hadBuild = p.BuildHas; Vector3 bs = p.BuildSpot; Quaternion br = p.BuildRot;
            string why;
            bool ok = FindBuildSpot(h, p, out why);
            if (ok) spot = p.BuildSpot;
            p.PileItem = was; p.BuildHas = hadBuild; p.BuildSpot = bs; p.BuildRot = br;
            return ok;
        }

        internal static bool HasOre(Humanoid hum)
        {
            Inventory inv = hum != null ? hum.GetInventory() : null;
            if (inv == null) return false;
            foreach (ItemDrop.ItemData it in inv.GetAllItems()) if (IsOreItem(it)) return true;
            return false;
        }

        // FF3: ore dropped the player's way, the biggest stacks first, only as many as take it to `limit` or under. The drops
        // are added to `into` when it is given. Returns how many it dropped.
        internal static int DropOreTo(Humanoid hum, float limit, List<ItemDrop> into)
        {
            Inventory inv = hum != null ? hum.GetInventory() : null;
            if (inv == null) return 0;
            List<ItemDrop.ItemData> ore = new List<ItemDrop.ItemData>();
            foreach (ItemDrop.ItemData it in inv.GetAllItems()) if (IsOreItem(it)) ore.Add(it);
            ore.Sort((a, b) => b.GetWeight().CompareTo(a.GetWeight()));
            int n = 0;
            foreach (ItemDrop.ItemData it in ore)
            {
                float over = inv.GetTotalWeight() - limit;
                if (over <= 0f) break;
                float unit = Mathf.Max(0.01f, it.GetWeight() / Mathf.Max(1, it.m_stack));
                int count = Mathf.Clamp(Mathf.CeilToInt(over / unit), 1, it.m_stack);
                s_dropInto = into;
                try { if (PlayerDrop(hum, it, count)) n += count; }
                finally { s_dropInto = null; }
            }
            return n;
        }

        // FF3: the walk to the vein's ore dump (the ore trip; 60 s, then it drops it where it is, as the ore trip does)
        internal static void StartPitDump(HarvestController h, Vector3 dump, Vector3 vein)
        {
            Per p = P(h);
            p.Trip = YardTrip.Ore; p.PitDump = true;
            p.OreSpot = dump; p.OreHas = true; p.YardFor = vein;
            p.PilePoint = dump; p.PileActive = true; p.PileUntil = Time.time + 60f;
            if (CompanionsPlugin.LogNormal) Info("Too heavy with ore - taking it to the ore dump at " + MHR_Mover.Fmt(dump));
        }

        // ---- the player's own drop (Humanoid.DropItem), with the player's no-auto-pickup mark (ItemDrop.OnPlayerDrop):
        // the game only marks it when the dropper is a player, so the postfix below adds it for this call ----
        [ThreadStatic] static bool s_playerDrop;
        [ThreadStatic] static List<ItemDrop> s_dropInto;   // FF3: where the drops of one call are kept (null = not kept)

        internal static bool PlayerDrop(Humanoid hum, ItemDrop.ItemData item, int amount)
        {
            if (hum == null || item == null || amount <= 0) return false;
            s_playerDrop = true;
            try { return hum.DropItem(hum.GetInventory(), item, amount); }
            catch (Exception e) { Warn("PlayerDrop: " + e.Message); return false; }
            finally { s_playerDrop = false; }
        }

        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        static class ItemDrop_DropItem_PlayerMark
        {
            static void Postfix(ItemDrop __result)
            {
                if (s_playerDrop && __result != null)
                {
                    __result.OnPlayerDrop();
                    s_dropInto?.Add(__result);
                }
            }
        }

        // D4 / F1 (John, 2026-11-20: "They can build stone piles anywhere the game will allow in the pit, once the ore vein
        // has popped"): from the pop, the pit ground every 2 m is open for its stone and wood piles (was the pit's middle
        // and 4 spots 1 m from it, for 60 s after the vein)
        internal static void OpenPit(HarvestController h)
        {
            Per p = P(h);
            int first, side;
            Bounds b = VeinBounds(h, out first, out side);
            if (!h.PitGrid(p.VoidSpots, 2f)) return;
            p.VoidHas = true; p.VoidAt = new Vector3(b.center.x, Ground(b.center), b.center.z); p.VoidUntil = float.MaxValue;
            if (CompanionsPlugin.LogNormal) Info("The vein popped - its pit is open for stone and wood piles (" + p.VoidSpots.Count + " spots)");
        }

        // F1: the after-pop work is over (it left the vein) or a new vein - the pit is closed for piles
        internal static void ClosePit(HarvestController h)
        {
            Per p = P(h);
            p.VoidHas = false;
            p.VoidSpots.Clear();
        }

        // F2: the pile piece's cost for stone or wood, when it can build one now (a hammer, not in a no-build wait)
        internal static bool PileCost(HarvestController h, string item, out int cost)
        {
            cost = 0;
            Humanoid hum = h.GetComponent<Humanoid>();
            return hum != null && CanBuild(h, P(h), hum, item, out cost) && cost > 0;
        }

        // after each finished (or given-up) vein: the hole is free for stone piles for a minute, and the ore is due
        internal static void AfterVein(HarvestController h)
        {
            try
            {
                if (Mode(h) != 3 || InDungeon(h)) return;
                Per p = P(h);
                int first, side;
                Bounds b = VeinBounds(h, out first, out side);
                // D2 (test 23): popped = the rock is gone, or the vein job saw its parts fall in the game's support drop (the
                // game's any-part-left test counted the parts buried out of reach, so a vein with buried parts never read as
                // popped)
                bool popped = h._target == null || !h._target.activeInHierarchy || h.OvSawPop();
                p.OreDue = true;
                // Q2: a popped vein with an ore dump - its ore is carried to storage after what it carries itself
                Vector3 dump;
                if (popped && HarvestController.PitDumpNear(h.transform.position, out dump))
                {
                    p.Collect = true; p.CollectAt = dump; p.CollectGot = false; p.CollectWalkUntil = 0f;
                }
                // F3: no dump - an ore pile next to a full chest that has storage with room now (a new chest)
                else if (popped && OrePileToCarry(h, h.transform.position, out dump))
                {
                    p.Collect = true; p.CollectAt = dump; p.CollectGot = false; p.CollectWalkUntil = 0f;
                }
            }
            catch (Exception e) { Warn("AfterVein: " + e.Message); }
        }

        // F1: a pile, toss or ore trip is going on, the ore is due to go in, or the dump is being carried to storage
        internal static bool TripBusy(HarvestController h)
        {
            Per p = P(h);
            return p.PileActive || p.OreDue || p.Collect;
        }

        // the ore after a vein (called each frame from the job; true = busy with it)
        internal static bool OreDue(HarvestController h)
        {
            Per p = P(h);
            if (p.PileActive || h.OnVein) return false;   // FF3: the ore to the storage waits until it is off the vein
            CompanionAI ai = h.GetComponent<CompanionAI>();
            Humanoid hum = h.GetComponent<Humanoid>();
            if (ai == null || hum == null || ai.PendingDepositContainer != null) return false;
            if (!p.OreDue) return p.Collect && CollectTick(h, p, ai, hum);
            p.OreDue = false;
            bool store = p.OreStore; p.OreStore = false;
            if (Mode(h) != 3 || InDungeon(h)) { p.OreChest = null; p.OreTrip = false; return false; }
            return OreLoad(h, p, ai, hum, afterVein: !store);
        }

        // Q2 (John: "after the vein 'pops' and yes only to their limit"): with player-built storage with room within 20 m
        // of the ore dump, it walks to the dump, picks up the ore lying within 2.5 m of it the way a player presses E,
        // until the next stack would take it over what it can carry, and takes it to the storage; again until the dump is
        // empty or the storage full. Each walk 30 s at most (F2: no cap on the whole of it); a trip that picks up nothing
        // ends it.
        const float CollectNear = 2.5f, CollectWalk = 30f;
        static readonly Collider[] s_collectBuf = new Collider[64];

        static bool CollectTick(HarvestController h, Per p, CompanionAI ai, Humanoid hum)
        {
            float now = Time.time;
            if (Mode(h) != 3) { CollectEnd(p, "the job changed"); return false; }   // F2: no 10 minute cap
            Container store = DropOff(h, p.CollectAt, 20f);
            if (store == null) { CollectEnd(p, "no storage with room near it"); return false; }
            ItemDrop next = null; float bd = float.MaxValue;
            int k = Physics.OverlapSphereNonAlloc(p.CollectAt + Vector3.up, CollectNear, s_collectBuf, LayerMask.GetMask("item"));
            Inventory inv = hum.GetInventory();
            float carry = CompanionTierData.CarryLimit(h);
            bool any = false;
            for (int i = 0; i < k; i++)
            {
                Collider c = s_collectBuf[i];
                ItemDrop d = c != null && c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (d == null || d.m_itemData == null) continue;
                d.Load();
                if (!IsOreItem(d.m_itemData)) continue;
                any = true;
                if (inv.GetTotalWeight() + d.m_itemData.GetWeight() > carry || !inv.CanAddItem(d.m_itemData)) continue;
                float dd = Vector3.Distance(h.transform.position, d.transform.position);
                if (dd < bd) { bd = dd; next = d; }
            }
            if (next == null)
            {
                // nothing more it can take: what it carries goes in; nothing carried = done
                if (HasOre(hum) && p.CollectGot)
                {
                    p.CollectGot = false;
                    if (CompanionsPlugin.LogNormal) Info("Taking the ore from the ore dump to \"" + store.m_name + "\"");
                    h.ResetToIdle();
                    ai.SetPendingDeposit(store, hum, IsOreItem);
                    p.CollectWalkUntil = 0f;
                    return true;
                }
                // F3: the dump is empty - an ore pile next to a full chest whose ore now has storage with room (a new chest) next
                Vector3 pile;
                if (!any && OrePileToCarry(h, p.CollectAt, out pile) && Vector3.Distance(pile, p.CollectAt) > 2f)
                {
                    if (CompanionsPlugin.LogNormal) Info("The ore dump is empty - carrying the ore pile at " + MHR_Mover.Fmt(pile) + " to storage with room");
                    p.CollectAt = pile; p.CollectGot = false; p.CollectWalkUntil = 0f;
                    return true;
                }
                CollectEnd(p, any ? "it can't carry any of what is left" : "the ore dump is empty");
                return false;
            }
            if (p.CollectWalkUntil <= 0f) p.CollectWalkUntil = now + CollectWalk;
            if (now >= p.CollectWalkUntil && !p.CollectGot) { CollectEnd(p, "it couldn't get to the ore dump"); return false; }
            Vector3 me = h.transform.position, at = next.transform.position;
            if (new Vector2(me.x - at.x, me.z - at.z).magnitude <= 1.2f && Mathf.Abs(me.y - at.y) <= 2f)
            {
                ai.StopMoving();
                if (!next.CanPickup(false)) { next.RequestOwn(); return true; }
                if (hum.Pickup(next.gameObject, false, false)) p.CollectGot = true;
                return true;
            }
            ai.MHR_Go(Time.deltaTime, at, 1.0f, false, "Collect");
            return true;
        }

        // F3: ore piles left next to full chests (shared by all companions), and which piles' chests were said to be full
        static readonly List<Vector3> s_orePiles = new List<Vector3>();
        static readonly HashSet<Vector2Int> s_fullSaid = new HashSet<Vector2Int>();
        static Vector2Int PileKey(Vector3 at) { return new Vector2Int(Mathf.RoundToInt(at.x / 4f), Mathf.RoundToInt(at.z / 4f)); }

        // F3: the nearest player-built chest (not a cart or ship) within 30 m of the vein (or of the companion, off the vein)
        // that it may use but that is full (the drop-off's own checks, ChestNo); null = none
        static Container FullChestNear(HarvestController h)
        {
            int f0, s0;
            Bounds b = VeinBounds(h, out f0, out s0);
            Vector3 me = h.transform.position;
            Container best = null; float bd = float.MaxValue;
            foreach (Container c in MHR_Chests.Near(b.center, 30f + Mathf.Max(b.extents.x, b.extents.z)))
            {
                if (c == null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null) continue;
                Vector3 cp = c.transform.position;
                if (Vector3.Distance(b.ClosestPoint(cp), cp) > 30f) continue;
                if (ChestNo(h, c) != "full") continue;
                float d = Vector3.Distance(me, cp);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // F3: the ore pile by a full chest within 25 m of `at` that has storage with room within 20 m of it now
        static bool OrePileToCarry(HarvestController h, Vector3 at, out Vector3 pile)
        {
            pile = Vector3.zero;
            s_orePiles.RemoveAll(op => !OreLying(op));
            foreach (Vector3 op in s_orePiles)
            {
                if (Vector3.Distance(op, at) > 25f) continue;
                if (DropOff(h, op, 20f) == null) continue;
                pile = op; return true;
            }
            return false;
        }

        // F3: ore still lying within 2.5 m of the pile point
        static bool OreLying(Vector3 at)
        {
            int k = Physics.OverlapSphereNonAlloc(at + Vector3.up, CollectNear, s_collectBuf, LayerMask.GetMask("item"));
            for (int i = 0; i < k; i++)
            {
                Collider c = s_collectBuf[i];
                ItemDrop d = c != null && c.attachedRigidbody != null ? c.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (d != null && d.m_itemData != null) { d.Load(); if (IsOreItem(d.m_itemData)) return true; }
            }
            return false;
        }

        static void CollectEnd(Per p, string why)
        {
            if (p.Collect && CompanionsPlugin.LogNormal) Info("Done carrying from the ore dump (" + why + ")");
            p.Collect = false; p.CollectGot = false; p.CollectWalkUntil = 0f;
        }

        // where to stand to build at a spot: 2 m back toward where it is now
        static Vector3 StandFor(HarvestController h, Vector3 spot)
        {
            Vector3 d = h.transform.position - spot; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            Vector3 stand = spot + d.normalized * 2f;
            stand.y = Ground(stand);
            return stand;
        }

        static int s_buildBlockMask = -1, s_groundMask = -1;

        // the spots to try: in the hole the vein left (for a minute after it popped), else 2-4 m outside the hole on the
        // side it's working (the hole = the vein's box + 1.5 m), level with it along that side and 1.5 / 3 m to each side
        static List<Vector3> BuildCandidates(HarvestController h, Per p, out Bounds hole)
        {
            List<Vector3> list = new List<Vector3>();
            int first, side;
            Bounds b = VeinBounds(h, out first, out side);
            hole = b; hole.Expand(new Vector3(3f, 0f, 3f));
            if (p.VoidHas && Time.time < p.VoidUntil)
            {
                // D4: every 2 m of the popped vein's pit ground, nearest the companion first
                Vector3 me = h.transform.position;
                list.AddRange(p.VoidSpots);
                // F2: with a stone dump, the pile goes next to it (the spots nearest the dump first)
                Vector3 sd;
                if (p.PileItem == "Stone" && h.OvStoneDumpHas(out sd)) me = sd;
                list.Sort((Vector3 x, Vector3 y) => (x - me).sqrMagnitude.CompareTo((y - me).sqrMagnitude));
                return list;
            }
            Vector3 n;
            if (side >= 0) n = HarvestController.SideDirOf(side);
            else
            {
                n = h.transform.position - b.center; n.y = 0f;
                if (n.sqrMagnitude < 0.01f) n = Vector3.forward;
                n.Normalize();
            }
            Vector3 along = new Vector3(-n.z, 0f, n.x);
            Vector3 edge = b.center + Vector3.Scale(n, b.extents) + n * 1.5f;
            float lat = Vector3.Dot(h.transform.position - edge, along);
            float[] outs = { 3f, 2f, 4f };
            float[] lats = { 0f, 1.5f, -1.5f, 3f, -3f };
            foreach (float o in outs)
                foreach (float l in lats)
                    list.Add(edge + n * o + along * (lat + l));
            return list;
        }

        // flat open ground for a stone pile, inside your build rules (the game's: no other ward, no no-build area, the
        // piece's station if it lists one)
        static bool FindBuildSpot(HarvestController h, Per p, out string why)
        {
            why = null;
            int cost;
            GameObject prefab = PilePiece(p.PileItem, out cost);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece == null) { why = "no pile piece"; return false; }
            if (s_buildBlockMask < 0) s_buildBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "character", "character_net", "vehicle", "item");
            if (s_groundMask < 0) s_groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            bool noBench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            Bounds hole;
            List<Vector3> cands = BuildCandidates(h, p, out hole);
            bool inVoid = p.VoidHas && Time.time < p.VoidUntil;
            float yaw = h.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            int notFlat = 0, blocked = 0, rules = 0, station = 0;
            foreach (Vector3 c in cands)
            {
                // D4: the ground there passes the game's placement rules for the pile piece (was: 5 terrain rays within 0.35 m)
                Vector3 spot;
                if (!GamePlaceOk(piece, c, out spot)) { notFlat++; continue; }
                if (!inVoid)
                {
                    Bounds hb = hole; hb.Expand(new Vector3(0f, 200f, 0f));
                    if (hb.Contains(spot)) { blocked++; continue; }
                }
                if (p.OreHas && Vector3.Distance(spot, p.OreSpot) < 2.5f) { blocked++; continue; }
                if (Physics.CheckBox(spot + Vector3.up * 1.0f, new Vector3(1.0f, 0.8f, 1.0f), rot, s_buildBlockMask, QueryTriggerInteraction.Ignore)) { blocked++; continue; }
                if (!PrivateArea.CheckAccess(spot, 0f, flash: false) || Location.IsInsideNoBuildLocation(spot)) { rules++; continue; }
                if (!noBench && piece.m_craftingStation != null && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, spot) == null) { station++; continue; }
                p.BuildSpot = spot; p.BuildRot = rot; p.BuildHas = true;
                return true;
            }
            why = station > 0 ? "it needs a " + MHR_List.Local(piece.m_craftingStation.m_name) + " nearby" : rules > 0 ? "a ward or no-build area" : notFlat >= blocked ? "no ground the game lets it go on" : "no free ground";
            if (CompanionsPlugin.LogDetailed) Debug("No " + PileWord(p.PileItem) + " pile spot (" + (inVoid ? "in the hole" : "outside the hole") + "): " + notFlat + " not allowed by the game's placement rules, " + blocked + " in the way, " + rules + " ward/no-build, " + station + " no station");
            return false;
        }

        // D4: the player's placement rules for a piece on the ground under `at` (Player.UpdatePlacementGhost, the checks on
        // what its placement ray hit: water, ground only, cultivated / vegetation ground, not on a tilting surface (normal.y
        // under 0.8), ceiling only, not on a floor). The point is where the pile goes.
        static int s_waterMask = -1;
        static bool GamePlaceOk(Piece piece, Vector3 at, out Vector3 point)
        {
            point = at;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(at.x, Ground(at) + 3f, at.z), Vector3.down, out hit, 8f, s_groundMask)) return false;
            point = hit.point;
            Vector3 normal = hit.normal;
            Heightmap heightmap = hit.collider.GetComponent<Heightmap>();
            if (s_waterMask < 0) s_waterMask = LayerMask.GetMask("Water");
            RaycastHit wh;
            bool inWater = Physics.Raycast(new Vector3(at.x, hit.point.y + 3f, at.z), Vector3.down, out wh, 3f, s_waterMask, QueryTriggerInteraction.Collide) && wh.point.y > hit.point.y;
            if (piece.m_noInWater && inWater) return false;
            if (piece.m_waterPiece && !inWater) return false;
            if ((piece.m_groundPiece || piece.m_groundOnly) && heightmap == null) return false;
            if (piece.m_cultivatedGroundOnly && (heightmap == null || !heightmap.IsCultivated(point))) return false;
            if (piece.m_vegetationGroundOnly)
            {
                if (heightmap == null) return false;
                Heightmap.Biome biome = heightmap.GetBiome(point);
                float mask = heightmap.GetVegetationMask(point);
                if (biome == Heightmap.Biome.AshLands ? mask > 0.1f : mask < 0.25f) return false;
            }
            if (piece.m_notOnTiltingSurface && normal.y < 0.8f) return false;
            if (piece.m_inCeilingOnly && normal.y > -0.5f) return false;
            if (piece.m_notOnFloor && normal.y > 0.1f) return false;
            return true;
        }

        // at the build spot: hammer out, one pile per work beat while it has enough stone. False = done.
        static bool BuildTick(HarvestController h, Per p, CompanionAI ai)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            Character ch = h.GetComponent<Character>();
            Inventory bag = hum != null ? hum.GetInventory() : null;
            int cost;
            GameObject prefab = PilePiece(p.PileItem, out cost);
            if (bag == null || prefab == null || CountIn(bag, p.PileItem) < cost || Time.time >= p.PileUntil) return false;
            ItemDrop.ItemData hammer = MHR_PileTaker.Hammer(hum);
            if (hammer == null) return false;
            CompanionSetup cs = h.GetComponent<CompanionSetup>();
            if (!hum.IsItemEquiped(hammer))
            {
                if (hum.InAttack()) return true;
                if (cs != null) cs.SuppressAutoEquip = true;
                hum.EquipItem(hammer);
                p.NextAct = Time.time + 0.6f;
                return true;
            }
            Vector3 aim = p.BuildSpot + Vector3.up * 0.4f;
            ai.StopMoving();
            ai.LookAtPoint(aim);
            if (Time.time < p.NextAct || ch.InAttack()) return true;
            float stamina = BuildStamina(ch, hammer);
            CompanionStamina st = h.GetComponent<CompanionStamina>();
            if (st != null && st.Stamina < stamina) return true;
            // still free, inside the rules?
            if (Physics.CheckBox(p.BuildSpot + Vector3.up * 1.0f, new Vector3(1.0f, 0.8f, 1.0f), p.BuildRot, s_buildBlockMask, QueryTriggerInteraction.Ignore))
            {
                string why;
                if (!FindBuildSpot(h, p, out why)) return false;
                p.PilePoint = StandFor(h, p.BuildSpot); p.Arrived = false;
                return true;
            }
            PlaceStonePile(h, p, prefab, cost, hammer, stamina);
            p.NextAct = Time.time + 1.0f;
            if (CountIn(bag, p.PileItem) < cost) return false;
            string why2;
            if (!FindBuildSpot(h, p, out why2)) return false;
            p.PilePoint = StandFor(h, p.BuildSpot);
            if (Vector3.Distance(h.transform.position, p.BuildSpot) > 3f) p.Arrived = false;
            return true;
        }

        // the hammer's swing, less up to half by the build skill (as Repair and Restock's piles)
        static float BuildStamina(Character ch, ItemDrop.ItemData hammer)
        {
            float cost = hammer.m_shared.m_attack != null ? hammer.m_shared.m_attack.m_attackStamina : 0f;
            PieceTable table = hammer.m_shared.m_buildPieces;
            if (table != null && table.m_skill != Skills.SkillType.None && ch != null) cost -= cost * 0.5f * ch.GetSkillFactor(table.m_skill);
            return Mathf.Max(0f, cost);
        }

        // what Player.PlacePiece does, then the stone, stamina and hammer wear (as UpkeepController.PlacePile)
        static void PlaceStonePile(HarvestController h, Per p, GameObject prefab, int cost, ItemDrop.ItemData hammer, float stamina)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            Character ch = h.GetComponent<Character>();
            TerrainModifier.SetTriggerOnPlaced(trigger: true);
            GameObject go = UnityEngine.Object.Instantiate(prefab, p.BuildSpot, p.BuildRot);
            TerrainModifier.SetTriggerOnPlaced(trigger: false);
            Piece piece = go.GetComponent<Piece>();
            if (piece != null)
            {
                PlatformUserID user = default(PlatformUserID);
                try { user = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID; } catch { }
                piece.SetCreator(MHR_Owner.CreatorId(h), user);
            }
            go.GetComponent<WearNTear>()?.OnPlaced();
            string anim = hammer.m_shared.m_attack?.m_attackAnimation;
            h.GetComponent<ZSyncAnimation>()?.SetTrigger(!string.IsNullOrEmpty(anim) ? anim : "swing_hammer");
            if (piece != null && ch != null) piece.m_placeEffect.Create(p.BuildSpot, p.BuildRot, go.transform, 1f, -1, ch.GetZDOID());
            Inventory bag = hum.GetInventory();
            RemoveStone(bag, p.PileItem, cost);
            h.GetComponent<CompanionStamina>()?.UseStamina(stamina);
            if (hammer.m_shared.m_useDurability) hammer.m_durability = Mathf.Max(0f, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain * Game.m_durabilityRate);
            p.PackFull = false;
            if (CompanionsPlugin.LogNormal) Info("Built a " + (piece != null ? MHR_List.Local(piece.m_name) : prefab.name) + " at " + MHR_Mover.Fmt(p.BuildSpot) + " from " + cost + " " + PileWord(p.PileItem));
        }

        static void RemoveStone(Inventory bag, string item, int n)
        {
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(bag.GetAllItems());
            for (int i = 0; i < items.Count && n > 0; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (it == null || it.m_dropPrefab == null || it.m_dropPrefab.name != item) continue;
                int take = Mathf.Min(n, it.m_stack);
                bag.RemoveItem(it, take);
                n -= take;
            }
        }

        // ore: what's on the Ore list (never stone)
        internal static bool IsOreItem(ItemDrop.ItemData it)
        {
            if (it == null || it.m_dropPrefab == null || it.m_equipped) return false;
            string name = it.m_dropPrefab.name;
            if (name == "Stone") return false;
            MHR_List list = Parse(3);
            if (list != null) return list.Allows(name, ItemLocal(name));
            // H6: the game's own ore list (MHR_Ore: what the coal smelters take, plus obsidian, chitin, black marble, soft tissue)
            return MHR_Ore.IsOre(name);
        }

        // 1.5.97 (fix 4): where it goes next after a vein - what "in a hole" is measured against. A chest within 20 m if
        // there is one; else, with ore, the ore spot; else you (Follow on) or where the job started.
        internal static bool AfterVeinGoal(HarvestController h, out Vector3 goal, out string what)
        {
            goal = h.transform.position; what = null;
            try
            {
                Humanoid hum = h.GetComponent<Humanoid>();
                if (hum == null || hum.GetInventory() == null) return false;
                Container c = DropOff(h, h.transform.position, 20f);
                if (c != null) { goal = c.transform.position; what = "the chest \"" + c.m_name + "\""; return true; }
                bool any = false;
                foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems()) if (IsOreItem(it)) { any = true; break; }
                if (any)
                {
                    Per p = P(h);
                    EnsureOreSpot(h, p);
                    goal = p.OreSpot; what = "the ore spot";
                    return true;
                }
                return h.AfterVeinFallback(out goal, out what);
            }
            catch (Exception e) { Warn("AfterVeinGoal: " + e.Message); return false; }
        }

        // T3 (John: "They should head out when they hit 90+ percent capacity"): off the vein to this chest with its ore;
        // once off the vein OreDue puts only the ore in it. False = busy with a pile or dump trip.
        internal static bool OreToChest(HarvestController h, Container c, Vector3 pitAt, float pitR)
        {
            Per p = P(h);
            if (c == null || p.PileActive) return false;
            p.OreDue = true; p.OreStore = true; p.OreChest = c;
            p.OreTrip = true; p.OreTripAt = pitAt; p.OreTripR = pitR;
            h.OvToStorage(c);
            return true;
        }

        // all the ore into a chest within 20 m, else all of it on the ore spot. False = no ore to unload.
        static bool OreLoad(HarvestController h, Per p, CompanionAI ai, Humanoid hum, bool afterVein = false)
        {
            bool any = false;
            foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems()) if (IsOreItem(it)) { any = true; break; }
            if (!any) return false;
            string why = afterVein ? "Done with the vein" : "Too heavy";
            // T3: the trip to the chest by the vein goes to that chest while it is usable. W5 (test 26: Comp's trip lost its
            // chest after its way up and left its ore on the ore spot in the pit): if not, why is said, and a chest is looked
            // for round the pit the way the 90% trip does; then the normal search within 20 m of itself
            Container c = null;
            if (p.OreChest != null)
            {
                string no = ChestNo(h, p.OreChest);
                if (no == null) c = p.OreChest;
                else if (CompanionsPlugin.LogNormal) Info("The chest it was taking its ore to (\"" + p.OreChest.m_name + "\") can't be used now: " + no + " - looking round the pit");
            }
            else if (p.OreTrip && CompanionsPlugin.LogNormal) Info("The chest it was taking its ore to is no longer set - looking round the pit");
            if (c == null && p.OreTrip)
            {
                ItemDrop.ItemData ore = null;
                foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems()) if (IsOreItem(it)) { ore = it; break; }
                if (ore != null) c = HarvestController.OvTripChest(h, p.OreTripAt, p.OreTripR, ore);
            }
            if (c != null && p.OreTrip) why = "Near its carry limit";
            if (c == null) c = DropOff(h, h.transform.position, 20f);
            p.OreChest = null;
            p.OreTrip = false;
            if (c != null)
            {
                s_fullSaid.Remove(PileKey(c.transform.position));   // F3: a chest with room again - it may ask again later
                if (CompanionsPlugin.LogNormal) Info(why + " - putting the ore in \"" + c.m_name + "\" " + Vector3.Distance(h.transform.position, c.transform.position).ToString("F0") + "m away");
                h.ResetToIdle();
                h.RestoreLoadout();
                ai.SetPendingDeposit(c, hum, IsOreItem);
                p.TooHeavy = false; p.PackFull = false;
                return true;
            }
            // F3 (John 11-21 23:40Z: "If those chests are full make a pile of ore near them and ask for another chest to be
            // built"): every player-built chest within 30 m of the vein is full - the ore goes in a pile by the nearest one
            Container full = FullChestNear(h);
            if (full != null)
            {
                Vector3 cp = full.transform.position;
                p.Trip = YardTrip.Ore; p.OreByChest = true; p.OreChestAt = cp;
                p.PilePoint = cp; p.PileActive = true; p.PileUntil = Time.time + 25f;
                if (CompanionsPlugin.LogNormal) Info(why + " and every chest by the vein is full - taking the ore to a pile next to \"" + full.m_name + "\" at " + MHR_Mover.Fmt(cp));
                return true;
            }
            EnsureOreSpot(h, p);
            p.Trip = YardTrip.Ore;
            p.PilePoint = p.OreSpot; p.PileActive = true; p.PileUntil = Time.time + 25f;
            if (CompanionsPlugin.LogNormal) Info(why + " and no chest within 20 m - taking the ore to the ore spot by the vein");
            return true;
        }

        // at the ore spot: every ore stack, the player's way, facing away from the vein
        static void DropOre(HarvestController h, Per p)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null) return;
            Inventory inv = hum.GetInventory();
            Vector3 away = h.transform.position - p.YardFor; away.y = 0f;
            if (away.sqrMagnitude > 0.01f) h.transform.rotation = Quaternion.LookRotation(away.normalized);
            // FF3: on the vein's ore dump with player-built storage (chest, cart, ship) with room within 20 m: only enough to
            // get under its weight limit, then the rest goes out to the storage; no storage: all of it
            bool byChest = p.OreByChest; p.OreByChest = false;
            if (byChest)
            {
                // F3: facing the full chest, so the pile lands between it and the companion (it stops within 1.5 m of it)
                Vector3 to = p.OreChestAt - h.transform.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f) h.transform.rotation = Quaternion.LookRotation(to.normalized);
                int k = 0;
                List<ItemDrop.ItemData> its = new List<ItemDrop.ItemData>(inv.GetAllItems());
                foreach (ItemDrop.ItemData it in its) { if (!IsOreItem(it)) continue; int count = it.m_stack; if (PlayerDrop(hum, it, count)) k += count; }
                p.PackFull = false;
                if (k > 0)
                {
                    Vector3 key = p.OreChestAt;
                    bool known = false;
                    foreach (Vector3 op in s_orePiles) if (Vector3.Distance(op, key) <= 2f) { known = true; break; }
                    if (!known) s_orePiles.Add(key);
                    if (CompanionsPlugin.LogNormal) Info("Every chest by the vein is full - left " + k + " ore in a pile next to the chest at " + MHR_Mover.Fmt(key));
                    if (s_fullSaid.Add(PileKey(key)))
                        h.GetComponent<CompanionTalk>()?.SayProblem("chests_full", ModLocalization.Loc("hc_speech_chests_full"));
                }
                return;
            }
            bool pit = p.PitDump; p.PitDump = false;
            Container store = pit ? DropOff(h, h.transform.position, 20f) : null;
            if (store != null)
            {
                int k = DropOreTo(hum, h.HeavyLimit, null);
                p.PackFull = false;
                if (CompanionsPlugin.LogNormal) Info("On the ore dump: left " + k + " ore to get under its weight limit - taking the rest to \"" + store.m_name + "\"");
                if (HasOre(hum)) { p.OreDue = true; p.OreStore = true; h.OvToStorage(store); }
                return;
            }
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (!IsOreItem(it)) continue;
                int count = it.m_stack;
                if (PlayerDrop(hum, it, count)) n += count;
            }
            p.PackFull = false;
            if (n > 0)
            {
                if (CompanionsPlugin.LogNormal) Info("Left " + n + " ore on the ore spot at " + MHR_Mover.Fmt(p.PilePoint));
                h.GetComponent<CompanionTalk>()?.SayProblem("ore_left_here", ModLocalization.Loc("hc_speech_ore_left_here"));
            }
        }
    }
}
