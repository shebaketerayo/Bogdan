using System;
using System.Collections.Generic;

namespace DesktopCat
{
    internal sealed class BrainHop
    {
        public string FromKey = "", ToKey = "";
        public bool FromWindow, ToWindow, Walk;
        public double TakeX, LandX, TakeRel, LandRel;
    }

    internal sealed class BrainBans
    {
        const int Cap = 64;
        readonly List<string> banFrom = new List<string>(), banTo = new List<string>();
        readonly List<double> banUntil = new List<double>();
        readonly List<string> failFrom = new List<string>(), failTo = new List<string>();
        readonly List<double> failTime = new List<double>();
        // every target ban escalates the next one; strikes reset after 10 quiet minutes
        readonly List<string> strikeKey = new List<string>();
        readonly List<int> strikeCount = new List<int>();
        readonly List<double> strikeTime = new List<double>();

        public int Count { get { return banUntil.Count; } }

        public void BanEdge(string from, string to, double until)
        {
            if (banUntil.Count >= Cap) { banFrom.RemoveAt(0); banTo.RemoveAt(0); banUntil.RemoveAt(0); }
            banFrom.Add(from); banTo.Add(to); banUntil.Add(until);
        }

        public void BanTarget(string key, double until) { BanEdge(null, key, until); }

