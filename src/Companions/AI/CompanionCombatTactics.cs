using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.52: combat part 2 - melee fighting like a player (user, 2026-10-15): "Most of the time I will do 2-3 quick swings
// with a fast weapon, then block again. Less swings with a slower weapon. All the time while moving a repositioning";
// "when blocking multiple enemies ... focus on blocking and getting clear of the group and attack when there is an
// opening"; "Often times its better to just take a step back or to the side"; "prioritize staying alive and reducing
// damage, then attacking. Trying to stay out of hit range to recharge stamina when possible"; "We also dont want to make
// the NPC perfect like some super robot."
//
// - Combos: the game carries a combo on only when the next swing starts within 0.2 s of the last one ending
//   (Attack.Start), and melee used to wait for the weapon's AI attack interval (2 s on player weapons) - so it never
//   comboed. Now it swings in bursts of 1-3 straight after each other, then pauses.
// - Openings: it swings when nothing is swinging at it, its target is staggered, or the swing at it has already landed
//   (blocked or hit) and the attacker is finishing it.
// - Stamina: it keeps a reserve for blocking; below it, it backs out of reach to get its breath back, and only fights
//   on if it can't get clear.
// - Groups: with several on it from different sides, it moves until they're all on one side.
// - Steps: sometimes it steps back or aside out of a swing's reach instead of blocking (UpdateBlocking).
// - All chance-based with a varied reaction time, so it gets it wrong now and then.
public partial class CompanionCombatAI
{
	private bool _tacticsOn;

	private int _burstLeft;

	private bool _burstPending;

	private float _burstPauseUntil;

	private Vector3 _stepDir;

	private float _stepUntil;

	private bool _stepRun;

	private Character _stepFrom;

	private bool _recovering;

	private float _recoverSince;

	private bool _cornered;

	private float _corneredUntil;

	private float _repositionSince = -1f;

	private float _repositionBlockedUntil;

	private Vector3 _moveDir;

	private float _moveDirUntil;

	private float _swingStartedAt;

	private float _swingLandedAt = -1f;

	private bool _evading;

	private string _lastTacticLog;

	private float _nextTacticLog;

	private readonly List<Character> _attackers = new List<Character>();

	private static float Reserve => ModConfig.CombatStaminaReserve?.Value ?? 0.3f;

	private static float Resume => Mathf.Max(Reserve, ModConfig.CombatStaminaResume?.Value ?? 0.5f);

	internal static bool FightLikePlayer => ModConfig.FightLikePlayer?.Value ?? true;

	private bool TacticsFor(ItemDrop.ItemData weapon)
	{
		return FightLikePlayer && weapon != null && !MHR_IsProjectileWeapon(weapon) && _targetCreature != null && !_stalking && !Unaware(_targetCreature);
	}

	private void TLog(string s)
	{
		if (s != _lastTacticLog || Time.time >= _nextTacticLog)
		{
			_lastTacticLog = s;
			_nextTacticLog = Time.time + 5f;
			CompanionsPlugin.Log.LogDebug("[Tactics] " + s);
		}
	}

	// how far (centre to centre) a creature's swing reaches us
	// 1.5.53: the swing's real reach (MHR_MeleeReach: the AI range, but no more than the attack's own range + 0.5), not
	// the AI range alone - that's where the AI starts a swing, often well short of where it lands, so steps went too far
	internal static float SwingReach(Character c)
	{
		ItemDrop.ItemData w = (c as Humanoid)?.GetCurrentWeapon();
		return (w != null && w.m_shared != null) ? MHR_MeleeReach(w) : 2f;
	}

	private float Danger(Character c)
	{
		return SwingReach(c) + c.GetRadius() + _character.GetRadius();
	}

