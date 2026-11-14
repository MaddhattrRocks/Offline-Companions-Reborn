using UnityEngine;

namespace Companions;

// 1.5.95: mining like a player (user, copper vein reports: "bare-handed mining animation a few times and stop", "a
// couple of pickaxe swings then stop", "depends on the specific vein"; John: "dig the dirt around the vein first, then
// hit the vein to pop it").
//  - Ore job, outdoors, split veins (MineRock5: copper, silver, flametal...): it works on the nearest part of the vein.
//    When dirt covers that part, it swings down at it and the game's own dig takes the dirt away (Attack.SpawnOnHitTerrain,
//    allowed only over and around that part of this vein, with the game's ward and no-build checks). Parts left with no
//    ground under them break together when the next part breaks (MineRock5.CheckSupport, the game's rule).
//  - Dungeons (muddy scrap piles): no digging, it just mines.
//  - Stamina: the game's swing already charges the player's cost (attack stamina less 33% at skill 100 of the tool's
//    skill); the breather check uses the same number.
//  - When it gives up on a rock it says why (buried, can't dig, out of reach, too hard, no tool).
public partial class HarvestController
{
	internal enum GiveUpWhy
	{
		Buried,
		CantDig,
		OutOfReach,
		TooHard,
		NoTool
	}

	private bool _digAim;

	private Vector3 _digPoint;

	private GameObject _digFor;

	private int _digCount;

	private int _digNoProgress;

	private bool _digPending;

	private Vector3 _digPendingAt;

	private float _digPendingGround;

	private const int DigMaxPerVein = 80;

	private const int DigMaxNoProgress = 4;

	// the player's swing cost (Attack.GetAttackStamina without the player-only gear modifiers): the attack's stamina,
	// less 33% at skill 100 of the tool's own skill (Pickaxes for a pickaxe, Axes for an axe)
	private float SwingStamina(ItemDrop.ItemData item, bool secondary)
	{
		if (item?.m_shared == null)
		{
			return 0f;
		}
		Attack a = secondary ? item.m_shared.m_secondaryAttack : item.m_shared.m_attack;
		if (a == null || a.m_attackStamina <= 0f)
		{
			return 0f;
		}
		float skill = _character != null ? _character.GetSkillFactor(item.m_shared.m_skillType) : 0f;
		return a.m_attackStamina - a.m_attackStamina * 0.33f * skill;
	}

	private static bool Worn(ItemDrop.ItemData item)
	{
		return item != null && item.m_shared != null && item.m_shared.m_useDurability && item.m_durability <= 0f;
	}

	private static float GroundAt(Vector3 p)
	{
		return ZoneSystem.instance != null ? ZoneSystem.instance.GetGroundHeight(p) : p.y;
	}

	// a dungeon (or any interior): the outdoor ground height is far below - the rock's own height counts there
	private bool InDungeonAt(Vector3 p)
	{
		if (_character != null && _character.InInterior())
		{
			return true;
		}
		return p.y - GroundAt(p) > 50f;
	}

	// digging is for the Ore job on a split vein outdoors
	private bool DigMode()
	{
		return _target != null && _targetType == "MineRock5" && GetMode() == 3 && _character != null && !InDungeonAt(base.transform.position);
	}

	// each frame while attacking: is the part it's working on under the dirt? Then it aims down at it to dig
	private void UpdateDigAim()
	{
		_digAim = false;
		if (!DigMode())
		{
			return;
		}
		if (_digFor != _target)
		{
			_digFor = _target;
			_digCount = 0;
			_digNoProgress = 0;
			_digPending = false;
		}
		GetEffectiveTargetPosition();
		Collider seg = _rock5CurrentSegment;
		if (seg == null || !seg.gameObject.activeSelf)
		{
			return;
		}
		Vector3 eye = Eye();
		Vector3 pt = SafeClosestPoint(seg, eye);
		if (pt.y >= GroundAt(pt) - 0.15f)
		{
			return;   // its near side is in the open - hit the rock
		}
		_digAim = true;
		_digPoint = pt;
	}

	private Vector3 Eye()
	{
		return (_character != null && _character.m_eye != null) ? _character.m_eye.position : base.transform.position + Vector3.up * 1.6f;
	}

