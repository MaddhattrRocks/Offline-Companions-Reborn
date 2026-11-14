using System;
using System.Collections.Generic;
using Splatform;
using UnityEngine;

namespace Companions
{
    internal enum YardTrip { Stone, Build, Ore }

    // ---- 1.5.95: the Ore job's yard by the vein (John: "Ore: into a chest within 20 m, else all dropped in one spot on
    // the ground. Stone: handled AS they dig so they never get overloaded: with a hammer, build a stone pile whenever
    // carrying enough for one (on flat ground near the vein, like a player builds, consuming the stone); without a hammer,
    // drop stone at a separate stone spot away from the ore spot whenever it starts to weigh them down. Leftover stone
    // goes to the stone spot. Never into a chest.") ----
    public static partial class MHR_GatherRules
    {
        // stone starts to weigh it down: this share of what it can carry
        const float StoneHeavyShare = 0.5f;
        const float StoneLoadShare = 0.25f;
        const float YardReuse = 25f;

        static GameObject s_stonePile; static int s_stonePileCost; static bool s_stonePileLooked;

        // the game's Stone pile piece and how much stone it takes
        static GameObject StonePile(out int cost)
        {
            if (!s_stonePileLooked && ZNetScene.instance != null)
            {
                s_stonePileLooked = true;
                GameObject go = ZNetScene.instance.GetPrefab("stone_pile");
                Piece pc = go != null ? go.GetComponent<Piece>() : null;
                if (pc != null && pc.m_resources != null && pc.m_resources.Length == 1 && pc.m_resources[0].m_resItem != null && pc.m_resources[0].m_resItem.gameObject.name == "Stone")
                {
                    s_stonePile = go; s_stonePileCost = Mathf.Max(1, pc.m_resources[0].m_amount);
                }
                else Warn("No Stone pile piece found (stone_pile) - stone is dropped on the stone spot instead");
            }
            cost = s_stonePileCost;
            return s_stonePile;
        }

        // is the stone due to be dealt with? (checked about once a second while mining, and before each scan)
        internal static bool StoneDue(HarvestController h, bool now = false)
        {
            try
            {
                if (Mode(h) != 3) return false;
                Per p = P(h);
                if (p.PileActive) return false;
                if (!now && Time.time < p.NextStoneCheck) return false;
                p.NextStoneCheck = Time.time + 1f;
                Humanoid hum = h.GetComponent<Humanoid>();
                Inventory inv = hum != null ? hum.GetInventory() : null;
                if (inv == null) return false;
                int stone = CountIn(inv, "Stone");
                if (stone <= 0) return false;
                int cost;
                if (CanBuild(h, p, hum, out cost) && stone >= cost) return true;
                // no pile to build: once the stone itself is a real load (a quarter of what it can carry), or the pack
                // as a whole is getting heavy with a fair amount of stone in it
                float limit = CompanionTierData.CarryLimit(h);
                float stoneWeight = StoneWeight(inv);
                return stoneWeight >= StoneLoadShare * limit || (inv.GetTotalWeight() >= StoneHeavyShare * limit && stone >= 20);
            }
            catch (Exception e) { Warn("StoneDue: " + e.Message); return false; }
        }

        static bool CanBuild(HarvestController h, Per p, Humanoid hum, out int cost)
        {
            GameObject prefab = StonePile(out cost);
            return prefab != null && Time.time >= p.NoBuildUntil && MHR_PileTaker.Hammer(hum) != null;
        }

        // the ore spot and the stone spot by the vein: apart from each other, off to the sides, not on the way back to it
        static void EnsureYard(HarvestController h, Per p)
        {
            Vector3 me = h.transform.position;
            Vector3 vein = h._attackNodePos != Vector3.zero ? h._attackNodePos : (h._target != null ? h._target.transform.position : me);
            if (p.PileHas && p.OreHas && Vector3.Distance(me, p.YardFor) <= YardReuse) return;
            Vector3 dir = me - vein; dir.y = 0f;
            if (dir.sqrMagnitude < 0.25f) dir = h.transform.right;
            dir.Normalize();
            Vector3 stone = vein + (Quaternion.Euler(0f, 60f, 0f) * dir) * 6f;
            Vector3 ore = vein + (Quaternion.Euler(0f, -60f, 0f) * dir) * 4f;
            stone.y = Ground(stone); ore.y = Ground(ore);
            p.PileSpot = stone; p.PileHas = true;
            p.OreSpot = ore; p.OreHas = true;
            p.YardFor = vein;
            p.BuildHas = false;
            if (CompanionsPlugin.LogDetailed) Debug("Yard for the vein at " + MHR_Mover.Fmt(vein) + ": ore spot " + MHR_Mover.Fmt(ore) + ", stone spot " + MHR_Mover.Fmt(stone));
        }

