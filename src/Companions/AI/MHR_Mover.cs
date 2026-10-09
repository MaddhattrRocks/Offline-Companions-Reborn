using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace Companions
{
    // ---- 1.4.74: one movement layer. Trips, repairs and bed go through here (follow and jobs move over in later builds). ----
    // Follows the real path corner by corner, re-plans every second, walks/jogs/runs by distance, plans around water it
    // can't swim, jumps narrow streams, opens doors on the way (and closes them behind), and when stuck works through:
    // open a door -> step back on a diagonal -> hop toward the next corner -> detour -> give up (callers decide what next).
    public static class MHR_Mover
    {
        class S
        {
            public Vector3 Goal, Dest; public float Dist; public string Tag = "";
            public readonly List<Vector3> Path = new List<Vector3>();
            public int Corner; public float NextPlan; public bool PathOk, Partial;
            public float Best, ProgressTime, AnchorTime; public Vector3 Anchor;
            public int Ladder; public float StepUntil; public Vector3 StepDir; public bool StepRun;
            public bool HasDetour; public Vector3 Detour; public float DetourUntil;
            public bool GaveUp; public float GaveUpAt;
            public float LastGo, NextLog, NextDoorSearch, NextDoorPeek, NextJump, NextDive, NextWaterPeek, NextLineLog;
            public Door RouteDoor; public Vector3 RouteDoorSpot;
            public bool WaitStamina, TooTired; public float NeedStamina, WaitStart, NoWaitUntil;
            public float MainBest; public int Passes; public Vector3 Shore; public bool HasShore; public float NextShore;
            public bool Walking, NewLeg;
            public float NextStartSearch;
            public Door LastRouteDoor; public float LastRouteDoorAt;
            public float RouteSwim;
            public bool MainPartial;
            public Vector3 GoodPos; public float GoodAt; public bool HasGood;
            public float ClimbUntil, ClimbBestY, ClimbProgressAt, NextClimbJump, ClimbRestUntil; public Vector3 ClimbTop;
            public int ClimbTries; public readonly List<Vector3> ClimbTried = new List<Vector3>();
            public bool Direct; public float NextHop, NoDirectUntil;
        }
        class Opened { public Door Door; public float At; }

        static readonly Dictionary<int, S> s_s = new Dictionary<int, S>();
        static readonly Dictionary<int, List<Opened>> s_opened = new Dictionary<int, List<Opened>>();
        static readonly List<Vector3> s_tmp = new List<Vector3>();
        static readonly List<Vector3> s_tmp2 = new List<Vector3>();
        static readonly List<Piece> s_pieces = new List<Piece>();
        static readonly Collider[] s_cols = new Collider[16];
        static int s_mask = -1;
        static float s_nextErr;

        public const float StillTime = 2.5f, NoProgressTime = 8f;

        static int Mask()
        {
            if (s_mask == -1) s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
            return s_mask;
        }
        static S Get(Component c)
        {
            if (c == null) return null;
            S s;
            if (!s_s.TryGetValue(c.gameObject.GetInstanceID(), out s)) { s = new S(); s_s[c.gameObject.GetInstanceID()] = s; }
            return s;
        }
        static S Peek(Component c)
        {
            S s;
            return c != null && s_s.TryGetValue(c.gameObject.GetInstanceID(), out s) ? s : null;
        }
        static void Dbg(string msg) { if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogDebug(msg); }
        public static string Fmt(Vector3 v) { return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")"; }
        static float Flat(Vector3 a, Vector3 b) { return new Vector2(a.x - b.x, a.z - b.z).magnitude; }

        // ---------- queries for the callers ----------
        public static bool OwnsMovement(Component c) { S s = Peek(c); return s != null && Time.time - s.LastGo < 0.3f; }
        public static bool GaveUp(CompanionAI ai) { S s = Peek(ai); return s != null && s.GaveUp; }
        // 1.4.79: the current route doesn't reach the goal (Follow switches to the player's path then)
        public static bool RoutePartial(CompanionAI ai) { S s = Peek(ai); return s != null && Time.time - s.LastGo < 0.5f && s.Partial && !s.HasDetour; }
        // 1.4.81: meters of swimming on the current route
        public static float RouteSwim(CompanionAI ai) { S s = Peek(ai); return s != null && Time.time - s.LastGo < 0.5f ? s.RouteSwim : 0f; }
        // 1.4.82: length of the current route, for callers that compare it with the straight line (0 when not moving)
        public static float RouteLength(CompanionAI ai)
        {
            S s = Peek(ai);
            if (s == null || Time.time - s.LastGo >= 0.5f || !s.PathOk || s.Path.Count == 0) return 0f;
            return Remaining(s, ai.transform.position);
        }
        public static bool Progressing(CompanionAI ai) { S s = Peek(ai); return s != null && !s.GaveUp && Time.time - s.ProgressTime < 10f; }
        public static float StuckSeconds(CompanionAI ai) { return GaveUp(ai) ? 11f : 0f; }
        // CompanionRest keeps its bed trip alive while we're still getting somewhere
        public static bool KeepAlive(Component c)
        {
            S s = Peek(c);
            return s != null && !s.GaveUp && Time.time - s.LastGo < 0.5f && Time.time - s.ProgressTime < 10f;
        }
        public static void Release(CompanionAI ai)
        {
            S s = Peek(ai);
            if (s == null) return;
            s.LastGo = 0f; s.GaveUp = false; s.Path.Clear(); s.HasDetour = false; s.RouteDoor = null;
            Character c = ai.GetComponent<Character>();
            if (c != null && s.Walking) { c.SetWalk(false); s.Walking = false; }
        }

        // ---------- main entry ----------
        // Returns true once within 'dist' (flat) and on the same level (within 1.5m up/down).
        public static bool Go(CompanionAI ai, float dt, Vector3 goal, float dist, bool allowRun, string tag)
        {
            return Go(ai, dt, goal, dist, allowRun, tag, SpeedAuto);
        }

        // 1.4.76: speed set by the caller (Follow matches the player's pace) instead of by distance.
        public const int SpeedAuto = 0, SpeedWalk = 1, SpeedJog = 2, SpeedRun = 3;
        public const string FollowTag = "Follow";
        public static bool Go(CompanionAI ai, float dt, Vector3 goal, float dist, bool allowRun, string tag, int speed)
        {
            if (ai == null) return false;
            try { return GoInner(ai, goal, dist, allowRun, tag ?? "", speed); }
            catch (Exception e)
            {
                if (CompanionsPlugin.Log != null && Time.time >= s_nextErr) { s_nextErr = Time.time + 30f; CompanionsPlugin.Log.LogWarning("[Move] " + e.Message); }
                return false;
            }
        }

        static bool GoInner(CompanionAI ai, Vector3 goal, float dist, bool allowRun, string tag, int speed)
        {
            float now = Time.time;
            S s = Get(ai);
            Character c = ai.GetComponent<Character>();
            Vector3 pos = ai.transform.position;
            float shift = Vector3.Distance(goal, s.Goal);
            bool fresh = shift > 4f || now - s.LastGo > 2f || s.Tag != tag;
            if (s.GaveUp && now - s.GaveUpAt > 15f) fresh = true;   // try again after a while
            if (fresh) Reset(s, goal, pos, now, tag);
            // 1.4.78: a new nearby target (the farmer's next crop 2 m on) or carrying on after a pause (harvesting,
            // swinging) starts the stuck clocks over. They used to keep running while he stood working, so the next
            // short step counted as "standing still 4s" and he started sidestepping and hopping around the field.
            // 1.4.82: a new goal also clears an old give-up (1.4.81 log: planting stopped because the give-up from the
            // last spot was still set). A mere pause on the same goal keeps the stuck ladder where it was: the harvest
            // swings reset it every few seconds, so in the moat he sat on "step 1" forever and never gave up.
            else if (now - s.LastGo > 0.5f || (shift > 1.5f && tag != FollowTag))
            {
                s.AnchorTime = now; s.Anchor = pos; s.ProgressTime = now; s.Best = float.MaxValue; s.StepUntil = 0f;
                if (shift > 1.5f) { s.MainBest = float.MaxValue; s.Ladder = 0; s.Passes = 0; s.GaveUp = false; }
            }
            else if (shift > 1.5f && !s.HasDetour && s.RouteDoor == null) s.NextPlan = 0f;
            // 1.4.76: a goal that keeps moving (Follow): re-plan once it has drifted from the planned destination
            else if (!s.HasDetour && s.RouteDoor == null && Vector3.Distance(goal, s.Dest) > 1.5f && s.NextPlan > now + 0.7f) s.NextPlan = Mathf.Min(s.NextPlan, now + 0.3f);
            s.LastGo = now; s.Goal = goal; s.Dist = dist;

            // arrived: within reach on the same level (not standing under it on the floor below)
            if (Flat(pos, goal) <= dist && Mathf.Abs(pos.y - goal.y) <= 1.5f) { Halt(ai, c, s); return true; }
            if (s.GaveUp) { Halt(ai, c, s); return false; }
            if (c == null || c.InAttack()) return false;

            // steering target: a detour point, the door we're routing through, or the path
            if (now >= s.NextPlan || s.Path.Count == 0) Plan(ai, c, s, pos, now);
            AdvanceCorner(s, pos);
            float remain = Remaining(s, pos);
            if (s.NewLeg) { s.NewLeg = false; s.Best = remain; }
            if (remain < s.Best - 0.75f) { s.Best = remain; s.ProgressTime = now; }
            if (!s.HasDetour && s.RouteDoor == null && remain < s.MainBest - 1.5f) { s.MainBest = remain; s.Ladder = 0; s.Passes = 0; }
            if (Vector3.Distance(pos, s.Anchor) > 0.6f) { s.Anchor = pos; s.AnchorTime = now; }

            Log(s, pos, remain, now, c);

            // 1.4.82: climbing out of a pit
            if (now < s.ClimbUntil) { Climb(ai, c, s, pos, now, tag); return false; }

            // a sidestep / hop in progress
            if (now < s.StepUntil) { Steer(ai, c, s, s.StepDir, s.StepRun, false); return false; }

            // 1.4.88: never stand in a fire
            if (EscapeFire(ai)) return false;

            // waiting on the bank until there's stamina for the swim
            if (s.WaitStamina)
            {
                if (Stamina(ai) >= s.NeedStamina) { s.WaitStamina = false; s.TooTired = false; s.NoWaitUntil = now + 30f; s.NextPlan = 0f; Dbg("[Move|" + tag + "] rested enough to swim across"); }
                else if (now - s.WaitStart > 40f) { s.WaitStamina = false; s.TooTired = false; s.NoWaitUntil = now + 60f; Dbg("[Move|" + tag + "] waited long enough - swimming across"); }
                else { Halt(ai, c, s); s.ProgressTime = now; s.AnchorTime = now; return false; }
            }

            if (s.HasDetour && (Flat(pos, s.Detour) < 1.0f || now > s.DetourUntil)) { s.HasDetour = false; Plan(ai, c, s, pos, now); }
            if (s.RouteDoor != null)
            {
                if (!DoorClosed(s.RouteDoor)) { s.RouteDoor = null; Plan(ai, c, s, pos, now); }
                else if (MHR_Nav.DoorDist(pos, s.RouteDoor.transform.position) <= 2.2f)
                {
                    // open it, then walk through to the far side before asking for a new route
                    Vector3 dp = s.RouteDoor.transform.position, across = dp - s.RouteDoorSpot; across.y = 0f;
                    OpenDoor(ai, c, s.RouteDoor, tag);
                    s.LastRouteDoor = s.RouteDoor; s.LastRouteDoorAt = now;
                    if (across.sqrMagnitude > 0.01f) { s.HasDetour = true; s.Detour = dp + across.normalized * 1.8f; s.DetourUntil = now + 5f; }
                    s.RouteDoor = null; s.NextPlan = 0f; Halt(ai, c, s);
                    return false;
                }
            }
            Vector3 target = NextCorner(s, pos);

            // swimming: head for the bank that's closest to where we're going, unless the path already knows the way
            if (c.IsSwimming() && (!s.PathOk || s.Partial))
            {
                if (now >= s.NextShore) { s.NextShore = now + 0.5f; Vector3 sh; s.HasShore = FindShore(c, pos, goal, out sh); s.Shore = sh; }
                if (s.HasShore) target = s.Shore;
            }

            // a closed door right ahead: open it before walking into it
            if (now >= s.NextDoorPeek) { s.NextDoorPeek = now + 0.4f; Door ahead = DoorAhead(pos, target); if (ahead != null) { OpenDoor(ai, c, ahead, tag); Halt(ai, c, s); return false; } }

            Vector3 dir = target - pos; dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) { Halt(ai, c, s); return false; }
            dir.Normalize();

            // water on the next stretch: jump narrow streams, take a running leap into wide water
            if (!c.IsSwimming() && c.IsOnGround() && now >= s.NextJump && now >= s.NextWaterPeek)
            {
                s.NextWaterPeek = now + 0.2f;
                if (TryWaterJump(ai, c, s, pos, dir, now, tag)) return false;
            }

            // 1.4.97: going straight and there's a gap right ahead: leap it
            if (s.Direct && !c.IsSwimming() && c.IsOnGround() && now >= s.NextJump && GapAhead(c, pos, dir))
            {
                Leap(ai, c, dir, 6.5f);
                s.NextJump = now + 1.2f; s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.6f;
                Dbg("[Move|" + tag + "] jumping the gap at " + Fmt(pos + dir * 0.8f));
                return false;
            }

            // 1.4.88: a fire on the next step (and the goal isn't in it): jump over it like a player when it's small
            // (clear ground 2.5 m on), otherwise go round it
            if (InFire(pos + dir * 0.8f) && !InFire(goal))
            {
                if (c.IsOnGround() && now >= s.NextHop && !InFire(pos + dir * 2.5f) && !DropAhead(pos, dir, 2.5f, 1.2f))
                {
                    s.NextHop = now + 1.0f;
                    ai.transform.rotation = Quaternion.LookRotation(dir);
                    c.Jump(false);
                    Rigidbody rb = ai.GetComponent<Rigidbody>();
                    if (rb != null) { Vector3 v = rb.linearVelocity; v.x = dir.x * 6f; v.z = dir.z * 6f; rb.linearVelocity = v; }
                    s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.6f;
                    Dbg("[Move|" + tag + "] jumping over a fire at " + Fmt(pos + dir * 1.2f));
                    return false;
                }
                Vector3 round;
                if (FireExit(pos, dir, out round)) dir = round;
            }

            float runDist = ModConfig.WorkRunDistance != null ? ModConfig.WorkRunDistance.Value : 25f;
            bool run = allowRun && runDist > 0f && remain > runDist;
            bool walk = remain < 2.5f;
            if (speed == SpeedWalk) { run = false; walk = true; }
            else if (speed == SpeedJog) { run = false; walk = false; }
            else if (speed == SpeedRun) { run = allowRun; walk = false; }
            Steer(ai, c, s, dir, run, walk);
            // 1.4.83: going straight: hop the step in front, like a player
            // 1.4.84: and on a normal route, when something low has stopped us for a moment
            // 1.4.86: walk up stairs like a player; only hop when a step actually holds him up (1.4.85 hopped every step)
            if (now >= s.NextHop && c.IsOnGround() && !c.InAttack() && !c.IsSwimming() && (s.Direct || now - s.AnchorTime > 0.5f) && StepAhead(pos, dir))
            {
                s.NextHop = now + 0.6f;
                ai.transform.rotation = Quaternion.LookRotation(dir);
                c.Jump(false);
                Rigidbody rb = ai.GetComponent<Rigidbody>();
                if (rb != null) { Vector3 v = rb.linearVelocity; v.x = dir.x * 4f; v.z = dir.z * 4f; rb.linearVelocity = v; }
            }

            // stuck?
            float still = now - s.AnchorTime, noCloser = now - s.ProgressTime;
            // 1.4.76: keeping pace with a player who's moving isn't getting closer, but isn't stuck either
            if (tag == FollowTag) noCloser = 0f;
            if (still >= StillTime || noCloser >= NoProgressTime) Unstick(ai, c, s, pos, target, now, tag, still, noCloser);
            return false;
        }

        static void Reset(S s, Vector3 goal, Vector3 pos, float now, string tag)
        {
            s.Goal = goal; s.Dest = goal; s.Tag = tag; s.Path.Clear(); s.Corner = 0; s.NextPlan = 0f; s.PathOk = false; s.Partial = false;
            s.Best = float.MaxValue; s.ProgressTime = now; s.AnchorTime = now; s.Anchor = pos;
            s.Ladder = 0; s.StepUntil = 0f; s.HasDetour = false; s.GaveUp = false; s.RouteDoor = null; s.WaitStamina = false;
            s.TooTired = false; s.MainBest = float.MaxValue; s.Passes = 0; s.HasShore = false; s.NextShore = 0f;
            s.NextDoorSearch = 0f; s.NextLog = now + 1f; s.NextStartSearch = 0f;
            s.ClimbUntil = 0f; s.ClimbTries = 0; s.ClimbTried.Clear(); s.ClimbRestUntil = 0f; s.Direct = false;
        }

        static void Halt(CompanionAI ai, Character c, S s)
        {
            ai.StopMoving();
            if (c != null && s.Walking) { c.SetWalk(false); s.Walking = false; }
        }

        static void Steer(CompanionAI ai, Character c, S s, Vector3 dir, bool run, bool walk)
        {
            if (walk != s.Walking) { c.SetWalk(walk); s.Walking = walk; }
            ai.MoveTowards(dir, run && !walk);
        }

        // ---------- path ----------
        static void Plan(CompanionAI ai, Character c, S s, Vector3 pos, float now)
        {
            s.NextPlan = now + 1f;
            s.Path.Clear(); s.Corner = 0;
            Vector3 goal = s.HasDetour ? s.Detour : s.RouteDoor != null ? s.RouteDoorSpot : s.Goal;
            if (Vector3.Distance(goal, s.Dest) > 1.5f) { s.NewLeg = true; s.ProgressTime = now; }   // new leg: measure progress from here
            s.Dest = goal;
            s.RouteSwim = 0f;
            bool ok = GetPath(pos, goal, s.Path, MHR_CompanionNavMesh.AgentOrHumanoid, false);
            if (!ok || s.Path.Count == 0)
            {
                s.PathOk = false; s.Partial = true; s.Path.Clear(); s.Path.Add(goal);
                if (!s.HasDetour && s.RouteDoor == null) s.MainPartial = true;
                // 1.4.75: no route can start from where we stand (e.g. in a doorway): step to a spot a meter or two
                // away that a route does start from, right away instead of after the stuck ladder
                Vector3 start;
                if (!s.HasDetour && s.RouteDoor == null && !c.IsSwimming() && now >= s.NextStartSearch)
                {
                    s.NextStartSearch = now + 2f;
                    if (FindRouteStart(pos, goal, out start))
                    {
                        s.HasDetour = true; s.Detour = start; s.DetourUntil = now + 3f;
                        s.Path.Clear(); s.Path.Add(start);
                        Dbg("[Move|" + s.Tag + "] no route from " + Fmt(pos) + " - stepping to " + Fmt(start) + " to find one");
                    }
                }
            }
            else
            {
                s.PathOk = true;
                Vector3 end = s.Path[s.Path.Count - 1];
                s.Partial = Flat(end, goal) > 1.5f || Mathf.Abs(end.y - goal.y) > 2.2f;
                // 1.4.82: the last place a full route to the goal started from, for the trapped rescue below
                if (!s.HasDetour && s.RouteDoor == null)
                {
                    s.MainPartial = s.Partial;
                    if (!s.Partial && c.IsOnGround() && !c.IsSwimming()) { s.GoodPos = pos; s.GoodAt = now; s.HasGood = true; }
                }
                // tired: a swim longer than the stamina allows -> a dry route, or wait on the bank
                s.TooTired = false;
                if (!c.IsSwimming())
                {
                    float swim = MHR_Water.SwimMeters(c, pos, s.Path, 60f);
                    s.RouteSwim = swim;
                    if (swim > 0.5f)
                    {
                        float need = MHR_Water.SwimStamina(c, swim) * 1.25f + 5f;
                        float max = MaxStamina(ai);
                        if (max > 0f) need = Mathf.Min(need, max * 0.9f);
                        if (now >= s.NoWaitUntil && Stamina(ai) < need)
                        {
                            s_tmp.Clear();
                            if (GetPath(pos, goal, s_tmp, Pathfinding.AgentType.HumanoidAvoidWater, true))
                            {
                                s.Path.Clear(); s.Path.AddRange(s_tmp); s.Partial = false;
                                if (now >= s.NextLog) Dbg("[Move|" + s.Tag + "] too tired to swim " + swim.ToString("F0") + "m - going around the water");
                            }
                            else { s.TooTired = true; s.NeedStamina = need; }   // stops at the water's edge (see TryWaterJump)
                        }
                    }
                }
            }
            // 1.4.83: a long way round for something close (1.4.82 log: 85-120 m round the base for the steps in front of
            // it, 4 m away - the route can't use the steps): if the straight line only has steps a player would hop up,
            // go straight and jump them
            bool wasDirect = s.Direct;
            s.Direct = false;
            if (!s.HasDetour && s.RouteDoor == null && !c.IsSwimming() && now >= s.NoDirectUntil)
            {
                float straight = Vector3.Distance(pos, goal);
                float len = Remaining(s, pos);
                int hops = 0; s_hopWhy = null;
                // 1.4.84: also a short way round something low that's in the way (a log, a low wall): a player hops it
                bool longWay = s.Partial || len > straight * 1.6f + 6f;
                bool line = straight < 25f && (longWay || len > straight + 2f) && HopLine(pos, goal, out hops, c);
                if (!line && straight < 25f && longWay && s_hopWhy != null && now >= s.NextLineLog)
                {
                    s.NextLineLog = now + 15f;
                    Dbg("[Move|" + s.Tag + "] taking the " + len.ToString("F0") + "m way round, not straight (" + straight.ToString("F0") + "m): " + s_hopWhy);
                }
                if (line && (longWay || hops > 0))
                {
                    if (!wasDirect) Dbg("[Move|" + s.Tag + "] going straight to " + Fmt(goal) + " (" + hops + " step(s) to hop) instead of " + (s.Partial ? "no route" : "a " + len.ToString("F0") + "m way round"));
                    s.Path.Clear(); s.Path.Add(goal); s.Corner = 0; s.PathOk = true; s.Partial = false; s.MainPartial = false; s.Direct = true; s.RouteSwim = 0f;
                }
            }
            // no full route: maybe a closed door is in the way (closed doors block the route); go open it like a player would
            // 1.4.79: not while following - a door 20 m the other way isn't how you keep up with someone (1.4.78 log);
            // MHR_Follow walks the player's own path instead when the route doesn't reach its spot
            if (s.Partial && s.Tag != FollowTag && s.RouteDoor == null && !s.HasDetour && now >= s.NextDoorSearch)
            {
                s.NextDoorSearch = now + 4f;
                // 1.4.78: not the door we just came through - if the route still doesn't reach the goal from the other
                // side, that door isn't the answer (1.4.77 log: back and forth through the same gate six times)
                Door skip = now - s.LastRouteDoorAt < 30f ? s.LastRouteDoor : null;
                Vector3 spot; Door d = RouteDoor(pos, goal, out spot, skip);
                if (d != null) { s.RouteDoor = d; s.RouteDoorSpot = spot; s.NextPlan = 0f; Dbg("[Move|" + s.Tag + "] no way through - heading for the closed " + d.m_name + " at " + Fmt(d.transform.position)); }
                // 1.4.84: no door either: a spot the route does reach, from which the rest can be hopped (stairs)
                else if (!c.IsSwimming())
                {
                    Vector3 b;
                    if (FindHopBridge(pos, goal, out b) && Flat(pos, b) > 1.0f)
                    {
                        s.HasDetour = true; s.Detour = b; s.DetourUntil = now + 10f + Flat(pos, b) * 0.5f; s.NextPlan = 0f;
                        Dbg("[Move|" + s.Tag + "] no route to " + Fmt(goal) + " - going to " + Fmt(b) + " to hop the rest");
                    }
                }
            }
        }

        static bool GetPath(Vector3 from, Vector3 to, List<Vector3> path, Pathfinding.AgentType agent, bool requireFull)
        {
            path.Clear();
            return Pathfinding.instance != null && Pathfinding.instance.GetPath(from, to, path, agent, requireFull, true, false) && path.Count > 0;
        }

        static void AdvanceCorner(S s, Vector3 pos)
        {
            while (s.Corner < s.Path.Count - 1)
            {
                Vector3 p = s.Path[s.Corner];
                if (Flat(pos, p) < 0.6f && Mathf.Abs(pos.y - p.y) < 1.6f) s.Corner++;
                else break;
            }
        }
        static Vector3 NextCorner(S s, Vector3 pos)
        {
            if (s.Path.Count == 0) return s.Dest;
            int i = Mathf.Clamp(s.Corner, 0, s.Path.Count - 1);
            // last corner of a partial path reached: keep pressing toward where we're going
            if (i == s.Path.Count - 1 && Flat(pos, s.Path[i]) < 0.8f) return s.Dest;
            return s.Path[i];
        }
        static float Remaining(S s, Vector3 pos)
        {
            if (s.Path.Count == 0) return Vector3.Distance(pos, s.Dest);
            float d = 0f; Vector3 prev = pos;
            for (int i = Mathf.Clamp(s.Corner, 0, s.Path.Count - 1); i < s.Path.Count; i++) { d += Vector3.Distance(prev, s.Path[i]); prev = s.Path[i]; }
            if (s.Partial) d += Vector3.Distance(prev, s.Dest);
            return d;
        }

        // ---------- stuck ladder ----------
        static void Unstick(CompanionAI ai, Character c, S s, Vector3 pos, Vector3 target, float now, string tag, float still, float noCloser)
        {
            string why = still >= StillTime ? "standing still " + still.ToString("F0") + "s" : "no closer for " + noCloser.ToString("F0") + "s";
            s.AnchorTime = now; s.Anchor = pos;
            s.ProgressTime = now - (NoProgressTime - 4f);   // next check in ~4s unless we start getting somewhere
            // 1.4.83: stuck going straight: that way doesn't work after all, take the route for a while
            if (s.Direct) { s.Direct = false; s.NoDirectUntil = now + 20f; s.NextPlan = 0f; Dbg("[Move|" + tag + "] couldn't get through going straight at " + Fmt(pos) + " - taking the route"); return; }
            int step = s.Ladder++;
            Vector3 dir = target - pos; dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = ai.transform.forward;
            dir.Normalize();
            // 1.4.82: down in a pit with no route out: climb out like a player before anything else
            if (step == 0 && TryStartClimb(ai, c, s, pos, now, tag, why)) { s.Ladder = 0; return; }
            switch (step)
            {
                case 0:
                    {
                        Door d = DoorNear(pos, 2.6f);
                        if (d != null) { OpenDoor(ai, c, d, tag); s.NextPlan = now + 0.6f; return; }
                        Sidestep(ai, c, s, pos, dir, now, tag, why, true);
                        return;
                    }
                case 1: Hop(ai, c, s, pos, dir, now, tag, why); return;
                case 2: Sidestep(ai, c, s, pos, dir, now, tag, why, false); return;
                case 3: Hop(ai, c, s, pos, dir, now, tag, why); return;
                case 4:
                    {
                        // after two full rounds of this without getting any closer, stop
                        if (++s.Passes > 2) { s.Ladder = 99; Unstick(ai, c, s, pos, target, now, tag, still, noCloser); return; }
                        Vector3 det;
                        if (FindDetour(pos, s.Goal, dir, out det)) { s.HasDetour = true; s.Detour = det; s.DetourUntil = now + 6f; s.NextPlan = 0f; Dbg("[Move|" + tag + "] stuck (" + why + ") at " + Fmt(pos) + " - trying another way via " + Fmt(det)); return; }
                        Hop(ai, c, s, pos, dir, now, tag, why);
                        return;
                    }
                case 5: Hop(ai, c, s, pos, dir, now, tag, why); s.Ladder = s.Passes >= 2 ? 6 : 0; return;
                default:
                    s.GaveUp = true; s.GaveUpAt = now;
                    Halt(ai, c, s);
                    if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|" + tag + "] couldn't get to " + Fmt(s.Goal) + " from " + Fmt(pos) + " - giving up");
                    Rescue(ai, c, s, pos, now, tag);
                    return;
            }
        }

        static void Sidestep(CompanionAI ai, Character c, S s, Vector3 pos, Vector3 dir, float now, string tag, string why, bool leftFirst)
        {
            Vector3 back = -dir;
            Vector3 left = Quaternion.Euler(0f, -45f, 0f) * back, right = Quaternion.Euler(0f, 45f, 0f) * back;
            Vector3 a = leftFirst ? left : right, b = leftFirst ? right : left;
            Vector3 side = Quaternion.Euler(0f, 90f, 0f) * dir;
            if (DropAhead(pos, side, 1.5f, 1.2f)) side = DropAhead(pos, -side, 1.5f, 1.2f) ? Vector3.zero : -side;   // 1.4.82: never off a ledge
            Vector3 pick = Clear(pos, a) ? a : Clear(pos, b) ? b : Clear(pos, back) ? back : side;
            s.StepDir = pick; s.StepRun = false; s.StepUntil = now + 1.2f;
            Dbg("[Move|" + tag + "] stuck (" + why + ") at " + Fmt(pos) + " - stepping back to the side");
        }

        // ---------- climbing out ----------
        // 1.4.82: like a player in a dry moat: find the lowest / least steep way up within reach, sprint at it and keep
        // jumping. Non-player characters never slide (Character.GetSlideAngle is 90 for them), and anything flatter
        // than ~84 degrees counts as ground to jump from, so jumping up a steep face works for them as it does for a player.
        const int ClimbMaxTries = 3;
        const float ClimbMaxRise = 10f;
        static readonly int s_terrainLayer = LayerMask.NameToLayer("terrain");

        public static bool Climbing(CompanionAI ai) { S s = Peek(ai); return s != null && Time.time < s.ClimbUntil && Time.time - s.LastGo < 0.5f; }

        static bool TryStartClimb(CompanionAI ai, Character c, S s, Vector3 pos, float now, string tag, string why)
        {
            if (!s.MainPartial || c.IsSwimming() || !c.IsOnGround() || s.ClimbTries >= ClimbMaxTries) return false;
            // how high we need to get: the last spot a full route started from, else the goal itself
            float needY = s.HasGood && s.GoodPos.y > pos.y + 1.5f && Vector3.Distance(pos, s.GoodPos) < 40f ? s.GoodPos.y : s.Goal.y;
            if (needY < pos.y + 1.5f) return false;
            needY = Mathf.Min(needY, pos.y + ClimbMaxRise);
            Vector3 top; float steep;
            if (!FindClimb(s, pos, needY, out top, out steep)) return false;
            s.ClimbTries++; s.ClimbTried.Add(top);
            s.ClimbTop = top; s.ClimbUntil = now + 15f; s.ClimbBestY = pos.y; s.ClimbProgressAt = now; s.NextClimbJump = 0f;
            s.StepUntil = 0f; s.HasDetour = false;
            if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|" + tag + "] stuck (" + why + ") " + (needY - pos.y).ToString("F1") + "m below where it needs to be at " + Fmt(pos) + " - climbing out at " + Fmt(top) + " (steepest step " + steep.ToString("F1") + "m, try " + s.ClimbTries + ")");
            return true;
        }

        // Ground heights along 16 directions, a meter at a time out to 16 m: the way up whose steepest one-meter step is
        // smallest (shorter ways win ties), skipping ways already tried and anything through deep water.
        static bool FindClimb(S s, Vector3 pos, float needY, out Vector3 top, out float steep)
        {
            top = Vector3.zero; steep = 0f;
            float bestScore = float.MaxValue;
            int mask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default");
            for (int k = 0; k < 16; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward;
                float prevY = pos.y, maxStep = 0f;
                for (int i = 1; i <= 16; i++)
                {
                    Vector3 p = pos + dir * i;
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(p.x, needY + 4f, p.z), Vector3.down, out hit, needY + 4f - (pos.y - 3f), mask)) break;
                    if (MHR_Water.DepthAt(hit.point) >= 1.0f) break;
                    float rise = hit.point.y - prevY;
                    if (rise < -1.5f) break;   // dips back down: not a way up
                    if (rise > 1.0f && hit.collider.gameObject.layer != s_terrainLayer) break;   // a built wall or a roof, not a slope
                    if (rise > 6f) break;
                    if (rise > maxStep) maxStep = rise;
                    prevY = hit.point.y;
                    if (hit.point.y >= needY - 0.5f)
                    {
                        Vector3 t = hit.point + dir * 0.75f;
                        bool tried = false;
                        foreach (Vector3 old in s.ClimbTried) if (Flat(old, t) < 3f) { tried = true; break; }
                        float score = maxStep * 3f + i * 0.1f;
                        if (!tried && score < bestScore) { bestScore = score; top = t; steep = maxStep; }
                        break;
                    }
                }
            }
            return bestScore < float.MaxValue;
        }

        static void Climb(CompanionAI ai, Character c, S s, Vector3 pos, float now, string tag)
        {
            Vector3 dir = s.ClimbTop - pos; dir.y = 0f;
            bool atTop = pos.y >= s.ClimbTop.y - 0.6f;
            if (atTop || dir.sqrMagnitude < 0.25f)
            {
                s.ClimbUntil = 0f; s.NextPlan = 0f; s.Ladder = 0; s.Passes = 0; s.MainBest = float.MaxValue;
                s.ProgressTime = now; s.AnchorTime = now; s.Anchor = pos;
                if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|" + tag + "] " + (atTop ? "climbed out" : "reached the climb spot") + " at " + Fmt(pos));
                return;
            }
            dir.Normalize();
            if (pos.y > s.ClimbBestY + 0.3f) { s.ClimbBestY = pos.y; s.ClimbProgressAt = now; }
            // out of breath: stand and get some back, like a player would, then go again
            float max = MaxStamina(ai);
            if (max > 0f && (now < s.ClimbRestUntil || Stamina(ai) < max * 0.15f) && c.IsOnGround())
            {
                if (now >= s.ClimbRestUntil) s.ClimbRestUntil = now + 5f;
                if (Stamina(ai) >= max * 0.6f) s.ClimbRestUntil = 0f;
                Halt(ai, c, s); s.ClimbProgressAt = now; s.ClimbUntil = Mathf.Max(s.ClimbUntil, now + 6f);
                return;
            }
            // no higher in 5 s: this way doesn't go; the next stuck check picks another or carries on with the ladder
            if (now - s.ClimbProgressAt > 5f)
            {
                s.ClimbUntil = 0f; s.AnchorTime = now - StillTime; s.Ladder = 0;
                if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|" + tag + "] couldn't climb out at " + Fmt(s.ClimbTop) + " (got to " + Fmt(pos) + ")");
                return;
            }
            ai.transform.rotation = Quaternion.LookRotation(dir);
            Steer(ai, c, s, dir, true, false);
            // jump whenever there's ground under us and the face ahead rises, or we've stopped moving forward
            if (c.IsOnGround() && now >= s.NextClimbJump)
            {
                Vector3 v = c.GetVelocity(); v.y = 0f;
                RaycastHit hit;
                bool rises = Physics.Raycast(pos + dir * 0.9f + Vector3.up * 3f, Vector3.down, out hit, 4f, Mask()) && hit.point.y > pos.y + 0.4f;
                if (rises || v.magnitude < 1f)
                {
                    c.Jump(false);
                    Rigidbody rb = ai.GetComponent<Rigidbody>();
                    if (rb != null) { Vector3 bv = rb.linearVelocity; bv.x = dir.x * 5f; bv.z = dir.z * 5f; rb.linearVelocity = bv; }
                    s.NextClimbJump = now + 0.3f;
                }
            }
        }

        // 1.4.82: fell somewhere with no route out (1.4.81 log: down a dry moat with sheer walls, only a player's
        // sprint-jumping gets out). After a full stuck ladder, well below the last spot that had a full route and still
        // no route from here: put him back there.
        static void Rescue(CompanionAI ai, Character c, S s, Vector3 pos, float now, string tag)
        {
            if (!s.HasGood || !s.MainPartial || c.IsSwimming() || ai.IsInCombat) return;
            if (pos.y > s.GoodPos.y - 2f || Vector3.Distance(pos, s.GoodPos) > 40f || now - s.GoodAt > 300f) return;
            if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|" + tag + "] trapped at " + Fmt(pos) + " (" + (s.GoodPos.y - pos.y).ToString("F1") + "m below the last good spot) - moving back to " + Fmt(s.GoodPos));
            ai.MHR_PlaceAt(s.GoodPos);
            s.HasGood = false;
            Reset(s, s.Goal, s.GoodPos, now, tag);
        }

        // 1.4.83: the straight line from -> to, every half meter: each step up no more than a hop (1.1 m), no big drop,
        // no deep water, and nothing in the way at chest height (walls, doors, fences taller than a hop).
        // 1.4.97: drops up to General.MaxDrop (3 m - a player takes those without damage) and gaps up to General.JumpGap
        // (jumped with a running leap) are fine too (user: jump across to the beehives instead of down the stairs and round)
        static string s_hopWhy;
        static bool HopLine(Vector3 from, Vector3 to, out int hops) { return HopLine(from, to, out hops, null); }
        static bool HopLine(Vector3 from, Vector3 to, out int hops, Character c)
        {
            hops = 0; s_hopWhy = null;
            float maxDrop = ModConfig.MaxDrop != null ? ModConfig.MaxDrop.Value : 3f;
            float maxGap = ModConfig.JumpGap != null ? ModConfig.JumpGap.Value : 1.5f;
            Vector3 d = to - from; d.y = 0f;
            float len = d.magnitude;
            if (len < 1.5f) return false;
            d /= len;
            Vector3 prev = from;
            float gap = 0f;
            for (float x = 0.5f; x <= len + 0.01f; x += 0.5f)
            {
                Vector3 p = from + d * Mathf.Min(x, len);
                RaycastHit hit;
                // 1.4.84: from just above where the feet are, like walking: a roof or an upper floor overhead isn't the ground
                bool ground = Physics.Raycast(new Vector3(p.x, prev.y + 1.5f, p.z), Vector3.down, out hit, 2f + maxDrop, Mask());
                float rise = ground ? hit.point.y - prev.y : -100f;
                if (!ground || rise < -maxDrop)
                {
                    gap += 0.5f;
                    if (gap > maxGap) { s_hopWhy = "a drop or gap at " + Fmt(p); return false; }
                    continue;
                }
                if (gap > 0f)
                {
                    // landing after a jump: not higher than we can jump up to while leaping across
                    if (rise > 0.3f) { s_hopWhy = "the far side of the gap at " + Fmt(p) + " is too high"; return false; }
                    // 1.4.98: and within what this companion can jump right now (speed, Jump skill, stamina)
                    if (c != null && (!MHR_Jump.CanAfford(c) || gap + 0.25f > MHR_Jump.Reach(c, d, rise))) { s_hopWhy = "the gap at " + Fmt(p) + " is too wide to jump" + (MHR_Jump.CanAfford(c) ? "" : " (out of stamina)"); return false; }
                    hops++; gap = 0f;
                }
                if (rise > 1.1f) { s_hopWhy = "a step too high at " + Fmt(p); return false; }
                // 1.4.99: with the companion known, only water it would have to swim in rules the line out (it wades or jumps the rest)
                if (c != null ? MHR_Water.WouldSwim(c, hit.point) : MHR_Water.DepthAt(hit.point) > 0.8f) { s_hopWhy = "deep water at " + Fmt(p); return false; }
                if (Physics.Linecast(prev + Vector3.up * 1.4f, hit.point + Vector3.up * 1.4f, Mask())) { s_hopWhy = "something in the way at " + Fmt(p); return false; }
                if (rise > 0.35f) hops++;
                prev = hit.point;
            }
            if (gap > 0f) { s_hopWhy = "it ends over a drop"; return false; }
            // 1.4.88: and it has to actually end up where the target is. Sampling from just above the feet can't see a
            // ledge higher than that, so a chest up on one looked like "flat all the way, 0 hops" (1.4.87 log: back and
            // forth under the ledge chest for a minute after the job was turned off)
            if (Mathf.Abs(prev.y - to.y) > 1.0f) { s_hopWhy = "it doesn't end up at the target's height"; return false; }
            return hops <= 10;
        }

        // 1.4.97: on a straight line, a gap right in front with ground on the other side: running leap across
        static bool GapAhead(Character c, Vector3 pos, Vector3 dir)
        {
            float maxDrop = ModConfig.MaxDrop != null ? ModConfig.MaxDrop.Value : 3f;
            float maxGap = ModConfig.JumpGap != null ? ModConfig.JumpGap.Value : 1.5f;
            RaycastHit hit;
            Vector3 p = pos + dir * 0.6f;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 1.5f + maxDrop, Mask())) return false;
            for (float x = 1.0f; x <= maxGap + 1.0f; x += 0.5f)
            {
                Vector3 q = pos + dir * x;
                if (Physics.Raycast(q + Vector3.up * 1.5f, Vector3.down, out hit, 1.5f + maxDrop, Mask()))
                    return hit.point.y < pos.y + 0.3f && MHR_Water.DepthAt(hit.point) < 0.8f && MHR_Jump.CanAfford(c) && x <= MHR_Jump.Reach(c, dir, hit.point.y - pos.y);
            }
            return false;
        }

        // something to hop up onto just ahead (a step, a low wall), or a knee-high block
        static bool StepAhead(Vector3 pos, Vector3 dir)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + dir * 0.7f + Vector3.up * 2f, Vector3.down, out hit, 2.5f, Mask()) && hit.point.y > pos.y + 0.35f && hit.point.y < pos.y + 1.3f) return true;
            return Physics.Raycast(pos + Vector3.up * 0.4f, dir, 0.7f, Mask()) && !Physics.Raycast(pos + Vector3.up * 1.4f, dir, 1.0f, Mask());
        }

        // ---------- fire ----------
        // 1.4.88: don't stand or walk in a fire (user: twice he stood in the fire pit and caught fire).
        // 1.4.90: the flames themselves - EffectArea type Burning (what cooking stations check, what sets you alight).
        // 1.4.88-89 used type Fire, the wide area fire-shy creatures keep away from: it covered the whole kitchen, so
        // almost every spot by a chest counted as "in a fire" and cooking couldn't reach anything (1.4.89 log).
        public static bool InFire(Vector3 p)
        {
            try { return EffectArea.IsPointInsideArea(p + Vector3.up * 0.3f, EffectArea.Type.Burning, 0.3f) != null; }
            catch { return false; }
        }

        // the nearest way out: a direction 1.5 m away that isn't in a fire and has ground
        static bool FireExit(Vector3 pos, Vector3 prefer, out Vector3 dir)
        {
            dir = Vector3.zero;
            prefer.y = 0f;
            if (prefer.sqrMagnitude < 0.01f) prefer = Vector3.forward;
            prefer.Normalize();
            for (int i = 0; i < 8; i++)
            {
                float ang = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 45f;
                Vector3 d = Quaternion.Euler(0f, ang, 0f) * prefer;
                if (!InFire(pos + d * 1.5f) && !DropAhead(pos, d, 1.0f, 1.2f)) { dir = d; return true; }
            }
            return false;
        }

        static readonly Dictionary<int, float> s_fireLog = new Dictionary<int, float>();

        // called before anything else steers the companion: standing in a fire -> step out of it
        public static bool EscapeFire(CompanionAI ai)
        {
            if (ai == null) return false;
            Character c = ai.GetComponent<Character>();
            if (c == null || c.IsAttached() || c.IsDead() || !c.IsOnGround()) return false;   // not mid-jump over one
            Vector3 pos = ai.transform.position;
            if (!InFire(pos)) return false;
            Vector3 dir;
            if (!FireExit(pos, ai.transform.forward, out dir)) return false;
            ai.MoveTowards(dir, false);
            int id = ai.gameObject.GetInstanceID();
            float last;
            if (!s_fireLog.TryGetValue(id, out last) || Time.time - last > 10f)
            {
                s_fireLog[id] = Time.time;
                Dbg("[Move] standing in a fire at " + Fmt(pos) + " - stepping out");
            }
            return true;
        }

        // 1.4.82: true when the ground a step ahead is missing or more than maxDrop below (a ledge, a pit)
        public static bool DropAhead(Vector3 pos, Vector3 dir, float dist, float maxDrop)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return false;
            Vector3 p = pos + dir.normalized * dist;
            RaycastHit hit;
            if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 1.5f + maxDrop, Mask())) return true;
            return MHR_Water.DepthAt(hit.point) >= 1.0f;
        }

        static void Hop(CompanionAI ai, Character c, S s, Vector3 pos, Vector3 dir, float now, string tag, string why)
        {
            if (c.IsSwimming() || !c.IsOnGround()) { Sidestep(ai, c, s, pos, dir, now, tag, why, true); return; }
            if (DropAhead(pos, dir, 1.5f, 1.2f)) { Sidestep(ai, c, s, pos, dir, now, tag, why, true); return; }
            ai.transform.rotation = Quaternion.LookRotation(dir);
            c.Jump(false);
            Rigidbody rb = ai.GetComponent<Rigidbody>();
            if (rb != null) { Vector3 v = rb.velocity; v.x = dir.x * 4f; v.z = dir.z * 4f; rb.velocity = v; }
            s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.7f;
            Dbg("[Move|" + tag + "] stuck (" + why + ") at " + Fmt(pos) + " - jumping toward " + Fmt(pos + dir * 2f));
        }

        static bool Clear(Vector3 pos, Vector3 dir) { return Clear(pos, dir, 1.5f); }
        static bool Clear(Vector3 pos, Vector3 dir, float dist)
        {
            Vector3 from = pos + Vector3.up * 0.5f;
            if (Physics.Raycast(from, dir, dist, Mask())) return false;
            // something to stand on and no drop into deep water
            Vector3 p = pos + dir * dist;
            RaycastHit hit;
            if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 4f, Mask())) return false;
            if (hit.point.y < pos.y - 1.2f) return false;   // 1.4.82: not over a ledge
            return MHR_Water.DepthAt(hit.point) < 1.0f;
        }

        // 1.4.75: a reachable spot 1.5-2.5 m away that a route to the goal starts from (full routes first, then the shortest).
        // Further than the 1 m that counts as reaching a detour, so the companion actually steps there.
        static bool FindRouteStart(Vector3 pos, Vector3 goal, out Vector3 start)
        {
            start = Vector3.zero;
            float best = float.MaxValue; bool bestFull = false;
            for (int ring = 1; ring <= 2; ring++)
            {
                float r = ring == 1 ? 1.5f : 2.5f;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                    if (!Clear(pos, dir, r)) continue;
                    RaycastHit hit;
                    Vector3 p = pos + dir * r;
                    if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 4f, Mask())) continue;
                    p = hit.point;
                    s_tmp.Clear();
                    if (!GetPath(p, goal, s_tmp, MHR_CompanionNavMesh.AgentOrHumanoid, false)) continue;
                    Vector3 end = s_tmp[s_tmp.Count - 1];
                    bool full = Flat(end, goal) <= 1.5f && Mathf.Abs(end.y - goal.y) <= 2.2f;
                    float len = r; Vector3 prev = p;
                    for (int i = 0; i < s_tmp.Count; i++) { len += Vector3.Distance(prev, s_tmp[i]); prev = s_tmp[i]; }
                    if (!full) len += Vector3.Distance(prev, goal);
                    if ((full && !bestFull) || (full == bestFull && len < best)) { best = len; bestFull = full; start = p; }
                }
                if (bestFull) break;   // a full route from the inner ring: no need to look further out
            }
            return best < float.MaxValue;
        }

        static bool FindDetour(Vector3 pos, Vector3 goal, Vector3 dir, out Vector3 det)
        {
            det = Vector3.zero;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            for (int i = 0; i < 4; i++)
            {
                float off = (i < 2 ? 4f : 7f) * (i % 2 == 0 ? 1f : -1f);
                Vector3 p = pos + side * off - dir * 1.5f;
                s_tmp.Clear();
                if (!GetPath(pos, p, s_tmp, MHR_CompanionNavMesh.AgentOrHumanoid, true)) continue;
                det = s_tmp[s_tmp.Count - 1];
                if (Flat(det, pos) < 2f) continue;
                return true;
            }
            return false;
        }

        // ---------- water ----------
        static bool TryWaterJump(CompanionAI ai, Character c, S s, Vector3 pos, Vector3 dir, float now, string tag)
        {
            float lim = MHR_Water.SwimLimit(c);
            // 1.4.96: shallow water too (user: a player jumps a small river or pond rather than run through it)
            float wade = ModConfig.JumpWaterDepth != null ? ModConfig.JumpWaterDepth.Value : 0.3f;
            float maxGap = ModConfig.JumpWaterWidth != null ? ModConfig.JumpWaterWidth.Value : 3f;
            float waterAt = -1f, landAt = -1f; bool swim = false;
            for (float d = 0.5f; d <= 6.0f; d += 0.5f)
            {
                Vector3 p = pos + dir * d;
                float depth = MHR_Water.DepthAt(p);
                if (waterAt < 0f) { if (depth > wade) waterAt = d; else if (d > 1.5f) return false; }
                else if (depth < Mathf.Min(wade, 0.3f)) { landAt = d; break; }
                if (waterAt >= 0f && depth > lim) swim = true;
            }
            if (waterAt < 0f) return false;
            float landY = landAt > 0f ? MHR_Water.GroundAt(pos + dir * landAt) : pos.y;
            if (landAt > 0f && landAt - waterAt <= maxGap && landY < pos.y + 1.0f && MHR_Jump.CanAfford(c) && landAt <= MHR_Jump.Reach(c, dir, landY - pos.y))
            {
                // a stream or puddle we can clear: jump it and stay dry
                Leap(ai, c, dir, 6.5f);
                s.NextJump = now + 0.8f; s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.6f;
                Dbg("[Move|" + tag + "] jumping the " + (swim ? "stream" : "shallow water") + " at " + Fmt(pos + dir * waterAt));
                return true;
            }
            // 1.4.99: close enough is good enough (user: "if it can make it most of the way across the water with a jump
            // into shallow water on the other side its worth the jump. They can always jump again"). Landing somewhere it
            // can stand counts, as long as it keeps some stamina back for the rest of the way.
            if (waterAt <= 1.5f && MHR_Jump.HasSpare(c, 0.25f))
            {
                float reach = MHR_Jump.Reach(c, dir, 0f);
                if (reach >= 2f)
                {
                    Vector3 l = pos + dir * Mathf.Min(reach, 6f);
                    float ly = MHR_Water.GroundAt(l);
                    if (!MHR_Water.WouldSwim(c, l) && ly < pos.y + 0.5f && ly > pos.y - (ModConfig.MaxDrop != null ? ModConfig.MaxDrop.Value : 3f))
                    {
                        Leap(ai, c, dir, 6.5f);
                        s.NextJump = now + 0.8f; s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.6f;
                        Dbg("[Move|" + tag + "] jumping as far across the water as it can (lands at " + Fmt(l) + ")");
                        return true;
                    }
                }
            }
            if (!swim) { s.NextWaterPeek = now + 1.0f; return false; }   // too wide to jump but shallow: wade through
            if (s.TooTired && waterAt <= 1.5f)
            {
                s.WaitStamina = true; s.WaitStart = now; Halt(ai, c, s);
                Dbg("[Move|" + tag + "] too tired to swim across - waiting on the bank");
                return true;
            }
            if (landAt < 0f && now >= s.NextDive && waterAt <= 1.0f && !s.WaitStamina && tag != "Loot")
            {
                // wide water we're about to swim: running leap in for a head start
                Leap(ai, c, dir, 5f);
                s.NextJump = now + 1.5f; s.NextDive = now + 20f; s.StepDir = dir; s.StepRun = true; s.StepUntil = now + 0.6f;
                Dbg("[Move|" + tag + "] diving in at " + Fmt(pos + dir * waterAt));
                return true;
            }
            return false;
        }

        // 1.4.98: its own run speed, Jump skill and stamina (MHR_Jump)
        static void Leap(CompanionAI ai, Character c, Vector3 dir, float speed) { MHR_Jump.Leap(ai, c, dir); }

        static bool FindShore(Character c, Vector3 pos, Vector3 goal, out Vector3 best)
        {
            best = Vector3.zero;
            float bestScore = float.MaxValue; bool found = false;
            float lim = MHR_Water.SwimLimit(c);
            for (int r = 0; r < 4; r++)
                for (int k = 0; k < 16; k++)
                {
                    float rad = r == 0 ? 3f : r == 1 ? 5f : r == 2 ? 8f : 12f;
                    float ang = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 p = pos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * rad;
                    float w = MHR_Water.WaterLevel(p);
                    if (w < -1000f) w = pos.y;
                    float g = MHR_Water.GroundAt(new Vector3(p.x, w + 0.5f, p.z));
                    if (w - g > lim - 0.3f) continue;     // still deep there
                    if (g - w > 1.0f) continue;           // a wall, not a bank you can climb out on
                    float score = rad + Vector3.Distance(new Vector3(p.x, g, p.z), goal) * 0.6f;
                    if (score < bestScore) { bestScore = score; best = new Vector3(p.x, g, p.z); found = true; }
                }
            return found;
        }

        static float MaxStamina(Component c)
        {
            CompanionStamina st = c.GetComponent<CompanionStamina>();
            return st != null ? st.MaxStamina : 0f;
        }
        static float Stamina(Component c)
        {
            CompanionStamina st = c.GetComponent<CompanionStamina>();
            return st != null && st.MaxStamina > 0f ? st.Stamina : 999f;
        }

        // ---------- doors ----------
        static bool DoorClosed(Door d)
        {
            if (d == null) return false;
            ZNetView nv = d.GetComponent<ZNetView>();
            return nv != null && nv.IsValid() && nv.GetZDO().GetInt("state", 0) == 0;
        }
        static bool Usable(Door d)
        {
            try { return d != null && DoorHandler.IsValidClosedDoor(d); } catch { return false; }
        }

        static Door DoorNear(Vector3 pos, float range)
        {
            int n = Physics.OverlapSphereNonAlloc(pos + Vector3.up, range, s_cols, LayerMask.GetMask("piece", "piece_nonsolid", "Default"));
            Door best = null; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Door d = s_cols[i] != null ? s_cols[i].GetComponentInParent<Door>() : null;
                if (!Usable(d)) continue;
                float dd = MHR_Nav.DoorDist(pos, d.transform.position);
                if (dd < bd) { bd = dd; best = d; }
            }
            return bd <= range + 0.5f ? best : null;
        }

        // closed door between us and the next corner, within reach
        static Door DoorAhead(Vector3 pos, Vector3 target)
        {
            Vector3 from = pos + Vector3.up * 1.0f;
            Vector3 to = new Vector3(target.x, Mathf.Max(target.y, pos.y) + 1.0f, target.z);
            Vector3 d = to - from; float len = Mathf.Min(d.magnitude, 2.5f);
            if (len < 0.2f) return null;
            RaycastHit hit;
            if (!Physics.Raycast(from, d.normalized, out hit, len, LayerMask.GetMask("piece", "Default"))) return null;
            Door door = hit.collider != null ? hit.collider.GetComponentInParent<Door>() : null;
            return Usable(door) ? door : null;
        }

        // the closed door that opens the shortest way to the goal, and where to stand to open it
        // 1.4.84: is there a closed door that, once opened, gets us from pos to goal?
        public static bool ReachableViaDoor(Vector3 pos, Vector3 goal)
        {
            return CanGetTo(pos, goal);
        }

        // 1.4.84: through up to three closed doors in a row (closed doors block routes): a breadth-first search over
        // door sides - from here to the near side of a door, from its far side to the near side of the next, and from
        // the last far side a route has to actually reach the goal (1.4.79: otherwise the door isn't the answer and he
        // goes round in circles). Returns the first door of the shortest chain; once through it, the next plan finds
        // the next one. Capped at 120 route queries.
        class DoorNode { public int Door, Side, Depth, First; public Vector3 FirstSpot; public float Len; }
        static readonly List<Door> s_doors = new List<Door>();
        static Door RouteDoor(Vector3 pos, Vector3 goal, out Vector3 spot, Door skip)
        {
            spot = Vector3.zero;
            s_doors.Clear();
            s_pieces.Clear();
            Piece.GetAllPiecesInRadius(goal, 35f, s_pieces);
            if (Flat(pos, goal) > 20f) Piece.GetAllPiecesInRadius(pos, 20f, s_pieces);
            for (int i = 0; i < s_pieces.Count; i++)
            {
                Piece p = s_pieces[i];
                Door d = p != null ? p.GetComponentInChildren<Door>() : null;
                if (!Usable(d) || d == skip || s_doors.Contains(d)) continue;
                s_doors.Add(d);
            }
            if (s_doors.Count == 0) return null;
            Vector3 mid = (pos + goal) * 0.5f;
            s_doors.Sort((a, b) => Vector3.Distance(a.transform.position, mid).CompareTo(Vector3.Distance(b.transform.position, mid)));
            if (s_doors.Count > 8) s_doors.RemoveRange(8, s_doors.Count - 8);

            int budget = 120;
            bool[] seen = new bool[s_doors.Count * 2];
            Queue<DoorNode> queue = new Queue<DoorNode>();
            for (int i = 0; i < s_doors.Count; i++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 end; float len;
                    if (budget-- <= 0) break;
                    if (!ToDoor(pos, s_doors[i], side, out end, out len)) continue;
                    seen[i * 2 + (side > 0 ? 1 : 0)] = true;
                    queue.Enqueue(new DoorNode { Door = i, Side = side, Depth = 1, First = i, FirstSpot = end, Len = len });
                }
            Door best = null; float bestScore = float.MaxValue; int bestDepth = 99;
            while (queue.Count > 0 && budget > 0)
            {
                DoorNode n = queue.Dequeue();
                if (n.Depth > bestDepth) break;
                Door d = s_doors[n.Door];
                Vector3 other = d.transform.position - d.transform.forward * (1.4f * n.Side);
                s_tmp2.Clear(); budget--;
                if (GetPath(other, goal, s_tmp2, Pathfinding.AgentType.Humanoid, false))
                {
                    Vector3 far = s_tmp2[s_tmp2.Count - 1];
                    if (Flat(far, goal) <= 2.0f && Mathf.Abs(far.y - goal.y) <= 2.2f)
                    {
                        float score = n.Len + Vector3.Distance(d.transform.position, goal);
                        if (score < bestScore) { bestScore = score; best = s_doors[n.First]; spot = n.FirstSpot; bestDepth = n.Depth; }
                        continue;
                    }
                }
                if (n.Depth >= 3) continue;
                for (int j = 0; j < s_doors.Count && budget > 0; j++)
                {
                    if (j == n.Door) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        int key = j * 2 + (side > 0 ? 1 : 0);
                        if (seen[key] || budget-- <= 0) continue;
                        Vector3 end; float len;
                        if (!ToDoor(other, s_doors[j], side, out end, out len)) continue;
                        seen[key] = true;
                        queue.Enqueue(new DoorNode { Door = j, Side = side, Depth = n.Depth + 1, First = n.First, FirstSpot = n.FirstSpot, Len = n.Len + len });
                    }
                }
            }
            return best;
        }

        // a full route from 'from' to one side of the door, ending by the door
        static bool ToDoor(Vector3 from, Door d, int side, out Vector3 end, out float len)
        {
            end = Vector3.zero; len = 0f;
            Vector3 dp = d.transform.position;
            Vector3 sp = dp + d.transform.forward * (1.4f * side);
            s_tmp.Clear();
            if (!GetPath(from, sp, s_tmp, Pathfinding.AgentType.Humanoid, true)) return false;
            end = s_tmp[s_tmp.Count - 1];
            if (MHR_Nav.DoorDist(end, dp) > 2.2f) return false;
            Vector3 prev = from;
            for (int k = 0; k < s_tmp.Count; k++) { len += Vector3.Distance(prev, s_tmp[k]); prev = s_tmp[k]; }
            return true;
        }

        // 1.4.84: no route at all, but a spot a route does reach from which the rest can be hopped (the bottom of stairs
        // the routes don't use - 1.4.83 test: the farm's chests are up the stairs in the house). Rings round the goal.
        public static bool FindHopBridge(Vector3 pos, Vector3 goal, out Vector3 bridge)
        {
            bridge = Vector3.zero;
            float best = float.MaxValue; int queries = 0;
            float[] radii = { 3f, 6f, 10f, 15f, 20f };
            for (int r = 0; r < radii.Length; r++)
            {
                for (int k = 0; k < 12; k++)
                {
                    Vector3 dir = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward;
                    Vector3 p = goal + dir * radii[r];
                    RaycastHit hit;
                    if (!Physics.Raycast(new Vector3(p.x, goal.y + 1.5f, p.z), Vector3.down, out hit, 12f, Mask())) continue;
                    if (MHR_Water.DepthAt(hit.point) > 0.8f) continue;
                    Vector3 b = hit.point;
                    int hops;
                    if (!HopLine(b, goal, out hops)) continue;
                    if (queries++ >= 24) break;
                    s_tmp.Clear();
                    if (!GetPath(pos, b, s_tmp, MHR_CompanionNavMesh.AgentOrHumanoid, true)) continue;
                    Vector3 end = s_tmp[s_tmp.Count - 1];
                    if (Flat(end, b) > 1.0f || Mathf.Abs(end.y - b.y) > 1.2f) continue;
                    float len = 0f; Vector3 prev = pos;
                    for (int i = 0; i < s_tmp.Count; i++) { len += Vector3.Distance(prev, s_tmp[i]); prev = s_tmp[i]; }
                    len += radii[r];
                    if (len < best) { best = len; bridge = b; }
                }
                if (best < float.MaxValue) break;   // nearest ring that works
            }
            return best < float.MaxValue;
        }

        // 1.4.84: can he get from pos to goal: a route, a route plus a hop, through closed doors, or straight hopping
        public static bool CanGetTo(Vector3 pos, Vector3 goal)
        {
            try
            {
                Vector3 v; int hops;
                if (Flat(pos, goal) < 25f && HopLine(pos, goal, out hops)) return true;
                if (FindHopBridge(pos, goal, out v)) return true;
                return RouteDoor(pos, goal, out v, null) != null;
            }
            catch { return false; }
        }

        static void OpenDoor(CompanionAI ai, Character c, Door d, string tag)
        {
            if (d == null || !DoorClosed(d)) return;
            Vector3 to = d.transform.position - ai.transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.01f) ai.transform.rotation = Quaternion.LookRotation(to.normalized);
            Humanoid h = c as Humanoid;
            bool ok = false;
            try { ok = h != null && d.Interact(h, false, false); } catch { ok = false; }
            if (!ok) { Dbg("[Move|" + tag + "] couldn't open " + d.m_name); return; }
            int id = ai.gameObject.GetInstanceID();
            List<Opened> list;
            if (!s_opened.TryGetValue(id, out list)) { list = new List<Opened>(); s_opened[id] = list; }
            list.Add(new Opened { Door = d, At = Time.time });
            Dbg("[Move|" + tag + "] opened " + d.m_name + " at " + Fmt(d.transform.position));
        }

        // ---------- per-frame housekeeping (called from MHR_ToolKeeper.Tick for every companion we own) ----------
        public static void Tick(Component cs, CompanionAI ai)
        {
            if (cs == null || ai == null) return;
            try
            {
                S s = Peek(ai);
                if (s != null && s.Walking && Time.time - s.LastGo > 0.5f)
                {
                    Character c = ai.GetComponent<Character>();
                    if (c != null) c.SetWalk(false);
                    s.Walking = false;
                }
                List<Opened> list;
                if (!s_opened.TryGetValue(ai.gameObject.GetInstanceID(), out list) || list.Count == 0) return;
                Vector3 pos = ai.transform.position;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Opened o = list[i];
                    if (o.Door == null || DoorClosed(o.Door) || Time.time - o.At > 60f) { list.RemoveAt(i); continue; }
                    if (Time.time - o.At < 1.5f) continue;
                    Vector3 dp = o.Door.transform.position;
                    if (Flat(pos, dp) < 3.5f && Mathf.Abs(pos.y - dp.y) < 3f) continue;
                    // 1.4.75: still stuck near it (working through the stuck ladder or a detour) - leave it open
                    if (s != null && Time.time - s.LastGo < 0.5f && (s.Ladder > 0 || s.HasDetour) && Flat(pos, dp) < 8f) continue;
                    if (Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, dp) < 2.5f) continue;
                    if (AnyCharacterIn(dp, 1.2f)) continue;
                    Humanoid h = ai.GetComponent<Humanoid>();
                    try { if (h != null) o.Door.Interact(h, false, false); } catch { }
                    Dbg("[Move] closed " + o.Door.m_name + " behind us");
                    list.RemoveAt(i);
                }
            }
            catch (Exception e)
            {
                if (CompanionsPlugin.Log != null && Time.time >= s_nextErr) { s_nextErr = Time.time + 30f; CompanionsPlugin.Log.LogWarning("[Move] " + e.Message); }
            }
        }
        static bool AnyCharacterIn(Vector3 p, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(p, r, s_cols, LayerMask.GetMask("character", "character_net", "character_ghost"));
            for (int i = 0; i < n; i++) if (s_cols[i] != null && s_cols[i].GetComponentInParent<Character>() != null) return true;
            return false;
        }

        // ---------- follow: optional catch-up when badly stuck and out of sight ----------
        class F { public Vector3 Anchor; public float AnchorTime; }
        static readonly Dictionary<int, F> s_follow = new Dictionary<int, F>();
        public static void FollowWatch(CompanionAI ai, GameObject target)
        {
            try
            {
                if (ai == null || target == null || ModConfig.FollowStuckTeleport == null || !ModConfig.FollowStuckTeleport.Value) return;
                float now = Time.time;
                int id = ai.gameObject.GetInstanceID();
                F f;
                if (!s_follow.TryGetValue(id, out f)) { f = new F { Anchor = ai.transform.position, AnchorTime = now }; s_follow[id] = f; }
                Vector3 pos = ai.transform.position, tp = target.transform.position;
                if (Vector3.Distance(pos, tp) < 10f || Vector3.Distance(pos, f.Anchor) > 1.5f || ai.IsInCombat) { f.Anchor = pos; f.AnchorTime = now; return; }
                if (now - f.AnchorTime < 15f) return;
                f.Anchor = pos; f.AnchorTime = now;
                if (!Physics.Linecast(pos + Vector3.up * 1.6f, tp + Vector3.up * 1.6f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain"))) return;   // you can see each other
                if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogInfo("[Move|Follow] stuck for 15s out of sight at " + Fmt(pos) + " - catching up");
                ai.TeleportToFollowTarget();
            }
            catch { }
        }

        // 1.4.83: eye to eye, nothing solid in between
        public static bool CanSee(Vector3 a, Vector3 b)
        {
            return !Physics.Linecast(a + Vector3.up * 1.6f, b + Vector3.up * 1.6f, LayerMask.GetMask("Default", "static_solid", "piece", "terrain"));
        }

        // ---------- log ----------
        static void Log(S s, Vector3 pos, float remain, float now, Character c)
        {
            if (now < s.NextLog || CompanionsPlugin.Log == null) return;
            s.NextLog = now + 3f;
            float still = now - s.AnchorTime, noCloser = now - s.ProgressTime;
            string route = !s.PathOk ? "none" : (s.Partial ? "partial" : "full") + " (" + s.Path.Count + " corners)";
            CompanionsPlugin.Log.LogDebug("[Move|" + s.Tag + "] at " + Fmt(pos) + " -> " + Fmt(s.Goal) + " " + remain.ToString("F1") + "m left, route=" + route +
                (s.RouteDoor != null ? ", via door" : "") + (s.HasDetour ? ", detour" : "") + (c.IsSwimming() ? ", swimming" : "") +
                (noCloser > 3f ? ", no closer for " + noCloser.ToString("F0") + "s" : "") + (still > 2f ? ", standing still " + still.ToString("F0") + "s" : "") +
                (s.Ladder > 0 ? ", unstuck step " + s.Ladder : ""));
        }
    }

    // ---- 1.4.74: water depth measured against the companion, not "is there water" ----
    public static class MHR_Water
    {
        static WaterVolume s_wv;
        static int s_mask = -1;
        static int Mask()
        {
            if (s_mask == -1) s_mask = LayerMask.GetMask("Default", "static_solid", "piece", "terrain", "vehicle");
            return s_mask;
        }
        public static float WaterLevel(Vector3 p)
        {
            try { return Floating.GetWaterLevel(p, ref s_wv); } catch { return -10000f; }
        }
        public static float GroundAt(Vector3 p)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out hit, 60f, Mask())) return hit.point.y;
            float y;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out y)) return y;
            return p.y;
        }
        public static float DepthAt(Vector3 p)
        {
            float g = GroundAt(p);
            float w = WaterLevel(new Vector3(p.x, g + 0.3f, p.z));
            if (w < -1000f) w = WaterLevel(p);
            if (w < -1000f) return 0f;
            return Mathf.Max(0f, w - g);
        }
        // vanilla: a character swims once the water is deeper than swimDepth - 0.4 (and only then gets Wet)
        public static float SwimLimit(Character c) { return Mathf.Max(0.3f, (c != null ? c.m_swimDepth : 2f) - 0.4f); }
        public static bool WouldSwim(Character c, Vector3 p) { return DepthAt(p) > SwimLimit(c); }

        public static float SwimStamina(Character c, float meters)
        {
            float speed = c != null && c.m_swimSpeed > 0.1f ? c.m_swimSpeed : 2f;
            float drain = ModConfig.StaminaSwimDrain != null ? ModConfig.StaminaSwimDrain.Value : 10f;
            return meters / speed * drain;
        }

        // meters of swimming along pos -> path corners (sampled every meter, up to maxLen)
        public static float SwimMeters(Character c, Vector3 pos, List<Vector3> path, float maxLen)
        {
            float lim = SwimLimit(c), swim = 0f, walked = 0f;
            Vector3 prev = pos;
            for (int i = 0; i < path.Count && walked < maxLen; i++)
            {
                Vector3 seg = path[i] - prev; float len = new Vector2(seg.x, seg.z).magnitude;
                for (float d = 0.5f; d < len && walked < maxLen; d += 1f, walked += 1f)
                    if (DepthAt(prev + seg * (d / len)) > lim) swim += 1f;
                prev = path[i];
            }
            return swim;
        }
        public static float DistToWater(Character c, Vector3 pos, List<Vector3> path)
        {
            float lim = SwimLimit(c), walked = 0f;
            Vector3 prev = pos;
            for (int i = 0; i < path.Count && walked < 30f; i++)
            {
                Vector3 seg = path[i] - prev; float len = new Vector2(seg.x, seg.z).magnitude;
                for (float d = 0.5f; d < len && walked < 30f; d += 1f, walked += 1f)
                    if (DepthAt(prev + seg * (d / len)) > lim) return walked;
                prev = path[i];
            }
            return 999f;
        }
        public static bool SwimOnLine(Character c, Vector3 a, Vector3 b, float maxLen)
        {
            float lim = SwimLimit(c);
            Vector3 d = b - a; float len = Mathf.Min(new Vector2(d.x, d.z).magnitude, maxLen);
            if (len < 0.5f) return DepthAt(b) > lim;
            Vector3 n = new Vector3(d.x, 0f, d.z).normalized;
            for (float t = 0.5f; t <= len; t += 1.5f)
                if (DepthAt(a + n * t + Vector3.up * (d.y * t / len)) > lim) return true;
            return DepthAt(b) > lim;
        }

        // replaces "below half stamina -> avoid ALL water": only steer around water he'd actually have to swim
        static readonly Dictionary<int, KeyValuePair<float, bool>> s_agent = new Dictionary<int, KeyValuePair<float, bool>>();
        public static void ApplyAgent(CompanionAI ai, bool lowStamina)
        {
            bool avoid = false;
            try
            {
                Character c = ai.GetComponent<Character>();
                GameObject f = ai.GetFollowTarget();
                if (lowStamina && c != null && !c.IsSwimming() && f != null)
                {
                    int id = ai.gameObject.GetInstanceID();
                    KeyValuePair<float, bool> cached;
                    if (s_agent.TryGetValue(id, out cached) && Time.time - cached.Key < 1f) avoid = cached.Value;
                    else
                    {
                        avoid = SwimOnLine(c, ai.transform.position, f.transform.position, 40f);
                        s_agent[id] = new KeyValuePair<float, bool>(Time.time, avoid);
                    }
                }
            }
            catch { avoid = false; }
            ai.m_avoidWater = avoid;
            ai.m_pathAgentType = avoid ? Pathfinding.AgentType.HumanoidAvoidWater : Pathfinding.AgentType.Humanoid;
        }
    }

    // ---- 1.4.74: what to fight ----
    public static class MHR_Threat
    {
        static readonly Dictionary<int, float> s_ignore = new Dictionary<int, float>();
        static float s_nextLog;

        // animals that never attack (vanilla deer and the like, modded fish, dolphins...) and creatures we gave up chasing
        public static bool IsPassive(Character c)
        {
            if (c == null) return false;
            try
            {
                string n = c.gameObject.name;
                if (n.StartsWith("Deer") || n.StartsWith("Hare") || n.StartsWith("Chicken")) return true;
                float until;
                BaseAI ai = c.GetBaseAI();
                if (s_ignore.TryGetValue(c.GetInstanceID(), out until))
                {
                    bool coming = ai != null && ai.GetTargetCreature() != null;
                    if (Time.time < until && !coming && MHR_Water.DepthAt(c.transform.position) > 1.0f) return true;
                    s_ignore.Remove(c.GetInstanceID());
                }
                if (ai is AnimalAI) return true;
                // 1.4.78: creatures that only fight back (passive-aggressive), sea creatures that keep off land, and ones
                // that flee unless alerted are left alone while they aren't after anyone. Modded crabs and dolphins
                // (RtDOcean) use MonsterAI, so the AnimalAI check above missed them.
                MonsterAI mai = ai as MonsterAI;
                if (ai != null && ai.GetTargetCreature() == null && !ai.IsAlerted() &&
                    (ai.m_passiveAggresive || (mai != null && (mai.m_avoidLand || mai.m_fleeIfNotAlerted)))) return true;
            }
            catch { }
            return false;
        }

        // 1.4.79: is it going after a player or a companion right now?
        public static bool IsAfterUs(Character c)
        {
            if (c == null) return false;
            try
            {
                BaseAI ai = c.GetBaseAI();
                if (ai == null) return true;   // not an AI creature: leave it to the normal rules
                Character t = ai.GetTargetCreature();
                if (t == null) return false;
                return t.IsPlayer() || t.GetComponent<CompanionSetup>() != null;
            }
            catch { return true; }
        }

        // don't chase into water he'd have to swim in; if it's attacking, a bow or crossbow with ammo still works
        public static bool AvoidChase(Character self, Character target)
        {
            if (self == null || target == null) return false;
            try
            {
                if (self.IsSwimming()) return false;
                if (!MHR_Water.WouldSwim(self, target.transform.position)) return false;
                // something swimming out there minding its own business: leave it. If it's coming for us, shoot it if we can.
                BaseAI tai = target.GetBaseAI();
                bool hostile = tai != null && (tai.IsAlerted() || tai.GetTargetCreature() != null);
                Humanoid h = self as Humanoid;
                if (hostile && h != null && MHR_Crossbow.HasReadyRanged(h)) return false;
                if (hostile && Vector3.Distance(self.transform.position, target.transform.position) < 3f) return false;
                if (s_ignore.Count > 200) s_ignore.Clear();
                s_ignore[target.GetInstanceID()] = Time.time + 20f;
                CompanionCombatAI cc = self.GetComponent<CompanionCombatAI>();
                if (cc != null) cc.Disengage();
                if (CompanionsPlugin.Log != null && Time.time >= s_nextLog)
                {
                    s_nextLog = Time.time + 5f;
                    CompanionsPlugin.Log.LogDebug("[Combat] " + target.m_name + " is in deep water - leaving it");
                }
                return true;
            }
            catch { return false; }
        }
    }

    // ---- 1.4.74: crossbows. Vanilla only lets the player load one (Humanoid.IsWeaponLoaded is always false), so a
    // companion with a crossbow could never fire. Companions now load it like a player: reload animation, reload time
    // (faster with skill), stamina. Ranged weapons only count as ready with matching ammo in the pack. ----
    public static class MHR_Crossbow
    {
        class S { public ItemDrop.ItemData Loaded, Reloading; public float Until; public string Anim; }
        static readonly Dictionary<int, S> s_s = new Dictionary<int, S>();
        static readonly Dictionary<int, bool> s_isComp = new Dictionary<int, bool>();

        public static bool IsCompanion(Humanoid h)
        {
            if (h == null || h is Player) return false;
            int id = h.GetInstanceID();
            bool v;
            if (!s_isComp.TryGetValue(id, out v)) { v = h.GetComponent<CompanionSetup>() != null; if (s_isComp.Count > 2000) s_isComp.Clear(); s_isComp[id] = v; }
            return v;
        }
        static S Get(Humanoid h)
        {
            S s;
            if (!s_s.TryGetValue(h.GetInstanceID(), out s)) { s = new S(); s_s[h.GetInstanceID()] = s; }
            return s;
        }

        static bool IsAmmo(ItemDrop.ItemData it)
        {
            return it != null && it.m_shared != null && (it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo || it.m_shared.m_itemType == ItemDrop.ItemData.ItemType.AmmoNonEquipable);
        }
        public static bool NeedsAmmo(ItemDrop.ItemData it)
        {
            return it != null && it.m_shared != null && !IsAmmo(it) && !string.IsNullOrEmpty(it.m_shared.m_ammoType);
        }
        public static bool HasAmmo(Humanoid h, ItemDrop.ItemData weapon)
        {
            if (h == null || weapon == null || h.GetInventory() == null) return false;
            string type = weapon.m_shared.m_ammoType;
            List<ItemDrop.ItemData> items = h.GetInventory().GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (IsAmmo(it) && it.m_shared.m_ammoType == type && it.m_stack > 0) return true;
            }
            return false;
        }
        // replaces WeaponLogic.Ready in FindReadyBow / BalancedPrefix
        public static bool ReadyFor(ItemDrop.ItemData it, Humanoid h)
        {
            if (it == null || it.m_shared == null) return false;
            if (it.m_shared.m_useDurability && it.m_durability <= 0f) return false;
            if (NeedsAmmo(it) && h != null && !HasAmmo(h, it)) return false;
            return true;
        }
        public static bool HasReadyRanged(Humanoid h)
        {
            if (h == null || h.GetInventory() == null) return false;
            List<ItemDrop.ItemData> items = h.GetInventory().GetAllItems();
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData it = items[i];
                if (NeedsAmmo(it) && ReadyFor(it, h)) return true;
            }
            return false;
        }

        public static bool IsLoaded(Humanoid h)
        {
            S s;
            if (h == null || !s_s.TryGetValue(h.GetInstanceID(), out s)) return false;
            ItemDrop.ItemData w = h.GetCurrentWeapon();
            return w != null && s.Loaded == w;
        }
        public static void Unload(Humanoid h)
        {
            S s;
            if (h != null && s_s.TryGetValue(h.GetInstanceID(), out s)) s.Loaded = null;
        }

        public static void Tick(Humanoid h)
        {
            if (h == null) return;
            try
            {
                ItemDrop.ItemData w = h.GetCurrentWeapon();
                bool needs = w != null && w.m_shared != null && w.m_shared.m_attack != null && w.m_shared.m_attack.m_requiresReload && h.IsItemEquiped(w);
                S s;
                if (!needs)
                {
                    if (s_s.TryGetValue(h.GetInstanceID(), out s) && s.Reloading != null) Cancel(h, s);
                    return;
                }
                s = Get(h);
                if (s.Loaded == w) return;
                if (s.Reloading != null && s.Reloading != w) Cancel(h, s);
                ZSyncAnimation zanim = h.GetComponent<ZSyncAnimation>();
                if (s.Reloading == w)
                {
                    if (Time.time < s.Until) return;
                    if (zanim != null && !string.IsNullOrEmpty(s.Anim)) { zanim.SetBool(s.Anim, false); zanim.SetTrigger(s.Anim + "_done"); }
                    s.Loaded = w; s.Reloading = null;
                    if (CompanionsPlugin.Log != null) CompanionsPlugin.Log.LogDebug("[Ranged] " + w.m_shared.m_name + " loaded");
                    return;
                }
                if (h.InAttack() || !HasAmmo(h, w)) return;
                Attack atk = w.m_shared.m_attack;
                CompanionStamina st = h.GetComponent<CompanionStamina>();
                if (st != null && atk.m_reloadStaminaDrain > 0f && !st.UseStamina(atk.m_reloadStaminaDrain)) return;
                float skill = 0f;
                try { skill = h.GetSkillFactor(w.m_shared.m_skillType); } catch { skill = 0f; }
                float time = Mathf.Lerp(atk.m_reloadTime, atk.m_reloadTime * 0.5f, skill);
                s.Reloading = w; s.Until = Time.time + Mathf.Max(0.3f, time); s.Anim = atk.m_reloadAnimation;
                if (zanim != null && !string.IsNullOrEmpty(s.Anim)) zanim.SetBool(s.Anim, true);
            }
            catch { }
        }
        static void Cancel(Humanoid h, S s)
        {
            ZSyncAnimation zanim = h.GetComponent<ZSyncAnimation>();
            if (zanim != null && !string.IsNullOrEmpty(s.Anim)) zanim.SetBool(s.Anim, false);
            s.Reloading = null;
        }
    }

    [HarmonyPatch(typeof(Humanoid), "IsWeaponLoaded")]
    public static class MHR_WeaponLoaded_Patch
    {
        static void Postfix(Humanoid __instance, ref bool __result)
        {
            if (!__result && MHR_Crossbow.IsCompanion(__instance)) __result = MHR_Crossbow.IsLoaded(__instance);
        }
    }

    [HarmonyPatch(typeof(Humanoid), "ResetLoadedWeapon")]
    public static class MHR_ResetLoaded_Patch
    {
        static void Postfix(Humanoid __instance)
        {
            if (MHR_Crossbow.IsCompanion(__instance)) MHR_Crossbow.Unload(__instance);
        }
    }
}
