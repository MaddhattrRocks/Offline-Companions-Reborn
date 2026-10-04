using System.Collections.Generic;
using UnityEngine;

namespace Companions;

public class FishController : MonoBehaviour
{
	private enum FishState
	{
		Idle,
		FindingSpot,
		MovingToSpot,
		Casting,
		Waiting,
		Reeling,
		Collecting
	}

	private struct FishEntry
	{
		public string PickupPrefabName;

		public float Chance;
	}

	private const float ScanInterval = 5f;

	private const float StuckTimeout = 6f;

	private const float MinFishingDepth = 2f;

	private const float SpeechCooldown = 30f;

	private const int MaxConsecutiveMiss = 3;

	private ZNetView _nview;

	private CompanionAI _ai;

	private CompanionCombatAI _combatAI;

	private CompanionSetup _setup;

	private Humanoid _humanoid;

	private Character _character;

	private CompanionStamina _stamina;

	private CompanionTalk _talk;

	private ZSyncAnimation _zanim;

	private FishState _state;

	private float _stateTimer;

	private float _scanTimer;

	private float _stuckTimer;

	private int _stuckCount;

	private float _lastSpeechTime;

	private Vector3 _lastPos;

	private int _missCount;

	private bool _castInitiated;

	private Vector3 _shorePos;

	private Vector3 _waterPos;

	private WaterVolume _cachedWaterVolume;

	private string _currentBaitName;

	private static Dictionary<string, List<FishEntry>> s_baitFishMap;

	private static HashSet<string> s_baitNames;

	private static bool s_mapBuilt;

	private static float ScanRadius => ModConfig.FishScanRadius.Value;

	private static float WaitTimeMin => ModConfig.FishWaitTimeMin.Value;

	private static float WaitTimeMax => ModConfig.FishWaitTimeMax.Value;

	private static float ReelTimeMin => ModConfig.FishReelTimeMin.Value;

	private static float ReelTimeMax => ModConfig.FishReelTimeMax.Value;

	private static float HookChance => ModConfig.FishHookChance.Value;

	private static float MissChance => ModConfig.FishMissChance.Value;

	private static float ReelStamDrain => ModConfig.FishReelStaminaDrain.Value;

	public bool IsActive
	{
		get
		{
			if (GetMode() == 9)
			{
				return _state != FishState.Idle;
			}
			return false;
		}
	}

	public void NotifyActionModeChanged()
	{
		if (_state != FishState.Idle)
		{
			Log($"NotifyActionModeChanged — resetting from {_state}");
		}
		ResetToIdle();
		_lastSpeechTime = 0f;
		UnequipFishingRod();
	}

	private int GetMode()
	{
		return (_nview?.GetZDO())?.GetInt(CompanionSetup.ActionModeHash) ?? 0;
	}

	private void Log(string msg)
	{
		string text = _character?.m_name ?? "?";
		CompanionsPlugin.Log.LogInfo("[Fish|" + text + "] " + msg);
	}

	private void ResetToIdle()
	{
		_state = FishState.Idle;
		_stateTimer = 0f;
		_scanTimer = 0f;
		_missCount = 0;
		_stuckTimer = 0f;
		_stuckCount = 0;
		_castInitiated = false;
		_cachedWaterVolume = null;
		if (_setup != null && _setup.SuppressAutoEquip)
		{
			_setup.SuppressAutoEquip = false;
			_setup.SyncEquipmentToInventory();
		}
	}

	private void UnequipFishingRod()
	{
		if (!(_humanoid == null))
		{
			ItemDrop.ItemData currentWeapon = _humanoid.GetCurrentWeapon();
			if (currentWeapon != null && currentWeapon.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod)
			{
				_humanoid.UnequipItem(currentWeapon);
				Log("Unequipped fishing rod on mode change");
			}
		}
	}

	private void Awake()
	{
		_nview = GetComponent<ZNetView>();
		_ai = GetComponent<CompanionAI>();
		_combatAI = GetComponent<CompanionCombatAI>();
		_setup = GetComponent<CompanionSetup>();
		_humanoid = GetComponent<Humanoid>();
		_character = GetComponent<Character>();
		_stamina = GetComponent<CompanionStamina>();
		_talk = GetComponent<CompanionTalk>();
		_zanim = GetComponent<ZSyncAnimation>();
	}