        // Ore job: deal with the stone it carries - build stone piles with a hammer, else drop it on the stone spot
        public static bool StartPile(HarvestController h, bool leftover = false)
        {
            Per p = P(h);
            if (p.PileActive) return true;
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null) return false;
            int stone = CountIn(hum.GetInventory(), "Stone");
            if (stone <= 0) return false;
            EnsureYard(h, p);
            int cost;
            if (!leftover && CanBuild(h, p, hum, out cost) && stone >= cost)
            {
                string why;
                if (FindBuildSpot(h, p, out why))
                {
                    p.Trip = YardTrip.Build; p.Arrived = false;
                    p.PilePoint = StandFor(h, p.BuildSpot);
                    p.PileActive = true; p.PileUntil = Time.time + 25f;
                    if (CompanionsPlugin.LogNormal) Info("Building a stone pile out of the way (" + stone + " stone, " + cost + " a pile)");
                    return true;
                }
                p.NoBuildUntil = Time.time + 60f;
                if (Time.time >= p.NextNoBuildLog)
                {
                    p.NextNoBuildLog = Time.time + 120f;
                    if (CompanionsPlugin.LogNormal) Info("Can't build a stone pile here (" + why + ") - dropping the stone on the stone spot instead");
                }
            }
            p.Trip = YardTrip.Stone;
            p.PilePoint = p.PileSpot; p.PileActive = true; p.PileUntil = Time.time + 25f;
            if (CompanionsPlugin.LogNormal) Info(leftover ? "Done here - taking the leftover stone to the stone spot" : "Taking the stone to the stone spot out of the way");
            return true;
        }

        static bool LeftoverStone(HarvestController h)
        {
            Per p = P(h);
            if (p.PileActive || !p.PileHas || Vector3.Distance(h.transform.position, p.PileSpot) > 30f) return false;
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null || CountIn(hum.GetInventory(), "Stone") <= 0) return false;
            return StartPile(h, leftover: true);
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

