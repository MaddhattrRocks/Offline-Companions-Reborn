using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.42: speech overhaul, phase 2 - what a companion is doing and why it isn't, in one place. Jobs and the movement code
// record the reason they stopped (Why); the hover text shows a status line (Describe), the idle "why" line says the real
// reason, and problems can be spoken through CompanionTalk.SayProblem. Reasons are localization keys (hc_why_*,
// hc_speech_*), so they can be translated.
internal static class CompanionStatus
{
	private class Entry
	{
		public string Text;

		public string Key;

		public string Label;

		public float At;
	}

	private static readonly Dictionary<int, Entry> s_why = new Dictionary<int, Entry>();

	// how long a reason counts as "the current one"
	internal const float Fresh = 180f;

	// record why it isn't doing its job (key = a localization key, args for {0}...); speak = also say it (a problem)
	internal static void Why(Component c, string key, bool speak = false, params object[] args)
	{
		WhyAs(c, null, key, speak, args);
	}

	// label = what the status shows in place of the job's name (1.5.42: "Mead" for the Cook job's mead reasons)
	internal static void WhyAs(Component c, string label, string key, bool speak, params object[] args)
	{
		if (c == null || string.IsNullOrEmpty(key))
		{
			return;
		}
		string text = (args != null && args.Length != 0) ? ModLocalization.LocFmt(key, args) : ModLocalization.Loc(key);
		int id = c.gameObject.GetInstanceID();
		if (!s_why.TryGetValue(id, out var e))
		{
			e = new Entry();
			s_why[id] = e;
		}
		e.Text = text;
		e.Key = key;
		e.Label = label;
		e.At = Time.time;
		if (speak)
		{
			c.GetComponent<CompanionTalk>()?.SayProblem(key, text);
		}
	}

	// it's working again: the reason no longer holds
	internal static void Clear(Component c)
	{
		if (c != null)
		{
			s_why.Remove(c.gameObject.GetInstanceID());
		}
	}

	// the current reason it isn't working, or null
	internal static string Reason(Component c, float within = Fresh)
	{
		if (c == null || !s_why.TryGetValue(c.gameObject.GetInstanceID(), out var e) || Time.time - e.At > within)
		{
			return null;
		}
		return e.Text;
	}

	// the reason as said out loud: with its label in front when it has one ("Mead: Honey 0/30 for Frost resistance mead.")
	internal static string SpokenReason(Component c, float within = Fresh)
	{
		string text = Reason(c, within);
		if (text == null)
		{
			return null;
		}
		string label = s_why[c.gameObject.GetInstanceID()].Label;
		return string.IsNullOrEmpty(label) ? text : (label + ": " + text);
	}

	private static string Label(Component c)
	{
		return (c != null && s_why.TryGetValue(c.gameObject.GetInstanceID(), out var e) && Time.time - e.At <= Fresh) ? e.Label : null;
	}

	// the hover status line: what it's doing right now, and why not when its job is stuck
	internal static string Describe(CompanionSetup cs)
	{
		if (cs == null)
		{
			return null;
		}
		Character c = cs.GetComponent<Character>();
		if (c == null || c.IsDead())
		{
			return null;
		}
		CompanionAI ai = cs.GetComponent<CompanionAI>();
		CompanionRest rest = cs.GetComponent<CompanionRest>();
		CompanionCombatAI combat = cs.GetComponent<CompanionCombatAI>();
		IdleController idle = cs.GetComponent<IdleController>();
		ZNetView nv = cs.GetComponent<ZNetView>();
		int mode = (nv != null && nv.GetZDO() != null) ? nv.GetZDO().GetInt(CompanionSetup.ActionModeHash) : 0;
		string job = JobLabel(mode);
		if (rest != null && rest.IsSleeping)
		{
			return L("hc_status_sleeping");
		}
		if (ai != null && ai.IsInCombat && combat != null && combat.TargetCreature != null)
		{
			string target = Localization.instance?.Localize(combat.TargetCreature.m_name) ?? combat.TargetCreature.m_name;
			return combat.HuntMode ? ModLocalization.LocFmt("hc_status_hunting", target) : ModLocalization.LocFmt("hc_status_fighting", target);
		}
		if (MHR_SpearThrow.UnarmedAfterThrow(ai, cs.GetComponent<Humanoid>()))
		{
			return L("hc_status_spear");
		}
		if (rest != null && rest.IsResting)
		{
			return L("hc_status_resting");
		}
		if (MHR_JobRules.OtherOrder(cs))
		{
			return L("hc_status_order");
		}
		string trip = MHR_ToolKeeper.TripText(cs);
		if (trip != null)
		{
			return trip;
		}
		string why = Reason(cs);
		if (mode != CompanionSetup.ModeFollow && mode != CompanionSetup.ModeStay && job != null)
		{
			job = Label(cs) ?? job;
			if (idle != null && idle.IsActive)
			{
				return job + " - " + (why ?? L("hc_status_nothing"));
			}
			if (cs.GetFollow() && ai != null && ai.GetFollowTarget() != null && !JobBusy(cs, mode))
			{
				return job + " - " + (why ?? L("hc_status_following_look"));
			}
			string doing = Doing(cs, mode);
			return (doing != null) ? (job + " - " + doing) : job;
		}
		if (cs.GetFollow() && ai != null && ai.GetFollowTarget() != null)
		{
			return L("hc_status_following");
		}
		if (idle != null && idle.IsActive)
		{
			return L("hc_status_idle");
		}
		return (mode == CompanionSetup.ModeStay) ? L("hc_status_staying") : L("hc_status_waiting");
	}

	// what the job is doing right now, where the job can say (1.5.42: Farm)
	private static string Doing(CompanionSetup cs, int mode)
	{
		if (mode == CompanionSetup.ModeFarm)
		{
			FarmController farm = cs.GetComponent<FarmController>();
			return (farm != null && farm.IsActive) ? farm.DoingText() : null;
		}
		return null;
	}

	private static bool JobBusy(CompanionSetup cs, int mode)
	{
		switch (mode)
		{
		case CompanionSetup.ModeGatherWood:
		case CompanionSetup.ModeGatherStone:
		case CompanionSetup.ModeGatherOre:
		case CompanionSetup.ModeForage:
			return cs.GetComponent<HarvestController>()?.IsActive ?? false;
		case CompanionSetup.ModeHunt:
			return cs.GetComponent<HuntController>()?.IsActive ?? false;
		case CompanionSetup.ModeFish:
			return cs.GetComponent<FishController>()?.IsActive ?? false;
		default:
			return false;
		}
	}

	internal static string JobLabel(int mode)
	{
		switch (mode)
		{
		case CompanionSetup.ModeGatherWood:
			return L("hc_status_job_wood");
		case CompanionSetup.ModeGatherStone:
			return L("hc_status_job_stone");
		case CompanionSetup.ModeGatherOre:
			return L("hc_status_job_ore");
		case CompanionSetup.ModeForage:
			return L("hc_status_job_forage");
		case CompanionSetup.ModeSmelt:
			return L("hc_status_job_smelt");
		case CompanionSetup.ModeHunt:
			return L("hc_status_job_hunt");
		case CompanionSetup.ModeFarm:
			return L("hc_status_job_farm");
		case CompanionSetup.ModeFish:
			return L("hc_status_job_fish");
		case CompanionSetup.ModeRepairBuildings:
			return L("hc_status_job_upkeep");
		case CompanionSetup.ModeCook:
			return L("hc_status_job_cook");
		default:
			return null;
		}
	}

	private static string L(string key)
	{
		return ModLocalization.Loc(key);
	}
}
