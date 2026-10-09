using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Companions;

internal static class ModConfig
{
	internal static readonly Dictionary<string, List<ConfigEntryBase>> Categories = new Dictionary<string, List<ConfigEntryBase>>();

	internal static readonly List<string> CategoryOrder = new List<string>();

	internal static ConfigEntry<int> CompanionPrice;

	internal static ConfigEntry<bool> SpawnStarterCompanion;

	internal static ConfigEntry<bool> ResetStarterFlag;

	internal static ConfigEntry<float> MaxCarryWeight;

	internal static ConfigEntry<float> BaseHealth;

	internal static ConfigEntry<float> BaseStamina;

	internal static ConfigEntry<int> MaxFoodSlots;

	internal static ConfigEntry<float> MaxLeashDistance;

	internal static ConfigEntry<float> FollowTeleportDistance;

	internal static ConfigEntry<bool> ShowMinimapHudPanel;

	internal static ConfigEntry<float> HealthRetreatPct;

	internal static ConfigEntry<float> FleeHealthPct;

	internal static ConfigEntry<float> WeaponSwitchMargin;

	internal static ConfigEntry<bool> UseTorchOpener;

	internal static ConfigEntry<float> TorchOpenerTimeout;

	internal static ConfigEntry<float> TorchOpenerCooldown;

	internal static ConfigEntry<float> TorchOpenerMinTargetHealth;

	internal static ConfigEntry<float> WaterStaminaFleeThreshold;

	internal static ConfigEntry<bool> ShowOverheadBars;

	internal static ConfigEntry<int> ShieldMultiAttackerCount;

	internal static ConfigEntry<float> StaminaRetreatPct;

	internal static ConfigEntry<float> HealthRecoverPct;

	internal static ConfigEntry<float> StaminaRecoverPct;

	internal static ConfigEntry<float> AttackCooldown;

	internal static ConfigEntry<float> PowerAttackCooldown;

	internal static ConfigEntry<float> BowMaxRange;

	internal static ConfigEntry<float> BowMinRange;

	internal static ConfigEntry<float> BowSwitchDistance;

	internal static ConfigEntry<float> MeleeSwitchDistance;

	internal static ConfigEntry<float> RetreatDistance;

	internal static ConfigEntry<float> BowDrawTime;

	internal static ConfigEntry<float> BowFireInterval;

	internal static ConfigEntry<float> DodgeCooldown;

	internal static ConfigEntry<float> DodgeStaminaCost;

	internal static ConfigEntry<float> ThreatDetectRange;

	internal static ConfigEntry<float> ProjectileDetectRange;

	internal static ConfigEntry<float> BlockSafetyCap;

	internal static ConfigEntry<float> CounterWindowDuration;

	internal static ConfigEntry<float> ConsumeCooldown;

	internal static ConfigEntry<float> FormationOffset;

	internal static ConfigEntry<float> FormationCatchupDist;

	internal static ConfigEntry<float> GiveUpTime;

	internal static ConfigEntry<float> UpdateTargetIntervalNear;

	internal static ConfigEntry<float> UpdateTargetIntervalFar;

	internal static ConfigEntry<float> SelfDefenseRange;

	internal static ConfigEntry<float> AutoPickupRange;

	internal static ConfigEntry<float> TombstoneScanInterval;

	internal static ConfigEntry<float> TombstoneNavTimeout;

	internal static ConfigEntry<bool> AutoRespawnEnabled;

	internal static ConfigEntry<float> AutoRespawnDelaySeconds;

	internal static ConfigEntry<bool> AutoRespawnAtWorldSpawnWhenNoBed;

	internal static ConfigEntry<int> DeathItemRule;

	internal static ConfigEntry<float> TombstoneRecoveryRange;

	internal static ConfigEntry<bool> SleepAtNight;

	internal static ConfigEntry<bool> SleepWhenPlayerSleeps;

	internal static ConfigEntry<float> BedRange;

	internal static ConfigEntry<float> RecallRange;

	internal static ConfigEntry<float> SuperRecallHoldTime;

	internal static ConfigEntry<bool> ProtectCompanions;

	internal static ConfigEntry<int> SeedReserve;

	internal static ConfigEntry<int> SeedTarget;

	internal static ConfigEntry<int> SeedCarry;

	internal static ConfigEntry<float> WorkRunDistance;

	internal static ConfigEntry<float> HomeTripRange;

	internal static ConfigEntry<bool> FollowStuckTeleport;

	internal static ConfigEntry<bool> CompanionNavMesh;

	internal static ConfigEntry<float> CookRange;

	internal static ConfigEntry<int> CookIngredientReserve;

	internal static ConfigEntry<int> CookFoodTarget;

	internal static ConfigEntry<int> CookBatchSize;

	internal static ConfigEntry<string> CookFoods;

	internal static ConfigEntry<bool> CookStoreWithCookedFood;

	internal static ConfigEntry<int> CookMeadBaseTarget;

	internal static ConfigEntry<int> CookMeadTarget;

	internal static ConfigEntry<string> CookMeadFocus;

	internal static ConfigEntry<bool> StatusEffectsOnRegen;

	internal static ConfigEntry<float> MaxDrop;

	internal static ConfigEntry<float> JumpGap;

	internal static ConfigEntry<float> JumpWaterDepth;

	internal static ConfigEntry<float> JumpWaterWidth;

	internal static ConfigEntry<string> HuntPrey;

	internal static ConfigEntry<bool> HuntSneak;

	internal static ConfigEntry<int> HuntMeatTarget;

	internal static ConfigEntry<float> HuntRange;

	internal static ConfigEntry<float> HuntFollowRange;

	internal static ConfigEntry<float> HuntChaseExtra;

	internal static ConfigEntry<float> HuntBagFull;

	internal static ConfigEntry<bool> LootKills;

	internal static ConfigEntry<float> LootRadius;

	internal static ConfigEntry<float> LootWait;

	internal static ConfigEntry<float> LootMaxDistance;

	internal static ConfigEntry<float> FollowMaxDistance;

	internal static ConfigEntry<float> FollowIndoorDistance;

	internal static ConfigEntry<bool> FollowRegroup;

	internal static ConfigEntry<float> FollowRegroupTime;

	internal static ConfigEntry<bool> FollowSitIndoors;

	internal static ConfigEntry<int> PlantGroupSize;

	internal static ConfigEntry<float> FarmNextCropRange;

	internal static ConfigEntry<string> PlantAny;

	internal static ConfigEntry<int> WoodLimit;

	internal static ConfigEntry<int> StoneLimit;

	internal static ConfigEntry<int> OreLimit;

	internal static ConfigEntry<string> GatherWood;

	internal static ConfigEntry<string> GatherStone;

	internal static ConfigEntry<string> GatherOre;

	internal static ConfigEntry<string> WoodSkipTrees;

	internal static ConfigEntry<float> BaseProtectRadius;

	internal static ConfigEntry<bool> JunkCleanup;

	internal static ConfigEntry<string> NeverDiscard;

	internal static ConfigEntry<float> WanderMoveInterval;

	internal static ConfigEntry<float> WalkSpeed;

