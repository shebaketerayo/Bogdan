using System;
using System.Collections.Generic;
using System.Drawing;

namespace DesktopCat
{
    public static class CatBrainFactory
    {
        public static ICatBrain Create(ICatSprites sprites, CatSettings settings)
        {
            return new CatBrain(sprites, settings, Environment.TickCount);
        }
    }

    internal enum BrainAct { Entrance, Idle, Travel, Nap, Play, Petted, Held, Tumble, Scratch }
    internal enum BrainWhy { Wander, Explore, NapSeek, Call, Down, Scratch }
    internal enum BrainPosture { None, Sitting, Standing, Lying }
    internal enum BrainThen { None, Wander, Explore, Down, NapSeek }
    internal enum BrainPlayPhase { Chase, Watch, Wiggle, Pounce, Swat }

    internal sealed class BrainCursor
    {
        const int N = 64;
        readonly double[] ts = new double[N], xs = new double[N], ys = new double[N];
        int head = -1, count;
        public double X, Y;
        public double LastMove = -1000;
        double lastSample = -1000;

        public void Add(double now, Point p)
        {
            if (count > 0 && Math.Abs(p.X - X) + Math.Abs(p.Y - Y) > 1.5) LastMove = now;
            X = p.X; Y = p.Y;
            if (count == 0 || now - lastSample >= 0.04)
            {
                head = (head + 1) % N;
                ts[head] = now; xs[head] = p.X; ys[head] = p.Y;
                if (count < N) count++;
                lastSample = now;
            }
        }

        public double StillFor(double now) { return now - LastMove; }

        public double PathLength(double now, double window)
        {
            double sum = 0, px = X, py = Y;
            for (int k = 0; k < count; k++)
            {
                int i = (head - k + N) % N;
                if (ts[i] < now - window) break;
                double dx = xs[i] - px, dy = ys[i] - py;
                sum += Math.Sqrt(dx * dx + dy * dy);
                px = xs[i]; py = ys[i];
            }
            return sum;
        }

        public void Velocity(double now, double window, out double vx, out double vy)
        {
            vx = vy = 0;
            if (count < 2) return;
            int oldest = -1;
            for (int k = 1; k < count; k++)
            {
                int i = (head - k + N) % N;
                oldest = i;
                if (ts[i] <= now - window) break;
            }
            if (oldest < 0) return;
            double dt = now - ts[oldest];
            if (dt < 0.01) return;
            vx = (X - xs[oldest]) / dt; vy = (Y - ys[oldest]) / dt;
        }
    }

    internal sealed class CatBrain : ICatBrain
    {
        static readonly int AnimCount = Enum.GetValues(typeof(CatAnim)).Length;

        ICatSprites sprites;
        CatSettings settings;
        readonly Random rng;
        internal readonly BrainWorld World = new BrainWorld();
        internal readonly BrainBody Body;
        internal readonly BrainPath Path;
        internal readonly BrainNav Nav;
        readonly BrainCursor cur = new BrainCursor();

        double catHArt = 22, halfWArt = 11, scruffOffX = 0, scruffOffY = 23;
        readonly double[] durCache = new double[AnimCount];
        readonly bool[] fastAnim = new bool[AnimCount];

        double now, dt, S = 3;
        bool started;
        IWorldProvider prov;

        CatAnim anim = CatAnim.Sit, pose = CatAnim.Sit;
        double animTime;
        bool animRestart;

        readonly CatAnim[] seq = new CatAnim[8];
        int seqLen, seqPos;
        double seqT;
        BrainThen seqThen;

        BrainAct act = BrainAct.Entrance;
        BrainWhy why = BrainWhy.Wander;
        int phase;
        double phaseT;
        CatAnim napAnim = CatAnim.Stretch;
        double idleUntil, nextBlink, lastTurn = -10, entranceX, outsideT;
        bool loaf, hidden;
        double sleepiness, awakeSpan = 260, napUntil, nextZ;
        bool napHopDown, wakeThenCall, wakeThenMeow;
        double playUntil, playCooldown, nearActive, playRetarget, playPhaseT, swatReady, pounceBlockUntil;
        BrainPlayPhase playPhase;
        double petUntil, lastPetT = -10, nextNote, lastSleepHeart = -10;
        bool pendingCall, pendingWake, pendingSleep, pendingMeow, callOpen;
        double callRetarget, callUntil;
        bool tumbleGroom;
        double lastErrLog = -1000, lastRescanReq = -1000, showUntil = -1000;
        readonly List<CatEffect> fx = new List<CatEffect>();

        // standup/sitdown play between sitting and standing poses; behaviour waits meanwhile so one-shot
        // anims and timers are never cut short
        bool inTrans;
        CatAnim transAnim;
        double transT, lastTransEnd = -10, preBehaveX;
        string preBehaveKey = "";

        bool napKnead, petKnead;
        double kneadUntil, nextSlowBlink = 20;
        double nextScratch, scratchUntil, nextMark, nextShred, scratchSpotX;
        bool scratchFaceLeft;
        string markKey;
        double clawX, clawY;

        public CatBrain(ICatSprites sprites, CatSettings settings, int seed)
        {
            rng = new Random(seed);
            Body = new BrainBody(World);
            Path = new BrainPath(World);
            Nav = new BrainNav(World, Body, Path);
            this.settings = settings != null ? settings.Clone() : new CatSettings();
            for (int i = 0; i < AnimCount; i++) durCache[i] = 0.3;
            SetSprites(sprites);
            awakeSpan = Rand(350, 500);
            sleepiness = Rand(0.0, 0.3);
            nextScratch = Rand(90, 240);
        }

        public void SetSprites(ICatSprites s)
        {
            if (s == null) return;
            sprites = s;
            try { ComputeMetrics(); }
            catch (Exception ex) { CatLog.Error("CatBrain.SetSprites", ex); }
        }

        public void ApplySettings(CatSettings s)
        {
            if (s == null) return;
            settings = s.Clone();
            if (settings.Play == PlayMode.Never && act == BrainAct.Play) EndPlay(false);
        }

        public CatFrameOutput Update(CatInput input)
        {
            CatFrameOutput o = new CatFrameOutput();
            try
            {
                UpdateCore(input, o);
            }
            catch (Exception ex)
            {
                if (now - lastErrLog > 30) { lastErrLog = now; CatLog.Error("CatBrain.Update", ex); }
                try { Respawn(); } catch { }
                try { o.Effects.Clear(); FillOutput(o); } catch { }
            }
            return o;
        }

