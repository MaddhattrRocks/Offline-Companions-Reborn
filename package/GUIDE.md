# Offline Companions Reborn — Companion Guide

How your companions work: what each job does, how to set it up, and what to expect. If you're wondering why a companion did (or didn't do) something, it's probably explained here.

Most numbers below are the defaults and can be changed in the settings (see `CONFIGURATION.md`, or press `F8` in game).

## Contents

1. [Getting started](#getting-started)
2. [Talking to your companion](#talking-to-your-companion)
3. [The command wheel](#the-command-wheel)
4. [Pointing and commanding](#pointing-and-commanding)
5. [Home](#home)
6. [Following you](#following-you)
7. [Jobs](#jobs)
   - [Wood, Stone, Ore and Forage](#wood-stone-ore-and-forage)
   - [Hunt](#hunt)
   - [Fish](#fish)
   - [Farm (and how to mark farm zones)](#farm)
   - [Smelt](#smelt)
   - [Cook](#cook)
   - [Repair and Restock](#repair-and-restock)
8. [When there's nothing to do](#when-theres-nothing-to-do)
9. [Tools, chests and carrying](#tools-chests-and-carrying)
10. [Combat](#combat)
11. [Food, stamina and health](#food-stamina-and-health)
12. [Sleeping, resting and beds](#sleeping-resting-and-beds)
13. [Death and coming back](#death-and-coming-back)
14. [Getting around](#getting-around)
15. [Skills](#skills)
16. [Multiplayer](#multiplayer)
17. [Troubleshooting](#troubleshooting)

---

## Getting started

- The first time you enter a world with a character, you're offered a free companion.
- Ways to get more companions are coming soon.
- A new companion follows you. Give it some gear and food: it equips the best weapon, shield and armor it's carrying, and eats from its own inventory.
- To give it a job, open the command wheel (below) and pick one. Most jobs work best once you've set its **home**.

## Talking to your companion

**Tap** the interact key (`E`) while looking at a companion to open its **panel**:

- its inventory and equipment
- its food
- its name (you can rename it)

Drag items in and out like a chest. Anything you give it counts as its own: it keeps it and doesn't put it away in a chest.

## The command wheel

**Hold** the interact key (`E`) while looking at a companion to open its **command wheel**. Move the mouse (or stick) to a slice and click.

| Slice | What it does |
|:---|:---|
| **Follow** | Turn following you on or off |
| **Wood / Stone / Ore / Forage** | Gathering jobs |
| **Hunt** | Hunting job |
| **Fish** | Fishing job |
| **Farm** | Farming job (right-click it to open the farm zone editor) |
| **Smelt** | Smelting job |
| **Cook** | Cooking and brewing job |
| **Repair and Restock** | Looks after your base |
| **Set Home** | Makes the spot where the companion stands its home |
| **Wander** | While following: on = it moves around and looks about when you stop, off = it keeps one spot and stands still |
| **Pickup** | Picks up items as it goes, and after kills (like auto-pickup) |
| **Command** | Whether this companion answers the command key (pointing and recall) |
| **Despawn** | Removes the companion (asks first) |

Picking a job again, or picking **Follow**, turns the job off. The **inner ring** holds the [combat stances](#combat).

**Jobs and Follow:**
- **Wood, Stone, Ore, Forage and Hunt** can run with Follow on or off. With Follow on they only go for things close to you as you travel. With Follow off they work an area.
- **The other jobs** (Fish, Farm, Smelt, Cook, Repair and Restock) turn Follow off when you pick them, and turn it back on when the job ends.

## Pointing and commanding

Look at something and press the **command key** (`Z`). Your nearby companions do what makes sense for what you're pointing at:

| Point at… | They… |
|:---|:---|
| An enemy | Attack it |
| A tree, rock or ore | Gather it |
| A cart | Hitch up and pull it (point again to unhitch) |
| A ship | Board it and take a seat |
| A bed | Go to sleep in it (point again to wake them) |
| A fire | Sit by it (point again to get up) |
| A closed door | Open it |
| A chest | Put what they've picked up into it |
| A crafting station | Repair their gear there |
| Their tombstone | Go and collect their things |
| The ground | Walk there and wait |

- **Hold** the command key to call your companions to you.
- **Keep holding** for about 2 seconds to call **every** companion, however far away, including ones with Command switched off.
- Only companions within 40 m answer a normal command or recall.

**Orders come first.** While a companion carries out your order, its job waits. Afterwards it goes back to what it was doing. A Repair and Restock companion stays where you sent it for 30 seconds before going back to its chores.

## Home

**Set Home** (on the command wheel) marks where the companion stands as its home and opens the home panel, where you can change the size of the home area.

Home matters because companions:

- **claim a bed** inside the home area (shown with their name)
- **fetch tools** from the chests around home and put borrowed ones back (take a tool out of its pack and it works without one for that job)
- **empty their bags** into the chests around home when they come back or change jobs
- **work around home** when a job is started near it
- **repair gear** a bit sooner at home, where a workbench is close

"Around home" means within 60 m of the home spot (the `HomeTripRange` setting).

## Following you

- A following companion picks its own spot near you: beside, behind, sometimes ahead. It moves when you get more than 10 m away (4 m indoors).
- **Stand still for 45 seconds** and your companions come back and wait close to you.
- **Stop indoors** and they sit down near you: on a free chair or bench, otherwise the floor. Near home, they empty their bags first.
- If a companion falls far behind (50 m) and can't see you, it catches up by appearing next to you.
- They go through portals with you.

## Jobs

Every job pauses for a fight, your orders, bedtime and picking up loot, then carries on. When a job runs out of work, see [When there's nothing to do](#when-theres-nothing-to-do).

> **Companions only work while you're nearby.** The game unloads the parts of the world that no player is near, and everything in them freezes, companions included. A companion working at home while you're far away exploring isn't working; it carries on when you come back. In multiplayer, the area stays loaded while any player is near it.

### Wood, Stone, Ore and Forage

**Where they work**
- **Follow on:** only trees, rocks or plants within 10 m of you, as you travel. Nothing that close? They just follow you. Think of it as "we passed some raspberries, let's grab them".
- **Follow off:** they work an area. That's around home if the job was started near home, otherwise around where you left them, up to 40 m out. They keep working there when you leave.

**What they collect**
- Each job has a list of what to gather and how much to keep (500 of each by default, counting the companion's pack and the chests at home). Once there's enough, they stop.
- Things not on the list (like stone from a copper deposit) are left on the ground.
- **Wood:** fells trees and chops logs and stumps. It skips Oak by default, and won't fell trees within 10 m of anything you've built. It also steps aside from falling trees.
- **Stone:** mines rocks for stone, flint and the like.
- **Ore:** mines ore deposits. Spare stone from the deposit is dropped in a pile away from the ore, so it doesn't bury it.
- **Forage:** picks berries, mushrooms, flowers and the like. No tool needed.

**Early in the game**
If there's no bronze (or better) axe anywhere and no forge at home to make one, the Wood and Stone jobs also pick up branches, stones and flint lying on the ground. A companion with no axe or pickaxe at all only picks things up.

**Tools**
They need an axe for Wood and a pickaxe for Stone and Ore. A missing tool is fetched from the chests at home. If the tool can't cut what's there (a stone axe on an old tree, say), you get a message.

**When the pack is full**
- Near home: they unload into the chests at home.
- Away from home: they unload into a chest, cart or ship within 30 m.
- If there's nowhere to unload, they drop junk they don't need (not gear, food, tools or anything on their lists).

### Hunt

- **What:** creatures that drop meat, until there's enough of each kind of meat (100 by default, counting pack and chests at home). Leather and other drops are picked up as a bonus. Tamed animals, bosses and other companions are never hunted.
- **Sneaking:** they sneak up on skittish game like deer and hares, crouched and not running, so it doesn't bolt. Sneaking costs stamina like a player's: when they run low they stop, stay crouched and get their breath back (Hunting > SneakRestStamina). They wade shallow water instead of jumping it while sneaking. Chasing prey that bolted, they only run while they have stamina to spare (Hunting > ChaseRunStamina).
- **Weapons:** a bow or crossbow with ammo comes first. Without one they hunt with a melee weapon: they sneak right up and strike.
- **Throwing spears:** a hunter holding a spear throws it at skittish game from up to 12 m (Hunting > ThrowRange), as long as it has another weapon in its pack to fight on with. It sneaks only to throwing distance (Hunting > ThrowStandoff, 8 m), stops and throws, and waits for the stamina the throw needs rather than walking in to stab. After the hunt it walks over and picks the spear back up. Turn this off with Hunting > ThrowSpear.
- **Birds:** with a bow or crossbow and ammo, they shoot crows and gulls for feathers.
- **Follow on:** they hunt within 25 m of you. **Follow off:** they hunt up to 40 m around where they were set to work.
- **Chasing:** they chase prey that runs, up to 15 m further, then let it go.
- **Full pack:** with Follow off and a home set, they bring the catch home and go back out.

### Fish

**Setting up**
- Give the companion a **fishing rod** and **bait**, and stand it near water.
- It fishes around the spot where you started the job, up to 30 m out.

**How it fishes**
- It looks for fish in range and picks a spot on the bank.
- It uses the best bait it has for those fish, casts at the right distance, and waits up to 60 seconds for a bite.
- When a fish bites it strikes, then reels in carefully, easing off when the line is tight.

**Limits**
- Each kind of fish has a target (100 by default, counting its pack and the chests at home). Once that's reached, it leaves that kind alone.
- Near home, it brings its catch home when its pack is nearly full.

### Farm

The farmer **plants** in farm zones you mark, and **harvests** ripe crops nearby.

**How to mark a farm zone**

1. **Till the ground first**, with a cultivator, like you would to plant yourself. The farmer plants only on cultivated ground, and doesn't till ground itself yet.
2. **Open the farm zone editor**, either way:
   - Stand near your farmer and press `Left Alt` + `Z`. This opens the editor for the nearest companion.
   - Or right-click **Farm** on its command wheel.
3. A green square shows where the zone would go. Controls:

   | Mouse and keyboard | Controller | Action |
   |:---|:---|:---|
   | Left-click | A | Place the zone |
   | Right-click on a zone | X | Remove it |
   | Mouse wheel | Bumpers | Make it bigger or smaller (4 m to 30 m across) |
   | Hold right-click + mouse wheel | D-pad | Rotate it |
   | Esc (or `Left Alt` + `Z` again) | Start | Close the editor |

4. After placing a zone, pick the **crop** for it, or **Any Crop**.
5. Each companion can have up to **4 zones**. Then set the companion to the **Farm** job.

**Planting**
- **A zone with a crop:** the farmer plants that crop there.
- **An "Any Crop" zone:** it plants from the `PlantAny` list (carrots, turnips, onions, barley, flax, mushrooms, vineberries by default). It plants in small blocks of 4 of each, so the field ends up mixed but tidy. Tree saplings are never planted in an "any" zone.
- It spaces plants so each has room to grow, and won't plant inside a ward that doesn't let it in.

**Seeds**
- It carries up to 40 of each seed, taken from the chests.
- It always keeps at least 20 of each seed and won't plant the last ones.
- When a seed runs below 100, it plants some seed plants (carrots, turnips, onions…) alongside the crops to build the stock back up.

**Harvesting**
It picks ripe crops, going straight to the next ripe one nearby like working down a row, and stores the harvest in a chest.

### Smelt

- **Feeding:** keeps kilns, smelters, furnaces, blast furnaces and the like fed. It brings input (ore, wood for the kiln…) and fuel (coal, wood) from the chests nearby, and from the chests at home when it's near home.
- **Output:** collects what comes out and stores it.
- **No fuel in the chests:** it can take down a wood pile with a hammer and use that wood. It needs a hammer in its pack.
- **Trip size:** it carries up to 20 input and 40 fuel per trip.

### Cook

**Stations it uses**
- cauldron
- food preparation table
- mead ketill
- cooking station and spit
- iron cooking station
- oven
- fermenters
- beehives

Each kind can be switched off in the settings. It only uses recipes you know.

**What it makes**
- **Food:** cooks each food until the chests nearby (and at home) hold 20 of it. It cooks in batches of 5, like a player.
- **Reserve:** it always leaves at least 20 of each raw ingredient, so there's some left for you.
- **Mead:** brews mead bases, fills the fermenters and taps the finished mead, until there are 10 of each base and 20 of each mead. Don't want mead? Turn off `MakeMead` and it cooks food only.
- **Your progress:** it brews the mead bases you'll need for your next boss first. For example, after Bonemass that's frost resistance and medium healing and stamina.
- **Storing:** cooked food goes into a chest that already holds that food (or other cooked food).

**Fires and honey**
- It keeps the fires under its cooking stations and the oven at about half full, and tops them up when they run low.
- With no wood in the chests, it can take down a wood pile.
- It collects honey from beehives for its recipes.

### Repair and Restock

The job for a companion you leave to look after a base. Start it near home and it works around home. Start it anywhere else (an outpost, say) and it works within 30 m of where you started it.

In order of priority:

1. **Repair:** fixes building pieces below 75% health with a hammer, one swing each. It needs the piece's crafting station nearby and ward access, like a player.
2. **Restock:** keeps fires fuelled, adding fuel one piece at a time and topping up anything below 80%. Each of these can be switched on or off:
   - campfires and fire pits
   - hearths
   - braziers
   - standing torches
   - wall torches and sconces
   - hot tubs
   - anything else that burns fuel

   It doesn't fill kilns or smelters; that's the Smelt job.
3. **Beehives:** empties hives that are at least half full and puts the honey with the rest of your honey.
4. **Piles:** keeps 100 of each building material (wood, stone…) in the chests. The surplus goes into piles (Stack of Wood, Stone Pile…), built next to piles you've already made. It never starts a new pile spot on its own. When the chests drop below 100, it takes a pile down to refill them.
5. **Sorting:** sorts the chests like with like, carrying each item to the chest that already holds the most of it and merging part stacks. Gear (weapons, armor, tools) isn't moved unless you turn that on.

It fetches a hammer and fuel from the chests if it needs them, and puts leftovers back.

## When there's nothing to do

If a companion has had nothing to do for 10 seconds and isn't following you, it idles within 8 m of its job instead of standing still. It might:

- **do a small chore nearby:** repair a piece, top up a fire, collect honey, put away what it's carrying
- **sit down** on a free chair or bench (or the ground) for a while
- **wander** over to a station or chest to check on it
- **chat:** go over to you or another companion and talk

Now and then (at most every 5 minutes) it tells you why it's idle, for example "Nothing to cook right now." or "Waiting for the crops to grow."

The moment its job has something to do, it gets back to work. It also stops idling for a fight, your orders, bedtime, or you opening its panel or wheel.

## Tools, chests and carrying

**Tools**
- Jobs fetch the tool they need from the chests at home: axe, pickaxe, cultivator, rod and bait, hammer, bow and arrows.
- When the job ends, the borrowed tool goes back to its chest.

**Repairing gear**
- A companion takes worn gear to a crafting station when anything drops below 50%, or below 80% at home.
- It only uses stations a player could use right now: under a roof, with a fire if needed.
- If no station nearby can repair an item (for example, it needs a black forge you haven't built), that item is left alone for 10 minutes instead of being tried again and again.

**Emptying bags**
- When you change a companion's job, or it comes home, it puts away what it picked up while working.
- What you gave it, it keeps.

**Which chests it uses**
- Companions use chests like a player. They won't use a chest someone has open, another player's private chest, or a chest behind a ward they can't open.
- Jobs looking for a chest don't use carts or ship holds. The exceptions: a full gatherer away from home can unload into one, and you can always point a companion at one.

**Carrying**
A companion can carry 300 weight, the same as a player.

## Combat

**Stances** (inner ring of the command wheel; on a controller, push the stick partway toward one):

| Stance | Behavior |
|:---|:---|
| **Balanced** | Chooses melee or a bow/crossbow depending on distance |
| **Passive** | Never starts a fight; just follows or works |
| **Ranged** | Prefers a bow or crossbow and keeps its distance |
| **Melee** | Sticks to melee weapons |

**When they fight**
- They attack a creature once it has noticed you or them. With the `EngageOnNotice` setting off, they wait until it's actually coming for you.
- They leave alone animals that never attack, like deer. Hunt goes after game on purpose.
- Anything that attacks them is always fought back.

**How they fight**
- **Blocking:** they block with a shield, parry and counter-attack.
- **Several attackers:** they switch to their best blocking shield (a tower shield, say) when 3 or more enemies attack at once.
- **Weapon choice:** they pick the weapon that does the most damage to *this* enemy, using its weaknesses and resistances.
- **Fire:** they open with a torch against enemies weak to fire, then switch back.
- **Bows and crossbows:** used only when they carry the matching ammo, at 15 m and beyond. Under 12 m they switch back to melee.
- **Low health:** below 30% they fight defensively, and below 15% they run.
- **Backstabs:** companions don't take backstab damage.

**After the fight**
With Pickup on, they go and pick up what the kill dropped (within 30 m). They won't swim for it.

## Food, stamina and health

- **Food:** companions have 3 food slots like a player and eat from their own inventory when a slot is empty. Food adds health and stamina the same way it does for you.
- **Base health and stamina:** 25 health and 50 stamina before food, the same as a player.
- **Meads:** they drink a healing mead below 50% health and a stamina mead below 25% stamina.
- **Status effects:** being wet, cold, rested and the like change their regeneration just like yours.
- **Running and swimming:** both cost stamina. A swimming companion low on stamina (25%) heads for the shore.

## Sleeping, resting and beds

**Beds**
- Each companion claims a free bed in its home area. The bed shows its name.
- It can also use one of your beds, as long as it isn't your spawn point. So clicking a companion's bed by accident doesn't take it away (the bed still shows the companion's name). Sleeping in it does, because that makes it your spawn point. It never uses another player's bed.
- Without a home set, it only takes a bed within 20 m of a bed of yours.
- It keeps the bed through death and when you log out and back in.
- Point the command key at a bed to send a companion to sleep in it.

**Sleeping**
- **At night:** a companion within 50 m of its bed stops what it's doing and goes to bed. That includes companions working a job. It goes back to work in the morning.
- **When you sleep:** your companions go to bed too, even in the evening. The night skip waits up to 30 seconds for them.

**Resting by a fire**
- When you sit down near a fire, companions nearby with no job sit down too.
- After 20 seconds by a fire they get the Rested buff. Sitting also heals them.
- When you send a companion to sit by a fire with the command key, it stays there for up to 5 minutes, or until you point at the fire again.

## Death and coming back

- **Tombstone:** a companion that dies leaves a tombstone, marked on your map until it's emptied.
- **What it loses:** this follows your world's death penalty setting, like a player. You can change this (keep everything, keep equipped gear, or drop everything).
- **Coming back:** after 5 minutes it comes back by itself at its bed, or next to you if it has no bed. The timer keeps counting while you're away or offline.
- **Its things:** once it's back near its tombstone it walks over and collects its own things. You can also point the command key at the tombstone.
- **Skills:** it loses 25% of its skill progress, a bit like a player.

### Recovering a lost companion

Your game keeps a roster of your companions in each world, saving each one's gear and settings every minute while it's near you, and whenever the game saves your character (including when you log out or quit). If a companion goes missing for good (for example deleted by a server without the mod), you can bring it back.

**Odin's Shrine.** Build one with the hammer (Misc tab, next to a workbench) for 10 stone, 5 wood and 3 greydwarf eyes. It looks like a gravestone (or a ward stone, with Death > ShrineModel). Use it to see your companions in this world and their status. For each one that's Lost or Unknown it asks whether to bring it back; say Yes and it appears next to the shrine, say No to move on to the next one. You can change the cost with Death > ShrineRecipe, or turn the shrine off with Death > ShrineBuildable.

**From the config** (no shrine needed):

- **See their status:** turn on **General > ShowCompanionRoster** in the config. You'll see each companion as **Alive**, **Dead** (its tombstone will bring it back), **Lost** (nothing left of it, so it can be recovered) or **Unknown**.
- **Bring one back:** turn on **General > RecoverLostCompanion**. The first Lost companion appears next to you with its gear and settings from the last roster save. Both settings turn themselves back off.

What the statuses mean:

- **Unknown:** on a dedicated server your game can only check the area around you, so a companion that isn't nearby is Unknown. It may still be alive somewhere. The shrine adds a warning when it asks; from the config, turn RecoverLostCompanion on a second time within 60 seconds to recover it anyway. If it had died, its tombstone keeps whatever was in it too.
- **No doubles:** if the old companion turns up later, it's removed, and its old tombstone won't bring it back.
- **Old tombstones:** a recovered companion still knows its own tombstones. Like a respawned one, it walks over and collects its things when it's near one (within Death > TombstoneRecoveryRange, 50 m). You can also empty it yourself.
- A companion only joins the roster once it's been near you with version 1.5.1 or later. A despawned companion is taken off the roster.

## Getting around

Companions find their own way:

- **Doors:** they open doors and gates as they reach them, line up with the doorway, and close the door behind them.
- **Steps and ledges:** they hop up steps and drop off ledges up to 3 m (no fall damage) when that's much shorter than going round.
- **Ladders:** to get up to where you are (a tower, a platform), they climb a wooden ladder like you would: walk to its foot and into it, and the ladder lifts them up. If the ladder starts above the ground, or pushing into it doesn't lift them, they jump onto it; after a few jumps that don't get them on, they try another way up. A ladder that starts from a floor is climbed from that floor, not jumped at from underneath it. Ladders stacked on top of each other are climbed all the way up. If the ladder stops a little short, they jump from the top onto the floor or ledge next to it, or up onto the top of a wall and from there onto the floor. A tower of short ladders with landings between is climbed as one staircase: they plan the whole chain first, go from each top straight to the next foot, and remember chains that worked (after a fall they get straight back on). Going down works the same way. They pick a ladder whose foot they can walk to on their own level and whose top gets them closest to where they're going, and go up a chain of ladders one after another. A ladder that didn't work, or keeps landing them back where they started, is left alone for a few minutes.
- **Following your path:** when you're on another floor and they can't find a way there themselves (a tight tower of short ladders and small floors), they go to the last spot where you were on their level and follow your exact steps from there, into each ladder the way you went and jumping where you jumped. If you jumped down something that would cost them more than 30% of their health, they find another way down instead.
- **Edges:** they don't walk off a drop of more than 3 m (Movement > MaxDrop) onto dry ground - they stop at the edge and look for a ladder, stairs or a safe drop instead.
- **Getting down:** they take the stairs or the way round when there is one, or go back down the ladder they came up. Only when there's no other way down do they jump down a drop, up to 8 m (Movement > StuckDrop) and never one that would cost more than 30% of their health right now A gap over a harmful drop is only jumped with room to spare.
- **Fall damage:** companions take fall damage like a player: none up to 4 m, more the further they fall (Feather Fall helps them too).
- **Water:** they jump narrow streams and small gaps. They walk around water when the dry way isn't much longer, and otherwise swim.
- **Walking or running:** they walk while working and run when what they're heading for is more than 25 m away.
- **Fire and tar:** they avoid fire and tar pits.

## Skills

Companions get better at what they do, like a player. Fighting raises their weapon skills, and jobs raise the matching skills (woodcutting, pickaxes, fishing, sneaking…). When a skill levels up, you get a message.

## Multiplayer

- **Your companion stays yours.** Sometimes another player's game runs your companion (the game hands characters to whichever nearby player's computer it likes). Even then, it follows you, sleeps when you sleep and sits when you sit.
- **Your orders always work.** When you give an order or open its wheel, your game takes over running it.
- **Shared chests are safe.** Items a companion moves are saved properly, with no losses or duplicates. It won't use a chest another player has open.
- **Credit goes to the owner.** Crops it plants and piles it builds count as built by its owner.
- **Everyone needs the mod, including the server.** On a dedicated server, install the mod on the server as well as on every player's game. A server (or a host) without the mod deletes any companion it loads. You can bring a deleted companion back from the roster (see [Recovering a lost companion](#recovering-a-lost-companion)), but it's better not to lose it in the first place. A dedicated server always keeps the area around the middle of the world loaded, so companions near the world spawn are at risk even while everyone is online. Players without the mod simply can't see companions.

## Troubleshooting

**My companion is just standing there.**
Usually its job has nothing to do, and it will start idling within a few seconds. Common reasons:
- **Cook:** it's leaving the ingredient reserve for you.
- **Gathering:** the items have reached their limits.
- **Farm:** there's no cultivated ground in its zones.
- **Smelt:** it has no input in the chests.

Check the log (below) for the reason.

**My companion has disappeared.**
Use Odin's Shrine, or turn on **General > ShowCompanionRoster**, to see its status. If it's Lost or Unknown, see [Recovering a lost companion](#recovering-a-lost-companion).

**It didn't get anything done while I was away.**
The game unloads parts of the world that no player is near, and companions there freeze until someone comes back. A companion only works while you (or another player) are close enough for its area to stay loaded.

**It won't go to bed.**
It needs a free bed in its home area (set a home first), and it has to be within 50 m of the bed.

**It keeps walking to a station to repair.**
At home, gear is repaired once it drops below 80%. You can change `HomeThreshold` in the Gear Repair settings.

**It ignores a chest.**
That chest may be open, private (built by another player), behind a ward, or outside its range.

**Reading the log**
Companions write what they're doing and why to `BepInEx/LogOutput.log`, for example "Nothing to cook right now" or "No station within 20m can repair…". If something seems wrong, that file is the best thing to include in a bug report.
