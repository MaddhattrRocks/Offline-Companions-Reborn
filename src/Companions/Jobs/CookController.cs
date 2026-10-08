using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.4.87: the Cook job, rewritten (design agreed with the user 2026-10-07, see docs/DEV_NOTES.md).
// Works like a player at home or at a camp:
// - Uses every cooking station: cauldron and food preparation table (crafted there, needs fire/roof like for a player),
//   cooking station, iron cooking station and oven (items go on, come off when done, oven gets wood).
// - Makes the best food it can: a recipe the player knows, food below Cooking.FoodTarget in the chests in range,
//   and every raw ingredient above Cooking.IngredientReserve (the player always keeps some to cook with).
//   Prep table -> oven chains (dough, raw pies) count as the food they turn into.
// - Mead: brews the bases in Cooking.MeadFocus for the last boss beaten (up to MeadBaseTarget), keeps fermenters
//   going while the finished mead is below MeadTarget, taps finished fermenters.
// - Takes finished food off any cooking station in range before it burns.
// - Puts away exactly what it made (a ledger kept on the companion), not food it was given to eat.
public class CookController : MonoBehaviour
{
	internal enum CookPhase
	{
		Idle,
		Fetching,
		MovingToStation,
		Crafting,
		Cooking,
		Fermenting,
		CollectingDrops,
		Storing
	}

	private enum TaskKind
	{
		None,
		Craft,
		Grill,
		Rescue,
		Fill,
		Tap,
		Store
	}

	private class Task
	{
		public TaskKind Kind;

		public Recipe Recipe;

		public CraftingStation Station;

		public CookingStation Cooker;

		public Fermenter Fermenter;

		public string Item;

		public int Count;

		public string Final;

		public readonly List<KeyValuePair<string, int>> Fetch = new List<KeyValuePair<string, int>>();

		public string Describe()
		{
			switch (Kind)
			{
			case TaskKind.Craft:
				return "make " + Count + "x " + Name(Recipe?.m_item?.gameObject.name) + " at the " + Loc(Station?.m_name);
			case TaskKind.Grill:
				return "cook " + Count + "x " + Name(Item) + " on the " + Loc(Cooker?.m_name);
			case TaskKind.Rescue:
				return "take finished food off the " + Loc(Cooker?.m_name);
			case TaskKind.Fill:
				return "fill a fermenter with " + Name(Item);
			case TaskKind.Tap:
				return "tap a fermenter";
			case TaskKind.Store:
				return "put away " + Name(Item);
			default:
				return "nothing";
			}
		}
	}

	private CompanionAI _ai;

	private Humanoid _humanoid;

	private Character _character;

	private CompanionSetup _setup;

	private ZNetView _nview;

	private ZSyncAnimation _zanim;

	private CompanionTalk _talk;

	private CookPhase _phase;

	private Task _task;

	private float _scanTimer;

	private float _timer;

	private float _phaseStart;

	private float _nextWatchLog;

	private int _fetchIndex;

	private Container _chest;

	private bool _chestOpen;

	private Vector3 _cookArea;

	private bool _hasCookArea;

	private bool _allDoneNotified;

	private readonly List<Container> _chests = new List<Container>();

	private readonly List<CraftingStation> _stations = new List<CraftingStation>();

	private readonly List<CookingStation> _cookers = new List<CookingStation>();

	private readonly List<Fermenter> _fermenters = new List<Fermenter>();

	private readonly List<Piece> _pieces = new List<Piece>();

	private readonly HashSet<int> _unreachable = new HashSet<int>();

	private float _unreachableClearAt;

	private readonly Dictionary<int, float> _stationProblemUntil = new Dictionary<int, float>();

	private readonly Dictionary<string, int> _ledger = new Dictionary<string, int>();

	private bool _ledgerLoaded;

	private string _approachKey;

	private Vector3 _approachSpot;

	private bool _hasApproach;

	private float _stuckTimer;

	private float _stuckCheckTimer;

	private float _stuckBestDist;

	private static readonly HashSet<int> s_claimed = new HashSet<int>();

	private int _claimedId;

	private static readonly Collider[] s_drops = new Collider[48];

	private static int s_itemMask = -1;

	private static readonly int LedgerHash = "HC_CookLedger".GetStableHashCode();

	private static readonly FieldInfo s_haveFire = AccessTools.Field(typeof(CraftingStation), "m_haveFire");

	private static readonly MethodInfo s_isFireLit = AccessTools.Method(typeof(CookingStation), "IsFireLit");

	private const float ScanInterval = 4f;

	private const float UseReach = 2.5f;

	internal CookPhase Phase => _phase;

	public bool IsActive => _phase != CookPhase.Idle;

	private static float Range => ModConfig.CookRange?.Value ?? 25f;

	private static int Reserve => ModConfig.CookIngredientReserve?.Value ?? 20;

	private static int FoodTarget => ModConfig.CookFoodTarget?.Value ?? 20;

	private static int BaseTarget => ModConfig.CookMeadBaseTarget?.Value ?? 10;

	private static int Batch => ModConfig.CookBatchSize?.Value ?? 5;

	private static int MeadTarget => ModConfig.CookMeadTarget?.Value ?? 20;

	public void NotifyActionModeChanged()
	{
		if (_phase != CookPhase.Idle)
		{
			Abort("mode changed");
		}
	}

	internal bool IsCookMode()
	{
		ZDO zDO = _nview?.GetZDO();
		return zDO != null && zDO.GetInt(CompanionSetup.ActionModeHash) == 17;
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
	}

	private void OnDestroy()
	{
		Unclaim();
		CloseChest();
	}

