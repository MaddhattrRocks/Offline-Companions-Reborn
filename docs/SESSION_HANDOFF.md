# Session handoff (2026-10-08)

Paste this into a new session to pick up where we left off. Details per version are in `docs/DEV_NOTES.md` (NEXT BUILD section, newest first) and `package/CHANGELOG.md`.

## Repo and workflow
- Repo: `MaddhattrRocks/Offline-Companions-Reborn` (private, going public soon). Fork of ProfMags' Offline Companions (MIT).
- Work branch: `claude/relaxed-archimedes-ecoh6h`. PRs #1-#4 are merged into `main` (C# conversion, 1.4.75, 1.4.76, 1.4.77-1.4.85). After a merge the branch is restarted from `main` under the same name.
- The user builds the DLL (`dotnet build -c Release` on their PC, paths in the git-ignored `Environment.props`). Sessions only compile-check, never hand over DLLs unless asked. No game files in git.
- Version lives in `src/Companions/Core/CompanionsPlugin.cs` (PluginVersion), `package/manifest.json`, `package/CHANGELOG.md` (one line per item, issue and fix together).
- Each version: user builds, tests, sends LogOutput.log; next version fixes what the log shows. Show the README "Recent Highlights" before any packaging; no packaged release until the movement update (phases 2-4) is done.

