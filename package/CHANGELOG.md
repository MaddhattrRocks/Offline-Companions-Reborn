# Changelog

All notable changes to this fork of ProfMags' Offline Companions. Versions before 1.4.0 are the original mod (see [ProfMags' Thunderstore page](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) for that history) plus the initial batch of Valheim 1.0 API-compatibility fixes that let the mod load and run at all on 1.0.

## 1.4.93
- Cooks collect honey from beehives in range: from a full hive, right away when a mead base is waiting on honey, and from any half-full hive when there's nothing else to cook. The honey gets put away with the rest.
- Fixed the "Setting linear velocity of a kinematic body is not supported" warning while a companion sleeps in a bed.

## 1.4.92
- Reviewed mead plan: after each boss, cooks brew the mead bases you'll want for the next one first (Eikthyr through Fader, plus Kall Fimbulbringer). The list sets the order, it doesn't limit it: other mead bases you know are still brewed after food, within the same targets.
- Bosses are read from the game itself, so the MeadFocus setting works with new and modded bosses too; OfflineCompanionsReborn.Cooking.txt lists them.
- The log now says why a cook isn't brewing a priority mead base (enough already, recipe not known, no usable station, or an ingredient at its reserve).

## 1.4.91
- Fixed cooks never actually putting mead bases into fermenters (the base disappeared from their bag and the fermenter stayed empty), and thinking a full fermenter was empty.
- Fixed companions trying to stand on top of a fermenter (or chest, station, bed) next to the one they're using. They now stand on the floor beside it.

## 1.4.90
- Fixed cooking (and other chest trips) not reaching any chest in a room with a fire. The "keep out of fires" check now only avoids the flames themselves, not the whole area around a fire.
- Companions carry their best parrying shield again instead of the tower shield. They still switch to the tower shield in a fight when they're hurt or outnumbered.
- Companions no longer put their gear back on piece by piece when you load in. They're already dressed, like you.

