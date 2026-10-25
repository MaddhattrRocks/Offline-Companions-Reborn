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
		public bool HasShore;
		public Vector3 Shore;
		public float TreadSince = -1f;
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

	// 1.5.84: the boarding companion's nearest ship ladder and its flat distance (WaterSafety asks, so it doesn't turn back
	// for the shore with the ladder right there)
	internal static bool KeepSwimming(Character c)
	{
		CompanionAI ai = c != null ? c.GetComponent<CompanionAI>() : null;
		S s = Peek(ai);
		if (s == null || s.Ship == null)
		{
			return false;
		}
		Ladder l = NearestLadder(s.Ship, c.transform.position, out float flat);
		if (l == null || flat > 8f)
		{
			return false;
		}
		CompanionStamina stam = c.GetComponent<CompanionStamina>();
		return flat < 3.5f || stam == null || stam.Stamina >= MHR_Water.SwimStamina(c, flat) * 1.1f;
	}

	private static Ladder NearestLadder(Ship ship, Vector3 me, out float flat)
	{
		flat = float.MaxValue;
		Ladder best = null;
		foreach (Ladder l in ship.GetComponentsInChildren<Ladder>())
		{
			if (l == null || l.m_targetPos == null)
			{
				continue;
			}
			float d = Flat(me, l.transform.position);
			if (d < flat)
			{
				flat = d;
				best = l;
			}
		}
		return best;
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
		s.TreadSince = -1f;
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

	// 1.5.83: which seat it took, for the log (user: "The NPC is also sitting on a stool that does not look to be there")
	private static string SeatPath(Transform t, Transform root)
	{
		string path = t.name;
		while (t.parent != null && t.parent != root)
		{
			t = t.parent;
			path = t.name + "/" + path;
		}
		return path;
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

	// 1.5.84: the mast has a seat point but no seat (log ca66e4e5: "taking a seat: 'interactive/mast'" - user: "sitting in
	// the middle of the boat rather than one of the seats ... There are 2 seats near the front and 2 near the rear")
	internal static bool IsMast(Chair ch)
	{
		return ch != null && ch.gameObject.name.IndexOf("mast", System.StringComparison.OrdinalIgnoreCase) >= 0;
	}

	internal static bool Free(Chair ch)
	{
		if (ch == null || !ch.m_inShip || ch.m_attachPoint == null || IsMast(ch))
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
			if (s.HasShore && Flat(ai.transform.position, s.Shore) > 1f)
			{
				MHR_Mover.Go(ai, dt, s.Shore, 0.8f, false, "Board", MHR_Mover.SpeedJog);
				if (MHR_Mover.GaveUp(ai))
				{
					s.HasShore = false;
				}
				return true;
			}
			s.HasShore = false;
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
				Vector3 local = ship.transform.InverseTransformPoint(seat.m_attachPoint.position);
				Log($"{name} is aboard - taking a seat: '{SeatPath(seat.transform, ship.transform)}' at {local:F2} on the ship (world {seat.m_attachPoint.position:F1})");
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
		// 1.5.83: resting on the bank for its breath before a swim isn't being stuck (log 6b069f3f: it gave up while resting,
		// then stood in the shallows)
		CompanionStamina stam = ai.GetComponent<CompanionStamina>();
		if (MHR_Mover.RestingForSwim(ai))
		{
			s.ProgressAt = now;
			if (now >= s.NextLog)
			{
				s.NextLog = now + 5f;
				Log($"{name} resting before the swim ({(stam != null ? stam.Stamina : 0f):F0}/{(stam != null ? stam.MaxStamina : 0f):F0} stamina)");
			}
		}
		// a swim longer than it has stamina for at all: say so and wait on the shore
		float swimM = MHR_Mover.RouteSwim(ai);
		if (swimM > 0.5f && stam != null && stam.MaxStamina > 0f && !c.IsSwimming())
		{
			float needSwim = MHR_Water.SwimStamina(c, swimM) * 1.25f + 5f;
			if (needSwim > stam.MaxStamina)
			{
				return GiveUp(ai, s, c, $"the swim to the ladder ({swimM:F0} m) needs about {needSwim:F0} stamina, it has {stam.MaxStamina:F0} at most");
			}
		}
		// progress = getting closer to the ship
		float dist = Flat(me, ship.transform.position);
		if (dist < s.Best - 0.5f)
		{
			s.Best = dist;
			s.ProgressAt = now;
		}
		// 1.5.84: like a player (user: "Its faster to jump off the dock to the boat and wait till you pop up from the water then
		// use the ladder ... rather than just treading water for a moment to wait for the ladder to be in range")
		Ladder near = NearestLadder(ship, me, out float flatL);
		CompanionStamina st2 = ai.GetComponent<CompanionStamina>();
		if (near != null && flatL <= 8f)
		{
			Vector3 toL = near.transform.position - me;
			toL.y = 0f;
			Vector3 dirL = toL.sqrMagnitude > 0.01f ? toL.normalized : ai.transform.forward;
			if (c.IsSwimming() && flatL < 3.5f)
			{
				// in the water by the ladder: tread water (no stamina) until it's in reach
				if (s.TreadSince < 0f)
				{
					s.TreadSince = now;
					Log(name + " treading water by the ladder until it's in reach");
				}
				s.ProgressAt = now;
				if (flatL > 0.8f)
				{
					ai.transform.rotation = Quaternion.LookRotation(dirL);
					ai.MoveTowards(dirL, run: false);
				}
				else
				{
					ai.StopMoving();
				}
				if (now - s.TreadSince > 20f)
				{
					return GiveUp(ai, s, c, "the ladder never came within reach");
				}
				return true;
			}
			s.TreadSince = -1f;
			if (!c.IsSwimming() && c.IsOnGround() && !Physics.Raycast(me + Vector3.up * 0.5f, dirL, 1.5f, Solid()))
			{
				Vector3 ahead = me + dirL * 1.2f;
				ahead.y = MHR_Water.GroundAt(ahead);
				if (MHR_Water.DepthAt(ahead) > 0.8f)
				{
					// at the water's edge facing the ladder: dive in, if it has the stamina for the swim
					float need = MHR_Water.SwimStamina(c, flatL) * 1.25f + 5f;
					if (st2 != null && st2.Stamina < need)
					{
						ai.StopMoving();
						s.ProgressAt = now;
						if (now >= s.NextLog)
						{
							s.NextLog = now + 5f;
							Log($"{name} resting before the swim to the ladder ({st2.Stamina:F0}/{need:F0} stamina)");
						}
						return true;
					}
					if (now >= s.NextJump)
					{
						s.NextJump = now + 2f;
						s.ProgressAt = now;
						MHR_Jump.Leap(ai, c, dirL);
						Log($"{name} diving in towards the ladder ({flatL:F1} m)");
					}
					return true;
				}
				if (MHR_Water.DepthAt(ahead) < 0.4f && ahead.y > me.y - 1.5f)
				{
					// dry ground on the way to the edge: walk straight at the ladder
					ai.transform.rotation = Quaternion.LookRotation(dirL);
					ai.MoveTowards(dirL, run: false);
					s.ProgressAt = now;
					return true;
				}
			}
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
			return GiveUp(ai, s, c, "no way found");
		}
		return true;
	}

	// 1.5.83: gives up for now and waits on dry land facing the ship - not where it stopped (it stood in the water)
	private static bool GiveUp(CompanionAI ai, S s, Character c, string why)
	{
		Ship ship = s.Ship;
		bool auto = s.Auto;
		Log(c.m_name + " can't get aboard (" + why + ") - waiting on the shore");
		Cancel(ai);
		s.HoldShip = ship;
		s.HoldUntil = Time.time + HoldFor;
		s.HasShore = DryShore(ai.transform.position, ship != null ? ship.transform.position : ai.transform.position, out s.Shore);
		if (!s.HasShore)
		{
			ai.StopMoving();
		}
		if (!auto)
		{
			ai.BoardingEnded();
		}
		return false;
	}

	// the nearest dry ground within 15 m (rings of 12 directions), a little towards the ship when it's a tie
	private static bool DryShore(Vector3 me, Vector3 ship, out Vector3 spot)
	{
		spot = me;
		if (MHR_Water.DepthAt(me) < 0.1f)
		{
			return false;
		}
		float best = float.MaxValue;
		bool found = false;
		Vector3 toShip = ship - me;
		toShip.y = 0f;
		foreach (float r in new float[6] { 2f, 4f, 6f, 9f, 12f, 15f })
		{
			for (int k = 0; k < 12; k++)
			{
				Vector3 d = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward;
				Vector3 p = me + d * r;
				p.y = MHR_Water.GroundAt(p);
				if (MHR_Water.DepthAt(p) >= 0.1f || p.y > me.y + 3f)
				{
					continue;
				}
				float score = r - (toShip.sqrMagnitude > 0.01f ? Vector3.Dot(d, toShip.normalized) : 0f);
				if (score < best)
				{
					best = score;
					spot = p;
					found = true;
				}
			}
			if (found)
			{
				break;
			}
		}
		return found;
	}
}
