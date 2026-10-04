# Changelog

All notable changes to this fork of ProfMags' Offline Companions. Versions before 1.4.0 are the original mod (see [ProfMags' Thunderstore page](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) for that history) plus the initial batch of Valheim 1.0 API-compatibility fixes that let the mod load and run at all on 1.0.

## 1.4.74
The first part of a new movement system. Tool trips, emptying bags, repair trips and walking to bed now all move the same way. Following and jobs move over in later updates.
- **Issue:** Companions on a trip often went the wrong way around walls, fell into moats or water, and gave up after about 20 seconds if they weren't getting closer in a straight line.
- **Fix:** They follow the route corner by corner, re-check it every second, and measure progress along the route, so going around a wall doesn't count as being stuck.
- **New:** Closed doors and gates on the way are opened from within reach, including from the bottom of a short staircase, and closed again behind them if they were closed before. When a closed gate is the only way in, the companion walks to it and opens it like a player would.
- **New:** When stuck, a companion steps back to one side, jumps toward the next turn of its route (not straight at the destination), tries another way around, and only then gives up.
- **New:** A companion in deep water heads for the bank nearest to where it's going, and only one it can climb out on.
- **Change:** Walking speed matches the distance: a walk for the last few steps, a jog in between, and a run when the destination is far away (WorkRunDistance).
- **Issue:** When stamina was under half, companions avoided all water, even water they could wade through.
- **Fix:** Wading is always fine. They only avoid water deep enough to swim in, and only if their stamina won't last the swim. If there's no dry way around, they wait on the bank until they've rested.
- **New:** Companions jump narrow streams to stay dry, and take a running jump into wider water they're going to swim across. (In Valheim you only get Wet when the water is deep enough to swim in.)
- **Issue:** Companions wouldn't chase anything that was swimming, even in water only waist-deep for them, and they chased modded sea animals into the water.
- **Fix:** They follow a target into water they can wade in. They leave it alone if they'd have to swim, unless it's attacking them and they have a bow or crossbow with ammo.
- **Fix:** Animals that never attack (deer and similar, including modded ones like fish and dolphins) are left alone, so they no longer pull a companion off its job.
- **Issue:** Crossbows never fired. Companions equipped them, but the game only lets the player load a crossbow.
- **Fix:** Companions now load a crossbow like a player: the reload animation, the reload time (faster with skill) and the stamina cost.
- **Fix:** A bow or crossbow only counts as ready if the matching ammo (arrows or bolts) is in the companion's inventory.
- **Issue:** Automatic repairs only looked for a station within 20 m of the companion.
- **Fix:** Near home, any repair station near home counts.
- **Issue:** If a companion couldn't reach a station to repair a broken tool, it kept trying the same trip.
- **Fix:** It looks for another tool in the chests instead. If there isn't one, it says the tool is broken and follows you.
- **New:** FollowStuckTeleport (General, off by default). When on, a companion following you that has been stuck for 15 seconds somewhere it can't see you appears next to you.