        // flat open ground near the stone spot, clear of the vein and the ore spot, inside your build rules
        static bool FindBuildSpot(HarvestController h, Per p, out string why)
        {
            why = null;
            int cost;
            GameObject prefab = StonePile(out cost);
            Piece piece = prefab != null ? prefab.GetComponent<Piece>() : null;
            if (piece == null) { why = "no Stone pile piece"; return false; }
            if (s_buildBlockMask < 0) s_buildBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "character", "character_net", "vehicle", "item");
            if (s_groundMask < 0) s_groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            bool noBench = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench);
            Bounds vein = new Bounds(p.YardFor, Vector3.one);
            if (h._target != null) foreach (Collider c in h._target.GetComponentsInChildren<Collider>()) if (c != null && c.gameObject.activeSelf) vein.Encapsulate(c.bounds);
            float yaw = h.transform.eulerAngles.y;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            int notFlat = 0, blocked = 0, rules = 0, station = 0;
            for (int ring = 0; ring < 3; ring++)
            {
                float r = ring * 1.75f;
                int steps = ring == 0 ? 1 : 8;
                for (int i = 0; i < steps; i++)
                {
                    float ang = i * Mathf.PI * 2f / steps;
                    Vector3 c = p.PileSpot + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                    // on the ground (terrain), level enough to stand a pile on
                    float lo = float.MaxValue, hi = float.MinValue; bool ok = true;
                    for (int k = 0; k < 5 && ok; k++)
                    {
                        Vector3 q = c + rot * (k == 0 ? Vector3.zero : new Vector3((k & 1) == 0 ? 0.9f : -0.9f, 0f, k < 3 ? 0.9f : -0.9f));
                        RaycastHit hit;
                        if (!Physics.Raycast(q + Vector3.up * 3f, Vector3.down, out hit, 8f, s_groundMask) || hit.collider.GetComponent<Heightmap>() == null) { ok = false; break; }
                        lo = Mathf.Min(lo, hit.point.y); hi = Mathf.Max(hi, hit.point.y);
                    }
                    if (!ok || hi - lo > 0.35f) { notFlat++; continue; }
                    Vector3 spot = new Vector3(c.x, lo, c.z);
                    Bounds vb = vein; vb.Expand(new Vector3(4f, 100f, 4f));
                    if (vb.Contains(spot) || (p.OreHas && Vector3.Distance(spot, p.OreSpot) < 3f)) { blocked++; continue; }
                    if (Physics.CheckBox(spot + Vector3.up * 1.0f, new Vector3(1.0f, 0.8f, 1.0f), rot, s_buildBlockMask, QueryTriggerInteraction.Ignore)) { blocked++; continue; }
                    if (!PrivateArea.CheckAccess(spot, 0f, flash: false) || Location.IsInsideNoBuildLocation(spot)) { rules++; continue; }
                    if (!noBench && piece.m_craftingStation != null && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, spot) == null) { station++; continue; }
                    p.BuildSpot = spot; p.BuildRot = rot; p.BuildHas = true;
                    return true;
                }
            }
            why = station > 0 ? "it needs a " + MHR_List.Local(piece.m_craftingStation.m_name) + " nearby" : rules > 0 ? "a ward or no-build area" : notFlat >= blocked ? "no flat ground" : "no free ground";
            if (CompanionsPlugin.LogDetailed) Debug("No stone pile spot near " + MHR_Mover.Fmt(p.PileSpot) + ": " + notFlat + " not flat, " + blocked + " in the way, " + rules + " ward/no-build, " + station + " no station");
            return false;
        }

        // at the build spot: hammer out, one pile per work beat while it has enough stone. False = done.
        static bool BuildTick(HarvestController h, Per p, CompanionAI ai)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            Character ch = h.GetComponent<Character>();
            Inventory bag = hum != null ? hum.GetInventory() : null;
            int cost;
            GameObject prefab = StonePile(out cost);
            if (bag == null || prefab == null || CountIn(bag, "Stone") < cost || Time.time >= p.PileUntil) return false;
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
            if (CountIn(bag, "Stone") < cost) return false;
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
            RemoveStone(bag, cost);
            h.GetComponent<CompanionStamina>()?.UseStamina(stamina);
            if (hammer.m_shared.m_useDurability) hammer.m_durability = Mathf.Max(0f, hammer.m_durability - hammer.m_shared.m_useDurabilityDrain * Game.m_durabilityRate);
            p.PackFull = false;
            if (CompanionsPlugin.LogNormal) Info("Built a " + (piece != null ? MHR_List.Local(piece.m_name) : prefab.name) + " at " + MHR_Mover.Fmt(p.BuildSpot) + " from " + cost + " stone");
        }

        static float StoneWeight(Inventory inv)
        {
            float w = 0f;
            foreach (ItemDrop.ItemData it in inv.GetAllItems())
                if (it != null && it.m_dropPrefab != null && it.m_dropPrefab.name == "Stone") w += it.GetWeight();
            return w;
        }

        static void RemoveStone(Inventory bag, int n)
        {
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(bag.GetAllItems());
            for (int i = 0; i < items.Count && n > 0; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (it == null || it.m_dropPrefab == null || it.m_dropPrefab.name != "Stone") continue;
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

        // too heavy in the Ore job: the ore into a chest within 20 m, else all of it on the ore spot. False = no ore to unload.
        static bool OreLoad(HarvestController h, Per p, CompanionAI ai, Humanoid hum)
        {
            bool any = false;
            foreach (ItemDrop.ItemData it in hum.GetInventory().GetAllItems()) if (IsOreItem(it)) { any = true; break; }
            if (!any) return false;
            Container c = DropOff(h, h.transform.position, 20f);
            if (c != null)
            {
                if (CompanionsPlugin.LogNormal) Info("Too heavy - putting the ore in \"" + c.m_name + "\" " + Vector3.Distance(h.transform.position, c.transform.position).ToString("F0") + "m away");
                h.ResetToIdle();
                h.RestoreLoadout();
                ai.SetPendingDeposit(c, hum, IsOreItem);
                p.TooHeavy = false; p.PackFull = false;
                return true;
            }
            EnsureYard(h, p);
            p.Trip = YardTrip.Ore;
            p.PilePoint = p.OreSpot; p.PileActive = true; p.PileUntil = Time.time + 25f;
            if (CompanionsPlugin.LogNormal) Info("Too heavy and no chest within 20 m - taking the ore to the ore spot by the vein");
            return true;
        }

        static void DropOre(HarvestController h, Per p)
        {
            Humanoid hum = h.GetComponent<Humanoid>();
            if (hum == null || hum.GetInventory() == null) return;
            Inventory inv = hum.GetInventory();
            List<ItemDrop.ItemData> items = new List<ItemDrop.ItemData>(inv.GetAllItems());
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (!IsOreItem(it)) continue;
                Vector3 at = p.PilePoint + Vector3.up * 0.4f + new Vector3(UnityEngine.Random.Range(-0.3f, 0.3f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));
                int count = it.m_stack;
                ItemDrop d = ItemDrop.DropItem(it, count, at, Quaternion.identity);
                if (d == null) continue;
                d.m_autoPickup = false;      // stays there for you; the gather jobs leave it alone
                inv.RemoveItem(it);
                n += count;
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
