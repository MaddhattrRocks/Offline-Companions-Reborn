# Offline Companions Reborn

A Valheim **1.0** compatibility update of **[Offline Companions](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) by [ProfMags](https://thunderstore.io/c/valheim/p/ProfMags/)**.

**All credit for the original concept, design, and the original code goes to ProfMags.** This update exists because the original mod predates Valheim 1.0 and needed a set of API/signature fixes plus some bug fixes to keep working. If you're not on Valheim 1.0, or ProfMags ships an official 1.0 update, use the original mod instead — go show it (and them) some love.

> Please go check out ProfMags' other work too: [Thunderstore profile](https://thunderstore.io/c/valheim/p/ProfMags/) · [Patreon](https://www.patreon.com/c/ProfMags)

> **Note:** Some of the "working" modes (smelting, farming, etc.) are still being rebuilt and may not function correctly yet. Combat and gear are solid. I'm doing regular updates to flesh out and fix these pieces, aiming to get companions acting as much like another player as the game's constraints allow. If you find a bug, please let me know — I'm also working out the best way to let people submit their BepInEx `LogOutput.log`, since that contains very useful information for tracking these down.

> **Note 2:** Smelting, Farming (still requires setting a zone with left-Alt + z), Wood, and Repair are mostly working now. There may still be some occasional odd behavior that will be cleaned up over the next few updates.

## Recent Highlights (v1.4.75)

- Farmers work down a row, going straight from one ripe crop to the next instead of walking back and forth.
- Companions get out of doorways faster, and doors are no longer shut on them while they're stuck.
- Companions in Passive stance hold their strongest weapon.
- Reborn no longer shows as incompatible with ProfMags' original mod; it just won't start while the original is installed.

See `CHANGELOG.md` for the full version history.

## Support

If you'd like to support the Valheim 1.0 compatibility work and ongoing maintenance on this update specifically, you can do that here: [Patreon](https://www.patreon.com/cw/MaddHattr) · [Buy Maddhattr a Coffee](https://ko-fi.com/maddhattr). And if you appreciate the mod itself, ProfMags' own Patreon (linked above) is the place for that.

## What This Mod Does

Offline Companions Reborn adds persistent NPC allies to Valheim. A companion spawns with you automatically the first time you enter a world, and more can be added via console commands.

Companions aren't pets — they're teammates with their own inventory, gear, stamina, food system, speech lines, and a purpose-built AI. Point them at your work and they'll actually do it: fight beside you, chop wood, mine, forage, farm crops, fish, hunt, cook and brew, run your smelters, repair and maintain your base, haul your cart, sit by the fire, sleep in beds, and follow you through portals. Leave one at home and it will keep working — refueling fires, repairing walls, sorting chests, tending crops.

Everything (appearance, inventory, gear, mode, home position, skills, ownership) is saved to the world and survives sessions, zone changes, server restarts, and deaths. When a companion dies, they leave a tombstone that shows on your map. They respawn at their claimed bed (or next to you if they don't have one), and once you bring them near their tombstone they'll collect their own gear.

### Commanding Companions

- **Tap** the interact key on a companion to open their inventory panel — manage gear, feed them, rename them.
- **Hold** the interact key (or the gamepad equivalent) to open a **radial command wheel** with everything below, plus toggles for Follow, Homestead, Wander, and Auto Pickup.
- **Point-to-command:** aim your crosshair at something in the world and press the command hotkey — companions figure out the right action from context (attack an enemy, gather from a resource node, haul a cart, board a ship, sleep in a bed, sit at a fire, open a door, deposit into a chest, recover a tombstone, or just walk to a spot and wait). A long press recalls every companion to your side.

### Combat

Companions fight with a defensive-first combat AI: they read incoming attacks, raise a shield and hold the block through the hit, then counter, and can deliver a power attack when an enemy is staggered. Four **combat stances** (selectable from the radial wheel's inner ring) control how they engage. On a controller, push the stick partway toward a stance (up for Balanced, right for Passive, down for Ranged, left for Melee) and it stays selected when you let go:

| Stance | Behavior |
|:---|:---|
| **Balanced** | Standard thresholds — blocks, dodges, power-attacks, retreats and re-engages automatically |
| **Passive** | Never fights; just follows or idles |
| **Ranged** | Prefers a bow and keeps its distance, falling back to melee if it runs out of arrows |
| **Melee** | Forces melee weapons even with a bow available |

They retreat below a configurable HP threshold, re-engage once healed up, and won't take backstab damage.

### Autonomous Work Modes

Set a work mode from the radial wheel and a companion will handle it on its own, pausing to fight off anything that gets close and resuming once it's dealt with:

- **Gather Wood / Stone / Ore** — finds and harvests the nearest matching resource, equipping the right tool automatically.
- **Forage** — picks berries, mushrooms, flowers, and other ground items, no tool needed.
- **Farm** — harvests ripe crops, replants from a seed chest, and deposits produce; **Farm Zones** let you mark out exactly which plots to plant and with what crop.
- **Fish** — casts, waits for a bite, and reels it in, given a rod and bait.
- **Hunt** — takes down passive wildlife (boar, deer, chicken, hare) at range so it doesn't spook the herd.
- **Smelt** — keeps kilns and furnaces fed with ore and fuel from nearby chests and collects the output.
- **Cook** — cooks meals and brews/fills/taps mead at a cauldron and fermenters.

### Homestead

Toggle **Homestead** and a companion stays near its saved home point instead of following you. With **Follow** off too, it rotates through repairing structures, refueling fires, sorting chests, farming, cooking, and just living at home (sitting, wandering, using workstations) on its own, so a companion parked at base actually keeps the place running while you're out. Picking a job turns Homestead off, and turning Homestead on ends the job.

### Inventory, Equipment & Repair

Each companion has a full inventory grid and auto-equips the best weapon, shield, and armor it's carrying — gear works exactly like player gear, with durability that drains on use and drops off automatically when broken. Near home, a companion repairs any worn gear at a crafting station; away from home it waits until gear drops below `DurabilityThreshold` (50% by default).

### Food, Stamina & Speech

Companions eat from up to three food slots just like a player (health/stamina bonuses, the same burn-timer curve), auto-consuming from their own inventory and using health/stamina meads automatically when they need them. They have context-aware speech (combat lines, idle chatter, hunger complaints, and more) as overhead text and/or voice audio, fully translatable.

### Everything Else

Companions show up on your minimap, get an overhead HUD with health/stamina/weight bars, level up skills as they work and fight, avoid drowning and tar pits, open and close doors on their own, get the Rested buff by a fire, and teleport through portals with you automatically. An in-game config panel (via a hotkey) exposes essentially every tunable value without touching a config file by hand.

*(That's the original mod's feature set, condensed — see [ProfMags' own README](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) for the full, exhaustive writeup.)*

## What This Update Changes

This started as a straight Valheim 1.0 compatibility patch, then grew to include some bug fixes and feature additions on top:

- Fixed a companion-inventory UI bug.
- Companions now fall in with a nearby real player and keep fighting if their owner dies mid-session, instead of standing idle until respawn.
- Fixed a bug where a player-issued direct attack order could silently bypass the companion's weapon/ammo/shield/flee AI entirely, leaving it on whatever it happened to have equipped.
- Fixed companions getting stuck holding a ranged weapon at ranged-engagement distance even after falling back to melee (out of ammo).
- Added a vulnerability check when engaged with an enemy — companions score weapon choice against that specific enemy's actual resistances/weaknesses and will switch weapon type if it calculates to do more damage, rather than just picking on raw damage numbers.
- Companions claim their own bed like a player would, keep it through death, and show their name on it.
- Rebuilt death and respawn: a respawn timer that keeps counting while you're away or offline, respawning at the companion's bed or next to you, a `DeathItemRule` setting for what they keep, and tombstones that stay on the map until they're emptied.
- Controller support for picking combat stances on the command wheel.
- Fixed a startup hang for some players using a non-English game language.
- Jobs fetch the tool they need from chests near home, put borrowed tools back, and repair or replace broken ones.
- Gather lists and limits for the Wood, Stone and Ore jobs (`GatherWood`, `GatherStone`, `GatherOre`), with falling-tree safety and stone piles away from ore.
- Farming keeps a seed supply (`SeedMinimum`, `SeedTarget`), plants in small blocks of each crop (`PlantGroupSize`), never plants tree seeds, and uses `PlantAny` for what "any" zones may grow.
- Companions go to bed when you do, empty their bags when a job changes, and sprint in bursts.
- New movement for trips and walking to bed: doors and gates opened and closed on the way, routes around walls and water, stream jumping, and a set of steps to get unstuck.
- Companions can load and fire crossbows, and only use a bow or crossbow when they carry the matching ammo.
- "Stay Home" on the command wheel is now "Homestead". Picking a job turns it off, and turning it on ends the job.
- The config file is now `Maddhattr.OfflineCompanionsReborn.cfg` (settings from the old file carry over automatically).

See `CHANGELOG.md` for the version-by-version detail.

## Future Plans

- Continue developing the companion AI so it behaves more like another player, rather than so much like a bot.
- Confirm functionality of all the other features.
- Clean up the settings/config options.
- Add a way to recruit companions from Haldor's shop.

## Installation

Install with a mod manager (recommended), or manually drop `Companions.dll` into your `BepInEx/plugins/` folder.

**Remove ProfMags' original Offline Companions first.** It doesn't work on Valheim 1.0, and Reborn won't start while it's installed (the BepInEx log says so).

**Requires:** [BepInEx](https://valheim.hexium.gg/mods/denikson/BepInExPack_Valheim) and [Harmony](https://github.com/pardeike/Harmony) — both pulled in automatically by BepInExPack.

## Source

The full source code will be available on GitHub soon, so anyone can verify or continue this project.

## License

MIT — see `LICENSE`. This is the same license the original Offline Companions mod is released under; this update keeps ProfMags' copyright notice in place per the license terms, alongside the additional changes made here.

## Disclaimer

This is an unofficial, community update made to keep a great mod working on Valheim 1.0. It is not affiliated with or endorsed by ProfMags. If ProfMags ever wants this taken down, merged, or handled differently, just ask — happy to work it out.

Parts of the code for this mod have been updated and created using AI tools to assist. This is done with specific instruction, code review, and rigorous and repeat testing before releasing a new version. AI tools just allow the work to be done faster and have no impact on creative direction.
