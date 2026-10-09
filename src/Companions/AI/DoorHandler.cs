using UnityEngine;

namespace Companions;

public class DoorHandler : MonoBehaviour
{
	private enum Phase
	{
		Idle,
		Approaching,
		WaitingForOpen,
		PassingThrough,
		Closing
	}

	private const float ScanInterval = 0.5f;

	private const float StuckThreshold = 1f;

	private const float StuckMoveDist = 0.2f;

	private const float StuckCheckInterval = 0.5f;

	private const float OpenWaitTime = 1.2f;

	private const float PassTimeout = 8f;

	private const float ProximityCheck = 2.5f;

	private const float ApproachTimeout = 10f;

	private const float HeartbeatInterval = 2f;

	private const float ProactiveCheckInterval = 1f;

	private const float ProactiveStagnationTime = 3f;

	private const float MinProactiveSpeed = 0.5f;

	private const float MinProgressRate = 0.3f;

	private CompanionAI _ai;

	private Humanoid _humanoid;

	private CompanionSetup _setup;

	private ZNetView _nview;

	private HarvestController _harvest;

	private RepairController _repair;

	private CompanionRest _rest;

	private Phase _phase;

	private Door _targetDoor;

	private Vector3 _doorPos;

	private float _scanTimer;

	private float _stuckTimer;

	private float _stuckCheckTimer;

	private Vector3 _lastStuckPos;

	private float _waitTimer;

	private float _passTimer;

	private float _approachTimer;

	private float _heartbeatTimer;

	private int _scanAttempts;

	private float _cooldownTimer;

	private Vector3 _approachDir;

	private const float PostCloseCooldown = 4f;

	private float _proactiveCheckTimer;

	private float _proactiveStagnationTimer;

	private float _lastFollowDist;

	private Vector3 _lastDisplacementPos;

	private bool _lastFollowDistValid;

	private readonly Collider[] _proximityBuffer = new Collider[16];

	private static Door[] _doorCache;

	private static float _doorCacheExpiry;

	private const float DoorCacheInterval = 5f;

	private static float ScanRadius => ModConfig.DoorScanRadius.Value;

	private static float FollowDistMin => ModConfig.DoorFollowDistMin.Value;

	private static float InteractDist => ModConfig.DoorInteractDist.Value;

	private static float CloseDistance => ModConfig.DoorCloseDistance.Value;

	private static float ProactiveScanRadius => ModConfig.DoorProactiveScanRadius.Value;

	public bool IsActive
	{
		get
		{
			if (_phase != Phase.Idle)
			{
				return _phase != Phase.Closing;
			}
			return false;
		}
	}

	public void DirectOpenDoor(Door door)
	{
		if (!(door == null))
		{
			if (!IsValidClosedDoor(door))
			{
				CompanionsPlugin.Log.LogDebug("[DoorHandler] DirectOpenDoor — door \"" + door.m_name + "\" is not a valid closed door");
				return;
			}
			_targetDoor = door;
			_doorPos = door.transform.position;
			_approachDir = _doorPos - base.transform.position;
			_approachDir.y = 0f;
			_approachDir = ((_approachDir.sqrMagnitude > 0.01f) ? _approachDir.normalized : base.transform.forward);
			_phase = Phase.Approaching;
			_approachTimer = 0f;
			_stuckTimer = 0f;
			_cooldownTimer = 0f;
			ResetProactive();
			CompanionsPlugin.Log.LogDebug("[DoorHandler] DirectOpenDoor — approaching \"" + door.m_name + "\" " + $"dist={Vector3.Distance(base.transform.position, _doorPos):F1}m");
		}
	}

	private static Door[] GetDoorCache()
	{
		if (_doorCache == null || Time.time >= _doorCacheExpiry)
		{
			_doorCache = Object.FindObjectsByType<Door>(FindObjectsSortMode.None);
			_doorCacheExpiry = Time.time + 5f;
		}
		return _doorCache;
	}

