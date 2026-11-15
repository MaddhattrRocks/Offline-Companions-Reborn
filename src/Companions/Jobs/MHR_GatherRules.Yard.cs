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
                    if (CanBuild(h, p, hum, item, out cost) && have >= cost) return true;
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
                if (CanBuild(h, p, hum, item, out cost) && have >= cost)
                {
                    string why;
                    p.PileItem = item;
                    if (FindBuildSpot(h, p, out why))
                    {
                        p.Trip = YardTrip.Build; p.Arrived = false;
                        p.PilePoint = StandFor(h, p.BuildSpot);
                        p.PileActive = true; p.PileUntil = Time.time + 25f;
                        if (CompanionsPlugin.LogNormal) Info("Building a " + PileWord(item) + " pile (" + have + " " + PileWord(item) + ", " + cost + " a pile)");
                        return true;
                    }
                    p.NoBuildUntil = Time.time + 60f;
                    if (Time.time >= p.NextNoBuildLog)
                    {
                        p.NextNoBuildLog = Time.time + 120f;
                        if (CompanionsPlugin.LogNormal) Info("Can't build a " + PileWord(item) + " pile here (" + why + ") - tossing the " + PileWord(item) + " instead");
                    }
                    return StartToss(h, p, item);
                }
                if (h.IsOverweight()) return StartToss(h, p, item);
            }
            return false;
        }

        static bool StartToss(HarvestController h, Per p, string item)
        {
            p.Trip = YardTrip.Toss; p.PileItem = item; p.TossJunk = false;
            p.PileActive = true; p.PileUntil = Time.time + 30f; p.NextToss = Time.time + 0.2f;
            p.PilePoint = h.transform.position;
            if (CompanionsPlugin.LogNormal) Info("Too heavy - tossing its " + PileWord(item) + " off to one side");
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
            if (hum == null || hum.GetInventory() == null || HeaviestJunk(hum) == null) return false;
            p.Trip = YardTrip.Toss; p.TossJunk = true;
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
            if (p.TossJunk)
            {
                // item G: only until it's under the limit again (weight, or a free slot when the pack was full)
                if (!h.IsOverweight()) return false;
                stack = HeaviestJunk(hum);
                if (stack == null) return false;
            }
            else
            {
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
            string tossed = p.TossJunk ? (stack.m_dropPrefab != null ? stack.m_dropPrefab.name : stack.m_shared.m_name) : PileWord(p.PileItem);
            if (PlayerDrop(hum, stack, n))
            {
                if (p.TossJunk) { if (CompanionsPlugin.LogNormal) Info("Tossed " + n + " " + tossed + " (junk) to get under its weight limit"); }
                else if (CompanionsPlugin.LogDetailed) Debug("Tossed " + n + " " + tossed + " toward " + MHR_Mover.Fmt(h.transform.position + dir * 3f));
            }
            p.NextToss = Time.time + 0.5f;
            return true;
        }

        // ---- the player's own drop (Humanoid.DropItem), with the player's no-auto-pickup mark (ItemDrop.OnPlayerDrop):
        // the game only marks it when the dropper is a player, so the postfix below adds it for this call ----
        [ThreadStatic] static bool s_playerDrop;

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
                if (s_playerDrop && __result != null) __result.OnPlayerDrop();
            }
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
                bool popped = h._target == null || !h._target.activeInHierarchy;
                if (h._target != null)
                {
                    MineRock5 r = h._target.GetComponent<MineRock5>();
                    bool? left = r != null ? MHR_Vein.AnyLeft(r) : null;
                    if (left.HasValue) popped = !left.Value;
                }
                if (popped) { p.VoidHas = true; p.VoidAt = new Vector3(b.center.x, Ground(b.center), b.center.z); p.VoidUntil = Time.time + 60f; }
                p.OreDue = true;
            }
            catch (Exception e) { Warn("AfterVein: " + e.Message); }
        }

        // the ore after a vein (called each frame from the job; true = busy with it)
        internal static bool OreDue(HarvestController h)
        {
            Per p = P(h);
            if (!p.OreDue || p.PileActive) return false;
            CompanionAI ai = h.GetComponent<CompanionAI>();
            Humanoid hum = h.GetComponent<Humanoid>();
            if (ai == null || hum == null || ai.PendingDepositContainer != null) return false;
            p.OreDue = false;
            if (Mode(h) != 3 || InDungeon(h)) return false;
            return OreLoad(h, p, ai, hum, afterVein: true);
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
                list.Add(p.VoidAt);
                for (int k = 0; k < 4; k++) list.Add(p.VoidAt + Quaternion.Euler(0f, k * 90f, 0f) * Vector3.forward * 1f);
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
                // on the ground (terrain), level enough to stand a pile on
                float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                for (int k = 0; k < 5 && ok; k++)
                {
                    Vector3 q = c + rot * (k == 0 ? Vector3.zero : new Vector3((k & 1) == 0 ? 0.9f : -0.9f, 0f, k < 3 ? 0.9f : -0.9f));
                    q.y = c.y;
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(q.x, Ground(q) + 3f, q.z), Vector3.down, out hit, 8f, s_groundMask) || hit.collider.GetComponent<Heightmap>() == null) { ok = false; break; }
                    lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y);
                }
                if (!ok || hi - lo > 0.35f) { notFlat++; continue; }
                Vector3 spot = new Vector3(c.x, lo, c.z);
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
            why = station > 0 ? "it needs a " + MHR_List.Local(piece.m_craftingStation.m_name) + " nearby" : rules > 0 ? "a ward or no-build area" : notFlat >= blocked ? "no flat ground" : "no free ground";
            if (CompanionsPlugin.LogDetailed) Debug("No " + PileWord(p.PileItem) + " pile spot (" + (inVoid ? "in the hole" : "outside the hole") + "): " + notFlat + " not flat, " + blocked + " in the way, " + rules + " ward/no-build, " + station + " no station");
            return false;
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
            return name.IndexOf("Ore", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Scrap", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Obsidian", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Flametal", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("BlackMetal", StringComparison.OrdinalIgnoreCase) >= 0;
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

        // all the ore into a chest within 20 m, else all of it on the ore spot. False = no ore to unload.
        static bool OreLoad(HarvestController h, Per p, CompanionAI ai, Humanoid hum, bool afterVein = false)
        {
            bool any = false;
            foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems()) if (IsOreItem(it)) { any = true; break; }
            if (!any) return false;
            string why = afterVein ? "Done with the vein" : "Too heavy";
            Container c = DropOff(h, h.transform.position, 20f);
            if (c != null)
            {
                if (CompanionsPlugin.LogNormal) Info(why + " - putting the ore in \"" + c.m_name + "\" " + Vector3.Distance(h.transform.position, c.transform.position).ToString("F0") + "m away");
                h.ResetToIdle();
                h.RestoreLoadout();
                ai.SetPendingDeposit(c, hum, IsOreItem);
                p.TooHeavy = false; p.PackFull = false;
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