## 1.4.73
A review of the Wood, Stone and Ore jobs turned up several places where companions didn't play by a player's rules.
- **Issue:** If a companion couldn't reach a tree or rock, it kept damaging it every 5 seconds from wherever it was, through walls and cliffs.
- **Fix:** It now gives up on that target and picks another.
- **Issue:** Every swing at a tree also landed an instant extra hit, so trees took roughly double damage, sometimes from beyond reach.
- **Fix:** It's one hit per swing, and only when the swing connects.
- **Issue:** A pickaxe swing that hit the ground still counted as a hit on the rock.
- **Fix:** Ground hits no longer count.
- **Issue:** A companion could be left with no weapon or shield. This happened if equipping a tool failed, during fights, when switching between gathering jobs, and on drop-off trips.
- **Fix:** Its normal gear comes back in all of those cases.
- **Change:** One-handed tools no longer take the shield or torch off.
- **Change:** A broken tool now comes off and the companion switches to its best weapon. Near home it then repairs or replaces the tool as in 1.4.71.
- **Fix:** Swings no longer wear down a shield or torch just because the right hand was empty.
- **Issue:** Companions ended up standing on stumps and logs, and shuffled forward during every swing.
- **Fix:** They stop at a normal distance and stand still while swinging.
- **Issue:** When too heavy with no chest to use, a companion flickered in and out of its job. With no home set, it switched the job off.
- **Fix:** It now says it's too heavy, pauses the job (following you if Follow is on) and checks every few seconds for somewhere to unload.
- **New:** Drop-off rules. Near home, it uses chests only. Away from home, it also uses a cart or ship close by, or chests at an outpost. Tombstones are never used. Private chests that aren't yours are skipped.
- **New:** Ore job stone piles. Stone from mining is gathered up and dropped in a pile a few meters from the deposit, out of the way, instead of being hauled home. Pick it up yourself if you want it.
- **Fix:** Large copper and tin deposits are mined until they're finished. Companions no longer step back every 2 swings and give up after about 30 swings.
- **Fix:** After a tree falls, the companion goes to the nearest log, and only if your gather list and limits allow it.
- **Fix:** Drops from rocks are collected around the rock, and companions pick items up from a player's reach (about 2 m) instead of 3 m.
- **Fix:** The Forage job picks wild barley and flax again (only farm crops are skipped).
- **Change:** Companions turn toward what they're working on instead of snapping to face it.

## 1.4.72
- **Issue:** A companion could suddenly take off all its armor and stay that way. This happened when its inventory was reloaded, which can happen when it's far from you. Its gear was treated as missing and unequipped, and while a gathering job was running nothing put it back on.
- **Fix:** After a reload, the companion keeps wearing the matching items from its inventory.

## 1.4.71
- **Issue:** A companion couldn't open a door from the bottom of the stairs below it, so it never got inside, including on the way to bed with the door closed. It walked straight at the door instead of taking the stairs.
- **Fix:** Companions route to doors properly and open them from anywhere in reach, the same way they open chests.
- **Change:** The same in-reach rule now applies to everything a companion walks up to and uses: smelters and kilns, cauldron, fermenter, fires, chairs, crafting stations, buildings it repairs, and chests in every job. Planting still makes it stand on the field itself.
- **Issue:** When its axe broke far from home, the companion set off on a long, failing walk back to the chests at home.
- **Fix:** Chest and repair trips only happen near home (new setting `HomeTripRange`, General, default 60 m). When a job tool breaks:
  - Near home, the companion first repairs it at a workbench or forge in range. If none of those can repair it, it takes a replacement from a chest.
  - Further away, it says so ("My axe broke. I can fix it when we get home."), pauses the job and follows you. Once you're both back near home, it repairs or replaces the tool and carries on.
- **Change:** Near home, companions repair any wear on their gear, not just items below `DurabilityThreshold`.
- **Change:** Gatherers stand a little further back from what they're chopping or mining, so they no longer end up on top of stumps.
- Emptying bags also uses `HomeTripRange` now (it used a fixed 40 m before).

## 1.4.70
- **Issue:** The farmer took vegetables out of a chest to grow seed plants, then stored them straight back. It repeated this over and over, so it looked like it was standing at the chest deciding what to do.
- **Fix:** Vegetables it's carrying for seed plants stay in its pack until that seed is back up to `SeedTarget`.
- **Issue:** With chests stacked on top of each other, the companion tried to climb up to the higher one and ended up spinning in a corner.
- **Fix:** Companions open chests and pick crops from anywhere in reach, like a player does, including chests stacked up to about 2 m above or below them. Only planting still makes them stand on the field itself.
- **New:** Sprinting in bursts. Companions run until their stamina is down to half, then walk until it's back to 90%, then run again. In a fight they can run down to 30%. This applies to everything: following, jobs and fighting.
- **New:** Emptying bags. When a job is turned off or changed, or Homestead is turned on, a companion near home puts away what it picked up while working before doing anything else. It says "I need to empty my bags first." It puts each item in a chest that already has that item if one is in reach, otherwise the nearest chest with room.
  - Kept: equipped items, plus weapons, armor, tools, ammo, food and mead that you gave it.
  - Put away: everything else, including wood and other materials you gave it.
  - If the companion is away from home when the job changes, it empties its bags the next time it's back.
  - Gear and food in its inventory when you load in, or whenever you have its inventory open, count as yours, so it keeps them.

