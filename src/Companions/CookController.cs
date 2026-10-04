using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions;

public class CookController : MonoBehaviour
{
	internal enum CookPhase
	{
		Idle,
		MovingToIngredientChest,
		TakingIngredients,
		MovingToCauldron,
		Crafting,
		MovingToFermenter,
		InsertingMeadBase,
		MovingToReadyFermenter,
		TappingFermenter,
		CollectingDrops,
		MovingToOutputChest,
		StoringOutput
	}

	private enum CookTask
	{
		CookFood,
		BrewMead,
		FillFermenter,
		TapFermenter,
		StoreOutput
	}

	private enum FermenterStatus
	{
		Empty,
		Fermenting,
		Ready
	}

	private CompanionAI _ai;

	private Humanoid _humanoid;

	private Character _character;

	private CompanionSetup _setup;

	private ZNetView _nview;

	private ZSyncAnimation _zanim;

	private CompanionTalk _talk;

	private CompanionFood _food;

	private DoorHandler _doorHandler;

	private CookPhase _phase;

	private CookTask _currentTask;

	private float _scanTimer;

	private float _actionTimer;

	private float _stuckTimer;

	private float _stuckCheckTimer;

	private Vector3 _stuckCheckPos;

	private bool _chestOpened;

	private bool _shouldRun;

	private float _cookCooldown;

	private float _brewCooldown;

	private float _fillCooldown;

	private float _tapCooldown;

	private float _storeCooldown;

	private static readonly HashSet<int> s_claimedFermenters = new HashSet<int>();

	private int _claimedFermenterId;

	private CraftingStation _targetCauldron;

	private Fermenter _targetFermenter;

	private Container _targetChest;

	private Container _outputChest;

	private Recipe _selectedRecipe;

	private readonly List<(string prefab, int needed)> _ingredientList = new List<(string, int)>();

	private int _ingredientIndex;

	private string _meadBasePrefab;

	private readonly List<Fermenter> _nearbyFermenters = new List<Fermenter>();

	private readonly List<Container> _nearbyChests = new List<Container>();

	private readonly List<Piece> _tempPieces = new List<Piece>();

	private static readonly Collider[] _dropScanBuffer = new Collider[32];

	private static int _itemLayerMask = -1;

	private static HashSet<string> s_fermenterInputPrefabs;

	private const float ScanRadius = 25f;

	private const float ScanInterval = 5f;

	private const float UseDistance = 2.5f;

	private const float MoveTimeout = 10f;

	private const float StuckCheckPeriod = 1f;

	private const float StuckMinDistance = 0.5f;

	private const float ChestOpenDelay = 0.8f;

	private const float CraftingTime = 3f;

	private const float TapWaitTime = 2f;

	private const float WeightTap = 50f;

	private const float WeightCook = 40f;

	private const float WeightStore = 35f;

	private const float WeightFill = 30f;

	private const float WeightBrew = 25f;

	internal CookPhase Phase => _phase;

	public bool IsActive => _phase != CookPhase.Idle;