        public bool IsBanned(string from, string to, double now)
        {
            for (int i = 0; i < banUntil.Count; i++)
            {
                if (banUntil[i] < now) continue;
                if (!string.Equals(banTo[i], to, StringComparison.Ordinal)) continue;
                if (banFrom[i] == null || string.Equals(banFrom[i], from, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public bool TargetBanned(string key, double now)
        {
            for (int i = 0; i < banUntil.Count; i++)
                if (banUntil[i] >= now && banFrom[i] == null && string.Equals(banTo[i], key, StringComparison.Ordinal)) return true;
            return false;
        }

        public void RecordFailure(string from, string to, double now)
        {
            int same = 0, toSame = 0;
            for (int i = failTime.Count - 1; i >= 0; i--)
            {
                if (now - failTime[i] > 180) { failFrom.RemoveAt(i); failTo.RemoveAt(i); failTime.RemoveAt(i); continue; }
                if (!string.Equals(failTo[i], to, StringComparison.Ordinal)) continue;
                toSame++;
                if (now - failTime[i] <= 60 && string.Equals(failFrom[i], from, StringComparison.Ordinal)) same++;
            }
            if (failTime.Count >= Cap) { failFrom.RemoveAt(0); failTo.RemoveAt(0); failTime.RemoveAt(0); }
            failFrom.Add(from); failTo.Add(to); failTime.Add(now);
            if (same >= 1) BanEdge(from, to, now + 45);
            if (toSame >= 2 && !TargetBanned(to, now)) BanTargetEscalating(to, now, 60);
        }

        public void BanTargetEscalating(string key, double now, double baseSecs)
        {
            if (key == null) return;
            int k = -1;
            for (int i = 0; i < strikeKey.Count; i++)
                if (string.Equals(strikeKey[i], key, StringComparison.Ordinal)) { k = i; break; }
            if (k >= 0 && now - strikeTime[k] > 600) strikeCount[k] = 0;
            if (k < 0)
            {
                if (strikeKey.Count >= Cap) { strikeKey.RemoveAt(0); strikeCount.RemoveAt(0); strikeTime.RemoveAt(0); }
                strikeKey.Add(key); strikeCount.Add(0); strikeTime.Add(now);
                k = strikeKey.Count - 1;
            }
            int n = strikeCount[k] + 1;
            strikeCount[k] = n; strikeTime[k] = now;
            BanTarget(key, now + baseSecs * Math.Pow(2, Math.Min(4, n - 1)));
        }
    }

    internal sealed class BrainPath
    {
        public const double Inf = double.MaxValue;
        readonly BrainWorld w;
        public readonly BrainBans Bans = new BrainBans();
        double[] cost = new double[0], takeX = new double[0], landX = new double[0], arrX = new double[0];
        int[] parent = new int[0];
        bool[] walkE = new bool[0], done = new bool[0];
        int n;
        public int StartIndex = -1;
        int stampRebuild = -1;
        double stampTime = -100, stampX;
        bool stampFull;

        public BrainPath(BrainWorld world) { w = world; }

        void Ensure(int count)
        {
            if (cost.Length >= count) return;
            int c = Math.Max(count, cost.Length * 2 + 16);
            cost = new double[c]; takeX = new double[c]; landX = new double[c]; arrX = new double[c];
            parent = new int[c]; walkE = new bool[c]; done = new bool[c];
        }

        public bool Reachable(int i) { return i >= 0 && i < n && StartIndex >= 0 && cost[i] < Inf; }
        public double Cost(int i) { return Reachable(i) ? cost[i] : Inf; }

        public void ExploreAll(int start, double startX, double now)
        {
            if (stampFull && start == StartIndex && stampRebuild == w.Rebuilds && now - stampTime < 1.0
                && Math.Abs(startX - stampX) < 40 * w.Scale) return;
            Explore(start, startX, -1, now);
        }

        public void Explore(int start, double startX, int goal, double now)
        {
            n = w.Plats.Count;
            Ensure(n + 1);
            for (int i = 0; i < n; i++) { cost[i] = Inf; parent[i] = -1; done[i] = false; walkE[i] = false; }
            StartIndex = start;
            stampRebuild = w.Rebuilds; stampTime = now; stampX = startX; stampFull = goal < 0;
            if (start < 0 || start >= n) { StartIndex = -1; return; }
            cost[start] = 0; arrX[start] = startX;
            double s = w.Scale;
            double maxUp = BrainTune.MaxJumpUp * s;
            double maxGap = (BrainTune.MaxJumpAcrossDown + 2) * s;
            double perPx = 1.0 / (300 * s);
            double comfyDrop = BrainTune.ComfyDrop * s, dropCost = 1.0 / (BrainTune.DropCostPer * s);
            bool anyBans = Bans.Count > 0;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; double best = Inf;
                for (int i = 0; i < n; i++) if (!done[i] && cost[i] < best) { best = cost[i]; u = i; }
                if (u < 0) break;
                done[u] = true;
                if (u == goal) break;
                BrainPlat a = w.Plats[u];
                Platform A = a.P;
                double ax = arrX[u];
                for (int v = 0; v < n; v++)
                {
                    if (done[v]) continue;
                    BrainPlat b = w.Plats[v];
                    if (!b.Usable) continue;
                    Platform B = b.P;
                    if (A.Y - B.Y > maxUp) continue;
                    double gap = B.Left - A.Right, g2 = A.Left - B.Right;
                    if (g2 > gap) gap = g2;
                    if (gap > maxGap) continue;
                    if (anyBans && Bans.IsBanned(A.Key, B.Key, now)) continue;
                    double tx, lx; bool wk;
                    if (!TryEdge(a, b, ax, out tx, out lx, out wk)) continue;
                    double c = best + (wk ? 0.15 : 1.0) + (Math.Abs(ax - tx) + Math.Abs(lx - tx) * 0.5) * perPx;
                    double drop = B.Y - A.Y - comfyDrop;          // a calm cat steps down a tall stack in stages
                    if (drop > 0) c += drop * dropCost;
                    if (c < cost[v]) { cost[v] = c; parent[v] = u; takeX[v] = tx; landX[v] = lx; arrX[v] = lx; walkE[v] = wk; }
                }
            }
        }

        public bool TryEdge(BrainPlat a, BrainPlat b, double ax, out double tx, out double lx, out bool walk)
        {
            tx = lx = 0; walk = false;
            if (a == null || b == null || a == b || !b.Usable) return false;
            double s = w.Scale;
            Platform A = a.P, B = b.P;
            double dy = A.Y - B.Y;                        // > 0: b is higher
            if (dy > BrainTune.MaxJumpUp * s) return false;

            if (Math.Abs(dy) <= 2)
            {
                double oLo = Math.Max(a.WLo, b.WLo), oHi = Math.Min(a.WHi, b.WHi);
                if (oLo <= oHi + 2)
                {
                    double ox = oLo <= oHi ? BrainWorld.Clamp(ax, oLo, oHi) : (oLo + oHi) * 0.5;
                    tx = BrainWorld.Clamp(ox, a.WLo, a.WHi);
                    lx = BrainWorld.Clamp(ox, b.WLo, b.WHi);
                    walk = true;
                    return true;
                }
            }

            bool down = dy < -4 * s;
            if (!down)
            {
                if (b.SHi >= a.WLo && b.SLo <= a.WHi)
                {
                    // b is above a: spring up through it (one-way) after a short run-up
                    lx = BrainWorld.Clamp(ax, b.SLo, b.SHi);
                    double run = BrainWorld.Clamp(Math.Max(dy, 0) * 0.3, 4 * s, 18 * s);
                    double dir = ax <= lx ? 1 : -1;
                    tx = BrainWorld.Clamp(lx - dir * run, a.WLo, a.WHi);
                }
                else if (b.SLo > a.WHi) { tx = a.WHi; lx = b.SLo; }
                else { tx = a.WLo; lx = b.SHi; }
                if (Math.Abs(lx - tx) > BrainTune.MaxJumpAcross * s) return false;
            }
            else
            {
                // hop down: leave a over one of its ends and land clear of it
                double drop = -dy;
                double hop = BrainWorld.Clamp(drop * 0.1, 8 * s, 24 * s);
                double txR = a.WHi;
                double lxR = BrainWorld.Clamp(Math.Max(txR + hop, A.Right + 2 * s), b.SLo, b.SHi);
                bool okR = lxR >= A.Right + 1 && lxR - txR <= BrainTune.MaxJumpAcrossDown * s;
                double txL = a.WLo;
                double lxL = BrainWorld.Clamp(Math.Min(txL - hop, A.Left - 2 * s), b.SLo, b.SHi);
                bool okL = lxL <= A.Left - 2 && txL - lxL <= BrainTune.MaxJumpAcrossDown * s;
                if (!okR && !okL) return false;
                bool useR;
                if (okR && okL) useR = Math.Abs(ax - txR) + (lxR - txR) <= Math.Abs(ax - txL) + (txL - lxL);
                else useR = okR;
                if (useR) { tx = txR; lx = lxR; } else { tx = txL; lx = lxL; }
            }

            double clear = BrainJump.Clearance(dy, lx - tx, s);
            BrainArc arc;
            if (!BrainJump.Solve(w, tx, A.Y, lx, B.Y, s, clear, true, out arc)) return false;
            if (!w.HeadroomOk(lx, B.Y)) return false;
            if (B.Y > A.Y + 0.5 && CrossesSolidFloor(A, B, tx, arc)) return false;
            if (down)
            {
                double xCross = tx + arc.Vx * 2 * arc.T1;   // where the arc comes back down through a's line
                if (xCross >= A.Left && xCross < A.Right) return false;
            }
            return true;
        }

        bool CrossesSolidFloor(Platform A, Platform B, double tx, BrainArc arc)
        {
            for (int m = 0; m < w.MonitorCount; m++)
            {
                BrainPlat f = w.FloorOfMon(m);
                if (f == null || f.P == B) continue;
                double fy = f.P.Y;
                if (fy < A.Y - 0.5 || fy >= B.Y - 0.5) continue;
                double x = BrainJump.XAtDescent(tx, A.Y, arc, fy);
                if (x < f.P.Left || x >= f.P.Right) continue;
                if (w.FloorSolidAt(f, x)) return true;
            }
            return false;
        }

        public bool BuildRoute(int goal, List<BrainHop> route, List<BrainHop> pool)
        {
            route.Clear();
            if (!Reachable(goal)) return false;
            int len = 0;
            for (int v = goal; v != StartIndex; v = parent[v])
            {
                if (v < 0 || ++len > n) return false;
            }
            while (pool.Count < len) pool.Add(new BrainHop());
            for (int k = 0; k < len; k++) route.Add(null);
            int idx = len - 1;
            for (int v = goal; v != StartIndex; v = parent[v])
            {
                BrainHop h = pool[idx];
                BrainPlat pa = w.Plats[parent[v]], pb = w.Plats[v];
                h.FromKey = pa.P.Key; h.ToKey = pb.P.Key; h.Walk = walkE[v];
                h.TakeX = takeX[v]; h.LandX = landX[v];
                h.FromWindow = pa.P.Kind == PlatformKind.WindowTop && pa.P.Bounds.Width > 0;
                h.ToWindow = pb.P.Kind == PlatformKind.WindowTop && pb.P.Bounds.Width > 0;
                h.TakeRel = h.FromWindow ? h.TakeX - pa.P.Bounds.Left : 0;
                h.LandRel = h.ToWindow ? h.LandX - pb.P.Bounds.Left : 0;
                route[idx] = h;
                idx--;
            }
            return true;
        }
    }

    internal enum NavStatus { None, Moving, Arrived, Failed }

    internal sealed class BrainNav
    {
        readonly BrainWorld w;
        readonly BrainBody body;
        readonly BrainPath path;
        public bool Active;
        public string GoalKey = "";
        bool goalWindow;
        double goalX, goalRel;
        public double SpeedU = BrainTune.WalkSpeed;
        public bool IgnoreBans;
        readonly List<BrainHop> route = new List<BrainHop>();
        readonly List<BrainHop> pool = new List<BrainHop>();
        int pos;
        bool needPlan;
        int routeStamp = -1;
        double lastPlan = -100, deadline;
        int plans;
        bool hopInFlight;
        int flightJumpId;
        string flightFrom = "", flightTo = "";
        public int StartScale;

        public BrainNav(BrainWorld world, BrainBody b, BrainPath p) { w = world; body = b; path = p; }

        public void Start(BrainPlat goal, double x, double speedU, double now, double timeout)
        {
            Active = true;
            GoalKey = goal.P.Key;
            goalWindow = goal.P.Kind == PlatformKind.WindowTop && goal.P.Bounds.Width > 0;
            goalX = x; goalRel = goalWindow ? x - goal.P.Bounds.Left : 0;
            SpeedU = speedU;
            StartScale = w.Scale;
            route.Clear(); pos = 0; needPlan = true; plans = 0; hopInFlight = false;
            deadline = now + timeout;
            lastPlan = -100;
        }

        public void SetGoalX(BrainPlat goal, double x)
        {
            if (goal == null || goal.P.Key != GoalKey) return;
            goalX = x; goalRel = goalWindow ? x - goal.P.Bounds.Left : 0;
        }

        public void Stop() { Active = false; route.Clear(); pos = 0; hopInFlight = false; }

        bool ResolveGoal(out BrainPlat gp, out double gx)
        {
            gx = goalX;
            if (goalWindow)
            {
                gp = null; double bestD = double.MaxValue;
                for (int i = w.FirstIndex(GoalKey); i >= 0; i = w.Plats[i].NextSameKey)
                {
                    BrainPlat b = w.Plats[i];
                    if (!b.Usable || b.P.Bounds.Width <= 0) continue;
                    double x = b.P.Bounds.Left + goalRel;
                    double d = x < b.SLo ? b.SLo - x : (x > b.SHi ? x - b.SHi : 0);
                    if (d < bestD) { bestD = d; gp = b; gx = BrainWorld.Clamp(x, b.SLo, b.SHi); }
                }
                return gp != null;
            }
            gp = w.FindKeyNear(GoalKey, goalX);
            if (gp == null || !gp.Usable) return false;
            gx = BrainWorld.Clamp(goalX, gp.WLo, gp.WHi);
            return true;
        }

        public NavStatus Tick(double now, double dt, double crouchDur)
        {
            if (!Active) return NavStatus.None;
            if (now > deadline) { Stop(); return NavStatus.Failed; }
            if (body.Mode != BodyMode.Ground || body.Ground == null) return NavStatus.Moving;
            BrainPlat gp; double gx;
            if (!ResolveGoal(out gp, out gx)) { Stop(); return NavStatus.Failed; }
            double speed = SpeedU * w.Scale;

            if (body.GroundKey == GoalKey)
            {
                double x = gx;
                if (goalWindow && body.Riding) x = body.RideFrame.Left + goalRel;
                double lo, hi;
                body.LiveWalkRange(out lo, out hi);
                if ((x >= lo - 2 && x <= hi + 2) || body.Ground == gp)
                {
                    if (body.WalkToward(x, speed, dt)) { Stop(); return NavStatus.Arrived; }
                    return NavStatus.Moving;
                }
            }

            if (w.Rebuilds != routeStamp)
            {
                routeStamp = w.Rebuilds;
                if (!needPlan && !ValidateNext()) needPlan = true;
            }
            if (!needPlan && (pos >= route.Count || route[pos].FromKey != body.GroundKey)) needPlan = true;
            if (needPlan)
            {
                if (now - lastPlan < 0.3) return NavStatus.Moving;       // stand for a moment, then think again
                if (!Plan(now, gp)) { Stop(); return NavStatus.Failed; }
            }
            BrainHop h = route[pos];
            double tx = h.FromWindow && body.Riding ? body.RideFrame.Left + h.TakeRel : h.TakeX;
            if (!body.WalkToward(tx, speed, dt)) return NavStatus.Moving;

            BrainPlat to = w.FindKeyNear(h.ToKey, h.LandX);
            if (to == null || !to.Usable) { needPlan = true; return NavStatus.Moving; }
            double lx = h.ToWindow && to.P.Bounds.Width > 0 ? to.P.Bounds.Left + h.LandRel : h.LandX;
            if (h.Walk)
            {
                double cx = body.X;
                body.PlaceOnGround(to, BrainWorld.Clamp(lx, to.WLo, to.WHi));
                if (Math.Abs(body.X - cx) > 0.1) body.FaceLeft = body.X < cx;
                body.MovedThisTick = true;
                pos++;
                return NavStatus.Moving;
            }
            lx = BrainWorld.Clamp(lx, to.SLo, to.SHi);
            body.BeginCrouch(to, lx, crouchDur, 0);
            hopInFlight = true; flightJumpId = body.JumpId; flightFrom = h.FromKey; flightTo = h.ToKey;
            return NavStatus.Moving;
        }

        bool ValidateNext()
        {
            if (pos >= route.Count || body.Ground == null) return false;
            BrainHop h = route[pos];
            if (h.FromKey != body.GroundKey) return false;
            BrainPlat to = w.FindKeyNear(h.ToKey, h.LandX);
            if (to == null || !to.Usable) return false;
            double tx, lx; bool wk;
            if (!path.TryEdge(body.Ground, to, body.X, out tx, out lx, out wk)) return false;
            // hysteresis: keep the planned points while close to the recomputed ones, otherwise a scale flip at a
            // dpi boundary moves the take-off point across the boundary and back and the cat dithers forever
            bool fromWin = h.FromWindow && body.Ground.P.Bounds.Width > 0, toWin = h.ToWindow && to.P.Bounds.Width > 0;
            double oldTx = fromWin ? body.Ground.P.Bounds.Left + h.TakeRel : h.TakeX;
            double oldLx = toWin ? to.P.Bounds.Left + h.LandRel : h.LandX;
            double s = w.Scale;
            if (wk == h.Walk && Math.Abs(oldTx - tx) <= 16 * s && Math.Abs(oldLx - lx) <= 24 * s)
            {
                tx = BrainWorld.Clamp(oldTx, body.Ground.WLo, body.Ground.WHi);
                lx = wk ? BrainWorld.Clamp(oldLx, to.WLo, to.WHi) : BrainWorld.Clamp(oldLx, to.SLo, to.SHi);
            }
            h.TakeX = tx; h.LandX = lx; h.Walk = wk;
            h.TakeRel = fromWin ? tx - body.Ground.P.Bounds.Left : 0;
            h.LandRel = toWin ? lx - to.P.Bounds.Left : 0;
            return true;
        }

        bool Plan(double now, BrainPlat gp)
        {
            lastPlan = now; needPlan = false; plans++;
            if (plans > 14) return false;
            if (!IgnoreBans && path.Bans.TargetBanned(GoalKey, now)) return false;
            BrainPlat start = body.Ground;
            if (start == null) return false;
            path.Explore(start.Index, body.X, gp.Index, now);
            if (!path.Reachable(gp.Index)) return false;
            if (!path.BuildRoute(gp.Index, route, pool) || route.Count == 0) return false;
            pos = 0; routeStamp = w.Rebuilds;
            return true;
        }

        public void OnLanded(string key, int jumpId, double now)
        {
            if (!Active) return;
            if (!hopInFlight || jumpId != flightJumpId) { needPlan = true; hopInFlight = false; return; }
            hopInFlight = false;
            if (key == flightTo)
            {
                if (pos < route.Count && route[pos].ToKey == key) pos++;
                else needPlan = true;
            }
            else
            {
                path.Bans.RecordFailure(flightFrom, flightTo, now);
                needPlan = true;
            }
        }

        public void OnLaunchFailed(double now)
        {
            if (hopInFlight)
            {
                path.Bans.RecordFailure(flightFrom, flightTo, now);
            }
            hopInFlight = false;
            needPlan = true;
            lastPlan = -100;
        }

        public void Invalidate() { needPlan = true; hopInFlight = false; }
    }
}
