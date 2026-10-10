# Offline Companions Reborn

A Valheim **1.0** update of **[Offline Companions](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) by [ProfMags](https://thunderstore.io/c/valheim/p/ProfMags/)**.

**All credit for the original concept, design, and the original code goes to ProfMags.** This update started because the original mod predates Valheim 1.0 and needed API fixes to keep working. It has since grown into a large rework of how companions move, work and think, aiming to make them act like another player rather than a bot. If you're not on Valheim 1.0, or ProfMags ships an official 1.0 update, use the original mod instead — go show it (and them) some love.

> Please go check out ProfMags' other work too: [Thunderstore profile](https://thunderstore.io/c/valheim/p/ProfMags/) · [Patreon](https://www.patreon.com/c/ProfMags)

> **Found a bug?** Please report it with your BepInEx `LogOutput.log` (in `BepInEx/` inside your Valheim or mod-manager profile folder) — companions log what they're doing and why, which makes problems much easier to track down.

## Recent Highlights (v1.4.156)

This is a big update. With working jobs! The short version:

- **New movement.** Companions find their own way around your base like a player: opening and closing doors as they go, lining up with doorways, hopping steps, dropping off ledges, jumping streams and small gaps, and walking around water instead of swimming when the dry way isn't much longer.
- **Idle behaviour.** A companion whose job has nothing to do no longer just stands there. It does small chores nearby, sits down, wanders over to check on stations and chests, or comes over for a chat — and gets straight back to work the moment there's something to do.
- **Repair and Restock.** A new job that keeps your base running: repairs damaged building pieces with a hammer, keeps fires, torches, braziers and hot tubs fuelled, empties beehives, stacks surplus wood and stone into piles, and sorts your chests.
- **Cooking.** Cooks and brews at every kind of cooking station like a player: cauldron, prep table, mead ketill, spit, iron cooking station, oven and fermenters, keeping its own fires lit and brewing the meads you'll need for the next boss first.
- **Gathering works like Hunt.** With Follow on, Wood, Stone, Ore and Forage only go for what's close to you as you travel. With Follow off they work an area. Early in the game they also pick up branches, stones and flint by hand.
- **Your orders come first.** Every order you give takes priority over the companion's job, and the job picks up where it left off afterwards.
- **Multiplayer.** A companion always treats its owner as its player, even when another player's game happens to be running it. It uses chests safely (no lost or duplicated items) and respects wards and private chests.
- **Cleaned-up settings.** The config file has been reorganised into clear sections with plain-language descriptions. Settings you'd already changed are carried over automatically.

See `CHANGELOG.md` for the version-by-version detail.

## Support

If you like this mod and would like to support its ongoing development or any of my other work: [Patreon](https://www.patreon.com/cw/MaddHattr) · [Buy Maddhattr a Coffee](https://ko-fi.com/maddhattr).

## What This Mod Does

Offline Companions Reborn adds persistent NPC allies to Valheim. A companion is offered to you the first time you enter a world. Ways to get more companions are coming soon.

Companions aren't pets — they're teammates with their own inventory, gear, food, stamina, skills and speech. Point them at your work and they'll actually do it: fight beside you, chop wood, mine, forage, farm, fish, hunt, cook and brew, run your smelters, look after your base, haul your cart, sit by the fire, sleep in beds, and follow you through portals.

Everything (appearance, inventory, gear, job, home, skills, ownership) is saved with the world and survives sessions, zone changes, server restarts and deaths.

Click a section below for the details. For the full picture, see the **Companion Guide** (`GUIDE.md`) and the **Settings Reference** (`CONFIGURATION.md`), included with the mod and on [GitHub](https://github.com/MaddhattrRocks/Offline-Companions-Reborn/tree/main/package).

<details>
<summary><b>Commanding your companions</b></summary>

- **Tap** the interact key on a companion to open its panel: inventory, gear, food and name.
- **Hold** the interact key to open the **command wheel**. Pick a job, switch Follow on or off, set its home, and toggle Wander, Pickup and Command. The inner ring holds the combat stances.
- **Point and command:** aim at something and press the command key (`Z` by default). Companions work out what you mean from context:
  - an enemy — attack it
  - a resource — gather it
  - a cart — haul it
  - a ship — board it
  - a bed — sleep in it
  - a fire — sit by it
  - a door — open it
  - a chest — put their things in it
  - a crafting station — repair their gear there
  - a tombstone — recover it
  - the ground — walk there
- **Hold** the command key to call nearby companions to you. Keep holding for a couple of seconds to recall every companion at any distance.
- **Your orders come first.** While a companion carries out an order, its job waits, then picks up where it left off.
- **Set Home** marks where a companion lives. It claims a bed nearby, brings tools back there, empties its bags there and works around it.

</details>

<details>
<summary><b>Combat</b></summary>

Companions fight with a defensive-minded combat AI. They raise a shield and block incoming hits, parry and counter-attack, and switch to their best blocking shield when several enemies attack at once. They pick their weapon based on the enemy they're facing — its actual weaknesses and resistances — rather than raw damage. They also open with a torch against fire-weak enemies.

Bows and crossbows are used when the companion carries the matching ammo. They load and fire crossbows properly, and switch to melee when an enemy closes in.

Companions don't pick fights with creatures minding their own business. By default they attack a creature once it has noticed you or them. With `[Combat] EngageOnNotice` off, they wait until it's actually coming for you. Anything that attacks them is always fought back. Below a set health level they fight defensively, and at very low health they run.

Combat stances (inner ring of the command wheel; on a controller, push the stick partway toward one):

| Stance | Behavior |
|:---|:---|
| **Balanced** | Picks melee or ranged depending on the situation |
| **Passive** | Never starts a fight; just follows or works |
| **Ranged** | Prefers a bow or crossbow and keeps its distance |
| **Melee** | Sticks to melee weapons |

After a fight, a companion goes over and picks up what the kill dropped (with Pickup on).

</details>

<details>
<summary><b>Gathering: Wood, Stone, Ore and Forage</b></summary>

- **With Follow on**, a gathering companion works like Hunt. It goes for trees, rocks or plants close to you (10 m by default) as you travel, and otherwise follows you. Forage is about the berries you happen to walk past, not clearing the whole area.
- **With Follow off**, it works an area: around home if it was started near home, otherwise around where you left it.
- **What and how much:** gather lists and amounts decide what it collects and stops at — wood types, stone and flint, each ore, each forage item. Items not on the list are left on the ground. Amounts count what's in its pack plus the chests at home.
- **Early game:** with no bronze axe anywhere and no forge to make one, the Wood and Stone jobs also pick up branches, stones and flint lying around.
- **Safety:** it steps aside from falling trees and won't fell trees right next to your buildings. Certain trees can be left alone entirely (Oak by default).
- **Full pack:** it unloads into a chest. Away from home it can also use a cart or ship nearby. If there's nowhere to unload, it can drop junk it doesn't need.

</details>

<details>
<summary><b>Hunting and Fishing</b></summary>

**Hunt**
- Goes after creatures that drop meat, until it has enough of each kind of meat. Tamed animals, bosses and companions are never hunted.
- Sneaks up on skittish game like deer to catch them off guard.
- Shoots birds for feathers (bow or crossbow needed).
- Picks up the drops, and brings the catch home when its pack is full.
- With Follow on it hunts near you; with Follow off, around where it was set to work.

**Fish**
- Casts with a rod and bait, picking the best bait for the fish nearby.
- Waits for a bite, strikes, and reels the fish in while watching the line.
- Brings the catch home when its pack is full.
- Each kind of fish has a target, so it stops when you have enough.

</details>

<details>
<summary><b>Farming</b></summary>

- Picks ripe crops, working down the rows, and stores the harvest.
- Replants in the farm zones you mark out with the farm zone editor (`Left Alt` + `Z` by default). A zone can be set to a particular crop or to "any".
- Keeps a seed reserve. It grows seed plants (carrots, turnips, onions…) alongside crops to build the stock back up, and never plants your last seeds.
- Plants in small blocks of each crop and never plants tree saplings in an "any" zone.
- Respects wards, like a player building.

</details>

<details>
<summary><b>Smelting and Cooking</b></summary>

**Smelt**
- Keeps kilns, smelters, furnaces and the like fed with ore and fuel from nearby chests (and the chests at home).
- Collects the output and stores it.
- With no fuel in the chests, it can take down a wood pile with a hammer and use that.

**Cook**
- Uses every kind of cooking station like a player: cauldron, food preparation table, mead ketill, spit and cooking station, iron cooking station, oven, fermenters and beehives. Each one can be switched off in the settings.
- Cooks until each food reaches its target and always leaves a reserve of ingredients for you.
- Brews mead bases in batches, fills and taps fermenters, and puts cooked food with food of the same kind. Mead making can be switched off.
- **Brews for your progress:** mead bases are prioritised by the last boss you beat, to get you ready for the next one.
- Keeps the fires under its cooking stations and the oven fuelled — just enough to cook with.

</details>

<details>
<summary><b>Repair and Restock (looking after your base)</b></summary>

A job for the companion you leave at home (or at an outpost). It keeps an area running on its own, as long as you (or another player) are close enough for the game to keep that area loaded — companions in unloaded areas freeze until someone comes back:

- **Repair:** fixes damaged building pieces with a hammer, one swing at a time like a player. It needs the right crafting station nearby and ward access.
- **Restock:** keeps campfires, hearths, braziers, torches, sconces and hot tubs fuelled. Each kind can be switched on or off.
- **Beehives:** empties them and puts the honey away.
- **Piles:** stacks surplus wood, stone and the like from the chests into piles next to the piles you've already built, keeping a set amount in the chests for building. When the chests run low, it takes a pile down to refill them.
- **Sorting:** sorts the chests like with like, carrying each item to the chest that already holds the most of it.

It fetches what it needs (a hammer, fuel) from the chests and puts leftovers back. If you send it somewhere, it waits there a little while before going back to its chores.

</details>

<details>
<summary><b>When there's nothing to do (idle behaviour)</b></summary>

When a companion's job runs out of work and it isn't following you, it idles near its job instead of standing still. It might:

- do small chores nearby: repair a piece, top up a fire, collect honey, put away what it's carrying
- sit down on a chair or bench (or the ground) for a while
- wander over to check on a station or chest
- go over to you or another companion and chat

Now and then it tells you why it's idle ("Waiting for the crops to grow."). It gets straight back to work the moment the job has something to do. It also stops idling for a fight, an order, bedtime, or you opening its menu.

</details>

<details>
<summary><b>Gear, tools and repair</b></summary>

- Each companion has a full inventory and equips the best weapon, shield and armor it's carrying. Durability works like a player's.
- **Tools:** jobs fetch the tool they need (axe, pickaxe, cultivator, rod and bait, hammer…) from the chests at home and put borrowed ones back afterwards. A broken tool is repaired or replaced.
- **Gear repair:** worn gear is taken to a crafting station a player could use (roof and fire where needed). At home this happens a bit sooner. An item no station nearby can repair is left alone for a while instead of being tried over and over.
- **Bags:** when you change a companion's job, or it comes home, it empties what it picked up into the chests.

</details>

<details>
<summary><b>Food, rest, beds and death</b></summary>

- **Food:** companions eat from up to three food slots like a player, from their own inventory. They drink healing and stamina meads when they need them.
- **Rest:** they get the Rested buff by a fire, and when you sit down near a fire, companions nearby sit down with you.
- **Beds:** each companion claims a bed like a player, which shows its name. It goes to bed at night (and when you do), then back to its job in the morning.
- **Death:** a companion that dies leaves a tombstone that stays on your map until it's emptied. It comes back after a while at its bed (or next to you) and collects its own gear when it's near its tombstone.
- **Death penalty:** what it loses on death follows your world's setting by default, and can be changed.

</details>

<details>
<summary><b>Multiplayer</b></summary>

- **Your companion stays yours.** In multiplayer, the game sometimes hands a companion to whichever nearby player's computer it likes. A companion still treats its owner as its player: it follows you, sleeps when you sleep, and carries out your orders, whoever's game is running it.
- **Chests are safe to share.** A companion won't use a chest someone has open, another player's private chest, or a chest behind a ward it can't open. Items it moves are saved properly, with no losses or duplicates.
- **Credit goes to the owner.** Crops and piles a companion makes count as built by its owner.
- **Night skip:** companions hold the night skip (up to 30 s) while they walk to bed.

</details>

<details>
<summary><b>Settings</b></summary>

All settings are in `BepInEx/config/Maddhattr.OfflineCompanionsReborn.cfg`. They can also be changed in game with the settings panel (`F8` by default). Each setting has a plain-language description.

They're grouped into: **General, Controls, Follow, Movement, Combat, Food, Stamina, Rest, Death, Looting, Gear Repair**, one section per job (**Gathering, Hunting, Fishing, Farming, Smelting, Cooking, Repair and Restock**), **Idle, Speech**, and **Advanced** (fine-tuning most players won't need).

**List settings** (what to gather, cook, hunt, plant…) all use the same format:
- `*` = everything
- `Name` = this one
- `-Name` = never this one
- `Name:Number` = this one, with its own amount

The names you can use are written to `OfflineCompanionsReborn.Cooking.txt`, `.Hunting.txt` and `.Fishing.txt` in the config folder at startup.

Updating from an older version? Your changed settings are carried over to their new names automatically the first time you load in.

</details>

## Installation

Install with a mod manager (recommended), or manually drop `Companions.dll` into your `BepInEx/plugins/` folder.

**Remove ProfMags' original Offline Companions first.** It doesn't work on Valheim 1.0, and Reborn won't start while it's installed (the BepInEx log says so).

**Requires:** [BepInEx](https://valheim.hexium.gg/mods/denikson/BepInExPack_Valheim) and [Harmony](https://github.com/pardeike/Harmony) — both pulled in automatically by BepInExPack.

## Future Plans

- Keep developing the companion AI so it behaves more like another player.
- More multiplayer features.
- A review of skills and stats (health, regen, stamina, skill gains) against a player's.
- Combat improvements.
- A full ship and cart review.
- Outposts: a way for companions to treat a second base like home.
- Smarter chest organisation (ore near the smelter, food near the cooking stations…).
- Add a way to recruit companions from Haldor's shop.

## Source

The full source code is on GitHub: [MaddhattrRocks/Offline-Companions-Reborn](https://github.com/MaddhattrRocks/Offline-Companions-Reborn).

## License

MIT — see `LICENSE`. This is the same license the original Offline Companions mod is released under; this update keeps ProfMags' copyright notice in place per the license terms, alongside the additional changes made here.

## Disclaimer

This is an unofficial, community update made to keep a great mod working on Valheim 1.0. It is not affiliated with or endorsed by ProfMags. If ProfMags ever wants this taken down, merged, or handled differently, just ask — happy to work it out.

Parts of the code for this mod have been updated and created using AI tools to assist. This is done with specific instruction, code review, and rigorous and repeat testing before releasing a new version. AI tools just allow the work to be done faster and have no impact on creative direction.
