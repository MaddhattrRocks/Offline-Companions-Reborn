using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.82: getting on a ship like a player (user, log e26ca237: "The NPC does not seem able to use the ladder to get onto the
// ship or jump to it" / "The NPC should be able to get on a ship if I can get on a ship. It might be worth having the follow
// in my footsteps fall back as well"). The log: it picked the ship's ladder but never got within its 2 m use range (it hangs
// on the hull over the water), the ship isn't on the route map, jumps never landed on a deck, and the board order just
// walked at the ship's middle with the game's MoveTo and put it in a seat after 30 s anyway.
//
// Boarding - on the board order, or on its own while following you when you're aboard a ship close by - is, every frame:
// aboard (standing on the ship's deck) -> a free seat, else stand on deck; next to one of the ship's ladders -> use it
// (Ladder.Interact puts it at the top, as it does you); on the dock or shore edge with the deck in jump reach -> jump aboard;
// otherwise walk (or swim, stamina allowing) to the nearest ladder through MHR_Mover. Stuck for a while -> your own steps
// onto the ship (where you stood before you got on, then what you did: a jump, or the ladder). Still nothing -> it waits on
// the shore ("can't get aboard"), no teleporting.
internal static class MHR_Board
{
	private class S
	{
		public Ship Ship;
		public Chair Chair;
		public bool Auto;
		public float Since;
		public float ProgressAt;
		public float Best = float.MaxValue;
		public float NextJump;
		public float NextLog;
		public bool Footsteps;
		public bool FootstepsTried;
		public Vector3 StepFrom;
		public Vector3 StepTo;
		public float StepBest = float.MaxValue;
		public float HoldUntil;
		public Ship HoldShip;
		public Ship SatOn;
		public float OwnerOffSince = -1f;
	}

	private static readonly Dictionary<int, S> s_s = new Dictionary<int, S>();

	private static int s_vehicle = -1;

	private static int s_solid = -1;

	private const float GiveUpAfter = 30f;

	private const float StuckFor = 8f;

	private const float HoldFor = 20f;

	private static int Vehicle()
	{
		if (s_vehicle == -1)
		{
			s_vehicle = LayerMask.GetMask("vehicle");
		}
		return s_vehicle;
	}

	private static int Solid()
	{
		if (s_solid == -1)
		{
			s_solid = LayerMask.GetMask("Default", "static_solid", "piece", "terrain");
		}
		return s_solid;
	}

	private static S Get(CompanionAI ai)
	{
		int id = ai.GetInstanceID();
		if (!s_s.TryGetValue(id, out S s))
		{
			s = new S();
			s_s[id] = s;
		}
		return s;
	}

	private static S Peek(CompanionAI ai)
	{
		return ai != null && s_s.TryGetValue(ai.GetInstanceID(), out S s) ? s : null;
	}

	private static float Flat(Vector3 a, Vector3 b)
	{
		return new Vector2(a.x - b.x, a.z - b.z).magnitude;
	}

	private static void Log(string msg)
	{
		CompanionsPlugin.Log?.LogInfo("[Board] " + msg);
	}

	internal static bool Active(CompanionAI ai)
	{
		S s = Peek(ai);
		return s != null && s.Ship != null;
	}

	internal static void Start(CompanionAI ai, Ship ship, Chair chair, bool auto)
	{
		if (ai == null || ship == null)
		{
			return;
		}
		S s = Get(ai);
		s.Ship = ship;
		s.Chair = chair;
		s.Auto = auto;
		s.Since = Time.time;
		s.ProgressAt = Time.time;
		s.Best = float.MaxValue;
		s.Footsteps = false;
		s.FootstepsTried = false;
		s.HoldUntil = 0f;
		MHR_Mover.Release(ai);
		Log(ai.GetComponent<Character>()?.m_name + (auto ? " is following you aboard" : " is boarding the ship"));
	}

	internal static void Cancel(CompanionAI ai)
	{
		S s = Peek(ai);
		if (s != null && s.Ship != null)
		{
			s.Ship = null;
			s.Chair = null;
			s.Footsteps = false;
			MHR_Mover.Release(ai);
		}
	}

	// the ship under a character standing on its deck (a short ray down onto the ship's own colliders)
	internal static Ship ShipUnder(Character c)
	{
		if (c == null)
		{
			return null;
		}
		Ship s = c.GetStandingOnShip();
		if (s != null)
		{
			return s;
		}
		if (Physics.Raycast(c.transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 1.2f, Vehicle()))
		{
			return hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.GetComponent<Ship>() : hit.collider.GetComponentInParent<Ship>();
		}
		return null;
	}

	// a free seat on the ship: none within 0.5 m of a character
	private static Chair FreeChair(Ship ship, Chair prefer, Vector3 near)
	{
		if (Free(prefer))
		{
			return prefer;
		}
		Chair best = null;
		float bd = float.MaxValue;
		foreach (Chair ch in ship.GetComponentsInChildren<Chair>())
		{
			if (!Free(ch))
			{
				continue;
			}
			float d = Vector3.Distance(near, ch.m_attachPoint.position);
			if (d < bd)
			{
				bd = d;
				best = ch;
			}
		}
		return best;
	}

	private static bool Free(Chair ch)
	{
		if (ch == null || !ch.m_inShip || ch.m_attachPoint == null)
		{
			return false;
		}
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c != null && Vector3.Distance(c.transform.position, ch.m_attachPoint.position) < 0.5f)
			{
				return false;
			}
		}
		return true;
	}

	// a spot on the deck on our side of the ship: the ship's closest point to us, a metre in, straight down onto it
	private static bool DeckSpot(Ship ship, Vector3 me, out Vector3 spot)
	{
		spot = Vector3.zero;
		Vector3 center = ship.transform.position;
		Collider best = null;
		float bd = float.MaxValue;
		Vector3 bp = center;
		foreach (Collider col in ship.GetComponentsInChildren<Collider>())
		{
			if (col == null || col.isTrigger || ((1 << col.gameObject.layer) & Vehicle()) == 0)
			{
				continue;
			}
			Vector3 p;
			try
			{
				p = col.ClosestPoint(me);
			}
			catch
			{
				continue;
			}
			float d = Flat(p, me);
			if (d < bd)
			{
				bd = d;
				best = col;
				bp = p;
			}
		}
		if (best == null)
		{
			return false;
		}
		Vector3 inward = center - bp;
		inward.y = 0f;
		Vector3 probe = bp + (inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.zero) * 1f;
		if (!Physics.Raycast(new Vector3(probe.x, Mathf.Max(me.y, center.y) + 4f, probe.z), Vector3.down, out RaycastHit hit, 10f, Vehicle()))
		{
			return false;
		}
		Ship s = hit.collider.attachedRigidbody != null ? hit.collider.attachedRigidbody.GetComponent<Ship>() : hit.collider.GetComponentInParent<Ship>();
		if (s != ship)
		{
			return false;
		}
		spot = hit.point;
		return true;
	}

	// While following: you're aboard a ship close by -> board it on its own. Returns true when it handled this frame
	// (boarding, or waiting on the shore after it couldn't get aboard).
	internal static bool FollowHook(CompanionAI ai, GameObject target, float dt)
	{
		if (ai == null || target == null || ai.IsOnShip || !(ModConfig.FollowAutoBoard?.Value ?? true))
		{
			return false;
		}
		Player p = target.GetComponent<Player>();
		Character c = ai.GetComponent<Character>();
		if (p == null || c == null)
		{
			return false;
		}
		Ship ship = ShipUnder(p);
		S s = Get(ai);
		if (ship == null)
		{
			s.HoldUntil = 0f;
			return false;
		}
		if (ShipUnder(c) == ship)
		{
			return false;
		}
		// couldn't get aboard: wait on the shore while you're on that ship (the far-away catch-up still applies)
		if (Time.time < s.HoldUntil && s.HoldShip == ship)
		{
			ai.StopMoving();
			ai.LookAtPoint(ship.transform.position);
			return true;
		}
		float range = ModConfig.FollowBoardRange?.Value ?? 20f;
		Rigidbody rb = ship.GetComponent<Rigidbody>();
		if (Flat(ai.transform.position, ship.transform.position) > range || (rb != null && rb.linearVelocity.magnitude > 3f) || ai.IsInCombat)
		{
			return false;
		}
		Start(ai, ship, null, auto: true);
		return Update(ai, dt);
	}

	// a seated companion that boarded with you gets up when you've left the ship (2 s on land, within 40 m)
	internal static bool ShouldLeaveSeat(CompanionAI ai, Ship ship)
	{
		S s = Peek(ai);
		if (s == null || s.SatOn == null || s.SatOn != ship)
		{
			return false;
		}
		Player owner = MHR_Owner.Of(ai);
		if (owner == null)
		{
			return false;
		}
		if (ShipUnder(owner) == ship || !owner.IsOnGround() || Vector3.Distance(owner.transform.position, ai.transform.position) > 40f)
		{
			s.OwnerOffSince = -1f;
			return false;
		}
		if (s.OwnerOffSince < 0f)
		{
			s.OwnerOffSince = Time.time;
		}
		if (Time.time - s.OwnerOffSince < 2f)
		{
			return false;
		}
		s.SatOn = null;
		s.OwnerOffSince = -1f;
		Log(ai.GetComponent<Character>()?.m_name + " - you've gone ashore, getting off the ship");
		return true;
	}

	// boarding, every frame; true while it's still at it
	internal static bool Update(CompanionAI ai, float dt)
	{
		S s = Peek(ai);
		Character c = ai.GetComponent<Character>();
		Humanoid h = c as Humanoid;
		if (s == null || s.Ship == null || c == null || h == null)
		{
			return false;
		}
		Ship ship = s.Ship;
		float now = Time.time;
		Vector3 me = ai.transform.position;
		string name = c.m_name;
		// aboard: a seat, or stand on the deck
		if (ShipUnder(c) == ship)
		{
			Chair seat = FreeChair(ship, s.Chair, me);
			bool auto = s.Auto;
			Cancel(ai);
			if (seat != null)
			{
				Log(name + " is aboard - taking a seat");
				ai.AttachToShip(ship, seat);
				if (auto)
				{
					Get(ai).SatOn = ship;
				}
			}
			else
			{
				Log(name + " is aboard - no free seat, standing on deck");
				if (!auto)
				{
					ai.BoardingEnded();
				}
			}
			return false;
		}
		// next to one of its ladders: use it
		Ladder nearest = null;
		float ld = float.MaxValue;
		foreach (Ladder l in ship.GetComponentsInChildren<Ladder>())
		{
			if (l == null || l.m_targetPos == null)
			{
				continue;
			}
			float d = Vector3.Distance(me, l.transform.position);
			if (d < l.m_useDistance - 0.1f)
			{
				l.Interact(h, hold: false, alt: false);
				Log(name + " used the ship's ladder");
				s.ProgressAt = now;
				return true;
			}
			if (d < ld)
			{
				ld = d;
				nearest = l;
			}
		}
		// progress = getting closer to the ship
		float dist = Flat(me, ship.transform.position);
		if (dist < s.Best - 0.5f)
		{
			s.Best = dist;
			s.ProgressAt = now;
		}
		// on a dock or the shore with the deck in jump reach: jump aboard
		if (c.IsOnGround() && !c.IsSwimming() && now >= s.NextJump && DeckSpot(ship, me, out Vector3 deck))
		{
			float gap = Flat(me, deck);
			float dy = deck.y - me.y;
			Vector3 dir = deck - me;
			dir.y = 0f;
			if (gap > 0.3f && gap <= 4f && dy <= 1.6f && dy >= -3f && dir.sqrMagnitude > 0.01f)
			{
				dir.Normalize();
				float reach = MHR_Jump.Reach(c, dir, dy);
				bool clear = !Physics.Linecast(me + Vector3.up * 1.2f, deck + Vector3.up * 0.8f, Solid());
				if (reach >= gap + 0.3f && clear)
				{
					s.NextJump = now + 1.5f;
					s.ProgressAt = now;
					MHR_Jump.Leap(ai, c, dir);
					Log($"{name} jumping aboard ({gap:F1} m across, {dy:+0.0;-0.0} m up)");
					return true;
				}
			}
		}
		// your own steps onto the ship, once it's been stuck for a while
		if (!s.FootstepsTried && now - s.ProgressAt > StuckFor)
		{
			s.FootstepsTried = true;
			Player owner = MHR_Owner.Of(ai);
			if (owner != null && MHR_Follow.StepsOnto(owner.gameObject, ship, out s.StepFrom, out s.StepTo))
			{
				s.Footsteps = true;
				s.StepBest = float.MaxValue;
				s.ProgressAt = now;
				Log($"{name} can't find its own way aboard - following your steps (you got on at {s.StepFrom:F1})");
			}
		}
		if (s.Footsteps)
		{
			float toStep = Flat(me, s.StepFrom);
			if (toStep < s.StepBest - 0.3f)
			{
				s.StepBest = toStep;
				s.ProgressAt = now;
			}
			if (toStep > 0.6f)
			{
				MHR_Mover.Go(ai, dt, s.StepFrom, 0.4f, true, "Board", MHR_Mover.SpeedJog);
			}
			else
			{
				Vector3 dir = s.StepTo - me;
				dir.y = 0f;
				if (dir.sqrMagnitude > 0.01f)
				{
					dir.Normalize();
					if (s.StepTo.y > me.y + 0.4f && c.IsOnGround() && now >= s.NextJump)
					{
						s.NextJump = now + 1.5f;
						MHR_Jump.Leap(ai, c, dir);
						Log(name + " jumping where you did");
					}
					else
					{
						ai.transform.rotation = Quaternion.LookRotation(dir);
						ai.MoveTowards(dir, run: false);
					}
				}
			}
		}
		else
		{
			// walk (or swim) to the nearest ladder, else to our side of the ship
			Vector3 goal = nearest != null ? nearest.transform.position : (DeckSpot(ship, me, out Vector3 d2) ? d2 : ship.transform.position);
			MHR_Mover.Go(ai, dt, goal, 0.5f, dist > 8f, "Board", dist > 8f ? MHR_Mover.SpeedRun : MHR_Mover.SpeedJog);
		}
		if (now >= s.NextLog)
		{
			s.NextLog = now + 5f;
			CompanionsPlugin.Log?.LogDebug($"[Board] {name}: {dist:F1} m from the ship, nearest ladder {(nearest != null ? ld.ToString("F1") + " m" : "none")}{(s.Footsteps ? ", on your steps" : "")}");
		}
		// stuck too long: wait on the shore
		if (now - s.ProgressAt > StuckFor * 2f || now - s.Since > GiveUpAfter * 2f || (s.Footsteps && now - s.ProgressAt > StuckFor))
		{
			Log(name + " can't get aboard - waiting on the shore");
			bool auto = s.Auto;
			Cancel(ai);
			s.HoldShip = ship;
			s.HoldUntil = now + HoldFor;
			ai.StopMoving();
			if (!auto)
			{
				ai.BoardingEnded();
			}
			return false;
		}
		return true;
	}
}