	internal static ConfigEntry<float> RunSpeed;

	internal static ConfigEntry<float> SwimSpeed;

	internal static ConfigEntry<float> TurnSpeed;

	internal static ConfigEntry<float> ViewRange;

	internal static ConfigEntry<float> ViewAngle;

	internal static ConfigEntry<float> FoodRegenInterval;

	internal static ConfigEntry<float> MeadCooldown;

	internal static ConfigEntry<float> HealthMeadThreshold;

	internal static ConfigEntry<float> StaminaMeadThreshold;

	internal static ConfigEntry<float> ConsumeCheckInterval;

	internal static ConfigEntry<float> StaminaRegenRate;

	internal static ConfigEntry<float> StaminaRunDrain;

	internal static ConfigEntry<float> StaminaSneakDrain;

	internal static ConfigEntry<float> StaminaSwimDrain;

	internal static ConfigEntry<float> StaminaRegenDelay;

	internal static ConfigEntry<float> HarvestScanRadius;

	internal static ConfigEntry<float> HarvestScanInterval;

	internal static ConfigEntry<float> HarvestAttackInterval;

	internal static ConfigEntry<float> HarvestOverweightThreshold;

	internal static ConfigEntry<float> HarvestDropScanRadius;

	internal static ConfigEntry<float> HarvestBlacklistDuration;

	internal static ConfigEntry<string> ForageItems;

	internal static ConfigEntry<float> HomeZoneRadius;

	internal static ConfigEntry<float> SmeltScanRadius;

	internal static ConfigEntry<float> SmeltScanInterval;

	internal static ConfigEntry<int> SmeltMaxCarryOre;

	internal static ConfigEntry<int> SmeltMaxCarryFuel;

	internal static ConfigEntry<float> SmeltUseDistance;

	internal static ConfigEntry<float> FarmScanRadius;

	internal static ConfigEntry<float> FarmScanInterval;

	internal static ConfigEntry<float> FarmPlantSpacing;

	internal static ConfigEntry<float> FarmUseDistance;

	internal static ConfigEntry<float> FishScanRadius;

	internal static ConfigEntry<float> FishWaitTimeMin;

	internal static ConfigEntry<float> FishWaitTimeMax;

	internal static ConfigEntry<float> FishReelTimeMin;

	internal static ConfigEntry<float> FishReelTimeMax;

	internal static ConfigEntry<float> FishHookChance;

	internal static ConfigEntry<float> FishMissChance;

	internal static ConfigEntry<float> FishReelStaminaDrain;

	internal static ConfigEntry<float> RepairDurabilityThreshold;

	internal static ConfigEntry<float> RepairScanRadius;

	internal static ConfigEntry<float> RepairScanInterval;

	internal static ConfigEntry<float> RepairTickInterval;

	internal static ConfigEntry<float> RepairUseDistance;

	internal static ConfigEntry<float> HomesteadScanRadius;

	internal static ConfigEntry<float> HomesteadScanInterval;

	internal static ConfigEntry<float> HomesteadFuelThreshold;

	internal static ConfigEntry<float> HomesteadTaskSlotTime;

	internal static ConfigEntry<float> HomesteadSmeltSlotTime;

	internal static ConfigEntry<float> HomesteadScanBackoff;

	internal static ConfigEntry<float> HomesteadUseDistance;

	internal static ConfigEntry<float> RestHealRate;

	internal static ConfigEntry<float> RestPlayerRange;

	internal static ConfigEntry<float> RestFireRange;

	internal static ConfigEntry<float> RestWarmupTime;

	internal static ConfigEntry<float> RestDirectedTimeout;

	internal static ConfigEntry<float> SpeechMinInterval;

	internal static ConfigEntry<float> SpeechMaxInterval;

	internal static ConfigEntry<float> SpeechCullDistance;

	internal static ConfigEntry<float> SpeechDuration;

	internal static ConfigEntry<float> SpeechSayCooldown;

	internal static ConfigEntry<float> SkillDeathLossFactor;

	internal static ConfigEntry<KeyCode> DirectTargetKey;

	internal static ConfigEntry<KeyCode> RadialMenuKey;

	internal static ConfigEntry<KeyCode> ConfigPanelKey;

	internal static ConfigEntry<KeyCode> FarmZoneKey;

	internal static ConfigEntry<KeyCode> FarmZoneModifier;

	internal static ConfigEntry<float> DoorScanRadius;

	internal static ConfigEntry<float> DoorInteractDist;

	internal static ConfigEntry<float> DoorProactiveScanRadius;

	internal static ConfigEntry<float> DoorFollowDistMin;

	internal static ConfigEntry<float> DoorCloseDistance;

