using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.70: diagnostic - a companion that rises more than 2 m in under 2 s without a jump or a ladder (log 31cb468a: from
// the front door at floor level to 4.4 m above the room within 3 s, no jump/ladder/warp line, then 6 minutes up there -
// user: "There is no way onto the roof"). Logs where it was and where it ended up, what it was doing and the building
// pieces round both spots, to see whether the physics pushed it up out of a piece.
internal static class MHR_RiseWatch
{
	private struct Sample
	{
		public float At;

		public Vector3 Pos;

		public string Doing;

		public bool Jumped;
	}

	private const float Every = 0.25f;

	private const float Window = 2f;

	private const float Rise = 2f;

	private static readonly Dictionary<int, List<Sample>> s_samples = new Dictionary<int, List<Sample>>();

	private static readonly Dictionary<int, float> s_next = new Dictionary<int, float>();

	private static readonly Dictionary<int, float> s_quietUntil = new Dictionary<int, float>();

	internal static void Tick(CompanionSetup cs)
	{
		try
		{
			if (cs == null)
			{
				return;
			}
			int id = cs.GetInstanceID();
			float now = Time.time;
			if (s_next.TryGetValue(id, out var next) && now < next)
			{
				return;
			}
			s_next[id] = now + Every;
			Character c = cs.GetComponent<Character>();
			CompanionAI ai = cs.GetComponent<CompanionAI>();
			if (c == null || c.IsDead() || c.IsAttached())
			{
				s_samples.Remove(id);
				return;
			}
			if (!s_samples.TryGetValue(id, out var list))
			{
				list = new List<Sample>();
				s_samples[id] = list;
			}
			Vector3 pos = cs.transform.position;
			bool jumped = !c.IsOnGround() && c.GetVelocity().y > 1f || (ai != null && MHR_Mover.Climbing(ai));
			list.Add(new Sample { At = now, Pos = pos, Doing = CompanionStatus.Describe(cs), Jumped = jumped });
			while (list.Count > 0 && now - list[0].At > Window)
			{
				list.RemoveAt(0);
			}
			if (s_quietUntil.TryGetValue(id, out var quiet) && now < quiet)
			{
				return;
			}
			Sample low = list[0];
			for (int i = 1; i < list.Count; i++)
			{
				if (list[i].Pos.y < low.Pos.y)
				{
					low = list[i];
				}
			}
			float rise = pos.y - low.Pos.y;
			float across = new Vector2(pos.x - low.Pos.x, pos.z - low.Pos.z).magnitude;
			if (rise < Rise || across > 8f)
			{
				return;
			}
			bool anyJump = false;
			foreach (Sample s in list)
			{
				if (s.At >= low.At && s.Jumped)
				{
					anyJump = true;
				}
			}
			if (anyJump && rise < 3f)
			{
				return;
			}
			s_quietUntil[id] = now + 5f;
			CompanionsPlugin.Log.LogWarning($"[Diag] \"{c.m_name}\" rose {rise:F1} m in {now - low.At:F1} s from {low.Pos:F1} to {pos:F1} ({across:F1} m across){(anyJump ? ", jumping" : ", no jump or ladder")}; doing: {low.Doing ?? "?"} -> {CompanionStatus.Describe(cs) ?? "?"}; pieces at the start: {PiecesNear(low.Pos)}; at the end: {PiecesNear(pos)}");
		}
		catch
		{
		}
	}

	private static string PiecesNear(Vector3 p)
	{
		HashSet<string> names = new HashSet<string>();
		foreach (Collider col in Physics.OverlapSphere(p + Vector3.up * 0.9f, 1.2f))
		{
			Piece piece = col != null ? col.GetComponentInParent<Piece>() : null;
			if (piece != null)
			{
				names.Add(piece.m_name + " at " + piece.transform.position.ToString("F1"));
			}
			if (names.Count >= 8)
			{
				break;
			}
		}
		return names.Count > 0 ? string.Join(", ", names) : "none";
	}
}
