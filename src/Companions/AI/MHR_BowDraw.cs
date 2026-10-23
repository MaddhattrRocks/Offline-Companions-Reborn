using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.61: companions draw a bow like a player holding attack (user, 1.5.60 test: "Does the NPC actually draw the bow?" -
// it didn't: the shot went off at once with m_bowDraw switched off, so every arrow was instant and fully drawn).
// Mirrors Player.UpdateAttackBowDraw: Attack.StartDraw (ammo), m_attackDrawTime counts up, the draw animation bool and
// "drawpercent", draw stamina drain (half once fully drawn); released at full draw with StartAttack, which reads
// Humanoid.GetAttackDrawPercentage - so the draw time (m_drawDurationMin, down to 20% with Bows skill), the spread and
// the arrow speed are the game's own.
internal static class MHR_BowDraw
{
	private class State
	{
		public float LastTick;

		public float Started;
	}

	private static readonly Dictionary<int, State> s_states = new Dictionary<int, State>();

	internal static bool NeedsDraw(Humanoid h)
	{
		ItemDrop.ItemData w = h?.GetCurrentWeapon();
		return w?.m_shared?.m_attack != null && w.m_shared.m_attack.m_bowDraw;
	}

	internal static bool IsDrawing(Humanoid h)
	{
		return h != null && s_states.ContainsKey(h.GetInstanceID());
	}

	// Call every frame the companion wants to shoot (aimed, in range). Returns true on the frame the arrow is loosed.
	internal static bool Tick(Humanoid h, Character target, float dt)
	{
		ItemDrop.ItemData w = h.GetCurrentWeapon();
		if (w == null || !NeedsDraw(h))
		{
			return false;
		}
		int id = h.GetInstanceID();
		if (!s_states.TryGetValue(id, out State st))
		{
			if (h.InAttack() || h.IsBlocking() || !w.m_shared.m_attack.StartDraw(h, w))
			{
				return false;
			}
			st = new State { Started = Time.time };
			s_states[id] = st;
			Attack a = w.m_shared.m_attack;
			float full = Mathf.Lerp(a.m_drawDurationMin, a.m_drawDurationMin * 0.2f, h.GetSkillFactor(w.m_shared.m_skillType));
			CompanionsPlugin.Log.LogDebug($"[Bow] Drawing {w.m_shared.m_name} - full draw in {full:F1} s");
			ReflectionHelper.TrySetAttackDrawTime(h, 0.0001f);
			w.m_shared.m_holdStartEffect.Create(h.transform.position, Quaternion.identity, h.transform);
		}
		st.LastTick = Time.time;
		float pct = h.GetAttackDrawPercentage();
		float drain = w.GetDrawStaminaDrain() * (pct >= 1f ? 0.5f : 1f);
		if (drain > 0f && !h.HaveStamina())
		{
			Cancel(h);
			return false;
		}
		ReflectionHelper.TrySetAttackDrawTime(h, ReflectionHelper.GetAttackDrawTime(h) + dt);
		pct = h.GetAttackDrawPercentage();
		ZSyncAnimation zanim = h.GetComponent<ZSyncAnimation>();
		string state = w.m_shared.m_attack.m_drawAnimationState;
		if (zanim != null && !string.IsNullOrEmpty(state))
		{
			zanim.SetBool(state, value: true);
			zanim.SetFloat("drawpercent", pct);
		}
		if (drain > 0f)
		{
			h.UseStamina(drain * dt);
		}
		if (pct < 1f)
		{
			return false;
		}
		bool shot = h.StartAttack(target, secondaryAttack: false);
		CompanionsPlugin.Log.LogDebug($"[Bow] {(shot ? "Loosed" : "Couldn't loose")} at full draw after {Time.time - st.Started:F1} s");
		Cancel(h);
		return shot;
	}

	// Lets go of the string without shooting (target gone, blocking, out of stamina).
	internal static void Cancel(Humanoid h)
	{
		if (h == null)
		{
			return;
		}
		s_states.Remove(h.GetInstanceID());
		ReflectionHelper.TrySetAttackDrawTime(h, 0f);
		string state = h.GetCurrentWeapon()?.m_shared?.m_attack?.m_drawAnimationState;
		ZSyncAnimation zanim = h.GetComponent<ZSyncAnimation>();
		if (zanim != null && !string.IsNullOrEmpty(state))
		{
			zanim.SetBool(state, value: false);
		}
	}

	// Call every frame: a draw nobody has ticked for a moment (the shot was given up) is let go.
	internal static void Maintain(Humanoid h)
	{
		if (h != null && s_states.TryGetValue(h.GetInstanceID(), out State st) && Time.time - st.LastTick > 0.5f)
		{
			Cancel(h);
		}
	}
}