	public void NotifyActionModeChanged()
	{
		if (_phase != CookPhase.Idle)
		{
			Log("NotifyActionModeChanged — aborting cook");
			Abort("mode changed");
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
		_doorHandler = GetComponent<DoorHandler>();
	}

	private void OnDestroy()
	{
		UnclaimFermenter();
		CloseAnyChest();
	}

	private void UnclaimFermenter()
	{
		if (_claimedFermenterId != 0)
		{
			s_claimedFermenters.Remove(_claimedFermenterId);
			_claimedFermenterId = 0;
		}
	}

	private void CloseAnyChest()
	{
		if (_chestOpened)
		{
			if (_phase == CookPhase.StoringOutput && _outputChest != null)
			{
				_outputChest.SetInUse(inUse: false);
			}
			else if (_targetChest != null)
			{
				_targetChest.SetInUse(inUse: false);
			}
			_chestOpened = false;
		}
	}

	private void Update()
	{
		if (CompanionRest.MHR_ShouldPauseWork(this) || _nview == null || _nview.GetZDO() == null || !_nview.IsOwner() || _character == null || _character.GetHealth() <= 0f)
		{
			return;
		}
		if (ShouldAbort())
		{
			if (_phase != CookPhase.Idle)
			{
				Abort("interrupted");
			}
		}
		else
		{
			if (!IsCookMode() && _phase == CookPhase.Idle)
			{
				return;
			}
			if (!IsCookMode() && _phase != CookPhase.Idle)
			{
				Abort("mode changed");
				return;
			}
			float deltaTime = Time.deltaTime;
			_cookCooldown = Mathf.Max(0f, _cookCooldown - deltaTime);
			_brewCooldown = Mathf.Max(0f, _brewCooldown - deltaTime);
			_fillCooldown = Mathf.Max(0f, _fillCooldown - deltaTime);
			_tapCooldown = Mathf.Max(0f, _tapCooldown - deltaTime);
			_storeCooldown = Mathf.Max(0f, _storeCooldown - deltaTime);
			switch (_phase)
			{
			case CookPhase.Idle:
				UpdateIdle(deltaTime);
				break;
			case CookPhase.MovingToIngredientChest:
				UpdateMovingToIngredientChest(deltaTime);
				break;
			case CookPhase.TakingIngredients:
				UpdateTakingIngredients(deltaTime);
				break;
			case CookPhase.MovingToCauldron:
				UpdateMovingToCauldron(deltaTime);
				break;
			case CookPhase.Crafting:
				UpdateCrafting(deltaTime);
				break;
			case CookPhase.MovingToFermenter:
				UpdateMovingToFermenter(deltaTime);
				break;
			case CookPhase.InsertingMeadBase:
				UpdateInsertingMeadBase(deltaTime);
				break;
			case CookPhase.MovingToReadyFermenter:
				UpdateMovingToReadyFermenter(deltaTime);
				break;
			case CookPhase.TappingFermenter:
				UpdateTappingFermenter(deltaTime);
				break;
			case CookPhase.CollectingDrops:
				UpdateCollectingDrops(deltaTime);
				break;
			case CookPhase.MovingToOutputChest:
				UpdateMovingToOutputChest(deltaTime);
				break;
			case CookPhase.StoringOutput:
				UpdateStoringOutput(deltaTime);
				break;
			}
		}
	}

	private bool ShouldAbort()
	{
		if (_ai != null && _ai.IsInCombat)
		{
			return true;
		}
		if (CompanionInteractPanel.IsOpenFor(_setup) || CompanionRadialMenu.IsOpenFor(_setup))
		{
			return true;
		}
		return false;
	}

	private void Abort(string reason)
	{
		Log($"Aborted — {reason} (phase was {_phase})");
		UnclaimFermenter();
		CloseAnyChest();
		_targetCauldron = null;
		_targetFermenter = null;
		_targetChest = null;
		_outputChest = null;
		_selectedRecipe = null;
		_meadBasePrefab = null;
		_ingredientList.Clear();
		_phase = CookPhase.Idle;
		_scanTimer = 5f;
		RestoreFollow();
	}

	internal bool IsCookMode()
	{
		ZDO zDO = _nview?.GetZDO();
		if (zDO == null)
		{
			return false;
		}
		return zDO.GetInt(CompanionSetup.ActionModeHash) == 17;
	}

	private void UpdateIdle(float dt)
	{
		_scanTimer -= dt;
		if (!(_scanTimer > 0f))
		{
			_scanTimer = 5f;
			ScanNearbyChests();
			ScanNearbyFermenters();
			EnsureFermenterInputCache();
			PickNextBehavior();
		}
	}

	private void UpdateMovingToIngredientChest(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetChest == null)
		{
			Abort("ingredient chest destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetChest.transform.position);
		if (num < 2.5f)
		{
			_ai?.StopMoving();
			_chestOpened = false;
			_phase = CookPhase.TakingIngredients;
			return;
		}
		_shouldRun = num > 8f;
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetChest.transform.position, 2.5f, _shouldRun);
		}
		UpdateStuck(dt, "ingredient chest", num);
	}