	private void Update()
	{
		if (CompanionRest.MHR_ShouldPauseWork(this) || _nview == null || _nview.GetZDO() == null || !_nview.IsOwner() || _character == null || _character.GetHealth() <= 0f)
		{
			return;
		}
		if (!_ledgerLoaded)
		{
			LoadLedger();
		}
		if ((_ai != null && _ai.IsInCombat) || CompanionInteractPanel.IsOpenFor(_setup) || CompanionRadialMenu.IsOpenFor(_setup))
		{
			if (_phase != CookPhase.Idle)
			{
				Abort("interrupted");
			}
			return;
		}
		if (!IsCookMode())
		{
			if (_phase != CookPhase.Idle)
			{
				Abort("mode changed");
			}
			return;
		}
		float dt = Time.deltaTime;
		try
		{
			switch (_phase)
			{
			case CookPhase.Idle:
				UpdateIdle(dt);
				break;
			case CookPhase.Fetching:
				UpdateFetching(dt);
				break;
			case CookPhase.MovingToStation:
				UpdateMovingToStation(dt);
				break;
			case CookPhase.Crafting:
				UpdateCrafting(dt);
				break;
			case CookPhase.Cooking:
				UpdateCooking(dt);
				break;
			case CookPhase.Fermenting:
				UpdateFermenting(dt);
				break;
			case CookPhase.CollectingDrops:
				UpdateCollecting(dt);
				break;
			case CookPhase.Storing:
				UpdateStoring(dt);
				break;
			}
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Cook] " + ex.Message + " - starting over");
			Abort("error");
		}
	}

	// ---------------------------------------------------------------- deciding what to do

	private void UpdateIdle(float dt)
	{
		_scanTimer -= dt;
		if (_scanTimer > 0f)
		{
			return;
		}
		_scanTimer = ScanInterval;
		CookCatalog.Ensure();
		Scan();
		Task task = PickTask();
		if (task == null)
		{
			if (!_allDoneNotified)
			{
				_allDoneNotified = true;
				Log("Nothing to cook right now");
				RestoreFollow();
			}
			_scanTimer = 10f;
			return;
		}
		_allDoneNotified = false;
		Begin(task);
	}

	private Task PickTask()
	{
		// 1) things that won't wait: finished food about to burn, finished fermenters
		foreach (CookingStation cooker in _cookers)
		{
			if (CountSlots(cooker, 1) + CountSlots(cooker, 2) > 0 && Usable(cooker))
			{
				return new Task { Kind = TaskKind.Rescue, Cooker = cooker };
			}
		}
		foreach (Fermenter fermenter in _fermenters)
		{
			if (!s_claimed.Contains(fermenter.GetInstanceID()) && FermenterState(fermenter) == 2 && Usable(fermenter))
			{
				return new Task { Kind = TaskKind.Tap, Fermenter = fermenter };
			}
		}
		// 2) a full bag of things it made: put them away first
		int carried = LedgerCarried();
		if (carried >= 10)
		{
			Task task = StoreTask();
			if (task != null)
			{
				return task;
			}
		}
		// 3) mead: fill an empty fermenter, brew a focus base
		Task task2 = FillTask() ?? BrewTask();
		if (task2 != null)
		{
			return task2;
		}
		// 4) the best food it can make
		Task task3 = FoodTask();
		if (task3 != null)
		{
			return task3;
		}
		// 5) nothing to make: put away what it made
		return (carried > 0) ? StoreTask() : null;
	}

	private Task FoodTask()
	{
		Task best = null;
		float bestScore = 0f;
		// crafted at the cauldron / prep table
		foreach (Recipe recipe in CookCatalog.Recipes)
		{
			string output = recipe.m_item.gameObject.name;
			string final = CookCatalog.FinalFood(output);
			if (final == null || CookCatalog.IsMeadBase(output) || !CookCatalog.Allowed(final) || !Known(recipe))
			{
				continue;
			}
			float score = CookCatalog.Value(final);
			if (score <= bestScore)
			{
				continue;
			}
			int stock = Count(final) + ((final != output) ? Count(output) : 0);
			if (stock >= FoodTarget)
			{
				continue;
			}
			// a prep-table item needs a cooking station in range that finishes it
			if (final != output && FindCooker(output, needFree: false) == null)
			{
				continue;
			}
			CraftingStation station = FindStation(recipe);
			if (station == null || !IngredientsOk(recipe))
			{
				continue;
			}
			best = CraftTask(recipe, station, final, FoodTarget - stock);
			bestScore = score;
		}
		// on a cooking station / oven
		foreach (CookingStation cooker in _cookers)
		{
			if (!Usable(cooker) || CountSlots(cooker, -1) == 0 || !HasHeat(cooker, orFuelInReach: true))
			{
				continue;
			}
			foreach (CookingStation.ItemConversion conv in cooker.m_conversion)
			{
				if (conv?.m_from == null || conv.m_to == null)
				{
					continue;
				}
				string from = conv.m_from.gameObject.name;
				string to = conv.m_to.gameObject.name;
				if (!CookCatalog.Allowed(to))
				{
					continue;
				}
				// finishing something already prepared beats starting something new of the same worth
				float score = CookCatalog.Value(to) + (CookCatalog.IsMade(from) ? 0.5f : 0f);
				if (score <= bestScore)
				{
					continue;
				}
				int room = FoodTarget - Count(to);
				int have = Usable(from);
				int n = Mathf.Min(Mathf.Min(CountSlots(cooker, -1), Batch), Mathf.Min(room, have));
				if (n <= 0)
				{
					continue;
				}
				Task task = new Task
				{
					Kind = TaskKind.Grill,
					Cooker = cooker,
					Item = from,
					Final = to,
					Count = n
				};
				AddFetch(task, from, n);
				if (cooker.m_useFuel && cooker.m_fuelItem != null)
				{
					int fuelNeed = Mathf.Clamp(3 - Mathf.FloorToInt(Fuel(cooker)), 0, 3);
					int fuelHave = Count(cooker.m_fuelItem.gameObject.name);
					if (fuelNeed > 0 && fuelHave > 0)
					{
						AddFetch(task, cooker.m_fuelItem.gameObject.name, Mathf.Min(fuelNeed, fuelHave));
					}
				}
				best = task;
				bestScore = score;
			}
		}
		return best;
	}

	private Task BrewTask()
	{
		foreach (string mead in CookCatalog.Focus())
		{
			Recipe recipe = CookCatalog.RecipeFor(mead);
			if (recipe == null || !Known(recipe))
			{
				continue;
			}
			string finished = CookCatalog.MeadOf(mead);
			if (Count(mead) >= BaseTarget || (finished != null && Count(finished) >= MeadTarget))
			{
				continue;
			}
			CraftingStation station = FindStation(recipe);
			if (station != null && IngredientsOk(recipe))
			{
				return CraftTask(recipe, station, mead, BaseTarget - Count(mead));
			}
		}
		return null;
	}

	private Task FillTask()
	{
		Inventory bag = Bag;
		List<string> list = new List<string>(CookCatalog.Focus());
		if (bag != null)
		{
			foreach (ItemDrop.ItemData item in bag.GetAllItems())
			{
				string text = item?.m_dropPrefab?.name;
				if (text != null && CookCatalog.IsMeadBase(text) && !list.Contains(text))
				{
					list.Add(text);
				}
			}
		}
		foreach (string mead in list)
		{
			string finished = CookCatalog.MeadOf(mead);
			if (finished == null || Count(finished) >= MeadTarget || Count(mead) <= 0)
			{
				continue;
			}
			foreach (Fermenter fermenter in _fermenters)
			{
				if (s_claimed.Contains(fermenter.GetInstanceID()) || FermenterState(fermenter) != 0 || !Usable(fermenter) || !Accepts(fermenter, mead))
				{
					continue;
				}
				Task task = new Task
				{
					Kind = TaskKind.Fill,
					Fermenter = fermenter,
					Item = mead
				};
				AddFetch(task, mead, 1);
				return task;
			}
		}
		return null;
	}

	// 1.4.87: a batch (Cooking.BatchSize), as many as the target has room for and the ingredients allow
	private Task CraftTask(Recipe recipe, CraftingStation station, string final, int room)
	{
		int per = Mathf.Max(1, recipe.m_amount);
		int times = Mathf.Min(Batch, Mathf.Max(1, (room + per - 1) / per));
		foreach (Piece.Requirement req in recipe.m_resources)
		{
			if (req?.m_resItem != null && req.GetAmount(1) > 0)
			{
				times = Mathf.Min(times, Usable(req.m_resItem.gameObject.name) / req.GetAmount(1));
			}
		}
		times = Mathf.Max(1, times);
		Task task = new Task
		{
			Kind = TaskKind.Craft,
			Recipe = recipe,
			Station = station,
			Item = recipe.m_item.gameObject.name,
			Final = final,
			Count = times
		};
		foreach (Piece.Requirement req2 in recipe.m_resources)
		{
			if (req2?.m_resItem != null && req2.GetAmount(1) > 0)
			{
				AddFetch(task, req2.m_resItem.gameObject.name, req2.GetAmount(1) * times);
			}
		}
		return task;
	}

	private void AddFetch(Task task, string item, int amount)
	{
		int inBag = HomesteadController.CountItemInInventory(Bag, item);
		if (amount > inBag)
		{
			task.Fetch.Add(new KeyValuePair<string, int>(item, amount));
		}
	}

	private Task StoreTask()
	{
		Inventory bag = Bag;
		if (bag == null)
		{
			return null;
		}
		foreach (KeyValuePair<string, int> item in _ledger)
		{
			if (item.Value <= 0 || HomesteadController.CountItemInInventory(bag, item.Key) <= 0)
			{
				continue;
			}
			Container container = StoreChest(item.Key);
			if (container != null)
			{
				return new Task
				{
					Kind = TaskKind.Store,
					Item = item.Key
				};
			}
		}
		return null;
	}

	// ---------------------------------------------------------------- running a task

	private void Begin(Task task)
	{
		_task = task;
		_fetchIndex = 0;
		_chest = null;
		_approachKey = null;
		if (_ai != null)
		{
			_ai.SetFollowTarget(null);
		}
		if (task.Fermenter != null)
		{
			Claim(task.Fermenter);
		}
		Log("Going to " + task.Describe());
		Say(task.Kind);
		if (task.Kind == TaskKind.Store)
		{
			_chest = StoreChest(task.Item);
			Enter(CookPhase.Storing);
		}
		else if (task.Fetch.Count > 0)
		{
			Enter(CookPhase.Fetching);
		}
		else
		{
			Enter(CookPhase.MovingToStation);
		}
	}

	private void Enter(CookPhase phase)
	{
		_phase = phase;
		_phaseStart = Time.time;
		_timer = 0f;
		_approachKey = null;
		ResetStuck();
	}

	private void UpdateFetching(float dt)
	{
		Inventory bag = Bag;
		if (bag == null)
		{
			Abort("no inventory");
			return;
		}
		while (_fetchIndex < _task.Fetch.Count && HomesteadController.CountItemInInventory(bag, _task.Fetch[_fetchIndex].Key) >= _task.Fetch[_fetchIndex].Value)
		{
			_fetchIndex++;
			_chest = null;
		}
		if (_fetchIndex >= _task.Fetch.Count)
		{
			CloseChest();
			Enter(CookPhase.MovingToStation);
			return;
		}
		string item = _task.Fetch[_fetchIndex].Key;
		int need = _task.Fetch[_fetchIndex].Value - HomesteadController.CountItemInInventory(bag, item);
		if (_chest == null)
		{
			_chest = ChestWith(item);
			if (_chest == null)
			{
				Abort("no chest in reach has " + Name(item));
				return;
			}
		}
		if (!AtChest(_chest, dt, "chest with " + Name(item)))
		{
			return;
		}
		if (!OpenChest(_chest, dt))
		{
			return;
		}
		Inventory inv = _chest.GetInventory();
		int taken = 0;
		while (taken < need)
		{
			ItemDrop.ItemData itemData = HomesteadController.FindItemByPrefab(inv, item);
			if (itemData == null || !HomesteadController.TransferOne(inv, bag, itemData))
			{
				break;
			}
			taken++;
		}
		Log($"Took {taken}x {Name(item)}");
		CloseChest();
		if (taken == 0)
		{
			// someone emptied it on the way: try another chest
			_unreachable.Add(_chest.GetInstanceID());
		}
		_chest = null;
		_approachKey = null;
	}

	private void UpdateMovingToStation(float dt)
	{
		Component target = TaskTarget();
		if (target == null)
		{
			Abort("station gone");
			return;
		}
		Vector3 point = TaskPoint();
		if (!AtPoint(target, point, dt, Loc(TaskName())))
		{
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(target.transform.position);
		_cookArea = target.transform.position;
		_hasCookArea = true;
		switch (_task.Kind)
		{
		case TaskKind.Craft:
			if (!StationReady(_task.Station, out var why))
			{
				_stationProblemUntil[_task.Station.GetInstanceID()] = Time.time + 60f;
				Abort("the " + Loc(_task.Station.m_name) + " " + why);
				return;
			}
			_task.Station.PokeInUse();
			_zanim?.SetTrigger("interact");
			_task.Station.m_craftItemEffects?.Create(_task.Station.transform.position, Quaternion.identity);
			Enter(CookPhase.Crafting);
			break;
		case TaskKind.Grill:
			AddToCooker();
			Enter(CookPhase.Cooking);
			break;
		case TaskKind.Rescue:
			Enter(CookPhase.Cooking);
			break;
		case TaskKind.Fill:
		case TaskKind.Tap:
			_zanim?.SetTrigger("interact");
			Enter(CookPhase.Fermenting);
			break;
		default:
			Finish();
			break;
		}
	}

	private void UpdateCrafting(float dt)
	{
		CraftingStation station = _task.Station;
		if (station == null)
		{
			Abort("station gone");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(station.transform.position);
		station.PokeInUse();
		_timer += dt;
		if (_timer < 2.5f)
		{
			return;
		}
		Inventory bag = Bag;
		Recipe recipe = _task.Recipe;
		foreach (Piece.Requirement req in recipe.m_resources)
		{
			if (req?.m_resItem != null && HomesteadController.CountItemInInventory(bag, req.m_resItem.gameObject.name) < req.GetAmount(1))
			{
				Abort("missing " + Name(req.m_resItem.gameObject.name));
				return;
			}
		}
		foreach (Piece.Requirement req2 in recipe.m_resources)
		{
			if (req2?.m_resItem != null)
			{
				for (int i = 0; i < req2.GetAmount(1); i++)
				{
					HomesteadController.ConsumeOneFromInventory(bag, req2.m_resItem.gameObject.name);
				}
			}
		}
		string output = recipe.m_item.gameObject.name;
		int amount = Mathf.Max(1, recipe.m_amount);
		if (bag.AddItem(output, amount, 1, 0, 0L, "", cheated: false) == null)
		{
			GameObject prefab = ObjectDB.instance?.GetItemPrefab(output);
			if (prefab != null)
			{
				ItemDrop drop = UnityEngine.Object.Instantiate(prefab, base.transform.position + Vector3.up * 0.5f, Quaternion.identity).GetComponent<ItemDrop>();
				if (drop != null)
				{
					drop.m_itemData.m_stack = amount;
				}
			}
			Log("Bag full - dropped " + Name(output));
		}
		AddLedger(output, amount);
		station.m_craftItemDoneEffects?.Create(station.transform.position, Quaternion.identity);
		Log($"Made {amount}x {Name(output)} at the {Loc(station.m_name)}");
		// the rest of the batch
		_task.Count--;
		if (_task.Count > 0 && CanCraftAgain(recipe))
		{
			_timer = 0.5f;
			_zanim?.SetTrigger("interact");
			station.m_craftItemEffects?.Create(station.transform.position, Quaternion.identity);
			return;
		}
		// a fresh mead base goes straight into an empty fermenter, if one wants it
		if (CookCatalog.IsMeadBase(output))
		{
			Task task = FillTask();
			if (task != null && task.Item == output)
			{
				Begin(task);
				return;
			}
		}
		Finish();
	}

	private bool CanCraftAgain(Recipe recipe)
	{
		foreach (Piece.Requirement req in recipe.m_resources)
		{
			if (req?.m_resItem != null && HomesteadController.CountItemInInventory(Bag, req.m_resItem.gameObject.name) < req.GetAmount(1))
			{
				return false;
			}
		}
		return true;
	}

	private void AddToCooker()
	{
		CookingStation cooker = _task.Cooker;
		Inventory bag = Bag;
		ZNetView nv = cooker.GetComponent<ZNetView>();
		if (bag == null || nv == null || !nv.IsValid())
		{
			return;
		}
		if (cooker.m_useFuel && cooker.m_fuelItem != null)
		{
			string fuel = cooker.m_fuelItem.gameObject.name;
			int added = 0;
			while (Fuel(cooker) + added < cooker.m_maxFuel && added < 3 && HomesteadController.ConsumeOneFromInventory(bag, fuel))
			{
				nv.InvokeRPC("RPC_AddFuel");
				added++;
			}
			if (added > 0)
			{
				Log($"Added {added}x {Name(fuel)} to the {Loc(cooker.m_name)}");
			}
		}
		int put = 0;
		int free = CountSlots(cooker, -1);
		while (put < _task.Count && put < free && HomesteadController.ConsumeOneFromInventory(bag, _task.Item))
		{
			nv.InvokeRPC("RPC_AddItem", _task.Item, false);
			put++;
		}
		_zanim?.SetTrigger("interact");
		Log($"Put {put}x {Name(_task.Item)} on the {Loc(cooker.m_name)}");
	}

	// stand by the station, take each item off when it's done (before it burns), pick it up
	private void UpdateCooking(float dt)
	{
		CookingStation cooker = _task.Cooker;
		if (cooker == null)
		{
			Abort("cooking station gone");
			return;
		}
		ZNetView nv = cooker.GetComponent<ZNetView>();
		_ai?.StopMoving();
		_ai?.LookAtPoint(cooker.transform.position);
		_timer -= dt;
		if (_timer > 0f)
		{
			return;
		}
		_timer = 0.5f;
		PickUpDrops(DropPoint(cooker), 4f, ledger: true);
		if (CountSlots(cooker, 1) + CountSlots(cooker, 2) > 0)
		{
			nv?.InvokeRPC("RPC_RemoveDoneItem", base.transform.position, 1);
			_zanim?.SetTrigger("interact");
			_timer = 0.8f;
			return;
		}
		bool waiting = _task.Kind == TaskKind.Grill && CountCooking(cooker, _task.Item) > 0;
		if (waiting && Time.time - _phaseStart < 240f)
		{
			if (Time.time >= _nextWatchLog)
			{
				_nextWatchLog = Time.time + 20f;
				Log("Waiting for the " + Loc(cooker.m_name) + " (" + CountCooking(cooker, _task.Item) + " cooking)");
			}
			return;
		}
		PickUpDrops(DropPoint(cooker), 4f, ledger: true);
		Finish();
	}

	private void UpdateFermenting(float dt)
	{
		Fermenter fermenter = _task.Fermenter;
		if (fermenter == null)
		{
			Abort("fermenter gone");
			return;
		}
		_ai?.StopMoving();
		_ai?.LookAtPoint(fermenter.transform.position);
		_timer += dt;
		if (_timer < 0.6f)
		{
			return;
		}
		ZNetView nv = fermenter.GetComponent<ZNetView>();
		if (_task.Kind == TaskKind.Fill)
		{
			if (FermenterState(fermenter) == 0 && HomesteadController.ConsumeOneFromInventory(Bag, _task.Item))
			{
				nv?.InvokeRPC("RPC_AddItem", _task.Item);
				TakeFromLedger(_task.Item, 1);
				Log("Put " + Name(_task.Item) + " in the fermenter");
			}
			Finish();
			return;
		}
		if (FermenterState(fermenter) == 2)
		{
			nv?.InvokeRPC("RPC_Tap");
			Log("Tapped the fermenter");
		}
		Enter(CookPhase.CollectingDrops);
	}

	private void UpdateCollecting(float dt)
	{
		_timer += dt;
		if (_timer < 2f)
		{
			return;
		}
		Fermenter fermenter = _task.Fermenter;
		Vector3 center = (fermenter != null && fermenter.m_outputPoint != null) ? fermenter.m_outputPoint.position : base.transform.position;
		PickUpDrops(center, 4f, ledger: true);
		Finish();
	}

	private void UpdateStoring(float dt)
	{
		Inventory bag = Bag;
		if (_chest == null || bag == null)
		{
			Finish();
			return;
		}
		if (!AtChest(_chest, dt, "chest to store " + Name(_task.Item)))
		{
			return;
		}
		if (!OpenChest(_chest, dt))
		{
			return;
		}
		Inventory inv = _chest.GetInventory();
		int stored = 0;
		// everything it made that fits here, starting with the item it came for
		List<string> items = new List<string> { _task.Item };
		foreach (string key in _ledger.Keys)
		{
			if (!items.Contains(key) && (HasItem(inv, key) || key == _task.Item))
			{
				items.Add(key);
			}
		}
		foreach (string item in items)
		{
			int want = Mathf.Min(LedgerOf(item), HomesteadController.CountItemInInventory(bag, item));
			int moved = 0;
			while (moved < want)
			{
				ItemDrop.ItemData itemData = HomesteadController.FindItemByPrefab(bag, item);
				if (itemData == null || !inv.CanAddItem(itemData, 1) || !HomesteadController.TransferOne(bag, inv, itemData))
				{
					break;
				}
				moved++;
			}
			if (moved > 0)
			{
				TakeFromLedger(item, moved);
				stored += moved;
				Log($"Put away {moved}x {Name(item)}");
			}
		}
		CloseChest();
		if (stored == 0)
		{
			// nothing fitted: don't come back to this chest for a while
			_unreachable.Add(_chest.GetInstanceID());
		}
		_chest = null;
		Finish();
	}

	// ---------------------------------------------------------------- moving

	private Component TaskTarget()
	{
		switch (_task.Kind)
		{
		case TaskKind.Craft:
			return _task.Station;
		case TaskKind.Grill:
		case TaskKind.Rescue:
			return _task.Cooker;
		case TaskKind.Fill:
		case TaskKind.Tap:
			return _task.Fermenter;
		default:
			return null;
		}
	}

	private string TaskName()
	{
		Component component = TaskTarget();
		if (component is CraftingStation craftingStation)
		{
			return craftingStation.m_name;
		}
		if (component is CookingStation cookingStation)
		{
			return cookingStation.m_name;
		}
		return "$piece_fermenter";
	}

	// where to stand: by the cooking station's food slot, by the fermenter's tap, by the crafting station
	private Vector3 TaskPoint()
	{
		if (_task.Cooker != null && (_task.Kind == TaskKind.Grill || _task.Kind == TaskKind.Rescue))
		{
			return (_task.Cooker.m_addFoodSwitch != null) ? _task.Cooker.m_addFoodSwitch.transform.position : _task.Cooker.transform.position;
		}
		if (_task.Fermenter != null && (_task.Kind == TaskKind.Fill || _task.Kind == TaskKind.Tap))
		{
			return (_task.Fermenter.m_outputPoint != null) ? _task.Fermenter.m_outputPoint.position : _task.Fermenter.transform.position;
		}
		return TaskTarget().transform.position;
	}

	private bool AtPoint(Component target, Vector3 point, float dt, string what)
	{
		string text = "p" + target.GetInstanceID();
		if (_approachKey != text)
		{
			_approachKey = text;
			_hasApproach = MHR_Chests.PickApproachPoint(base.transform.position, point, target, out _approachSpot);
		}
		Vector3 position = base.transform.position;
		float num = MHR_Nav.ReachDist(position, point);
		if (num < 2f || (_hasApproach && OnSpot(num)))
		{
			return true;
		}
		Vector3 goal = _hasApproach ? _approachSpot : point;
		MHR_Nav.Move(_ai, dt, goal, _hasApproach ? 0.5f : 2f, FarEnough(num), "Cook");
		CheckStuck(dt, num, what);
		return false;
	}

	private bool AtChest(Container chest, float dt, string what)
	{
		string text = "c" + chest.GetInstanceID();
		if (_approachKey != text)
		{
			_approachKey = text;
			_hasApproach = MHR_Chests.PickApproach(base.transform.position, chest, out _approachSpot);
			if (!_hasApproach && !MHR_Chests.CanReach(base.transform.position, chest))
			{
				_unreachable.Add(chest.GetInstanceID());
				Log($"Skipping chest at {chest.transform.position:F1} - can't get within reach of it");
				Abort("chest out of reach");
				return false;
			}
		}
		float num = MHR_Nav.ReachDist(base.transform.position, chest.transform.position);
		if (num < UseReach || (_hasApproach && OnSpot(num)))
		{
			_ai?.StopMoving();
			return true;
		}
		Vector3 goal = _hasApproach ? _approachSpot : chest.transform.position;
		MHR_Nav.Move(_ai, dt, goal, _hasApproach ? 0.5f : UseReach, FarEnough(num), "Cook");
		CheckStuck(dt, num, what);
		return false;
	}

	private bool OnSpot(float dist)
	{
		Vector3 position = base.transform.position;
		return dist <= UseReach + 1.5f && new Vector2(position.x - _approachSpot.x, position.z - _approachSpot.z).magnitude <= 0.7f && Mathf.Abs(position.y - _approachSpot.y) <= 1.2f;
	}

	private static bool FarEnough(float dist)
	{
		float num = ModConfig.WorkRunDistance?.Value ?? 25f;
		return num > 0f && dist > num;
	}

	private void ResetStuck()
	{
		_stuckTimer = 0f;
		_stuckCheckTimer = 0f;
		_stuckBestDist = float.MaxValue;
	}

	private void CheckStuck(float dt, float dist, string what)
	{
		_stuckCheckTimer += dt;
		if (_stuckCheckTimer >= 1f)
		{
			if (_stuckBestDist - dist < 0.3f && !(_ai != null && MHR_Mover.Progressing(_ai)))
			{
				_stuckTimer += _stuckCheckTimer;
			}
			else
			{
				_stuckBestDist = Mathf.Min(_stuckBestDist, dist);
				_stuckTimer = 0f;
			}
			_stuckCheckTimer = 0f;
		}
		if ((_ai != null && MHR_Mover.GaveUp(_ai)) || _stuckTimer > 12f)
		{
			Abort("couldn't get to the " + what);
		}
	}

	// ---------------------------------------------------------------- chests

	private bool OpenChest(Container chest, float dt)
	{
		if (!_chestOpen)
		{
			chest.SetInUse(inUse: true);
			_chestOpen = true;
			_zanim?.SetTrigger("interact");
			_ai?.LookAtPoint(chest.transform.position);
			_timer = 0.8f;
			return false;
		}
		_timer -= dt;
		return _timer <= 0f;
	}

	private void CloseChest()
	{
		if (_chestOpen && _chest != null)
		{
			_chest.SetInUse(inUse: false);
		}
		_chestOpen = false;
	}

	private Container ChestWith(string item)
	{
		Container best = null;
		float bestDist = float.MaxValue;
		foreach (Container chest in _chests)
		{
			if (chest == null || chest.IsInUse() || _unreachable.Contains(chest.GetInstanceID()) || !HasItem(chest.GetInventory(), item))
			{
				continue;
			}
			float num = Vector3.Distance(base.transform.position, chest.transform.position);
			if (num < bestDist)
			{
				best = chest;
				bestDist = num;
			}
		}
		return best;
	}

	// like emptying bags: a chest that already has this food; else one with other cooked food; else the one nearest the
	// cooking area (Cooking.StoreWithCookedFood off: the nearest chest with room)
	private Container StoreChest(string item)
	{
		GameObject prefab = ObjectDB.instance?.GetItemPrefab(item);
		ItemDrop.ItemData sample = prefab?.GetComponent<ItemDrop>()?.m_itemData;
		if (sample == null)
		{
			return null;
		}
		bool smart = ModConfig.CookStoreWithCookedFood?.Value ?? true;
		Vector3 near = _hasCookArea ? _cookArea : base.transform.position;
		Container same = null;
		Container cooked = null;
		Container nearest = null;
		float d1 = float.MaxValue;
		float d2 = float.MaxValue;
		float d3 = float.MaxValue;
		foreach (Container chest in _chests)
		{
			if (chest == null || chest.IsInUse() || _unreachable.Contains(chest.GetInstanceID()))
			{
				continue;
			}
			Inventory inv = chest.GetInventory();
			if (inv == null || !inv.CanAddItem(sample, 1))
			{
				continue;
			}
			float num = Vector3.Distance(near, chest.transform.position);
			if (smart && HasItem(inv, item) && num < d1)
			{
				same = chest;
				d1 = num;
			}
			else if (smart && HasCookedFood(inv) && num < d2)
			{
				cooked = chest;
				d2 = num;
			}
			else if (inv.GetEmptySlots() > 0 && num < d3)
			{
				nearest = chest;
				d3 = num;
			}
		}
		return same ?? cooked ?? nearest;
	}

	private static bool HasItem(Inventory inv, string item)
	{
		return inv != null && HomesteadController.CountItemInInventory(inv, item) > 0;
	}

	private static bool HasCookedFood(Inventory inv)
	{
		foreach (ItemDrop.ItemData item in inv.GetAllItems())
		{
			string text = item?.m_dropPrefab?.name;
			if (text != null && CookCatalog.IsMade(text))
			{
				return true;
			}
		}
		return false;
	}

	// ---------------------------------------------------------------- counting

	private Inventory Bag => _humanoid?.GetInventory();

	// in the chests in range and the bag
	private int Count(string item)
	{
		int num = HomesteadController.CountItemInInventory(Bag, item);
		foreach (Container chest in _chests)
		{
			if (chest != null)
			{
				num += HomesteadController.CountItemInInventory(chest.GetInventory(), item);
			}
		}
		return num;
	}

	// how many of an ingredient may be used: raw ones keep Cooking.IngredientReserve back
	private int Usable(string item)
	{
		int num = Count(item);
		return CookCatalog.IsMade(item) ? num : (num - Reserve);
	}

	private bool IngredientsOk(Recipe recipe)
	{
		foreach (Piece.Requirement req in recipe.m_resources)
		{
			if (req?.m_resItem != null && req.GetAmount(1) > 0 && Usable(req.m_resItem.gameObject.name) < req.GetAmount(1))
			{
				return false;
			}
		}
		return true;
	}

	private static bool Known(Recipe recipe)
	{
		Player localPlayer = Player.m_localPlayer;
		if (localPlayer == null)
		{
			return true;
		}
		string text = recipe.m_item?.m_itemData?.m_shared?.m_name;
		return text != null && localPlayer.IsRecipeKnown(text);
	}

	// ---------------------------------------------------------------- stations

	private void Scan()
	{
		if (Time.time >= _unreachableClearAt)
		{
			_unreachable.Clear();
			_unreachableClearAt = Time.time + 60f;
		}
		_chests.Clear();
		_stations.Clear();
		_cookers.Clear();
		_fermenters.Clear();
		_pieces.Clear();
		Vector3 position = base.transform.position;
		Piece.GetAllPiecesInRadius(position, Range, _pieces);
		foreach (Piece piece in _pieces)
		{
			if (piece == null)
			{
				continue;
			}
			Container container = piece.GetComponentInChildren<Container>();
			if (container != null && container.GetInventory() != null && container.gameObject != base.gameObject && !_chests.Contains(container))
			{
				_chests.Add(container);
			}
			CookingStation cookingStation = piece.GetComponent<CookingStation>();
			if (cookingStation != null && cookingStation.GetComponent<ZNetView>()?.GetZDO() != null)
			{
				_cookers.Add(cookingStation);
			}
			Fermenter fermenter = piece.GetComponent<Fermenter>();
			if (fermenter != null && fermenter.GetComponent<ZNetView>()?.GetZDO() != null)
			{
				_fermenters.Add(fermenter);
			}
			CraftingStation craftingStation = piece.GetComponent<CraftingStation>();
			if (craftingStation != null && CookCatalog.IsCookingStation(craftingStation.m_name))
			{
				_stations.Add(craftingStation);
			}
		}
	}

	private bool Usable(Component c)
	{
		if (c == null || (_ai != null && _ai.IsPositionBlacklisted(c.transform.position)))
		{
			return false;
		}
		float value;
		return !_stationProblemUntil.TryGetValue(c.GetInstanceID(), out value) || Time.time >= value;
	}

	private CraftingStation FindStation(Recipe recipe)
	{
		CraftingStation best = null;
		float bestDist = float.MaxValue;
		foreach (CraftingStation station in _stations)
		{
			if (station == null || recipe.m_craftingStation == null || station.m_name != recipe.m_craftingStation.m_name || station.GetLevel() < recipe.GetRequiredStationLevel(1) || !Usable(station) || !StationReady(station, out var _))
			{
				continue;
			}
			float num = Vector3.Distance(base.transform.position, station.transform.position);
			if (num < bestDist)
			{
				best = station;
				bestDist = num;
			}
		}
		return best;
	}

	// what a player needs to use it: a roof and a fire, where the station asks for them
	private static bool StationReady(CraftingStation station, out string why)
	{
		why = "";
		if (station.m_craftRequireRoof && station.m_roofCheckPoint != null)
		{
			Cover.GetCoverForPoint(station.m_roofCheckPoint.position, out var coverPercentage, out var underRoof);
			if (!underRoof || coverPercentage < 0.7f)
			{
				why = "needs a roof";
				return false;
			}
		}
		if (station.m_craftRequireFire && s_haveFire != null && !(bool)s_haveFire.GetValue(station))
		{
			why = "needs a fire";
			return false;
		}
		return true;
	}

	private CookingStation FindCooker(string from, bool needFree)
	{
		foreach (CookingStation cooker in _cookers)
		{
			if (cooker == null || !Usable(cooker) || (needFree && CountSlots(cooker, -1) == 0))
			{
				continue;
			}
			foreach (CookingStation.ItemConversion conv in cooker.m_conversion)
			{
				if (conv?.m_from != null && conv.m_from.gameObject.name == from)
				{
					return cooker;
				}
			}
		}
		return null;
	}

	private bool HasHeat(CookingStation cooker, bool orFuelInReach)
	{
		if (cooker.m_requireFire && s_isFireLit != null && (bool)s_isFireLit.Invoke(cooker, null))
		{
			return true;
		}
		if (cooker.m_useFuel)
		{
			return Fuel(cooker) >= 1f || (orFuelInReach && cooker.m_fuelItem != null && Count(cooker.m_fuelItem.gameObject.name) > 0);
		}
		return !cooker.m_requireFire;
	}

	private static float Fuel(CookingStation cooker)
	{
		return cooker.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(ZDOVars.s_fuel) ?? 0f;
	}

	// status: -1 = empty slots, 0 = cooking, 1 = done, 2 = burnt
	private static int CountSlots(CookingStation cooker, int status)
	{
		ZDO zDO = cooker.GetComponent<ZNetView>()?.GetZDO();
		if (zDO == null || cooker.m_slots == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < cooker.m_slots.Length; i++)
		{
			string text = zDO.GetString("slot" + i);
			if (status < 0)
			{
				if (text == "")
				{
					num++;
				}
			}
			else if (text != "" && zDO.GetInt("slotstatus" + i) == status)
			{
				num++;
			}
		}
		return num;
	}

	private static int CountCooking(CookingStation cooker, string from)
	{
		ZDO zDO = cooker.GetComponent<ZNetView>()?.GetZDO();
		if (zDO == null || cooker.m_slots == null)
		{
			return 0;
		}
		int num = 0;
		for (int i = 0; i < cooker.m_slots.Length; i++)
		{
			if (zDO.GetString("slot" + i) == from && zDO.GetInt("slotstatus" + i) == 0)
			{
				num++;
			}
		}
		return num;
	}

	private static Vector3 DropPoint(CookingStation cooker)
	{
		return (cooker.m_spawnPoint != null) ? cooker.m_spawnPoint.position : cooker.transform.position;
	}

	// 0 = empty, 1 = fermenting, 2 = ready
	private static int FermenterState(Fermenter fermenter)
	{
		ZDO zDO = fermenter.GetComponent<ZNetView>()?.GetZDO();
		if (zDO == null || string.IsNullOrEmpty(zDO.GetString(ZDOVars.s_content)))
		{
			return 0;
		}
		long @long = zDO.GetLong(ZDOVars.s_startTime, 0L);
		if (@long == 0L || ZNet.instance == null)
		{
			return 1;
		}
		return ((ZNet.instance.GetTime() - new DateTime(@long)).TotalSeconds > (double)fermenter.m_fermentationDuration) ? 2 : 1;
	}

	private static bool Accepts(Fermenter fermenter, string item)
	{
		foreach (Fermenter.ItemConversion conv in fermenter.m_conversion)
		{
			if (conv?.m_from != null && conv.m_from.gameObject.name == item)
			{
				return true;
			}
		}
		return false;
	}

	private void Claim(Fermenter f)
	{
		Unclaim();
		_claimedId = f.GetInstanceID();
		s_claimed.Add(_claimedId);
	}

	private void Unclaim()
	{
		if (_claimedId != 0)
		{
			s_claimed.Remove(_claimedId);
			_claimedId = 0;
		}
	}

	// ---------------------------------------------------------------- picking things up

	private void PickUpDrops(Vector3 center, float radius, bool ledger)
	{
		if (s_itemMask < 0)
		{
			s_itemMask = LayerMask.GetMask("item");
		}
		Inventory bag = Bag;
		if (bag == null)
		{
			return;
		}
		int num = Physics.OverlapSphereNonAlloc(center, radius, s_drops, s_itemMask);
		for (int i = 0; i < num; i++)
		{
			ItemDrop drop = s_drops[i]?.GetComponentInParent<ItemDrop>();
			ZNetView nv = drop?.GetComponent<ZNetView>();
			if (drop == null || drop.m_itemData == null || nv == null || nv.GetZDO() == null)
			{
				continue;
			}
			string text = drop.m_itemData.m_dropPrefab?.name ?? Utils.GetPrefabName(drop.gameObject);
			// only what came off the stations: cooked food, mead, coal from burnt food
			if (!CookCatalog.IsMade(text) && !CookCatalog.IsMead(text) && text != "Coal")
			{
				continue;
			}
			if (!bag.CanAddItem(drop.m_itemData, drop.m_itemData.m_stack))
			{
				continue;
			}
			int stack = drop.m_itemData.m_stack;
			bag.AddItem(drop.m_itemData);
			nv.ClaimOwnership();
			nv.Destroy();
			if (ledger)
			{
				AddLedger(text, stack);
			}
			Log($"Picked up {stack}x {Name(text)}");
		}
	}

	// ---------------------------------------------------------------- what it made (kept on the companion)

	private void LoadLedger()
	{
		_ledgerLoaded = true;
		_ledger.Clear();
		string text = _nview?.GetZDO()?.GetString(LedgerHash, "") ?? "";
		foreach (string part in text.Split(';'))
		{
			int num = part.LastIndexOf(':');
			if (num > 0 && int.TryParse(part.Substring(num + 1), out var result) && result > 0)
			{
				_ledger[part.Substring(0, num)] = result;
			}
		}
	}

	private void SaveLedger()
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (KeyValuePair<string, int> item in _ledger)
		{
			if (item.Value > 0)
			{
				stringBuilder.Append(item.Key).Append(':').Append(item.Value)
					.Append(';');
			}
		}
		_nview?.GetZDO()?.Set(LedgerHash, stringBuilder.ToString());
	}

	private void AddLedger(string item, int n)
	{
		_ledger[item] = LedgerOf(item) + n;
		SaveLedger();
	}

	private void TakeFromLedger(string item, int n)
	{
		int num = LedgerOf(item) - n;
		if (num > 0)
		{
			_ledger[item] = num;
		}
		else
		{
			_ledger.Remove(item);
		}
		SaveLedger();
	}

	private int LedgerOf(string item)
	{
		return _ledger.TryGetValue(item, out var value) ? value : 0;
	}

	// what it made and still carries (the ledger can't be more than what's in the bag)
	private int LedgerCarried()
	{
		Inventory bag = Bag;
		if (bag == null)
		{
			return 0;
		}
		int num = 0;
		List<string> list = null;
		foreach (KeyValuePair<string, int> item in _ledger)
		{
			int num2 = HomesteadController.CountItemInInventory(bag, item.Key);
			if (num2 < item.Value)
			{
				if (list == null)
				{
					list = new List<string>();
				}
				list.Add(item.Key);
			}
			num += Mathf.Min(num2, item.Value);
		}
		if (list != null)
		{
			// eaten, given away or dropped: forget what's no longer carried
			foreach (string item2 in list)
			{
				int num3 = HomesteadController.CountItemInInventory(bag, item2);
				if (num3 > 0)
				{
					_ledger[item2] = num3;
				}
				else
				{
					_ledger.Remove(item2);
				}
			}
			SaveLedger();
		}
		return num;
	}

	// ---------------------------------------------------------------- ending a task

	private void Finish()
	{
		Unclaim();
		CloseChest();
		_task = null;
		_phase = CookPhase.Idle;
		_scanTimer = 1f;
	}

	private void Abort(string reason)
	{
		Log("Stopped - " + reason + " (was " + (_task?.Describe() ?? "idle") + ")");
		Unclaim();
		CloseChest();
		_task = null;
		_chest = null;
		_phase = CookPhase.Idle;
		_scanTimer = 5f;
	}

	private void RestoreFollow()
	{
		if (_setup != null && _ai != null && _setup.GetFollow() && Player.m_localPlayer != null && _ai.GetFollowTarget() == null)
		{
			_ai.SetFollowTarget(Player.m_localPlayer.gameObject);
		}
	}

	private void Say(TaskKind kind)
	{
		if (_talk == null)
		{
			return;
		}
		string text;
		switch (kind)
		{
		case TaskKind.Craft:
		case TaskKind.Grill:
			text = "hc_speech_cook_cooking";
			break;
		case TaskKind.Fill:
			text = "hc_speech_cook_fermenter";
			break;
		case TaskKind.Tap:
			text = "hc_speech_cook_tapping";
			break;
		case TaskKind.Store:
			text = "hc_speech_cook_storing";
			break;
		default:
			return;
		}
		_talk.Say(ModLocalization.Loc(text));
	}

	private void Log(string msg)
	{
		CompanionsPlugin.Log.LogInfo("[Cook|" + (_character?.m_name ?? "?") + "] " + msg);
	}

	internal static string Name(string prefab)
	{
		if (string.IsNullOrEmpty(prefab))
		{
			return "?";
		}
		string text = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
		return (text != null) ? Loc(text) : prefab;
	}

	internal static string Loc(string token)
	{
		if (string.IsNullOrEmpty(token))
		{
			return "?";
		}
		return Localization.instance?.Localize(token) ?? token;
	}
}