## 1.4.69
- **Issue:** Companions still woke up standing beside the bed. Old code from the original mod moved them 1.5 m to the side of the bed after the 1.4.61 fix had already stood them up properly.
- **Fix:** That old move is gone, so companions now get up where they slept.
- **Issue:** Companions ran everywhere while working and burned stamina.
- **Fix:** They now walk while doing jobs and only run when what they're heading to is far away. New setting `WorkRunDistance` (General, default 25 m, 0 = never run for work). This covers farming, tool trips, smelting, cooking, Homestead chores, repairs, restocking and gathering. Following you, fighting and hunting aren't affected.
- **Issue:** The farmer made a separate chest trip for each type of seed, walking back and forth up the stairs.
- **Fix:** When it opens a chest for seeds, it now takes up to `SeedCarry` (Farm, default 40) of every type on the PlantAny list, plus vegetables to grow seed plants from while that seed is under its target. Then it fills the fields. It keeps up to that many of each in its pack instead of storing them.

## 1.4.68
- **Issue:** The farmer sometimes stood still and gave up when what it was walking to was on a different level, like a field just below a deck or a chest up on the porch. It counted itself as close enough because it was right above or below the target, but too far to actually plant or open the chest.
- **Fix:** When the target is above or below, the companion keeps walking until it's actually there.
- **Issue:** The farmer planted one crop until it ran out of seeds instead of switching types every `PlantGroupSize` plants.
- **Fix:** After each group the farmer goes back and picks the next type on the list.

## 1.4.67
- **Issue:** Farm zones set to a specific crop, and Any zones, never fetched seeds from chests. The farmer always reported there were none, even with plenty in the chests. This has been in the farm code since before this fork.
- **Fix:** The farmer now finds seeds in chests correctly.
- **Issue:** In crowded rooms, a companion on a tool trip would stand right next to the chest and still not open it, because other chests and furniture counted as walls.
- **Fix:** Only walls, floors and other large solid pieces block reaching a chest. Other chests, tables and small clutter no longer do.
- **Change:** A companion only heads for a spot by a chest if it can actually walk all the way there. If a chest can't be reached, it tries another chest that has the tool, and the log says which chest it couldn't reach.
- **Change:** The stuck jump now only kicks in when the companion really is stuck: it hasn't moved a meter in 3 seconds, or it's been pacing without getting any closer for 8 seconds. It no longer jumps while walking around a building.
- **Change:** The default `PlantAny` list uses the real names `MushroomJotunPuffs` and `MushroomMagecap`, and short names like `JotunPuffs` still work. If your config already has a PlantAny line, it keeps your old value until you edit or delete it.

## 1.4.66
- **Issue:** In a large base, companions often couldn't "see" chests that were close by. The farmer said there was nothing to plant even with carrots and turnips in the chests, and tool and gather-limit checks could miss chests the same way.
- **Fix:** Chest searches now check every chest in range, no matter how much is built around them.
- **Issue:** A farmer with only a few seeds and no vegetables to grow seed plants from would never plant anything.
- **Fix:** In that case it plants the seeds anyway, so it gets vegetables to grow seed plants from. After that the normal minimum and target rules take over.
- **Issue:** Companions took tools out of chests through walls.
- **Fix:** On tool trips the companion now picks a spot next to the chest that it can walk to and that has nothing solid between it and the chest, and only opens the chest from there.
- **New:** A companion that's stuck on the way somewhere (stairs, ledges) jumps toward where it's trying to go, trying a few angles. This applies to tool trips and farm work.
- Names in `PlantAny` now match regardless of capital letters. "Nothing to plant" log lines now show how many of each listed seed and vegetable the farmer found.
- For troubleshooting (Debug log level): companions on tool trips and farm work log where they are, where they're headed and whether the game has a path there, every 2 seconds. The farm also lists everything a cultivator can plant once at startup.

