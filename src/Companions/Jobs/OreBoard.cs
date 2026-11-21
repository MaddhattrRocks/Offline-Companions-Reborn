using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 AA6 (test 11; AA1-AA3 removed after test 12, CC1): the vein's board (AA6): which companion stands where, where it walks to and whether it is working there.
// Performance (John, 23:35Z): the board is written only when a walk goal changes, read when a point is picked and at most
// once a second while walking; positions are read from the game when needed.
public partial class HarvestController
{
	private const float OvBoardNear = 1.2f;      // AA6 (a): a point within 1.2 m of another's position or goal is not picked

	private const float OvBoardLine = 1.0f;      // AA6 (b): a working companion within 1 m of the straight line: detour

	private const float OvBoardWait = 5f;        // AA6 (a): a trench point waits up to 5 s

	private const float OvBoardLadderWait = 20f; // AA6: a ladder in use: wait 2 m away up to 20 s

	// the board entry (AA6): written only when the walk goal changes
	private Vector3 _bdGoal;

	private bool _bdGoalOn;

	private float _bdWalkAt = -1f;   // R3b.4: the last time it walked toward its board goal (WalkGo on a vein)

	private float _bdNextLook;

	private float _bdWaitFrom = -1f;

	private float _bdNextPick;

	private HarvestController _bdBlock;

	private float _bdAheadAt;     // G1: the next look for a companion just ahead (4 times a second)

	// AA4: the hand-off clock (only time in range swinging at the thing counts)
	private float _hdClock;

	private float _hdHitSeen;

	// AA7: a thing's box hangs over a trench spot (flat)
	private bool OvOverSpot(Bounds b)
	{
		if (_vp == null)
		{
			return false;
		}
		foreach (OvSpot sp in _vp.Spots)
		{
			if (sp.P.x >= b.min.x && sp.P.x <= b.max.x && sp.P.z >= b.min.z && sp.P.z <= b.max.z)
			{
				return true;
			}
		}
		return false;
	}

	// ---------- AA6: the board ----------

	// working where it stands: swinging, or standing still (catching its breath, waiting, on a ladder)
	internal bool OvBoardWorking => (_humanoid != null && _humanoid.InAttack()) || (_character != null && new Vector2(_character.GetVelocity().x, _character.GetVelocity().z).magnitude < 0.3f);

	// another companion on this vein standing within 1.2 m of pt, or walking to a goal within 1.2 m of it (null = free)
	private HarvestController OvBoardTaken(Vector3 pt)
	{
		if (_vp == null)
		{
			return null;
		}
		foreach (HarvestController h in _vp.Workers.Keys)
		{
			if (h == null || h == this || h._vp != _vp)
			{
				continue;
			}
			if (Flat(h.transform.position, pt) <= OvBoardNear || (h._bdGoalOn && Flat(h._bdGoal, pt) <= OvBoardNear))
			{
				return h;
			}
		}
		return null;
	}

	// (b): the nearest working companion within 1 m of the straight line from here to goal (not at the goal itself, within 6 m)
	private HarvestController OvBoardOnLine(Vector3 me, Vector3 goal)
	{
		Vector2 a = new Vector2(me.x, me.z), g = new Vector2(goal.x, goal.z);
		Vector2 d = g - a;
		float len = d.magnitude;
		if (len < 0.5f)
		{
			return null;
		}
		d /= len;
		HarvestController best = null;
		float bd = float.MaxValue;
		foreach (HarvestController h in _vp.Workers.Keys)
		{
			if (h == null || h == this || h._vp != _vp || !h.OvBoardWorking)
			{
				continue;
			}
			Vector3 w = h.transform.position;
			Vector2 o = new Vector2(w.x, w.z) - a;
			float t = Vector2.Dot(o, d);
			if (t <= 0f || t >= len || t > 6f || Flat(w, goal) <= OvBoardNear)
			{
				continue;
			}
			float off = Mathf.Abs(o.x * d.y - o.y * d.x);
			if (off <= OvBoardLine && t < bd)
			{
				bd = t;
				best = h;
			}
		}
		return best;
	}

	// AA6 (from WalkGo, on a vein): the board entry (written when the goal changes), the side step / no shoving, the detour
	private void OvBoardWalk(Vector3 goal, float dt)
	{
		Vector3 me = base.transform.position;
		_bdWalkAt = Time.time;
		if (!_bdGoalOn || Flat(_bdGoal, goal) > 0.5f)
		{
			_bdGoal = goal;
			_bdGoalOn = true;
		}
		// G1 (test 28: Gunter pushed Je 20 m down the trench - pushing someone still gets closer, so the old "no headway for
		// 1 s" side step never came): 4 times a second, another companion within 1.2 m ahead in the way it is walking = it
		// goes round them now on the side with more room (the mover's detour); when the mover can't take a detour (already
		// on one, or on a way-out leg) it stops pushing and takes the mover's side step (at most once a second)
		if (Time.time >= _bdAheadAt)
		{
			_bdAheadAt = Time.time + 0.25f;
			Vector3 v = _character != null ? _character.GetVelocity() : Vector3.zero;
			v.y = 0f;
			Vector3 toward = v.sqrMagnitude > 0.25f ? me + v.normalized * 2f : goal;
			HarvestController ah = OvBoardAhead(me, toward);
			if (ah != null)
			{
				Vector3 round = OvBoardSide(me, toward, ah);
				if (MHR_Mover.DetourNow(_ai, round, 4f))
				{
					if (CompanionsPlugin.LogDetailed) Log($"Another companion is just ahead at {MHR_Mover.Fmt(ah.transform.position)} - going round it by {MHR_Mover.Fmt(round)}");
					return;
				}
				if (Time.time - _dgSideAt >= OvSideAfter)
				{
					_dgSideAt = Time.time;
					_ai?.StopMoving();
					if (CompanionsPlugin.LogDetailed) Log($"Another companion is in the way to {MHR_Mover.Fmt(goal)} - stopped pushing, side step");
					MHR_Mover.SidestepNow(_ai, MHR_GatherRules.ApproachPoint(this, goal), "Gather");
				}
				return;
			}
		}
		// (b) once a second: a working companion on the line - the mover's detour past it, on the side with more room
		if (Time.time < _bdNextLook)
		{
			return;
		}
		_bdNextLook = Time.time + 1f;
		HarvestController w = OvBoardOnLine(me, goal);
		if (w == null)
		{
			return;
		}
		Vector3 wp = w.transform.position;
		Vector3 via = OvBoardSide(me, goal, w);
		if (MHR_Mover.DetourNow(_ai, via, 4f) && CompanionsPlugin.LogDetailed)
		{
			Log($"A companion is working on the way to {MHR_Mover.Fmt(goal)} at {MHR_Mover.Fmt(wp)} - going round it by {MHR_Mover.Fmt(via)}");
		}
	}

