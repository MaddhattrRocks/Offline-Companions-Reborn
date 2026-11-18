using UnityEngine;

namespace Companions;

// 1.5.95/1.5.96: mining like a player - shared helpers. The vein work itself (dig every side and below, then pop) is in
// HarvestVein.cs, getting out of a pit in HarvestPit.cs, dungeons in HarvestDungeon.cs.
//  - Stamina: the game's swing already charges the player's cost (attack stamina less 33% at skill 100 of the tool's
//    skill); the breather check uses the same number.
//  - When it gives up on a rock it says why (buried, ward, can't dig, out of reach, too hard, no tool, stuck in a hole).
public partial class HarvestController
{
	internal enum GiveUpWhy
	{
		Buried,
		CantDig,
		OutOfReach,
		TooHard,
		NoTool,
		Ward,
		StuckInHole
	}

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

	private Vector3 Eye()
	{
		return (_character != null && _character.m_eye != null) ? _character.m_eye.position : base.transform.position + Vector3.up * 1.6f;
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
			GiveUpWhy.Ward => "hc_speech_vein_ward",
			GiveUpWhy.StuckInHole => "hc_speech_stuck_in_hole",
			_ => "hc_speech_no_working_tool"
		};
		string what = why switch
		{
			GiveUpWhy.Buried => "the rest is buried too deep",
			GiveUpWhy.CantDig => "it can't dig here",
			GiveUpWhy.OutOfReach => "it can't reach it",
			GiveUpWhy.TooHard => "it needs a better pickaxe",
			GiveUpWhy.Ward => "someone else's ward covers it",
			GiveUpWhy.StuckInHole => "it's stuck in the hole it dug",
			_ => "no working tool left"
		};
		_lastGiveUpText = what + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")");
		// 1.5.98 L5: the vein clearing - the try on that part ends (a round), nothing said, it stays on the vein
		if (_ovPart != null && _vFor != null)
		{
			if (CompanionsPlugin.LogNormal) LogInfo("The try on the part \"" + (_ovPin != null ? _ovPin.name : "?") + "\" ended - " + _lastGiveUpText);
			return;
		}
		if (CompanionsPlugin.LogNormal) LogInfo("Leaving \"" + thing + "\" - " + what);
		if (CompanionsPlugin.LogDetailed && !string.IsNullOrEmpty(detail)) Log("  (" + detail + ")");
		if (why == GiveUpWhy.NoTool)
		{
			string tool = ModLocalization.Loc(GetMode() == 1 ? "hc_tool_axe" : "hc_tool_pickaxe");
			_talk?.SayProblem("tool:none:" + GetMode(), string.Format(ModLocalization.Loc(key), tool));
			return;
		}
		if (why == GiveUpWhy.StuckInHole)
		{
			_talk?.SayProblem("vein:" + why, ModLocalization.Loc(key));
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
