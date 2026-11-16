using UnityEngine;

namespace Companions;

// 1.5.96: getting out of the hole it dug (John: "check the cliff scaling we built into NPC movement already. That is usually
// how I will get out unless its really deep. Then I either build a ladder out or like you said dig a steep path"):
//  - First the movement's own climb (MHR_Mover: 16 directions out to 16 m, the way up with the smallest steepest step,
//    3 tries). Only when that has given up and it is in a hole (the ground 3 m out is 1.5 m or more above its feet all
//    round, 8 directions) does it dig its way out.
//  - It picks the wall where the ground 2 m out is lowest (of 16 directions), digs a 1 m-deep notch into it at about 1 m
//    above its feet, jumps up onto it and repeats: a stair of 1 m steps at about 45 degrees. No ladders.
//  - 1.5.97 (L3): progress is only its feet getting 0.1 m higher. 12 swings or 60 s without that: "I'm stuck in this
//    hole", and the job stops (it still follows you when you call it; it picks
//    the job back up when you set a job again or it has been moved 5 m away).
//  - 1.5.97 (plan v3): the Ore job also starts it itself on a vein (PitBegin, vein: true) when the general climb can't get
//    it out of its trench: then it's out when its feet are within 0.5 m of the rim, the job carries on, and it checks the
//    ladder head room over each step before jumping onto it.
public partial class HarvestController
{
	private bool _pitActive;

	private Vector3 _pitDir;

	private float _pitProgAt;

	private int _pitNoProg;

	private float _pitBestY;

	private bool _pitSwung;

	private float _pitSwungAt;

	private float _pitStepGround;

	private float _pitNextCheck;

	private float _pitJumpAt;

	private bool _stuckInHole;

	private bool _pitVein;

	private float _pitRimY;

	// 1.5.97 (item D): walking at a low step - where and when it last got somewhere; a direction given up on (the vein's
	// getting-out picks another)
	private Vector3 _pitWalkFrom;

	// part 1 G2: where its feet last got 0.1 m higher (or the steps began) - walking PitWalkMax from there without
	// rising drops the direction
	private Vector3 _pitProgFrom;

	private bool _pitWalkLogged;

	private const float PitWalkMax = 3f;

	private float _pitWalkAt;

	private bool _pitDropped;

	private string _pitDropWhy;

	private Vector3 _stuckAt;

	internal bool PitDigging => _pitActive;

	private void PitNotifyDig(Vector3 hitPoint)
	{
		if (CompanionsPlugin.LogDetailed) Log($"Dug a step at {MHR_Mover.Fmt(hitPoint)}");
	}

	// in a hole: the ground 3 m out is at least 1.5 m above its feet in all 8 directions
	private bool InHole()
	{
		Vector3 me = base.transform.position;
		for (int k = 0; k < 8; k++)
		{
			Vector3 d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
			if (GroundAt(me + d * 3f) < me.y + 1.5f)
			{
				return false;
			}
		}
		return true;
	}

	// called each frame before the job's own state; true = it is busy getting out (or stuck) and the job waits
	private bool PitTick()
	{
		if (_stuckInHole)
		{
			if (GetMode() != 3 || Vector3.Distance(base.transform.position, _stuckAt) > 5f)
			{
				_stuckInHole = false;
				if (CompanionsPlugin.LogNormal) LogInfo("Out of the hole - back to work");
				return false;
			}
			return true;
		}
		if (!_pitActive)
		{
			if (GetMode() != 3 || Time.time < _pitNextCheck || _ai == null || _character == null)
			{
				return false;
			}
			_pitNextCheck = Time.time + 1f;
			// 1.5.97: not while it works a vein - it stands in the trench it digs on purpose and has its own stair out
			if (VeinWorking || !MHR_Mover.GaveUp(_ai) || !_character.IsOnGround() || InDungeonAt(base.transform.position) || !InHole())
			{
				return false;
			}
			PitStart();
		}
		PitStep();
		return true;
	}

	private void PitStart()
	{
		Vector3 me = base.transform.position;
		float best = float.MaxValue;
		Vector3 dir = Vector3.forward;
		for (int k = 0; k < 16; k++)
		{
			Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
			float g = GroundAt(me + d * 2f);
			if (g < best)
			{
				best = g;
				dir = d;
			}
		}
		PitBegin(dir, vein: false, 0f);
	}

	// 1.5.97 (plan v3): the stair toward dir. vein = the Ore job's own way out of its trench: the job stays on (no reset)
	// and it's out when its feet are within 0.5 m of the rim ground rimY
	private void PitBegin(Vector3 dir, bool vein, float rimY)
	{
		Vector3 me = base.transform.position;
		_pitDir = dir;
		_pitVein = vein;
		_pitRimY = rimY;
		_pitActive = true;
		_pitProgAt = Time.time;
		_pitNoProg = 0;
		_pitBestY = me.y;
		_pitSwung = false;
		_pitWalkFrom = me;
		_pitWalkAt = Time.time;
		_pitProgFrom = me;
		_pitWalkLogged = false;
		_pitDropped = false;
		if (!vein)
		{
			ResetToIdle();
		}
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo(vein ? $"Cutting steps out of the trench at {MHR_Mover.Fmt(me)} toward {MHR_Mover.Fmt(me + _pitDir * 3f)} (the rim is {rimY - me.y:F1} m up)" : $"Can't climb out of the hole at {MHR_Mover.Fmt(me)} - digging steps out toward {MHR_Mover.Fmt(me + _pitDir * 3f)}");
	}

