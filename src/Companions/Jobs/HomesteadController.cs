using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions;

public class HomesteadController : MonoBehaviour
{
	internal enum HomePhase
	{
		Idle,
		Wandering,
		MovingToChair,
		Sitting,
		MovingToStation,
		UsingStation,
		MovingToRepairTarget,
		Repairing,
		MovingToSupplyChest,
		TakingFuel,
		MovingToFireplace,
		Refueling,
		MovingToSortChest,
		SortingChest,
		MovingToIngredientChest,
		TakingIngredients,
		MovingToCauldron,
		Cooking
	}

	private CompanionAI _ai;

	private Humanoid _humanoid;

	private Character _character;

	private CompanionSetup _setup;

	private ZNetView _nview;

	private ZSyncAnimation _zanim;

	private CompanionTalk _talk;

	private CompanionFood _food;

	private HarvestController _harvest;

	private RepairController _repair;

	private CompanionRest _rest;

	private DoorHandler _doorHandler;

	private HomePhase _phase;

	private float _idleTimer;

	private float _actionTimer;

	private float _stuckTimer;

	private float _stuckCheckTimer;

	private Vector3 _stuckCheckPos;

	private bool _chestOpened;

	private float _wanderCooldown;

	private float _sitCooldown;

	private float _stationCooldown;

	private float _repairCooldown;

	private float _refuelCooldown;

	private float _sortCooldown;

	private float _cookCooldown;

	private Vector3 _wanderTarget;

	private float _wanderTimeout;

	private float _wanderDuration;

	private Transform _chairAttachPoint;

	private float _sitDuration;

	private float _sitTimer;

	private bool _isSitting;

	private Rigidbody _body;

	private CraftingStation _targetStation;

	private float _stationDuration;

	private WearNTear _targetPiece;

	private ItemDrop.ItemData _prevRightItem;

	private bool _hammerEquipped;

	private bool _lastHammerMissing;

	private Fireplace _targetFireplace;

	private Container _supplyChest;

	private string _fuelItemPrefab;

	private int _fuelToAdd;

	private Container _sortChest;

	private CraftingStation _targetCauldron;

	private Recipe _selectedRecipe;

	private Container _ingredientChest;

	private readonly List<(string prefab, int needed)> _ingredientList = new List<(string, int)>();

	private int _ingredientIndex;

	private static readonly HashSet<int> s_claimedPieces = new HashSet<int>();

	private int _claimedPieceId;

	private readonly Dictionary<int, float> _blacklist = new Dictionary<int, float>();

	private readonly List<int> _expiredKeys = new List<int>();

	private const float BlacklistDuration = 120f;

	private readonly List<Piece> _tempPieces = new List<Piece>();

	private readonly List<Container> _nearbyChests = new List<Container>();

	private readonly Collider[] _scanBuffer = new Collider[256];

	private const float FuelAddDelay = 0.8f;

	private const float RepairDelay = 1.2f;

	private const float ChestOpenDelay = 0.8f;

	private const float MoveTimeout = 10f;

	private const float StuckCheckPeriod = 1f;

	private const float StuckMinDistance = 0.5f;

	private const float IdleDelayMin = 3f;

	private const float IdleDelayMax = 8f;

	private const float WeightCook = 100f;

	private const float WeightRepair = 20f;

	private const float WeightRefuel = 20f;

	private const float WeightSort = 10f;

	private const float WeightSit = 15f;

	private const float WeightStation = 10f;

	private const float WeightWander = 25f;

	internal HomePhase Phase => _phase;

	public bool IsActive => _phase != HomePhase.Idle;

	private static float ScanRadius => ModConfig.HomesteadScanRadius.Value;

	private static float UseDistance => ModConfig.HomesteadUseDistance.Value;

	private static float FuelThreshold => ModConfig.HomesteadFuelThreshold.Value;