	private void Awake()
	{
		_ai = GetComponent<CompanionAI>();
		_humanoid = GetComponent<Humanoid>();
		_setup = GetComponent<CompanionSetup>();
		_nview = GetComponent<ZNetView>();
		_harvest = GetComponent<HarvestController>();
		_repair = GetComponent<RepairController>();
		_rest = GetComponent<CompanionRest>();
		CompanionsPlugin.Log.LogDebug("[DoorHandler] Initialized — door handling active");
	}

	private void Update()
	{
		if ((_phase == Phase.Idle && (MHR_Mover.OwnsMovement(this) || MHR_Follow.Owns(this))) || _ai == null || _humanoid == null || _setup == null || _nview == null || _nview.GetZDO() == null || !_nview.IsOwner())
		{
			return;
		}
		int num = _nview.GetZDO().GetInt(CompanionSetup.ActionModeHash);
		if (num != 4)
		{
			float deltaTime = Time.deltaTime;
			_heartbeatTimer -= deltaTime;
			if (_heartbeatTimer <= 0f && _phase != Phase.Idle)
			{
				_heartbeatTimer = 2f;
				GameObject followTarget = _ai.GetFollowTarget();
				float num2 = ((followTarget != null) ? Vector3.Distance(base.transform.position, followTarget.transform.position) : (-1f));
				float num3 = ((_targetDoor != null) ? Vector3.Distance(base.transform.position, _doorPos) : (-1f));
				CompanionsPlugin.Log.LogDebug($"[DoorHandler] ♥ phase={_phase} " + "door=\"" + ((_targetDoor != null) ? _targetDoor.m_name : "null") + "\" " + $"doorDist={num3:F1} followDist={num2:F1} " + $"pos={base.transform.position:F1} doorPos={_doorPos:F1}");
			}
			switch (_phase)
			{
			case Phase.Idle:
				UpdateIdle(deltaTime);
				break;
			case Phase.Approaching:
				UpdateApproaching(deltaTime);
				break;
			case Phase.WaitingForOpen:
				UpdateWaitingForOpen(deltaTime);
				break;
			case Phase.PassingThrough:
				UpdatePassingThrough(deltaTime);
				break;
			case Phase.Closing:
				UpdateClosing();
				break;
			}
		}
	}

	private void UpdateIdle(float dt)
	{
		if (_cooldownTimer > 0f)
		{
			_cooldownTimer -= dt;
			_stuckTimer = 0f;
			ResetProactive();
			return;
		}
		if (_rest != null && _rest.IsResting)
		{
			_stuckTimer = 0f;
			_stuckCheckTimer = 0f;
			ResetProactive();
			return;
		}
		if ((_harvest != null && _harvest.IsActive && !_harvest.IsMoving) || (_repair != null && _repair.IsActive && _repair.Phase != RepairController.RepairPhase.MovingToStation))
		{
			_stuckTimer = 0f;
			_stuckCheckTimer = 0f;
			ResetProactive();
			return;
		}
		bool flag = (_harvest != null && _harvest.IsActive) || (_repair != null && _repair.IsActive);
		GameObject gameObject = null;
		float num = 0f;
		if (!flag)
		{
			gameObject = _ai?.GetFollowTarget();
			if (gameObject != null)
			{
				num = Vector3.Distance(base.transform.position, gameObject.transform.position);
				if (num < FollowDistMin)
				{
					_stuckTimer = 0f;
					_scanAttempts = 0;
					ResetProactive();
					return;
				}
			}
		}
		bool flag2 = !flag && gameObject != null;
		bool flag3 = flag;
		if (flag2 || flag3)
		{
			Vector3 vector = _humanoid?.GetVelocity() ?? Vector3.zero;
			float magnitude = new Vector3(vector.x, 0f, vector.z).magnitude;
			if (magnitude > 0.5f)
			{
				_proactiveCheckTimer += dt;
				if (_proactiveCheckTimer >= 1f)
				{
					_proactiveCheckTimer = 0f;
					if (_lastFollowDistValid)
					{
						float num2 = ((!flag2) ? Vector3.Distance(base.transform.position, _lastDisplacementPos) : (_lastFollowDist - num));
						if (num2 < 0.3f)
						{
							_proactiveStagnationTimer += 1f;
						}
						else
						{
							_proactiveStagnationTimer = Mathf.Max(0f, _proactiveStagnationTimer - 1f);
						}
					}
					if (flag2)
					{
						_lastFollowDist = num;
					}
					_lastDisplacementPos = base.transform.position;
					_lastFollowDistValid = true;
				}
				if (_proactiveStagnationTimer >= 3f)
				{
					ResetProactive();
					if (flag2)
					{
						ScanForDoorProactive(gameObject.transform.position);
					}
					else
					{
						ScanForDoor(ProactiveScanRadius);
					}
					return;
				}
			}
		}
		else
		{
			ResetProactive();
		}
		_stuckCheckTimer += dt;
		if (_stuckCheckTimer >= 0.5f)
		{
			float num3 = Vector3.Distance(base.transform.position, _lastStuckPos);
			_lastStuckPos = base.transform.position;
			_stuckCheckTimer = 0f;
			if (num3 < 0.2f)
			{
				_stuckTimer += 0.5f;
			}
			else
			{
				_stuckTimer = 0f;
			}
		}
		if (!(_stuckTimer < 1f))
		{
			_scanTimer += dt;
			if (!(_scanTimer < 0.5f))
			{
				_scanTimer = 0f;
				ScanForDoor();
			}
		}
	}