	// SpawnOnHitTerrain asks: may this swing's hit on the ground dig? Only over and around the part it's digging out
	internal bool AllowDig(Vector3 hitPoint)
	{
		if (_state != HarvestState.Attacking || !_digAim || !DigMode())
		{
			return false;
		}
		Collider seg = _rock5CurrentSegment;
		if (seg == null || !seg.gameObject.activeSelf)
		{
			return false;
		}
		Bounds b = seg.bounds;
		float dx = Mathf.Max(0f, Mathf.Max(b.min.x - hitPoint.x, hitPoint.x - b.max.x));
		float dz = Mathf.Max(0f, Mathf.Max(b.min.z - hitPoint.z, hitPoint.z - b.max.z));
		if (Mathf.Sqrt(dx * dx + dz * dz) > 1.5f)
		{
			return false;
		}
		return hitPoint.y >= b.min.y - 0.3f;
	}

	internal void NotifyDig(Vector3 hitPoint)
	{
		_lastHitAt = Time.time;   // a dig is a swing that landed
		_noDamageSwings = 0;
		_digCount++;
		// digs don't count toward the 20-hit / 30-try limit for the rock itself
		_swingCount = Mathf.Max(0, _swingCount - 1);
		_totalAttempts = Mathf.Max(0, _totalAttempts - 1);
		_digPending = true;
		_digPendingAt = hitPoint;
		_digPendingGround = GroundAt(hitPoint);
		if (CompanionsPlugin.LogDetailed) Log($"Dug at {hitPoint:F1} (dig {_digCount}/{DigMaxPerVein} on \"{_target?.name}\")");
	}

	// at each swing while digging: did the last dig take any dirt away? (a ward, a no-build area or the game's dig depth
	// limit stop it). True = give up.
	private bool DigStalled()
	{
		if (_digPending)
		{
			_digPending = false;
			float now = GroundAt(_digPendingAt);
			if (_digPendingGround - now < 0.05f)
			{
				_digNoProgress++;
			}
			else
			{
				_digNoProgress = 0;
			}
		}
		return _digNoProgress >= DigMaxNoProgress;
	}

	// says why it's leaving a rock (Normal log + one spoken line per reason; the numbers only in the Detailed log)
	internal void SayGaveUp(GiveUpWhy why, string detail)
	{
		string thing = _target != null ? MHR_Items.NiceName(_target) : (GetMode() == 1 ? "?" : ModLocalization.Loc("hc_tool_pickaxe"));
		string key = why switch
		{
			GiveUpWhy.Buried => "hc_speech_vein_buried",
			GiveUpWhy.CantDig => "hc_speech_vein_cant_dig",
			GiveUpWhy.OutOfReach => "hc_speech_vein_out_of_reach",
			GiveUpWhy.TooHard => "hc_speech_vein_too_hard",
			_ => "hc_speech_no_working_tool"
		};
		string what = why switch
		{
			GiveUpWhy.Buried => "the rest is buried too deep",
			GiveUpWhy.CantDig => "it can't dig here",
			GiveUpWhy.OutOfReach => "it can't reach it",
			GiveUpWhy.TooHard => "it needs a better pickaxe",
			_ => "no working tool left"
		};
		if (CompanionsPlugin.LogNormal) LogInfo("Leaving \"" + thing + "\" - " + what);
		if (CompanionsPlugin.LogDetailed && !string.IsNullOrEmpty(detail)) Log("  (" + detail + ")");
		if (why == GiveUpWhy.NoTool)
		{
			string tool = ModLocalization.Loc(GetMode() == 1 ? "hc_tool_axe" : "hc_tool_pickaxe");
			_talk?.SayProblem("tool:none:" + GetMode(), string.Format(ModLocalization.Loc(key), tool));
			return;
		}
		_talk?.SayProblem("vein:" + why, string.Format(ModLocalization.Loc(key), thing));
	}

	// only rocks (Stone/Ore) get the spoken reason; trees keep their own log lines
	private bool RockGiveUp()
	{
		return IsRockLikeTarget() && (GetMode() == 2 || GetMode() == 3);
	}
}