	// enemies on this companion: its target and anything close whose target is this companion
	private void GatherAttackers()
	{
		_attackers.Clear();
		float range = ModConfig.CombatGroupRange?.Value ?? 6f;
		Vector3 me = base.transform.position;
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c == null || c == _character || c.GetHealth() <= 0f || !BaseAI.IsEnemy(_character, c))
			{
				continue;
			}
			if (Vector3.Distance(me, c.transform.position) > range)
			{
				continue;
			}
			if (c != _targetCreature && c.GetBaseAI()?.GetTargetCreature() != _character)
			{
				continue;
			}
			_attackers.Add(c);
		}
	}

	private Character NearestAttacker(out float dist)
	{
		Character best = null;
		dist = float.MaxValue;
		foreach (Character c in _attackers)
		{
			float d = Vector3.Distance(base.transform.position, c.transform.position);
			if (d < dist)
			{
				dist = d;
				best = c;
			}
		}
		return best;
	}

	// attackers on different sides (more than FlankAngle apart, seen from here)
	private bool Flanked()
	{
		float limit = ModConfig.CombatFlankAngle?.Value ?? 100f;
		Vector3 me = base.transform.position;
		for (int i = 0; i < _attackers.Count; i++)
		{
			Vector3 a = _attackers[i].transform.position - me;
			a.y = 0f;
			for (int j = i + 1; j < _attackers.Count; j++)
			{
				Vector3 b = _attackers[j].transform.position - me;
				b.y = 0f;
				if (Vector3.Angle(a, b) > limit)
				{
					return true;
				}
			}
		}
		return false;
	}

	// the safe direction that gets furthest from the attackers (and a little towards the owner when it's close by)
	private Vector3 BestDir(float dist, Character from)
	{
		Vector3 me = base.transform.position;
		Vector3 toOwner = Vector3.zero;
		Player owner = MHR_Owner.Of(this);
		if (owner != null)
		{
			Vector3 o = owner.transform.position - me;
			o.y = 0f;
			if (o.magnitude > 2f && o.magnitude < 30f)
			{
				toOwner = o.normalized;
			}
		}
		Vector3 best = Vector3.zero;
		float bestScore = float.MinValue;
		for (int i = 0; i < 12; i++)
		{
			Vector3 d = Quaternion.Euler(0f, i * 30f, 0f) * Vector3.forward;
			float score = 0.5f * Vector3.Dot(d, toOwner);
			foreach (Character c in _attackers)
			{
				Vector3 t = c.transform.position - me;
				t.y = 0f;
				float w = (c == from) ? 2f : 1f;
				score -= w * Vector3.Dot(d, t.normalized) / Mathf.Max(1f, t.magnitude * 0.3f);
			}
			if (from != null && !_attackers.Contains(from))
			{
				Vector3 t = from.transform.position - me;
				t.y = 0f;
				score -= 2f * Vector3.Dot(d, t.normalized);
			}
			score += Random.Range(0f, 0.15f);   // two near-equal ways: not always the same one
			if (score > bestScore && MHR_Mover.SafeStep(me, d, dist))
			{
				bestScore = score;
				best = d;
			}
		}
		return best;
	}

	// moves that way while facing the enemy (backing off, stepping aside). BaseAI.MoveTowards with smooth movement only
	// ever walks forward (moveDir = forward x how well it's lined up), so the move direction is set directly; a
	// non-player turns to where it looks (Character.AlwaysRotateCamera is true), not where it moves.
	private void Move(Vector3 dir, bool run, Character face)
	{
		dir.y = 0f;
		_character.SetMoveDir(dir.normalized);
		_character.SetRun(run);
		if (face != null)
		{
			_ai.LookAtPoint(face.GetCenterPoint());
		}
		else
		{
			_ai.LookTowards(dir);
		}
	}

	private float AttackCost(ItemDrop.ItemData w)
	{
		Attack a = w?.m_shared?.m_attack;
		if (a == null)
		{
			return 0f;
		}
		return a.m_attackStamina * (1f - 0.33f * _character.GetSkillFactor(w.m_shared.m_skillType));
	}

	private float StaminaNow()
	{
		StaminaShare();
		return _stam != null ? _stam.Stamina : 999f;
	}

	private float StaminaMax()
	{
		StaminaShare();
		return (_stam != null && _stam.MaxStamina > 0f) ? _stam.MaxStamina : 100f;
	}

	// 0 = carry on as normal (chase, swing when ReadyToSwing), 1 = it moved this tick, 2 = hold here (swing only if ready)
	private int Tactics(float dt, ItemDrop.ItemData weapon)
	{
		GatherAttackers();
		if (_burstPending && !_character.InAttack())
		{
			_burstPending = false;
			float pause = ModConfig.CombatComboPause?.Value ?? 1f;
			_burstPauseUntil = Time.time + pause * Random.Range(0.6f, 1.4f);
			MaybeStepOut();
		}
		Character near = NearestAttacker(out var nearDist);
		// a step out of a swing or out of reach under way
		if (Time.time < _stepUntil)
		{
			if (!_character.InAttack())
			{
				// 1.5.53: stop as soon as it's out of reach (user: "moving back a lot and not re-engaging after" - the
				// step ran for a set time, at a run, and covered 4-6 m)
				if (_stepFrom != null && Vector3.Distance(base.transform.position, _stepFrom.transform.position) >= Danger(_stepFrom) + 0.3f)
				{
					_stepUntil = 0f;
				}
				else if (!MHR_Mover.SafeStep(base.transform.position, _stepDir, 0.8f))
				{
					_stepUntil = 0f;
				}
				else
				{
					Move(_stepDir, _stepRun, _stepFrom ?? near);
					return 1;
				}
			}
			else
			{
				return 1;
			}
		}
		if (_character.InAttack())
		{
			return 0;
		}
		// a staggered target: close in and punish it (ReadyToSwing) rather than back off or reposition
		if (Punish(weapon))
		{
			_stepUntil = 0f;
			return 0;
		}
		// stamina: keep enough to block with; get out of reach to get it back
		float share = StaminaShare();
		if (!_recovering && share < Reserve)
		{
			_recovering = true;
			_recoverSince = Time.time;
			_burstLeft = 0;
			TLog($"Low on stamina ({share:P0}) - backing out of reach to get its breath back");
		}
		else if (_recovering && share >= Resume)
		{
			_recovering = false;
			_cornered = false;
			TLog($"Got its breath back ({share:P0}) - back in");
		}
		if (_cornered && Time.time >= _corneredUntil)
		{
			_cornered = false;
			_recoverSince = Time.time;
		}
		if (_recovering && !_cornered)
		{
			Character close = null;
			foreach (Character c in _attackers)
			{
				if (Vector3.Distance(base.transform.position, c.transform.position) < Danger(c) + 1f)
				{
					close = c;
					break;
				}
			}
			if (close == null)
			{
				_ai.StopMoving();
				if (near != null)
				{
					_ai.LookAtPoint(near.GetCenterPoint());
				}
				return 2;
			}
			if (Time.time - _recoverSince > (ModConfig.CombatRecoverTime?.Value ?? 8f))
			{
				return Cornered(close.m_name + " keeps after it");
			}
			if (Time.time >= _moveDirUntil)
			{
				_moveDir = BestDir(1.5f, close);
				_moveDirUntil = Time.time + 0.3f;
			}
			if (_moveDir == Vector3.zero)
			{
				return Cornered("nowhere to back off to");
			}
			Move(_moveDir, run: false, close);
			return 1;
		}
		// several on it from different sides: get them all on one side first
		int groupSize = ModConfig.CombatGroupSize?.Value ?? 2;
		if (_attackers.Count >= groupSize && Flanked() && Time.time >= _repositionBlockedUntil)
		{
			if (_repositionSince < 0f)
			{
				_repositionSince = Time.time;
				TLog($"{_attackers.Count} on it from different sides - getting clear");
			}
			if (Time.time - _repositionSince > 4f)
			{
				_repositionSince = -1f;
				_repositionBlockedUntil = Time.time + 5f;
				TLog("Couldn't get them on one side - fighting where it stands");
				return 0;
			}
			if (Time.time >= _moveDirUntil)
			{
				_moveDir = BestDir(2f, null);
				_moveDirUntil = Time.time + 0.3f;
			}
			if (_moveDir != Vector3.zero)
			{
				Move(_moveDir, run: StaminaShare() > Resume, near);
				return 1;
			}
		}
		else
		{
			_repositionSince = -1f;
		}
		return 0;
	}

	private int Cornered(string why)
	{
		_cornered = true;
		_corneredUntil = Time.time + 4f;
		TLog("Can't get clear (" + why + ") - fighting on what stamina it has");
		return 0;
	}

	// after a combo: when tired, in a group, or now and then anyway, step back out of the target's reach
	private void MaybeStepOut()
	{
		if (_targetCreature == null)
		{
			return;
		}
		// 1.5.53: not after every combo below StaminaResume (that was most of the fight) - when close to the reserve
		bool want = StaminaShare() < Reserve + 0.15f || _attackers.Count >= (ModConfig.CombatGroupSize?.Value ?? 2) || Random.value < 0.2f;
		if (!want)
		{
			return;
		}
		float need = Danger(_targetCreature) + 0.7f - Vector3.Distance(base.transform.position, _targetCreature.transform.position);
		if (need <= 0f || need > 2.5f)
		{
			return;
		}
		Vector3 dir = BestDir(Mathf.Max(need, 0.8f), _targetCreature);
		if (dir == Vector3.zero)
		{
			return;
		}
		StartStep(dir, need, _targetCreature, run: false);
		TLog("Stepping back out of " + _targetCreature.m_name + "'s reach after its combo");
	}

	private void StartStep(Vector3 dir, float dist, Character from, bool run)
	{
		_stepDir = dir;
		_stepRun = run;
		_stepFrom = from;
		_stepUntil = Time.time + Mathf.Max(dist, 0.8f) / 3f + 0.3f;   // a cap - it stops once out of reach
		_burstLeft = 0;
	}

	// a swing at it it can get out of the way of, rather than block (UpdateBlocking calls this once per swing, at the
	// moment it reacts)
	private bool TryEvade(Character swinger)
	{
		float chance = ModConfig.StepAwayChance?.Value ?? 0.3f;
		if (StaminaShare() < Reserve)
		{
			chance = Mathf.Min(1f, chance * 2f);   // blocking costs stamina it doesn't have
		}
		if (Random.value >= chance)
		{
			return false;
		}
		float need = Danger(swinger) + 0.6f - Vector3.Distance(base.transform.position, swinger.transform.position);
		if (need > 2.5f)
		{
			return false;   // too deep in its reach to get out in time - block
		}
		need = Mathf.Max(need, 0.8f);
		Vector3 dir = BestDir(need, swinger);
		if (dir == Vector3.zero)
		{
			return false;
		}
		// a quick step: a run only when it has further to go than a jog covers in time
		bool run = need > 1.5f && StaminaShare() > Reserve;
		StartStep(dir, need, swinger, run);
		Vector3 away = base.transform.position - swinger.transform.position;
		away.y = 0f;
		float ang = Vector3.Angle(away, dir);
		TLog("Stepping " + (ang < 30f ? "back" : ang < 70f ? "back and aside" : "aside") + $" out of {swinger.m_name}'s swing ({need:F1} m)");
		return true;
	}

	// an opening: nothing swinging at it, the target staggered, or the swing at it already landed (it's finishing it)
	private bool Opening()
	{
		if (_targetCreature != null && _targetCreature.IsStaggering())
		{
			return true;
		}
		Character sw = SwingAtUs();
		if (sw == null)
		{
			return true;
		}
		return sw == _blockAgainst && _swingLandedAt >= _swingStartedAt;
	}

	// a hit on it or a block, from the creature whose swing it's watching: that swing has landed
	internal void NoteIncoming(Character attacker)
	{
		if (attacker != null && attacker == _blockAgainst)
		{
			_swingLandedAt = Time.time;
		}
	}

	// 1.5.53: its target is staggered (a parry, or a hit that staggered it) and there's stamina for a combo that still
	// leaves a block's worth: go in and punish it, even while catching its breath or between combos (user: "If the NPC
	// parries is should perform a combo if possible against the enemy.")
	// At least one hit whenever it has the stamina for that swing (user: "It should at least do a single hit."); the rest of
	// the combo only above half the reserve (BurstSize, and below for the swings after the first). One combo per stagger.
	private bool Punish(ItemDrop.ItemData w)
	{
		if (_targetCreature == null || w == null || !_targetCreature.IsStaggering())
		{
			_punishUsed = false;
			return false;
		}
		if (_burstLeft > 0)
		{
			return _punishing && StaminaNow() - AttackCost(w) >= Reserve * StaminaMax() * 0.5f;
		}
		return !_punishUsed && StaminaNow() >= AttackCost(w);
	}

	private bool _punishing;

	// the combo for this stagger has started
	private bool _punishUsed;

	private bool ReadyToSwing(ItemDrop.ItemData w)
	{
		if (_character.InAttack() || w == null)
		{
			return false;
		}
		if (Punish(w))
		{
			if (!_punishing)
			{
				_punishing = true;
				TLog(_targetCreature.m_name + " is staggered - going in with a combo" + (_recovering ? " (catching its breath can wait)" : ""));
			}
			return true;
		}
		if (_punishing && _burstLeft <= 0)
		{
			_punishing = false;
		}
		if ((_recovering && !_cornered) || _burstPending)
		{
			EndBurst();   // a punish combo cut short by stamina ends here
			return false;
		}
		if (_burstLeft <= 0 && Time.time < _burstPauseUntil)
		{
			return false;
		}
		if (!Opening())
		{
			EndBurst();
			return false;
		}
		// 1.5.53: a combo is started above the reserve and may dip to half of it (still enough for a block), then it backs
		// off to get its breath back - like a player at 40% stamina still doing a full combo (user: "It had just looked like
		// the NPC was taking 1 swing near the end"; log: 35/85 stamina, 25 kept back, 8 a swing -> one swing at a time)
		float floor = _cornered ? 0f : Reserve * StaminaMax() * (_burstLeft > 0 ? 0.5f : 1f);
		if (StaminaNow() - AttackCost(w) < floor)
		{
			EndBurst();
			return false;
		}
		return true;
	}

	private void EndBurst()
	{
		if (_burstLeft > 0)
		{
			_burstLeft = 0;
			_burstPending = true;
		}
	}

	private int BurstSize(ItemDrop.ItemData w)
	{
		int max = ModConfig.CombatComboHits?.Value ?? 3;
		Attack a = w.m_shared.m_attack;
		int size = (a != null && a.m_attackChainLevels > 1) ? a.m_attackChainLevels : 2;
		ItemDrop.ItemData.ItemType t = w.m_shared.m_itemType;
		if (t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
		{
			size = Mathf.Min(size, 2);
		}
		size = Mathf.Clamp(size, 1, Mathf.Max(1, max));
		size = Random.Range(Mathf.Max(1, size - 1), size + 1);
		float cost = AttackCost(w);
		float floor = _cornered ? 0f : Reserve * StaminaMax() * 0.5f;
		int want = size;
		while (size > 1 && StaminaNow() - size * cost < floor)
		{
			size--;
		}
		if (size < want)
		{
			TLog($"{size}-hit combo, not {want} - saving stamina to block ({StaminaNow():F0}/{StaminaMax():F0})");
		}
		return size;
	}

	// a swing that started (DoAttack succeeded)
	private void NoteTacticSwing(ItemDrop.ItemData w)
	{
		if (_burstLeft <= 0)
		{
			_burstPending = false;
			if (_punishing)
			{
				_punishUsed = true;
			}
			_burstLeft = BurstSize(w);
			if (_burstLeft > 1)
			{
				TLog($"{_burstLeft}-hit combo at {_targetCreature?.m_name}");
			}
		}
		_burstLeft--;
		if (_burstLeft <= 0)
		{
			_burstPending = true;
		}
	}

	// below the flee threshold: run to the owner if the enemy isn't between them, through the mover (edges, water)
	private bool FleeToOwner(float dt)
	{
		Player owner = MHR_Owner.Of(this);
		if (owner == null || _targetCreature == null)
		{
			return false;
		}
		Vector3 me = base.transform.position;
		Vector3 toO = owner.transform.position - me;
		toO.y = 0f;
		if (toO.magnitude < 4f || toO.magnitude > 60f)
		{
			return false;
		}
		Vector3 toE = _targetCreature.transform.position - me;
		toE.y = 0f;
		if (Vector3.Angle(toO, toE) < 60f)
		{
			return false;
		}
		MHR_Mover.Go(_ai, dt, owner.transform.position, 3f, allowRun: true, "Flee", MHR_Mover.SpeedRun);
		if (MHR_Mover.GaveUp(_ai))
		{
			return false;
		}
		TLog("Badly hurt - running to its owner, away from " + _targetCreature.m_name);
		return true;
	}

	private void ResetTactics()
	{
		_burstLeft = 0;
		_burstPending = false;
		_stepUntil = 0f;
		_recovering = false;
		_cornered = false;
		_repositionSince = -1f;
		_evading = false;
		_punishing = false;
		_punishUsed = false;
	}
}