	private void UpdateApproaching(float dt)
	{
		if (!ValidateDoor())
		{
			return;
		}
		_approachTimer += dt;
		if (_approachTimer > 10f)
		{
			CompanionsPlugin.Log.LogWarning($"[DoorHandler] Approach timeout ({10f}s) — door \"{_targetDoor.m_name}\" " + $"dist={Vector3.Distance(base.transform.position, _doorPos):F1}m — giving up");
			ResetToIdle();
			return;
		}
		float num = MHR_Nav.DoorDist(base.transform.position, _doorPos);
		if (num > InteractDist * 0f)
		{
			MHR_Nav.Move(_ai, dt, _doorPos, InteractDist, run: true, "Door");
		}
		else
		{
			Vector3 vector = _doorPos - base.transform.position;
			vector.y = 0f;
			if (vector.sqrMagnitude > 0.001f)
			{
				_ai.MoveTowards(vector.normalized, run: true);
			}
		}
		if (num < InteractDist)
		{
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] Within interact range ({num:F1}m < {InteractDist}m) — opening door");
			bool flag = _targetDoor.Interact(_humanoid, hold: false, alt: false);
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] Door.Interact returned {flag}");
			_phase = Phase.WaitingForOpen;
			_waitTimer = 1.2f;
		}
	}

	private void UpdateWaitingForOpen(float dt)
	{
		if (!ValidateDoor())
		{
			return;
		}
		_waitTimer -= dt;
		if (_waitTimer > 0f)
		{
			return;
		}
		ZNetView component = _targetDoor.GetComponent<ZNetView>();
		if (component != null && component.GetZDO() != null)
		{
			int num = component.GetZDO().GetInt(ZDOVars.s_state);
			if (num != 0)
			{
				_phase = Phase.PassingThrough;
				_passTimer = 0f;
				CompanionsPlugin.Log.LogDebug($"[DoorHandler] Door opened (state={num}) — passing through");
				return;
			}
			CompanionsPlugin.Log.LogWarning("[DoorHandler] Door still closed after wait — retrying interact " + $"(dist={Vector3.Distance(base.transform.position, _doorPos):F1}m)");
			if (_targetDoor.Interact(_humanoid, hold: false, alt: false))
			{
				_waitTimer = 1.2f;
				return;
			}
		}
		CompanionsPlugin.Log.LogWarning("[DoorHandler] Door didn't open after retry — resetting");
		ResetToIdle();
	}

	private void UpdatePassingThrough(float dt)
	{
		if (!ValidateDoor())
		{
			return;
		}
		_passTimer += dt;
		float num = Vector3.Distance(base.transform.position, _doorPos);
		if (num > CloseDistance || _passTimer > 8f)
		{
			_phase = Phase.Closing;
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] Past door (dist={num:F1}m, timer={_passTimer:F1}s) — closing");
			return;
		}
		if (_passTimer > 3f && !(_ai.GetFollowTarget() != null))
		{
			CompanionsPlugin.Log.LogDebug("[DoorHandler] Door is open - handing movement back to the job");
			ResetToIdle(withCooldown: true);
			return;
		}
		GameObject followTarget = _ai.GetFollowTarget();
		Vector3 vector;
		if (followTarget != null)
		{
			vector = followTarget.transform.position - base.transform.position;
			vector.y = 0f;
		}
		else
		{
			vector = _approachDir;
		}
		if (vector.sqrMagnitude > 0.01f)
		{
			_ai.PushDirection(vector.normalized, run: true);
		}
	}

	private void UpdateClosing()
	{
		if (_targetDoor == null || !_targetDoor)
		{
			ResetToIdle();
			return;
		}
		if (IsAnyoneNearDoor())
		{
			CompanionsPlugin.Log.LogDebug("[DoorHandler] Someone near door — skipping close");
			ResetToIdle(withCooldown: true);
			return;
		}
		ZNetView component = _targetDoor.GetComponent<ZNetView>();
		if (component != null && component.GetZDO() != null && component.GetZDO().GetInt(ZDOVars.s_state) != 0)
		{
			_targetDoor.Interact(_humanoid, hold: false, alt: false);
			CompanionsPlugin.Log.LogDebug("[DoorHandler] Closed door behind us");
		}
		ResetToIdle(withCooldown: true);
	}

	internal static bool IsValidClosedDoor(Door door)
	{
		if (door == null || !door)
		{
			return false;
		}
		ZNetView component = door.GetComponent<ZNetView>();
		if (component == null || component.GetZDO() == null)
		{
			return false;
		}
		if (component.GetZDO().GetInt(ZDOVars.s_state) != 0)
		{
			return false;
		}
		if (door.m_keyItem != null)
		{
			return false;
		}
		if (door.m_checkGuardStone && !PrivateArea.CheckAccess(door.transform.position, 0f, flash: false))
		{
			return false;
		}
		return true;
	}

	private void ResetProactive()
	{
		_proactiveCheckTimer = 0f;
		_proactiveStagnationTimer = 0f;
		_lastFollowDistValid = false;
		_lastDisplacementPos = base.transform.position;
	}

	private void ScanForDoorProactive(Vector3 playerPos)
	{
		Door[] doorCache = GetDoorCache();
		Door door = null;
		float num = float.MaxValue;
		float num2 = 0f;
		float num3 = 0f;
		int num4 = 0;
		Door[] array = doorCache;
		foreach (Door door2 in array)
		{
			if (door2 == null)
			{
				continue;
			}
			float num5 = Vector3.Distance(base.transform.position, door2.transform.position);
			if (num5 > ProactiveScanRadius)
			{
				continue;
			}
			num4++;
			if (IsValidClosedDoor(door2))
			{
				float num6 = Vector3.Distance(playerPos, door2.transform.position);
				float num7 = num6 + num5 * 0.3f;
				if (num7 < num)
				{
					num = num7;
					door = door2;
					num2 = num5;
					num3 = num6;
				}
			}
		}
		if (door != null)
		{
			_targetDoor = door;
			_doorPos = door.transform.position;
			_approachDir = _doorPos - base.transform.position;
			_approachDir.y = 0f;
			_approachDir = ((_approachDir.sqrMagnitude > 0.01f) ? _approachDir.normalized : base.transform.forward);
			_phase = Phase.Approaching;
			_approachTimer = 0f;
			_stuckTimer = 0f;
			CompanionsPlugin.Log.LogDebug("[DoorHandler] PROACTIVE — found door \"" + door.m_name + "\" " + $"dist={num2:F1}m playerDist={num3:F1}m " + $"score={num:F1} ({num4} doors nearby) — approaching");
		}
		else
		{
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] PROACTIVE scan — no valid doors within {ProactiveScanRadius}m " + $"({num4} doors seen)");
		}
	}

	private void ScanForDoor(float overrideRadius = -1f)
	{
		float num = ((overrideRadius > 0f) ? overrideRadius : ScanRadius);
		Door[] doorCache = GetDoorCache();
		Door door = null;
		float num2 = float.MaxValue;
		int num3 = 0;
		int num4 = 0;
		Door[] array = doorCache;
		foreach (Door door2 in array)
		{
			if (door2 == null)
			{
				continue;
			}
			float num5 = Vector3.Distance(base.transform.position, door2.transform.position);
			if (num5 > num)
			{
				continue;
			}
			num3++;
			if (!IsValidClosedDoor(door2))
			{
				if (_scanAttempts < 3)
				{
					ZNetView component = door2.GetComponent<ZNetView>();
					int num6 = ((component != null && component.GetZDO() != null) ? component.GetZDO().GetInt(ZDOVars.s_state) : (-1));
					CompanionsPlugin.Log.LogDebug("[DoorHandler] Scan — rejected door \"" + door2.m_name + "\" " + $"state={num6} locked={door2.m_keyItem != null} " + $"pos={door2.transform.position:F1}");
				}
			}
			else
			{
				num4++;
				if (num5 < num2)
				{
					num2 = num5;
					door = door2;
				}
			}
		}
		_scanAttempts++;
		if (door != null)
		{
			_targetDoor = door;
			_doorPos = door.transform.position;
			_approachDir = _doorPos - base.transform.position;
			_approachDir.y = 0f;
			_approachDir = ((_approachDir.sqrMagnitude > 0.01f) ? _approachDir.normalized : base.transform.forward);
			_phase = Phase.Approaching;
			_approachTimer = 0f;
			_stuckTimer = 0f;
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] Found closed door \"{door.m_name}\" at dist={num2:F1}m " + $"pos={_doorPos:F1} — approaching " + $"({num3} doors nearby, {num4} valid)");
		}
		else if (_scanAttempts <= 3 || _scanAttempts % 10 == 0)
		{
			CompanionsPlugin.Log.LogDebug($"[DoorHandler] Scan #{_scanAttempts} — no valid doors " + $"({num3} doors nearby, {num4} valid) " + $"stuck={_stuckTimer:F1}s pos={base.transform.position:F1}");
		}
	}

	private bool ValidateDoor()
	{
		if (_targetDoor == null || !_targetDoor)
		{
			CompanionsPlugin.Log.LogDebug("[DoorHandler] Target door destroyed — resetting");
			ResetToIdle();
			return false;
		}
		return true;
	}

	private void ResetToIdle(bool withCooldown = false)
	{
		_phase = Phase.Idle;
		_targetDoor = null;
		_stuckTimer = 0f;
		_scanTimer = 0f;
		_passTimer = 0f;
		_approachTimer = 0f;
		_scanAttempts = 0;
		ResetProactive();
		if (withCooldown)
		{
			_cooldownTimer = 4f;
		}
	}

	private bool IsAnyoneNearDoor()
	{
		if (Player.m_localPlayer != null)
		{
			float num = Vector3.Distance(Player.m_localPlayer.transform.position, _doorPos);
			if (num < 2.5f)
			{
				return true;
			}
		}
		int num2 = Physics.OverlapSphereNonAlloc(_doorPos, 2.5f, _proximityBuffer);
		for (int i = 0; i < num2; i++)
		{
			Collider collider = _proximityBuffer[i];
			if (!(collider == null) && !(collider.gameObject == base.gameObject) && collider.GetComponent<CompanionSetup>() != null)
			{
				return true;
			}
		}
		return false;
	}
}