## 1.4.89
- Companions jump over a small fire in their way, like a player, and only walk round a big one. Fixed a cook getting stuck stepping in and out of a fire pit on the way to the cauldron.
- Fixed the cook trying the same recipe over and over when an ingredient is only in a chest it can't reach. Such chests no longer count, and a recipe that can't be made is left alone for a minute.
- Companions can use chests on ledges and shelves from the floor below: they look for a place to stand under an overhang too, and check whether they can see the chest from a little higher (like a player's camera).

## 1.4.88
- Fixed companions running back and forth under a chest up on a ledge (seen when emptying their bags after turning Cook off).
- Companions keep out of fires: they don't pick a spot in one, walk round one in their way, and step straight out if they end up in one (seen in a tight kitchen, standing in the fire pit).
- Companions reach chests like a player: anything within about 4 m of their eyes that they can see, so chests up on a ledge or a high shelf can be used from the floor below.

## 1.4.87
- Cooking rewritten. Companions now use every cooking station like a player: cauldron and food preparation table (with a fire and roof, like you need), cooking station, iron cooking station and oven (they put food on, take it off when it's done, and keep the oven fed with wood). Bread and pies go from the prep table to the oven.
- They make the best food they can from recipes you know, until there are 20 of it in the chests nearby, and never use a raw ingredient once 20 or fewer are left, so you always have some to cook with yourself. All amounts are in the new [Cooking] settings.
- Mead: they brew and ferment the meads you'll want for the next boss (the MeadFocus setting, by the last boss beaten), keeping 10 of each mead base and 20 of each finished mead, and tap fermenters when they're done.
- They take finished food off any cooking station nearby before it burns.
- Cooked food goes in a chest that already has it, else one with other cooked food, else the one nearest the cooking area. Food you gave them to eat stays with them.
- They cook in batches like a player (Cooking > BatchSize, 5 by default): several at the cauldron or prep table in one trip, several slots on a cooking station or oven at once.
- The [Cooking] Foods setting limits what they cook (* = everything, -Name to leave one out). OfflineCompanionsReborn.Cooking.txt in the config folder lists the names, mods included.

## 1.4.86
- Smelting now uses the same movement as farming and gathering: routes through doors and up stairs, the shared stuck handling, and a spot it can stand on by each chest and by the smelter's ore and fuel slots. Chests it can't get to are skipped.
- Companions walk up stairs normally again and only jump a step that stops them.
- Removed a false "hasn't moved" warning in the log while a companion stands and fights.

## 1.4.85
- Companions now find their way with their own map of the world. Closed doors don't block their way (they open each one as they reach it, however many there are), and they hop up stairs and steps the normal map doesn't connect. New setting General > CompanionPathing (on) turns it off if it causes trouble.
- Companions find a place to stand by chests in narrow spots more often (anywhere within reach, from more directions).

## 1.4.84
- Fixed the farmer never storing crops in chests up the stairs indoors. Companions now head for the bottom of stairs the game's pathing doesn't use and hop up them.
- Companions can find their way through several closed doors in a row (up to 3), not just one.
- Farmers plant in rows from the far edge of the zone back toward where they came in, instead of in a ring around themselves until they're boxed in by their own crops.
- Companions coming home with no job now put away what they picked up, like after turning a job off. This also happens after loading in at home.
- Companions hop over low things in the way (logs, low walls) instead of running around them.

## 1.4.83
- Fixed companions running a long way round (and warping) instead of hopping up steps or a low wall. When the way round is much longer, they now go straight and jump over it like a player.
- Following companions no longer warp to you while you can still see them, unless they're half as far again as FollowTeleportDistance.
- Fixed companions switching mid-fight to creatures that weren't after anyone (boars while exploring).
- Companions now carry their shield with a one-handed weapon all the time, not only once a fight starts.

## 1.4.82
- Fixed the farmer carrying crops to a chest after every few. It now picks everything ripe first and stores when nothing ripe is left or its pack is nearly full.
- Fixed the farmer planting a few seeds and then stopping.
- Fixed following companions taking a long way round (down past the beach) when you went a shorter way. They now follow your path.
- A standing companion stays put while you're within 2.5 m of it, so it's easy to walk up to.
- Fixed gatherers walking off ledges (into a moat) while stepping in to reach a tree or rock.
- A companion stuck in a pit (a dry moat) now climbs out like a player: it picks the lowest, least steep way up nearby, sprints at it and keeps jumping, resting when out of stamina. If that fails, it's moved back up to where it last had a way to its target.
- Companions now play the interact animation when opening a chest for farming, smelting and putting tools away.

## 1.4.81
- Fixed companions walking all the way home to empty their bags or put a tool back when you turn a job off out in the field. They now do it once they're back home.
- Fixed the Wood job equipping a crossbow and standing there. Bows and crossbows are never used as gathering tools.
- Fixed following companions swimming across a moat when you walked in through the gate. They now follow your path when their own way would mean a swim.
- When you set off, a companion that was standing still now waits 1.5 seconds before following, so it's easy to walk up to, with Wander on or off.
- Fixed setting a farm zone (modifier + Z) also recalling your companions and turning their job off.

## 1.4.80
- Fixed Stone and Ore companions hitting each rock once and moving on to the next. Many rocks turn into a cracked version on the first hit, and companions took that as the rock being gone. They now keep mining the cracked rock until it breaks.

## 1.4.79
- Fixed following companions running off to a gate on the other side of the base. When they can't get to their spot, they now follow your path instead.
- Fixed the farmer picking chests it can't reach (up on a ledge, behind a wall) and running around the building trying. It now only uses chests it can get to.
- Companions no longer go through a door unless it actually leads to where they're going.
- Fixed gatherers swinging at big rocks without reaching them. They now step in until the rock is in reach.
- Gatherers wait a moment for drops to land before moving on to the next rock.
- Companions only start fights with creatures that are going after you or them. Anything that attacks them is still fought.

## 1.4.78
- Fixed the farmer running back and forth through a gate on the way to a chest. Companions now walk to a spot by chests and crafting stations that they can actually reach.
- Fixed companions sidestepping and hopping around between crops while farming.
- Fixed gatherers swinging at a rock over and over without hitting it.
- Fixed companions picking fights with harmless animals (modded crabs and dolphins) and staying "in combat" with something far away, which stopped their job. They now let go of anything more than 40 m away.
- Picking a job now turns Follow off, so the companion stays at the work. Turning the job off turns Follow back on, and picking Follow turns the job off.
- Companions only repair barely-worn gear when they're actually at home (within 25 m), not anywhere within HomeTripRange.
- Smoother following when you walk close to a companion.

## 1.4.77
The third part of the new movement system starts: jobs. Farming and the Wood, Stone, Ore and Forage jobs now move the same way as following and trips.
- Farm and gathering companions find their way around walls, open doors and gates, handle water and get unstuck like they do on trips.
- Gatherers working their way around something to reach a tree or rock no longer give up on it too early. If they really can't get there, they pick another target.
- Companions wait a moment before following when you set off, and stay put while you're right next to them, so they're easy to grab.
- The Wander toggle now also controls following. On (now the default for every companion): moves around you and idles when you stop. Off: keeps one spot near you and stands still.
- Fixed thousands of log warnings while a companion sat on a chair.

## 1.4.76
The second part of the new movement system: following you.
- Companions follow like a player. Each picks its own spot beside, behind or sometimes ahead of you, and moves it now and then.
- Several companions spread out around you instead of lining up behind you.
- Following uses the new movement: doors, water, stream jumps and getting unstuck all work while following.
- In doorways, on stairs and in narrow paths, companions follow your path and spread out again when there's room.
- When you stop, companions wait where they are and idle nearby.
- After you've stood still for a while, companions come back and wait near you. New settings `Regroup` and `RegroupTime` (Follow, on, 45 s).
- Indoors, companions stay closer (`IndoorDistance`, Follow, default 4 m) and sit down on a free chair or the floor when you stop. Near home they empty their bags first. New setting `SitIndoors` (Follow, on).
- New setting `MaxDistance` (Follow, default 10 m). `FormationOffset` and `FormationCatchupDist` no longer do anything.

## 1.4.75
- Fixed companions walking back and forth while farming. With a job on, they now stand still between tasks instead of wandering back to their idle spot.
- The farmer now goes straight to the next ripe crop close by, like working down a row, instead of pausing and rescanning the field. New setting `NextCropRange` (Farm, default 3 m, 0 = off).
- Fixed companions getting stuck in doorways when no route could start from where they stood. They now step a meter or two to find one.
- Fixed doors being closed behind a companion that was still stuck next to them.
- Fixed a weapon check that never ran. Companions in Passive stance now hold their strongest weapon.
- Farm scans now write one line to the log instead of one per crop.
- Reborn no longer shows as incompatible with ProfMags' original mod. It still won't start if the original is installed, and the log says to remove it.
- The mod is now built from regular C# source code. It should play the same; if something seems different, please send your log.

## 1.4.74
The first part of a new movement system. Tool trips, emptying bags, repair trips and walking to bed now all move the same way. Following and jobs move over in later updates.
- Fixed companions on trips going the wrong way around walls, falling into moats and giving up after 20 seconds. They now follow the route corner by corner.
- Companions open closed doors and gates on their way, including from the bottom of short stairs, and close them again behind them.
- When stuck, companions step back to one side, jump toward the next turn of their route, try another way around, and only then give up.
- Companions in deep water swim to the bank nearest where they're going, and only one they can climb out on.
- Companions walk the last few steps, jog in between, and run when the destination is far away (`WorkRunDistance`).
- Fixed companions avoiding all water when low on stamina. Wading is always fine; they only avoid swimming if their stamina won't last, and otherwise wait on the bank to rest.
- Companions jump narrow streams to stay dry and take a running jump into water they're going to swim.
- Fixed companions ignoring targets in shallow water and chasing modded sea animals. They now follow targets into wading depth, and only into swimming depth if attacked and armed with a bow or crossbow.
- Fixed companions chasing animals that never attack (deer, and modded fish and dolphins). These are now left alone.
- Fixed crossbows never firing. Companions now reload them like a player.
- Fixed bows and crossbows counting as ready without the matching ammo.
- Fixed automatic repairs only finding a station within 20 m. Near home, any station near home counts.
- Fixed companions retrying a repair trip to a station they can't reach. They now look for another tool, or say it's broken and follow you.
- New setting `FollowStuckTeleport` (General, off by default): a following companion stuck out of sight for 15 seconds appears next to you.

## 1.4.73
A review of the Wood, Stone and Ore jobs turned up several places where companions didn't play by a player's rules.
- Fixed companions damaging a tree or rock they couldn't reach. They now pick a new target instead.
- Fixed every swing at a tree landing an extra hit. It's one hit per swing, and only when the swing connects.
- Fixed pickaxe swings that hit the ground counting as hits on the rock.
- Fixed companions being left with no weapon or shield after tool, fight and job switches. Their normal gear now comes back.
- One-handed tools no longer take the shield or torch off.
- A broken tool now comes off and the companion switches to its best weapon, then repairs or replaces the tool near home.
- Fixed swings wearing down a shield or torch.
- Fixed companions standing on stumps and logs and shuffling forward while swinging.
- Fixed overweight companions flickering in and out of their job. They now say they're too heavy, pause the job and check for somewhere to unload.
- Drop-off rules: chests only near home; away from home, also a nearby cart, ship or outpost chest. Never tombstones or other players' private chests.
- Stone from the Ore job goes in a pile a few meters from the deposit instead of being hauled home.
- Fixed large copper and tin deposits being abandoned partway. They're now mined until finished.
- After a tree falls, companions go to the nearest log, if your gather list and limits allow it.
- Companions collect drops from around a rock and pick items up from a player's reach (about 2 m).
- Fixed the Forage job skipping wild barley and flax.
- Companions turn toward their work instead of snapping to face it.

## 1.4.72
- Fixed companions taking off all their armor after their inventory reloaded. They now keep wearing their gear.

## 1.4.71
- Fixed companions being unable to open a door from the bottom of the stairs below it. They now open doors from anywhere in reach.
- The same in-reach rule applies to smelters, kilns, cauldrons, fermenters, fires, chairs, crafting stations, repairs and chests. Planting still needs them on the field.
- Fixed companions walking home from far away when a tool broke. Chest and repair trips only happen within `HomeTripRange` (General, default 60 m); further away they say so, pause the job and follow you.
- Near home, a broken job tool is repaired at a workbench or forge, or replaced from a chest.
- Near home, companions repair any wear on their gear, not just items below `DurabilityThreshold`.
- Gatherers stand a little further back so they no longer end up on top of stumps.
- Emptying bags now uses `HomeTripRange` too (was a fixed 40 m).

## 1.4.70
- Fixed the farmer taking vegetables out of a chest for seed plants and storing them straight back. They stay in its pack until that seed reaches `SeedTarget`.
- Fixed companions spinning in a corner trying to reach stacked chests. They now use chests and pick crops from anywhere in reach, up to about 2 m above or below.
- Companions sprint in bursts: run down to half stamina (30% in a fight), walk back up to 90%, then run again.
- When a job is changed or turned off, or Homestead turned on, a companion near home puts away what it gathered first. It keeps equipped items and the gear, ammo, food and mead you gave it. If it's away from home, it does this when it's back.

## 1.4.69
- Fixed companions waking up beside the bed instead of where they slept.
- Companions walk while working and only run to far targets. New setting `WorkRunDistance` (General, default 25 m, 0 = never run for work).
- Fixed the farmer making a separate chest trip for each seed type. It now takes up to `SeedCarry` (Farm, default 40) of each type in one trip.

## 1.4.68
- Fixed the farmer giving up on a field or chest just above or below it. It now keeps walking until it's actually there.
- Fixed the farmer planting one crop until it ran out of seeds. It now switches type every `PlantGroupSize` plants.

## 1.4.67
- Fixed farm zones never finding seeds in chests.
- Fixed companions on tool trips being blocked from a chest by nearby chests and furniture. Only walls, floors and large solid pieces block reach now.
- Companions only head for a chest they can walk all the way to, and try another chest with the tool if one can't be reached.
- The stuck jump only kicks in when really stuck, not while walking around a building.
- The default `PlantAny` list uses the real names `MushroomJotunPuffs` and `MushroomMagecap`. Short names still work, and existing configs keep their old value.

## 1.4.66
- Fixed companions missing nearby chests in large bases. Every chest in range is now checked.
- Fixed a farmer with only a few seeds and no vegetables never planting. It now plants the seeds to get started.
- Fixed companions taking tools out of chests through walls. They now open chests only from a spot they can walk to with nothing solid in between.
- Companions stuck on stairs or ledges on tool trips and farm work jump toward where they're going.
- `PlantAny` names ignore capital letters, and "Nothing to plant" log lines show how many seeds and vegetables were found.
- Debug logging for tool trips and farm work: position, destination and whether there's a path, every 2 seconds.

## 1.4.65
- Fixed a job and Stay Home both steering the companion back and forth. Picking one now turns the other off.
- "Stay Home" on the command wheel is now called "Homestead".
- Fixed companions crawling slowly away from a chest when Follow was picked mid tool trip. Trips now finish first, and a companion that's attacked fights and then carries on.
- Fixed Any farm zones planting tree seeds. They now only plant what's on the new `PlantAny` list (Farm).
- New setting `PlantGroupSize` (default 4): plant this many of one type before switching, so mixed fields end up in small blocks.
- New settings `SeedMinimum` (default 20, replaces `SeedReserve`) and `SeedTarget` (default 100) control when the farmer grows seed plants or crops.
- Barley, flax and other self-seeding crops are always planted, keeping `SeedMinimum` in the pack for replanting.

## 1.4.64
- New gather lists `GatherWood`, `GatherStone` and `GatherOre` (Harvest) set what each job collects and how much, with `WoodLimit`, `StoneLimit` and `OreLimit` (500 each) as category limits.
- Limits count the companion's pack and chests near home. Once everything is at the limit, it says so and waits.
- New setting `WoodSkipTrees` (default `Oak`): trees the Wood job never fells.
- New setting `BaseProtectRadius` (default 10 m): the Wood job won't fell a tree that close to anything built.
- Companions step clear of falling trees and splitting logs, and approach from the uphill side on slopes.
- A gatherer with a full pack takes a load to the home chest. With no home chest, `JunkCleanup` (on by default) drops junk to make room; gear, tools, ammo, food, seeds and `NeverDiscard` items are kept.
- Fixed the Ore job mining plain boulders that only drop stone.

## 1.4.63
- Fixed tool trips giving up before reaching the chest. Companions now walk to the front of the chest and only give up after 20 seconds without progress or 2 minutes.
- Repair (buildings), Restock and Hunt now pause during a tool trip like the other jobs.
- Routine farm scanning lines only show at Debug log level.

## 1.4.62
- When a job needs a tool the companion doesn't have, it takes the best one from a chest near home.
- Borrowed tools, ammo and bait go back to the chest when the job is turned off or changed. Tools it already had stay with it.
- If there's no tool in the chests, the companion says what it needs.
- The farmer keeps a reserve of carrot, turnip and onion seeds, planting vegetables as seed plants when short. New setting `SeedReserve` (Farm, default 20, 0 = off).

## 1.4.61
- Fixed item icons from a chest showing over the companion's inventory.
- Fixed companions being pushed to the side when getting out of bed.
- Fixed Farm and gathering jobs restarting every time the companion's inventory changed.
- Fixed the farmer leaving harvested crops on the ground and storing unrelated items in the crop chest.
- Forage leaves crops in your farm fields for the Farm job.
- Fixed companions standing in an open doorway for 8 seconds and giving up on their target.
- Fixed the "[Diag] ... hasn't moved" warning showing while a companion was working.

## 1.4.60
- Fixed companions vanishing when another mod deleted them. They're now protected and can still die and respawn normally. New setting `ProtectCompanions` (General, on by default).

## 1.4.59
- Fixed companions being deleted inside active wards by the "Guardstone wards remove monsters" mod.

## 1.4.58
- Fixed Farm filling the log with "Invalid IL code" errors and never farming when another mod changes the terrain lookup.
- Farm, Fish and the gathering jobs now write what they're doing to the log.
- Fixed one food message being logged every frame.

## 1.4.57
- Recall now only reaches companions within `RecallRange` (Controls, default 40 m) that have Command on. Point-and-command orders use the same range.
- Hold the command key for `SuperRecallHoldTime` (Controls, default 2 seconds) to recall every companion at any distance.

## 1.4.56
- When you get into bed, companions near their own beds go to bed too.
- At night, companions near their bed go to sleep even while working, and pick the job back up in the morning.
- Fixed the night skip waiting forever on a companion that couldn't reach a bed. It now gives up after 30 seconds.
- Recalling your companions wakes any that are asleep.
- New settings (Sleep): `SleepAtNight`, `SleepWhenPlayerSleeps` and `BedRange` (default 50 m).

## 1.4.55
- Fixed controller stick errors filling the log and the stick not working on the command wheel.

## 1.4.54
- The mod's ID is now `Maddhattr.OfflineCompanionsReborn` and it shows as "Offline Companions Reborn" in the config manager. Settings carry over automatically.
- Reborn won't load if ProfMags' original Offline Companions is also installed. Remove the original to use Reborn.

## 1.4.53
- Every companion now has a permanent ID that stays with it through death and respawn.
- Fixed companions collecting another companion's tombstone when they shared a name and owner.
- Beds remember companions by ID, so renaming a companion keeps its bed.
- The config file is now `Maddhattr.OfflineCompanionsReborn.cfg`. Settings are copied over automatically; the old `com.profmags.companions.cfg` can be deleted.
- Fixed the starter companion window never opening with Valheim Plus's "disable arrived message" setting.

## 1.4.52
- **Change:** Despawn on the command wheel now asks you to confirm with a Yes/No box that names the companion and says its items will be left in a tombstone. This replaces the old "press the wedge again within 3 seconds" confirm, and works the same with mouse or controller.

## 1.4.51
- **Issue:** Dragging an item a companion was wearing into your own inventory left it looking equipped there.
- **Fix:** The companion takes the item off first, so it arrives in your inventory as a normal item.
- **Issue:** Despawn on the command wheel gave an error and did nothing, left over from the 1.4.32 bed update.
- **Fix:** Despawn works again. The companion leaves a tombstone with everything it was carrying, and its bed is freed up.

## 1.4.50
- **Issue:** After you slept through the night, a companion got out of bed, climbed right back in, then got up again a few seconds later.
- **Fix:** When your sleep ends the night, companions get up and stay up.
- **Issue:** A companion that came back with its gear only put on one piece, and didn't put on the rest until later.
- **Fix:** If it's still putting on one piece, it now waits and then puts on the rest.

## 1.4.49
- **Issue:** When a companion kept all its items through death, its map marker vanished right away because the tombstone was empty, even though that tombstone is where you'd go to bring it back.
- **Fix:** The marker now stays while the companion is waiting to be revived. After it comes back, the marker only stays if items were left in the tombstone.
- Cleaned up a leftover debug message that showed up as a warning in the log.

## 1.4.48
- **Issue:** A companion's bed said "unclaimed" while the companion was dead, and could show "HC_Companion(Clone)" again after it came back.
- **Fix:** The bed claim is now saved on the bed itself. It keeps the companion's real name through death, travel, and reloading the game, and other players see it too.
- **Issue:** After respawning, a companion with items left in a tombstone far away would stand around, refuse to fight, and ignore Follow for a couple of minutes, then give up on its items for good.
- **Fix:** A companion only goes after its tombstone once it's actually nearby. Until then it follows, fights, and works as normal, and it never forgets the items. It fights off attackers before going back to looting, and it remembers every one of its tombstones, not just the latest.
- **Issue:** A companion's tombstone disappeared from the map as soon as it respawned, even though its items were still in the tombstone.
- **Fix:** The map marker now stays until the tombstone is emptied, whether you empty it or the companion does.

## 1.4.47
- **Issue:** Auto-respawn always brought a companion back at its tombstone, never at its bed or next to you. When the tombstone was far away, the respawn failed completely.
- **Fix:** Auto-respawn now uses the claimed bed first (a free spot beside it), then next to you, or the spawn stones if you've turned that on.
- **Issue:** After dying and coming back, a companion forgot which bed was its own.
- **Fix:** Companions keep their claimed bed through death.
- **Issue:** Companions never said they needed a bed, even though that line was added in 1.4.30.
- **Fix:** They now speak up when they don't have a bed.

## 1.4.46
- **Issue:** A dead companion's auto-respawn timer stopped, and started over from zero, whenever you went far from its tombstone or reloaded the game.
- **Fix:** The respawn time is now saved with the world. The companion comes back on time no matter where you are, even after quitting and reloading.
- A companion that respawns while you're far from its tombstone leaves any dropped items in the tombstone and goes back for them. An empty tombstone clears itself the next time its area loads.
- A companion with no claimed bed now respawns next to you by default. New setting `AutoRespawnAtWorldSpawnWhenNoBed` sends it to the spawn stones instead. This replaces `AutoRespawnNearOwner`.
- In multiplayer, only the companion's owner's game brings it back, so two players can't respawn the same companion.
- Controller: the stance area of the command wheel is bigger, so stances are easier to pick.

## 1.4.45
- **Issue:** In 1.4.44, the command wheel wouldn't highlight anything on a controller, and a companion that had slept wouldn't follow you after waking.
- **Fix:** Corrected two new pieces of code that crashed when they ran.
- **Issue:** Companion crafting (cooking and homestead work) and taking seeds for farming could crash, left over from the 1.4.29 game-patch update.
- **Fix:** Corrected the same kind of problem in both.

## 1.4.44
- **Issue:** A companion's bed could show "HC_Companion(Clone)" instead of its name.
- **Fix:** The bed's name label is now only set when the companion claims the bed, using its real name.
- **Issue:** Companions going to bed ended up lying on the floor next to it.
- **Fix:** Companions now lie in the bed the same way a player does.
- **Issue:** Every time a companion slept, it quietly moved its Home point to a spot beside the bed, even if you never used Set Home.
- **Fix:** Home now only changes when you use Set Home.
- Added logging for controller stance selection in the command wheel, to help tune how it feels.

## 1.4.43
- **New:** What a companion loses when it dies is now one setting, `DeathItemRule` (under Respawn). 0 = follow the world's death penalty setting, the same as a player would (default). 1 = keep everything. 2 = keep equipped gear, drop the rest. 3 = drop everything. This replaces the old `DropInventoryOnDeath` and `DropEquippedGearOnDeath` toggles.
- In multiplayer, a companion always uses its owner's setting, even if another player's game happens to be handling it when it dies.
- **Issue:** When a companion auto-respawned at its bed, it instantly pulled its dropped items out of the tombstone, even from across the map.
- **Fix:** After an auto-respawn, dropped items stay in the tombstone and the companion goes back for them. Resurrecting a companion by hand at its tombstone still gives the items back right away.
- **Issue:** When keeping items through death, a companion could lose some of them. Anything past 16 stacks was deleted, and about half of the items that should have been kept went into the tombstone instead.
- **Fix:** Kept items now all come back with the companion.

## 1.4.42
- **Issue:** On a controller, picking a combat stance in the command wheel was really hard. The stance only highlighted while the stick was held at a very specific partial push, so it kept flickering off.
- **Fix:** Stance selection is now "sticky." Push the stick partway toward a stance (up for Balanced, right for Passive, down for Ranged, left for Melee) and it stays highlighted after you let go. Press the use button to apply it. The stance area is also a bit larger. Pushing the stick all the way out still selects the outer actions like before. Mouse and keyboard are unchanged.

## 1.4.41
- **Issue:** For some players (reported with the game set to Russian), the game could hang on the loading screen for a couple of minutes and then close.
- **Fix:** The mod was reloading its translation files every time the game refreshed its language settings, which some setups do many times at startup. It now only reloads when the language actually changes.

## 1.4.40
- **Issue:** A companion running the smelter would get the kiln lit but then keep interrupting itself every time it tried to insert ore or fuel, never finishing the job.
- **Fix:** Removed a bug that made the companion think its job had changed every time it used an item from its own inventory. It now stays on task.

## 1.4.39
- **Issue:** Companions could silently fail (and spam the log) while inserting ore into a smelter.
- **Fix:** Corrected a missing piece of data the game expects when adding ore. Fuel insertion was never affected.

## 1.4.38
- **Issue:** Follow and Stay Home could both end up "on" at once, with Stay Home quietly winning forever.
- **Fix:** Follow and Stay Home are now mutually exclusive — turning one on turns the other off. "Set Home" also no longer turns Stay Home on by itself; it just sets the home point.

## 1.4.37
- **Issue:** A companion could crash if it picked up ore/fuel from a chest at the exact moment its gear happened to re-equip.
- **Fix:** Closed that timing gap so it's handled cleanly instead of crashing.

## 1.4.36
- **Issue:** Companions could get "stuck" wandering near a chest instead of using it, without ever giving up and trying somewhere else.
- **Fix:** The stuck-detection now checks whether the companion is actually making progress toward its target, not just whether it's moving at all — so it correctly notices, gives up on that chest, and tries another.

## 1.4.35
- **Issue:** A companion's claimed bed showed a confusing "Unclaimed (claimed by ...)" label with a garbled name.
- **Fix:** It now reads cleanly as "\<Companion's Name\>'s Bed."

## 1.4.34
- **Issue:** Companions weren't sleeping, "Set Home" seemed to silently fail, and companions moved erratically near beds.
- **Fix:** All three turned out to be the same root cause — a bed-ownership check that was failing every time. Fixed, and all three symptoms went away together.

## 1.4.33
- **Issue:** A rare crash could hit bed logic, tombstones, or a few other spots depending on the exact build, seemingly at random.
- **Fix:** Found and corrected the underlying bug everywhere it appeared in the mod, not just the one reported spot.

## 1.4.32
- Companions now claim a specific bed as their own — permanently, not just while sleeping in it — the same way a player would settle into one. That bed shows "(claimed by \<name\>)" and is their auto-respawn point.
- A companion automatically claims a bed when you Set Home nearby, after a short delay if they don't have one, or if they notice you've claimed a bed and they haven't.
- Pointing at a bed and commanding a companion is now a manual override: claims it if free, sleeps in it if already theirs, or refuses if it belongs to someone else.
- Claiming a new bed releases the old one back to the pool.
- Companions no longer grab a random nearby bed at night — if they don't have a claimed one, they'll say so.

## 1.4.31
- Added an automatic respawn timer for dead companions, alongside the existing manual tombstone resurrect. Configurable delay, default 5 minutes.
- Auto-respawn happens at a companion's claimed bed if it has one, otherwise world spawn (or optionally near the owner).
- Companions now keep 100% of their inventory by default when they die, instead of always dropping it. Two new options let you change that.
- If items were dropped, the companion will walk back and loot its own tombstone once it's close enough. The tombstone disappears automatically once nothing's left in it.
- A companion's tombstone now shows up on the map, like a player's own death marker.

## 1.4.30
- Companions no longer take over a bed a player has personally claimed.
- A bed a companion is using now shows "claimed by \<companion name>" so it's clear it's taken.
- Companions will speak up if they don't have a bed to sleep in.

## 1.4.29
- Updated for a Valheim game patch that changed how a few things work under the hood (inventory, gear visuals, piece placement). No behavior change intended — this just keeps the mod working.

## 1.4.28
- **Issue:** Companions could get stuck standing at "bow range" even after correctly switching to melee when they ran out of arrows.
- **Fix:** Positioning now follows the weapon actually in hand, not the companion's general combat stance.

## 1.4.27
- **Issue:** A direct attack command could bypass a companion's normal combat AI (weapon choice, ammo checks, fleeing) entirely.
- **Fix:** Direct attacks now go through the same AI as everything else.
- Added a separate "flee" health threshold, distinct from the shield-retreat threshold.
- Companions can now equip a shield while fleeing.
- A companion that gets cornered while fleeing now defends itself instead of just standing there.

## 1.4.26
- Shield choice is now handled entirely by the health/threat-aware logic, instead of sometimes being overridden by "pick whatever blocks the most."
- "Return to me" now walks back normally at short range, or does a disguised teleport-recall if you're too far away, instead of risking getting stuck.
- Companions now fall in with a nearby real player and keep fighting if their owner dies mid-session, instead of standing idle.

## 1.4.17 – 1.4.25
- Fixed shields defaulting to the tower shield far more often than intended.
- Fixed companions not properly falling back to melee when they ran out of arrows.
- Rebuilt "come to me" / return-to-owner as reliable distance tracking instead of an older timer approach that could get stuck.
- Fixed a few "return to me" bugs (not firing on companion death; wrongly pulling in companions not set to Follow).
- Tracked down and fixed several different "companion just stands there" reports.

## 1.4.13 – 1.4.16
- Added companion health/stamina status bars above their heads, then simplified them based on feedback.

## 1.4.4 – 1.4.12
- Added a "torch opener" behavior so companions can light their way in the dark.
- Fixed a weapon-swap loop, a "stuck on Passive after death" bug, a version-string bug, and a water-safety stuck state.

## 1.4.1 – 1.4.3
- Fixed a long-standing bug where a companion's inventory could show garbage tooltips instead of real items.

## 1.4.0
- Initial Valheim 1.0 compatibility pass — updated several game functions that changed in the 1.0 update so the mod loads and runs at all.