## Current state: 1.4.125 pushed, not yet tested (1.4.86-1.4.125 on the branch, no PR yet)
- 1.4.124 test: repairs work (18 done); stuck when gear repair started at the same time - 1.4.125 lets gear repair go first. Next: wood/stone piles while sorting (design pending).
- 1.4.123 test: Repair and Restock stood still (hammer check used GetCurrentWeapon; reach blocked by touching floor). 1.4.124 fixes both.
- 1.4.123: loose pickups only early game (no bronze+ axe anywhere, no forge at home).
- 1.4.122: Wood/Stone pick up branches, stones and flint with a stone/flint axe or no axe ([Harvest] PickUpLoose).
- 1.4.121: gather jobs with Follow off work an area (home or start point, [Harvest] Range 40 m) like Hunt.
- 1.4.120: Wood/Stone/Ore/Forage keep Follow and work targets of opportunity near you (FollowRange 20 m).
- 1.4.119: per-appliance Cook switches; Repair and Restock sorts wherever it was started.
- 1.4.118: RepairBelow default 0.75; per-kind refuel switches (fires, hearths, braziers, standing torches, wall torches, hot tubs, other).
- 1.4.117: Repair and Restock job (real repair, refuel, like-with-like chest sorting near home); Restock and Homestead wheel slots are "Pending"; Homestead retired.
- 1.4.115 test: Fish works (user). 1.4.116: recast at the closest fish, better first cast.
- 1.4.115: rock tops allowed again, falls handled (back on / lost fish / avoid the spot after two falls).
- 1.4.113 test: fishing works really well (user). 1.4.114: wading spots, firm footing, shorter casts preferred.
- 1.4.112 test: only 1 fishing place mapped (too strict). 1.4.113 relaxes MapShore.
- 1.4.111 test: stuck floating (swim corner bug), fished from the water. 1.4.112 fixes both, spots off the edge.
- 1.4.108 test (user hadn't built 1.4.109/110): fishing works - fast spot, Perch + Pike caught. 1.4.111: rest before casting, stamina food (eat + fetch), stricter dive.
- 1.4.110: prefer dry routes unless the detour is long (DryDetour), jobs accept short swims (JobSwim).
- 1.4.108 test: first real catch (a Pike). 1.4.109: strict HaveStamina, 20 m casts, no job dives.
- 1.4.107 test: first real cast, float Setup crashed for the companion. 1.4.108: own float setup for companions, shore map for spots, aim clamp.
- 1.4.106 test: never found a bank (moat walls too high for the 4 m rule). 1.4.107 fixes the bank search.
- 1.4.106: Fish redone for real (vanilla float/fish, MHR_FishingPatches), one list format for every list (Core/MHR_List.cs). Bait crafting parked in the backlog.
- 1.4.104 tested: user says everything else works. 1.4.105: get up in the bed, then step off.
- 1.4.104: commands, wheel actions, tombstone, heal, combat chases, harvest approach/dodge, door handler, formation follow on MHR_Mover. Still old: Homestead, Stay Home, Fish.
- 1.4.103: hunting chases on MHR_Mover (shoot from the edge, leave prey needing a swim), no follow warp while still making progress.
- 1.4.102: no auto-sleep for 20 s after load, get up beside the bed, birds shot from 30 m / no swimming. Combat chases onto MHR_Mover is in the combat review.
- 1.4.101: get up from chairs onto open floor (not into the hearth); fire rescue after 3 s.
- 1.4.100: bird hunting (feathers), hearth/fire escape fix, command logs why nobody answered. User: hunting is fine now - next job is Fish (bait crafting).
- 1.4.99: close-enough jumps into shallow water, chained jumps, looser gap/water rules for shortcuts.
- 1.4.98: jumps use the companion's run speed, Jump skill and stamina (MHR_Jump); jumps cost stamina. Skills/stats review added to the backlog.
- 1.4.97: no swimming for loot, no crouch in water, shortcuts with 3 m drops / 1.5 m gap leaps, cook keeps collected honey. Bed kinematic fix confirmed.
- 1.4.96: hunters sneak up on skittish game (real stealth for crouching companions), jump narrow shallow water, loot survives a fight in between kills.
- 1.4.95: loot timing fix (+ shallow water, no swimming), cook walks to each honey, status effects (Wet etc.) slow companion regen.
- 1.4.88-1.4.93: Cook test fixes (fermenter RPC, fire spots, chest reach, mead plan by boss, beehives). Cook is done apart from testing the honey collecting.
- 1.4.94: Hunt redone (Follow on = hunt near the player; off = around home/start spot; anything that drops meat via [Hunting] Prey; MeatTarget 100; bow first; defends itself) and every kill gets looted (MHR_Loot, [Looting]). Next jobs: Fish (bait crafting), then Homestead (rename).

## Previous: 1.4.87
- 1.4.87: Cook rewritten to the agreed design (all cooking stations like a player, best known food, reserves/targets, boss-based mead focus, ledger so his own food isn't stored, [Cooking] settings, reference file OfflineCompanionsReborn.Cooking.txt). The user still has to review the MeadFocus defaults. Expect several refinement rounds.

## Previous: 1.4.86 pushed, not yet tested
- 1.4.86 (phase 3 part 2, Smelt only): Smelt on MHR_Mover with stand spots at chests and smelter slots, unreachable chests skipped; stairs walked normally (hop only when held up); no "hasn't moved" diag warning in combat. Cook and Homestead wait for a feature review.

## Previous: 1.4.85 (tested OK, merged in PR #4)
- 1.4.85: companions' own route map (`Patches/MHR_CompanionNavMesh.cs`): closed doors don't block routes (opened on arrival, any number), steps up to 0.6 m connect and are hopped as he reaches them. Config `General > CompanionPathing` (on; restart). Wider chest stand-spot search (1.2/1.7/2.2 m, 16 directions); farm opens a chest from its picked spot. Log should say "[Move] Companion route map ready" at world load.
- User's next test: build 1.4.85, check the farm stores crops in the house chests up the stairs, doors, stairs, narrow chests ("Skipping chest at ..." lines show any still ruled out).

## What 1.4.77-1.4.85 did (movement overhaul phase 3 part 1 + test fixes)
- Farm and gathering on MHR_Mover; farm harvests everything ripe before storing; plants in rows from the far edge; stores past unreachable chests; chest animations.
- Mover: soft-reset/give-up fixes, ledge guard, climb out of pits (sprint + jump at the lowest/least steep way up), trapped rescue (teleport back to the last good spot), go straight and hop steps/logs instead of long detours, hop bridges for stairs, door chains, then the companion route map.
- Follow: 1.5 s hold after the player sets off + hold while the player is within 2.5 m, trail mode for long detours/swims, no warp while in view (unless 1.5 x FollowTeleportDistance).
- Combat: only fights things after the player/companions (also when switching targets mid-fight); Engage log shows the creature's target. Shield carried with a one-hander.
- Bags: emptied when arriving home with no job (and after loading in at home), not only on a job change.

## Backlog (talk through with the user before building)
- **Jobs + Follow together:** field jobs (Wood/Stone/Ore...) should keep following the player and work what's nearby; home jobs turn Follow off (current rule for all jobs except Hunt since 1.4.94). User: with Ore and no ore around he just stood there.
- **Command wheel revisit** (Rename, guard/patrol, late-game features; maybe a sub-menu) and a **rename fallback** via the vanilla TextInput dialog (SeneaL_UI blocks the panel's name box).
- Movement phase 3 part 2 (Smelt, Cook, Homestead on MHR_Mover), part 3 (Hunt, Fish), phase 4 (remove old per-job stuck code and DoorHandler).
- Combat polish, ship-seat kinematic warnings, config cleanup, release packaging; older items in DEV_NOTES BACKLOG.