	internal static void Init(ConfigFile cfg)
	{
		CompanionPrice = Bind(cfg, "General", "CompanionPrice", 2000, "Gold cost to purchase a companion", new AcceptableValueRange<int>(0, 50000));
		SpawnStarterCompanion = Bind(cfg, "General", "SpawnStarterCompanion", defaultValue: true, "Spawn a free companion when entering a new world for the first time");
		ResetStarterFlag = Bind(cfg, "General", "ResetStarterFlag", defaultValue: false, "Set to true to clear this character's 'starter companion already offered' flag for the current world. The starter companion panel will show again on the next world load, and this setting turns itself back to false.");
		MaxCarryWeight = Bind(cfg, "General", "MaxCarryWeight", 300f, "Maximum carry weight for companions", new AcceptableValueRange<float>(50f, 2000f));
		BaseHealth = Bind(cfg, "General", "BaseHealth", 25f, "Base HP before food bonuses", new AcceptableValueRange<float>(1f, 500f));
		BaseStamina = Bind(cfg, "General", "BaseStamina", 50f, "Base stamina before food bonuses", new AcceptableValueRange<float>(1f, 500f));
		MaxFoodSlots = Bind(cfg, "General", "MaxFoodSlots", 3, "Number of food slots", new AcceptableValueRange<int>(1, 5));
		MaxLeashDistance = Bind(cfg, "General", "MaxLeashDistance", 50f, "StayHome teleport-home leash distance", new AcceptableValueRange<float>(10f, 200f));
		FollowTeleportDistance = Bind(cfg, "General", "FollowTeleportDistance", 50f, "Distance from player before companion warps", new AcceptableValueRange<float>(20f, 200f));
		ShowMinimapHudPanel = Bind(cfg, "General", "ShowMinimapHudPanel", defaultValue: true, "Show companion status panel below the minimap");
		HealthRetreatPct = Bind(cfg, "Combat", "HealthRetreatPct", 0.3f, "Retreat when HP drops below this %", new AcceptableValueRange<float>(0.05f, 0.9f));
		FleeHealthPct = Bind(cfg, "Combat", "FleeHealthPct", 0.15f, "Flee (run away from the enemy) when HP drops below this %. Separate from HealthRetreatPct (which just switches to a defensive shield).", new AcceptableValueRange<float>(0.05f, 0.6f));
		WeaponSwitchMargin = Bind(cfg, "Combat", "WeaponSwitchMargin", 0.1f, "A different weapon must score this much higher than the current one before the companion switches (0.10 = 10%)", new AcceptableValueRange<float>(0f, 0.5f));
		UseTorchOpener = Bind(cfg, "Combat", "UseTorchOpener", defaultValue: true, "Companions open with a torch against fire-weak enemies, then switch back to their best weapon once the enemy is burning");
		TorchOpenerTimeout = Bind(cfg, "Combat", "TorchOpenerTimeout", 4f, "Seconds to keep trying to ignite before giving up and switching back", new AcceptableValueRange<float>(1f, 10f));
		TorchOpenerCooldown = Bind(cfg, "Combat", "TorchOpenerCooldown", 10f, "Seconds before the same enemy can be opened on with the torch again", new AcceptableValueRange<float>(0f, 60f));
		TorchOpenerMinTargetHealth = Bind(cfg, "Combat", "TorchOpenerMinTargetHealth", 40f, "Skip the torch opener on enemies with less max health than this", new AcceptableValueRange<float>(0f, 500f));
		WaterStaminaFleeThreshold = Bind(cfg, "Combat", "WaterStaminaFleeThreshold", 0.25f, "When swimming and stamina drops below this fraction (0.25 = 25%), the companion heads for the nearest shore instead of continuing whatever it was doing", new AcceptableValueRange<float>(0f, 1f));
		ShowOverheadBars = Bind(cfg, "Overhead", "ShowOverheadBars", defaultValue: true, "Show the health/stamina bars floating above a companion's head");
		ShieldMultiAttackerCount = Bind(cfg, "Combat", "ShieldMultiAttackerCount", 3, "Switch to the highest block-power shield once this many enemies are attacking at once", new AcceptableValueRange<int>(1, 10));
		StaminaRetreatPct = Bind(cfg, "Combat", "StaminaRetreatPct", 0.15f, "Retreat when stamina drops below this %", new AcceptableValueRange<float>(0.05f, 0.9f));
		HealthRecoverPct = Bind(cfg, "Combat", "HealthRecoverPct", 0.5f, "Re-engage when HP recovers to this %", new AcceptableValueRange<float>(0.1f, 1f));
		StaminaRecoverPct = Bind(cfg, "Combat", "StaminaRecoverPct", 0.3f, "Re-engage when stamina recovers to this %", new AcceptableValueRange<float>(0.1f, 1f));
		AttackCooldown = Bind(cfg, "Combat", "AttackCooldown", 0.225f, "Seconds between melee attacks", new AcceptableValueRange<float>(0.1f, 2f));
		PowerAttackCooldown = Bind(cfg, "Combat", "PowerAttackCooldown", 3f, "Seconds between power attacks", new AcceptableValueRange<float>(0.5f, 10f));
		BowMaxRange = Bind(cfg, "Combat", "BowMaxRange", 30f, "Maximum bow engagement range", new AcceptableValueRange<float>(10f, 80f));
		BowMinRange = Bind(cfg, "Combat", "BowMinRange", 10f, "Always use melee below this distance", new AcceptableValueRange<float>(1f, 30f));
		BowSwitchDistance = Bind(cfg, "Combat", "BowSwitchDistance", 15f, "Switch TO bow when target exceeds this distance", new AcceptableValueRange<float>(5f, 60f));
		MeleeSwitchDistance = Bind(cfg, "Combat", "MeleeSwitchDistance", 12f, "Switch FROM bow when target is closer than this", new AcceptableValueRange<float>(3f, 40f));
		RetreatDistance = Bind(cfg, "Combat", "RetreatDistance", 12f, "How far to retreat from enemy", new AcceptableValueRange<float>(3f, 30f));
		BowDrawTime = Bind(cfg, "Combat", "BowDrawTime", 1.2f, "Seconds to fully draw bow", new AcceptableValueRange<float>(0.3f, 3f));
		BowFireInterval = Bind(cfg, "Combat", "BowFireInterval", 2.5f, "Seconds between bow shots", new AcceptableValueRange<float>(0.5f, 5f));
		DodgeCooldown = Bind(cfg, "Combat", "DodgeCooldown", 2.5f, "Seconds between dodges", new AcceptableValueRange<float>(0.5f, 10f));
		DodgeStaminaCost = Bind(cfg, "Combat", "DodgeStaminaCost", 15f, "Stamina consumed per dodge", new AcceptableValueRange<float>(0f, 50f));
		ThreatDetectRange = Bind(cfg, "Combat", "ThreatDetectRange", 8f, "Range to detect enemy attacks for blocking", new AcceptableValueRange<float>(2f, 25f));
		ProjectileDetectRange = Bind(cfg, "Combat", "ProjectileDetectRange", 20f, "Range to detect incoming projectiles", new AcceptableValueRange<float>(5f, 50f));
		BlockSafetyCap = Bind(cfg, "Combat", "BlockSafetyCap", 3f, "Max continuous block time before forced counter", new AcceptableValueRange<float>(1f, 10f));
		CounterWindowDuration = Bind(cfg, "Combat", "CounterWindowDuration", 0.8f, "Post-parry attack window duration", new AcceptableValueRange<float>(0.2f, 3f));
		ConsumeCooldown = Bind(cfg, "Combat", "ConsumeCooldown", 10f, "Seconds between food consumption in combat", new AcceptableValueRange<float>(1f, 60f));
		FormationOffset = Bind(cfg, "AI", "FormationOffset", 3f, "Lateral spacing between companions in formation", new AcceptableValueRange<float>(1f, 10f));
		FormationCatchupDist = Bind(cfg, "AI", "FormationCatchupDist", 15f, "Distance to sprint to catch up with formation", new AcceptableValueRange<float>(5f, 50f));
		GiveUpTime = Bind(cfg, "AI", "GiveUpTime", 30f, "Seconds before abandoning a target", new AcceptableValueRange<float>(5f, 120f));
		UpdateTargetIntervalNear = Bind(cfg, "AI", "UpdateTargetIntervalNear", 2f, "Target scan interval when player is near", new AcceptableValueRange<float>(0.5f, 10f));
		UpdateTargetIntervalFar = Bind(cfg, "AI", "UpdateTargetIntervalFar", 6f, "Target scan interval when player is far", new AcceptableValueRange<float>(1f, 30f));
		SelfDefenseRange = Bind(cfg, "AI", "SelfDefenseRange", 10f, "Self-defense targeting range in gather mode", new AcceptableValueRange<float>(3f, 30f));
		AutoPickupRange = Bind(cfg, "AI", "AutoPickupRange", 2f, "Item auto-pickup radius", new AcceptableValueRange<float>(0.5f, 10f));
		TombstoneScanInterval = Bind(cfg, "AI", "TombstoneScanInterval", 5f, "Seconds between tombstone scans", new AcceptableValueRange<float>(1f, 30f));
		TombstoneNavTimeout = Bind(cfg, "AI", "TombstoneNavTimeout", 120f, "Max tombstone recovery navigation time", new AcceptableValueRange<float>(10f, 600f));
		WanderMoveInterval = Bind(cfg, "AI", "WanderMoveInterval", 5f, "Seconds between random wander movements at home", new AcceptableValueRange<float>(1f, 30f));
		WalkSpeed = Bind(cfg, "Movement", "WalkSpeed", 2f, "Walking speed (requires world reload)", new AcceptableValueRange<float>(0.5f, 5f));
		RunSpeed = Bind(cfg, "Movement", "RunSpeed", 7f, "Running speed (requires world reload)", new AcceptableValueRange<float>(2f, 15f));
		SwimSpeed = Bind(cfg, "Movement", "SwimSpeed", 2f, "Swimming speed (requires world reload)", new AcceptableValueRange<float>(0.5f, 5f));
		TurnSpeed = Bind(cfg, "Movement", "TurnSpeed", 300f, "Turn speed (requires world reload)", new AcceptableValueRange<float>(50f, 600f));
		ViewRange = Bind(cfg, "Movement", "ViewRange", 40f, "Visual detection range (requires world reload)", new AcceptableValueRange<float>(5f, 100f));
		ViewAngle = Bind(cfg, "Movement", "ViewAngle", 90f, "Field of view angle (requires world reload)", new AcceptableValueRange<float>(30f, 360f));
		FoodRegenInterval = Bind(cfg, "Food", "FoodRegenInterval", 10f, "Seconds between food regen ticks", new AcceptableValueRange<float>(1f, 60f));
		MeadCooldown = Bind(cfg, "Food", "MeadCooldown", 10f, "Mead consumption cooldown", new AcceptableValueRange<float>(1f, 60f));
		HealthMeadThreshold = Bind(cfg, "Food", "HealthMeadThreshold", 0.5f, "Use health mead below this HP %", new AcceptableValueRange<float>(0.1f, 0.9f));
		StaminaMeadThreshold = Bind(cfg, "Food", "StaminaMeadThreshold", 0.25f, "Use stamina mead below this stamina %", new AcceptableValueRange<float>(0.05f, 0.9f));
		ConsumeCheckInterval = Bind(cfg, "Food", "ConsumeCheckInterval", 1f, "Auto-consume check interval", new AcceptableValueRange<float>(0.5f, 10f));
		StaminaRegenRate = Bind(cfg, "Stamina", "RegenRate", 6f, "Stamina per second while idle", new AcceptableValueRange<float>(1f, 30f));
		StaminaRunDrain = Bind(cfg, "Stamina", "RunDrainRate", 10f, "Stamina drain per second while running", new AcceptableValueRange<float>(1f, 30f));
		StaminaSneakDrain = Bind(cfg, "Stamina", "SneakDrainRate", 5f, "Stamina drain per second while sneaking", new AcceptableValueRange<float>(1f, 30f));
		StaminaSwimDrain = Bind(cfg, "Stamina", "SwimDrainRate", 10f, "Stamina drain per second while swimming", new AcceptableValueRange<float>(1f, 30f));
		StaminaRegenDelay = Bind(cfg, "Stamina", "RegenDelay", 1f, "Seconds after stamina use before regen starts", new AcceptableValueRange<float>(0.1f, 5f));
		HarvestScanRadius = Bind(cfg, "Harvest", "ScanRadius", 30f, "Resource scan radius", new AcceptableValueRange<float>(5f, 100f));
		HarvestScanInterval = Bind(cfg, "Harvest", "ScanInterval", 4f, "Seconds between resource scans", new AcceptableValueRange<float>(1f, 30f));
		HarvestAttackInterval = Bind(cfg, "Harvest", "AttackInterval", 2.5f, "Seconds between harvest swings", new AcceptableValueRange<float>(0.5f, 5f));
		HarvestOverweightThreshold = Bind(cfg, "Harvest", "OverweightThreshold", 298f, "Stop gathering at this carry weight", new AcceptableValueRange<float>(50f, 500f));
		HarvestDropScanRadius = Bind(cfg, "Harvest", "DropScanRadius", 8f, "Drop collection scan radius", new AcceptableValueRange<float>(2f, 20f));
		HarvestBlacklistDuration = Bind(cfg, "Harvest", "BlacklistDuration", 60f, "Seconds a failed target stays blacklisted", new AcceptableValueRange<float>(5f, 300f));
		ForageItems = Bind(cfg, "Harvest", "ForageItems", "Raspberry,Blueberries,Cloudberry,Mushroom,MushroomYellow,MushroomBlue,MushroomMagecap,MushroomJotunPuffs,MushroomSmokePuff,Thistle,Dandelion,Carrot,Turnip,Onion,Barley,Flax,RoyalJelly,Honey", "Comma-separated item prefab names to forage. Only crops, berries, mushrooms, and flowers by default. Use * for all pickables including rocks and sticks");
		if (ForageItems.Value.Trim() == "*")
		{
			ForageItems.Value = (string)ForageItems.DefaultValue;
		}
		HomeZoneRadius = Bind(cfg, "StayHome", "HomeZoneRadius", 50f, "Default gather/homestead zone radius in meters around home position", new AcceptableValueRange<float>(5f, 200f));
		SmeltScanRadius = Bind(cfg, "Smelting", "ScanRadius", 25f, "Smelter scan radius", new AcceptableValueRange<float>(5f, 100f));
		SmeltScanInterval = Bind(cfg, "Smelting", "ScanInterval", 3f, "Seconds between smelter scans", new AcceptableValueRange<float>(1f, 30f));
		SmeltMaxCarryOre = Bind(cfg, "Smelting", "MaxCarryOre", 20, "Maximum ore items per trip", new AcceptableValueRange<int>(1, 100));
		SmeltMaxCarryFuel = Bind(cfg, "Smelting", "MaxCarryFuel", 40, "Maximum fuel items per trip", new AcceptableValueRange<int>(1, 100));
		SmeltUseDistance = Bind(cfg, "Smelting", "UseDistance", 2.5f, "Smelter interaction range", new AcceptableValueRange<float>(1f, 5f));
		FarmScanRadius = Bind(cfg, "Farming", "ScanRadius", 30f, "Crop/soil scan radius", new AcceptableValueRange<float>(5f, 100f));
		FarmScanInterval = Bind(cfg, "Farming", "ScanInterval", 4f, "Seconds between farm scans", new AcceptableValueRange<float>(1f, 30f));
		FarmPlantSpacing = Bind(cfg, "Farming", "PlantSpacing", 0.75f, "Min spacing between crops (uses max of this and plant grow radius)", new AcceptableValueRange<float>(0.25f, 3f));
		FarmUseDistance = Bind(cfg, "Farming", "UseDistance", 2f, "Interaction range for crops and chests", new AcceptableValueRange<float>(1f, 5f));
		FishScanRadius = Bind(cfg, "Fishing", "ScanRadius", 30f, "Water scan radius", new AcceptableValueRange<float>(5f, 100f));
		FishWaitTimeMin = Bind(cfg, "Fishing", "WaitTimeMin", 15f, "Min seconds waiting for nibble", new AcceptableValueRange<float>(1f, 120f));
		FishWaitTimeMax = Bind(cfg, "Fishing", "WaitTimeMax", 20f, "Max seconds waiting for nibble", new AcceptableValueRange<float>(1f, 180f));
		FishReelTimeMin = Bind(cfg, "Fishing", "ReelTimeMin", 4f, "Min seconds to reel in", new AcceptableValueRange<float>(1f, 30f));
		FishReelTimeMax = Bind(cfg, "Fishing", "ReelTimeMax", 6f, "Max seconds to reel in", new AcceptableValueRange<float>(1f, 60f));
		FishHookChance = Bind(cfg, "Fishing", "HookChance", 0.85f, "Chance to hook after nibble", new AcceptableValueRange<float>(0.1f, 1f));
		FishMissChance = Bind(cfg, "Fishing", "MissChance", 0.1f, "Chance of no nibble per wait", new AcceptableValueRange<float>(0f, 0.9f));
		FishReelStaminaDrain = Bind(cfg, "Fishing", "ReelStaminaDrain", 3f, "Stamina drain per second while reeling", new AcceptableValueRange<float>(0f, 20f));
		RepairDurabilityThreshold = Bind(cfg, "Repair", "DurabilityThreshold", 0.5f, "Repair items below this durability %", new AcceptableValueRange<float>(0.1f, 0.9f));
		RepairScanRadius = Bind(cfg, "Repair", "ScanRadius", 20f, "Crafting station scan radius", new AcceptableValueRange<float>(5f, 60f));
		RepairScanInterval = Bind(cfg, "Repair", "ScanInterval", 5f, "Seconds between repair scans", new AcceptableValueRange<float>(1f, 30f));
		RepairTickInterval = Bind(cfg, "Repair", "RepairTickInterval", 0.8f, "Seconds between individual item repairs", new AcceptableValueRange<float>(0.1f, 5f));
		RepairUseDistance = Bind(cfg, "Repair", "UseDistance", 2.5f, "Crafting station interaction range", new AcceptableValueRange<float>(1f, 5f));
		HomesteadScanRadius = Bind(cfg, "Homestead", "ScanRadius", 40f, "Maintenance scan radius from home position", new AcceptableValueRange<float>(10f, 100f));
		HomesteadScanInterval = Bind(cfg, "Homestead", "ScanInterval", 5f, "Seconds between maintenance scans", new AcceptableValueRange<float>(1f, 30f));
		HomesteadFuelThreshold = Bind(cfg, "Homestead", "FuelThreshold", 0.5f, "Refuel fires below this fuel %", new AcceptableValueRange<float>(0.1f, 0.9f));
		HomesteadTaskSlotTime = Bind(cfg, "Homestead", "TaskSlotTime", 60f, "Seconds per task rotation (repair/refuel/sort)", new AcceptableValueRange<float>(10f, 300f));
		HomesteadSmeltSlotTime = Bind(cfg, "Homestead", "SmeltSlotTime", 60f, "Seconds for smelting turn in rotation", new AcceptableValueRange<float>(10f, 300f));
		HomesteadScanBackoff = Bind(cfg, "Homestead", "ScanBackoff", 30f, "Extended scan interval when nothing to do", new AcceptableValueRange<float>(5f, 120f));
		HomesteadUseDistance = Bind(cfg, "Homestead", "UseDistance", 2f, "Interaction range for fires/chests", new AcceptableValueRange<float>(1f, 5f));
		CookRange = Bind(cfg, "Cooking", "Range", 25f, "How far around itself (meters) a cooking companion looks for cooking stations, fermenters and chests.", new AcceptableValueRange<float>(5f, 60f));
		CookIngredientReserve = Bind(cfg, "Cooking", "IngredientReserve", 20, "A raw ingredient is only used while the chests in range (plus the companion's bag) hold more than this many, so there is always some left for you to cook something yourself.", new AcceptableValueRange<int>(0, 1000));
		CookFoodTarget = Bind(cfg, "Cooking", "FoodTarget", 20, "Cook a food until the chests in range hold this many of it.", new AcceptableValueRange<int>(1, 1000));
		CookBatchSize = Bind(cfg, "Cooking", "BatchSize", 5, "How many of a food (or mead base) to make in one go, like a player cooking a batch. Still stops at FoodTarget / MeadBaseTarget and never goes into the ingredient reserve. On a cooking station or oven it's also limited by the free slots.", new AcceptableValueRange<int>(1, 50));
		CookFoods = Bind(cfg, "Cooking", "Foods", "*", "What may be cooked. * = everything (mods included). Otherwise a comma-separated list of item names; -Name excludes one, e.g. \"*, -Eyescream\". The names to use are listed in OfflineCompanionsReborn.Cooking.txt in the config folder (written at startup).");
		CookStoreWithCookedFood = Bind(cfg, "Cooking", "StoreWithCookedFood", true, "Put cooked food in a chest that already holds that food; if there is none, one that holds other cooked food; otherwise the chest nearest the cooking area. Off = the nearest chest with room.");
		CookMeadBaseTarget = Bind(cfg, "Cooking", "MeadBaseTarget", 10, "Brew a mead base (one in MeadFocus) until the chests in range hold this many of it.", new AcceptableValueRange<int>(0, 1000));
		CookMeadTarget = Bind(cfg, "Cooking", "MeadTarget", 20, "Keep fermenters going with a mead base until the chests in range hold this many of the finished mead.", new AcceptableValueRange<int>(0, 1000));
		CookMeadFocus = Bind(cfg, "Cooking", "MeadFocus", "start: MeadBaseHealthMinor, MeadBaseStaminaMinor; eikthyr: MeadBaseHealthMinor, MeadBaseStaminaMinor, MeadBasePoisonResist; gdking: MeadBasePoisonResist, MeadBaseHealthMedium, MeadBaseStaminaMinor; bonemass: MeadBaseFrostResist, MeadBaseHealthMedium, MeadBaseStaminaMedium; dragon: MeadBaseHealthMedium, MeadBaseStaminaMedium, MeadBaseTasty, BarleyWineBase; goblinking: MeadBaseHealthMajor, MeadBaseStaminaMedium, MeadBaseEitrMinor, MeadBaseLightfoot; queen: BarleyWineBase, MeadBaseHealthMajor, MeadBaseStaminaLingering, MeadBaseHealthLingering, MeadBaseEitrMinor; fader: MeadBaseFrostResist, MeadBaseHealthMajor, MeadBaseHealthLingering, MeadBaseStaminaLingering, MeadBaseEitrMinor; kall: MeadBaseFrostResist, MeadBaseHealthMajor, MeadBaseHealthLingering, MeadBaseStaminaLingering, MeadBaseEitrMinor", "Mead bases to brew FIRST, by the last boss beaten, to get ready for the next one (other mead bases you know are still brewed after food, within MeadBaseTarget / MeadTarget). Format: boss: Base, Base; boss: ... in the order you beat them. A boss is its name or part of it (eikthyr, gdking = The Elder, bonemass, dragon = Moder, goblinking = Yagluth, queen, fader, kall = Kall Fimbulbringer, or a modded boss); start = none beaten yet. The bosses your game has, and the mead base names, are listed in OfflineCompanionsReborn.Cooking.txt in the config folder.");
		StatusEffectsOnRegen = Bind(cfg, "General", "StatusEffectsOnRegen", true, "Status effects change a companion's health and stamina regeneration like they do for a player: Wet and Cold slow it, meads and other buffs change it. Off = companions regenerate at their normal rate whatever the effects.");
		HuntPrey = Bind(cfg, "Hunting", "Prey", "*", "What the Hunt job goes after: creatures that drop meat. * = all of them (mods included). Otherwise a comma-separated list of creature names; -Name excludes one, e.g. \"*, -Lox, -Serpent\". Tamed animals, bosses and companions are never hunted. The names to use are listed in OfflineCompanionsReborn.Hunting.txt in the config folder (written at startup).");
		HuntMeatTarget = Bind(cfg, "Hunting", "MeatTarget", 100, "Hunt a creature only while one of the meats it drops is under this many (counting the companion's pack and the chests near its home). Leather, hides and other drops are picked up as a bonus. 0 = no limit.", new AcceptableValueRange<int>(0, 10000));
		HuntRange = Bind(cfg, "Hunting", "Range", 40f, "With Follow off: how far (meters) from where it is hunting (home if it's near home, otherwise where the job was started) a companion looks for prey.", new AcceptableValueRange<float>(10f, 200f));
		HuntFollowRange = Bind(cfg, "Hunting", "FollowRange", 25f, "With Follow on: prey has to be within this many meters of you. With nothing that close the companion just follows you.", new AcceptableValueRange<float>(5f, 100f));
		HuntChaseExtra = Bind(cfg, "Hunting", "ChaseExtra", 15f, "How much further than Range / FollowRange a companion chases prey that runs off before letting it go.", new AcceptableValueRange<float>(0f, 100f));
		HuntBagFull = Bind(cfg, "Hunting", "BagFull", 0.9f, "With Follow off and home set: when the pack is this full (by weight, or out of free slots) the hunter walks home and puts its catch away, then goes back out.", new AcceptableValueRange<float>(0.3f, 1f));
		HuntSneak = Bind(cfg, "Hunting", "Sneak", true, "Sneak up on skittish game (deer, hares...) that hasn't noticed anyone yet, like a player: crouched and not running, so it doesn't bolt. Crouching companions are quieter and harder to spot, like a sneaking player (light and Sneak skill count).");
		MaxDrop = Bind(cfg, "General", "MaxDrop", 3f, "When a straight line is much shorter than the way round (down the stairs and back, round a wall), companions take it like a player: hopping steps, dropping off ledges up to this many meters (players take no fall damage below 4 m) and jumping small gaps (JumpGap).", new AcceptableValueRange<float>(1f, 4f));
		JumpGap = Bind(cfg, "General", "JumpGap", 1.5f, "The widest gap (meters) a companion leaps across on such a shortcut. 0.5 = no gaps.", new AcceptableValueRange<float>(0.5f, 3f));
		JumpWaterDepth = Bind(cfg, "General", "JumpWaterDepth", 0.3f, "Water at least this deep (meters) in a companion's way is jumped over when it's narrow enough (JumpWaterWidth), like a player jumps a small stream instead of running through it. Wider shallow water is waded through.", new AcceptableValueRange<float>(0.05f, 2f));
		JumpWaterWidth = Bind(cfg, "General", "JumpWaterWidth", 3f, "The widest stretch of water (meters) a companion jumps over.", new AcceptableValueRange<float>(1f, 4f));
		LootKills = Bind(cfg, "Looting", "PickUpKills", true, "After a companion kills something it goes over and picks up what the creature dropped (things you'd auto-pickup yourself), like a player would. Needs Auto Pickup on in the wheel.");
		LootRadius = Bind(cfg, "Looting", "Radius", 6f, "How far around the kill (meters) the drops are looked for.", new AcceptableValueRange<float>(2f, 20f));
		LootWait = Bind(cfg, "Looting", "WaitSeconds", 10f, "Longest wait (seconds) by the body for its drops to come out.", new AcceptableValueRange<float>(2f, 60f));
		LootMaxDistance = Bind(cfg, "Looting", "MaxDistance", 30f, "Kills further away than this (meters), or that you've moved this far away from while it follows you, are left.", new AcceptableValueRange<float>(5f, 100f));
		// 1.4.92: a config file still holding the 1.4.87 draft gets the reviewed table
		if (CookMeadFocus.Value == "start: MeadBaseHealthMinor, MeadBaseStaminaMinor; eikthyr: MeadBaseHealthMinor, MeadBaseStaminaMinor, MeadBasePoisonResist; gdking: MeadBaseHealthMinor, MeadBaseStaminaMinor, MeadBasePoisonResist; bonemass: MeadBaseHealthMedium, MeadBaseStaminaMedium, MeadBaseFrostResist; dragon: MeadBaseHealthMedium, MeadBaseStaminaMedium, MeadBaseFrostResist, BarleyWineBase; goblinking: MeadBaseHealthMajor, MeadBaseStaminaMedium, MeadBaseEitrMinor, BarleyWineBase; queen: MeadBaseHealthMajor, MeadBaseStaminaLingering, MeadBaseEitrMinor, BarleyWineBase; fader: MeadBaseHealthMajor, MeadBaseHealthLingering, MeadBaseStaminaLingering, MeadBaseEitrMinor, BarleyWineBase")
		{
			CookMeadFocus.Value = "start: MeadBaseHealthMinor, MeadBaseStaminaMinor; eikthyr: MeadBaseHealthMinor, MeadBaseStaminaMinor, MeadBasePoisonResist; gdking: MeadBasePoisonResist, MeadBaseHealthMedium, MeadBaseStaminaMinor; bonemass: MeadBaseFrostResist, MeadBaseHealthMedium, MeadBaseStaminaMedium; dragon: MeadBaseHealthMedium, MeadBaseStaminaMedium, MeadBaseTasty, BarleyWineBase; goblinking: MeadBaseHealthMajor, MeadBaseStaminaMedium, MeadBaseEitrMinor, MeadBaseLightfoot; queen: BarleyWineBase, MeadBaseHealthMajor, MeadBaseStaminaLingering, MeadBaseHealthLingering, MeadBaseEitrMinor; fader: MeadBaseFrostResist, MeadBaseHealthMajor, MeadBaseHealthLingering, MeadBaseStaminaLingering, MeadBaseEitrMinor; kall: MeadBaseFrostResist, MeadBaseHealthMajor, MeadBaseHealthLingering, MeadBaseStaminaLingering, MeadBaseEitrMinor";
		}
		RestHealRate = Bind(cfg, "Rest", "HealRate", 2f, "HP per second while resting", new AcceptableValueRange<float>(0.5f, 20f));
		RestPlayerRange = Bind(cfg, "Rest", "PlayerRange", 5f, "Max player distance for auto-sit", new AcceptableValueRange<float>(2f, 20f));
		RestFireRange = Bind(cfg, "Rest", "FireRange", 5f, "Campfire detection radius", new AcceptableValueRange<float>(2f, 20f));
		RestWarmupTime = Bind(cfg, "Rest", "WarmupTime", 20f, "Seconds of sitting before Rested buff applies", new AcceptableValueRange<float>(5f, 120f));
		RestDirectedTimeout = Bind(cfg, "Rest", "DirectedTimeout", 300f, "Directed sit timeout (seconds)", new AcceptableValueRange<float>(30f, 1800f));
		SpeechMinInterval = Bind(cfg, "Speech", "MinInterval", 15f, "Minimum seconds between ambient chatter", new AcceptableValueRange<float>(5f, 120f));
		SpeechMaxInterval = Bind(cfg, "Speech", "MaxInterval", 30f, "Maximum seconds between ambient chatter", new AcceptableValueRange<float>(10f, 300f));
		SpeechCullDistance = Bind(cfg, "Speech", "CullDistance", 20f, "Max distance at which speech is visible", new AcceptableValueRange<float>(5f, 60f));
		SpeechDuration = Bind(cfg, "Speech", "Duration", 5f, "How long speech text stays visible", new AcceptableValueRange<float>(1f, 15f));
		SpeechSayCooldown = Bind(cfg, "Speech", "SayCooldown", 3f, "Minimum seconds between any companion speaking", new AcceptableValueRange<float>(1f, 30f));
		SkillDeathLossFactor = Bind(cfg, "Skills", "DeathLossFactor", 0.25f, "Fraction of skills lost on death (0 = none)", new AcceptableValueRange<float>(0f, 1f));
		DirectTargetKey = Bind(cfg, "Controls", "DirectTargetKey", KeyCode.Z, "Directed command / focus-fire key");
		RadialMenuKey = Bind(cfg, "Controls", "RadialMenuKey", KeyCode.E, "Radial command menu key (hold while hovering companion)");
		ConfigPanelKey = Bind(cfg, "Controls", "ConfigPanelKey", KeyCode.F8, "Open the in-game config panel");
		FarmZoneKey = Bind(cfg, "Controls", "FarmZoneKey", KeyCode.Z, "Farm zone placement key (press with modifier to open zone editor)");
		FarmZoneModifier = Bind(cfg, "Controls", "FarmZoneModifier", KeyCode.LeftAlt, "Modifier key for farm zone placement (hold this + FarmZoneKey)");
		DoorScanRadius = Bind(cfg, "Door", "ScanRadius", 5f, "Stuck-mode door scan radius", new AcceptableValueRange<float>(2f, 15f));
		DoorInteractDist = Bind(cfg, "Door", "InteractDist", 2f, "Door interaction range", new AcceptableValueRange<float>(1f, 5f));
		DoorProactiveScanRadius = Bind(cfg, "Door", "ProactiveScanRadius", 15f, "Proactive door scan radius", new AcceptableValueRange<float>(5f, 30f));
		DoorFollowDistMin = Bind(cfg, "Door", "FollowDistMin", 4f, "Min distance from target to engage door handler", new AcceptableValueRange<float>(2f, 15f));
		DoorCloseDistance = Bind(cfg, "Door", "CloseDistance", 3.5f, "Distance past door before closing it", new AcceptableValueRange<float>(1f, 10f));
		AutoRespawnEnabled = Bind(cfg, "Respawn", "AutoRespawnEnabled", defaultValue: true, "Automatically respawn a dead companion after AutoRespawnDelaySeconds if nobody manually resurrects them from their tombstone first");
		AutoRespawnDelaySeconds = Bind(cfg, "Respawn", "AutoRespawnDelaySeconds", 300f, "Seconds after death before a companion automatically respawns", new AcceptableValueRange<float>(30f, 3600f));
		AutoRespawnAtWorldSpawnWhenNoBed = Bind(cfg, "Respawn", "AutoRespawnAtWorldSpawnWhenNoBed", defaultValue: false, "When a companion auto-respawns and has no claimed bed, respawn it at the world spawn stones instead of next to its owner");
		DeathItemRule = Bind(cfg, "Respawn", "DeathItemRule", 0, "What a companion loses when it dies. 0 = Follow the world's death penalty setting, same as a player. 1 = Keep everything. 2 = Keep equipped gear, drop the rest into the tombstone. 3 = Drop everything into the tombstone. The companion's owner's setting is used even if another player's game handles the death.", new AcceptableValueRange<int>(0, 3));
		TombstoneRecoveryRange = Bind(cfg, "Respawn", "TombstoneRecoveryRange", 50f, "How close a companion needs to be to their own leftover tombstone before they'll walk over and loot it themselves", new AcceptableValueRange<float>(5f, 100f));
		SleepAtNight = Bind(cfg, "Sleep", "SleepAtNight", defaultValue: true, "At night, a companion within BedRange of its claimed bed stops what it's doing and goes to bed, including when it's working or on Homestead. It goes back to its job in the morning.");
		SleepWhenPlayerSleeps = Bind(cfg, "Sleep", "SleepWhenPlayerSleeps", defaultValue: true, "When you get into a bed, your companions within BedRange of their claimed beds go to bed too, even in the evening. The night skip waits up to 30 seconds for them.");
		BedRange = Bind(cfg, "Sleep", "BedRange", 50f, "How close (in meters) a companion has to be to its claimed bed for the sleep rules above to send it to bed", new AcceptableValueRange<float>(10f, 200f));
		RecallRange = Bind(cfg, "Controls", "RecallRange", 40f, "How close (in meters) a companion has to be to you to answer a recall (holding the command key) or a point-and-command order. Companions with Command switched off on the wheel ignore both. Set to 0 for no limit.", new AcceptableValueRange<float>(0f, 500f));
		SuperRecallHoldTime = Bind(cfg, "Controls", "SuperRecallHoldTime", 2f, "Keep holding the command key this many seconds to recall every companion at any distance, including ones with Command switched off", new AcceptableValueRange<float>(0.5f, 10f));
		ProtectCompanions = Bind(cfg, "General", "ProtectCompanions", defaultValue: true, "Stops other mods from deleting a companion outright (for example mods that clear creatures out of warded areas). Companions can still die, leave a tombstone and respawn as normal, and the Despawn button still works. When something is blocked, the log says what tried. Turn off only if another mod needs to remove companions.");
		SeedReserve = Bind(cfg, "Farm", "SeedMinimum", 20, "The fewest of each plantable item (carrot, turnip, onion seeds, barley, flax and so on) the farmer keeps, counting its pack and chests near its home. It never plants seeds if that would take them below this. Barley, flax and other crops that grow from themselves are still planted, and the farmer keeps this many in its pack instead of storing them.", new AcceptableValueRange<int>(0, 1000));
		SeedTarget = Bind(cfg, "Farm", "SeedTarget", 100, "While a seed is under this number, the farmer grows seed plants (planting carrots, turnips, onions...) alongside its regular crops to build the stock back up. Below SeedMinimum it grows only seed plants for that crop. At or above this number it grows only crops.", new AcceptableValueRange<int>(0, 5000));
		PlantGroupSize = Bind(cfg, "Farm", "PlantGroupSize", 4, "How many of one plant the farmer puts in before switching to the next type, so a mixed field ends up in small blocks instead of one crop. 1 = fully mixed.", new AcceptableValueRange<int>(1, 100));
		FarmNextCropRange = Bind(cfg, "Farm", "NextCropRange", 3f, "After picking a crop, the farmer goes straight to the next ripe crop within this many meters, like working down a row, instead of pausing and scanning the whole field. 0 = always pause and scan.", new AcceptableValueRange<float>(0f, 10f));
		PlantAny = Bind(cfg, "Farm", "PlantAny", "CarrotSeeds, TurnipSeeds, OnionSeeds, Barley, Flax, MushroomJotunPuffs, MushroomMagecap, VineberrySeeds", "What the farmer plants in farm zones set to Any crop. Comma-separated item names. Anything not on the list (tree seeds such as BirchSeeds, Acorn, BeechSeeds, PineCone, FirCone) is never planted in an Any zone. Items added by other mods work too, for example MushroomCommon or Raspberry with Plant Everything.");
		SeedCarry = Bind(cfg, "Farm", "SeedCarry", 40, "How many of each plantable item the farmer carries. When it opens a chest for seeds it takes up to this many of every type on the PlantAny list (and vegetables for seed plants while that seed is under SeedTarget), so it can fill the fields without walking back for each type. It keeps up to this many in its pack instead of storing them.", new AcceptableValueRange<int>(1, 200));
		WorkRunDistance = Bind(cfg, "General", "WorkRunDistance", 25f, "Companions walk while doing jobs (farming, chest trips, smelting, cooking, chores, repairs, gathering) and only run when what they are heading to is further away than this many meters. 0 = never run for work. Following you, fighting and hunting are not affected.", new AcceptableValueRange<float>(0f, 500f));
		HomeTripRange = Bind(cfg, "General", "HomeTripRange", 60f, "How far from home (meters) a companion will still walk back for chest trips: fetching or putting back tools, repairing a broken tool, emptying its bags. Further away than this, a companion missing its job tool says so and follows you until you are both back near home.", new AcceptableValueRange<float>(10f, 500f));
		FollowStuckTeleport = Bind(cfg, "General", "FollowStuckTeleport", defaultValue: false, "When following you, a companion that has been stuck for 15 seconds somewhere it can't see you appears next to you. Off = it keeps trying to find its own way.");
		CompanionNavMesh = Bind(cfg, "General", "CompanionPathing", defaultValue: true, "Companions find their way with their own map of the world, where doors don't block the way (they open them as they get there) and steps up to 0.6 m are climbable (they hop them). Off = the same map creatures use, where every closed door is a wall and most stairs don't connect. Takes effect after a restart.");
		FollowMaxDistance = Bind(cfg, "Follow", "MaxDistance", 10f, "How far a following companion lets you get before it moves to keep up. It picks its own spot within this range (beside, behind, sometimes ahead) and changes it now and then.", new AcceptableValueRange<float>(4f, 30f));
		FollowIndoorDistance = Bind(cfg, "Follow", "IndoorDistance", 4f, "The same range while you're indoors (under a roof), so companions stay in the room with you.", new AcceptableValueRange<float>(2f, 10f));
		FollowRegroup = Bind(cfg, "Follow", "Regroup", defaultValue: true, "When you stand still for RegroupTime seconds, companions come back and wait close to you. Off = they stay wherever they stopped.");
		FollowRegroupTime = Bind(cfg, "Follow", "RegroupTime", 45f, "Seconds you stand still before companions regroup around you (see Regroup).", new AcceptableValueRange<float>(5f, 600f));
		FollowSitIndoors = Bind(cfg, "Follow", "SitIndoors", defaultValue: true, "When you stop indoors, companions sit down near you: on a free chair or bench if there is one, otherwise on the floor. Near home they empty their bags first.");
		WoodLimit = Bind(cfg, "Harvest", "WoodLimit", 500, "How many of each wood material (counting your companion's pack and chests near its home) the Wood job gathers before stopping. 0 = no limit. Per-item numbers in GatherWood override this.", new AcceptableValueRange<int>(0, 100000));
		StoneLimit = Bind(cfg, "Harvest", "StoneLimit", 500, "Same as WoodLimit, for the Stone job and GatherStone.", new AcceptableValueRange<int>(0, 100000));
		OreLimit = Bind(cfg, "Harvest", "OreLimit", 500, "Same as WoodLimit, for the Ore job and GatherOre.", new AcceptableValueRange<int>(0, 100000));
		GatherWood = Bind(cfg, "Harvest", "GatherWood", "Wood, FineWood, RoundLog, ElderBark, YggdrasilWood, Blackwood, Resin", "What the Wood job collects. Trees are only felled if they drop something on this list that is still under its limit. Comma-separated items. Name:Number sets that item's own limit, a name with no number uses the category limit, :0 means no limit. Items not on the list are not gathered and their drops are left on the ground.");
		GatherStone = Bind(cfg, "Harvest", "GatherStone", "Stone, Flint, BlackMarble, Grausten", "What the Stone job collects. Comma-separated items. Name:Number sets that item's own limit, a name with no number uses the category limit, :0 means no limit. Items not on the list are not gathered and their drops are left on the ground.");
		GatherOre = Bind(cfg, "Harvest", "GatherOre", "CopperOre, TinOre, IronScrap, SilverOre, BlackMetalScrap, CopperScrap, FlametalOreNew, Obsidian", "What the Ore job collects. Comma-separated items. Name:Number sets that item's own limit, a name with no number uses the category limit, :0 means no limit. Items not on the list are not gathered and their drops are left on the ground.");
		WoodSkipTrees = Bind(cfg, "Harvest", "WoodSkipTrees", "Oak", "Standing trees the Wood job never fells, matched on part of the tree name (Oak, Birch, Beech, Pine, Fir, SwampTree, YggaShoot...). Logs and stumps are still cleaned up.");
		BaseProtectRadius = Bind(cfg, "Harvest", "BaseProtectRadius", 10f, "The Wood job won't fell a standing tree with anything players built within this many meters of its trunk. 0 = off.", new AcceptableValueRange<float>(0f, 50f));
		JunkCleanup = Bind(cfg, "Harvest", "JunkCleanup", defaultValue: true, "When a gathering companion's pack is full and it has no home chest to unload into, it drops junk (materials and trophies that aren't on any gather list) to make room. Gear, tools, ammo, food, potions, seeds and anything on NeverDiscard are always kept.");
		NeverDiscard = Bind(cfg, "Harvest", "NeverDiscard", "Trophy", "Items the junk clean-up never drops, matched on part of the item name.");
	}

	private static ConfigEntry<T> Bind<T>(ConfigFile cfg, string category, string key, T defaultValue, string desc, AcceptableValueBase range = null)
	{
		ConfigEntry<T> configEntry = cfg.Bind(category, key, defaultValue, new ConfigDescription(desc, range));
		if (!Categories.TryGetValue(category, out var value))
		{
			value = new List<ConfigEntryBase>();
			Categories[category] = value;
			CategoryOrder.Add(category);
		}
		value.Add(configEntry);
		return configEntry;
	}
}