	private void Update()
	{
		if (CompanionRest.MHR_ShouldPauseWork(this) || _nview == null || !_nview.IsOwner())
		{
			return;
		}
		int mode = GetMode();
		if (mode != 9)
		{
			if (_state != FishState.Idle)
			{
				ResetToIdle();
			}
		}
		else if (IsCompanionUIOpen())
		{
			_ai.StopMoving();
		}
		else if (!(_combatAI != null) || !_combatAI.IsEngaged)
		{
			switch (_state)
			{
			case FishState.Idle:
				UpdateIdle();
				break;
			case FishState.FindingSpot:
				UpdateFindingSpot();
				break;
			case FishState.MovingToSpot:
				UpdateMovingToSpot();
				break;
			case FishState.Casting:
				UpdateCasting();
				break;
			case FishState.Waiting:
				UpdateWaiting();
				break;
			case FishState.Reeling:
				UpdateReeling();
				break;
			case FishState.Collecting:
				UpdateCollecting();
				break;
			}
		}
	}

	private bool IsCompanionUIOpen()
	{
		if (!CompanionInteractPanel.IsOpenFor(_setup) && !CompanionRadialMenu.IsOpenFor(_setup))
		{
			return HomeZonePanel.IsOpenFor(_setup);
		}
		return true;
	}

	private void UpdateIdle()
	{
		_scanTimer -= Time.deltaTime;
		if (_scanTimer > 0f)
		{
			return;
		}
		_scanTimer = 5f;
		BuildBaitFishMapIfNeeded();
		ItemDrop.ItemData itemData = FindFishingRod();
		if (itemData == null)
		{
			SayThrottled(ModLocalization.Loc("hc_speech_fish_need_rod"));
			return;
		}
		ItemDrop.ItemData itemData2 = FindBait();
		if (itemData2 == null)
		{
			SayThrottled(ModLocalization.Loc("hc_speech_fish_need_bait"));
		}
		else
		{
			_state = FishState.FindingSpot;
		}
	}

	private void UpdateFindingSpot()
	{
		if (FindWaterSpot(out var shorePos, out var waterPos))
		{
			_shorePos = shorePos;
			_waterPos = waterPos;
			_lastPos = base.transform.position;
			_stuckTimer = 0f;
			_state = FishState.MovingToSpot;
			Log($"FindingSpot → MovingToSpot (shore={_shorePos:F1}, water={_waterPos:F1})");
		}
		else
		{
			SayThrottled(ModLocalization.Loc("hc_speech_fish_no_water"));
			_state = FishState.Idle;
			_scanTimer = 10f;
		}
	}

	private void UpdateMovingToSpot()
	{
		float num = Vector3.Distance(base.transform.position, _shorePos);
		if (num < 3f)
		{
			_ai.LookAtPoint(_waterPos);
			_state = FishState.Casting;
			_stateTimer = 0f;
			Log("MovingToSpot → Casting (arrived at shore)");
			return;
		}
		_ai.MoveToPoint(Time.deltaTime, _shorePos, 2f, run: true);
		_stuckTimer += Time.deltaTime;
		if (!(_stuckTimer >= 1f))
		{
			return;
		}
		_stuckTimer = 0f;
		float num2 = Vector3.Distance(base.transform.position, _lastPos);
		_lastPos = base.transform.position;
		if (num2 < 0.3f)
		{
			_stuckCount++;
			if (_stuckCount >= 5)
			{
				Log("MovingToSpot → FindingSpot (stuck)");
				_state = FishState.FindingSpot;
				_stuckCount = 0;
			}
		}
		else
		{
			_stuckCount = 0;
		}
	}

	private void UpdateCasting()
	{
		if (!_castInitiated)
		{
			ItemDrop.ItemData itemData = FindFishingRod();
			if (itemData == null)
			{
				Log("Casting → Idle (rod missing)");
				ResetToIdle();
				return;
			}
			ItemDrop.ItemData itemData2 = FindBait();
			if (itemData2 == null)
			{
				Log("Casting → Idle (bait missing)");
				ResetToIdle();
				return;
			}
			_currentBaitName = itemData2.m_dropPrefab?.name ?? "";
			if (_setup != null)
			{
				_setup.SuppressAutoEquip = true;
			}
			ItemDrop.ItemData currentWeapon = _humanoid.GetCurrentWeapon();
			if (currentWeapon == null || currentWeapon.m_shared.m_animationState != ItemDrop.ItemData.AnimationState.FishingRod)
			{
				_humanoid.EquipItem(itemData);
				return;
			}
			_ai.StopMoving();
			_ai.LookAtPoint(_waterPos);
			if (_ai.IsLookingAtPoint(_waterPos, 30f) && _humanoid.StartAttack(null, secondaryAttack: false))
			{
				Log("Casting — cast animation started");
				_castInitiated = true;
				Rigidbody rigidbody = _character?.GetComponent<Rigidbody>();
				if (rigidbody != null)
				{
					rigidbody.linearVelocity = Vector3.zero;
				}
			}
		}
		else
		{
			_ai.StopMoving();
			if (!_humanoid.InAttack())
			{
				_castInitiated = false;
				float num = (_stateTimer = Random.Range(WaitTimeMin, WaitTimeMax));
				_state = FishState.Waiting;
				Log($"Casting → Waiting ({num:F1}s)");
			}
		}
	}

