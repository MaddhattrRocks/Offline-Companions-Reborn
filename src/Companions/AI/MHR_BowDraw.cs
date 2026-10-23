using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.61: companions draw a bow like a player holding attack (user, 1.5.60 test: "Does the NPC actually draw the bow?" -
// it didn't: the shot went off at once with m_bowDraw switched off, so every arrow was instant and fully drawn).
// Mirrors Player.UpdateAttackBowDraw: Attack.StartDraw (ammo), m_attackDrawTime counts up, the draw animation bool and
// "drawpercent", draw stamina drain (half once fully drawn); released with StartAttack, which reads
// Humanoid.GetAttackDrawPercentage - so the draw time (m_drawDurationMin, down to 20% with Bows skill), the spread and
// the arrow speed are the game's own.
// 1.5.62: the draw runs on the clock like the player's (FixedUpdate adds time every step while attack is held): Maintain
// advances it every frame, also while turning or stepping; Tick only says "still want to shoot" and lets go of the string
// once it's fully drawn, lined up and nothing is in the way (user, 1.5.61 test: 2.4 s draws took 6-8 s - the time only
// ran on the frames it was already lined up - and it shot into a hill).
internal static class MHR_BowDraw
{
	private class State
	{
		public float LastWant;

		public float Started;

		public float Held;
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

	// Call every frame the companion wants to shoot (target in range, roughly facing it). Starts the draw if it isn't
	// drawing; looses the arrow once fully drawn, aimed and clear. Returns true on the frame the arrow is loosed.
	internal static bool Tick(Humanoid h, Character target, bool aimed, bool clear)
	{
		ItemDrop.ItemData w = h?.GetCurrentWeapon();
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
			LogBowOnce(h, "companion");
		}
		st.LastWant = Time.time;
		if (h.GetAttackDrawPercentage() < 1f)
		{
			return false;
		}
		if (!aimed || !clear)
		{
			if (!clear && st.Held == 0f)
			{
				CompanionsPlugin.Log.LogDebug("[Bow] Fully drawn but something is in the way of the shot - holding");
			}
			st.Held += Time.deltaTime;
			return false;
		}
		bool shot = h.StartAttack(target, secondaryAttack: false);
		CompanionsPlugin.Log.LogDebug($"[Bow] {(shot ? "Loosed" : "Couldn't loose")} after {Time.time - st.Started:F1} s" + (st.Held > 0.05f ? $" (held at full draw {st.Held:F1} s)" : ""));
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

	// Call every frame (CompanionCombatAI.Update): the draw time, animation and stamina, like Player.UpdateAttackBowDraw.
	// Let go when nothing has wanted the shot for a second, the bow was put away, it blocks or runs out of stamina.
	internal static void Maintain(Humanoid h, float dt)
	{
		LogPlayerBowOnce();
		if (h == null || !s_states.TryGetValue(h.GetInstanceID(), out State st))
		{
			return;
		}
		ItemDrop.ItemData w = h.GetCurrentWeapon();
		if (w == null || !NeedsDraw(h) || h.IsBlocking() || Time.time - st.LastWant > 1f)
		{
			Cancel(h);
			return;
		}
		float pct = h.GetAttackDrawPercentage();
		float drain = w.GetDrawStaminaDrain() * (pct >= 1f ? 0.5f : 1f);
		if (drain > 0f && !h.HaveStamina())
		{
			CompanionsPlugin.Log.LogDebug("[Bow] Out of stamina - letting go of the string");
			Cancel(h);
			return;
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
	}

	// A straight line from the eyes and from the bow hand to the middle of the target that no terrain, rock or building
	// crosses (a player doesn't shoot into the hill in front of them).
	internal static bool ClearShot(Character me, Character target)
	{
		if (me == null || target == null)
		{
			return true;
		}
		Vector3 to = target.GetCenterPoint();
		return ClearLine(me.GetEyePoint(), to, target) && ClearLine(me.transform.position + Vector3.up * 1.3f + me.transform.forward * 0.3f, to, target);
	}

	private static int s_blockMask;

	private static bool ClearLine(Vector3 from, Vector3 to, Character target)
	{
		if (s_blockMask == 0)
		{
			s_blockMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle");
		}
		Vector3 d = to - from;
		float len = d.magnitude;
		if (len < 0.5f)
		{
			return true;
		}
		RaycastHit[] hits = Physics.RaycastAll(from, d / len, len - 0.3f, s_blockMask, QueryTriggerInteraction.Ignore);
		foreach (RaycastHit hit in hits)
		{
			if (hit.collider != null && hit.collider.GetComponentInParent<Character>() == null)
			{
				return false;
			}
		}
		return true;
	}

	// 1.5.62: diagnostic - the user sees the draw pose but the bow string doesn't move. The game code never touches the
	// string, so it's in the bow model or the character's animation; log what's on the bow in the hand, once for a
	// companion and once for you, to compare.
	private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_leftInst = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");

	private static bool s_loggedCompanion;

	private static bool s_loggedPlayer;

	private static void LogPlayerBowOnce()
	{
		Player p = Player.m_localPlayer;
		if (!s_loggedPlayer && p != null && p.IsDrawingBow() && p.GetAttackDrawPercentage() > 0.5f)
		{
			LogBowOnce(p, "you");
		}
	}

	private static void LogBowOnce(Humanoid h, string who)
	{
		bool player = who == "you";
		if (player ? s_loggedPlayer : s_loggedCompanion)
		{
			return;
		}
		if (player)
		{
			s_loggedPlayer = true;
		}
		else
		{
			s_loggedCompanion = true;
		}
		try
		{
			VisEquipment ve = h.GetComponent<VisEquipment>();
			GameObject bow = ve != null ? s_leftInst(ve) : null;
			if (bow == null)
			{
				CompanionsPlugin.Log.LogInfo($"[BowDiag] {who}: no bow model in the left hand");
				return;
			}
			string comps = string.Join(", ", bow.GetComponentsInChildren<Component>(includeInactive: true).Where((Component c) => c != null && !(c is Transform)).Select((Component c) => c.GetType().Name + "@" + c.gameObject.name).Distinct().Take(30));
			string anims = string.Join("; ", bow.GetComponentsInChildren<Animator>(includeInactive: true).Select((Animator a) => a.gameObject.name + " ctrl=" + (a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "none") + " enabled=" + a.enabled + " params=[" + string.Join(",", a.parameters.Select((AnimatorControllerParameter x) => x.name)) + "]"));
			string bones = string.Join("; ", bow.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true).Select((SkinnedMeshRenderer s) => s.gameObject.name + " bones=" + (s.bones?.Length ?? 0) + " root=" + (s.rootBone != null ? s.rootBone.name : "none")));
			Animator body = h.GetComponentInChildren<Animator>();
			CompanionsPlugin.Log.LogInfo($"[BowDiag] {who}: bow model \"{bow.name}\" under \"{(bow.transform.parent != null ? bow.transform.parent.name : "none")}\" | components: {comps} | animators: {(anims.Length > 0 ? anims : "none")} | skinned: {(bones.Length > 0 ? bones : "none")} | body animator: cull={body?.cullingMode} drawpercent={(body != null ? body.GetFloat("drawpercent").ToString("F2") : "?")}");
		}
		catch (System.Exception ex)
		{
			CompanionsPlugin.Log.LogInfo("[BowDiag] " + who + ": " + ex.Message);
		}
	}
}
