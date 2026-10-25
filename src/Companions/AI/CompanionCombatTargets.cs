using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.65: which enemy to fight, like a player (user, 2026-10-23, passing on a player's report: "that psycho runs towards
// everything they see and especially don't see (like from behind a wall)", "it ignores every recall or command when they
// have anything to fight", "it still tries to get to another seeker behind the opposite blocked door. We don't look for
// easy and near targets!"; user: "NPC should only attack mobs they have a path too, or have line of sight for ranged
// weapons, with a priority on close mobs ... Only open a door with nearby mobs when commanded. Indoor spaces they should
// swap to only going after mobs that have actual aggro"; "the 'defensive fight' should be the default in a dungeon").
//
// It used to take the game's BaseAI.FindEnemy: the nearest enemy it could see or HEAR (20 m, through walls and doors),
// straight-line distance, and the chase opened any door on the way. Now:
// - only enemies it can get at: in plain sight (nothing solid between its eyes and the enemy - walls, doors, rock), or
//   right next to it. Hearing alone doesn't start a fight.
// - the best of those: whatever is attacking it first, then whatever is after you, closest first; the current target
//   is kept unless another is clearly better.
// - indoors (a dungeon, mine or cave, or under a roof): only enemies that are alerted and after you or a companion
//   ([Combat] IndoorAggroOnly).
// - in a dungeon, mine or cave: a defensive fight ([Combat] DungeonDefensive) - only what comes within DefendRange of
//   you, so it stays with you instead of running off down the corridor.
// - a fight never opens a door (MHR_Mover refuses for the "Combat" tag); an enemy behind one is let go for a while.
//   An attack order (DirectEngage) still does.
// - a recall or move order drops the fight and starts no new one for [Combat] OrderHoldTime, except against something
//   hitting it up close.
public partial class CompanionCombatAI
{
	private Character _ordered;

	private float _holdUntil;

	private readonly Dictionary<Character, float> _ignoreUntil = new Dictionary<Character, float>();

	private float _noSightSince = -1f;

	private float _indoorCheckAt;

	private bool _indoors;

	private float _nextTargetLog;

	internal bool Holding => Time.time < _holdUntil;

	internal bool IsOrdered(Character c)
	{
		return c != null && c == _ordered;
	}

	// an attack order: this target may be chased anywhere, doors included
	internal void Order(Character target)
	{
		_ordered = target;
		_holdUntil = 0f;
		_ignoreUntil.Remove(target);
	}

	// a recall or move order: let go of the fight and start no new one for a while
	internal void HoldForOrder(string what)
	{
		float secs = ModConfig.CombatOrderHoldTime?.Value ?? 10f;
		if (_engaged)
		{
			CompanionsPlugin.Log.LogDebug($"[Combat] {what} - leaving {(_targetCreature != null ? _targetCreature.m_name : "the fight")}, no new fights for {secs:F0} s");
			Disengage();
		}
		_ordered = null;
		_holdUntil = Time.time + secs;
	}

	// while holding for an order, only something hitting it up close is fought
	internal bool MayFightBack(Character attacker)
	{
		if (!Holding || attacker == null)
		{
			return true;
		}
		return Vector3.Distance(base.transform.position, attacker.transform.position) - attacker.GetRadius() < 3f;
	}

	internal bool IndoorsNow()
	{
		if (Time.time >= _indoorCheckAt)
		{
			_indoorCheckAt = Time.time + 1f;
			_indoors = _character != null && (_character.InInterior() || Physics.Raycast(base.transform.position + Vector3.up * 2f, Vector3.up, 30f, LayerMask.GetMask("piece", "static_solid", "Default")));
		}
		return _indoors;
	}

	private bool Defensive()
	{
		return _character != null && _character.InInterior() && (ModConfig.CombatDungeonDefensive?.Value ?? true);
	}

	// nothing solid between it and the enemy, or the enemy right next to it
	internal bool CanGetAt(Character c)
	{
		if (c == null)
		{
			return false;
		}
		float d = Vector3.Distance(base.transform.position, c.transform.position) - c.GetRadius();
		return d < 2.5f || MHR_BowDraw.ClearShot(_character, c);
	}

	private Character _latched;

	private float _latchedCheckAt;

	// 1.5.73: a tick latched on to it (checked every 0.25 s)
	internal Character Latched()
	{
		if (Time.time >= _latchedCheckAt)
		{
			_latchedCheckAt = Time.time + 0.25f;
			_latched = MHR_TickShake.LatchedOn(_character);
		}
		return _latched;
	}

	private bool LatchedTarget()
	{
		return _targetCreature != null && Latched() == _targetCreature;
	}

	// the enemy to fight now, or null
	internal Character PickTarget()
	{
		// 1.5.73: something latched on and feeding comes first, orders or not
		Character latched = Latched();
		if (latched != null && !latched.IsDead())
		{
			return latched;
		}
		if (Holding || _character == null)
		{
			return null;
		}
		bool indoors = IndoorsNow();
		bool aggroOnly = indoors && (ModConfig.CombatIndoorAggroOnly?.Value ?? true);
		bool defensive = Defensive();
		float defendRange = ModConfig.CombatDefendRange?.Value ?? 8f;
		Player owner = MHR_Owner.Of(_setup);
		Vector3 me = base.transform.position;
		float maxRange = Mathf.Max(_ai.m_viewRange, 20f);
		if (MHR_IsProjectileWeapon(_humanoid.GetCurrentWeapon()) || _setup?.GetCombatStance() == 2)
		{
			maxRange = Mathf.Max(maxRange, 30f);
		}
		Character best = null;
		float bestScore = float.MaxValue;
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c == null || c == _character || c.IsDead() || c.m_aiSkipTarget || !_ai.IsEnemy(c) || IsPassiveAnimal(c) || MHR_Threat.IgnoredInWater(c))
			{
				continue;
			}
			if (_ignoreUntil.TryGetValue(c, out float until) && Time.time < until)
			{
				continue;
			}
			float d = Vector3.Distance(me, c.transform.position);
			if (d > maxRange)
			{
				continue;
			}
			BaseAI cai = c.GetBaseAI();
			if (cai != null && cai.IsSleeping())
			{
				continue;
			}
			Character itsTarget = cai?.GetTargetCreature();
			bool onMe = itsTarget == _character;
			if (!MHR_Threat.IsAfterUs(c))
			{
				continue;
			}
			if (aggroOnly && cai != null && !cai.IsAlerted())
			{
				continue;
			}
			if (defensive && owner != null && Vector3.Distance(owner.transform.position, c.transform.position) > defendRange && !(onMe && d - c.GetRadius() < 3f))
			{
				continue;
			}
			if (!CanGetAt(c))
			{
				continue;
			}
			float score = d - (onMe ? 6f : 0f) - (itsTarget != null && itsTarget.IsPlayer() ? 2f : 0f) - (c == _targetCreature ? 3f : 0f);
			if (score < bestScore)
			{
				bestScore = score;
				best = c;
			}
		}
		return best;
	}

	// The current target is still worth chasing? Lost from sight for 3 s (gone round a corner, behind a door), or - in a
	// dungeon - gone further than DefendRange from you: let it go, unless it was ordered or is hitting it.
	private void CheckTargetStillValid()
	{
		if (_targetCreature == null || HuntMode || IsOrdered(_targetCreature))
		{
			_noSightSince = -1f;
			return;
		}
		Character c = _targetCreature;
		bool onMe = c.GetBaseAI()?.GetTargetCreature() == _character;
		float d = Vector3.Distance(base.transform.position, c.transform.position) - c.GetRadius();
		if (onMe && d < 3f)
		{
			_noSightSince = -1f;
			return;
		}
		if (Defensive())
		{
			Player owner = MHR_Owner.Of(_setup);
			float range = (ModConfig.CombatDefendRange?.Value ?? 8f) + 3f;
			if (owner != null && Vector3.Distance(owner.transform.position, c.transform.position) > range)
			{
				TargetLog($"[Combat] {c.m_name} is more than {range:F0} m from you - fighting defensively, letting it come to us");
				_ignoreUntil[c] = Time.time + 3f;
				_targetCreature = null;
				return;
			}
		}
		if (CanGetAt(c))
		{
			_noSightSince = -1f;
			return;
		}
		if (_noSightSince < 0f)
		{
			_noSightSince = Time.time;
		}
		else if (Time.time - _noSightSince > 3f)
		{
			TargetLog($"[Combat] Lost sight of {c.m_name} (behind a wall or door) - letting it go");
			_ignoreUntil[c] = Time.time + 8f;
			_targetCreature = null;
			_noSightSince = -1f;
		}
	}

	// the chase came to a closed door: a fight doesn't open doors (an order does)
	private void DoorInTheWay()
	{
		if (_targetCreature == null)
		{
			return;
		}
		TargetLog($"[Combat] {_targetCreature.m_name} is behind a closed door - not opening it in a fight");
		_ignoreUntil[_targetCreature] = Time.time + 15f;
		_targetCreature = null;
		MHR_Mover.Release(_ai);
	}

	private void TargetLog(string msg)
	{
		if (Time.time >= _nextTargetLog)
		{
			_nextTargetLog = Time.time + 1f;
			CompanionsPlugin.Log.LogDebug(msg);
		}
	}
}