        void ComputeMetrics()
        {
            Point anc = sprites.Anchor;
            double h = 0, hw = 0;
            CatAnim[] probe = { CatAnim.Sit, CatAnim.Stand, CatAnim.Walk };
            for (int k = 0; k < probe.Length; k++)
            {
                if (sprites.FrameCount(probe[k]) <= 0) continue;
                Rectangle r = sprites.BodyBounds(probe[k], 0);
                if (r.Width <= 0 || r.Height <= 0) continue;
                h = Math.Max(h, anc.Y - r.Top);
                hw = Math.Max(hw, Math.Max(anc.X - r.Left, r.Right - anc.X));
            }
            catHArt = h >= 4 && h <= sprites.FrameHeight + 2 ? h : Math.Max(8, sprites.FrameHeight * 0.7);
            halfWArt = hw >= 3 && hw <= sprites.FrameWidth ? hw : Math.Max(4, sprites.FrameWidth * 0.35);
            scruffOffX = 0; scruffOffY = catHArt + 1;
            if (sprites.FrameCount(CatAnim.Held) > 0)
            {
                Rectangle hb = sprites.BodyBounds(CatAnim.Held, 0);
                Point hp = sprites.HeadPoint(CatAnim.Held, 0);
                if (hb.Width > 0 && hb.Height > 0)
                {
                    // held by the scruff: between head and back, just under the cursor (offsets are feet
                    // relative to the cursor, facing right)
                    double sx = (hp.X + anc.X) * 0.5;
                    if (sx < hb.Left) sx = hb.Left;
                    if (sx > hb.Right - 1) sx = hb.Right - 1;
                    scruffOffX = anc.X - (sx + 0.5);
                    scruffOffY = anc.Y - hb.Top + 0.5;
                }
            }
            for (int i = 0; i < AnimCount; i++)
            {
                CatAnim a = (CatAnim)i;
                double d = 0.3; bool fast = false;
                try
                {
                    int fc = sprites.FrameCount(a);
                    d = sprites.Duration(a);
                    double minF = double.MaxValue;
                    for (int f = 0; f < fc; f++)
                    {
                        double fd = sprites.FrameDuration(a, f);
                        if (fd > 0 && fd < minF) minF = fd;
                    }
                    fast = fc > 1 && minF < 0.16;
                }
                catch { }
                if (double.IsNaN(d) || d < 0.05) d = 0.3;
                if (d > 6) d = 6;
                durCache[i] = d; fastAnim[i] = fast;
            }
        }

        double Dur(CatAnim a) { return durCache[(int)a]; }

        double Rand(double a, double b) { return a + (b - a) * rng.NextDouble(); }

        void UpdateCore(CatInput input, CatFrameOutput o)
        {
            fx.Clear();
            markKey = null;
            if (input == null) { FillOutput(o); return; }
            dt = input.Dt;
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0) dt = 0;
            if (dt > 0.1) dt = 0.1;
            now += dt;
            S = input.Scale >= 1 ? input.Scale : 1;
            prov = input.Provider;
            Body.Prov = prov; Body.S = S;
            WorldSnapshot snap = input.World;
            if (snap == null || snap.Monitors == null || snap.Monitors.Count == 0) snap = World.Snap;   // keep the last good world
            if (snap == null)
            {
                o.Visible = false; o.X = 0; o.Y = 0;
                return;
            }
            World.Sync(snap, (int)S, catHArt, halfWArt, settings.AlwaysOnTop && settings.WalkOnWindows);
            if (World.MonitorCount == 0 || World.FloorAt(0, 0) == null)
            {
                o.Visible = false; o.X = Body.X; o.Y = Body.Y;
                return;
            }
            cur.Add(now, input.Cursor);
            Body.MovedThisTick = false;
            if (!started) Start();

            if (input.DragActive)
            {
                if (act != BrainAct.Held) StartHeld();
                HoldAt(input.Cursor);
            }
            else if (act == BrainAct.Held) ReleaseHeld(input.DragReleased, input.ReleaseVelocity);

            if (input.CmdCall)
            {
                pendingCall = true; callOpen = true;
                if (snap.FullscreenAppActive) showUntil = now + 45;     // called explicitly: come out for a while
            }
            hidden = snap.FullscreenAppActive && settings.HideInFullscreen && act != BrainAct.Held && now >= showUntil;

            if (input.CmdWake) pendingWake = true;
            if (input.CmdSleep) pendingSleep = true;
            if (input.DoubleClicked) pendingMeow = true;

            if (act != BrainAct.Held)
            {
                BodyTick();
                if (Body.Mode != BodyMode.Ground) inTrans = false;
                if (Body.Mode == BodyMode.Ground && !Body.NoClamp) ProcessPending(input.Petting || input.Clicked);   // not while still walking in
                preBehaveX = Body.X; preBehaveKey = Body.GroundKey;
                if (!inTrans) Behave();
                Safety();
            }
            else inTrans = false;
            // a call nobody is working on any more (picked up, hidden by a fullscreen app...) is over
            if (callOpen && !pendingCall && !wakeThenCall && !(act == BrainAct.Travel && why == BrainWhy.Call))
            {
                callOpen = false;
            }
            UpdateDrives();
            ResolveAnim();
            FillOutput(o);
        }

        void Start()
        {
            started = true;
            MonitorInfo pm = World.Snap.PrimaryMonitor;
            int mi = pm != null ? World.Snap.Monitors.IndexOf(pm) : 0;
            BrainPlat f = World.FloorOfMon(mi);
            if (f == null) f = World.FloorAt(0, 0);
            if (f == null) { act = BrainAct.Idle; return; }
            int m = f.Mon >= 0 ? f.Mon : Math.Max(0, mi);
            Rectangle b = World.MonitorBounds(Math.Min(m, World.MonitorCount - 1));
            bool leftOpen = !World.InsideAny(b.Left - 1, f.P.Y - 1), rightOpen = !World.InsideAny(b.Right, f.P.Y - 1);
            bool fromLeft;
            if (leftOpen == rightOpen) fromLeft = rng.NextDouble() < 0.5;
            else fromLeft = leftOpen;
            double span = Math.Max(0, f.WHi - f.WLo);
            double startX = fromLeft ? f.P.Left - World.HalfW - 2 : f.P.Right + World.HalfW + 2;
            entranceX = fromLeft ? f.WLo + span * Rand(0.12, 0.35) : f.WHi - span * Rand(0.12, 0.35);
            Body.PlaceOnGround(f, startX);
            Body.NoClamp = true;
            Body.FaceLeft = !fromLeft;
            act = BrainAct.Entrance; phaseT = 0;
            nextBlink = now + 3;
        }

        void SetAct(BrainAct a)
        {
            if (act == BrainAct.Play && a != BrainAct.Play) SetPlayCooldown();
            if (act == BrainAct.Nap && a != BrainAct.Nap)
            {
                // the nap ended early (support vanished, picked up...): a call or meow requested while waking up
                // still stands and is handled once the cat is on its feet
                if (wakeThenCall) { wakeThenCall = false; pendingCall = true; }
                if (wakeThenMeow) { wakeThenMeow = false; pendingMeow = true; }
                napKnead = false;
            }
            act = a;
            phaseT = 0;
        }

        void BodyTick()
        {
            switch (Body.Mode)
            {
                case BodyMode.Ground:
                case BodyMode.Crouch:
                case BodyMode.Land:
                {
                    int r = Body.ResolveGround();
                    if (r == 0) { Body.LostTime = 0; Body.LostHandled = false; }
                    else if (r == 2) { FallNow(); return; }
                    else
                    {
                        Body.LostTime += dt;
                        if (!Body.LostHandled)
                        {
                            double grace = IsAsleep ? BrainTune.LostGraceAsleep : BrainTune.LostGraceAwake;
                            if (Body.LostTime >= grace)
                            {
                                Body.LostHandled = true;
                                if (IsAsleep) { napHopDown = true; NapWake(); }
                                else { FallNow(); return; }
                            }
                        }
                        else if (Body.LostTime > 6) { FallNow(); return; }
                    }
                    if (Body.Mode == BodyMode.Crouch)
                    {
                        Body.ModeTime += dt;
                        if (Body.ModeTime >= Body.ModeDur)
                        {
                            if (!Body.Launch())
                            {
                                Body.Mode = BodyMode.Ground;
                                Nav.OnLaunchFailed(now);
                                OnJumpAborted();
                            }
                        }
                    }
                    else if (Body.Mode == BodyMode.Land)
                    {
                        Body.ModeTime += dt;
                        if (Body.ModeTime >= Body.ModeDur) Body.Mode = BodyMode.Ground;
                    }
                    break;
                }
                case BodyMode.Air:
                    if (Body.StepAir(dt, Dur(CatAnim.Land))) OnLanded();
                    break;
            }
        }

