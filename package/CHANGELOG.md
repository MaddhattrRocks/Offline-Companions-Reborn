# Changelog

All notable changes to this fork of ProfMags' Offline Companions. Versions before 1.4.0 are the original mod (see [ProfMags' Thunderstore page](https://thunderstore.io/c/valheim/p/ProfMags/Offline_Companions/) for that history) plus the initial batch of Valheim 1.0 API-compatibility fixes that let the mod load and run at all on 1.0.

## 1.5.8
- Controller binds for every action, each with an optional modifier (default: hold block + Y to command, hold block + X for the wheel); any bind can be cleared, keyboard keys too; hints show your controller's buttons.
- Torches: companions carry one at night and in dark dungeons and caves, not in a lit base or next to you holding yours; it burns down like yours (Torch > BurnsDown) and is never repaired.

## 1.5.7
- The beehive order goes to the nearest companion that isn't resting; companions don't go to bed in the middle of an order.
- Companions step round a tree or rock that is in the way of their swing.
- Companions get encumbered like a player when they carry too much; a strength belt raises their limit.
- Portals follow the game's item rules for companions (General > PortalItems); ore they carry is left in a nearby chest, or they wait.
- Fixed the summon window not taking clicks for some players.
- Stone and Ore no longer mine walls or other built pieces.
- Wood takes fallen logs first, then stumps, then standing trees, finishes the log it split before starting another, and chops a log where it can reach it with nothing in the way.
- Wood: chops a log from one spot instead of moving along it, and only steps aside when a tree comes down, not when a log breaks.
- Gathering: a companion whose swings aren't damaging what it's working on (a rock in the way) tries another side, then moves on.
- Gathering: companions stay where their swings land until the target breaks, wait for a fresh log to stop rolling, and keep track of every piece of a tree they cut.
- Wood: a log that keeps rolling or is stuck up in the branches is left for a moment instead of chased.
- Companions jump out of the water at a bank like a player instead of getting stuck.
- Wood: fixed standing under a log caught in a tree, and picking up and dropping the same log when you walk away.
- Companions keep clear of the hearth when walking round it, even in a tight room.
- Companions no longer store things in or take things from world-generated chests.
- Repairs: companions without a home repair worn gear in a base like at home, and an item they couldn't fix out in the wild is looked at again once they're back.

## 1.5.6
- Combat: full combos, parries and blocks by chance (better with skill), spacing and going in on an opening, flanking, finishing blows, stamina kept for blocking.
- Bows drawn like a player (draw time, partial draws); Ranged stance carries the bow; Balanced takes the bow for enemies melee can't reach.
- Fights: only enemies in sight, no doors opened mid-fight, defensive in dungeons, no swimming after enemies, back to you past 35 m (Combat LeashFromYou), unreachable enemies let go and picked up again once reachable.
- Fixed ticks: a latched tick is fought first with full combos.
- Safer movement: spike walls avoided, fight moves stay off ledges, water and fire; follows you through dungeon doors even mid-fight.
- Fixed getting on and off boats: jumps aboard, or swims and wades to the ladder; walks to a seat (or the one you point at); gets off with you. No teleporting.
- Swimming uses stamina like the player (Stamina SwimDrainAtSkill0).
- Odin's Shrine: summon more companions for coins; recover lost or unsummoned companions; menus in the game's look.
- Collects honey from a beehive via command.
- Fixed the overhead stamina bar turning grey after a portal.
- Fixed a companion warping to you when you got off a ship.
- Companions eat again with 3 minutes of food left (Food > EatAgainMinutes); follow you indoors to a chair up to 8 m away.

## 1.5.5
Speech overhaul: companions talk less, and what they say and show tells you what they're doing and why not.
- Status: look at a companion and a line under its name says what it's doing ("Wood - chopping Beech", "Repair and Restock - fuelling the Hearth", "Mead - filling a fermenter") and, when its job is stuck, why (General > ShowStatus).
- Status: a stuck job goes through its reasons in turn, with the numbers from your settings - "Wood 512/500 - at the limit.", "Copper deposit needs a better pickaxe.", "Raw meat 104/100 - enough.", "No bait for Pike (Fishing bait).", "No wood for the Charcoal kiln.", "Mead - Honey 0/30 for Frost resistance mead." (the recipe's 10 plus the 20 kept back), "No Turnip seeds to plant.", "No Resin for the fires."
- Speech: much less small talk - every 1.5-3 minutes instead of 15-30 seconds (Speech > Chatter: Off, Rare, Normal, Often; Speech > Enabled turns all speech off).
- Speech: small talk fits what it's doing and where it is - job lines only while working that job, fight lines only in a fight, home lines at home, travelling lines away from home, lines for the biome, night, rain and cold, nothing while it sleeps, sits, swims or sails.
- Speech: it says what it's off to do ("Fetching an axe.", "Putting a cultivator back.", "Off to bed.") and gives the real reason when it has nothing to do.
- Speech: new lines for things it used to give up on silently - no way to reach you, chests full, a tool it needs (any tool), no station to fix its gear, a lost spear, prey that got away, no hunting in the Passive stance.
- Speech: replies to your orders come at once; problems aren't lost to small talk and aren't repeated for 5 minutes; hungry, worn-gear, no-bed and heavy-pack remarks at most every 10 minutes, and "no bed" only at night at home.
- Speech: a problem a companion runs into while you're out of earshot shows top left with its name (Speech > FarAwayMessages).
- Messages: when one of your companions dies, the middle of the screen says so, with the biome and how far away (Speech > AnnounceDeath); when no companion takes your order (Command off, too far) a short message top left says so.
- speech.json has a new layout with tags per line ([home], [night], [rain], [blackforest]...). Your old file is kept next to it (speech.v1.json / speech.v2.json) and replaced by the new lines.
- Death (GitHub issue #17): a revived companion gets its gear before fighting again. Death > RespawnStance = RecoverFirst (default): it comes back Passive with Pickup off, goes for its tombstone when it's safe (waiting by you while enemies are after it, but finishing the trip once it's nearer its gear than you), fights back only when attacked with a weapon in hand, puts its gear on, eats, then goes back to its old stance. Passive: stays Passive. Keep: as before.
- Stance keys (issue #17): Controls > PassiveKey, BalancedKey, RangedKey, MeleeKey (unbound by default) - the companion you're looking at, or everyone in RecallRange with Command on.
- Death (issue #16): a companion that comes back by itself says where ("Saber is back - at its bed (Meadows, 850 m from you)"), and the roster and Odin's Shrine show where each living companion is. Death > RespawnAtPlayer: it always comes back next to you.
- Fixed: a companion's farm zones were lost when it died and came back, or was recovered. They now come back with it.
- Farming: ground you've tilled with a cultivator counts as an "Any" zone - it plants the PlantAny crops there, never inside a zone you set (Farming > PlantTilledGround).
- Farming: no more planting under eaves or against walls (where crops never grow), and no more trying to reach tilled ground it can't walk to (Farming > UnreachableSpotMemory).
- Farming without zones no longer keeps announcing "no farm zones"; "All done farming" at most every 10 minutes.
- Fixed: companions walked through the edge of the hearth's flames and stepped up onto it - which damages the hearth, as it does when a player stands in it. Routes, idle spots and follow spots now keep clear of fires.
- Water: in shallow water (the swamp) companions wade instead of hopping in and out; they jump only when the jump lands them on dry land, or to save a swim.
- Fixed: shooting at a bird, a companion often hit the hillside - it now aims at the bird and only shoots with a clear line to it.
- Following indoors: no more stuttering out of the door and back; a companion that sat down stays seated while you move about within 10 m (Follow > SitStayRange).
- Fishing: the log names the stamina food it takes along.
- Fixed: key settings in the F8 panel can be set again (a click starts "Press a key...", Esc cancels).

## 1.5.4
- Hunting: with a spear to throw at a deer (and another weapon in its pack), a companion sneaks only to throwing distance, stops and throws, instead of walking right up and stabbing it. New setting Hunting > ThrowStandoff (10 m; it throws from anywhere within ThrowRange, 12 m).
- Hunting: a companion with a spear as its only weapon throws it too once a deer (or other timid game) has noticed it, then lets the animal go and fetches the spear before hunting on.
- Hunting: a companion waits for the stamina a throw needs instead of closing in to stab, and the log says why it isn't throwing yet.
- Hunting: sneaking costs stamina like a player's, so a companion sneaking up on prey stops crouched to get its breath back when low (Hunting > SneakRestStamina 30%, carries on at SneakResumeStamina 60%).
- Hunting: a companion walks quietly (no running) up to prey that hasn't noticed it and only crouches within 20 m of it (Hunting > SneakFrom), like a player, instead of sneaking all the way.
- Hunting: if a much closer animal turns up before a companion has struck at its prey (closer by at least 5 m and under 60% of the distance - Hunting > SwitchCloserBy and SwitchShare), it goes for that one instead.
- Sneaking costs less stamina as a companion's Sneak skill goes up, the same way it does for a player (up to 75% less).
- Fixed: a companion that took a while sneaking up on prey (resting on the way) gave up on it after a minute without attacking, ignored that animal for a minute and went after one further away. Hunting now allows 3 minutes, and the log says when and why a target is given up.
- Hunting: while sneaking a companion wades shallow water and walks round instead of jumping, which stood it up, made noise and spent stamina (it only hops a step it's really stuck on).
- Hunting: chasing prey that bolted, a companion only runs while it has stamina to spare and jogs otherwise (Hunting > ChaseRunStamina 30%), so it doesn't reach the prey with an empty stamina bar.
- Fixed: ordering a companion to attack a deer (or other harmless game) did nothing - it dropped it straight away as "not a threat". The order now starts a hunt: it sneaks up and throws a spear like on the Hunt job.
- Hunting: with Follow on, a companion looks for prey up to 40 m from you (was 25 m - Hunting > FollowRange), closer to what you can see yourself.
- Melee: companions swing once they are a little inside their weapon's reach (Advanced > MeleeReachMargin, 0.2 m), not right at its edge where hits are hit-or-miss (user test: a stab at an unaware deer from the very edge of the spear's reach missed).
- Melee: companions aim a swing or stab at the middle of the target instead of its top (it went over a low animal's back), step back before swinging when standing right on top of it (Advanced > MeleeTooClose 0.3 m), and only stab once they face the target squarely.
- Log (debug): every melee swing, shot and spear throw is logged with distance, height and facing, followed by whether it hit (and for how much) or missed - to track down missed attacks.
- Fixed: after switching the job (e.g. turning Hunt off) a companion kept walking back to a chest for a tool the new job doesn't use, even after you called it back.
- Tools: take a tool out of a companion's pack (e.g. its bow) and it works without it for this job instead of going home for another one. Picking the job again lets it fetch tools again.

## 1.5.3
- Ladders: a tower of short ladders with landings between is now planned as one climb - the companion works out the whole chain at the start and goes from each ladder's top straight to the next one's foot without stopping. Chains that worked are remembered for a while, so after a fall (or the next time) it gets straight back on.
- Fixed: after loading in, a companion with Follow off followed you anyway (the return-to-you step on load switched follow on and never switched it back properly). It now leaves companions with Follow off alone.
- Ladders: a companion walks into a ladder and lets it lift it, like a player, and only hops when that doesn't work; it finishes each ladder on the landing before turning for the next one (it used to turn early and walk off the side).
- Ladders: a companion lines up square in front of each ladder first (like a doorway), walks straight into it and is bumped up it every half second just like you; only when it's really stuck does it hop.
- Fixed: going up a tower of ladders, a companion could walk or hop off the open side of a landing (a 9 m fall). The usual edge check now applies on ladder landings, and it never hops towards an edge.
- Fixed: a companion with nothing to do walked off - back towards where it was first spawned or last brought back, or to its home point up to 60 m away - even right after you sent it somewhere. It now waits and idles where it is, and around home only when it's at home.
- Quieter log: a cornered, fleeing companion logs once every few seconds instead of every moment.
- Ladders: companions walk up ladders instead of running, and when low on stamina they get their breath back on a landing before the next ladder (never part way up).
- Fixed: a companion with a melee weapon swung at an enemy a floor above it (hitting the ladder in between) instead of climbing up to it. Melee range now counts height, and on its way up a ladder it finishes the climb before it fights.
- Fixed: at the top of a ladder a companion could carry on walking the ladder's way and out through an opening in a tower wall (a 10 m fall). It now stops at the ladder's top end or the edge of the landing and turns for the next ladder.
- Fixed: after falling off a chain of ladders a companion couldn't find the first ladder again ("no ladder found" right beside it).
- Fixed: following your path up a tower, a companion could jump up onto a wall top beside the path and fall off the other side.
- Ladders: going down a tower of ladders works the same way - planned once, then down each ladder from its top to its foot and straight on to the next, instead of stopping short of each bottom.
- Companions recognise ladders that are part of the world (a ruined tower's), not only ones you built, so they can climb to an enemy at the top.
- Quieter log: the weapon-choice lines during fights are now debug-only.
- Fixed: at the top of a ladder a companion leapt onto the floor next to it and could carry straight on over a small floor and off the far side. It now steps off onto a floor that is level and close, looks first the way the ladder faces, and only takes spots with real footing.
- Fixed: on a sloping ladder a companion walked on past its top end while making sure the climb was over, ending up off to the side of the next ladder. It now stops there, and checks for a drop only as far as the next ladder's foot.
- Ladders: at the top of one ladder a companion that still has further to climb goes straight for the next ladder instead of stepping off first, and walks round a small opening to reach it (only a drop of over 4 m stops it).
- Ladders: a companion walks all the way to the foot of a ladder before pushing into it, instead of pushing in from beside it and walking along its edge; a narrow opening on the way no longer makes it give the ladder up.
- Ladders: walking to the next ladder a companion keeps to the floor - if the straight line would cut across the open side of a narrow floor, it goes round the corner instead, and only gives the ladder up when there is no floor all the way.
- Ladders: floor and edge checks look at a foot-sized area, so the gaps between a stair-like ladder's rungs no longer read as an edge or "no floor".
- Ladders: a companion pressed against a wall beside a ladder stops trying to get closer after a moment and climbs on from where it is.
- Fixed: at the top of a ladder a companion already at your height could jump up onto a wall top and over the side (an 11 m fall). It only jumps up when you're higher, and never onto something with a harmful drop just past it.
- Ladders: smoother climbing up a chain of ladders - shorter pauses on each landing (it steps on to the next ladder sooner), and while it waits at the top it turns towards the next ladder.
- Fixed: a companion only looked for the next ladder when its target was more than 2 m up, so with an enemy exactly one floor up it stepped off instead and could fall. Now from 1.5 m.
- Fixed: after a fall a companion wouldn't use the ladders it had just climbed again ("no ladder found" right beside them).
- Follow: a companion standing right beside the path you took steps onto it there, instead of walking round to where you last were on its level (which could mean going round a hole).

## 1.5.2
- Fixed: hunting with a melee weapon (spear, axe, knife - no bow ready) - the companion stopped 20 m away and swung at the air. It now sneaks up and strikes.
- Hunters throw their spear at skittish game (deer, hares...) when they have another weapon in their pack, then go and pick it back up.
- New [Hunting] ThrowSpear (on) and ThrowRange (12 m).
- Fixed: a spear counted as a ranged weapon (its long AI range is the throw), so a companion stabbed at the air from far off. Melee weapons now close to their real reach.
- A companion's thrown spears, arrows and bolts lead a running target and aim a little high for the drop, like a player.
- Fixed: following you, a companion could jump a stream and straight back when your path changed; it now wades or goes round instead of jumping back within a few seconds.
- Fixed: a creature it can't reach in deep water (a crocodile watching from the river) made a companion stand still, starting and dropping the fight over and over. It now leaves it unless it's attacked.
- Fixed: arrows were picked as a "bow" when the companion had arrows but no bow.
- Fixed: a companion out hunting far from home gave up its bed because the bed wasn't loaded; it only looks for its bed when it's nearby.
- Companions climb wooden ladders (push into them and hop up, like a player) to follow you up a tower or reach something on a platform, and jump from the top onto a ledge (or up onto a wall top and on to the floor) when the ladder stops short.
- Ladders stacked on top of each other are climbed as one tall ladder, all the way to the top.
- Companions jump onto a ladder whose bottom is off the ground, or when walking into it doesn't lift them, like a player, and try another way after 4 jumps that don't get them on.
- Ladder choice: companions pick a ladder whose foot is on their level and that they can walk to, and whose top gets them closest to where they are going; they climb a chain of short ladders one after another and leave a ladder alone for a while if it keeps getting them nowhere.
- Ladders: a ladder foot close by is walked to straight, without stepping off the edge of a small floor; a companion that falls off on the way looks again from where it lands instead of jumping at the ladder from below.
- Companions stop at the edge of a drop that would hurt (over 3 m, not into deep water) instead of walking off it, whether following, fighting or working, and look for a ladder or a safe way down instead.
- Gap jumps over a drop that would hurt need room to spare (80% of the companion's jump), and a companion never steps off the top of a ladder over such a drop.
- Follow: when you go up or down somewhere the companion can't find its own way (a narrow tower of ladders), it follows the exact path you took, ladders and jumps included - except a drop that would cost it more than 30% of its health.
- Follow: a companion on another floor from you keeps working its way to you (ladders included) instead of counting as "close enough" and idling, and never stops in the middle of a climb.
- Companions no longer try to jump onto a ladder from under a floor: a ladder that starts from a floor above them is climbed from that floor once they get up there.
- Ladders: companions walk round to the foot of a ladder instead of into the wall beside it, and skip a ladder that didn't get them up for a few minutes.
- Companions take fall damage like a player (none up to 4 m; Feather Fall works for them too).
- Companions go back down the ladder they came up. Only with no other way down do they jump down a drop: up to 8 m and never one costing more than 30% of their health. New [Movement] StuckDrop.
- When stuck just below where it needs to be, a companion jumps up onto something it can stand on nearby, like a player; it no longer steps backwards off a high ledge when stuck.
- Companions can use the game's use-to-climb ladders to reach something up out of reach.

## 1.5.1
- Fixed: a companion that died while you were dead too (for example both drowning at sea) left no tombstone and never came back - it now leaves its tombstone and respawns as normal, wherever you respawn.
- Fixed: a companion whose respawn time came while its tombstone was far away could be respawned again and again (one every second) - it now comes back once.
- README and guide: on a dedicated server the server needs the mod too - a server without it deletes companions.
- New companion roster: your game saves each companion's gear and settings every minute and when you log out or quit, per world.
- New [General] ShowCompanionRoster: lists your companions as Alive, Dead, Lost or Unknown.
- New [General] RecoverLostCompanion: brings a Lost companion back next to you (an Unknown one asks you to confirm).
- A recovered companion's old copy is removed if it turns up later, and its old tombstone no longer respawns it.
- New [Advanced] RosterSaveInterval (60 s): how often the roster is saved.
- New Odin's Shrine (hammer, Misc, at a workbench: 10 stone, 5 wood, 3 greydwarf eyes): shows your companions and their status in the game's message window, and offers to bring back a lost one.
- Fixed: a companion looked for a free bed about every 30 minutes instead of every 30 seconds (at night it still looked every few seconds).
- Fixed: a companion kept saying it had no bed after it had found one, until the next night.
- A bed claimed by a companion that no longer exists is free again (single player and host).
- The log now says why a companion couldn't find a bed to claim.
- Fixed: a companion could lose its bed when you loaded in, if the bed hadn't appeared yet - it now waits up to a minute for it.
- A companion can use a bed of its owner's that isn't the owner's spawn point, so clicking its bed by accident no longer takes it away. Once you make it your spawn point, the companion gives it up straight away.
- New [Death] ShrineBuildable, ShrineRecipe and ShrineModel (Tombstone or Ward): turn the shrine off, change its cost or its look.

## 1.5.0
- Major release: everything from 1.4.86 to 1.4.156 below (new movement, idle behaviour, Repair and Restock, cooking, gathering like Hunt, orders first, multiplayer ownership and chest safety, cleaned-up settings), plus the new Companion Guide (GUIDE.md) and Settings Reference (CONFIGURATION.md).

## Older versions

The full history, including 1.4.0 – 1.4.156 and every detail of each version, is in [CHANGELOG-full.md](https://github.com/MaddhattrRocks/Offline-Companions-Reborn/blob/main/CHANGELOG-full.md) on GitHub.
