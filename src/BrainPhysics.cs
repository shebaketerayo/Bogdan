using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopCat
{
    // distances are in U (art pixels) and multiplied by the current scale for screen px; times are in seconds
    internal static class BrainTune
    {
        public const double WalkSpeed = 22;
        public const double RunSpeed = 80;
        public const double ChaseSpeed = 85;
        public const double CallSpeed = 125;
        public const double Gravity = 900;
        public const double TerminalFall = 1400;
        public const double MaxJumpUp = 70;          // platform-to-platform rise
        public const double MaxRise = 94;            // takeoff to apex
        public const double MaxJumpAcross = 120;
        public const double MaxJumpAcrossDown = 150;
        public const double BigDrop = 70;
        public const double ComfyDrop = 120;         // planned drops up to this height cost like any jump,
        public const double DropCostPer = 70;        // every further DropCostPer of drop costs one extra jump
        public const double MaxAirTime = 6.0;
        public const double ReleaseSpeedCap = 2500;  // screen px/s, not U
        public const double EdgeMargin = 3;
        public const double LostGraceAwake = 0.15;   // tolerates scanner flicker
        public const double LostGraceAsleep = 1.5;
        public const double RunAnimSpeed = 45;
    }

    // pooled: never keep a reference across a world rebuild, keep keys instead
    internal sealed class BrainPlat
    {
        public Platform P;
        public int Index;
        public int Mon;
        public bool Usable;
        public int NextSameKey = -1;
        public double WLo, WHi;   // feet range when walking on it (walls applied)
        public double SLo, SHi;   // preferred landing range
        public bool IsFolder { get { return P.Kind == PlatformKind.Icon && P.IsFolder; } }
    }

    internal sealed class BrainWorld
    {
        public WorldSnapshot Snap;
        long version = long.MinValue;
        public int Scale = 1;
        public bool UseWindows = true;
        public double CatHArt = 22, HalfWArt = 11;
        public double CatH = 66, HalfW = 33;
        public int Rebuilds;
        public readonly List<BrainPlat> Plats = new List<BrainPlat>();
        readonly List<BrainPlat> pool = new List<BrainPlat>();
        readonly Dictionary<string, int> firstByKey = new Dictionary<string, int>(StringComparer.Ordinal);
        BrainPlat[] floorByMon = new BrainPlat[0];
        Rectangle[] mons = new Rectangle[0];
        int monCount;

        public int MonitorCount { get { return monCount; } }
        public Rectangle MonitorBounds(int i) { return mons[i]; }

        public bool Sync(WorldSnapshot snap, int scale, double catHArt, double halfWArt, bool useWindows)
        {
            if (snap == null) return false;
            if (scale < 1) scale = 1;
            if (ReferenceEquals(snap, Snap) && snap.Version == version && scale == Scale && useWindows == UseWindows
                && catHArt == CatHArt && halfWArt == HalfWArt) return false;
            Snap = snap; version = snap.Version; Scale = scale; UseWindows = useWindows;
            CatHArt = catHArt; HalfWArt = halfWArt;
            CatH = catHArt * scale; HalfW = halfWArt * scale;
            Rebuild();
            return true;
        }

        void Rebuild()
        {
            Rebuilds++;
            List<MonitorInfo> ms = Snap.Monitors;
            monCount = ms == null ? 0 : ms.Count;
            if (mons.Length < monCount) mons = new Rectangle[monCount];
            for (int i = 0; i < monCount; i++) mons[i] = ms[i] == null ? Rectangle.Empty : ms[i].Bounds;
            if (floorByMon.Length < monCount) floorByMon = new BrainPlat[monCount];
            else Array.Clear(floorByMon, 0, floorByMon.Length);
            Plats.Clear();
            firstByKey.Clear();
            List<Platform> ps = Snap.Platforms;
            if (ps != null)
            {
                for (int i = 0; i < ps.Count; i++)
                {
                    Platform p = ps[i];
                    if (p == null || p.Key == null || p.Right - p.Left < 2) continue;
                    if (p.Kind == PlatformKind.Floor && FloorTaken(p)) continue;
                    Add(p);
                }
            }
            // every monitor gets a floor even if the provider did not publish one
            for (int m = 0; m < monCount; m++)
            {
                if (floorByMon[m] != null || ms[m] == null) continue;
                Rectangle wa = ms[m].WorkArea.Width > 0 && ms[m].WorkArea.Height > 0 ? ms[m].WorkArea : ms[m].Bounds;
                if (wa.Width < 2) continue;
                Platform fp = new Platform();
                fp.Kind = PlatformKind.Floor; fp.Key = "floor:" + (ms[m].DeviceName ?? ("#" + m));
                fp.Left = wa.Left; fp.Right = wa.Right; fp.Y = wa.Bottom; fp.Name = ""; fp.Bounds = wa; fp.MonitorIndex = m;
                Add(fp);
            }
            for (int i = 0; i < Plats.Count; i++) ComputeRanges(Plats[i]);
        }

        bool FloorTaken(Platform p)
        {
            int m = p.MonitorIndex;
            if (m < 0 || m >= monCount) return false;
            return floorByMon[m] != null;
        }

        void Add(Platform p)
        {
            BrainPlat b;
            if (Plats.Count < pool.Count) b = pool[Plats.Count];
            else { b = new BrainPlat(); pool.Add(b); }
            b.P = p; b.Index = Plats.Count; b.NextSameKey = -1;
            double cx = (p.Left + p.Right) * 0.5;
            int m = p.MonitorIndex;
            if (m < 0 || m >= monCount || !mons[m].Contains((int)cx, p.Y - 1)) m = MonitorAt(cx, p.Y - 1);
            b.Mon = m;
            int first;
            if (firstByKey.TryGetValue(p.Key, out first)) { b.NextSameKey = first; firstByKey[p.Key] = b.Index; }
            else firstByKey.Add(p.Key, b.Index);
            if (p.Kind == PlatformKind.Floor)
            {
                b.Usable = true;
                if (m >= 0 && floorByMon[m] == null) floorByMon[m] = b;
            }
            else if (p.Kind == PlatformKind.WindowTop && !UseWindows) b.Usable = false;
            else b.Usable = InsideAny(cx, p.Y - 1) && HeadroomOk(cx, p.Y);
            Plats.Add(b);
        }

        void ComputeRanges(BrainPlat b)
        {
            double lo, hi;
            WalkRangeLR(b.P.Left, b.P.Right, b.P.Y, out lo, out hi);
            b.WLo = lo; b.WHi = hi;
            double m = BrainTune.EdgeMargin * Scale;
            double half = (hi - lo) * 0.5;
            if (m > half) m = half;
            b.SLo = lo + m; b.SHi = hi - m;
        }

        public bool InsideAny(double x, double y)
        {
            for (int i = 0; i < monCount; i++)
            {
                Rectangle r = mons[i];
                if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return true;
            }
            return false;
        }

        public int MonitorContaining(double x, double y)
        {
            for (int i = 0; i < monCount; i++)
            {
                Rectangle r = mons[i];
                if (x >= r.Left && x < r.Right && y >= r.Top && y < r.Bottom) return i;
            }
            return -1;
        }

        public int MonitorAt(double x, double y)
        {
            int m = MonitorContaining(x, y);
            if (m >= 0 || monCount == 0) return m;
            int best = -1; double bestD = double.MaxValue;
            for (int i = 0; i < monCount; i++)
            {
                Rectangle r = mons[i];
                double dx = x < r.Left ? r.Left - x : (x >= r.Right ? x - r.Right + 1 : 0);
                double dy = y < r.Top ? r.Top - y : (y >= r.Bottom ? y - r.Bottom + 1 : 0);
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public bool HeadroomOk(double x, double y)
        {
            return InsideAny(x, y - CatH);
        }

        public double CeilingAt(double x, double y)
        {
            int m = MonitorAt(x, y - 1);
            if (m < 0) return y - 100000;
            double top = mons[m].Top;
            for (int guard = 0; guard < 8; guard++)
            {
                int k = MonitorContaining(x, top - 1);
                if (k < 0) break;
                top = mons[k].Top;
            }
            return top;
        }

        // monitor edges are walls only where no other monitor continues
        public void WalkRangeLR(double l, double r, double y, out double lo, out double hi)
        {
            lo = l; hi = r - 1;
            if (monCount == 0) return;
            double yy = y - 1;
            double cx = (l + r) * 0.5;
            int m = MonitorContaining(Clamp(cx, l, r - 1), yy);
            if (m < 0) m = MonitorContaining(l, yy);
            if (m < 0) m = MonitorContaining(r - 1, yy);
            if (m < 0) m = MonitorAt(cx, yy);
            if (m < 0) return;
            double leftEdge = mons[m].Left, rightEdge = mons[m].Right;
            for (int guard = 0; guard < 8; guard++)
            {
                int k = MonitorContaining(leftEdge - 1, yy);
                if (k < 0 || mons[k].Left >= leftEdge) break;
                leftEdge = mons[k].Left;
            }
            for (int guard = 0; guard < 8; guard++)
            {
                int k = MonitorContaining(rightEdge, yy);
                if (k < 0 || mons[k].Right <= rightEdge) break;
                rightEdge = mons[k].Right;
            }
            double wl = leftEdge + HalfW, wr = rightEdge - 1 - HalfW;
            if (lo < wl) lo = wl;
            if (hi > wr) hi = wr;
            if (lo > hi)
            {
                double x = Clamp(cx, l, r - 1);
                x = Clamp(x, leftEdge + 1, rightEdge - 2);
                lo = hi = x;
            }
        }

        // a floor is solid only where no monitor continues below it; above a stacked monitor it is an
        // ordinary one-way platform
        public bool FloorSolidAt(BrainPlat f, double x)
        {
            int m = MonitorContaining(x, f.P.Y);
            return m < 0 || m == f.Mon;
        }

        public BrainPlat FloorOfMon(int m)
        {
            if (m < 0 || m >= monCount) return null;
            return floorByMon[m];
        }

        public BrainPlat FloorAt(double x, double y)
        {
            BrainPlat f = FloorOfMon(MonitorAt(x, y - 1));
            if (f != null) return f;
            for (int i = 0; i < monCount; i++) if (floorByMon[i] != null) return floorByMon[i];
            return null;
        }

        public int FirstIndex(string key)
        {
            int i;
            if (key != null && firstByKey.TryGetValue(key, out i)) return i;
            return -1;
        }

        public BrainPlat FindKey(string key)
        {
            int i = FirstIndex(key);
            return i >= 0 ? Plats[i] : null;
        }

        public BrainPlat FindKeyContaining(string key, double x, double tol)
        {
            for (int i = FirstIndex(key); i >= 0; i = Plats[i].NextSameKey)
            {
                Platform p = Plats[i].P;
                if (x >= p.Left - tol && x < p.Right + tol) return Plats[i];
            }
            return null;
        }

        public BrainPlat FindKeyNear(string key, double x)
        {
            BrainPlat best = null; double bestD = double.MaxValue;
            for (int i = FirstIndex(key); i >= 0; i = Plats[i].NextSameKey)
            {
                Platform p = Plats[i].P;
                double d = x < p.Left ? p.Left - x : (x >= p.Right ? x - p.Right + 1 : 0);
                if (d < bestD) { bestD = d; best = Plats[i]; }
            }
            return best;
        }

        public BrainPlat BelowAt(double x, double y, double reach)
        {
            BrainPlat best = null; double bestScore = double.MaxValue;
            for (int i = 0; i < Plats.Count; i++)
            {
                BrainPlat b = Plats[i];
                if (!b.Usable || b.P.Y <= y + 2) continue;
                double dx = x < b.SLo ? b.SLo - x : (x > b.SHi ? x - b.SHi : 0);
                if (dx > reach) continue;
                double score = (b.P.Y - y) + dx * 3;
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        public static double Clamp(double v, double lo, double hi)
        {
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }

    internal struct BrainArc
    {
        public double Vx, Vy, G, T, ApexY, T1;
    }

    internal static class BrainJump
    {
        public static double Clearance(double rise, double dx, double s)
        {
            bool up = rise > 2 * s;
            double c = (up ? 9 * s : 5 * s) + (up ? 0.10 : 0.08) * Math.Abs(dx);
            double cap = (up ? 24 : 16) * s;
            return c > cap ? cap : c;
        }

        public static bool Solve(BrainWorld w, double x0, double y0, double x1, double y1, double s, double clearance,
            bool checkArc, out BrainArc arc)
        {
            arc = new BrainArc();
            double g = BrainTune.Gravity * s;
            double hi = Math.Min(y0, y1);
            double top = hi - clearance;
            double ceil = w.CeilingAt((x0 + x1) * 0.5, hi) + w.CatH + 1;
            if (top < ceil) top = ceil;
            if (top > hi - 1) return false;
            double h1 = y0 - top, h2 = y1 - top;
            if (h1 > BrainTune.MaxRise * s) return false;
            double t1 = Math.Sqrt(2 * h1 / g), t2 = Math.Sqrt(2 * h2 / g);
            arc.T1 = t1;
            arc.T = t1 + t2;
            arc.Vy = -g * t1;
            arc.Vx = (x1 - x0) / arc.T;
            arc.G = g;
            arc.ApexY = top;
            if (checkArc && !ArcInside(w, x0, y0, arc)) return false;
            return true;
        }

        // x where the arc comes down through line y (the later root)
        public static double XAtDescent(double x0, double y0, BrainArc a, double y)
        {
            double disc = a.Vy * a.Vy - 2 * a.G * (y0 - y);
            if (disc < 0) disc = 0;
            double t = a.G > 1e-9 ? (-a.Vy + Math.Sqrt(disc)) / a.G : 0;
            return x0 + a.Vx * t;
        }

        public static bool ArcInside(BrainWorld w, double x0, double y0, BrainArc a)
        {
            const int N = 6;
            for (int k = 1; k <= N; k++)
            {
                double t = k == N ? a.T1 : a.T * k / N;
                double x = x0 + a.Vx * t, y = y0 + a.Vy * t + 0.5 * a.G * t * t;
                if (!w.InsideAny(x, y - 1)) return false;
                if (!w.InsideAny(x, y - w.CatH + 1)) return false;
            }
            return true;
        }
    }

    internal enum BodyMode { Ground = 0, Crouch = 1, Air = 2, Land = 3, Held = 4 }

    internal sealed class BrainBody
    {
        readonly BrainWorld w;
        public IWorldProvider Prov;
        public double S = 3;

        public double X, Y;
        public BodyMode Mode = BodyMode.Ground;
        public bool FaceLeft;
        public double ModeTime, ModeDur;

        public string GroundKey = "";
        public PlatformKind GroundKind = PlatformKind.Floor;
        public IntPtr GroundHwnd;
        public BrainPlat Ground;             // resolved this tick, null when support is missing
        public double GroundL, GroundR;
        public bool Riding;
        public Rectangle RideFrame;
        public double LostTime;
        public bool LostHandled;
        public bool NoClamp;                 // entrance: may stand beyond the floor's walk range
        public double Vwalk;
        public bool MovedThisTick;

        public double Vx, Vy, G, AirTime, MinY;
        public bool Planned, BigFall, Targeted;
        public string TargetKey = "";
        public IntPtr TargetHwnd;
        public bool TargetIsWindow;
        public double TargetX, TargetRel;
        public double JumpClearU;            // <= 0 means automatic
        public int JumpId;

        // landing report, valid when StepAir returned true
        public string LandedKey = "";
        public double LandFallH;
        public bool LandWasPlanned;
        public int LandJumpId;

        public BrainBody(BrainWorld world) { w = world; }

        public void PlaceOnGround(BrainPlat p, double x)
        {
            Mode = BodyMode.Ground; ModeTime = 0; ModeDur = 0;
            GroundKey = p.P.Key; GroundKind = p.P.Kind; GroundHwnd = p.P.Hwnd;
            Ground = p; X = x; Y = p.P.Y; GroundL = p.P.Left; GroundR = p.P.Right;
            LostTime = 0; LostHandled = false; Vwalk = 0; Riding = false; Targeted = false; AirTime = 0;
            if (GroundKind == PlatformKind.WindowTop)
            {
                Rectangle f;
                if (GetFrame(GroundHwnd, GroundKey, out f) && p.P.Bounds.Width > 0)
                {
                    RideFrame = f; Riding = true;
                    GroundL = f.Left + (p.P.Left - p.P.Bounds.Left);
                    GroundR = f.Left + (p.P.Right - p.P.Bounds.Left);
                    Y = f.Top + (p.P.Y - p.P.Bounds.Top);
                }
            }
        }

        bool GetFrame(IntPtr hwnd, string key, out Rectangle f)
        {
            if (Prov != null)
            {
                try { return Prov.TryGetWindowFrame(hwnd, out f); }
                catch { f = Rectangle.Empty; return false; }
            }
            BrainPlat b = w.FindKey(key);
            if (b != null && b.P.Bounds.Width > 0) { f = b.P.Bounds; return true; }
            f = Rectangle.Empty;
            return false;
        }

        // 0 = supported, 1 = missing (grace applies), 2 = definitely gone (window closed / no floor at all)
        public int ResolveGround()
        {
            Ground = null;
            if (GroundKind == PlatformKind.Floor)
            {
                BrainPlat p = w.FindKey(GroundKey);
                if (p == null)
                {
                    p = w.FloorAt(X, Y);
                    if (p == null) return 2;
                    GroundKey = p.P.Key;
                }
                Ground = p; Y = p.P.Y; GroundL = p.P.Left; GroundR = p.P.Right;
                if (NoClamp)
                {
                    if (X >= p.WLo && X <= p.WHi) NoClamp = false;
                }
                else if (X < p.WLo) X = p.WLo;
                else if (X > p.WHi) X = p.WHi;
                return 0;
            }
            if (GroundKind == PlatformKind.Icon)
            {
                BrainPlat p = w.FindKeyContaining(GroundKey, X, 1.0);
                if (p == null || Math.Abs(p.P.Y - Y) > 3) return 1;
                Ground = p; Y = p.P.Y; GroundL = p.P.Left; GroundR = p.P.Right;
                if (!p.Usable) return 1;
                return 0;
            }
            Rectangle f;
            if (!GetFrame(GroundHwnd, GroundKey, out f)) return 2;
            if (Riding) { X += f.Left - RideFrame.Left; Y += f.Top - RideFrame.Top; }
            RideFrame = f; Riding = true;
            for (int i = w.FirstIndex(GroundKey); i >= 0; i = w.Plats[i].NextSameKey)
            {
                Platform sp = w.Plats[i].P;
                double l, r, y;
                if (sp.Bounds.Width > 0)
                {
                    l = f.Left + (sp.Left - sp.Bounds.Left); r = f.Left + (sp.Right - sp.Bounds.Left);
                    y = f.Top + (sp.Y - sp.Bounds.Top);
                }
                else { l = sp.Left; r = sp.Right; y = sp.Y; }
                if (X >= l - 1 && X < r + 1)
                {
                    Ground = w.Plats[i]; GroundL = l; GroundR = r; Y = y;
                    // a window dragged against a monitor edge slides under the cat instead of carrying it off-screen
                    double lo, hi;
                    w.WalkRangeLR(l, r, y, out lo, out hi);
                    if (X < lo) X = lo;
                    if (X > hi) X = hi;
                    if (X < l) X = l;
                    if (X > r - 1) X = r - 1;
                    break;
                }
            }
            // the live frame is authoritative (no scanner flicker to wait out): off-screen, below the floor line
            // or under the screen top means gone now, even during a sleepy grace period
            if (!w.InsideAny(X, Y - 1)) { PullInside(); return 2; }
            if (!w.HeadroomOk(X, Y)) return 2;
            BrainPlat fl = w.FloorAt(X, Y);
            if (fl != null && Y > fl.P.Y && w.FloorSolidAt(fl, X)) return 2;
            if (Ground == null) return 1;            // spot hidden behind another window (stale / occluded segment)
            return 0;
        }

        // carried off-screen by a dragged window: step back onto the nearest monitor so the fall starts on screen
        void PullInside()
        {
            int m = w.MonitorAt(X, Y - 1);
            if (m < 0) return;
            Rectangle b = w.MonitorBounds(m);
            X = BrainWorld.Clamp(X, b.Left, b.Right - 1);
            Y = BrainWorld.Clamp(Y, b.Top + 1, b.Bottom);
        }

        public void LiveWalkRange(out double lo, out double hi)
        {
            if (Ground != null && GroundKind != PlatformKind.WindowTop) { lo = Ground.WLo; hi = Ground.WHi; return; }
            w.WalkRangeLR(GroundL, GroundR, Y, out lo, out hi);
        }

        public void LiveSafeRange(out double lo, out double hi)
        {
            LiveWalkRange(out lo, out hi);
            double m = BrainTune.EdgeMargin * S, half = (hi - lo) * 0.5;
            if (m > half) m = half;
            lo += m; hi -= m;
        }

        public bool WalkToward(double tx, double speed, double dt)
        {
            if (!NoClamp)
            {
                double lo, hi;
                LiveWalkRange(out lo, out hi);
                tx = BrainWorld.Clamp(tx, lo, hi);
            }
            double d = tx - X, ad = Math.Abs(d);
            if (ad <= 0.5) { X = tx; Vwalk = 0; return true; }
            double v = speed;
            double minV = Math.Max(BrainTune.WalkSpeed * S, speed * 0.3);
            if (v > minV)
            {
                double slow = ad * 5.0;          // ease out of a run over the last body length or two
                if (slow < v) v = Math.Max(minV, slow);
            }
            double step = v * dt;
            FaceLeft = d < 0;
            MovedThisTick = true;
            if (step >= ad) { X = tx; Vwalk = 0; return true; }
            X += d > 0 ? step : -step;
            Vwalk = d > 0 ? v : -v;
            return false;
        }

        public void BeginCrouch(BrainPlat to, double landX, double crouchDur, double clearU)
        {
            Mode = BodyMode.Crouch; ModeTime = 0; ModeDur = crouchDur;
            TargetKey = to.P.Key; TargetHwnd = to.P.Hwnd;
            TargetIsWindow = to.P.Kind == PlatformKind.WindowTop && to.P.Bounds.Width > 0;
            TargetX = landX;
            TargetRel = TargetIsWindow ? landX - to.P.Bounds.Left : 0;
            JumpClearU = clearU;
            if (Math.Abs(landX - X) > 1) FaceLeft = landX < X;
            Vwalk = 0;
            JumpId++;
        }

        bool ResolveTarget(out BrainPlat tp, out double tx, out double ty, out double tl, out double tr)
        {
            tp = null; tx = ty = tl = tr = 0;
            if (TargetIsWindow)
            {
                Rectangle f;
                if (!GetFrame(TargetHwnd, TargetKey, out f)) return false;
                double x = f.Left + TargetRel;
                double bestD = double.MaxValue;
                for (int i = w.FirstIndex(TargetKey); i >= 0; i = w.Plats[i].NextSameKey)
                {
                    BrainPlat b = w.Plats[i];
                    if (!b.Usable || b.P.Bounds.Width <= 0) continue;
                    double l = f.Left + (b.P.Left - b.P.Bounds.Left), r = f.Left + (b.P.Right - b.P.Bounds.Left);
                    double d = x < l ? l - x : (x >= r ? x - r + 1 : 0);
                    if (d < bestD) { bestD = d; tp = b; tl = l; tr = r; ty = f.Top + (b.P.Y - b.P.Bounds.Top); }
                }
                if (tp == null || bestD > 2 * S) return false;
                double m = Math.Min(BrainTune.EdgeMargin * S, (tr - tl - 1) * 0.5);
                if (m < 0) m = 0;
                tx = BrainWorld.Clamp(x, tl + m, tr - 1 - m);
                return true;
            }
            tp = w.FindKeyNear(TargetKey, TargetX);
            if (tp == null || !tp.Usable) return false;
            tl = tp.P.Left; tr = tp.P.Right; ty = tp.P.Y;
            tx = BrainWorld.Clamp(TargetX, tp.SLo, tp.SHi);
            return true;
        }

        public bool Launch()
        {
            BrainPlat tp; double tx, ty, tl, tr;
            if (!ResolveTarget(out tp, out tx, out ty, out tl, out tr)) return false;
            double rise = Y - ty;
            if (rise > (BrainTune.MaxJumpUp + 6) * S) return false;
            double clear = JumpClearU > 0 ? JumpClearU * S : BrainJump.Clearance(rise, tx - X, S);
            BrainArc arc;
            if (!BrainJump.Solve(w, X, Y, tx, ty, S, clear, false, out arc)) return false;
            Mode = BodyMode.Air; ModeTime = 0; AirTime = 0; MinY = Y;
            Vx = arc.Vx; Vy = arc.Vy; G = arc.G;
            Planned = true; Targeted = true; BigFall = false;
            if (Math.Abs(Vx) > 1) FaceLeft = Vx < 0;
            Ground = null;
            return true;
        }

        public void StartFall(double vx)
        {
            NoClamp = false;
            Mode = BodyMode.Air; ModeTime = 0; AirTime = 0; MinY = Y;
            Vx = vx; Vy = 0; G = BrainTune.Gravity * S;
            Planned = false; Targeted = false;
            BigFall = EstimateDrop() > BrainTune.BigDrop * S;
            Ground = null; Vwalk = 0;
            JumpId++;
        }

        public void Release(double vx, double vy)
        {
            NoClamp = false;
            // hanging below the floor line (cursor near the taskbar): stand on the floor first
            BrainPlat fl = w.FloorAt(X, Y);
            if (fl != null && Y > fl.P.Y) Y = fl.P.Y;
            Mode = BodyMode.Air; ModeTime = 0; AirTime = 0; MinY = Y;
            Vx = vx; Vy = vy; G = BrainTune.Gravity * S;
            Planned = false; Targeted = false; BigFall = true;
            Ground = null; Vwalk = 0;
            if (Math.Abs(vx) > 60) FaceLeft = vx < 0;
            JumpId++;
        }

        public void Hold(double x, double y)
        {
            NoClamp = false;
            Mode = BodyMode.Held; X = x; Y = y; Vx = Vy = 0; Targeted = false; Ground = null; Vwalk = 0; AirTime = 0;
        }

        double EstimateDrop()
        {
            double xc, yc;
            BrainPlat p = FindCrossing(X, Y - 0.5, X, Y + 100000, out xc, out yc);
            double best = p != null ? yc : double.MaxValue;
            BrainPlat f = w.FloorAt(X, Y);
            if (f != null && f.P.Y >= Y - 0.5 && f.P.Y < best) best = f.P.Y;
            return best == double.MaxValue ? 0 : best - Y;
        }

        void Steer(double tx, double ty, double dt)
        {
            double c = Y - ty;
            double disc = Vy * Vy - 2 * G * c;
            if (disc < 0) return;
            double t = (-Vy + Math.Sqrt(disc)) / G;
            if (t < 0.03) return;
            double want = (tx - X) / t;
            double maxDv = 2400 * S * dt;
            double dv = want - Vx;
            if (dv > maxDv) dv = maxDv; else if (dv < -maxDv) dv = -maxDv;
            Vx += dv;
        }

        public bool StepAir(double dt, double landDur)
        {
            AirTime += dt; ModeTime += dt;
            double x0 = X, y0 = Y;
            BrainPlat tp = null; double tx = 0, ty = 0, tl = 0, tr = 0;
            if (Targeted)
            {
                if (!ResolveTarget(out tp, out tx, out ty, out tl, out tr)) { Targeted = false; tp = null; }
                else Steer(tx, ty, dt);
            }
            X += Vx * dt;
            Y += Vy * dt + 0.5 * G * dt * dt;
            Vy += G * dt;
            double term = BrainTune.TerminalFall * S;
            if (Vy > term) Vy = term;
            if (Y < MinY) MinY = Y;
            ApplyWalls(x0, y0);
            if (Y <= y0) return false;                        // rising: platforms are one-way

            BrainPlat land = null; double lx = 0, ly = 0;
            if (Targeted && tp != null)
            {
                if (y0 <= ty + 0.5 && Y >= ty)
                {
                    double f = (Y - y0) > 1e-9 ? (ty - y0) / (Y - y0) : 1;
                    if (f < 0) f = 0;
                    double xc = x0 + f * (X - x0);
                    if (xc >= tl - 1.5 && xc < tr + 1.5) { land = tp; lx = BrainWorld.Clamp(xc, tl, tr - 1); ly = ty; }
                    else Targeted = false;
                }
                else if (y0 > ty + 0.5) Targeted = false;
            }
            if (land == null && !Targeted)
            {
                double xc, yc;
                BrainPlat p = FindCrossing(x0, y0, X, Y, out xc, out yc);
                if (p != null) { land = p; lx = xc; ly = yc; }
            }
            BrainPlat fl = w.FloorAt(X, y0);
            if (fl != null && Y >= fl.P.Y && (land == null || fl.P.Y < ly))
            {
                double f = (Y - y0) > 1e-9 ? (fl.P.Y - y0) / (Y - y0) : 1;
                if (f < 0) f = 0; if (f > 1) f = 1;
                double xc = x0 + f * (X - x0);
                bool catchIt = w.FloorSolidAt(fl, xc)
                    || (y0 <= fl.P.Y + 0.5 && (!Targeted || tp == fl) && xc >= fl.P.Left && xc < fl.P.Right);
                if (catchIt) { land = fl; lx = BrainWorld.Clamp(xc, fl.WLo, fl.WHi); ly = fl.P.Y; }
            }
            if (land == null) return false;
            DoLand(land, lx, ly, landDur);
            return true;
        }

        BrainPlat FindCrossing(double x0, double y0, double x1, double y1, out double xc, out double yc)
        {
            BrainPlat best = null; xc = 0; yc = 0;
            double bestY = double.MaxValue;
            double span = y1 - y0;
            double band = 400 * S;
            for (int i = 0; i < w.Plats.Count; i++)
            {
                BrainPlat b = w.Plats[i];
                if (!b.Usable || b.P.Kind == PlatformKind.Floor) continue;
                double py = b.P.Y, pl = b.P.Left, pr = b.P.Right;
                if (b.P.Kind == PlatformKind.WindowTop && Prov != null && b.P.Bounds.Width > 0)
                {
                    // the snapshot may be ~150 ms old while a window is dragged: use the live frame
                    if (py < y0 - band || py > y1 + band) continue;
                    Rectangle f;
                    if (!GetFrame(b.P.Hwnd, b.P.Key, out f)) continue;
                    py = f.Top + (b.P.Y - b.P.Bounds.Top);
                    pl = f.Left + (b.P.Left - b.P.Bounds.Left);
                    pr = f.Left + (b.P.Right - b.P.Bounds.Left);
                }
                if (py < y0 - 0.5 || py > y1 || py >= bestY) continue;
                double fr = span > 1e-9 ? (py - y0) / span : 1;
                if (fr < 0) fr = 0;
                double x = x0 + fr * (x1 - x0);
                if (x < pl || x >= pr) continue;
                if (!w.HeadroomOk(x, py) || !w.InsideAny(x, py - 1)) continue;
                best = b; bestY = py; xc = x; yc = py;
            }
            return best;
        }

        void DoLand(BrainPlat p, double x, double y, double landDur)
        {
            LandedKey = p.P.Key;
            LandWasPlanned = Planned;
            LandFallH = y - MinY;
            LandJumpId = JumpId;
            double keepVx = Vx;
            PlaceOnGround(p, x);
            if (!NoClamp)
            {
                double lo, hi;
                LiveWalkRange(out lo, out hi);
                X = BrainWorld.Clamp(X, lo, hi);
            }
            Mode = BodyMode.Land; ModeTime = 0; ModeDur = landDur;
            if (Math.Abs(keepVx) > 5) FaceLeft = keepVx < 0;
        }

        void ApplyWalls(double x0, double y0)
        {
            if (w.MonitorCount == 0) return;
            double midY = Y - w.CatH * 0.5;
            if (!w.InsideAny(X, midY) || !w.InsideAny(X, Y - 1))
            {
                // moved sideways into a region no monitor covers (incl. below a shorter neighbour): wall
                int m = w.MonitorContaining(x0, y0 - 1);
                if (m < 0) m = w.MonitorAt(x0, y0 - w.CatH * 0.5);
                if (m >= 0)
                {
                    Rectangle b = w.MonitorBounds(m);
                    if (X < b.Left || X >= b.Right)
                    {
                        double margin = Math.Min(w.HalfW * 0.6, b.Width * 0.25);
                        X = BrainWorld.Clamp(X, b.Left + margin, b.Right - 1 - margin);
                        Vx = -Vx * 0.3;
                        Targeted = false;
                    }
                }
            }
            double headY = Y - w.CatH;
            if (!w.InsideAny(X, headY))
            {
                double ceil = w.CeilingAt(X, Math.Max(Y, y0));
                if (headY < ceil)
                {
                    Y = ceil + w.CatH;
                    if (Vy < 0) Vy = 0;
                    Targeted = false;
                    if (Y < MinY) MinY = Y;
                }
            }
        }

        public bool Respawn()
        {
            double x = double.IsNaN(X) || double.IsInfinity(X) ? 0 : X;
            double y = double.IsNaN(Y) || double.IsInfinity(Y) ? 0 : Y;
            BrainPlat f = w.FloorAt(x, y);
            if (f == null) return false;
            NoClamp = false;
            PlaceOnGround(f, BrainWorld.Clamp(x, f.WLo, f.WHi));
            return true;
        }
    }
}