	private void UpdateWaiting()
	{
		_ai.StopMoving();
		_ai.LookAtPoint(_waterPos);
		_stateTimer -= Time.deltaTime;
		if (_stateTimer > 0f)
		{
			return;
		}
		if (Random.value < MissChance)
		{
			_missCount++;
			if (_missCount >= 3)
			{
				_missCount = 0;
				_state = FishState.FindingSpot;
				Log("Waiting → FindingSpot (too many misses)");
			}
			else
			{
				_stateTimer = Random.Range(WaitTimeMin, WaitTimeMax);
				Log($"Waiting — miss #{_missCount}, waiting {_stateTimer:F1}s more");
			}
		}
		else if (Random.value > HookChance)
		{
			_talk?.Say(ModLocalization.Loc("hc_speech_fish_got_away"));
			_stateTimer = Random.Range(WaitTimeMin * 0.5f, WaitTimeMax * 0.5f);
			Log($"Waiting — hook failed, waiting {_stateTimer:F1}s more");
		}
		else
		{
			float num = (_stateTimer = Random.Range(ReelTimeMin, ReelTimeMax));
			_state = FishState.Reeling;
			_missCount = 0;
			Log($"Waiting → Reeling ({num:F1}s)");
		}
	}

	private void UpdateReeling()
	{
		_ai.StopMoving();
		_ai.LookAtPoint(_waterPos);
		_stateTimer -= Time.deltaTime;
		if (_stamina != null)
		{
			_stamina.Drain(ReelStamDrain * Time.deltaTime);
			if (_stamina.Stamina <= 0f)
			{
				_talk?.Say(ModLocalization.Loc("hc_speech_fish_got_away"));
				_stateTimer = Random.Range(WaitTimeMin * 0.5f, WaitTimeMax * 0.5f);
				_state = FishState.Waiting;
				Log("Reeling → Waiting (stamina depleted, fish escaped)");
				return;
			}
		}
		if (_stateTimer <= 0f)
		{
			_state = FishState.Collecting;
			Log("Reeling → Collecting");
		}
	}

