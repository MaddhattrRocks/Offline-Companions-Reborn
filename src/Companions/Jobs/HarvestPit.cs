using UnityEngine;

namespace Companions;

// 1.5.96: getting out of the hole it dug (John: "check the cliff scaling we built into NPC movement already. That is usually
// how I will get out unless its really deep. Then I either build a ladder out or like you said dig a steep path"):
//  - First the movement's own climb (MHR_Mover: 16 directions out to 16 m, the way up with the smallest steepest step,
//    3 tries). Only when that has given up and it is in a hole (the ground 3 m out is 1.5 m or more above its feet all
//    round, 8 directions) does it dig its way out.
//  - It picks the wall where the ground 2 m out is lowest (of 16 directions), digs a 1 m-deep notch into it at about 1 m
//    above its feet, jumps up onto it and repeats: a stair of 1 m steps at about 45 degrees. No ladders.
//  - Progress: the ground at the next step lowered 5 cm or more, or its feet 0.3 m higher. 12 swings or 60 s without
//    progress on a step: "I'm stuck in this hole", and the job stops (it still follows you when you call it; it picks
//    the job back up when you set a job again or it has been moved 5 m away).
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
			if (!MHR_Mover.GaveUp(_ai) || !_character.IsOnGround() || InDungeonAt(base.transform.position) || !InHole())
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
		for (int k = 0; k < 16; k++)
		{
			Vector3 d = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
			float g = GroundAt(me + d * 2f);
			if (g < best)
			{
				best = g;
				_pitDir = d;
			}
		}
		_pitActive = true;
		_pitProgAt = Time.time;
		_pitNoProg = 0;
		_pitBestY = me.y;
		_pitSwung = false;
		ResetToIdle();
		_ai.StopMoving();
		if (CompanionsPlugin.LogNormal) LogInfo($"Can't climb out of the hole at {MHR_Mover.Fmt(me)} - digging steps out toward {MHR_Mover.Fmt(me + _pitDir * 3f)}");
	}

	private void PitStep()
	{
		Vector3 me = base.transform.position;
		if (!InHole())
		{
			_pitActive = false;
			if (CompanionsPlugin.LogNormal) LogInfo("Dug its way out of the hole");
			return;
		}
		if (me.y > _pitBestY + 0.3f)
		{
			_pitBestY = me.y;
			_pitProgAt = Time.time;
			_pitNoProg = 0;
		}
		bool busy = _humanoid.InAttack() || _character.IsStaggering();
		Vector3 step = me + _pitDir * 1.0f;
		float g = GroundAt(step);
		if (_pitSwung && !busy && Time.time - _pitSwungAt > 0.35f)
		{
			_pitSwung = false;
			if (_pitStepGround - g >= VDigMin)
			{
				_pitProgAt = Time.time;
				_pitNoProg = 0;
			}
			else
			{
				_pitNoProg++;
			}
		}
		if (_pitNoProg >= VNoProgSwings || Time.time - _pitProgAt >= VNoProgTime)
		{
			_pitActive = false;
			_stuckInHole = true;
			_stuckAt = me;
			SayGaveUp(GiveUpWhy.StuckInHole, $"{_pitNoProg} swings / {Time.time - _pitProgAt:F0}s without a step, step ground {g:F1}, feet {me.y:F1}");
			RestoreLoadout();
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
			MHR_Nav.TurnTo(base.transform, Quaternion.LookRotation(flat.normalized));
			_ai.MoveTowards(flat.normalized, run: false);
			if (_character.IsOnGround() && Time.time >= _pitJumpAt && g > me.y + 0.4f)
			{
				_pitJumpAt = Time.time + 1f;
				_character.Jump();
			}
			if (Time.time - _pitProgAt > 3f && g <= me.y + 0.4f)
			{
				_pitProgAt = Time.time;   // walking up a slope of its own stair
			}
			return;
		}
		// dig the notch: at the wall 1 m ahead, about 1 m above its feet
		_ai.StopMoving();
		Vector3 aim = new Vector3(step.x, me.y + 1.2f, step.z);
		bool leave;
		_pitStepGround = g;
		if (ToolSwing(aim, out leave))
		{
			_pitSwung = true;
			_pitSwungAt = Time.time;
		}
		else if (leave)
		{
			_pitActive = false;
			_stuckInHole = true;
			_stuckAt = me;
		}
	}
}