// What can be cooked, from the game's own data (mods included): cauldron / prep table recipes, cooking station and oven
// conversions, fermenter conversions. Built once; also writes OfflineCompanionsReborn.Cooking.txt with the names to use
// in the Cooking settings.
internal static class CookCatalog
{
	internal static readonly List<Recipe> Recipes = new List<Recipe>();

	private static readonly Dictionary<string, string> s_convTo = new Dictionary<string, string>();

	private static readonly Dictionary<string, string> s_meadOf = new Dictionary<string, string>();

	private static readonly HashSet<string> s_meads = new HashSet<string>();

	private static readonly HashSet<string> s_made = new HashSet<string>();

	private static readonly HashSet<string> s_stationNames = new HashSet<string>();

	private static readonly Dictionary<string, Recipe> s_recipeFor = new Dictionary<string, Recipe>();

	private static bool s_built;

	private static string s_foodsKey;

	private static readonly HashSet<string> s_allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly HashSet<string> s_deny = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static bool s_all;

	private static string s_focusKey;

	private static string s_focusStage;

	private static readonly List<string> s_focus = new List<string>();

	private static readonly string[] s_stages = new string[8] { "start", "eikthyr", "gdking", "bonemass", "dragon", "goblinking", "queen", "fader" };

	internal static void Ensure()
	{
		if (s_built || ObjectDB.instance == null || ObjectDB.instance.m_recipes == null || ObjectDB.instance.m_recipes.Count == 0)
		{
			return;
		}
		s_built = true;
		try
		{
			foreach (Fermenter fermenter in Resources.FindObjectsOfTypeAll<Fermenter>())
			{
				if (fermenter?.m_conversion == null)
				{
					continue;
				}
				foreach (Fermenter.ItemConversion conv in fermenter.m_conversion)
				{
					if (conv?.m_from != null && conv.m_to != null)
					{
						s_meadOf[conv.m_from.gameObject.name] = conv.m_to.gameObject.name;
						s_meads.Add(conv.m_to.gameObject.name);
					}
				}
			}
			foreach (CookingStation cooker in Resources.FindObjectsOfTypeAll<CookingStation>())
			{
				if (cooker?.m_conversion == null)
				{
					continue;
				}
				foreach (CookingStation.ItemConversion conv2 in cooker.m_conversion)
				{
					if (conv2?.m_from != null && conv2.m_to != null)
					{
						s_convTo[conv2.m_from.gameObject.name] = conv2.m_to.gameObject.name;
						s_made.Add(conv2.m_to.gameObject.name);
					}
				}
			}
			foreach (Recipe recipe in ObjectDB.instance.m_recipes)
			{
				if (recipe == null || !recipe.m_enabled || recipe.m_item == null || recipe.m_craftingStation == null)
				{
					continue;
				}
				string name = recipe.m_item.gameObject.name;
				ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData?.m_shared;
				if (shared == null)
				{
					continue;
				}
				bool flag = Value(shared) > 0f || s_meadOf.ContainsKey(name) || (s_convTo.TryGetValue(name, out var value) && Value(value) > 0f);
				if (flag && !s_meads.Contains(name))
				{
					Recipes.Add(recipe);
					s_made.Add(name);
					s_stationNames.Add(recipe.m_craftingStation.m_name);
					if (!s_recipeFor.ContainsKey(name))
					{
						s_recipeFor[name] = recipe;
					}
				}
			}
			WriteReference();
			CompanionsPlugin.Log.LogInfo($"[Cook] {Recipes.Count} recipes, {s_convTo.Count} cooking station items, {s_meadOf.Count} mead bases");
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Cook] couldn't read the cooking recipes: " + ex.Message);
		}
	}

	internal static bool IsCookingStation(string stationName)
	{
		return s_stationNames.Contains(stationName);
	}

	internal static bool IsMade(string item)
	{
		return s_made.Contains(item);
	}

	internal static bool IsMeadBase(string item)
	{
		return s_meadOf.ContainsKey(item);
	}

	internal static bool IsMead(string item)
	{
		return s_meads.Contains(item);
	}

	internal static string MeadOf(string meadBase)
	{
		return s_meadOf.TryGetValue(meadBase, out var value) ? value : null;
	}

	internal static Recipe RecipeFor(string item)
	{
		return s_recipeFor.TryGetValue(item, out var value) ? value : null;
	}

	// the food a crafted item ends up as: itself, or what a cooking station / oven turns it into
	internal static string FinalFood(string item)
	{
		if (Value(item) > 0f && !IsMeadBase(item))
		{
			return item;
		}
		if (s_convTo.TryGetValue(item, out var value) && Value(value) > 0f)
		{
			return value;
		}
		return null;
	}

	internal static float Value(string item)
	{
		ItemDrop.ItemData.SharedData sharedData = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
		return (sharedData != null) ? Value(sharedData) : 0f;
	}

	private static float Value(ItemDrop.ItemData.SharedData s)
	{
		if (s.m_itemType != ItemDrop.ItemData.ItemType.Consumable)
		{
			return 0f;
		}
		return s.m_food + s.m_foodStamina + s.m_foodEitr;
	}

	internal static bool Allowed(string item)
	{
		string text = ModConfig.CookFoods?.Value ?? "*";
		if (text != s_foodsKey)
		{
			s_foodsKey = text;
			s_allow.Clear();
			s_deny.Clear();
			s_all = false;
			foreach (string part in text.Split(','))
			{
				string text2 = part.Trim();
				if (text2 == "*")
				{
					s_all = true;
				}
				else if (text2.StartsWith("-"))
				{
					s_deny.Add(text2.Substring(1).Trim());
				}
				else if (text2.Length > 0)
				{
					s_allow.Add(text2);
				}
			}
		}
		string text3 = CookController.Name(item);
		if (s_deny.Contains(item) || s_deny.Contains(text3))
		{
			return false;
		}
		return s_all || s_allow.Contains(item) || s_allow.Contains(text3);
	}

	// the mead bases to work on now: the MeadFocus entry for the last boss beaten (or the closest one before it)
	internal static List<string> Focus()
	{
		string text = ModConfig.CookMeadFocus?.Value ?? "";
		string stage = Stage();
		if (text == s_focusKey && stage == s_focusStage)
		{
			return s_focus;
		}
		s_focusKey = text;
		s_focusStage = stage;
		s_focus.Clear();
		Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string part in text.Split(';'))
		{
			int num = part.IndexOf(':');
			if (num > 0)
			{
				dictionary[part.Substring(0, num).Trim()] = part.Substring(num + 1);
			}
		}
		for (int num2 = Array.IndexOf(s_stages, stage); num2 >= 0; num2--)
		{
			if (!dictionary.TryGetValue(s_stages[num2], out var value))
			{
				continue;
			}
			foreach (string part2 in value.Split(','))
			{
				string text2 = part2.Trim();
				if (text2.Length > 0)
				{
					if (IsMeadBase(text2))
					{
						s_focus.Add(text2);
					}
					else
					{
						CompanionsPlugin.Log.LogWarning("[Cook] MeadFocus: \"" + text2 + "\" isn't a mead base (see OfflineCompanionsReborn.Cooking.txt)");
					}
				}
			}
			break;
		}
		CompanionsPlugin.Log.LogInfo("[Cook] Mead focus (" + stage + "): " + ((s_focus.Count > 0) ? string.Join(", ", s_focus) : "none"));
		return s_focus;
	}

	private static string Stage()
	{
		ZoneSystem instance = ZoneSystem.instance;
		if (instance == null)
		{
			return "start";
		}
		for (int num = s_stages.Length - 1; num > 0; num--)
		{
			if (instance.GetGlobalKey("defeated_" + s_stages[num]))
			{
				return s_stages[num];
			}
		}
		return "start";
	}

	private static void WriteReference()
	{
		try
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.AppendLine("Offline Companions Reborn - names for the [Cooking] settings. Written at every start from the game's own data, mods included.");
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("FOODS (Cooking > Foods)  name = in-game name  (health + stamina + eitr, where it's made)");
			SortedDictionary<string, string> sortedDictionary = new SortedDictionary<string, string>();
			foreach (Recipe recipe in Recipes)
			{
				string name = recipe.m_item.gameObject.name;
				if (!IsMeadBase(name) && Value(name) > 0f)
				{
					sortedDictionary[name] = $"{name} = {CookController.Name(name)}  ({Value(name):F0}, {CookController.Loc(recipe.m_craftingStation.m_name)})";
				}
			}
			foreach (KeyValuePair<string, string> item in s_convTo)
			{
				if (Value(item.Value) > 0f)
				{
					sortedDictionary[item.Value] = $"{item.Value} = {CookController.Name(item.Value)}  ({Value(item.Value):F0}, from {CookController.Name(item.Key)} on a cooking station / oven)";
				}
			}
			foreach (string value in sortedDictionary.Values)
			{
				stringBuilder.AppendLine("  " + value);
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("MEAD BASES (Cooking > MeadFocus)  name = in-game name -> mead");
			foreach (KeyValuePair<string, string> item2 in s_meadOf)
			{
				stringBuilder.AppendLine($"  {item2.Key} = {CookController.Name(item2.Key)} -> {CookController.Name(item2.Value)}");
			}
			stringBuilder.AppendLine();
			stringBuilder.AppendLine("MEADFOCUS STAGES (the last boss beaten): start, eikthyr, gdking (The Elder), bonemass, dragon (Moder), goblinking (Yagluth), queen (The Queen), fader (Fader)");
			File.WriteAllText(Path.Combine(Paths.ConfigPath, "OfflineCompanionsReborn.Cooking.txt"), stringBuilder.ToString());
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Cook] couldn't write OfflineCompanionsReborn.Cooking.txt: " + ex.Message);
		}
	}
}
