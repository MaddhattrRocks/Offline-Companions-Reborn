using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions
{
    // ---- 1.4.76: follow like a player (movement phase 2). ----
    // Each companion picks its own spot near the player (beside, behind, sometimes ahead), moves it now and then, and
    // keeps clear of the other companions' spots. Spots are measured from the way the player is travelling, not where
    // the camera points. Movement goes through MHR_Mover (doors, water, getting unstuck). Where no spot works (doorways,
    // stairs, corridors) it walks the player's recent path instead. When the player stops it stays put and idles,
    // regroups close by after FollowRegroupTime, and indoors sits down on a free chair or the floor (near home it
    // empties its bags first). Combat is unchanged: CompanionAI handles it before Follow ever runs.
    public static class MHR_Follow
    {
        class S
        {
            public int TargetId;
            public bool HasSpot, Trail; public float Angle, Radius, SpotDy, NextPick, NextCheck;
            public Vector3 Spot;
            public bool Moving; public float LastUpdate;
            public bool HasIdle, IdleMoving; public Vector3 IdleAnchor, IdleGoal; public float NextIdle;
            public bool Regrouped;
            public float SettleDelay; public bool UnloadAsked; public float UnloadAskedAt;
            public Chair Chair; public bool GoingToChair; public float ChairUntil, ChairFailUntil, NextChairLook;
            public bool SitChair, SitFloor; public Vector3 SitPlayerPos; public string ChairAnim;
            public Collider[] ChairCols;
        }
        // one per followed player, shared by all companions following them
        class T
        {
            public readonly List<Vector3> Trail = new List<Vector3>();
            public Vector3 Travel, LastPos; public float Speed, StillSince, MovingSince, NextIndoorCheck; public bool Indoors; public int Frame = -1;
        }

        static readonly Dictionary<int, S> s_s = new Dictionary<int, S>();
        static readonly Dictionary<int, T> s_t = new Dictionary<int, T>();
        static readonly List<Piece> s_pieces = new List<Piece>();
        static int s_ground = -1, s_los = -1;
        static float s_nextErr;

        static int GroundMask() { if (s_ground == -1) s_ground = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"); return s_ground; }
        static int LosMask() { if (s_los == -1) s_los = LayerMask.GetMask("Default", "static_solid", "piece", "terrain"); return s_los; }
        static void Dbg(string msg) { if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogDebug("[Follow] " + msg); }
        static float Flat(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }
        static float MaxDist(T t)
        {
            if (t.Indoors) return ModConfig.FollowIndoorDistance != null ? ModConfig.FollowIndoorDistance.Value : 4f;
            return ModConfig.FollowMaxDistance != null ? ModConfig.FollowMaxDistance.Value : 10f;
        }
        static S Get(Component c)
        {
            S s; int id = c.gameObject.GetInstanceID();
            if (!s_s.TryGetValue(id, out s)) { s = new S(); s_s[id] = s; }
            return s;
        }
        static S Peek(Component c)
        {
            S s;
            return c != null && s_s.TryGetValue(c.gameObject.GetInstanceID(), out s) ? s : null;
        }

        // DoorHandler stays out of the way while Follow is steering (or deliberately standing still)
        public static bool Owns(Component c) { S s = Peek(c); return s != null && Time.time - s.LastUpdate < 0.5f; }

        // ---------- the followed player ----------
        static T Track(GameObject target)
        {
            int id = target.GetInstanceID();
            float now = Time.time;
            T t;
            if (!s_t.TryGetValue(id, out t))
            {
                t = new T { StillSince = now, LastPos = target.transform.position };
                Vector3 f = target.transform.forward; f.y = 0f;
                t.Travel = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
                s_t[id] = t;
            }
            if (t.Frame == Time.frameCount) return t;
            t.Frame = Time.frameCount;
            Vector3 p = target.transform.position;
            // speed from how far the player moved (works for any player, not only the one on this machine)
            Vector3 v = p - t.LastPos; v.y = 0f;
            t.LastPos = p;
            if (v.magnitude > 8f) { t.Trail.Clear(); v = Vector3.zero; }   // teleport / portal: old path is no use
            float frameDt = Mathf.Max(Time.deltaTime, 0.001f);
            v /= frameDt;
            t.Speed = Mathf.Lerp(t.Speed, v.magnitude, Mathf.Clamp01(frameDt * 8f));
            if (t.Speed > 0.6f)
            {
                if (now - t.StillSince > 1f) t.MovingSince = now;   // just set off after standing still
                t.StillSince = now;
                Vector3 d = Vector3.Slerp(t.Travel, v.sqrMagnitude > 0.0001f ? v.normalized : t.Travel, Mathf.Clamp01(frameDt * 2.5f)); d.y = 0f;
                if (d.sqrMagnitude > 0.0001f) t.Travel = d.normalized;
            }
            if (t.Trail.Count == 0 || Flat(p, t.Trail[t.Trail.Count - 1]) > 0.75f)
            {
                t.Trail.Add(p);
                if (t.Trail.Count > 40) t.Trail.RemoveAt(0);
            }
            if (now >= t.NextIndoorCheck)
            {
                t.NextIndoorCheck = now + 0.5f;
                Player pl = target.GetComponent<Player>();
                t.Indoors = pl != null && pl == Player.m_localPlayer && pl.InShelter();
            }
            return t;
        }

        // ---------- main entry: CompanionAI's follow branch (combat, trips and jobs are handled before this) ----------
        // Returns false for follow targets that aren't players (the old formation code handles those).
        public static bool Update(CompanionAI ai, GameObject target, float dt)
        {
            if (ai == null || target == null || target.GetComponent<Player>() == null) return false;
            try { UpdateInner(ai, target, dt); return true; }
            catch (Exception e)
            {
                if (CompanionsPlugin.Log != null && Time.time >= s_nextErr) { s_nextErr = Time.time + 30f; CompanionsPlugin.Log.LogWarning("[Follow] " + e.Message); }
                return false;
            }
        }

        static void UpdateInner(CompanionAI ai, GameObject target, float dt)
        {
            float now = Time.time;
            S s = Get(ai);
            T t = Track(target);
            Player player = target.GetComponent<Player>();
            Character c = ai.GetComponent<Character>();
            Vector3 me = ai.transform.position, pp = target.transform.position;
            float maxD = MaxDist(t);
            // 1.4.77: the Wander toggle. On = moves its spot around and idles when the player stops.
            // Off = keeps one spot (still spread out from the others) and stands still.
            CompanionSetup setup = ai.GetComponent<CompanionSetup>();
            bool wander = setup == null || setup.GetWander();
            float distP = Mathf.Abs(me.y - pp.y) > 3f ? Vector3.Distance(me, pp) : Flat(me, pp);
            int tid = target.GetInstanceID();
            if (now - s.LastUpdate > 0.5f || s.TargetId != tid)
            {
                // starting, or back from a fight / trip / job: start fresh
                s.HasSpot = false; s.Moving = distP > maxD; s.HasIdle = false; s.IdleMoving = false; s.Regrouped = false;
                s.UnloadAsked = false; s.GoingToChair = false; s.Chair = null; s.SettleDelay = UnityEngine.Random.Range(5f, 10f);
            }
            s.LastUpdate = now; s.TargetId = tid;

            if (s.SitChair || s.SitFloor)
            {
                ai.StopMoving();
                if (s.SitChair) Pin(ai, s);
                return;
            }

            float still = now - t.StillSince;
            bool playerMoving = still < 1.0f;

            if (playerMoving)
            {
                s.Regrouped = false; s.UnloadAsked = false; s.HasIdle = false; s.IdleMoving = false;
                if (s.GoingToChair) ReleaseChair(s);
                if (!s.HasSpot || (now >= s.NextPick && (wander || s.Trail))) PickSpot(ai, s, t, pp, maxD, false, 0f);
                else if (now >= s.NextCheck)
                {
                    s.NextCheck = now + 1f;
                    if (s.Trail) PickSpot(ai, s, t, pp, maxD, false, 0f);   // see if there's room to spread out again
                    else
                    {
                        Vector3 g;
                        if (!Ground(SpotXZ(s, t, pp), pp, out g) || !Los(pp, g)) PickSpot(ai, s, t, pp, maxD, false, 0f);
                        else s.SpotDy = g.y - pp.y;
                    }
                }
                Vector3 goal = Goal(ai, s, t, pp, tid);
                float dSpot = Flat(me, goal);
                // 1.4.77/1.4.82: if it was standing still, stay put for 1.5 s after the player sets off, so the player can
                // walk up and open the inventory or the command wheel. Only from standing - never once it's moving.
                // 1.4.82: and while the player stands within 2.5 m of it (doesn't start it moving, never stops it)
                bool hold = now - t.MovingSince < 1.5f || (distP < 2.5f && !c.IsSwimming());
                if (!s.Moving && !hold && (dSpot > (t.Indoors ? 2.5f : 4f) || distP > maxD || c.IsSwimming())) s.Moving = true;
                if (s.Moving)
                {
                    // aim well ahead of the spot so it keeps walking with the player instead of arriving, stopping and
                    // starting again (1.4.78: the 1.4.77 lead was short enough to "arrive" mid-walk - stutter)
                    Vector3 lead = s.Trail ? goal : goal + t.Travel * Mathf.Max(1.5f, Mathf.Min(t.Speed, 8f));
                    int pace = Pace(player, dSpot, distP, maxD);
                    // ahead of its spot: ease off a step instead of stopping
                    if (!s.Trail && dSpot < 2f && Vector3.Dot(me - goal, t.Travel) > 0.5f && pace > MHR_Mover.SpeedWalk) pace--;
                    MHR_Mover.Go(ai, dt, lead, 0.3f, true, MHR_Mover.FollowTag, pace);
                    // 1.4.79: no route to its spot (other side of a wall, across water): walk the player's path instead
                    if (!s.Trail && MHR_Mover.RoutePartial(ai) && distP > 3f) { s.Trail = true; s.NextPick = now + 4f; s.NextCheck = now + 4f; }
                    // 1.4.81: the route to its spot means a swim but the player walked (1.4.80 log: round through the moat
                    // while the player went in by the gate) - walk the player's path instead
                    if (!s.Trail && !player.IsSwimming() && !c.IsSwimming() && MHR_Mover.RouteSwim(ai) > 1f) { s.Trail = true; s.NextPick = now + 6f; s.NextCheck = now + 6f; }
                    // 1.4.82: the route is a long way round (1.4.81 log: 67 m down past the beach for 21 m straight,
                    // while the player took the path through the moat) - walk the player's path instead
                    if (!s.Trail && distP > 3f)
                    {
                        float len = MHR_Mover.RouteLength(ai);
                        if (len > Flat(me, lead) * 1.8f + 8f) { s.Trail = true; s.NextPick = now + 6f; s.NextCheck = now + 6f; }
                    }
                    if (MHR_Mover.GaveUp(ai)) ai.MHR_FollowFallback(target, dt);
                }
                else ai.StopMoving();
                ai.MHR_SetCrouch(player.IsCrouching());
                return;
            }

            ai.MHR_SetCrouch(player.IsCrouching());

            // the player has stopped. Too far (or treading water): come back within range, then wait there.
            if (distP > maxD || c.IsSwimming())
            {
                if (!s.HasSpot || s.Radius > maxD * 0.7f) PickSpot(ai, s, t, pp, maxD, false, maxD * 0.6f);
                Vector3 goal = Goal(ai, s, t, pp, tid);
                s.Moving = true; s.HasIdle = false; s.IdleMoving = false;
                MHR_Mover.Go(ai, dt, goal, 1.0f, true, MHR_Mover.FollowTag, distP > maxD + 6f ? MHR_Mover.SpeedRun : MHR_Mover.SpeedJog);
                if (MHR_Mover.GaveUp(ai)) ai.MHR_FollowFallback(target, dt);
                return;
            }
            if (s.Moving || !s.HasIdle)
            {
                // wait where we are
                s.Moving = false; ai.StopMoving();
                s.HasIdle = true; s.IdleAnchor = me; s.IdleMoving = false; s.NextIdle = now + UnityEngine.Random.Range(3f, 6f);
            }

            // stood still a long time: come back and wait close by
            if (ModConfig.FollowRegroup != null && ModConfig.FollowRegroup.Value && !s.Regrouped && still >= ModConfig.FollowRegroupTime.Value)
            {
                s.Regrouped = true;
                PickSpot(ai, s, t, pp, maxD, true, 0f);
                s.IdleGoal = Goal(ai, s, t, pp, tid); s.IdleAnchor = s.IdleGoal; s.IdleMoving = true;
                Dbg(c.m_name + " regrouping after " + still.ToString("F0") + "s");
            }

            // indoors: sit down (a chair if there's a free one, otherwise the floor)
            if (t.Indoors && !s.IdleMoving && ModConfig.FollowSitIndoors != null && ModConfig.FollowSitIndoors.Value && still >= s.SettleDelay && c.IsOnGround() && !c.IsSwimming())
            {
                if (Settle(ai, s, c, pp, maxD, dt, now)) return;
            }

            // idle: shuffle around a little, now and then turn to look at the player (Wander on only)
            if (!wander && !s.IdleMoving)
            {
                ai.StopMoving();
                return;
            }
            if (s.IdleMoving)
            {
                if (MHR_Mover.Go(ai, dt, s.IdleGoal, 0.6f, false, MHR_Mover.FollowTag, MHR_Mover.SpeedWalk) || MHR_Mover.GaveUp(ai))
                {
                    s.IdleMoving = false; s.NextIdle = now + UnityEngine.Random.Range(6f, 14f);
                    MHR_Mover.Release(ai);
                    ai.LookAtPoint(pp + Vector3.up * 1.5f);
                }
                return;
            }
            ai.StopMoving();
            if (now >= s.NextIdle)
            {
                s.NextIdle = now + UnityEngine.Random.Range(6f, 14f);
                Vector3 g;
                if (UnityEngine.Random.value < 0.35f || !IdlePoint(ai, s, pp, maxD, t.Indoors ? 1.0f : 2.0f, tid, out g)) ai.LookAtPoint(pp + Vector3.up * 1.5f);
                else { s.IdleGoal = g; s.IdleMoving = true; }
            }
        }

        // walk while the player walks, run while they run, a step faster to close a gap, run when far behind
        static int Pace(Player p, float dSpot, float distP, float maxD)
        {
            if (distP > maxD + 4f || dSpot > 8f) return MHR_Mover.SpeedRun;
            int pace = p.IsRunning() ? MHR_Mover.SpeedRun : (p.IsCrouching() || p.GetWalk()) ? MHR_Mover.SpeedWalk : MHR_Mover.SpeedJog;
            if (dSpot > 3f && pace < MHR_Mover.SpeedRun) pace++;
            return pace;
        }

        // ---------- spots ----------
        static Vector3 SpotXZ(S s, T t, Vector3 pp)
        {
            Vector3 w = pp + Quaternion.Euler(0f, s.Angle, 0f) * t.Travel * s.Radius;
            w.y = pp.y + s.SpotDy;
            return w;
        }
        static Vector3 Goal(CompanionAI ai, S s, T t, Vector3 pp, int tid)
        {
            if (s.Trail) return TrailPoint(t, pp, 2f + Rank(ai, tid) * 1.5f);
            s.Spot = SpotXZ(s, t, pp);
            return s.Spot;
        }

        static float RandomAngle(bool anywhere)
        {
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            if (anywhere) return UnityEngine.Random.Range(0f, 360f);
            float r = UnityEngine.Random.value;
            if (r < 0.15f) return side * UnityEngine.Random.Range(20f, 60f);     // ahead
            if (r < 0.45f) return side * UnityEngine.Random.Range(60f, 100f);    // beside
            return side * UnityEngine.Random.Range(100f, 170f);                   // behind, off to one side
        }

        static bool PickSpot(CompanionAI ai, S s, T t, Vector3 pp, float maxD, bool regroup, float maxRadius)
        {
            float now = Time.time;
            float rMin, rMax;
            if (regroup) { rMin = t.Indoors ? 1.5f : 2f; rMax = t.Indoors ? 2.5f : 4f; }
            else { rMin = t.Indoors ? 1.5f : 3f; rMax = Mathf.Max(rMin + 0.5f, maxD * 0.7f); }
            if (maxRadius > 0f) rMax = Mathf.Max(rMin, Mathf.Min(rMax, maxRadius));
            float spacing = t.Indoors ? 1.5f : 3f;
            int tid = s.TargetId;
            for (int i = 0; i < 10; i++)
            {
                float ang = RandomAngle(regroup);
                float r = UnityEngine.Random.Range(rMin, rMax);
                Vector3 w = pp + Quaternion.Euler(0f, ang, 0f) * t.Travel * r;
                Vector3 g;
                if (!Ground(w, pp, out g) || Crowded(ai, tid, g, spacing) || !Los(pp, g)) continue;
                s.Angle = ang; s.Radius = r; s.SpotDy = g.y - pp.y; s.Spot = g;
                s.HasSpot = true; s.Trail = false;
                s.NextPick = now + UnityEngine.Random.Range(10f, 25f); s.NextCheck = now + 1f;
                return true;
            }
            // nowhere to spread out (doorway, stairs, corridor): walk the player's path for a bit
            s.HasSpot = true; s.Trail = true; s.NextPick = now + 3f; s.NextCheck = now + 1f;
            return false;
        }

        // somewhere to stand: solid, not steep, not deep water, about the player's level
        static bool Ground(Vector3 w, Vector3 pp, out Vector3 g)
        {
            g = w;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(w.x, pp.y + 2.5f, w.z), Vector3.down, out hit, 6f, GroundMask())) return false;
            if (hit.normal.y < 0.6f || Mathf.Abs(hit.point.y - pp.y) > 2.5f) return false;
            if (MHR_Water.DepthAt(hit.point) > 1.0f) return false;
            if (MHR_Mover.InFire(hit.point)) return false;   // 1.4.88: not in the fire pit
            g = hit.point;
            return true;
        }
        static bool Los(Vector3 from, Vector3 to)
        {
            return !Physics.Linecast(from + Vector3.up * 1.6f, to + Vector3.up * 1.0f, LosMask());
        }
        static bool Crowded(CompanionAI ai, int tid, Vector3 g, float spacing)
        {
            int me = ai.gameObject.GetInstanceID();
            float now = Time.time;
            foreach (KeyValuePair<int, S> kv in s_s)
            {
                S o = kv.Value;
                if (kv.Key == me || o.TargetId != tid || now - o.LastUpdate > 2f) continue;
                if (o.HasSpot && !o.Trail && Flat(g, o.Spot) < spacing) return true;
            }
            return false;
        }
        // companions on the trail line up along it instead of piling onto one point
        static int Rank(CompanionAI ai, int tid)
        {
            int me = ai.gameObject.GetInstanceID(), rank = 0;
            float now = Time.time;
            foreach (KeyValuePair<int, S> kv in s_s)
                if (kv.Key != me && kv.Key < me && kv.Value.TargetId == tid && kv.Value.Trail && now - kv.Value.LastUpdate < 2f) rank++;
            return rank;
        }
        static Vector3 TrailPoint(T t, Vector3 pp, float back)
        {
            float d = 0f; Vector3 prev = pp;
            for (int i = t.Trail.Count - 1; i >= 0; i--)
            {
                d += Vector3.Distance(prev, t.Trail[i]); prev = t.Trail[i];
                if (d >= back) return t.Trail[i];
            }
            return t.Trail.Count > 0 ? t.Trail[0] : pp - t.Travel * back;
        }
        static bool IdlePoint(CompanionAI ai, S s, Vector3 pp, float maxD, float range, int tid, out Vector3 g)
        {
            g = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * range;
                Vector3 w = s.IdleAnchor + new Vector3(r.x, 0f, r.y);
                if (!Ground(w, pp, out g)) continue;
                float dp = Flat(g, pp);
                if (dp > maxD || dp < 1.2f || Crowded(ai, tid, g, 1.2f) || !Los(s.IdleAnchor, g)) continue;
                return true;
            }
            return false;
        }

        // ---------- sitting down indoors ----------
        static bool Settle(CompanionAI ai, S s, Character c, Vector3 pp, float maxD, float dt, float now)
        {
            CompanionSetup cs = ai.GetComponent<CompanionSetup>();
            if (cs == null || !cs.CanWearArmor()) return false;   // no sitting animations
            // near home, put away what it picked up while working first (MHR_ToolKeeper takes over for the trip)
            if (!s.UnloadAsked) { s.UnloadAsked = true; s.UnloadAskedAt = now; MHR_ToolKeeper.RequestUnload(cs); return false; }
            if (now - s.UnloadAskedAt < 2.5f || MHR_ToolKeeper.IsBusy(cs)) return false;
            RepairController rc = ai.GetComponent<RepairController>();
            if (rc != null && rc.IsActive) return false;

            if (s.GoingToChair)
            {
                if (s.Chair == null || ChairTaken(s.Chair, ai) || now > s.ChairUntil || MHR_Mover.GaveUp(ai))
                {
                    ReleaseChair(s); s.ChairFailUntil = now + 20f;
                    MHR_Mover.Release(ai);
                }
                else
                {
                    Vector3 ap = s.Chair.m_attachPoint.position;
                    if (Vector3.Distance(ai.transform.position, ap) < 1.3f) SitInChair(ai, s, s.Chair, pp);
                    else MHR_Mover.Go(ai, dt, ap, 0.9f, false, MHR_Mover.FollowTag, MHR_Mover.SpeedWalk);
                    return true;
                }
            }
            if (now >= s.ChairFailUntil && now >= s.NextChairLook)
            {
                s.NextChairLook = now + 3f;
                Chair ch = FindChair(ai, pp, maxD + 1.5f);
                if (ch != null)
                {
                    s.Chair = ch; s.GoingToChair = true; s.ChairUntil = now + 15f;
                    Dbg(c.m_name + " heading for " + ch.m_name + " at " + MHR_Mover.Fmt(ch.m_attachPoint.position));
                    return true;
                }
            }
            if (Flat(ai.transform.position, pp) <= maxD) { SitOnFloor(ai, s, pp); return true; }
            return false;
        }

        static bool ChairTaken(Chair ch, CompanionAI ai)
        {
            if (ch == null || ch.m_attachPoint == null) return true;
            try { if (ch.IsInUse()) return true; } catch { }
            int me = ai.gameObject.GetInstanceID();
            float now = Time.time;
            foreach (KeyValuePair<int, S> kv in s_s)
                if (kv.Key != me && kv.Value.Chair == ch && (kv.Value.SitChair || kv.Value.GoingToChair) && now - kv.Value.LastUpdate < 5f) return true;
            return false;
        }

        // a free chair or bench seat near the player, on their level and in sight of them (not on a ship)
        static Chair FindChair(CompanionAI ai, Vector3 pp, float range)
        {
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(pp, range, s_pieces);
            Vector3 me = ai.transform.position;
            Chair best = null; float bd = float.MaxValue;
            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece p = s_pieces[i];
                if (p == null) continue;
                Chair[] chairs = p.GetComponentsInChildren<Chair>();
                for (int k = 0; k < chairs.Length; k++)
                {
                    Chair ch = chairs[k];
                    if (ch == null || ch.m_inShip || ch.m_attachPoint == null) continue;
                    Vector3 ap = ch.m_attachPoint.position;
                    if (Mathf.Abs(ap.y - pp.y) > 2f || Flat(ap, pp) > range || ChairTaken(ch, ai)) continue;
                    RaycastHit hit;
                    if (Physics.Linecast(pp + Vector3.up * 1.6f, ap + Vector3.up * 1.2f, out hit, LosMask()) && hit.collider.GetComponentInParent<Piece>() != p) continue;
                    float d = Vector3.Distance(me, ap);
                    if (d < bd) { bd = d; best = ch; }
                }
            }
            return best;
        }

        static void SitInChair(CompanionAI ai, S s, Chair ch, Vector3 pp)
        {
            MHR_Mover.Release(ai);
            ai.StopMoving();
            s.GoingToChair = false; s.SitChair = true; s.SitPlayerPos = pp; s.ChairAnim = ch.m_attachAnimation;
            // pass through the chair itself (like ship seats) so physics doesn't push the companion off it
            Piece piece = ch.GetComponentInParent<Piece>();
            s.ChairCols = piece != null ? piece.GetComponentsInChildren<Collider>() : ch.GetComponentsInChildren<Collider>();
            IgnoreChair(ai, s, true);
            Pin(ai, s);
            ZSyncAnimation za = ai.GetComponent<ZSyncAnimation>();
            if (za != null && !string.IsNullOrEmpty(s.ChairAnim)) za.SetBool(s.ChairAnim, true);
            SetResting(ai, true);
            Dbg(ai.GetComponent<Character>().m_name + " sat down on " + ch.m_name);
        }
        // keep the seated companion on the seat. Not kinematic: the game sets every character's velocity each physics
        // tick, and on a kinematic body that's a Unity warning every time (1.4.76 test log: thousands of them).
        static void Pin(CompanionAI ai, S s)
        {
            if (s.Chair == null || s.Chair.m_attachPoint == null) return;
            Transform at = s.Chair.m_attachPoint;
            ai.transform.position = at.position; ai.transform.rotation = at.rotation;
            Rigidbody rb = ai.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic) { rb.position = at.position; rb.linearVelocity = Vector3.zero; rb.useGravity = false; }
        }
        static void IgnoreChair(CompanionAI ai, S s, bool ignore)
        {
            Collider me = ai.GetComponent<CapsuleCollider>();
            if (me == null || s.ChairCols == null) return;
            for (int i = 0; i < s.ChairCols.Length; i++)
                if (s.ChairCols[i] != null) Physics.IgnoreCollision(me, s.ChairCols[i], ignore);
            if (!ignore) s.ChairCols = null;
        }

        static void SitOnFloor(CompanionAI ai, S s, Vector3 pp)
        {
            MHR_Mover.Release(ai);
            ai.StopMoving();
            Vector3 d = pp - ai.transform.position; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) ai.transform.rotation = Quaternion.LookRotation(d.normalized);
            s.SitFloor = true; s.SitPlayerPos = pp;
            ZSyncAnimation za = ai.GetComponent<ZSyncAnimation>();
            if (za != null) { za.SetTrigger("emote_sit"); za.SetBool("emote_sit", true); }
            SetResting(ai, true);
            Dbg(ai.GetComponent<Character>().m_name + " sat down on the floor");
        }

        static void SetResting(CompanionAI ai, bool on)
        {
            CompanionStamina st = ai.GetComponent<CompanionStamina>();
            if (st != null) st.IsResting = on;
        }

        static void ReleaseChair(S s) { s.GoingToChair = false; s.Chair = null; }

        internal static bool StandSpot(Vector3 seat, Vector3 fwd, Vector3 right, out Vector3 spot)
        {
            fwd.y = 0f; right.y = 0f;
            Vector3[] dirs = { fwd, right, -right, (fwd + right).normalized, (fwd - right).normalized, -fwd };
            for (int i = 0; i < dirs.Length; i++)
            {
                if (dirs[i].sqrMagnitude < 0.01f) continue;
                for (float r = 0.8f; r <= 1.6f; r += 0.4f)
                {
                    Vector3 p = seat + dirs[i].normalized * r;
                    RaycastHit hit;
                    if (!Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out hit, 2.5f, GroundMask()) || hit.normal.y <= 0.6f) continue;
                    if (hit.point.y > seat.y + 0.3f) continue;   // on top of something (a hearth, a table), not the floor
                    if (hit.collider.GetComponentInParent<Fireplace>() != null || MHR_Chests.IsFurniture(hit.collider) || MHR_Mover.InFire(hit.point)) continue;
                    if (Physics.CheckCapsule(hit.point + Vector3.up * 0.6f, hit.point + Vector3.up * 1.6f, 0.3f, GroundMask())) continue;
                    spot = hit.point + Vector3.up * 0.05f;
                    return true;
                }
            }
            spot = seat;
            return false;
        }

        static void StandUp(CompanionAI ai, S s, bool keepAnim, string why)
        {
            ZSyncAnimation za = ai.GetComponent<ZSyncAnimation>();
            if (s.SitChair)
            {
                if (za != null && !string.IsNullOrEmpty(s.ChairAnim)) za.SetBool(s.ChairAnim, false);
                Rigidbody rb = ai.GetComponent<Rigidbody>();
                Vector3 pos = ai.transform.position;
                if (s.Chair != null && s.Chair.m_attachPoint != null)
                {
                    // step off the front of the seat onto the floor, like getting up (vanilla's detach offset if there's no floor there).
                    // 1.4.101: onto real floor with room to stand - not into the hearth in front of the chair (1.4.99 test: got up
                    // into the hearth and was stuck inside it), else the sides or behind
                    Transform at = s.Chair.m_attachPoint;
                    Vector3 spot;
                    if (StandSpot(at.position, at.forward, at.right, out spot)) pos = spot;
                    else pos = at.TransformPoint(s.Chair.m_detachOffset);
                }
                ai.transform.position = pos;
                IgnoreChair(ai, s, false);
                if (rb != null && !rb.isKinematic) { rb.useGravity = true; rb.position = pos; rb.linearVelocity = Vector3.zero; }
            }
            if (s.SitFloor && !keepAnim && za != null) { za.SetTrigger("emote_stop"); za.SetBool("emote_sit", false); }
            if (!keepAnim) SetResting(ai, false);
            Dbg(ai.GetComponent<Character>().m_name + " got up (" + why + ")");
            s.SitChair = false; s.SitFloor = false; s.Chair = null; s.GoingToChair = false;
            s.UnloadAsked = false; s.HasIdle = false; s.IdleMoving = false; s.SettleDelay = UnityEngine.Random.Range(5f, 10f);
        }

        // ---------- per-frame housekeeping (from MHR_ToolKeeper.Tick, owner only): get up when something else needs the companion ----------
        public static void Tick(CompanionSetup cs, CompanionAI ai)
        {
            if (cs == null || ai == null) return;
            try
            {
                S s = Peek(ai);
                if (s == null) return;
                if (s.GoingToChair && Time.time - s.LastUpdate > 0.5f) ReleaseChair(s);
                if (!s.SitChair && !s.SitFloor) return;
                string why = null; bool keepAnim = false;
                GameObject f = ai.GetFollowTarget();
                CompanionRest rest = cs.GetComponent<CompanionRest>();
                RepairController rc = cs.GetComponent<RepairController>();
                ZNetView nv = cs.GetComponent<ZNetView>();
                int mode = nv != null && nv.GetZDO() != null ? nv.GetZDO().GetInt(CompanionSetup.ActionModeHash, 0) : 0;
                if (rest != null && (rest.IsResting || rest.IsSleeping || rest.IsNavigating)) { why = "resting"; keepAnim = s.SitFloor; }
                else if (f == null || f.GetInstanceID() != s.TargetId) why = "not following";
                else if (ai.IsInCombat) why = "fight";
                else if (mode != 0 || cs.GetStayHome()) why = "new orders";
                else if (MHR_ToolKeeper.IsBusy(cs)) why = "trip";
                else if (rc != null && rc.IsActive) why = "repairs";
                else if (Time.time - s.LastUpdate > 1f) why = "busy";
                else
                {
                    T t;
                    bool indoors = s_t.TryGetValue(s.TargetId, out t) && t.Indoors;
                    float moved = Flat(f.transform.position, s.SitPlayerPos);
                    if (moved > 2.5f || (!indoors && moved > 1f)) why = "player moved";
                }
                if (why != null) StandUp(ai, s, keepAnim, why);
                else if (s.SitChair) Pin(ai, s);
            }
            catch (Exception e)
            {
                if (CompanionsPlugin.Log != null && Time.time >= s_nextErr) { s_nextErr = Time.time + 30f; CompanionsPlugin.Log.LogWarning("[Follow] " + e.Message); }
            }
        }
    }
}