	// AA6 (b) / G1: the point beside companion w to go round it by, on the side with more room - the side whose ground is
	// nearer its own feet's height (in a trench the other side is the wall), and with no other companion within 1.2 m
	private Vector3 OvBoardSide(Vector3 me, Vector3 toward, HarvestController w)
	{
		Vector3 wp = w.transform.position;
		Vector3 dir = toward - me;
		dir.y = 0f;
		dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : base.transform.forward;
		Vector3 side = new Vector3(dir.z, 0f, 0f - dir.x);
		Vector3 l = wp + side * (OvBoardLine + 0.6f), r = wp - side * (OvBoardLine + 0.6f);
		l.y = GroundAt(l);
		r.y = GroundAt(r);
		// R2.4: a side the pit map says is no place to stand (stone, rock, a part or a thing on it) counts as taken; a built
		// floor: on the floor
		int sl = OvMapStand(l, out float fl, out _), sr = OvMapStand(r, out float fr, out _);
		if (sl == 1)
		{
			l.y = fl;
		}
		if (sr == 1)
		{
			r.y = fr;
		}
		HarvestController tl = OvBoardTaken(l), tr = OvBoardTaken(r);
		float cl = Mathf.Abs(l.y - me.y) + (tl != null && tl != w ? 10f : 0f) + (sl == 0 ? 10f : 0f);
		float cr = Mathf.Abs(r.y - me.y) + (tr != null && tr != w ? 10f : 0f) + (sr == 0 ? 10f : 0f);
		return cl <= cr ? l : r;
	}

	// G1: the nearest other companion on this vein within 1.2 m ahead (flat, within 60 degrees of the way it walks)
	private HarvestController OvBoardAhead(Vector3 me, Vector3 toward)
	{
		Vector3 dir = toward - me;
		dir.y = 0f;
		if (dir.sqrMagnitude < 0.01f || _vp == null)
		{
			return null;
		}
		dir.Normalize();
		HarvestController best = null;
		float bd = float.MaxValue;
		foreach (HarvestController h in _vp.Workers.Keys)
		{
			if (h == null || h == this || h._vp != _vp)
			{
				continue;
			}
			Vector3 o = h.transform.position - me;
			o.y = 0f;
			float d = o.magnitude;
			if (d <= OvSideNear && d > 0.01f && Vector3.Dot(o / d, dir) > 0.5f && d < bd)
			{
				bd = d;
				best = h;
			}
		}
		return best;
	}

	// AA6 (a): a trench point another companion stands on or walks to - waits up to 5 s (looked at once a second), then the
	// spot is skipped this lap. True = waiting (or skipped).
	private bool OvBoardTrenchWait()
	{
		if (Time.time >= _bdNextPick)
		{
			_bdNextPick = Time.time + 1f;
			_bdBlock = OvBoardTaken(_dgPoint);
		}
		if (_bdBlock == null)
		{
			_bdWaitFrom = -1f;
			return false;
		}
		if (_bdWaitFrom < 0f)
		{
			_bdWaitFrom = Time.time;
			if (CompanionsPlugin.LogDetailed) Log($"Another companion stands at or is going to {MHR_Mover.Fmt(_dgPoint)} - waiting (up to {OvBoardWait:F0} s)");
		}
		_ai?.StopMoving();
		if (Time.time - _bdWaitFrom >= OvBoardWait)
		{
			OvSpot q = _dgSpot;
			_bdWaitFrom = -1f;
			_bdBlock = null;
			OvDigOff();
			OvSkip(q, $"another companion was working there for {OvBoardWait:F0} s", normal: true);
		}
		return true;
	}

	// AA6: another companion is on (or climbing) this ladder
	private bool OvLnInUse(GameObject ladder)
	{
		if (_vp == null || ladder == null)
		{
			return false;
		}
		foreach (HarvestController h in _vp.Workers.Keys)
		{
			if (h != null && h != this && h._vp == _vp && h._clLadder && h._lnLadder == ladder && h._lnStage >= 3 && h._lnPushingOrUp)
			{
				return true;
			}
		}
		return false;
	}
}