## 1.4.65
- **Issue:** With Stay Home on, a job and the Stay Home chores (repairs, sitting, wandering) both tried to steer the companion, so it ran back and forth without finishing either.
- **Fix:** A job and Stay Home can't both be on any more. Picking a job turns Stay Home off, and picking Stay Home turns the job off.
- **Change:** "Stay Home" on the command wheel is now called "Homestead".
- **Issue:** Turning a job off and then picking Follow made the companion crawl slowly away from the chest it was returning a tool to.
- **Fix:** Tool trips now steer through the companion's normal movement, so only one thing moves it and it runs. A trip always finishes before a new job, Follow or Homestead takes over. If the companion is attacked it fights first, then carries on with the trip. In Passive stance it just keeps going.
- **Issue:** A farm zone set to "Any" could be planted with tree seeds (birch, beech and so on).
- **Fix:** Any zones only plant what's on the new `PlantAny` list (Farm section). The default list is the vanilla crops: `CarrotSeeds, TurnipSeeds, OnionSeeds, Barley, Flax, JotunPuffs, Magecap, VineberrySeeds`. Plants added by other mods can be added to the list, for example `MushroomCommon` or `Raspberry` with Plant Everything.
- **New:** `PlantGroupSize` (default 4): the farmer plants this many of one type before switching to the next, so a mixed field ends up in small blocks.
- **New:** `SeedMinimum` (default 20, replaces `SeedReserve`) and `SeedTarget` (default 100). Below the minimum the farmer only grows seed plants for that crop. Between the two it grows crops and seed plants side by side. At the target it grows only crops. It never plants seeds if that would take them below the minimum.
- **Change:** Barley, flax and other crops that grow from themselves are always planted. The farmer keeps `SeedMinimum` of each in its pack so it can replant, and stores anything over that. Seeds work the same way.

