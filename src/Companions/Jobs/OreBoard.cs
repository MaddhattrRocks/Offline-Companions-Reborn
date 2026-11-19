using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 AA6 (test 11; AA1-AA3 removed after test 12, CC1): the vein's board (AA6): which companion stands where, where it walks to and whether it is working there.
// Performance (John, 23:35Z): the board is written only when a walk goal changes, read when a point is picked and at most
// once a second while walking; positions are read from the game when needed.
public partial class HarvestController
{
	private const float OvVeinGrid = 0.5f;       // AA6: the step a thing's box is looked at with

	private const float OvBoardNear = 1.2f;      // AA6 (a): a point within 1.2 m of another's position or goal is not picked

	private const float OvBoardLine = 1.0f;      // AA6 (b): a working companion within 1 m of the straight line: detour

	private const float OvBoardWait = 5f;        // AA6 (a): a trench point waits up to 5 s

	private const float OvBoardLadderWait = 20f; // AA6: a ladder in use: wait 2 m away up to 20 s

	// the board entry (AA6): written only when the walk goal changes
	private Vector3 _bdGoal;

	private bool _bdGoalOn;

	private float _bdHwBest = float.MaxValue;

	private float _bdHwTime;

	private float _bdNextLook;

	private float _bdWaitFrom = -1f;

	private float _bdNextPick;

	private HarvestController _bdBlock;

	// AA4: the hand-off clock (only time in range swinging at the thing counts)
	private float _hdClock;

	private float _hdHitSeen;

	// AA6: the reach point on a handed-off log (refreshed at most once a second)
	private Vector3 _ovHandAt;

	private bool _ovHandAtOn;

	private float _ovHandAtNext;

	// a thing's collider is at s (flat, within 0.3 m): the game's closest point; a non-convex mesh by its box
	private static bool OvThingThere(Collider[] cols, Vector3 s, out Vector3 at)
	{
		at = s;
		foreach (Collider c in cols)
		{
			if (c == null || c.isTrigger || !c.enabled || !c.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (c is MeshCollider mc && !mc.convex)
			{
				Bounds cb = c.bounds;
				if (s.x >= cb.min.x && s.x <= cb.max.x && s.z >= cb.min.z && s.z <= cb.max.z)
				{
					return true;
				}
				continue;
			}
			Vector3 cp = c.ClosestPoint(s);
			if (Flat(cp, s) <= 0.3f)
			{
				at = cp;
				return true;
			}
		}
		return false;
	}

	// AA6: the sampled points of a thing (every 0.5 m over its box, at most 24 x 24): 1 = pt is the nearest free on the
	// board (else the nearest point of it); 0 = no point of it found
	private int OvThingPoint(GameObject go, Bounds b, Vector3 from, out Vector3 pt)
	{
		pt = from;
		Collider[] cols = go.GetComponentsInChildren<Collider>();
		float sx = Mathf.Max(OvVeinGrid, b.size.x / 24f), sz = Mathf.Max(OvVeinGrid, b.size.z / 24f);
		bool anyOff = false, anyFree = false;
		float bdOff = float.MaxValue, bdFree = float.MaxValue;
		Vector3 off = from, free = from;
		for (float x = b.min.x; x <= b.max.x + 0.01f; x += sx)
		{
			for (float z = b.min.z; z <= b.max.z + 0.01f; z += sz)
			{
				Vector3 s = new Vector3(x, b.center.y, z);
				if (!OvThingThere(cols, s, out Vector3 at))
				{
					continue;
				}
				float d = Flat(from, at);
				if (d < bdOff)
				{
					bdOff = d;
					off = at;
					anyOff = true;
				}
				if (d < bdFree && OvBoardTaken(at) == null)
				{
					bdFree = d;
					free = at;
					anyFree = true;
				}
			}
		}
		if (anyFree)
		{
			pt = free;
			return 1;
		}
		if (anyOff)
		{
			pt = off;
			return 1;
		}
		return 0;
	}

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

	// AA6 (Y6c): where it goes for a handed-off log - the Y6c point unless another companion stands or walks within 1.2 m
	// of it; then the nearest point of the log that is free. Looked at again at most once a second.
	private Vector3 OvHandLogPoint(Bounds lb, Vector3 want)
	{
		if (Time.time >= _ovHandAtNext)
		{
			_ovHandAtNext = Time.time + 1f;
			_ovHandAtOn = false;
			if (OvBoardTaken(want) != null && OvThingPoint(_target, lb, base.transform.position, out Vector3 fp) == 1 && OvBoardTaken(fp) == null)
			{
				_ovHandAt = fp;
				_ovHandAtOn = true;
			}
		}
		return _ovHandAtOn ? _ovHandAt : want;
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
		if (!_bdGoalOn || Flat(_bdGoal, goal) > 0.5f)
		{
			_bdGoal = goal;
			_bdGoalOn = true;
			_bdHwBest = float.MaxValue;
			_bdHwTime = 0f;
		}
		float flat = Flat(me, goal);
		if (flat < _bdHwBest - OvHeadwayGain)
		{
			_bdHwBest = flat;
			_bdHwTime = 0f;
		}
		else
		{
			_bdHwTime += dt;
		}
		// (c) + H4: pressed against another (within 1.2 m ahead) with no headway for 1 s: stop pushing, the mover's side step
		if (_bdHwTime >= OvSideAfter && Time.time - _dgSideAt >= OvSideAfter && OvOtherAhead(me, goal))
		{
			_dgSideAt = Time.time;
			_ai?.StopMoving();
			if (CompanionsPlugin.LogDetailed) Log($"Another companion is in the way to {MHR_Mover.Fmt(goal)} - stopped pushing, side step");
			MHR_Mover.SidestepNow(_ai, MHR_GatherRules.ApproachPoint(this, goal), "Gather");
			return;
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
		Vector3 dir = goal - me;
		dir.y = 0f;
		dir.Normalize();
		Vector3 side = new Vector3(dir.z, 0f, 0f - dir.x);
		Vector3 l = wp + side * (OvBoardLine + 0.6f), r = wp - side * (OvBoardLine + 0.6f);
		l.y = GroundAt(l);
		r.y = GroundAt(r);
		// more room: the side whose ground is nearer its own feet's height (in a trench the other side is the wall), and
		// with no other companion within 1.2 m
		float cl = Mathf.Abs(l.y - me.y) + (OvBoardTaken(l) != null && OvBoardTaken(l) != w ? 10f : 0f);
		float cr = Mathf.Abs(r.y - me.y) + (OvBoardTaken(r) != null && OvBoardTaken(r) != w ? 10f : 0f);
		Vector3 via = cl <= cr ? l : r;
		if (MHR_Mover.DetourNow(_ai, via, 4f) && CompanionsPlugin.LogDetailed)
		{
			Log($"A companion is working on the way to {MHR_Mover.Fmt(goal)} at {MHR_Mover.Fmt(wp)} - going round it by {MHR_Mover.Fmt(via)}");
		}
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