	private void UpdateCollecting()
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			ResetToIdle();
			return;
		}
		string text = DetermineCatch(_currentBaitName);
		if (!string.IsNullOrEmpty(text))
		{
			GameObject gameObject = ZNetScene.instance?.GetPrefab(text);
			if (gameObject != null)
			{
				ItemDrop component = gameObject.GetComponent<ItemDrop>();
				if (component != null)
				{
					if (!inventory.CanAddItem(gameObject, 1))
					{
						_talk?.Say(ModLocalization.Loc("hc_speech_fish_full"), force: true);
						Log("Collecting → Idle (inventory full)");
						ResetToIdle();
						return;
					}
					inventory.AddItem(gameObject, 1);
					string text2 = component.m_itemData.m_shared.m_name;
					string text3 = ((Localization.instance != null) ? Localization.instance.Localize(text2) : text2);
					_talk?.Say(ModLocalization.LocFmt("hc_speech_fish_caught", text3), force: true);
					PlayCatchEffect();
					Log("Collecting — caught " + text + " (" + text3 + ")");
				}
			}
			else
			{
				Log("Collecting — fish prefab '" + text + "' not found in ZNetScene");
			}
		}
		else
		{
			Log("Collecting — no fish determined (empty catch)");
		}
		ItemDrop.ItemData itemData = FindBait();
		if (itemData == null)
		{
			SayThrottled(ModLocalization.Loc("hc_speech_fish_need_bait"));
			Log("Collecting → Idle (no bait left)");
			ResetToIdle();
		}
		else
		{
			_stateTimer = 0f;
			_state = FishState.Casting;
			Log("Collecting → Casting (continuing to fish)");
		}
	}

	private static void BuildBaitFishMapIfNeeded()
	{
		if (s_mapBuilt || ZNetScene.instance == null)
		{
			return;
		}
		s_baitFishMap = new Dictionary<string, List<FishEntry>>();
		s_baitNames = new HashSet<string>();
		int num = 0;
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			Fish component = prefab.GetComponent<Fish>();
			if (component == null)
			{
				continue;
			}
			string pickupPrefabName = ((component.m_pickupItem != null) ? component.m_pickupItem.name : prefab.name);
			num++;
			if (component.m_baits == null || component.m_baits.Count == 0)
			{
				continue;
			}
			foreach (Fish.BaitSetting bait in component.m_baits)
			{
				if (!(bait.m_bait == null))
				{
					string text = bait.m_bait.name;
					s_baitNames.Add(text);
					if (!s_baitFishMap.ContainsKey(text))
					{
						s_baitFishMap[text] = new List<FishEntry>();
					}
					s_baitFishMap[text].Add(new FishEntry
					{
						PickupPrefabName = pickupPrefabName,
						Chance = bait.m_chance
					});
				}
			}
		}
		s_mapBuilt = true;
	}

	private ItemDrop.ItemData FindFishingRod()
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return null;
		}
		foreach (ItemDrop.ItemData allItem in inventory.GetAllItems())
		{
			if (allItem.m_shared.m_animationState == ItemDrop.ItemData.AnimationState.FishingRod)
			{
				return allItem;
			}
		}
		return null;
	}

	private void SayThrottled(string text)
	{
		float time = Time.time;
		if (!(time - _lastSpeechTime < 30f))
		{
			_lastSpeechTime = time;
			_talk?.Say(text);
		}
	}

	private ItemDrop.ItemData FindBait()
	{
		Inventory inventory = _humanoid?.GetInventory();
		if (inventory == null)
		{
			return null;
		}
		BuildBaitFishMapIfNeeded();
		if (s_baitNames == null || s_baitNames.Count == 0)
		{
			return null;
		}
		foreach (ItemDrop.ItemData allItem in inventory.GetAllItems())
		{
			if (allItem.m_dropPrefab != null && s_baitNames.Contains(allItem.m_dropPrefab.name))
			{
				return allItem;
			}
		}
		return null;
	}

	private bool FindWaterSpot(out Vector3 shorePos, out Vector3 waterPos)
	{
		shorePos = Vector3.zero;
		waterPos = Vector3.zero;
		if (ZoneSystem.instance == null)
		{
			return false;
		}
		Vector3 position = base.transform.position;
		float num = float.MaxValue;
		bool result = false;
		for (int i = 0; i < 360; i += 30)
		{
			Vector3 vector = Quaternion.Euler(0f, i, 0f) * Vector3.forward;
			for (float num2 = 8f; num2 <= ScanRadius; num2 += 5f)
			{
				Vector3 vector2 = position + vector * num2;
				float waterLevel = GetWaterLevel(vector2);
				if (waterLevel < -999f || !ZoneSystem.instance.GetSolidHeight(vector2, out var height))
				{
					continue;
				}
				float num3 = waterLevel - height;
				if (num3 < 2f)
				{
					continue;
				}
				Vector3 vector3 = Vector3.zero;
				bool flag = false;
				for (float num4 = 3f; num4 <= num2; num4 += 1f)
				{
					Vector3 vector4 = position + vector * (num2 - num4);
					if (ZoneSystem.instance.GetSolidHeight(vector4, out var height2))
					{
						float num5 = waterLevel - height2;
						if (num5 <= 0f)
						{
							vector3 = vector4;
							flag = true;
							break;
						}
					}
				}
				if (flag)
				{
					Vector3 normalized = (vector3 - vector2).normalized;
					vector3 += normalized * 2f;
					float num6 = Vector3.Distance(position, vector3);
					if (num6 < num)
					{
						num = num6;
						shorePos = vector3;
						waterPos = vector2;
						result = true;
					}
				}
				break;
			}
		}
		return result;
	}

	private static string DetermineCatch(string baitName)
	{
		if (string.IsNullOrEmpty(baitName) || s_baitFishMap == null)
		{
			return null;
		}
		if (!s_baitFishMap.TryGetValue(baitName, out var value) || value.Count == 0)
		{
			return null;
		}
		float num = 0f;
		foreach (FishEntry item in value)
		{
			num += item.Chance;
		}
		if (num <= 0f)
		{
			return value[0].PickupPrefabName;
		}
		float num2 = Random.value * num;
		float num3 = 0f;
		foreach (FishEntry item2 in value)
		{
			num3 += item2.Chance;
			if (num2 <= num3)
			{
				return item2.PickupPrefabName;
			}
		}
		return value[value.Count - 1].PickupPrefabName;
	}

	private void PlayCatchEffect()
	{
		Player localPlayer = Player.m_localPlayer;
		if (!(localPlayer == null) && !(_character == null))
		{
			localPlayer.m_skillLevelupEffects.Create(_character.GetHeadPoint(), _character.transform.rotation, _character.transform, 1f, -1, ZDOID.None);
		}
	}

	private float GetWaterLevel(Vector3 point)
	{
		return Floating.GetWaterLevel(point, ref _cachedWaterVolume);
	}
}
