using System;
using UnityEngine;

namespace Companions;

public class HuntController : MonoBehaviour
{
	private enum HuntState
	{
		Scanning,
		Hunting,
		CollectingDrops
	}

	private const float ScanInterval = 3f;

	private const float ScanRadius = 20f;

	private const float DropScanRadius = 12f;

	private const float DropPickupRange = 3f;

	private const float DropScanDelay = 5f;

	private const float DropTimeout = 12f;

	private static readonly string[] s_preyPrefixes = new string[4] { "Boar", "Deer", "Chicken", "Hare" };

	private ZNetView _nview;

	private CompanionAI _ai;

	private CompanionCombatAI _combatAI;

	private CompanionSetup _setup;

	private Character _character;

	private Humanoid _humanoid;

	private HuntState _state;

	private float _scanTimer;

	private Character _prey;

	private Vector3 _lastPreyPos;

	private Vector3 _killPos;

	private GameObject _currentDrop;

	private float _dropTimer;

	private float _dropScanDelayTimer;

	private int _dropsPickedUp;

	private readonly Collider[] _dropBuffer = new Collider[64];

	private int _itemLayerMask;

	public bool IsActive
	{
		get
		{
			if (GetMode() == 7)
			{
				if (_state != HuntState.Hunting)
				{
					return _state == HuntState.CollectingDrops;
				}
				return true;
			}
			return false;
		}
	}

	public void NotifyActionModeChanged()
	{
		if (_combatAI != null && _combatAI.HuntMode)
		{
			_combatAI.HuntMode = false;
		}
		_state = HuntState.Scanning;
		_prey = null;
		_scanTimer = 0f;
	}

	private int GetMode()
	{
		return (_nview?.GetZDO())?.GetInt(CompanionSetup.ActionModeHash) ?? 0;
	}

	private void Awake()
	{
		_nview = GetComponent<ZNetView>();
		_ai = GetComponent<CompanionAI>();
		_combatAI = GetComponent<CompanionCombatAI>();
		_setup = GetComponent<CompanionSetup>();
		_character = GetComponent<Character>();
		_humanoid = GetComponent<Humanoid>();
		_itemLayerMask = LayerMask.GetMask("item");
	}

	private void Update()
	{
		if (_nview == null || !_nview.IsOwner() || CompanionRest.MHR_ShouldPauseWork(this))
		{
			return;
		}
		int mode = GetMode();
		if (mode != 7)
		{
			if (_combatAI != null && _combatAI.HuntMode)
			{
				_combatAI.HuntMode = false;
			}
			_state = HuntState.Scanning;
			_prey = null;
			return;
		}
		switch (_state)
		{
		case HuntState.Scanning:
			UpdateScanning();
			break;
		case HuntState.Hunting:
			UpdateHunting();
			break;
		case HuntState.CollectingDrops:
			UpdateCollectingDrops();
			break;
		}
	}

	private void UpdateScanning()
	{
		if ((_combatAI != null && _combatAI.IsEngaged && !_combatAI.HuntMode) || CompanionInteractPanel.IsOpenFor(_setup) || CompanionRadialMenu.IsOpenFor(_setup))
		{
			return;
		}
		_scanTimer -= Time.deltaTime;
		if (!(_scanTimer > 0f))
		{
			_scanTimer = 3f;
			Character character = FindPrey();
			if (!(character == null))
			{
				_prey = character;
				_lastPreyPos = character.transform.position;
				_combatAI.HuntMode = true;
				_combatAI.Engage(character);
				_state = HuntState.Hunting;
			}
		}
	}

	private void UpdateHunting()
	{
		if (_combatAI != null && _combatAI.IsEngaged && !_combatAI.HuntMode)
		{
			bool flag = !_prey;
			bool flag2 = !flag && !(_prey.GetComponent<ZNetView>()?.IsValid() ?? false);
			bool flag3 = !flag && !flag2 && _prey.GetHealth() <= 0f;
			if (flag || flag2 || flag3)
			{
				_killPos = ((flag || flag2) ? _lastPreyPos : _prey.transform.position);
				_currentDrop = null;
				_dropTimer = 0f;
				_dropScanDelayTimer = 5f;
				_dropsPickedUp = 0;
				_state = HuntState.CollectingDrops;
				_prey = null;
			}
			return;
		}
		if (_combatAI == null || !_combatAI.IsEngaged)
		{
			bool flag4 = !_prey;
			bool flag5 = !flag4 && !(_prey.GetComponent<ZNetView>()?.IsValid() ?? false);
			bool flag6 = !flag4 && !flag5 && _prey.GetHealth() <= 0f;
			if (flag4 || flag5 || flag6)
			{
				_killPos = ((flag4 || flag5) ? _lastPreyPos : _prey.transform.position);
				_currentDrop = null;
				_dropTimer = 0f;
				_dropScanDelayTimer = 5f;
				_dropsPickedUp = 0;
				_state = HuntState.CollectingDrops;
				if (_ai != null)
				{
					_ai.StopMoving();
				}
			}
			else
			{
				_state = HuntState.Scanning;
				_scanTimer = 1.5f;
			}
			_prey = null;
			return;
		}
		Character targetCreature = _combatAI.TargetCreature;
		if (targetCreature != null)
		{
			_lastPreyPos = targetCreature.transform.position;
		}
		bool flag7 = !targetCreature;
		bool flag8 = !flag7 && !(targetCreature.GetComponent<ZNetView>()?.IsValid() ?? false);
		bool flag9 = !flag7 && !flag8 && targetCreature.GetHealth() <= 0f;
		if (flag7 || flag8 || flag9)
		{
			_killPos = ((flag7 || flag8) ? _lastPreyPos : targetCreature.transform.position);
			_combatAI.Disengage();
			_currentDrop = null;
			_dropTimer = 0f;
			_dropScanDelayTimer = 5f;
			_dropsPickedUp = 0;
			_prey = null;
			_state = HuntState.CollectingDrops;
			if (_ai != null)
			{
				_ai.StopMoving();
			}
		}
		else if (targetCreature != null)
		{
			float num = Vector3.Distance(base.transform.position, targetCreature.transform.position);
			if (num > 20f)
			{
				_combatAI.Disengage();
				_prey = null;
				_state = HuntState.Scanning;
				_scanTimer = 3f;
			}
		}
		else
		{
			_combatAI.Disengage();
			_prey = null;
			_state = HuntState.Scanning;
			_scanTimer = 3f;
		}
	}