        void FallNow()
        {
            Body.StartFall(Body.Vwalk * 0.5);
            Nav.Invalidate();
            seqLen = 0;
            if (act == BrainAct.Nap || act == BrainAct.Idle || act == BrainAct.Petted || act == BrainAct.Entrance)
            {
                tumbleGroom = act == BrainAct.Nap || rng.NextDouble() < 0.4;
                loaf = false;
                SetAct(BrainAct.Tumble);
            }
        }

        void OnLanded()
        {
            if (Body.LandFallH > 25 * S || !Body.LandWasPlanned) fx.Add(CatEffect.Dust);
            Nav.OnLanded(Body.LandedKey, Body.LandJumpId, now);
            if (!Body.LandWasPlanned && prov != null && now - lastRescanReq > 5)
            {
                lastRescanReq = now;
                try { prov.RequestRescan(); } catch { }
            }
        }

        void OnJumpAborted()
        {
            if (Body.LostTime > 0) { FallNow(); return; }
            if (act == BrainAct.Play)
            {
                pounceBlockUntil = now + Rand(10, 20);
                SetPlayPhase(BrainPlayPhase.Watch);
            }
            if (act == BrainAct.Tumble) StartIdle(Rand(2, 4));
        }

        void ProcessPending(bool petNow)
        {
            if (pendingCall)
            {
                pendingCall = false;
                if (act == BrainAct.Nap && phase <= 2)
                {
                    wakeThenCall = true;
                    if (phase <= 1) NapWake();
                }
                else StartCall();
                return;
            }
            if (pendingWake)
            {
                pendingWake = false;
                if (act == BrainAct.Nap && phase <= 1) NapWake();
            }
            if (pendingSleep)
            {
                pendingSleep = false;
                if (act != BrainAct.Nap) { StartNapSeek(true); return; }
                if (phase >= 2) { StartNap(false); return; }
            }
            if (pendingMeow)
            {
                pendingMeow = false;
                if (act == BrainAct.Nap && phase <= 2) { wakeThenMeow = true; if (phase <= 1) NapWake(); }
                else { DoMeow(); return; }
            }
            if (petNow)
            {
                if (act == BrainAct.Nap)
                {
                    if (phase <= 1 && now - lastSleepHeart > 1.5) { lastSleepHeart = now; fx.Add(CatEffect.Heart); }
                }
                else if (act == BrainAct.Petted) lastPetT = now;
                else StartPetted();
            }
        }

        void Behave()
        {
            phaseT += dt;
            if (hidden && act != BrainAct.Nap && Body.Mode == BodyMode.Ground) StartNapHidden();
            switch (act)
            {
                case BrainAct.Entrance: BehaveEntrance(); break;
                case BrainAct.Idle: BehaveIdle(); break;
                case BrainAct.Travel: BehaveTravel(); break;
                case BrainAct.Nap: BehaveNap(); break;
                case BrainAct.Play: BehavePlay(); break;
                case BrainAct.Petted: BehavePetted(); break;
                case BrainAct.Tumble: BehaveTumble(); break;
                case BrainAct.Scratch: BehaveScratch(); break;
            }
        }

        void BehaveEntrance()
        {
            if (Body.Mode != BodyMode.Ground) return;
            bool done = Body.WalkToward(entranceX, BrainTune.WalkSpeed * S, dt);
            if (done || phaseT > 30)
            {
                StartIdle(Rand(3, 6));
                if (rng.NextDouble() < 0.5) SeqStart1(CatAnim.Meow, BrainThen.None);
            }
        }

        void StartIdle(double dur)
        {
            SetAct(BrainAct.Idle);
            Nav.Stop();
            loaf = false;
            idleUntil = now + dur;
            nextBlink = now + Rand(1.5, 5);
            pose = CatAnim.Sit;
        }

        void BehaveIdle()
        {
            if (Body.Mode != BodyMode.Ground) return;
            if (SeqTick()) return;
            if (loaf)
            {
                pose = CatAnim.Loaf;
                LookAtCursor(false);
                pose = CatAnim.Loaf;
                if (MaybeEngagePlay()) return;
                if (now >= idleUntil || sleepiness >= 1.0)
                {
                    bool doze = sleepiness >= 1.0 || (sleepiness > 0.6 && rng.NextDouble() < 0.6);
                    if (doze)
                    {
                        if (!settings.SleepOnFolders || OnFolder() || !FolderReachable()) { StartNap(true); return; }
                        loaf = false;
                        SeqStart1(CatAnim.WakeUp, BrainThen.NapSeek);
                        return;
                    }
                    loaf = false;
                    SeqStart1(CatAnim.WakeUp, BrainThen.None);
                    idleUntil = now + Dur(CatAnim.WakeUp) + Rand(2, 5);
                }
                return;
            }
            LookAtCursor(true);
            if (pose == CatAnim.Sit && now >= nextSlowBlink && CursorRestingNear())
            {
                nextSlowBlink = now + Rand(25, 70);
                nextBlink = now + Rand(3, 6);
                SeqStart1(CatAnim.SlowBlink, BrainThen.None);
                fx.Add(CatEffect.Heart);
                return;
            }
            if (pose == CatAnim.Sit && now >= nextBlink)
            {
                nextBlink = now + Rand(2, 6);
                SeqStart1(CatAnim.SitBlink, BrainThen.None);
                return;
            }
            if (MaybeEngagePlay()) return;
            if (Body.Ground != null && !Body.Ground.Usable) { StartDown(); return; }
            if (now >= idleUntil) Decide();
        }

        void Decide()
        {
            if (sleepiness >= 1.0) { StartNapSeek(false); return; }
            if (settings.ScratchIcons && now >= nextScratch && sleepiness < 0.85 && rng.NextDouble() < 0.35 && StartScratchSeek()) return;
            bool high = Body.GroundKind != PlatformKind.Floor;
            double r = rng.NextDouble();
            if (r < 0.09) { idleUntil = now + Rand(3, 9); MaybeMicro(); return; }
            r -= 0.09;
            if (r < 0.07) { SeqGroom(); idleUntil = now + Rand(3, 8); return; }
            r -= 0.07;
            if (r < 0.04) { SeqStart1(CatAnim.Yawn, BrainThen.None); idleUntil = now + Rand(3, 8); return; }
            r -= 0.04;
            double pLoaf = 0.07 + sleepiness * 0.08;
            if (r < pLoaf) { StartLoaf(); return; }
            BrainThen t;
            if (high && rng.NextDouble() < 0.5) t = BrainThen.Down;
            else t = rng.NextDouble() < 0.55 ? BrainThen.Wander : BrainThen.Explore;
            if (rng.NextDouble() < 0.3) { SeqStart1(CatAnim.Stretch, t); idleUntil = now + 30; }
            else DoThen(t);
        }

        void MaybeMicro()
        {
            double r = rng.NextDouble();
            if (r < 0.25) SeqGroom();
            else if (r < 0.33) SeqStart1(CatAnim.Yawn, BrainThen.None);
        }

        void StartLoaf()
        {
            loaf = true;
            idleUntil = now + Dur(CatAnim.LieDown) + Rand(6, 17);
            SeqStart1(CatAnim.LieDown, BrainThen.None);
        }

        void DoThen(BrainThen t)
        {
            switch (t)
            {
                case BrainThen.Wander: StartWander(); break;
                case BrainThen.Explore: StartExplore(); break;
                case BrainThen.Down: StartDown(); break;
                case BrainThen.NapSeek: StartNapSeek(false); break;
            }
        }