	internal static ItemDrop.ItemData FindHammerInInventory(Inventory inv)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			if (allItems[i]?.m_shared?.m_buildPieces != null)
			{
				return allItems[i];
			}
		}
		return null;
	}

	internal static int CountItemInInventory(Inventory inv, string prefabName)
	{
		int num = 0;
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			string text = allItems[i].m_dropPrefab?.name ?? allItems[i].m_shared?.m_name;
			if (text == prefabName)
			{
				num += allItems[i].m_stack;
			}
		}
		return num;
	}

	internal static ItemDrop.ItemData FindItemByPrefab(Inventory inv, string prefabName)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			if (allItems[i].m_dropPrefab != null && allItems[i].m_dropPrefab.name == prefabName)
			{
				return allItems[i];
			}
		}
		return null;
	}

	internal static bool TransferOne(Inventory source, Inventory dest, ItemDrop.ItemData item)
	{
		ItemDrop.ItemData itemData = item.Clone();
		itemData.m_stack = 1;
		if (!dest.CanAddItem(itemData, 1))
		{
			return false;
		}
		if (!dest.AddItem(itemData))
		{
			return false;
		}
		source.RemoveItem(item, 1);
		return true;
	}

	internal static bool ConsumeOneFromInventory(Inventory inv, string prefabName)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			string text = allItems[i].m_dropPrefab?.name ?? allItems[i].m_shared?.m_name;
			if (text == prefabName)
			{
				inv.RemoveItem(allItems[i], 1);
				return true;
			}
		}
		return false;
	}

	public void CancelDirected()
	{
		if (_phase != HomePhase.Idle)
		{
			Abort("cancelled by command");
		}
	}

	private void Awake()
	{
		_ai = GetComponent<CompanionAI>();
		_humanoid = GetComponent<Humanoid>();
		_character = GetComponent<Character>();
		_setup = GetComponent<CompanionSetup>();
		_nview = GetComponent<ZNetView>();
		_zanim = GetComponent<ZSyncAnimation>();
		_talk = GetComponent<CompanionTalk>();
		_food = GetComponent<CompanionFood>();
		_harvest = GetComponent<HarvestController>();
		_repair = GetComponent<RepairController>();
		_rest = GetComponent<CompanionRest>();
		_doorHandler = GetComponent<DoorHandler>();
		_body = GetComponent<Rigidbody>();
		Log("Awake OK");
	}

	private void Log(string msg)
	{
		string text = _character?.m_name ?? "?";
		CompanionsPlugin.Log.LogInfo("[HomeLife|" + text + "] " + msg);
	}

	private void OnDestroy()
	{
		UnclaimPiece();
		CloseAnyChest();
		if (_isSitting)
		{
			StandUp();
		}
	}

	private void UnclaimPiece()
	{
		if (_claimedPieceId != 0)
		{
			s_claimedPieces.Remove(_claimedPieceId);
			_claimedPieceId = 0;
		}
	}

	private void CloseAnyChest()
	{
		if (!_chestOpened)
		{
			return;
		}
		switch (_phase)
		{
		case HomePhase.TakingFuel:
			if (_supplyChest != null)
			{
				_supplyChest.SetInUse(inUse: false);
			}
			break;
		case HomePhase.SortingChest:
			if (_sortChest != null)
			{
				_sortChest.SetInUse(inUse: false);
			}
			break;
		case HomePhase.TakingIngredients:
			if (_ingredientChest != null)
			{
				_ingredientChest.SetInUse(inUse: false);
			}
			break;
		}
		_chestOpened = false;
	}

	private void StandUp()
	{
		if (_isSitting)
		{
			_isSitting = false;
			if (_zanim != null)
			{
				_zanim.SetBool("attach_chair", value: false);
			}
			if (_body != null)
			{
				_body.useGravity = true;
			}
			base.transform.position += Vector3.up * 0.5f;
		}
	}

	private void Update()
	{
		if (_nview == null || _nview.GetZDO() == null || !_nview.IsOwner() || _character == null || _character.GetHealth() <= 0f)
		{
			return;
		}
		if (!IsHomesteadMode())
		{
			if (_phase != HomePhase.Idle)
			{
				Abort("left homestead mode");
			}
			return;
		}
		if (ShouldAbort())
		{
			if (_phase != HomePhase.Idle)
			{
				Abort("interrupted");
			}
			return;
		}
		float deltaTime = Time.deltaTime;
		_wanderCooldown = Mathf.Max(0f, _wanderCooldown - deltaTime);
		_sitCooldown = Mathf.Max(0f, _sitCooldown - deltaTime);
		_stationCooldown = Mathf.Max(0f, _stationCooldown - deltaTime);
		_repairCooldown = Mathf.Max(0f, _repairCooldown - deltaTime);
		_refuelCooldown = Mathf.Max(0f, _refuelCooldown - deltaTime);
		_sortCooldown = Mathf.Max(0f, _sortCooldown - deltaTime);
		_cookCooldown = Mathf.Max(0f, _cookCooldown - deltaTime);
		switch (_phase)
		{
		case HomePhase.Idle:
			UpdateIdle(deltaTime);
			break;
		case HomePhase.Wandering:
			UpdateWandering(deltaTime);
			break;
		case HomePhase.MovingToChair:
			UpdateMovingToChair(deltaTime);
			break;
		case HomePhase.Sitting:
			UpdateSitting(deltaTime);
			break;
		case HomePhase.MovingToStation:
			UpdateMovingToStation(deltaTime);
			break;
		case HomePhase.UsingStation:
			UpdateUsingStation(deltaTime);
			break;
		case HomePhase.MovingToRepairTarget:
			UpdateMovingToRepair(deltaTime);
			break;
		case HomePhase.Repairing:
			UpdateRepairing(deltaTime);
			break;
		case HomePhase.MovingToSupplyChest:
			UpdateMovingToChest(deltaTime);
			break;
		case HomePhase.TakingFuel:
			UpdateTakingFuel(deltaTime);
			break;
		case HomePhase.MovingToFireplace:
			UpdateMovingToFire(deltaTime);
			break;
		case HomePhase.Refueling:
			UpdateRefueling(deltaTime);
			break;
		case HomePhase.MovingToSortChest:
			UpdateMovingToSortChest(deltaTime);
			break;
		case HomePhase.SortingChest:
			UpdateSortingChest(deltaTime);
			break;
		case HomePhase.MovingToIngredientChest:
			UpdateMovingToIngredientChest(deltaTime);
			break;
		case HomePhase.TakingIngredients:
			UpdateTakingIngredients(deltaTime);
			break;
		case HomePhase.MovingToCauldron:
			UpdateMovingToCauldron(deltaTime);
			break;
		case HomePhase.Cooking:
			UpdateCooking(deltaTime);
			break;
		}
	}

	private bool IsHomesteadMode()
	{
		if (MHR_JobRules.HasJob(_setup))
		{
			return false;
		}
		if (_setup != null && _setup.GetStayHome() && _setup.HasHomePosition())
		{
			return !_setup.GetFollow();
		}
		return false;
	}

	private void Abort(string reason)
	{
		Log($"Aborted — {reason} (phase was {_phase})");
		if (_isSitting)
		{
			StandUp();
		}
		UnclaimPiece();
		CloseAnyChest();
		RestoreWeaponAfterRepair();
		_targetFireplace = null;
		_supplyChest = null;
		_fuelItemPrefab = null;
		_targetPiece = null;
		_sortChest = null;
		_targetStation = null;
		_targetCauldron = null;
		_selectedRecipe = null;
		_ingredientChest = null;
		_ingredientList.Clear();
		_phase = HomePhase.Idle;
		_idleTimer = UnityEngine.Random.Range(3f, 8f);
	}

	private bool ShouldAbort()
	{
		if (MHR_ToolKeeper.IsBusy(this))
		{
			return true;
		}
		if (_ai != null && _ai.IsInCombat)
		{
			return true;
		}
		if (_harvest != null && _harvest.IsActive)
		{
			return true;
		}
		if (_repair != null && _repair.IsActive)
		{
			return true;
		}
		if (_rest != null && (_rest.IsResting || _rest.IsNavigating))
		{
			return true;
		}
		if (CompanionInteractPanel.IsOpenFor(_setup) || CompanionRadialMenu.IsOpenFor(_setup))
		{
			return true;
		}
		return false;
	}

	private void UpdateIdle(float dt)
	{
		_idleTimer -= dt;
		if (!(_idleTimer > 0f))
		{
			PruneBlacklist();
			PickNextBehavior();
		}
	}

	private void UpdateWandering(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		_wanderTimeout -= dt;
		_wanderDuration -= dt;
		float num = Vector3.Distance(base.transform.position, _wanderTarget);
		if (_wanderDuration <= 0f)
		{
			_ai?.StopMoving();
			_wanderCooldown = UnityEngine.Random.Range(5f, 15f);
			_phase = HomePhase.Idle;
			_idleTimer = UnityEngine.Random.Range(3f, 8f);
		}
		else if (num < 2f || _wanderTimeout <= 0f)
		{
			CompanionSetup component = GetComponent<CompanionSetup>();
			if (component != null && component.HasHomePosition())
			{
				PickWanderPoint(component.GetHomePosition(), component.GetHomeRadius());
			}
			else
			{
				PickWanderPoint(base.transform.position, 10f);
			}
		}
		else
		{
			CompanionAI ai = _ai;
			if ((object)ai != null)
			{
				MHR_Nav.MoveWalk(ai, dt, _wanderTarget, 1.5f, run: false);
			}
		}
	}

	private void UpdateMovingToChair(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_chairAttachPoint == null)
		{
			Abort("chair destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _chairAttachPoint.position);
		if (num <= 2f)
		{
			_ai?.StopMoving();
			SitDown();
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _chairAttachPoint.position, 1.5f, num > 8f);
		}
		UpdateStuck(dt, "chair", num);
	}

	private void UpdateSitting(float dt)
	{
		if (_chairAttachPoint == null)
		{
			StandUp();
			Abort("chair destroyed while sitting");
			return;
		}
		base.transform.position = _chairAttachPoint.position;
		base.transform.rotation = _chairAttachPoint.rotation;
		if (_body != null)
		{
			_body.velocity = Vector3.zero;
			_body.angularVelocity = Vector3.zero;
		}
		if (_ai != null)
		{
			_ai.FreezeTimer = 0.5f;
		}
		_sitTimer -= dt;
		if (_sitTimer <= 0f)
		{
			StandUp();
			_sitCooldown = UnityEngine.Random.Range(60f, 180f);
			_phase = HomePhase.Idle;
			_idleTimer = UnityEngine.Random.Range(3f, 8f);
			Log("Stood up from chair");
		}
	}

	private void UpdateMovingToStation(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetStation == null)
		{
			Abort("station destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetStation.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_targetStation.transform.position);
			_actionTimer = _stationDuration;
			_phase = HomePhase.UsingStation;
			Log("Using station \"" + _targetStation.m_name + "\"");
		}
		else
		{
			CompanionAI ai = _ai;
			if ((object)ai != null)
			{
				MHR_Nav.MoveWalk(ai, dt, _targetStation.transform.position, UseDistance, num > 8f);
			}
			UpdateStuck(dt, "station", num);
		}
	}

	private void UpdateUsingStation(float dt)
	{
		if (_targetStation == null)
		{
			Abort("station destroyed while using");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(_targetStation.transform.position);
		_actionTimer -= dt;
		if (_actionTimer <= 0f)
		{
			_stationCooldown = UnityEngine.Random.Range(60f, 120f);
			_targetStation = null;
			_phase = HomePhase.Idle;
			_idleTimer = UnityEngine.Random.Range(3f, 8f);
			Log("Finished using station");
		}
	}

	private void UpdateMovingToRepair(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetPiece == null)
		{
			Abort("repair target destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetPiece.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_targetPiece.transform.position);
			_hammerEquipped = false;
			Inventory inventory = _humanoid?.GetInventory();
			if (inventory != null)
			{
				ItemDrop.ItemData itemData = FindHammerInInventory(inventory);
				if (itemData != null)
				{
					_prevRightItem = ReflectionHelper.GetRightItem(_humanoid);
					if (_prevRightItem != null && _prevRightItem != itemData)
					{
						_humanoid.UnequipItem(_prevRightItem, triggerEquipEffects: false);
					}
					_humanoid.EquipItem(itemData);
					_hammerEquipped = true;
				}
			}
			_actionTimer = 1.2f;
			_phase = HomePhase.Repairing;
		}
		else
		{
			CompanionAI ai = _ai;
			if ((object)ai != null)
			{
				MHR_Nav.MoveWalk(ai, dt, _targetPiece.transform.position, UseDistance, num > 8f);
			}
			UpdateStuck(dt, "repair", num);
		}
	}

	private void UpdateRepairing(float dt)
	{
		if (_targetPiece == null)
		{
			Abort("repair target destroyed");
			return;
		}
		float num = Vector3.Distance(base.transform.position, _targetPiece.transform.position);
		if (num > UseDistance * 2f)
		{
			Abort("drifted from repair");
			return;
		}
		_ai?.LookAtPoint(_targetPiece.transform.position);
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		_actionTimer = 1.2f;
		if (_zanim != null)
		{
			_zanim.SetTrigger("swing_pickaxe");
		}
		_targetPiece.Repair();
		Piece component = _targetPiece.GetComponent<Piece>();
		if (component?.m_placeEffect != null)
		{
			component.m_placeEffect.Create(component.transform.position, component.transform.rotation, null, 1f, -1, ZDOID.None);
		}
		if (_targetPiece.GetHealthPercentage() >= 1f)
		{
			if (_talk != null)
			{
				_talk.Say(ModLocalization.Loc("hc_speech_homestead_repaired"));
			}
			RestoreWeaponAfterRepair();
			UnclaimPiece();
			_targetPiece = null;
			_repairCooldown = 5f;
			_phase = HomePhase.Idle;
			_idleTimer = 2f;
		}
	}

	private void UpdateMovingToChest(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_supplyChest == null)
		{
			Abort("chest destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _supplyChest.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_supplyChest.transform.position);
			_chestOpened = false;
			_phase = HomePhase.TakingFuel;
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _supplyChest.transform.position, UseDistance, num > 8f);
		}
		UpdateStuck(dt, "supply chest", num);
	}

	private void UpdateTakingFuel(float dt)
	{
		if (_supplyChest == null)
		{
			CloseAnyChest();
			Abort("chest destroyed");
			return;
		}
		if (!_chestOpened)
		{
			_supplyChest.SetInUse(inUse: true);
			_chestOpened = true;
			_actionTimer = 0.8f;
			if (_zanim != null)
			{
				_zanim.SetTrigger("interact");
			}
			return;
		}
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		_actionTimer = 0.6f;
		Inventory inventory = _supplyChest.GetInventory();
		Inventory inventory2 = _humanoid?.GetInventory();
		if (inventory == null || inventory2 == null)
		{
			CloseChest(_supplyChest);
			Abort("inv null");
			return;
		}
		ItemDrop.ItemData itemData = FindItemByPrefab(inventory, _fuelItemPrefab);
		if (itemData == null || _fuelToAdd <= 0)
		{
			CloseChest(_supplyChest);
			int num = CountItemInInventory(inventory2, _fuelItemPrefab);
			if (num == 0)
			{
				Abort("no fuel taken");
				return;
			}
			_fuelToAdd = Mathf.Min(_fuelToAdd, num);
			ResetStuck();
			_phase = HomePhase.MovingToFireplace;
		}
		else if (TransferOne(inventory, inventory2, itemData))
		{
			_fuelToAdd--;
		}
		else
		{
			CloseChest(_supplyChest);
			int num2 = CountItemInInventory(inventory2, _fuelItemPrefab);
			if (num2 > 0)
			{
				_fuelToAdd = num2;
				ResetStuck();
				_phase = HomePhase.MovingToFireplace;
			}
			else
			{
				Abort("inv full");
			}
		}
	}

	private void UpdateMovingToFire(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetFireplace == null)
		{
			Abort("fire destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetFireplace.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_targetFireplace.transform.position);
			_actionTimer = 0.8f;
			_phase = HomePhase.Refueling;
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetFireplace.transform.position, UseDistance, num > 8f);
		}
		UpdateStuck(dt, "fire", num);
	}

	private void UpdateRefueling(float dt)
	{
		if (_targetFireplace == null)
		{
			Abort("fire destroyed");
			return;
		}
		float num = Vector3.Distance(base.transform.position, _targetFireplace.transform.position);
		if (num > UseDistance * 2f)
		{
			Abort("drifted from fire");
			return;
		}
		_ai?.LookAtPoint(_targetFireplace.transform.position);
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		_actionTimer = 0.8f;
		ZNetView component = _targetFireplace.GetComponent<ZNetView>();
		if (component?.GetZDO() == null)
		{
			Abort("fire nview lost");
			return;
		}
		float num2 = component.GetZDO().GetFloat(ZDOVars.s_fuel);
		if (num2 >= _targetFireplace.m_maxFuel)
		{
			FinishRefuel();
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null || !ConsumeOneFromInventory(inventory, _fuelItemPrefab))
		{
			FinishRefuel();
			return;
		}
		if (_zanim != null)
		{
			_zanim.SetTrigger("interact");
		}
		component.InvokeRPC("RPC_AddFuel");
		_fuelToAdd--;
		if (_fuelToAdd <= 0)
		{
			FinishRefuel();
		}
	}

	private void UpdateMovingToSortChest(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_sortChest == null)
		{
			Abort("sort chest destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _sortChest.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_sortChest.transform.position);
			_chestOpened = false;
			_phase = HomePhase.SortingChest;
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _sortChest.transform.position, UseDistance, num > 8f);
		}
		UpdateStuck(dt, "sort chest", num);
	}

	private void UpdateSortingChest(float dt)
	{
		if (_sortChest == null)
		{
			CloseAnyChest();
			Abort("sort chest destroyed");
			return;
		}
		if (!_chestOpened)
		{
			_sortChest.SetInUse(inUse: true);
			_chestOpened = true;
			_actionTimer = 0.8f;
			if (_zanim != null)
			{
				_zanim.SetTrigger("interact");
			}
			return;
		}
		_actionTimer -= dt;
		if (!(_actionTimer > 0f))
		{
			SortInventoryInPlace(_sortChest.GetInventory());
			CloseChest(_sortChest);
			if (_talk != null)
			{
				_talk.Say(ModLocalization.Loc("hc_speech_homestead_tidied"));
			}
			_sortChest = null;
			_sortCooldown = 120f;
			_phase = HomePhase.Idle;
			_idleTimer = UnityEngine.Random.Range(3f, 8f);
			Log("Chest sorted");
		}
	}

	private void UpdateMovingToIngredientChest(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_ingredientChest == null)
		{
			Abort("ingredient chest destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _ingredientChest.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_ingredientChest.transform.position);
			_chestOpened = false;
			_phase = HomePhase.TakingIngredients;
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _ingredientChest.transform.position, UseDistance, num > 8f);
		}
		UpdateStuck(dt, "ingredient chest", num);
	}

	private void UpdateTakingIngredients(float dt)
	{
		if (_ingredientChest == null)
		{
			CloseAnyChest();
			Abort("chest destroyed");
			return;
		}
		if (!_chestOpened)
		{
			_ingredientChest.SetInUse(inUse: true);
			_chestOpened = true;
			_actionTimer = 0.8f;
			if (_zanim != null)
			{
				_zanim.SetTrigger("interact");
			}
			return;
		}
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		_actionTimer = 0.6f;
		if (_ingredientIndex >= _ingredientList.Count)
		{
			CloseChest(_ingredientChest);
			ResetStuck();
			_phase = HomePhase.MovingToCauldron;
			return;
		}
		(string prefab, int needed) tuple = _ingredientList[_ingredientIndex];
		string item = tuple.prefab;
		int item2 = tuple.needed;
		Inventory inventory = _humanoid?.GetInventory();
		Inventory inventory2 = _ingredientChest.GetInventory();
		if (inventory == null || inventory2 == null)
		{
			CloseChest(_ingredientChest);
			Abort("inv null");
			return;
		}
		int num = CountItemInInventory(inventory, item);
		if (num >= item2)
		{
			_ingredientIndex++;
			CloseChest(_ingredientChest);
			FindNextIngredientChest();
			return;
		}
		ItemDrop.ItemData itemData = FindItemByPrefab(inventory2, item);
		if (itemData == null)
		{
			CloseChest(_ingredientChest);
			_ingredientChest = FindChestWithItem(item);
			if (_ingredientChest == null)
			{
				Abort("ran out of ingredient");
				return;
			}
			ResetStuck();
			_phase = HomePhase.MovingToIngredientChest;
		}
		else
		{
			TransferOne(inventory2, inventory, itemData);
		}
	}

	private void UpdateMovingToCauldron(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetCauldron == null)
		{
			Abort("cauldron destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetCauldron.transform.position);
		if (num <= UseDistance)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_targetCauldron.transform.position);
			_actionTimer = 3f;
			_phase = HomePhase.Cooking;
			return;
		}
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetCauldron.transform.position, UseDistance, num > 8f);
		}
		UpdateStuck(dt, "cauldron", num);
	}

	private void UpdateCooking(float dt)
	{
		if (_targetCauldron == null)
		{
			Abort("cauldron destroyed");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(_targetCauldron.transform.position);
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		if (_selectedRecipe == null)
		{
			Abort("no recipe");
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			Abort("inv null");
			return;
		}
		Piece.Requirement[] resources = _selectedRecipe.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if (!(requirement?.m_resItem == null))
			{
				string prefabName = requirement.m_resItem.gameObject.name;
				int amount = requirement.GetAmount(1);
				for (int j = 0; j < amount; j++)
				{
					ConsumeOneFromInventory(inventory, prefabName);
				}
			}
		}
		string text = _selectedRecipe.m_item.gameObject.name;
		int amount2 = _selectedRecipe.m_amount;
		ItemDrop.ItemData itemData = inventory.AddItem(text, amount2, 1, 0, 0L, "", cheated: false);
		string arg = _selectedRecipe.m_item.m_itemData.m_shared.m_name;
		if (itemData == null)
		{
			GameObject gameObject = ObjectDB.instance?.GetItemPrefab(text);
			if (gameObject != null)
			{
				GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, base.transform.position + Vector3.up, Quaternion.identity);
				ItemDrop component = gameObject2.GetComponent<ItemDrop>();
				if (component != null)
				{
					component.m_itemData.m_stack = amount2;
				}
			}
			Log($"Cooked {amount2}x \"{arg}\" (dropped — inv full)");
		}
		else
		{
			Log($"Cooked {amount2}x \"{arg}\"");
		}
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_homestead_cooked"));
		}
		Player localPlayer = Player.m_localPlayer;
		if (localPlayer != null && _character != null)
		{
			localPlayer.m_skillLevelupEffects?.Create(_character.GetHeadPoint(), _character.transform.rotation, _character.transform, 1f, -1, ZDOID.None);
		}
		_selectedRecipe = null;
		_targetCauldron = null;
		_ingredientChest = null;
		_ingredientList.Clear();
		_cookCooldown = 60f;
		_phase = HomePhase.Idle;
		_idleTimer = 5f;
	}

	private void PruneBlacklist()
	{
		if (_blacklist.Count == 0)
		{
			return;
		}
		float time = Time.time;
		_expiredKeys.Clear();
		foreach (KeyValuePair<int, float> item in _blacklist)
		{
			if (time >= item.Value)
			{
				_expiredKeys.Add(item.Key);
			}
		}
		for (int i = 0; i < _expiredKeys.Count; i++)
		{
			_blacklist.Remove(_expiredKeys[i]);
		}
	}

	private void PickNextBehavior()
	{
		Vector3 homePosition = _setup.GetHomePosition();
		float homeRadius = _setup.GetHomeRadius();
		float num = 0f;
		bool flag = IsCompanionHungry();
		float num2 = 0f;
		if (flag && _cookCooldown <= 0f)
		{
			num2 = 100f;
			num += num2;
		}
		float num3 = ((_repairCooldown <= 0f) ? 20f : 0f);
		num += num3;
		float num4 = ((_refuelCooldown <= 0f) ? 20f : 0f);
		num += num4;
		float num5 = ((_sortCooldown <= 0f) ? 10f : 0f);
		num += num5;
		float num6 = ((_sitCooldown <= 0f) ? 15f : 0f);
		num += num6;
		float num7 = ((_stationCooldown <= 0f) ? 10f : 0f);
		num += num7;
		float num8 = ((_wanderCooldown <= 0f) ? 25f : 0f);
		num += num8;
		if (num <= 0f)
		{
			_idleTimer = 5f;
			return;
		}
		float num9 = UnityEngine.Random.Range(0f, num);
		float num10 = 0f;
		num10 += num2;
		if (num9 < num10 && num2 > 0f)
		{
			if (TryStartCook(homePosition))
			{
				return;
			}
			_cookCooldown = 30f;
		}
		else
		{
			num10 += num3;
			if (num9 < num10 && num3 > 0f)
			{
				if (TryStartRepair(homePosition))
				{
					return;
				}
				_repairCooldown = 30f;
			}
			else
			{
				num10 += num4;
				if (num9 < num10 && num4 > 0f)
				{
					if (TryStartRefuel(homePosition))
					{
						return;
					}
					_refuelCooldown = 30f;
				}
				else
				{
					num10 += num5;
					if (num9 < num10 && num5 > 0f)
					{
						if (TryStartSort(homePosition))
						{
							return;
						}
						_sortCooldown = 60f;
					}
					else
					{
						num10 += num6;
						if (num9 < num10 && num6 > 0f)
						{
							if (TryStartSit(homePosition, homeRadius))
							{
								return;
							}
							_sitCooldown = 30f;
						}
						else
						{
							num10 += num7;
							if (num9 < num10 && num7 > 0f)
							{
								if (TryStartStation(homePosition))
								{
									return;
								}
								_stationCooldown = 30f;
							}
							else if (num8 > 0f)
							{
								StartWander(homePosition, homeRadius);
								return;
							}
						}
					}
				}
			}
		}
		_idleTimer = UnityEngine.Random.Range(3f, 8f);
	}

	private bool IsCompanionHungry()
	{
		if (_food == null)
		{
			return false;
		}
		for (int i = 0; i < CompanionFood.MaxFoodSlots; i++)
		{
			CompanionFood.FoodEffect food = _food.GetFood(i);
			if (!food.IsActive)
			{
				return true;
			}
			if (food.RemainingTime < food.TotalTime * 0.5f)
			{
				return true;
			}
		}
		return false;
	}

	private bool TryStartCook(Vector3 homePos)
	{
		List<CraftingStation> allCraftingStations = ReflectionHelper.GetAllCraftingStations();
		if (allCraftingStations == null)
		{
			return false;
		}
		CraftingStation craftingStation = null;
		float num = float.MaxValue;
		for (int i = 0; i < allCraftingStations.Count; i++)
		{
			CraftingStation craftingStation2 = allCraftingStations[i];
			if (craftingStation2 == null || craftingStation2.m_name == null || !craftingStation2.m_name.ToLower().Contains("cauldron"))
			{
				continue;
			}
			float num2 = Vector3.Distance(homePos, craftingStation2.transform.position);
			if (!(num2 > ScanRadius))
			{
				float num3 = Vector3.Distance(base.transform.position, craftingStation2.transform.position);
				if (num3 < num)
				{
					craftingStation = craftingStation2;
					num = num3;
				}
			}
		}
		if (craftingStation == null)
		{
			return false;
		}
		ScanNearbyChests(homePos);
		if (_nearbyChests.Count == 0)
		{
			return false;
		}
		List<Recipe> list = ObjectDB.instance?.m_recipes;
		if (list == null)
		{
			return false;
		}
		List<Recipe> list2 = new List<Recipe>();
		for (int j = 0; j < list.Count; j++)
		{
			Recipe recipe = list[j];
			if (!(recipe == null) && recipe.m_enabled && !(recipe.m_item == null) && !(recipe.m_craftingStation == null) && !(recipe.m_craftingStation.m_name != craftingStation.m_name))
			{
				ItemDrop.ItemData.SharedData sharedData = recipe.m_item.m_itemData?.m_shared;
				if (sharedData != null && (!(sharedData.m_food <= 0f) || !(sharedData.m_foodStamina <= 0f)) && craftingStation.GetLevel() >= recipe.GetRequiredStationLevel(1) && CanCraftFromChests(recipe))
				{
					list2.Add(recipe);
				}
			}
		}
		if (list2.Count == 0)
		{
			return false;
		}
		_selectedRecipe = list2[UnityEngine.Random.Range(0, list2.Count)];
		_targetCauldron = craftingStation;
		_ingredientList.Clear();
		Piece.Requirement[] resources = _selectedRecipe.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if (!(requirement?.m_resItem == null))
			{
				string item = requirement.m_resItem.gameObject.name;
				int amount = requirement.GetAmount(1);
				if (amount > 0)
				{
					_ingredientList.Add((item, amount));
				}
			}
		}
		_ingredientIndex = 0;
		Log($"Cooking \"{_selectedRecipe.m_item.m_itemData.m_shared.m_name}\" — {_ingredientList.Count} ingredient types");
		if (!FindNextIngredientChest())
		{
			_selectedRecipe = null;
			_targetCauldron = null;
			return false;
		}
		return true;
	}

	private bool TryStartRepair(Vector3 homePos)
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return false;
		}
		if (FindHammerInInventory(inventory) == null)
		{
			if (!_lastHammerMissing)
			{
				Log("No hammer — skipping repair");
				_lastHammerMissing = true;
			}
			return false;
		}
		_lastHammerMissing = false;
		List<WearNTear> allInstances = WearNTear.GetAllInstances();
		if (allInstances == null)
		{
			return false;
		}
		WearNTear wearNTear = null;
		float num = float.MaxValue;
		for (int i = 0; i < allInstances.Count; i++)
		{
			WearNTear wearNTear2 = allInstances[i];
			if (wearNTear2 == null)
			{
				continue;
			}
			Piece component = wearNTear2.GetComponent<Piece>();
			if (component == null || !component.IsPlacedByPlayer())
			{
				continue;
			}
			float num2 = Vector3.Distance(homePos, wearNTear2.transform.position);
			if (!(num2 > ScanRadius))
			{
				float healthPercentage = wearNTear2.GetHealthPercentage();
				if (!(healthPercentage >= 1f) && !IsBlacklisted(wearNTear2.gameObject) && !s_claimedPieces.Contains(wearNTear2.GetInstanceID()) && num2 < num)
				{
					wearNTear = wearNTear2;
					num = num2;
				}
			}
		}
		if (wearNTear == null)
		{
			return false;
		}
		_targetPiece = wearNTear;
		ClaimPiece(wearNTear.GetInstanceID());
		ResetStuck();
		_phase = HomePhase.MovingToRepairTarget;
		Log($"Repairing \"{wearNTear.GetComponent<Piece>()?.m_name ?? wearNTear.name}\" ({num:F1}m)");
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_homestead_repair"));
		}
		return true;
	}

	private bool TryStartRefuel(Vector3 homePos)
	{
		_tempPieces.Clear();
		Piece.GetAllPiecesInRadius(homePos, ScanRadius, _tempPieces);
		Fireplace fireplace = null;
		float num = float.MaxValue;
		for (int i = 0; i < _tempPieces.Count; i++)
		{
			Fireplace fireplace2 = _tempPieces[i]?.GetComponent<Fireplace>();
			if (fireplace2 == null || !fireplace2.m_canRefill || fireplace2.m_infiniteFuel || fireplace2.m_fuelItem == null)
			{
				continue;
			}
			ZNetView component = fireplace2.GetComponent<ZNetView>();
			if (component?.GetZDO() == null)
			{
				continue;
			}
			float num2 = component.GetZDO().GetFloat(ZDOVars.s_fuel);
			if (!(fireplace2.m_maxFuel <= 0f) && !(num2 / fireplace2.m_maxFuel >= FuelThreshold) && !IsBlacklisted(fireplace2.gameObject) && !s_claimedPieces.Contains(fireplace2.GetInstanceID()))
			{
				float num3 = Vector3.Distance(base.transform.position, fireplace2.transform.position);
				if (num3 < num)
				{
					fireplace = fireplace2;
					num = num3;
				}
			}
		}
		if (fireplace == null)
		{
			return false;
		}
		_targetFireplace = fireplace;
		ClaimPiece(fireplace.GetInstanceID());
		_fuelItemPrefab = fireplace.m_fuelItem.gameObject.name;
		ZNetView component2 = fireplace.GetComponent<ZNetView>();
		float num4 = component2.GetZDO().GetFloat(ZDOVars.s_fuel);
		_fuelToAdd = Mathf.Max(1, (int)(fireplace.m_maxFuel - num4));
		Inventory inventory = _humanoid?.GetInventory();
		int num5 = ((inventory != null) ? CountItemInInventory(inventory, _fuelItemPrefab) : 0);
		if (num5 > 0)
		{
			_fuelToAdd = Mathf.Min(_fuelToAdd, num5);
			ResetStuck();
			_phase = HomePhase.MovingToFireplace;
		}
		else
		{
			ScanNearbyChests(homePos);
			_supplyChest = FindChestWithItem(_fuelItemPrefab);
			if (_supplyChest == null)
			{
				_targetFireplace = null;
				UnclaimPiece();
				return false;
			}
			ResetStuck();
			_phase = HomePhase.MovingToSupplyChest;
		}
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_homestead_refuel"));
		}
		return true;
	}

	private bool TryStartSort(Vector3 homePos)
	{
		ScanNearbyChests(homePos);
		if (_nearbyChests.Count == 0)
		{
			return false;
		}
		List<Container> list = new List<Container>();
		for (int i = 0; i < _nearbyChests.Count; i++)
		{
			Inventory inventory = _nearbyChests[i].GetInventory();
			if (inventory != null && inventory.GetAllItems().Count >= 2)
			{
				list.Add(_nearbyChests[i]);
			}
		}
		if (list.Count == 0)
		{
			return false;
		}
		_sortChest = list[UnityEngine.Random.Range(0, list.Count)];
		ResetStuck();
		_phase = HomePhase.MovingToSortChest;
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_homestead_tidy"));
		}
		Log($"Moving to sort chest at {_sortChest.transform.position:F1}");
		return true;
	}

	private bool TryStartSit(Vector3 homePos, float radius)
	{
		int num = Physics.OverlapSphereNonAlloc(homePos, radius, _scanBuffer);
		Chair chair = null;
		float num2 = float.MaxValue;
		for (int i = 0; i < num; i++)
		{
			Chair componentInParent = _scanBuffer[i].GetComponentInParent<Chair>();
			if (componentInParent == null || componentInParent.m_attachPoint == null || IsBlacklisted(componentInParent.gameObject))
			{
				continue;
			}
			bool flag = false;
			List<Player> allPlayers = Player.GetAllPlayers();
			for (int j = 0; j < allPlayers.Count; j++)
			{
				if (allPlayers[j].IsAttached() && Vector3.Distance(allPlayers[j].transform.position, componentInParent.m_attachPoint.position) < 0.5f)
				{
					flag = true;
					break;
				}
			}
			if (!flag)
			{
				foreach (CompanionSetup allCompanion in CompanionSetup.AllCompanions)
				{
					if (!(allCompanion == null) && !(allCompanion == _setup))
					{
						HomesteadController component = allCompanion.GetComponent<HomesteadController>();
						if (component != null && component._isSitting && component._chairAttachPoint == componentInParent.m_attachPoint)
						{
							flag = true;
							break;
						}
					}
				}
			}
			if (!flag)
			{
				float num3 = Vector3.Distance(base.transform.position, componentInParent.transform.position);
				if (num3 < num2)
				{
					num2 = num3;
					chair = componentInParent;
				}
			}
		}
		if (chair == null)
		{
			return false;
		}
		_chairAttachPoint = chair.m_attachPoint;
		_sitDuration = UnityEngine.Random.Range(30f, 90f);
		ResetStuck();
		_phase = HomePhase.MovingToChair;
		Log($"Moving to chair at {chair.transform.position:F1}");
		return true;
	}

	private bool TryStartStation(Vector3 homePos)
	{
		List<CraftingStation> allCraftingStations = ReflectionHelper.GetAllCraftingStations();
		if (allCraftingStations == null)
		{
			return false;
		}
		CraftingStation craftingStation = null;
		float num = float.MaxValue;
		for (int i = 0; i < allCraftingStations.Count; i++)
		{
			CraftingStation craftingStation2 = allCraftingStations[i];
			if (craftingStation2 == null || (craftingStation2.m_name != null && craftingStation2.m_name.ToLower().Contains("cauldron")))
			{
				continue;
			}
			float num2 = Vector3.Distance(homePos, craftingStation2.transform.position);
			if (!(num2 > ScanRadius) && !IsBlacklisted(craftingStation2.gameObject))
			{
				float num3 = Vector3.Distance(base.transform.position, craftingStation2.transform.position);
				if (num3 < num)
				{
					craftingStation = craftingStation2;
					num = num3;
				}
			}
		}
		if (craftingStation == null)
		{
			return false;
		}
		_targetStation = craftingStation;
		_stationDuration = UnityEngine.Random.Range(10f, 20f);
		ResetStuck();
		_phase = HomePhase.MovingToStation;
		Log($"Moving to station \"{craftingStation.m_name}\" at {craftingStation.transform.position:F1}");
		return true;
	}

	private void StartWander(Vector3 homePos, float radius)
	{
		PickWanderPoint(homePos, radius);
		_wanderDuration = UnityEngine.Random.Range(30f, 60f);
		_phase = HomePhase.Wandering;
		Log($"Wandering around home for {_wanderDuration:F0}s");
	}

	private void PickWanderPoint(Vector3 homePos, float radius)
	{
		Vector2 vector = UnityEngine.Random.insideUnitCircle * radius * 0.8f;
		_wanderTarget = homePos + new Vector3(vector.x, 0f, vector.y);
		if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(_wanderTarget, out var height))
		{
			_wanderTarget.y = height;
		}
		_wanderTimeout = 15f;
	}

	private bool IsBlacklisted(GameObject target)
	{
		if (target == null)
		{
			return false;
		}
		int instanceID = target.GetInstanceID();
		if (!_blacklist.TryGetValue(instanceID, out var value))
		{
			return false;
		}
		if (Time.time >= value)
		{
			_blacklist.Remove(instanceID);
			return false;
		}
		return true;
	}

	private void ResetStuck()
	{
		_stuckTimer = 0f;
		_stuckCheckTimer = 0f;
		_stuckCheckPos = base.transform.position;
	}

	private void SitDown()
	{
		if (_chairAttachPoint == null)
		{
			Abort("no attach point");
			return;
		}
		base.transform.position = _chairAttachPoint.position;
		base.transform.rotation = _chairAttachPoint.rotation;
		if (_zanim != null)
		{
			_zanim.SetBool("attach_chair", value: true);
		}
		if (_body != null)
		{
			_body.useGravity = false;
		}
		_isSitting = true;
		_sitTimer = _sitDuration;
		_phase = HomePhase.Sitting;
		Log($"Sat down for {_sitDuration:F0}s");
	}

	private void UpdateStuck(float dt, string target, float dist)
	{
		_stuckCheckTimer += dt;
		if (_stuckCheckTimer >= 1f)
		{
			float num = Vector3.Distance(base.transform.position, _stuckCheckPos);
			if (num < 0.5f)
			{
				_stuckTimer += _stuckCheckTimer;
			}
			else
			{
				_stuckTimer = 0f;
			}
			_stuckCheckPos = base.transform.position;
			_stuckCheckTimer = 0f;
		}
		if (_stuckTimer > 10f)
		{
			Abort($"stuck moving to {target} ({dist:F1}m)");
		}
	}

	private void ClaimPiece(int id)
	{
		UnclaimPiece();
		_claimedPieceId = id;
		if (_claimedPieceId != 0)
		{
			s_claimedPieces.Add(_claimedPieceId);
		}
	}

	private void RestoreWeaponAfterRepair()
	{
		if (!_hammerEquipped)
		{
			return;
		}
		_hammerEquipped = false;
		ItemDrop.ItemData rightItem = ReflectionHelper.GetRightItem(_humanoid);
		if (rightItem?.m_shared?.m_buildPieces != null)
		{
			_humanoid.UnequipItem(rightItem, triggerEquipEffects: false);
		}
		if (_prevRightItem != null)
		{
			Inventory inventory = _humanoid?.GetInventory();
			if (inventory != null && inventory.ContainsItem(_prevRightItem))
			{
				_humanoid.EquipItem(_prevRightItem);
			}
			else
			{
				_setup?.SyncEquipmentToInventory();
			}
		}
		else
		{
			_setup?.SyncEquipmentToInventory();
		}
		_prevRightItem = null;
	}

	private void ScanNearbyChests(Vector3 center)
	{
		_nearbyChests.Clear();
		_tempPieces.Clear();
		Piece.GetAllPiecesInRadius(center, ScanRadius, _tempPieces);
		for (int i = 0; i < _tempPieces.Count; i++)
		{
			Container container = _tempPieces[i]?.GetComponentInChildren<Container>();
			if (!(container == null) && container.GetInventory() != null && !(container.gameObject == base.gameObject) && !container.IsInUse() && !IsBlacklisted(container.gameObject))
			{
				_nearbyChests.Add(container);
			}
		}
	}

	private Container FindChestWithItem(string prefabName)
	{
		Container result = null;
		float num = float.MaxValue;
		for (int i = 0; i < _nearbyChests.Count; i++)
		{
			Container container = _nearbyChests[i];
			if (container == null)
			{
				continue;
			}
			Inventory inventory = container.GetInventory();
			if (inventory != null && FindItemByPrefab(inventory, prefabName) != null)
			{
				float num2 = Vector3.Distance(base.transform.position, container.transform.position);
				if (num2 < num)
				{
					result = container;
					num = num2;
				}
			}
		}
		return result;
	}

	private void CloseChest(Container chest)
	{
		if (chest != null)
		{
			chest.SetInUse(inUse: false);
		}
		_chestOpened = false;
	}

	private void FinishRefuel()
	{
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_homestead_stoked"));
		}
		UnclaimPiece();
		_targetFireplace = null;
		_supplyChest = null;
		_fuelItemPrefab = null;
		_refuelCooldown = 30f;
		_phase = HomePhase.Idle;
		_idleTimer = 2f;
	}

	private void SortInventoryInPlace(Inventory inv)
	{
		if (inv == null)
		{
			return;
		}
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		if (allItems.Count >= 2)
		{
			int width = inv.GetWidth();
			allItems.Sort(delegate(ItemDrop.ItemData a, ItemDrop.ItemData b)
			{
				int sortPriority = GetSortPriority(a);
				int sortPriority2 = GetSortPriority(b);
				return (sortPriority != sortPriority2) ? sortPriority.CompareTo(sortPriority2) : string.Compare(a.m_shared.m_name, b.m_shared.m_name, StringComparison.Ordinal);
			});
			for (int num = 0; num < allItems.Count; num++)
			{
				allItems[num].m_gridPos = new Vector2i(num % width, num / width);
			}
			inv.m_onChanged?.Invoke();
		}
	}

	private static int GetSortPriority(ItemDrop.ItemData item)
	{
		switch (item.m_shared.m_itemType)
		{
		case ItemDrop.ItemData.ItemType.OneHandedWeapon:
		case ItemDrop.ItemData.ItemType.Bow:
		case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
		case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
			return 0;
		case ItemDrop.ItemData.ItemType.Shield:
			return 1;
		case ItemDrop.ItemData.ItemType.Helmet:
		case ItemDrop.ItemData.ItemType.Chest:
		case ItemDrop.ItemData.ItemType.Legs:
		case ItemDrop.ItemData.ItemType.Shoulder:
			return 2;
		case ItemDrop.ItemData.ItemType.Consumable:
			return 3;
		case ItemDrop.ItemData.ItemType.Ammo:
		case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
			return 4;
		case ItemDrop.ItemData.ItemType.Trophy:
			return 6;
		case ItemDrop.ItemData.ItemType.Tool:
			return 5;
		default:
			return 5;
		}
	}

	private bool CanCraftFromChests(Recipe recipe)
	{
		Piece.Requirement[] resources = recipe.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if (requirement?.m_resItem == null)
			{
				continue;
			}
			string prefabName = requirement.m_resItem.gameObject.name;
			int amount = requirement.GetAmount(1);
			if (amount <= 0)
			{
				continue;
			}
			int num = 0;
			for (int j = 0; j < _nearbyChests.Count; j++)
			{
				Inventory inventory = _nearbyChests[j]?.GetInventory();
				if (inventory != null)
				{
					num += CountItemInInventory(inventory, prefabName);
				}
			}
			if (num < amount)
			{
				return false;
			}
		}
		return true;
	}

	private bool FindNextIngredientChest()
	{
		while (_ingredientIndex < _ingredientList.Count)
		{
			(string prefab, int needed) tuple = _ingredientList[_ingredientIndex];
			string item = tuple.prefab;
			int item2 = tuple.needed;
			Inventory inventory = _humanoid?.GetInventory();
			int num = ((inventory != null) ? CountItemInInventory(inventory, item) : 0);
			if (num >= item2)
			{
				_ingredientIndex++;
				continue;
			}
			_ingredientChest = FindChestWithItem(item);
			if (_ingredientChest == null)
			{
				Abort("ingredient chest not found");
				return false;
			}
			ResetStuck();
			_phase = HomePhase.MovingToIngredientChest;
			return true;
		}
		ResetStuck();
		_phase = HomePhase.MovingToCauldron;
		return true;
	}
}