	private void UpdateCollectingDrops()
	{
		if (_combatAI != null && _combatAI.IsEngaged)
		{
			return;
		}
		float deltaTime = Time.deltaTime;
		_dropTimer += deltaTime;
		if (_dropTimer > 12f)
		{
			FinishDropCollection();
			return;
		}
		if (_dropScanDelayTimer > 0f)
		{
			_dropScanDelayTimer -= deltaTime;
			float num = Vector3.Distance(base.transform.position, _killPos);
			if (_dropScanDelayTimer > 0f)
			{
				_ai?.MoveToPoint(deltaTime, _killPos, 3f, num > 8f);
			}
			else
			{
				_ai?.StopMoving();
			}
			return;
		}
		if (_currentDrop != null)
		{
			ZNetView component = _currentDrop.GetComponent<ZNetView>();
			if (component == null || !component.IsValid())
			{
				_currentDrop = null;
			}
			else
			{
				float num2 = Vector3.Distance(base.transform.position, _currentDrop.transform.position);
				if (!(num2 <= 3f))
				{
					_ai?.MoveToPoint(deltaTime, _currentDrop.transform.position, 1.5f, run: true);
					return;
				}
				TryPickupDrop(_currentDrop);
				_currentDrop = null;
			}
		}
		_currentDrop = ScanForDrops();
		if (_currentDrop == null)
		{
			FinishDropCollection();
		}
		else
		{
			_ai?.MoveToPoint(deltaTime, _currentDrop.transform.position, 1.5f, run: true);
		}
	}

	private Character FindPrey()
	{
		bool flag = _setup != null && _setup.GetStayHome() && _setup.HasHomePosition();
		Vector3 vector = (flag ? _setup.GetHomePosition() : base.transform.position);
		float num = (flag ? Mathf.Min(20f, _setup.GetHomeRadius()) : 20f);
		Character result = null;
		float num2 = num * num;
		foreach (Character allCharacter in Character.GetAllCharacters())
		{
			if (!(allCharacter == null) && !(allCharacter.GetHealth() <= 0f) && !(allCharacter == _character) && IsPrey(allCharacter))
			{
				float sqrMagnitude = (allCharacter.transform.position - vector).sqrMagnitude;
				if (!(sqrMagnitude > num2))
				{
					num2 = sqrMagnitude;
					result = allCharacter;
				}
			}
		}
		return result;
	}

	private void FinishDropCollection()
	{
		_currentDrop = null;
		_state = HuntState.Scanning;
		_scanTimer = 0f;
		bool flag = _setup != null && _setup.GetFollow();
		bool flag2 = _setup != null && _setup.GetStayHome() && _setup.HasHomePosition();
		if (_ai != null && flag2)
		{
			_ai.SetFollowTarget(null);
			_ai.SetPatrolPointAt(_setup.GetHomePosition());
		}
		else if (_ai != null && flag && Player.m_localPlayer != null)
		{
			_ai.SetFollowTarget(Player.m_localPlayer.gameObject);
		}
	}

	private void TryPickupDrop(GameObject dropGO)
	{
		if (dropGO == null || _humanoid == null)
		{
			return;
		}
		ItemDrop component = dropGO.GetComponent<ItemDrop>();
		if (!(component == null))
		{
			string text = component.m_itemData?.m_shared?.m_name ?? "?";
			int num = component.m_itemData?.m_stack ?? 0;
			if (_humanoid.Pickup(dropGO, autoequip: false, autoPickupDelay: false))
			{
				_dropsPickedUp++;
			}
		}
	}

	private GameObject ScanForDrops()
	{
		int num = Physics.OverlapSphereNonAlloc(_killPos, 12f, _dropBuffer, _itemLayerMask);
		if (num > _dropBuffer.Length)
		{
			num = _dropBuffer.Length;
		}
		GameObject result = null;
		float num2 = float.MaxValue;
		int num3 = 0;
		for (int i = 0; i < num; i++)
		{
			Collider collider = _dropBuffer[i];
			if (collider == null || collider.attachedRigidbody == null)
			{
				continue;
			}
			ItemDrop component = collider.attachedRigidbody.GetComponent<ItemDrop>();
			if (component == null || !component.m_autoPickup)
			{
				continue;
			}
			ZNetView component2 = component.GetComponent<ZNetView>();
			if (!(component2 == null) && component2.IsValid())
			{
				num3++;
				float num4 = Vector3.Distance(base.transform.position, component.transform.position);
				if (num4 < num2)
				{
					num2 = num4;
					result = component.gameObject;
				}
			}
		}
		return result;
	}

	private static bool IsPrey(Character c)
	{
		string text = c.gameObject.name;
		string[] array = s_preyPrefixes;
		foreach (string value in array)
		{
			if (text.StartsWith(value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}
}