        void SeqStart1(CatAnim a, BrainThen then)
        {
            seq[0] = a; seqLen = 1; seqPos = 0; seqT = 0; seqThen = then;
            pose = a; animRestart = true;
            if (a == CatAnim.Meow) fx.Add(CatEffect.Meow);
        }

        void SeqGroom()
        {
            int n = rng.Next(2, 5);
            for (int i = 0; i < n; i++) seq[i] = CatAnim.Groom;
            seqLen = n; seqPos = 0; seqT = 0; seqThen = BrainThen.None;
            pose = CatAnim.Groom; animRestart = true;
        }

        bool SeqTick()
        {
            if (seqLen == 0) return false;
            seqT += dt;
            if (seqT >= Dur(seq[seqPos]))
            {
                seqPos++; seqT = 0;
                if (seqPos >= seqLen)
                {
                    seqLen = 0;
                    pose = loaf ? CatAnim.Loaf : CatAnim.Sit;
                    BrainThen t = seqThen; seqThen = BrainThen.None;
                    DoThen(t);
                    return act == BrainAct.Idle ? false : true;
                }
                animRestart = true;
                if (seq[seqPos] == CatAnim.Meow) fx.Add(CatEffect.Meow);
            }
            pose = seq[seqPos];
            return true;
        }

        void LookAtCursor(bool allowUp)
        {
            pose = CatAnim.Sit;
            double dx = cur.X - Body.X;
            int mc = World.MonitorAt(cur.X, cur.Y), mcat = World.MonitorAt(Body.X, Body.Y - 1);
            if (mc != mcat && Math.Abs(dx) > 500 * S) return;
            if (cur.StillFor(now) < 4 && Math.Abs(dx) > 8 * S && (dx < 0) != Body.FaceLeft && now - lastTurn > 0.7)
            {
                Body.FaceLeft = dx < 0; lastTurn = now;
            }
            if (allowUp && mc == mcat)
            {
                double hy = Body.Y - World.CatH;
                double hx = Body.X, ddx = cur.X - hx, ddy = cur.Y - hy;
                if (cur.Y < hy && ddx * ddx + ddy * ddy < 200 * S * 200 * S) pose = CatAnim.SitLookUp;
            }
        }

        void FaceCursor()
        {
            double dx = cur.X - Body.X;
            if (Math.Abs(dx) > 4 * S) Body.FaceLeft = dx < 0;
        }

        void ExploreFromHere()
        {
            if (Body.Ground != null) Path.ExploreAll(Body.Ground.Index, Body.X, now);
        }

        void StartTravel(BrainWhy w, BrainPlat p, double x, double speedU, double timeout)
        {
            SetAct(BrainAct.Travel);
            seqLen = 0; loaf = false;
            why = w;
            Nav.Start(p, x, speedU, now, timeout);
            Nav.IgnoreBans = w == BrainWhy.Call;
        }