## 1.4.64
- **New:** Gather lists. `GatherWood`, `GatherStone` and `GatherOre` (Harvest section) say what each job collects, and how much. `Name:1000` sets that item's own limit, a name with no number uses the category limit (`WoodLimit`, `StoneLimit`, `OreLimit`, 500 each), and `:0` means no limit. Items not on the list aren't gathered and their drops are left on the ground.
- **New:** Limits count what's in the companion's pack and in chests near its home. Once everything it can reach on its list is at the limit, it says "We've got plenty of wood." (or stone, or ore) and waits with the job still on.
- **New:** `WoodSkipTrees` (default `Oak`): standing trees the Wood job never fells. Logs and stumps are still cleaned up.
- **New:** `BaseProtectRadius` (default 10 m): the Wood job won't fell a standing tree with anything players built that close to it.
- **New:** Falling-tree safety. When a tree comes down or a log splits, the companion steps to the side (the uphill side if there is one) and waits for things to settle. On slopes it also approaches trees and logs from the uphill side.
- **New:** When a gathering companion's pack is full, it takes a load to the chest near its home. With no home chest, `JunkCleanup` (on by default) has it drop junk to make room: materials and trophies that aren't on any gather list. Gear, tools, ammo, food, potions, seeds and anything matching `NeverDiscard` (default `Trophy`) are always kept.
- **Fix:** Ore mode no longer mines plain boulders that only drop stone (stone isn't on the default ore list).

## 1.4.63
- **Issue:** Tool trips often gave up before reaching the chest ("took too long"), so borrowed tools weren't put back.
- **Fix:** The companion now walks to a spot just in front of the chest instead of its center. Repair (buildings), Restock and Hunt now pause during a trip like the other jobs. Re-selecting Follow mid-trip no longer pulls it away. It only gives up if it can't get any closer for 20 seconds, or after 2 minutes.
- Farm's routine scanning lines now only show in the log when BepInEx is set to show Debug lines, so normal logs stay readable.

## 1.4.62
- **New:** When a job needs a tool the companion doesn't have, it looks in chests near its home (or near itself if it has no home), takes the best one and gets to work. Wood uses an axe, Stone and Ore a pickaxe, Hunt a bow and matching arrows, Farm a cultivator, Fish a fishing rod and bait, and Repair a hammer.

- **New:** Borrowed tools go back to the chest they came from when the job is turned off or changed, or to the nearest chest with room if that one is full or gone. Ammo and bait go back too, up to the amount taken. Tools the companion already had stay with it, and a borrowed tool is kept when switching between jobs that use it (Stone to Ore, for example).
- **New:** If there's no tool in the chests, the companion says what it needs ("I need an axe for that.", "I need a pickaxe for that.", "I need arrows."); jobs that already had their own line (rod, bait, bow, cultivator, hammer) keep using it.
- **New:** Seed reserve for farming. The farmer keeps enough carrot, turnip and onion seeds on hand (its own pack plus nearby chests). When it's short, it plants some of the vegetables as seed plants and harvests those to grow more. Otherwise vegetables are stored in the chest instead of being replanted.
- **New setting** (Farm section): `SeedReserve`, 20 by default. 0 means it never plants vegetables for seeds.

## 1.4.61
- **Issue:** If you opened a chest before opening a companion's inventory for the first time, that chest's item icons showed up over the companion's inventory until you quit.
- **Fix:** Leftover icons are cleared when the companion's inventory panel is built.
- **Issue:** A companion getting out of bed was pushed about a meter to the side of it.
- **Fix:** Companions now get up the way players do, at the bed's sleeping spot, before the bed becomes solid again.
- **Issue:** Farm and the gathering jobs restarted themselves every time the companion's inventory changed, cutting off what they were doing.
- **Fix:** They only re-check when they're between tasks.
- **Issue:** Farming left harvested crops lying on the ground, then stored unrelated items (wood, resin, meat and so on) in the crop chest.
- **Fix:** The farmer now picks up what it harvests and only stores crops.
- **Change:** Forage leaves crops in your farm fields alone, so they're left for the Farm job.
- **Issue:** During jobs, a companion could stand in an open doorway for 8 seconds and give up on its target meanwhile.
- **Fix:** Once the door is open, the job takes over again after a few seconds, and the job doesn't give up on its target while a door is being handled.
- Fixed the "[Diag] ... hasn't moved" warning showing while a companion was working a job.

## 1.4.60
- **Issue:** Some mods delete creatures outright instead of killing them (for example mods that clear creatures out of warded areas). A companion caught by one simply vanished, with no death and no tombstone.
- **Fix:** Companions are now protected from being deleted by other mods. They can still die, leave a tombstone and respawn as normal, and the Despawn button still works. When another mod tries to delete a companion, the log names what tried.
- **New setting** (General section): `ProtectCompanions`, on by default. Turn it off only if another mod needs to remove companions.

## 1.4.59
- **Issue:** With the "Guardstone wards remove monsters" mod installed, a companion inside an active ward was deleted, with no death and no tombstone, because that mod removes every AI character in a ward.
- **Fix:** Companions now carry the marker that mod checks for to leave a creature alone, so they're safe inside wards. A companion that was already deleted can't be brought back.

## 1.4.58
- **Issue:** Turning on Farm filled the log with errors ("Invalid IL code in FarmController:FindRipePickable") and the companion never farmed. This seems to happen when another mod changes the game's terrain lookup.
- **Fix:** The check for cultivated ground is now safe. If it can't run, the log shows one warning and farming carries on using the crops it already recognizes.
- **Issue:** Farm, Fish and the gathering jobs (wood, stone, ore, forage) wrote nothing to the log, so problems with them couldn't be tracked down.
- **Fix:** Those jobs now log what they're doing, tagged `[Farm|name]`, `[Fish|name]` and `[Gather|name]`.
- **Issue:** One food message ("ClampHealth ... food expired?") was written every frame, making logs several times bigger than they needed to be.
- **Fix:** It's now only logged when a companion's health is actually pulled down by a noticeable amount.

## 1.4.57
- **Issue:** Recalling your companions (holding the command key) pulled in every companion you own, wherever they were and whatever they were doing, so it was hard to call a few over without pulling workers off their jobs.
- **Change:** A normal recall now only reaches companions within 40m of you, and skips any with Command switched off on the wheel.
- **New:** Keep holding the command key for 2 seconds to recall every companion at any distance, including ones with Command switched off. The screen shows "Recalling all companions". Companions pulled off a job this way need to be set back to it.
- **Change:** Point-and-command orders (sit at a fire, attack, sleep, deposit and so on) also only go to companions within that range with Command switched on, so pointing at a fire doesn't send your whole base over.
- **New settings** (Controls section): `RecallRange` (40m by default, 0 for no limit) and `SuperRecallHoldTime` (2 seconds by default).

## 1.4.56
- **New:** When you get into bed, your companions within range of their own beds go to bed too, even if it's only evening.
- **Change:** At night, a companion near its bed now goes to sleep even if it's working a job or on Stay Home, and picks the job back up in the morning. Before, any job kept it up all night.
- **Issue:** The night skip could wait forever ("Waiting for Companion to sleep...") if a companion couldn't get to a bed.
- **Fix:** The night skip now only waits for companions that are actually heading to bed, and gives up after 30 seconds.
- **New:** Recalling your companions (the long press) wakes any that are asleep. They stay awake and follow you until you next go to bed.
- **New settings** (Sleep section): `SleepAtNight`, `SleepWhenPlayerSleeps` (both on by default) and `BedRange` (how close a companion has to be to its bed, 50m by default).
- Added extra logging to help track down companions that stop following you, and for the Repair (buildings) job.

## 1.4.55
- **Issue:** On some game versions, opening the command wheel with a controller filled the log with errors ("Method not found: ZInput.GetJoyRightStickX" or "...GetJoyLeftStickX"), and the stick didn't work on the wheel.
- **Fix:** The mod now works with either form of the game's controller-stick code. If it can't find either one, the stick reads as centered and the log shows one warning instead of an error every frame. The wheel still works with mouse and buttons.

## 1.4.54
- **Change:** The mod's internal ID is now `Maddhattr.OfflineCompanionsReborn`, and it shows as "Offline Companions Reborn" in the F1 config manager. The config file `Maddhattr.OfflineCompanionsReborn.cfg` is now the standard BepInEx config for the mod. Settings from 1.4.53, or from the old `com.profmags.companions.cfg`, carry over automatically.
- Reborn won't load if ProfMags' original Offline Companions is also installed, so the two can't run at the same time. Remove the original to use Reborn. The BepInEx log says so if this happens.

## 1.4.53
- **New:** Every companion now has a permanent ID that stays with it through death and respawn. Existing companions get one automatically the first time they load.
- **Issue:** A companion could collect items from another companion's tombstone if they had the same name and owner (for example, a new starter companion taking a despawned companion's gear).
- **Fix:** Companions only collect from tombstones stamped with their own ID. Tombstones from before this update still use the old name-and-owner match.
- Beds now remember which companion claimed them by ID, so renaming a companion keeps its bed, and two companions with the same name can't mix up beds.
- **Change:** The config file is now `Maddhattr.OfflineCompanionsReborn.cfg`. Your existing settings are copied over automatically the first time you start the game. The old `com.profmags.companions.cfg` is no longer used and can be deleted. If you edit the config by hand, use the new file.
- **Issue:** With Valheim Plus's "disable arrived message" setting on, the starter companion window never opened.
- **Fix:** If the game's first-spawn event doesn't come through, the mod now checks for the starter companion itself a few seconds after you arrive.

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