	private void UpdateTakingIngredients(float dt)
	{
		if (_targetChest == null)
		{
			Abort("ingredient chest gone");
			return;
		}
		if (!_chestOpened)
		{
			_targetChest.SetInUse(inUse: true);
			_chestOpened = true;
			if (_zanim != null)
			{
				_zanim.SetTrigger("interact");
			}
			_actionTimer = 0.8f;
			return;
		}
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		Inventory inventory2 = _targetChest.GetInventory();
		if (inventory == null || inventory2 == null)
		{
			CloseCurrentChest();
			Abort("inventory null");
			return;
		}
		(string prefab, int needed) tuple = _ingredientList[_ingredientIndex];
		string item = tuple.prefab;
		int item2 = tuple.needed;
		int num = HomesteadController.CountItemInInventory(inventory, item);
		int num2 = item2 - num;
		if (num2 > 0)
		{
			ItemDrop.ItemData itemData = HomesteadController.FindItemByPrefab(inventory2, item);
			if (itemData != null)
			{
				HomesteadController.TransferOne(inventory2, inventory, itemData);
				return;
			}
			CloseCurrentChest();
			_targetChest = FindChestWithItem(item);
			if (_targetChest != null)
			{
				ResetStuck();
				_phase = CookPhase.MovingToIngredientChest;
			}
			else
			{
				Abort("no more chests with ingredient");
			}
			return;
		}
		_ingredientIndex++;
		if (FindNextIngredientChest())
		{
			CloseCurrentChest();
			ResetStuck();
			_phase = CookPhase.MovingToIngredientChest;
			return;
		}
		CloseCurrentChest();
		ResetStuck();
		if (_currentTask == CookTask.FillFermenter)
		{
			if (_targetFermenter == null)
			{
				Abort("fermenter gone");
				return;
			}
			_phase = CookPhase.MovingToFermenter;
			Log("Got mead base — heading to fermenter");
		}
		else if (_targetCauldron == null)
		{
			Abort("cauldron gone");
		}
		else
		{
			_phase = CookPhase.MovingToCauldron;
			Log("All ingredients gathered — heading to cauldron");
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
		if (num < 2.5f)
		{
			_ai?.StopMoving();
			_actionTimer = 3f;
			_phase = CookPhase.Crafting;
			Log("At cauldron — crafting");
			return;
		}
		_shouldRun = num > 8f;
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetCauldron.transform.position, 2.5f, _shouldRun);
		}
		UpdateStuck(dt, "cauldron", num);
	}

	private void UpdateCrafting(float dt)
	{
		if (_targetCauldron != null)
		{
			_ai?.StopMoving();
			_ai?.LookAtPoint(_targetCauldron.transform.position);
		}
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			Abort("inventory null");
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
					HomesteadController.ConsumeOneFromInventory(inventory, prefabName);
				}
			}
		}
		string text = _selectedRecipe.m_item.gameObject.name;
		int amount2 = _selectedRecipe.m_amount;
		ItemDrop.ItemData itemData = inventory.AddItem(text, amount2, 1, 0, 0L, "", cheated: false);
		if (itemData == null)
		{
			GameObject gameObject = ObjectDB.instance?.GetItemPrefab(text);
			if (gameObject != null)
			{
				GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, base.transform.position + Vector3.up * 0.5f, Quaternion.identity);
				ItemDrop component = gameObject2.GetComponent<ItemDrop>();
				if (component != null)
				{
					component.m_itemData.m_stack = amount2;
				}
				Log("Inventory full — dropped " + text + " on ground");
			}
		}
		string arg = _selectedRecipe.m_item.m_itemData?.m_shared?.m_name ?? text;
		Log($"Crafted {amount2}x {arg}");
		if (_currentTask == CookTask.BrewMead)
		{
			_brewCooldown = 15f;
		}
		else
		{
			_cookCooldown = 15f;
		}
		if (_currentTask == CookTask.BrewMead && itemData != null)
		{
			_meadBasePrefab = text;
			Fermenter fermenter = FindEmptyFermenter();
			if (fermenter != null)
			{
				_targetFermenter = fermenter;
				ClaimFermenter(fermenter);
				ResetStuck();
				_phase = CookPhase.MovingToFermenter;
				if (_talk != null)
				{
					_talk.Say(ModLocalization.Loc("hc_speech_cook_fermenter"));
				}
				Log("Brewed mead base — heading to fermenter");
				return;
			}
		}
		if (HasStorableOutput() && TryStartStore())
		{
			_currentTask = CookTask.StoreOutput;
		}
		else
		{
			Finish();
		}
	}

	private void UpdateMovingToFermenter(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetFermenter == null)
		{
			Abort("fermenter destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetFermenter.transform.position);
		if (num < 2.5f)
		{
			_ai?.StopMoving();
			_actionTimer = 0.5f;
			_phase = CookPhase.InsertingMeadBase;
			return;
		}
		_shouldRun = num > 8f;
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetFermenter.transform.position, 2.5f, _shouldRun);
		}
		UpdateStuck(dt, "fermenter", num);
	}

	private void UpdateInsertingMeadBase(float dt)
	{
		if (_targetFermenter == null)
		{
			Abort("fermenter gone");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(_targetFermenter.transform.position);
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		if (GetFermenterStatus(_targetFermenter) != FermenterStatus.Empty)
		{
			Log("Fermenter no longer empty — aborting fill");
			UnclaimFermenter();
			Finish();
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null || !HomesteadController.ConsumeOneFromInventory(inventory, _meadBasePrefab))
		{
			Log("Don't have mead base \"" + _meadBasePrefab + "\" — aborting");
			UnclaimFermenter();
			Finish();
			return;
		}
		ZNetView component = _targetFermenter.GetComponent<ZNetView>();
		if (component != null && component.GetZDO() != null)
		{
			component.InvokeRPC("RPC_AddItem", _meadBasePrefab);
			Log("Inserted \"" + _meadBasePrefab + "\" into fermenter");
		}
		UnclaimFermenter();
		_fillCooldown = 10f;
		Finish();
	}

	private void UpdateMovingToReadyFermenter(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_targetFermenter == null)
		{
			Abort("ready fermenter destroyed");
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _targetFermenter.transform.position);
		if (num < 2.5f)
		{
			_ai?.StopMoving();
			_actionTimer = 0f;
			_phase = CookPhase.TappingFermenter;
			return;
		}
		_shouldRun = num > 8f;
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _targetFermenter.transform.position, 2.5f, _shouldRun);
		}
		UpdateStuck(dt, "ready fermenter", num);
	}

	private void UpdateTappingFermenter(float dt)
	{
		if (_targetFermenter == null)
		{
			Abort("fermenter gone during tap");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(_targetFermenter.transform.position);
		if (_actionTimer <= 0f)
		{
			if (GetFermenterStatus(_targetFermenter) != FermenterStatus.Ready)
			{
				Log("Fermenter no longer ready");
				UnclaimFermenter();
				Finish();
				return;
			}
			ZNetView component = _targetFermenter.GetComponent<ZNetView>();
			if (component != null && component.GetZDO() != null)
			{
				component.InvokeRPC("RPC_Tap");
				Log("Tapped fermenter — waiting for items to spawn");
			}
			_actionTimer = 2f;
		}
		else
		{
			_actionTimer -= dt;
			if (!(_actionTimer > 0f))
			{
				_phase = CookPhase.CollectingDrops;
			}
		}
	}

	private void UpdateCollectingDrops(float dt)
	{
		if (_targetFermenter == null)
		{
			UnclaimFermenter();
			Finish();
			return;
		}
		Vector3 vector = ((_targetFermenter.m_outputPoint != null) ? _targetFermenter.m_outputPoint.position : _targetFermenter.transform.position);
		float num = Vector3.Distance(base.transform.position, vector);
		if (num > 3.5f)
		{
			_shouldRun = num > 8f;
			CompanionAI ai = _ai;
			if ((object)ai != null)
			{
				MHR_Nav.MoveWalk(ai, dt, vector, 2.5f, _shouldRun);
			}
			return;
		}
		_ai?.StopMoving();
		PickUpNearbyDrops(vector);
		UnclaimFermenter();
		_tapCooldown = 5f;
		if (HasStorableOutput() && TryStartStore())
		{
			_currentTask = CookTask.StoreOutput;
		}
		else
		{
			Finish();
		}
	}

	private void UpdateMovingToOutputChest(float dt)
	{
		if (_doorHandler != null && _doorHandler.IsActive)
		{
			return;
		}
		if (_outputChest == null)
		{
			Finish();
			return;
		}
		float num = MHR_Nav.ReachDist(base.transform.position, _outputChest.transform.position);
		if (num < 2.5f)
		{
			_ai?.StopMoving();
			_chestOpened = false;
			_phase = CookPhase.StoringOutput;
			return;
		}
		_shouldRun = num > 8f;
		CompanionAI ai = _ai;
		if ((object)ai != null)
		{
			MHR_Nav.MoveWalk(ai, dt, _outputChest.transform.position, 2.5f, _shouldRun);
		}
		UpdateStuck(dt, "output chest", num);
	}

	private void UpdateStoringOutput(float dt)
	{
		if (_outputChest == null)
		{
			if (_chestOpened)
			{
				_chestOpened = false;
			}
			Finish();
			return;
		}
		if (!_chestOpened)
		{
			_outputChest.SetInUse(inUse: true);
			_chestOpened = true;
			if (_zanim != null)
			{
				_zanim.SetTrigger("interact");
			}
			_actionTimer = 0.8f;
			return;
		}
		_actionTimer -= dt;
		if (_actionTimer > 0f)
		{
			return;
		}
		Inventory inventory = _humanoid?.GetInventory();
		Inventory inventory2 = _outputChest.GetInventory();
		if (inventory == null || inventory2 == null)
		{
			_outputChest.SetInUse(inUse: false);
			_chestOpened = false;
			Finish();
			return;
		}
		int num = 0;
		List<ItemDrop.ItemData> allItems = inventory.GetAllItems();
		for (int num2 = allItems.Count - 1; num2 >= 0; num2--)
		{
			ItemDrop.ItemData itemData = allItems[num2];
			if (itemData != null && IsStorableOutput(itemData) && inventory2.CanAddItem(itemData, itemData.m_stack))
			{
				ItemDrop.ItemData itemData2 = itemData.Clone();
				itemData2.m_stack = itemData.m_stack;
				if (inventory2.AddItem(itemData2))
				{
					inventory.RemoveItem(itemData);
					num++;
				}
			}
		}
		_outputChest.SetInUse(inUse: false);
		_chestOpened = false;
		_storeCooldown = 10f;
		Log($"Stored {num} item stacks in chest");
		Finish();
	}

	private void LateUpdate()
	{
		if (!(_character == null) && (_phase == CookPhase.MovingToIngredientChest || _phase == CookPhase.MovingToCauldron || _phase == CookPhase.MovingToFermenter || _phase == CookPhase.MovingToReadyFermenter || _phase == CookPhase.MovingToOutputChest || _phase == CookPhase.CollectingDrops))
		{
			_character.SetRun(_shouldRun);
		}
	}

	private void Log(string msg)
	{
		string text = _character?.m_name ?? "?";
		CompanionsPlugin.Log.LogInfo("[Cook|" + text + "] " + msg);
	}

	private void ScanNearbyChests()
	{
		_nearbyChests.Clear();
		_tempPieces.Clear();
		Piece.GetAllPiecesInRadius(base.transform.position, 25f, _tempPieces);
		foreach (Piece tempPiece in _tempPieces)
		{
			if (!(tempPiece == null))
			{
				Container componentInChildren = tempPiece.GetComponentInChildren<Container>();
				if (!(componentInChildren == null) && componentInChildren.GetInventory() != null && !(componentInChildren.gameObject == base.gameObject) && !componentInChildren.IsInUse())
				{
					_nearbyChests.Add(componentInChildren);
				}
			}
		}
	}

	private void ScanNearbyFermenters()
	{
		_nearbyFermenters.Clear();
		foreach (Piece tempPiece in _tempPieces)
		{
			if (tempPiece == null)
			{
				continue;
			}
			Fermenter component = tempPiece.GetComponent<Fermenter>();
			if (!(component == null))
			{
				ZNetView component2 = component.GetComponent<ZNetView>();
				if (!(component2 == null) && component2.GetZDO() != null)
				{
					_nearbyFermenters.Add(component);
				}
			}
		}
	}

	private static void EnsureFermenterInputCache()
	{
		if (s_fermenterInputPrefabs != null)
		{
			return;
		}
		s_fermenterInputPrefabs = new HashSet<string>();
		Fermenter[] array = Resources.FindObjectsOfTypeAll<Fermenter>();
		Fermenter[] array2 = array;
		foreach (Fermenter fermenter in array2)
		{
			if (fermenter?.m_conversion == null)
			{
				continue;
			}
			foreach (Fermenter.ItemConversion item in fermenter.m_conversion)
			{
				if (item?.m_from != null)
				{
					s_fermenterInputPrefabs.Add(item.m_from.gameObject.name);
				}
			}
		}
	}

	private void PickNextBehavior()
	{
		if (_storeCooldown <= 0f && HasStorableOutput() && TryStartStore())
		{
			_currentTask = CookTask.StoreOutput;
			return;
		}
		if (_tapCooldown <= 0f && TryStartTapFermenter())
		{
			_currentTask = CookTask.TapFermenter;
			return;
		}
		float num = ((_cookCooldown <= 0f) ? 40f : 0f);
		float num2 = ((_brewCooldown <= 0f) ? 25f : 0f);
		float num3 = ((_fillCooldown <= 0f) ? 30f : 0f);
		float num4 = num + num2 + num3;
		if (num4 <= 0f)
		{
			return;
		}
		float num5 = UnityEngine.Random.Range(0f, num4);
		float num6 = 0f;
		num6 += num;
		if (num5 < num6)
		{
			if (TryStartCookOrBrew(meadOnly: false))
			{
				_currentTask = CookTask.CookFood;
			}
			else
			{
				_cookCooldown = 30f;
			}
			return;
		}
		num6 += num2;
		if (num5 < num6)
		{
			if (TryStartCookOrBrew(meadOnly: true))
			{
				_currentTask = CookTask.BrewMead;
			}
			else
			{
				_brewCooldown = 30f;
			}
			return;
		}
		num6 += num3;
		if (num5 < num6)
		{
			if (TryStartFillFermenter())
			{
				_currentTask = CookTask.FillFermenter;
			}
			else
			{
				_fillCooldown = 20f;
			}
		}
	}

	private bool HasStorableOutput()
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return false;
		}
		List<ItemDrop.ItemData> allItems = inventory.GetAllItems();
		foreach (ItemDrop.ItemData item in allItems)
		{
			if (IsStorableOutput(item))
			{
				return true;
			}
		}
		return false;
	}

	private bool TryStartStore()
	{
		ScanNearbyChests();
		_outputChest = FindChestWithSpace();
		if (_outputChest == null)
		{
			Log("No chest with space for output");
			return false;
		}
		ClearFollowForMovement();
		ResetStuck();
		_phase = CookPhase.MovingToOutputChest;
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_cook_storing"));
		}
		Log($"Storing output in chest at {_outputChest.transform.position:F1}");
		return true;
	}

	private bool TryStartTapFermenter()
	{
		foreach (Fermenter nearbyFermenter in _nearbyFermenters)
		{
			if (!(nearbyFermenter == null) && !s_claimedFermenters.Contains(nearbyFermenter.GetInstanceID()) && GetFermenterStatus(nearbyFermenter) == FermenterStatus.Ready)
			{
				_targetFermenter = nearbyFermenter;
				ClaimFermenter(nearbyFermenter);
				ClearFollowForMovement();
				ResetStuck();
				_phase = CookPhase.MovingToReadyFermenter;
				Log("Found ready fermenter — heading to tap");
				if (_talk != null)
				{
					_talk.Say(ModLocalization.Loc("hc_speech_cook_tapping"));
				}
				return true;
			}
		}
		return false;
	}

	private bool TryStartCookOrBrew(bool meadOnly)
	{
		_targetCauldron = FindCauldron();
		if (_targetCauldron == null)
		{
			Log("No cauldron found nearby");
			return false;
		}
		List<Recipe> list = ObjectDB.instance?.m_recipes;
		if (list == null)
		{
			return false;
		}
		List<Recipe> list2 = new List<Recipe>();
		foreach (Recipe item in list)
		{
			if (item == null || !item.m_enabled || item.m_item == null || item.m_craftingStation == null || item.m_craftingStation.m_name != _targetCauldron.m_name || _targetCauldron.GetLevel() < item.GetRequiredStationLevel(1))
			{
				continue;
			}
			ItemDrop.ItemData.SharedData sharedData = item.m_item.m_itemData?.m_shared;
			if (sharedData == null)
			{
				continue;
			}
			if (meadOnly)
			{
				if (!IsFermenterInput(item.m_item.gameObject.name))
				{
					continue;
				}
			}
			else if ((sharedData.m_food <= 0f && sharedData.m_foodStamina <= 0f) || IsFermenterInput(item.m_item.gameObject.name))
			{
				continue;
			}
			if (CanCraftFromChests(item))
			{
				list2.Add(item);
			}
		}
		if (list2.Count == 0)
		{
			Log(meadOnly ? "No viable mead base recipes" : "No viable food recipes");
			return false;
		}
		_selectedRecipe = list2[UnityEngine.Random.Range(0, list2.Count)];
		_ingredientList.Clear();
		Piece.Requirement[] resources = _selectedRecipe.m_resources;
		foreach (Piece.Requirement requirement in resources)
		{
			if (!(requirement?.m_resItem == null))
			{
				int amount = requirement.GetAmount(1);
				if (amount > 0)
				{
					_ingredientList.Add((requirement.m_resItem.gameObject.name, amount));
				}
			}
		}
		_ingredientIndex = 0;
		if (!FindNextIngredientChest())
		{
			Log("Can't find chest with first ingredient");
			return false;
		}
		ClearFollowForMovement();
		ResetStuck();
		_phase = CookPhase.MovingToIngredientChest;
		string text = _selectedRecipe.m_item.m_itemData?.m_shared?.m_name ?? "?";
		Log("Starting " + (meadOnly ? "brew" : "cook") + ": " + text);
		if (_talk != null)
		{
			string key = (meadOnly ? "hc_speech_cook_brewing" : "hc_speech_cook_cooking");
			_talk.Say(ModLocalization.Loc(key));
		}
		return true;
	}

	private bool TryStartFillFermenter()
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return false;
		}
		string text = FindMeadBaseInInventory(inventory);
		Container container = null;
		if (text == null)
		{
			foreach (Container nearbyChest in _nearbyChests)
			{
				Inventory inventory2 = nearbyChest?.GetInventory();
				if (inventory2 != null)
				{
					string text2 = FindMeadBaseInInventoryInternal(inventory2);
					if (text2 != null)
					{
						text = text2;
						container = nearbyChest;
						break;
					}
				}
			}
		}
		if (text == null)
		{
			Log("No mead base available");
			return false;
		}
		Fermenter fermenter = null;
		foreach (Fermenter nearbyFermenter in _nearbyFermenters)
		{
			if (!(nearbyFermenter == null) && !s_claimedFermenters.Contains(nearbyFermenter.GetInstanceID()) && GetFermenterStatus(nearbyFermenter) == FermenterStatus.Empty && FermenterAccepts(nearbyFermenter, text))
			{
				fermenter = nearbyFermenter;
				break;
			}
		}
		if (fermenter == null)
		{
			Log("No empty fermenter available");
			return false;
		}
		_targetFermenter = fermenter;
		_meadBasePrefab = text;
		ClaimFermenter(fermenter);
		ClearFollowForMovement();
		ResetStuck();
		if (container != null)
		{
			_targetChest = container;
			_phase = CookPhase.MovingToIngredientChest;
			_ingredientList.Clear();
			_ingredientList.Add((text, 1));
			_ingredientIndex = 0;
			Log("Getting mead base \"" + text + "\" from chest, then filling fermenter");
		}
		else
		{
			_phase = CookPhase.MovingToFermenter;
			Log("Have mead base \"" + text + "\" — heading to fermenter");
		}
		if (_talk != null)
		{
			_talk.Say(ModLocalization.Loc("hc_speech_cook_fermenter"));
		}
		return true;
	}

	private CraftingStation FindCauldron()
	{
		List<CraftingStation> allCraftingStations = ReflectionHelper.GetAllCraftingStations();
		if (allCraftingStations == null)
		{
			return null;
		}
		CraftingStation result = null;
		float num = float.MaxValue;
		foreach (CraftingStation item in allCraftingStations)
		{
			if (!(item == null) && item.m_name.ToLower().Contains("cauldron"))
			{
				float num2 = Vector3.Distance(base.transform.position, item.transform.position);
				if (!(num2 > 25f) && num2 < num)
				{
					num = num2;
					result = item;
				}
			}
		}
		return result;
	}

	private static bool IsFermenterInput(string prefabName)
	{
		if (s_fermenterInputPrefabs == null)
		{
			return false;
		}
		return s_fermenterInputPrefabs.Contains(prefabName);
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
			Inventory inventory = _humanoid?.GetInventory();
			if (inventory != null)
			{
				num += HomesteadController.CountItemInInventory(inventory, prefabName);
			}
			foreach (Container nearbyChest in _nearbyChests)
			{
				Inventory inventory2 = nearbyChest?.GetInventory();
				if (inventory2 != null)
				{
					num += HomesteadController.CountItemInInventory(inventory2, prefabName);
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
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return false;
		}
		while (_ingredientIndex < _ingredientList.Count)
		{
			(string prefab, int needed) tuple = _ingredientList[_ingredientIndex];
			string item = tuple.prefab;
			int item2 = tuple.needed;
			int num = HomesteadController.CountItemInInventory(inventory, item);
			if (num >= item2)
			{
				_ingredientIndex++;
				continue;
			}
			_targetChest = FindChestWithItem(item);
			if (_targetChest != null)
			{
				return true;
			}
			Log("No chest has ingredient \"" + item + "\"");
			return false;
		}
		return false;
	}

	private void ClearFollowForMovement()
	{
		if (_ai != null)
		{
			_ai.SetFollowTarget(null);
			Log("Cleared follow target — CookController driving movement");
		}
	}

	private void ResetStuck()
	{
		_stuckTimer = 0f;
		_stuckCheckTimer = 0f;
		_stuckCheckPos = base.transform.position;
	}

	private Container FindChestWithItem(string prefab)
	{
		Container result = null;
		float num = float.MaxValue;
		foreach (Container nearbyChest in _nearbyChests)
		{
			if (nearbyChest == null || nearbyChest.IsInUse())
			{
				continue;
			}
			Inventory inventory = nearbyChest.GetInventory();
			if (inventory != null && HomesteadController.CountItemInInventory(inventory, prefab) > 0)
			{
				float num2 = Vector3.Distance(base.transform.position, nearbyChest.transform.position);
				if (num2 < num)
				{
					num = num2;
					result = nearbyChest;
				}
			}
		}
		return result;
	}

	private void UpdateStuck(float dt, string target, float dist)
	{
		_stuckCheckTimer += dt;
		if (_stuckCheckTimer < 1f)
		{
			return;
		}
		_stuckCheckTimer = 0f;
		float num = Vector3.Distance(base.transform.position, _stuckCheckPos);
		_stuckCheckPos = base.transform.position;
		if (num < 0.5f)
		{
			_stuckTimer += 1f;
			if (_stuckTimer > 10f)
			{
				Log($"Stuck moving to {target} (dist={dist:F1}m) — aborting");
				Abort("stuck moving to " + target);
			}
		}
		else
		{
			_stuckTimer = 0f;
		}
	}

	private void CloseCurrentChest()
	{
		if (_chestOpened && _targetChest != null)
		{
			_targetChest.SetInUse(inUse: false);
		}
		_chestOpened = false;
	}

	private Fermenter FindEmptyFermenter()
	{
		foreach (Fermenter nearbyFermenter in _nearbyFermenters)
		{
			if (!(nearbyFermenter == null) && !s_claimedFermenters.Contains(nearbyFermenter.GetInstanceID()) && GetFermenterStatus(nearbyFermenter) == FermenterStatus.Empty && (_meadBasePrefab == null || FermenterAccepts(nearbyFermenter, _meadBasePrefab)))
			{
				return nearbyFermenter;
			}
		}
		return null;
	}

	private void ClaimFermenter(Fermenter f)
	{
		UnclaimFermenter();
		_claimedFermenterId = f?.GetInstanceID() ?? 0;
		if (_claimedFermenterId != 0)
		{
			s_claimedFermenters.Add(_claimedFermenterId);
		}
	}

	private void Finish()
	{
		Log("Finish — restoring follow target");
		UnclaimFermenter();
		CloseAnyChest();
		_targetCauldron = null;
		_targetFermenter = null;
		_targetChest = null;
		_outputChest = null;
		_selectedRecipe = null;
		_meadBasePrefab = null;
		_ingredientList.Clear();
		_phase = CookPhase.Idle;
		_scanTimer = 5f;
		RestoreFollow();
	}

	private string FindMeadBaseInInventory(Inventory inv)
	{
		return FindMeadBaseInInventoryInternal(inv);
	}

	private string FindMeadBaseInInventoryInternal(Inventory inv)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		foreach (ItemDrop.ItemData item in allItems)
		{
			string text = item.m_dropPrefab?.name;
			if (text != null && IsFermenterInput(text))
			{
				return text;
			}
		}
		return null;
	}

	private FermenterStatus GetFermenterStatus(Fermenter fermenter)
	{
		ZNetView component = fermenter.GetComponent<ZNetView>();
		if (component == null || component.GetZDO() == null)
		{
			return FermenterStatus.Empty;
		}
		string value = component.GetZDO().GetString(ZDOVars.s_content);
		if (string.IsNullOrEmpty(value))
		{
			return FermenterStatus.Empty;
		}
		long num = component.GetZDO().GetLong(ZDOVars.s_startTime, 0L);
		if (num == 0L)
		{
			return FermenterStatus.Fermenting;
		}
		DateTime dateTime = new DateTime(num);
		double totalSeconds = (ZNet.instance.GetTime() - dateTime).TotalSeconds;
		if (!(totalSeconds > (double)fermenter.m_fermentationDuration))
		{
			return FermenterStatus.Fermenting;
		}
		return FermenterStatus.Ready;
	}

	private static bool FermenterAccepts(Fermenter fermenter, string prefabName)
	{
		foreach (Fermenter.ItemConversion item in fermenter.m_conversion)
		{
			if (item?.m_from != null && item.m_from.gameObject.name == prefabName)
			{
				return true;
			}
		}
		return false;
	}

	private void PickUpNearbyDrops(Vector3 center)
	{
		if (_itemLayerMask < 0)
		{
			_itemLayerMask = LayerMask.GetMask("item");
		}
		int num = Physics.OverlapSphereNonAlloc(center, 3f, _dropScanBuffer, _itemLayerMask);
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return;
		}
		for (int i = 0; i < num; i++)
		{
			Collider collider = _dropScanBuffer[i];
			if (collider == null)
			{
				continue;
			}
			ItemDrop componentInParent = collider.GetComponentInParent<ItemDrop>();
			if (!(componentInParent == null) && componentInParent.m_itemData != null)
			{
				ZNetView component = componentInParent.GetComponent<ZNetView>();
				if (!(component == null) && component.GetZDO() != null && inventory.CanAddItem(componentInParent.m_itemData, componentInParent.m_itemData.m_stack))
				{
					inventory.AddItem(componentInParent.m_itemData);
					component.ClaimOwnership();
					component.Destroy();
					Log($"Picked up {componentInParent.m_itemData.m_stack}x \"{componentInParent.m_itemData.m_shared?.m_name}\"");
				}
			}
		}
	}

	private Container FindChestWithSpace()
	{
		Container result = null;
		float num = float.MaxValue;
		foreach (Container nearbyChest in _nearbyChests)
		{
			if (nearbyChest == null || nearbyChest.IsInUse())
			{
				continue;
			}
			Inventory inventory = nearbyChest.GetInventory();
			if (inventory != null && inventory.GetEmptySlots() > 0)
			{
				float num2 = Vector3.Distance(base.transform.position, nearbyChest.transform.position);
				if (num2 < num)
				{
					num = num2;
					result = nearbyChest;
				}
			}
		}
		return result;
	}

	private bool IsStorableOutput(ItemDrop.ItemData item)
	{
		if (item == null)
		{
			return false;
		}
		ItemDrop.ItemData.SharedData shared = item.m_shared;
		if (shared == null)
		{
			return false;
		}
		if (shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable && shared.m_itemType != ItemDrop.ItemData.ItemType.Material)
		{
			return false;
		}
		if (item.m_equipped)
		{
			return false;
		}
		bool flag = shared.m_food > 0f || shared.m_foodStamina > 0f;
		bool flag2 = item.m_dropPrefab != null && IsFermenterInput(item.m_dropPrefab.name);
		return flag || flag2;
	}

	private void RestoreFollow()
	{
		if (!(_ai == null))
		{
			bool flag = _setup != null && _setup.GetFollow();
			if (_setup != null && _setup.GetStayHome() && _setup.HasHomePosition())
			{
				_ai.SetFollowTarget(null);
				_ai.SetPatrolPointAt(_setup.GetHomePosition());
			}
			else if (flag && Player.m_localPlayer != null)
			{
				_ai.SetFollowTarget(Player.m_localPlayer.gameObject);
			}
		}
	}
}