        void StartWander()
        {
            BrainPlat g = Body.Ground;
            if (g == null) { StartIdle(Rand(1, 3)); return; }
            double lo, hi;
            Body.LiveWalkRange(out lo, out hi);
            bool high = Body.GroundKind != PlatformKind.Floor;
            if (hi - lo > 30 * S && (!high || rng.NextDouble() < 0.35) && rng.NextDouble() < 0.7)
            {
                double d = Rand(50, 260) * S, dir = rng.NextDouble() < 0.5 ? -1 : 1;
                double x = Body.X + dir * d;
                if (x < lo || x > hi) x = Body.X - dir * d;
                x = BrainWorld.Clamp(x, lo, hi);
                if (Math.Abs(x - Body.X) < 12 * S) { StartIdle(Rand(2, 5)); return; }
                StartTravel(BrainWhy.Wander, g, x, rng.NextDouble() < 0.15 ? BrainTune.RunSpeed : BrainTune.WalkSpeed, 30);
                return;
            }
            ExploreFromHere();
            BrainPlat pick = null; double total = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                double acc = 0, roll = rng.NextDouble() * total;
                for (int i = 0; i < World.Plats.Count; i++)
                {
                    BrainPlat b = World.Plats[i];
                    if (b == g || !b.Usable || !Path.Reachable(i) || b.P.Key == Body.GroundKey) continue;
                    double c = Path.Cost(i);
                    if (c > 2.6 || Path.Bans.TargetBanned(b.P.Key, now)) continue;
                    double wgt = 1.0 / (0.5 + c);
                    if (high && b.P.Y > Body.Y + 4 * S) wgt *= 3;
                    if (pass == 0) total += wgt;
                    else { acc += wgt; if (acc >= roll) { pick = b; break; } }
                }
                if (total <= 0) break;
            }
            if (pick == null) { StartIdle(Rand(2, 6)); return; }
            StartTravel(BrainWhy.Wander, pick, SpotOn(pick), BrainTune.WalkSpeed, 30);
        }

        void StartExplore()
        {
            ExploreFromHere();
            BrainPlat g = Body.Ground;
            BrainPlat pick = null; double total = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                double acc = 0, roll = rng.NextDouble() * total;
                for (int i = 0; i < World.Plats.Count; i++)
                {
                    BrainPlat b = World.Plats[i];
                    if (b == g || !b.Usable || b.P.Kind == PlatformKind.Floor || !Path.Reachable(i)) continue;
                    if (b.P.Key == Body.GroundKey) continue;
                    double c = Path.Cost(i);
                    if (c > 4.6 || Path.Bans.TargetBanned(b.P.Key, now)) continue;
                    double wgt = (b.IsFolder ? 1.6 : 1.0) * (b.P.Kind == PlatformKind.WindowTop ? 1.2 : 1.0) / (0.6 + c);
                    if (pass == 0) total += wgt;
                    else { acc += wgt; if (acc >= roll) { pick = b; break; } }
                }
                if (total <= 0) break;
            }
            if (pick == null) { StartWander(); return; }
            StartTravel(BrainWhy.Explore, pick, SpotOn(pick), rng.NextDouble() < 0.25 ? BrainTune.RunSpeed : BrainTune.WalkSpeed, 40);
        }

        void StartDown()
        {
            ExploreFromHere();
            BrainPlat g = Body.Ground;
            BrainPlat pick = null; double best = double.MaxValue;
            for (int i = 0; i < World.Plats.Count; i++)
            {
                BrainPlat b = World.Plats[i];
                if (b == g || !b.Usable || !Path.Reachable(i) || b.P.Y <= Body.Y + 4 * S) continue;
                if (Path.Bans.TargetBanned(b.P.Key, now)) continue;
                double score = Path.Cost(i) * (b.P.Kind == PlatformKind.Floor ? 0.6 : 1.0) + rng.NextDouble() * 0.8;
                if (score < best) { best = score; pick = b; }
            }
            if (pick != null) { StartTravel(BrainWhy.Down, pick, SpotOn(pick), BrainTune.WalkSpeed, 30); return; }
            if (g != null && !g.Usable) { HopDownCalm(); return; }
            StartWander();
        }

        double SpotOn(BrainPlat b)
        {
            if (b.P.Kind == PlatformKind.Icon || b.SHi - b.SLo < 20 * S) return (b.SLo + b.SHi) * 0.5;
            double x = Body.X + Rand(-160, 160) * S;
            return BrainWorld.Clamp(x, b.SLo, b.SHi);
        }

        void BehaveTravel()
        {
            if (why == BrainWhy.Call) CallRetarget();
            NavStatus st = Nav.Tick(now, dt, Dur(CatAnim.Crouch));
            if (st == NavStatus.Arrived) { OnArrive(); return; }
            if (st == NavStatus.Failed || st == NavStatus.None) { OnTravelFailed(); return; }
            if (Body.Mode == BodyMode.Ground && !Body.MovedThisTick) pose = CatAnim.Stand;
            if (why == BrainWhy.Wander || why == BrainWhy.Explore || why == BrainWhy.Down) MaybeEngagePlay();
        }

        void OnArrive()
        {
            switch (why)
            {
                case BrainWhy.NapSeek:
                    StartNap(false);
                    break;
                case BrainWhy.Call:
                    CallArrived();
                    break;
                case BrainWhy.Scratch:
                    StartScratch();
                    break;
                case BrainWhy.Explore:
                    StartIdle(Rand(3.5, 11));
                    if (rng.NextDouble() < 0.2) StartLoaf();
                    break;
                default:
                    StartIdle(Rand(3, 9));
                    break;
            }
        }

        void OnTravelFailed()
        {
            string key = Nav.GoalKey;
            Nav.Stop();
            if (why == BrainWhy.Call)
            {
                callOpen = false;
                FaceCursor();
                StartIdle(Rand(5, 9));
                SeqStart1(CatAnim.Meow, BrainThen.None);
                return;
            }
            // only ban a goal that failed at the scale it was chosen for; one that became unreachable because the
            // cat changed size meanwhile (settings / other monitor) is not the goal's fault
            if (!string.IsNullOrEmpty(key) && Nav.StartScale == World.Scale) Path.Bans.BanTargetEscalating(key, now, 40);
            if (why == BrainWhy.NapSeek) { StartNap(false); return; }
            if (why == BrainWhy.Scratch) nextScratch = now + Rand(60, 150);
            StartIdle(Rand(1.5, 4));
        }

        void HopDownCalm()
        {
            BrainPlat below = World.BelowAt(Body.X, Body.Y, 60 * S);
            tumbleGroom = false;
            SetAct(BrainAct.Tumble);
            if (below == null) { FallNow(); return; }
            double lx = BrainWorld.Clamp(Body.X, below.SLo, below.SHi);
            Body.BeginCrouch(below, lx, Dur(CatAnim.Crouch), 6);
        }

        void BehaveTumble()
        {
            if (Body.Mode != BodyMode.Ground) return;
            bool groom = tumbleGroom;
            tumbleGroom = false;
            StartIdle(Rand(2.5, 6));
            if (groom && rng.NextDouble() < 0.6) SeqGroom();
        }

        bool IsAsleep { get { return act == BrainAct.Nap && phase <= 1; } }

        bool OnFolder() { return Body.Ground != null && Body.Ground.IsFolder; }

        bool FolderReachable()
        {
            ExploreFromHere();
            for (int i = 0; i < World.Plats.Count; i++)
            {
                BrainPlat b = World.Plats[i];
                if (b.IsFolder && b.Usable && Path.Reachable(i) && !Path.Bans.TargetBanned(b.P.Key, now)) return true;
            }
            return false;
        }

        void StartNapSeek(bool command)
        {
            BrainPlat spot = null;
            if (settings.SleepOnFolders && Body.Ground != null)
            {
                ExploreFromHere();
                spot = PickNapSpot(true, command);
                if (spot == null && !command) spot = PickNapSpot(false, false);
            }
            if (spot != null)
            {
                StartTravel(BrainWhy.NapSeek, spot, (spot.SLo + spot.SHi) * 0.5, BrainTune.WalkSpeed, 60);
                return;
            }
            StartNap(false);
        }

        BrainPlat PickNapSpot(bool folders, bool command)
        {
            BrainPlat best = null; double bestScore = double.MaxValue;
            for (int i = 0; i < World.Plats.Count; i++)
            {
                BrainPlat b = World.Plats[i];
                if (b.P.Kind != PlatformKind.Icon || !b.Usable) continue;
                if (folders && !b.P.IsFolder) continue;
                bool here = b == Body.Ground;
                if (!here && !Path.Reachable(i)) continue;
                if (Path.Bans.TargetBanned(b.P.Key, now)) continue;
                double c = here ? 0 : Path.Cost(i);
                double dist = Math.Abs((b.SLo + b.SHi) * 0.5 - Body.X) + Math.Abs(b.P.Y - Body.Y);
                if (command && (c > 2.4 || dist > 260 * S)) continue;
                double score = c * 90 * S + dist * 0.35 + rng.NextDouble() * 70 * S;
                if (score < bestScore) { bestScore = score; best = b; }
            }
            return best;
        }

        void StartNap(bool fromLoaf)
        {
            SetAct(BrainAct.Nap);
            Nav.Stop(); seqLen = 0; loaf = false;
            napHopDown = false; wakeThenCall = false; wakeThenMeow = false;
            phase = fromLoaf ? 1 : 0;
            if (fromLoaf) EnterSleep();
            napKnead = !fromLoaf && rng.NextDouble() < 0.6;
            if (napKnead) { kneadUntil = now + Rand(1.8, 3.6); nextNote = now + Rand(0.4, 0.9); }
        }

        void StartNapHidden()
        {
            if (Body.NoClamp)
            {
                // hidden while still walking in: nobody sees it, so just be inside
                double lo, hi;
                Body.LiveWalkRange(out lo, out hi);
                Body.X = BrainWorld.Clamp(Body.X, lo, hi);
                Body.NoClamp = false;
            }
            SetAct(BrainAct.Nap);
            Nav.Stop(); seqLen = 0; loaf = false;
            napHopDown = false; wakeThenCall = false; wakeThenMeow = false;
            EnterSleep();
            napUntil = now + Rand(20, 90);
        }

        void EnterSleep()
        {
            phase = 1; phaseT = 0;
            napUntil = now + 40 + 200 * Math.Pow(rng.NextDouble(), 1.35);
            nextZ = now + Rand(0.8, 1.6);
        }

        void NapWake()
        {
            if (act != BrainAct.Nap) SetAct(BrainAct.Nap);
            if (phase == 0)
            {
                napKnead = false;
                bool call = wakeThenCall, meow = wakeThenMeow;
                wakeThenCall = false; wakeThenMeow = false;
                if (napHopDown && Body.LostTime > 0) { napHopDown = false; HopDownCalm(); }
                else { napHopDown = false; StartIdle(Rand(2, 5)); }
                if (call) pendingCall = true;
                else if (meow) pendingMeow = true;
                return;
            }
            phase = 2; phaseT = 0;
        }

        void BehaveNap()
        {
            if (Body.Mode != BodyMode.Ground) return;
            switch (phase)
            {
                case 0:
                    if (napKnead)
                    {
                        pose = CatAnim.Knead;
                        if (now >= nextNote) { fx.Add(CatEffect.Note); nextNote = now + Rand(0.8, 1.4); }
                        if (now >= kneadUntil) { napKnead = false; phaseT = 0; }
                        break;
                    }
                    pose = CatAnim.LieDown;
                    if (phaseT >= Dur(CatAnim.LieDown)) EnterSleep();
                    break;
                case 1:
                    pose = CatAnim.Sleep;
                    if (hidden) { if (napUntil < now + 5) napUntil = now + 5; }
                    else if (now >= nextZ) { fx.Add(CatEffect.Zzz); nextZ = now + Rand(1.3, 1.7); }
                    if (now >= napUntil) NapWake();
                    break;
                case 2:
                    pose = CatAnim.WakeUp;
                    if (phaseT >= Dur(CatAnim.WakeUp)) AfterWake();
                    break;
                default:
                    pose = napAnim;
                    if (phaseT >= Dur(napAnim)) StartIdle(Rand(3, 8));
                    break;
            }
        }

        void AfterWake()
        {
            sleepiness = Rand(0, 0.12);
            awakeSpan = Rand(350, 500);
            if (napHopDown && Body.LostTime > 0) { napHopDown = false; HopDownCalm(); return; }
            napHopDown = false;
            if (wakeThenCall) { wakeThenCall = false; StartCall(); return; }
            if (wakeThenMeow) { wakeThenMeow = false; DoMeow(); return; }
            phase = 3; phaseT = 0;
            napAnim = rng.NextDouble() < 0.6 ? CatAnim.Stretch : CatAnim.Yawn;
            pose = napAnim;
        }

        void StartCall()
        {
            fx.Add(CatEffect.Exclaim);
            callUntil = now + 25; callRetarget = now + 1.0;
            double gx;
            BrainPlat t = BestNearCursor(out gx);
            if (t == null) { callOpen = false; DoMeow(); return; }
            if (t == Body.Ground && Math.Abs(gx - Body.X) < 6 * S) { CallArrived(); return; }
            StartTravel(BrainWhy.Call, t, gx, BrainTune.CallSpeed, 25);
        }

        void CallRetarget()
        {
            if (now < callRetarget) return;
            callRetarget = now + 1.0;
            if (Body.Mode != BodyMode.Ground) return;
            double gx;
            BrainPlat t = BestNearCursor(out gx);
            if (t == null) return;
            if (t.P.Key == Nav.GoalKey) Nav.SetGoalX(t, gx);
            else if (now < callUntil - 1) Nav.Start(t, gx, BrainTune.CallSpeed, now, callUntil - now);
        }

        void CallArrived()
        {
            callOpen = false;
            StartIdle(Rand(6, 12));
            FaceCursor();
            SeqStart1(CatAnim.Meow, BrainThen.None);
        }

        void DoMeow()
        {
            StartIdle(Rand(3, 8));
            FaceCursor();
            SeqStart1(CatAnim.Meow, BrainThen.None);
        }

        void StartPetted()
        {
            SetAct(BrainAct.Petted);
            Nav.Stop(); seqLen = 0; loaf = false;
            lastPetT = now;
            petUntil = now + Rand(2, 4);
            nextNote = now + Rand(0.6, 1.2);
            petKnead = rng.NextDouble() < 0.3;
            fx.Add(CatEffect.Hearts);
        }

        void BehavePetted()
        {
            if (Body.Mode != BodyMode.Ground) return;
            pose = petKnead ? CatAnim.Knead : CatAnim.Happy;
            if (now - lastPetT < 0.3 && petUntil < now + 1.5) petUntil = now + 1.5;
            if (now >= nextNote)
            {
                fx.Add(rng.NextDouble() < 0.7 ? CatEffect.Note : CatEffect.Heart);
                nextNote = now + Rand(0.9, 1.7);
            }
            if (now >= petUntil)
            {
                sleepiness += 0.03;
                StartIdle(Rand(3, 8));
            }
        }

        void StartHeld()
        {
            SetAct(BrainAct.Held);
            Nav.Stop(); seqLen = 0; loaf = false;
            napHopDown = false; wakeThenCall = false; wakeThenMeow = false;
        }

        void HoldAt(Point c)
        {
            double dir = Body.FaceLeft ? -1 : 1;
            Body.Hold(c.X + dir * scruffOffX * S, c.Y + scruffOffY * S);
        }

        void ReleaseHeld(bool released, PointF v)
        {
            double vx = v.X, vy = v.Y;
            if (!released || double.IsNaN(vx) || double.IsNaN(vy) || double.IsInfinity(vx) || double.IsInfinity(vy))
                cur.Velocity(now, 0.08, out vx, out vy);
            double mag = Math.Sqrt(vx * vx + vy * vy), cap = BrainTune.ReleaseSpeedCap;
            if (mag > cap) { vx *= cap / mag; vy *= cap / mag; }
            Body.Release(vx, vy);
            tumbleGroom = true;
            SetAct(BrainAct.Tumble);
        }

        bool MaybeEngagePlay()
        {
            if (settings.Play == PlayMode.Never || hidden || sleepiness > 1.1) return false;
            if (Body.Mode != BodyMode.Ground || now < playCooldown || seqLen > 0) return false;
            if (settings.Play == PlayMode.Often)
            {
                if (cur.StillFor(now) < 0.2 && cur.PathLength(now, 0.8) > 60 * S) { StartPlay(); return true; }
                return false;
            }
            if (nearActive >= 1.0)
            {
                nearActive = 0;
                if (rng.NextDouble() < 0.5) { StartPlay(); return true; }
                playCooldown = now + Rand(25, 50);
            }
            return false;
        }

        void SetPlayCooldown()
        {
            playCooldown = now + (settings.Play == PlayMode.Often ? Rand(4, 10) : Rand(60, 180));
            nearActive = 0;
        }

        void StartPlay()
        {
            SetAct(BrainAct.Play);
            Nav.Stop(); seqLen = 0; loaf = false;
            playUntil = now + Rand(10, 40);
            playRetarget = 0;
            SetPlayPhase(BrainPlayPhase.Chase);
            if (rng.NextDouble() < 0.5) fx.Add(CatEffect.Exclaim);
        }

        void EndPlay(bool groom)
        {
            Nav.Stop();
            StartIdle(Rand(3, 8));
            if (groom && rng.NextDouble() < 0.7) SeqGroom();
        }

        void SetPlayPhase(BrainPlayPhase p) { playPhase = p; playPhaseT = 0; }

        bool CursorLow()
        {
            return cur.Y >= Body.Y - World.CatH * 1.3 && cur.Y <= Body.Y + 15 * S;
        }

        void BehavePlay()
        {
            if (Body.Mode != BodyMode.Ground) return;
            if (settings.Play == PlayMode.Never) { EndPlay(false); return; }
            playPhaseT += dt;
            bool bored = cur.StillFor(now) > 2.0;
            if ((bored || now >= playUntil || sleepiness > 1.2) && playPhase != BrainPlayPhase.Swat) { EndPlay(true); return; }
            switch (playPhase)
            {
                case BrainPlayPhase.Chase:
                {
                    if (now >= playRetarget)
                    {
                        playRetarget = now + 0.5;
                        double gx;
                        BrainPlat t = BestNearCursor(out gx);
                        if (t == null || t == Body.Ground) { Nav.Stop(); SetPlayPhase(BrainPlayPhase.Watch); PlayWatch(); return; }
                        if (Nav.Active && t.P.Key == Nav.GoalKey) Nav.SetGoalX(t, gx);
                        else Nav.Start(t, gx, BrainTune.ChaseSpeed, now, 15);
                    }
                    NavStatus st = Nav.Tick(now, dt, Dur(CatAnim.Crouch));
                    if (st != NavStatus.Moving) { Nav.Stop(); SetPlayPhase(BrainPlayPhase.Watch); }
                    else if (Body.Mode == BodyMode.Ground && !Body.MovedThisTick) { pose = CatAnim.Stand; FaceCursor(); }
                    break;
                }
                case BrainPlayPhase.Watch:
                    PlayWatch();
                    break;
                case BrainPlayPhase.Wiggle:
                    pose = CatAnim.Wiggle;
                    FaceCursor();
                    if (!CursorLow() || Math.Abs(cur.X - Body.X) > 70 * S) SetPlayPhase(BrainPlayPhase.Watch);
                    else if (playPhaseT >= swatReady) { if (now < pounceBlockUntil) StartSwat(); else Pounce(); }
                    break;
                case BrainPlayPhase.Pounce:
                    if (rng.NextDouble() < 0.4) StartSwat();
                    else SetPlayPhase(BrainPlayPhase.Watch);
                    break;
                case BrainPlayPhase.Swat:
                    pose = CatAnim.Swat;
                    if (playPhaseT >= Dur(CatAnim.Swat)) SetPlayPhase(BrainPlayPhase.Watch);
                    break;
            }
        }

        void PlayWatch()
        {
            FaceCursor();
            pose = CatAnim.Sit;
            if (now >= playRetarget)
            {
                playRetarget = now + 0.5;
                double gx;
                BrainPlat t = BestNearCursor(out gx);
                if (t != null && t != Body.Ground)
                {
                    SetPlayPhase(BrainPlayPhase.Chase);
                    Nav.Start(t, gx, BrainTune.ChaseSpeed, now, 15);
                    return;
                }
            }
            double lo, hi;
            Body.LiveWalkRange(out lo, out hi);
            double tx = BrainWorld.Clamp(cur.X, lo, hi);
            if (Math.Abs(tx - Body.X) > 30 * S) { Body.WalkToward(tx, BrainTune.ChaseSpeed * S, dt); return; }
            double cdx = Math.Abs(cur.X - Body.X);
            if (CursorLow() && cdx <= 60 * S)
            {
                if (cdx <= 10 * S) { if (playPhaseT > 0.6) StartSwat(); }
                else { SetPlayPhase(BrainPlayPhase.Wiggle); swatReady = Rand(0.5, 1.2); pose = CatAnim.Wiggle; }
                return;
            }
            double hy = Body.Y - World.CatH, ddx = cur.X - Body.X, ddy = cur.Y - hy;
            if (cur.Y < hy && ddx * ddx + ddy * ddy < 200 * S * 200 * S) pose = CatAnim.SitLookUp;
        }

        void StartSwat()
        {
            SetPlayPhase(BrainPlayPhase.Swat);
            FaceCursor();
            pose = CatAnim.Swat; animRestart = true;
        }

        void Pounce()
        {
            if (Body.Ground == null) { SetPlayPhase(BrainPlayPhase.Watch); return; }
            double lo, hi;
            Body.LiveSafeRange(out lo, out hi);
            double target = Body.X + BrainWorld.Clamp(cur.X - Body.X, -45 * S, 45 * S);
            target = BrainWorld.Clamp(target, lo, hi);
            if (Math.Abs(target - Body.X) < 5 * S) { StartSwat(); return; }
            double snapX = target;
            if (Body.Riding && Body.Ground.P.Bounds.Width > 0) snapX = target - Body.RideFrame.Left + Body.Ground.P.Bounds.Left;
            Body.BeginCrouch(Body.Ground, snapX, Dur(CatAnim.Crouch), 7);
            SetPlayPhase(BrainPlayPhase.Pounce);
        }

        BrainPlat BestNearCursor(out double gx)
        {
            gx = Body.X;
            BrainPlat g = Body.Ground;
            if (g == null) return null;
            ExploreFromHere();
            double cx = cur.X, cy = cur.Y;
            BrainPlat best = null; double bestScore = double.MaxValue;
            for (int i = 0; i < World.Plats.Count; i++)
            {
                BrainPlat b = World.Plats[i];
                bool here = b == g;
                if (!here && (!b.Usable || !Path.Reachable(i))) continue;
                double lo, hi;
                if (here) Body.LiveWalkRange(out lo, out hi);
                else { lo = b.SLo; hi = b.SHi; }
                double px = BrainWorld.Clamp(cx, lo, hi);
                double dx = Math.Abs(px - cx);
                double dy = (here ? Body.Y : b.P.Y) - cy;
                double score = dx + (dy >= -World.CatH * 0.3 ? dy * 0.5 : -dy * 2.0 + 40 * S);
                if (!here) score += Path.Cost(i) * 6 * S;           // a hop ~ 12 U of height: prefer what is really under the cursor
                if (score < bestScore) { bestScore = score; best = b; gx = px; }
            }
            return best;
        }

        const double ClawReachArt = 19.5;

        bool StartScratchSeek()
        {
            BrainPlat g = Body.Ground;
            if (g == null) return false;
            ExploreFromHere();
            BrainPlat pick = null;
            if (g.P.Kind == PlatformKind.Icon && g.Usable && !Path.Bans.TargetBanned(g.P.Key, now)) pick = g;
            else
            {
                double total = 0;
                for (int pass = 0; pass < 2 && pick == null; pass++)
                {
                    double acc = 0, roll = rng.NextDouble() * total;
                    for (int i = 0; i < World.Plats.Count; i++)
                    {
                        BrainPlat b = World.Plats[i];
                        if (b.P.Kind != PlatformKind.Icon || !b.Usable || !Path.Reachable(i)) continue;
                        double c = Path.Cost(i);
                        if (c > 3.6 || Path.Bans.TargetBanned(b.P.Key, now)) continue;
                        double wgt = (b.IsFolder ? 1.8 : 1.0) / (0.6 + c);
                        if (pass == 0) total += wgt;
                        else { acc += wgt; if (acc >= roll) { pick = b; break; } }
                    }
                    if (total <= 0) break;
                }
            }
            if (pick == null) { nextScratch = now + Rand(40, 90); return false; }
            // stand near the back edge of the icon so the outstretched front paws land on its picture
            scratchFaceLeft = rng.NextDouble() < 0.5;
            Rectangle pic = pick.P.Bounds;
            double reach = ClawReachArt * S, margin = (BrainTune.EdgeMargin + 1) * S;
            double x = scratchFaceLeft ? pic.Right - margin : pic.Left + margin;
            double claw = x + (scratchFaceLeft ? -reach : reach);
            if (claw < pic.Left || claw > pic.Right) x = pic.Left + pic.Width * 0.5;
            scratchSpotX = BrainWorld.Clamp(x, pick.SLo, pick.SHi);
            if (pick == g && Math.Abs(scratchSpotX - Body.X) < 2 * S) { StartScratch(); return true; }
            StartTravel(BrainWhy.Scratch, pick, scratchSpotX, BrainTune.WalkSpeed, 40);
            return true;
        }

        void StartScratch()
        {
            if (Body.Ground == null || Body.GroundKind != PlatformKind.Icon) { StartIdle(Rand(2, 5)); return; }
            SetAct(BrainAct.Scratch);
            Nav.Stop(); seqLen = 0; loaf = false;
            Body.FaceLeft = scratchFaceLeft;
            scratchUntil = now + Rand(3.0, 5.0);
            nextMark = now + Rand(0.5, 0.8);
            nextShred = now + 0.25;
            pose = CatAnim.Scratch;
        }

        void BehaveScratch()
        {
            if (Body.Mode != BodyMode.Ground) return;
            if (Body.Ground == null || Body.GroundKind != PlatformKind.Icon) { EndScratch(false); return; }
            pose = CatAnim.Scratch;
            clawX = Body.X + (Body.FaceLeft ? -ClawReachArt : ClawReachArt) * S;
            clawY = Body.Y;
            if (now >= nextShred)
            {
                fx.Add(CatEffect.Shred);
                nextShred = now + Rand(0.2, 0.35);
            }
            if (now >= nextMark)
            {
                markKey = Body.GroundKey;
                nextMark = now + Rand(0.9, 1.4);
            }
            if (now >= scratchUntil) EndScratch(true);
        }

        void EndScratch(bool finished)
        {
            nextScratch = now + (finished ? Rand(420, 900) : Rand(60, 150));
            StartIdle(Rand(3, 7));
            if (finished && rng.NextDouble() < 0.5) SeqGroom();
        }

        bool CursorRestingNear()
        {
            if (cur.StillFor(now) < 1.5) return false;
            double hx = Body.X, hy = Body.Y - World.CatH * 0.7;
            double dx = cur.X - hx, dy = cur.Y - hy;
            if (dx * dx + dy * dy > 170 * S * 170 * S) return false;
            if (World.MonitorAt(cur.X, cur.Y) != World.MonitorAt(Body.X, Body.Y - 1)) return false;
            return Math.Abs(dx) < 10 * S || (dx < 0) == Body.FaceLeft;
        }

        static BrainPosture StartPosture(CatAnim a)
        {
            switch (a)
            {
                case CatAnim.Sit: case CatAnim.SitBlink: case CatAnim.SitLookUp: case CatAnim.Happy: case CatAnim.Meow:
                case CatAnim.Yawn: case CatAnim.Swat: case CatAnim.Groom: case CatAnim.SlowBlink: case CatAnim.LieDown:
                    return BrainPosture.Sitting;
                case CatAnim.Stand: case CatAnim.Walk: case CatAnim.Run: case CatAnim.Knead: case CatAnim.Scratch:
                case CatAnim.Stretch: case CatAnim.Wiggle:
                    return BrainPosture.Standing;
                case CatAnim.Loaf: case CatAnim.Sleep: case CatAnim.WakeUp:
                    return BrainPosture.Lying;
                default:
                    return BrainPosture.None;
            }
        }

        static BrainPosture EndPosture(CatAnim a)
        {
            switch (a)
            {
                case CatAnim.Sit: case CatAnim.SitBlink: case CatAnim.SitLookUp: case CatAnim.Happy: case CatAnim.Meow:
                case CatAnim.Yawn: case CatAnim.Swat: case CatAnim.Groom: case CatAnim.SlowBlink: case CatAnim.WakeUp:
                case CatAnim.SitDown:
                    return BrainPosture.Sitting;
                case CatAnim.Stand: case CatAnim.Walk: case CatAnim.Run: case CatAnim.Knead: case CatAnim.Scratch:
                case CatAnim.Stretch: case CatAnim.Wiggle: case CatAnim.Land: case CatAnim.Crouch: case CatAnim.StandUp:
                    return BrainPosture.Standing;
                case CatAnim.Loaf: case CatAnim.Sleep: case CatAnim.LieDown:
                    return BrainPosture.Lying;
                default:
                    return BrainPosture.None;
            }
        }

        static CatAnim TransitionFor(CatAnim from, CatAnim to)
        {
            BrainPosture pf = EndPosture(from), pt = StartPosture(to);
            if (pf == BrainPosture.Standing && pt == BrainPosture.Sitting) return CatAnim.SitDown;
            if ((pf == BrainPosture.Sitting || pf == BrainPosture.Lying) && pt == BrainPosture.Standing) return CatAnim.StandUp;
            return to;
        }

        void UpdateDrives()
        {
            double hx = Body.X, hy = Body.Y - World.CatH * 0.5;
            double ddx = cur.X - hx, ddy = cur.Y - hy;
            bool near = ddx * ddx + ddy * ddy < 140 * S * 140 * S;
            if (cur.StillFor(now) < 0.3 && near) nearActive += dt;
            else nearActive = Math.Max(0, nearActive - 2 * dt);

            if (act == BrainAct.Nap || act == BrainAct.Held) return;
            double rate = 1.0 / awakeSpan;
            if (Body.Mode == BodyMode.Air || Body.MovedThisTick) rate *= 1.5;
            if (act == BrainAct.Play) rate *= 2.0;
            if (loaf) rate *= 1.3;
            sleepiness += rate * dt;
            if (sleepiness > 2) sleepiness = 2;
        }

        void Safety()
        {
            if (double.IsNaN(Body.X) || double.IsNaN(Body.Y) || double.IsInfinity(Body.X) || double.IsInfinity(Body.Y))
            {
                Respawn();
                return;
            }
            if (Body.Mode == BodyMode.Air && Body.AirTime > BrainTune.MaxAirTime) { Respawn(); return; }
            if (!Body.NoClamp && Body.Mode != BodyMode.Held && !World.InsideAny(Body.X, Body.Y - 1))
            {
                outsideT += dt;
                if (outsideT > 0.25) { outsideT = 0; Respawn(); }
            }
            else outsideT = 0;
        }

        void Respawn()
        {
            Nav.Stop(); seqLen = 0; loaf = false; inTrans = false; napKnead = false;
            if (World.Snap != null && Body.Respawn())
            {
                started = true;
                StartIdle(Rand(2, 5));
            }
        }

        void ResolveAnim()
        {
            CatAnim a;
            switch (Body.Mode)
            {
                case BodyMode.Held: a = CatAnim.Held; break;
                case BodyMode.Crouch: a = CatAnim.Crouch; break;
                case BodyMode.Air:
                    a = (!Body.Planned && Body.BigFall) ? CatAnim.Fall : (Body.Vy < 0 ? CatAnim.JumpUp : CatAnim.JumpFall);
                    break;
                case BodyMode.Land: a = CatAnim.Land; break;
                default:
                    if (Body.MovedThisTick) a = Math.Abs(Body.Vwalk) > BrainTune.RunAnimSpeed * S ? CatAnim.Run : CatAnim.Walk;
                    else a = pose;
                    break;
            }
            if (Body.Mode == BodyMode.Ground && act != BrainAct.Held && !Body.NoClamp)
            {
                if (inTrans)
                {
                    transT += dt;
                    if (transT < Dur(transAnim)) a = transAnim;
                    else { inTrans = false; lastTransEnd = now; animRestart = true; }
                }
                else if (a != anim && now - lastTransEnd > 0.35)
                {
                    CatAnim t = TransitionFor(anim, a);
                    if (t != a && (!Body.MovedThisTick || Body.GroundKey == preBehaveKey))
                    {
                        // the first step came straight out of a sitting pose: stand up first, then go
                        if (Body.MovedThisTick) { Body.X = preBehaveX; Body.MovedThisTick = false; Body.Vwalk = 0; }
                        inTrans = true; transAnim = t; transT = 0; a = t;
                    }
                }
            }
            else inTrans = false;
            if (a != anim || animRestart) { anim = a; animTime = 0; }
            else if ((a == CatAnim.Walk || a == CatAnim.Run) && Body.MovedThisTick)
            {
                // loops play in step with the ground speed so the paws do not skate
                double v = Math.Abs(Body.Vwalk) / S, nominal = a == CatAnim.Run ? BrainTune.RunSpeed : BrainTune.WalkSpeed;
                double k = v > 0 ? BrainWorld.Clamp(v / nominal, 0.75, 1.6) : 1;
                animTime += dt * k;
            }
            else animTime += dt;
            animRestart = false;
        }

        void FillOutput(CatFrameOutput o)
        {
            double x = Body.X, y = Body.Y;
            if (double.IsNaN(x) || double.IsInfinity(x)) x = 0;
            if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
            o.X = x; o.Y = y;
            o.Anim = anim; o.AnimTime = animTime; o.FaceLeft = Body.FaceLeft;
            o.Visible = !hidden;
            o.Sleeping = IsAsleep;
            if (o.Visible && settings.ShowEffects && fx.Count > 0) o.Effects.AddRange(fx);
            bool moving = Body.Mode != BodyMode.Ground || Body.MovedThisTick;
            o.WantsHighFps = o.Visible && (moving || fastAnim[(int)anim]);
            o.ScratchIconKey = markKey;
            bool scratching = act == BrainAct.Scratch;
            o.ScratchX = scratching ? clawX : x;
            o.ScratchY = scratching ? clawY : y;
        }
    }
}