	private void PitStuck(Vector3 me, string detail)
	{
		_pitActive = false;
		_stuckInHole = true;
		_stuckAt = me;
		SayGaveUp(GiveUpWhy.StuckInHole, detail);
		RestoreLoadout();
		if (_pitVein)
		{
			_vFor = null;   // once it's moved or given a job, it plans the vein again from where it is
		}
	}

	// item D: on a vein a direction that doesn't work is dropped, and the getting-out picks another (each of the 16 once)
	private void PitGiveUpDir(Vector3 me, string detail)
	{
		if (!_pitVein)
		{
			PitStuck(me, detail);
			return;
		}
		_pitActive = false;
		_pitDropped = true;
		_pitDropWhy = detail;
		_ai.StopMoving();
	}

	private void PitStep()
	{
		Vector3 me = base.transform.position;
		if (_pitVein ? me.y >= _pitRimY - 0.5f : !InHole())
		{
			_pitActive = false;
			if (CompanionsPlugin.LogNormal) LogInfo(_pitVein ? $"Out of the trench by its own steps (feet {me.y:F1}, the rim {_pitRimY:F1})" : "Dug its way out of the hole");
			return;
		}
		// 1.5.97 (L3): progress is only its feet getting at least 0.1 m higher - walking about on its own step, or digging
		// without getting up, doesn't restart the clock
		if (me.y > _pitBestY + 0.1f)
		{
			_pitBestY = me.y;
			_pitProgAt = Time.time;
			_pitNoProg = 0;
			_pitProgFrom = me;
		}
		bool busy = _humanoid.InAttack() || _character.IsStaggering();
		Vector3 step = me + _pitDir * 1.0f;
		float g = GroundAt(step);
		if (_pitSwung && !busy && Time.time - _pitSwungAt > 0.35f)
		{
			_pitSwung = false;
			_pitNoProg++;
			if (CompanionsPlugin.LogDetailed) Log($"Step swing: the step ahead {_pitStepGround:F2} -> {g:F2} ({(_pitStepGround - g) * 100f:F0} cm), feet {me.y:F2}");
		}
		if (_pitNoProg >= VNoProgSwings || Time.time - _pitProgAt >= VNoProgTime)
		{
			PitGiveUpDir(me, $"{_pitNoProg} swings / {Time.time - _pitProgAt:F0}s without getting 0.1 m higher, step ground {g:F1}, feet {me.y:F1}");
			return;
		}
		if (busy || _pitSwung)
		{
			return;
		}
		// the step is low enough to jump onto: walk at it and jump
		if (g <= me.y + 1.0f)
		{
			Vector3 flat = _pitDir;
			flat.y = 0f;
			// item D: walking at a low step and not getting anywhere - not moved 0.3 m or risen 0.1 m in 3 s
			if (_pitVein)
			{
				// part 1 G2: the walk has a length - PitWalkMax from where its feet last rose (log 97d599fc: Gunter
				// walked at a low step for 30 s, out of the loaded area and into water)
				if (!_pitWalkLogged)
				{
					_pitWalkLogged = true;
					if (CompanionsPlugin.LogDetailed) Log($"Walking at the step (the ground 1 m ahead is {g:F1}, feet {me.y:F1}; out at {_pitRimY - 0.5f:F1}) - {PitWalkMax:F0} m at most without getting higher");
				}
				if (Flat(me, _pitProgFrom) > PitWalkMax)
				{
					PitGiveUpDir(me, $"walked {Flat(me, _pitProgFrom):F1} m along the steps without getting 0.1 m higher (feet {me.y:F1}, out at {_pitRimY - 0.5f:F1})");
					return;
				}
				if (Flat(me, _pitWalkFrom) >= 0.3f || me.y >= _pitWalkFrom.y + 0.1f)
				{
					_pitWalkFrom = me;
					_pitWalkAt = Time.time;
				}
				else if (Time.time - _pitWalkAt >= 3f)
				{
					PitGiveUpDir(me, $"walked at the step for 3 s without moving 0.3 m or getting 0.1 m higher (step ground {g:F1}, feet {me.y:F1})");
					return;
				}
			}
			MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(flat.normalized));
			_ai.MoveTowards(flat.normalized, run: false);
			if (_character.IsOnGround() && Time.time >= _pitJumpAt && g > me.y + 0.4f)
			{
				// 1.5.97 (plan v3): the ladder head room check over the step before jumping onto it
				if (!MHR_Mover.HeadRoomAt(new Vector3(step.x, g, step.z), 2.2f))
				{
					PitGiveUpDir(me, $"something over the step at {MHR_Mover.Fmt(new Vector3(step.x, g, step.z))} - no room to jump onto it");
					return;
				}
				// a jump costs stamina: get it back first, standing on the flat
				if (_stamina != null && _stamina.Stamina < 10f + 0.15f * Mathf.Max(1f, _stamina.MaxStamina))
				{
					_ai.StopMoving();
					_pitProgAt += Time.deltaTime;   // resting: the clock waits
					_pitWalkAt += Time.deltaTime;
					return;
				}
				_pitJumpAt = Time.time + 1f;
				_character.Jump();
			}
			return;
		}
		// dig the notch: at the wall 1 m ahead, about 1 m above its feet
		_ai.StopMoving();
		_pitWalkFrom = me;
		_pitWalkAt = Time.time;
		Vector3 aim = new Vector3(step.x, me.y + 1.2f, step.z);
		bool leave;
		_pitStepGround = g;
		if (ToolSwing(aim, out leave))
		{
			_pitSwung = true;
			_pitSwungAt = Time.time;
		}
		else
		{
			if (_catchingBreath)
			{
				_pitProgAt += Time.deltaTime;   // catching its breath: the clock waits
			}
			if (leave)
			{
				_pitActive = false;
				_stuckInHole = true;
				_stuckAt = me;
			}
		}
	}
}
