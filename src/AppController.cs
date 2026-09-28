using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DesktopCat
{
    public sealed class AppController : ApplicationContext, IAppCommands
    {
        public const int FastInterval = 15;
        public const int MediumInterval = 45;
        public const int SlowInterval = 66;
        public const int HiddenInterval = 250;

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly EventWaitHandle callEvent;
        RegisteredWaitHandle callWait;
        volatile bool callSignaled;

        CatSettings settings;
        IWorldProvider world;
        WorldOptions worldOpts;
        bool usingFallbackWorld;
        ICatSprites sprites;
        CatPalette spritePalette;
        ICatBrain brain;
        CatWindow window;
        TrayController tray;
        SettingsForm settingsForm;
        ScratchMarks scratches;

        CatFrameOutput lastOut;
        Point lastCursor;
        double lastTick, lastZOrder = -10, lastCursorMove = -10;
        bool cmdCall, cmdSleep, cmdWake, userHidden, exiting;
        int consecutiveErrors, worldErrors;
        long tickErrors;

        double Now { get { return clock.Elapsed.TotalSeconds; } }

        public AppController(EventWaitHandle callEvent)
        {
            this.callEvent = callEvent;
            CatLog.Write("start: DesktopCat pid " + Process.GetCurrentProcess().Id);

            bool firstRun = false;
            try { firstRun = !File.Exists(SettingsStore.FilePath); } catch { }
            settings = SettingsStore.Sanitize(SettingsStore.Load());

            window = new CatWindow(delegate { return clock.Elapsed.TotalSeconds; });
            IntPtr hwnd = window.Handle;        // create the native window now: the world options exclude its handle
            window.EffectsEnabled = settings.ShowEffects;
            try { scratches = new ScratchMarks(); }
            catch (Exception ex) { CatLog.Error("ScratchMarks", ex); scratches = null; }

            StartWorld();

            CatPalette pal = AppPalette.Resolve(settings);
            sprites = CreateSprites(pal);
            if (sprites == null)
            {
                pal = AppPalette.DefaultPalette();
                sprites = CreateSprites(pal);
            }
            if (sprites == null) throw new InvalidOperationException("no cat sprites could be created");
            spritePalette = pal;
            window.SetSprites(sprites);

            brain = CatBrainFactory.Create(sprites, settings.Clone());
            if (brain == null) throw new InvalidOperationException("CatBrainFactory.Create returned null");

            tray = new TrayController(this);
            window.MenuRequested += delegate (Point p) { tray.ShowMenuAt(p); };
            tray.SetIcon(MakeTrayIcon());
            tray.Show();

            SettingsStore.SyncAutostart(settings.Autostart);

            if (firstRun)
            {
                // saving creates the settings file, so the welcome is shown only once
                SettingsStore.Save(settings);
                tray.ShowWelcome();
            }

            if (callEvent != null)
            {
                try
                {
                    callWait = ThreadPool.RegisterWaitForSingleObject(callEvent,
                        delegate { callSignaled = true; }, null, Timeout.Infinite, false);
                }
                catch (Exception ex) { CatLog.Error("RegisterWaitForSingleObject", ex); }
            }

            lastTick = Now;
            timer.Interval = FastInterval;
            timer.Tick += OnTick;
            timer.Start();
            Application.ApplicationExit += delegate { Cleanup(); };
        }

        void StartWorld()
        {
            worldOpts = MakeWorldOptions();
            try
            {
                world = WorldFactory.Create();
                if (world == null) throw new InvalidOperationException("WorldFactory.Create returned null");
                world.Configure(worldOpts.Clone());
                world.Start();
                if (world.Current == null) throw new InvalidOperationException("world.Current is null after Start");
            }
            catch (Exception ex)
            {
                CatLog.Error("world start (using the fallback floor-only world)", ex);
                SwitchToFallbackWorld();
            }
        }

        void SwitchToFallbackWorld()
        {
            if (usingFallbackWorld) return;
            IWorldProvider old = world;
            usingFallbackWorld = true;
            world = new AppFallbackWorld();
            try { world.Configure(worldOpts.Clone()); world.Start(); } catch { }
            if (old != null) { try { old.Dispose(); } catch { } }
        }

        WorldOptions MakeWorldOptions()
        {
            WorldOptions o = new WorldOptions();
            o.IncludeWindowTops = settings.AlwaysOnTop && settings.WalkOnWindows;
            o.OccludeByWindows = settings.AlwaysOnTop;
            o.ExcludeWindows = window != null && window.IsHandleCreated ? new IntPtr[] { window.Handle } : new IntPtr[0];
            return o;
        }

        void ConfigureWorld()
        {
            WorldOptions o = MakeWorldOptions();
            bool same = worldOpts != null && worldOpts.IncludeWindowTops == o.IncludeWindowTops &&
                        worldOpts.OccludeByWindows == o.OccludeByWindows &&
                        worldOpts.ExcludeWindows.Length == o.ExcludeWindows.Length &&
                        (o.ExcludeWindows.Length == 0 || worldOpts.ExcludeWindows[0] == o.ExcludeWindows[0]);
            if (same) return;
            worldOpts = o;
            try { world.Configure(o.Clone()); } catch (Exception ex) { CatLog.Error("world.Configure", ex); }
        }

        static ICatSprites CreateSprites(CatPalette p)
        {
            try
            {
                ICatSprites s = CatSpriteFactory.Create(p.Clone());
                if (s == null) return null;
                if (s.FrameWidth <= 0 || s.FrameHeight <= 0) { s.Dispose(); return null; }
                return s;
            }
            catch (Exception ex)
            {
                CatLog.Error("CatSpriteFactory.Create(" + (p != null ? p.Name : "null") + ")", ex);
                return null;
            }
        }

        Icon MakeTrayIcon()
        {
            int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
            try
            {
                Icon ic = SpriteExport.MakeIcon(sprites, size);
                if (ic != null) return ic;
            }
            catch (Exception ex) { CatLog.Error("SpriteExport.MakeIcon", ex); }
            return AppIcons.FromSprites(sprites, size);
        }

        void RecreateSprites(CatPalette pal)
        {
            ICatSprites ns = CreateSprites(pal);
            if (ns == null) return;
            ICatSprites old = sprites;
            sprites = ns;
            spritePalette = pal;
            try { brain.SetSprites(ns); } catch (Exception ex) { CatLog.Error("brain.SetSprites", ex); }
            window.SetSprites(ns);
            tray.SetIcon(MakeTrayIcon());
            if (old != null) { try { old.Dispose(); } catch { } }
        }

        void RecreateBrain()
        {
            try
            {
                ICatBrain b = CatBrainFactory.Create(sprites, settings.Clone());
                if (b != null) { brain = b; CatLog.Write("brain recreated after repeated errors"); }
            }
            catch (Exception ex) { CatLog.Error("RecreateBrain", ex); }
        }

        void OnTick(object sender, EventArgs e)
        {
            if (exiting) return;
            try
            {
                Step();
                consecutiveErrors = 0;
            }
            catch (Exception ex)
            {
                tickErrors++;
                consecutiveErrors++;
                if (tickErrors <= 20 || tickErrors % 500 == 0) CatLog.Error("tick (#" + tickErrors + ")", ex);
                if (consecutiveErrors >= 50) { consecutiveErrors = 0; RecreateBrain(); }
                SetInterval(SlowInterval);
            }
        }

        WorldSnapshot Snapshot()
        {
            WorldSnapshot s = null;
            try { s = world.Current; }
            catch (Exception ex)
            {
                if (++worldErrors <= 5) CatLog.Error("world.Current", ex);
            }
            if (s == null)
            {
                if (++worldErrors > 30) SwitchToFallbackWorld();
                try { s = world.Current; } catch { }
            }
            else worldErrors = 0;
            return s;
        }

        void Step()
        {
            double now = Now;
            double dt = now - lastTick;
            if (dt < 0) dt = 0;
            if (dt > 0.1) dt = 0.1;
            lastTick = now;

            if (callSignaled)
            {
                callSignaled = false;
                CallCat();
            }

            if (userHidden)
            {
                window.SetShown(false);
                SetInterval(HiddenInterval);
                return;
            }

            WorldSnapshot snap = Snapshot();
            if (snap == null) { SetInterval(SlowInterval); return; }

            Point cur = AppNative.CursorPos();
            if (cur.X == int.MinValue) cur = lastCursor;   // no cursor position, e.g. on the secure desktop
            if (cur != lastCursor) lastCursorMove = now;
            lastCursor = cur;

            int scale = ComputeScale(snap);
            CatInput ci = new CatInput();
            ci.Dt = dt;
            ci.Cursor = cur;
            ci.World = snap;
            ci.Provider = world;
            ci.Scale = scale;
            window.PollInput(now, cur, ci);
            ci.CmdCall = cmdCall;
            ci.CmdSleep = cmdSleep;
            ci.CmdWake = cmdWake;
            cmdCall = cmdSleep = cmdWake = false;

            CatFrameOutput o = brain.Update(ci);
            if (o == null) throw new InvalidOperationException("brain.Update returned null");
            if (double.IsNaN(o.X) || double.IsInfinity(o.X) || double.IsNaN(o.Y) || double.IsInfinity(o.Y))
                throw new InvalidOperationException("brain.Update returned a non-finite position");
            lastOut = o;

            if (scratches != null)
            {
                try
                {
                    if (o.ScratchIconKey != null && settings.ScratchIcons) scratches.AddMark(o.ScratchIconKey, o.ScratchX, snap);
                    scratches.CatHwnd = window.Handle;
                    scratches.CatOnTop = settings.AlwaysOnTop;
                    scratches.Sync(snap, scale, now, true);
                }
                catch (Exception ex) { CatLog.Error("scratch marks", ex); }
            }

            if (o.Visible)
            {
                if (settings.ShowEffects && o.Effects != null)
                    for (int i = 0; i < o.Effects.Count; i++) window.SpawnEffect(o.Effects[i], o, scale);
                window.Present(o, scale, dt);
                if (window.SetShown(true)) ApplyZOrder(now);
                else if (now - lastZOrder >= 1.5) ApplyZOrder(now);
            }
            else window.SetShown(false);

            int interval = o.Visible ? SlowInterval : HiddenInterval;
            if (o.Visible)
            {
                // a cursor moving near the cat needs smooth sampling (hover, stroking); a resting one does not
                bool cursorActive = window.CursorNear(cur) && now - lastCursorMove < 0.6;
                if (o.WantsHighFps || window.Input.Pressed || window.Input.Dragging || cursorActive ||
                    (window.Effects.Alive && !window.Effects.OnlySlow))
                    interval = FastInterval;
                else if (window.Effects.Alive)
                    interval = MediumInterval;
            }
            SetInterval(interval);
        }

        int ComputeScale(WorldSnapshot snap)
        {
            double dpi = 1.0;
            try
            {
                if (snap.Monitors.Count > 0)
                {
                    int mi;
                    if (lastOut != null) mi = snap.MonitorIndexAt((int)Math.Round(lastOut.X), (int)Math.Round(lastOut.Y) - 1);
                    else { MonitorInfo pm = snap.PrimaryMonitor; mi = pm != null ? snap.Monitors.IndexOf(pm) : 0; }
                    if (mi >= 0 && mi < snap.Monitors.Count) dpi = snap.Monitors[mi].DpiScale;
                }
            }
            catch { }
            if (double.IsNaN(dpi) || dpi < 0.5 || dpi > 5) dpi = 1.0;
            return Math.Max(1, (int)Math.Round(settings.Size * dpi, MidpointRounding.AwayFromZero));
        }

        void SetInterval(int ms)
        {
            if (timer.Interval != ms) timer.Interval = ms;
        }

        void ApplyZOrder(double now)
        {
            lastZOrder = now;
            try { DesktopZOrder.Apply(window.Handle, settings.AlwaysOnTop); }
            catch (Exception ex)
            {
                if (zErrors++ < 5) CatLog.Error("DesktopZOrder.Apply", ex);
            }
            // in desktop mode the claw marks share the desktop layer with the cat: put them back under it
            if (!settings.AlwaysOnTop && scratches != null)
            {
                try { scratches.ReassertZ(); } catch (Exception ex) { if (zErrors++ < 5) CatLog.Error("ScratchMarks.ReassertZ", ex); }
            }
        }
        int zErrors;

        public CatSettings Settings { get { return settings; } }
        public bool CatSleeping { get { return lastOut != null && lastOut.Sleeping; } }
        public bool CatHidden { get { return userHidden; } }

        public int ScratchedIcons { get { return scratches != null ? scratches.IconCount : 0; } }

        public void ClearScratches()
        {
            if (scratches != null) scratches.Clear();
            tray.Refresh();
        }

        public void CallCat()
        {
            if (userHidden) { userHidden = false; tray.Refresh(); }
            cmdCall = true;
            SetInterval(FastInterval);
        }

        public void SleepOrWake()
        {
            if (CatSleeping) cmdWake = true; else cmdSleep = true;
        }

        public void ToggleHidden()
        {
            userHidden = !userHidden;
            if (userHidden) window.SetShown(false);
            else SetInterval(FastInterval);
            tray.Refresh();
        }

        public void OpenSettings()
        {
            try
            {
                if (settingsForm != null && !settingsForm.IsDisposed)
                {
                    if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
                    settingsForm.Activate();
                    return;
                }
                settingsForm = new SettingsForm(this);
                settingsForm.FormClosed += delegate
                {
                    SettingsForm f = settingsForm;
                    settingsForm = null;
                    if (f != null) f.Dispose();
                };
                settingsForm.Show();
                settingsForm.Activate();
            }
            catch (Exception ex) { CatLog.Error("OpenSettings", ex); }
        }

        public void ApplySettings(CatSettings ns)
        {
            if (ns == null) return;
            ns = SettingsStore.Sanitize(ns.Clone());
            bool autostartTurnedOn = ns.Autostart && !settings.Autostart;
            settings = ns;
            try
            {
                CatPalette np = AppPalette.Resolve(ns);
                if (!AppPalette.SameColors(np, spritePalette)) RecreateSprites(np);
            }
            catch (Exception ex) { CatLog.Error("ApplySettings/sprites", ex); }
            try { brain.ApplySettings(ns.Clone()); } catch (Exception ex) { CatLog.Error("brain.ApplySettings", ex); }
            window.EffectsEnabled = ns.ShowEffects;
            if (!ns.ShowEffects) window.Effects.Clear();
            ConfigureWorld();
            SettingsStore.SyncAutostart(ns.Autostart, autostartTurnedOn);
            if (window.IsShown) ApplyZOrder(Now);
            tray.Refresh();
            SettingsStore.Save(ns);
            SetInterval(FastInterval);
        }

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            CatLog.Write("exit requested");
            StartExitWatchdog();
            // hide at once but tear down on the next message-loop turn: Exit usually runs inside a menu item's
            // click handler, and disposing that menu while winforms is still processing the click throws
            try { timer.Stop(); } catch { }
            try { window.SetShown(false); } catch { }
            try { if (tray != null) tray.HideIcon(); } catch { }
            try
            {
                window.BeginInvoke((MethodInvoker)delegate { Cleanup(); ExitThread(); });
            }
            catch
            {
                Cleanup();
                ExitThread();
            }
        }

        // a hang during shutdown must never leave an invisible process holding the single-instance mutex
        static void StartExitWatchdog()
        {
            try
            {
                Thread t = new Thread(delegate ()
                {
                    Thread.Sleep(6000);
                    try { CatLog.Write("exit watchdog: shutdown took too long, terminating"); } catch { }
                    try { Process.GetCurrentProcess().Kill(); } catch { }
                });
                t.IsBackground = true;
                t.Name = "exit watchdog";
                t.Start();
            }
            catch { }
        }

        bool cleaned;
        void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            exiting = true;
            try { timer.Stop(); timer.Dispose(); } catch { }
            try { if (callWait != null) callWait.Unregister(null); } catch { }
            try { if (tray != null) tray.Dispose(); } catch { }
            try { if (settingsForm != null && !settingsForm.IsDisposed) settingsForm.Close(); } catch { }
            try { if (window != null) { window.SetShown(false); window.Dispose(); } } catch { }
            try { if (scratches != null) scratches.Dispose(); } catch (Exception ex) { CatLog.Error("scratches.Dispose", ex); }
            try { if (world != null) world.Dispose(); } catch (Exception ex) { CatLog.Error("world.Dispose", ex); }
            try { if (sprites != null) sprites.Dispose(); } catch { }
            CatLog.Write("stopped");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Cleanup();
            base.Dispose(disposing);
        }
    }

    internal static class AppIcons
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public static Icon FromSprites(ICatSprites s, int size)
        {
            try
            {
                using (Bitmap b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(b))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                        if (s != null)
                        {
                            Bitmap f = s.GetFrame(CatAnim.Sit, 0, false);
                            if (f != null) g.DrawImage(f, new Rectangle(0, 0, size, size));
                        }
                    }
                    IntPtr h = b.GetHicon();
                    Icon tmp = Icon.FromHandle(h);
                    Icon owned = (Icon)tmp.Clone();
                    tmp.Dispose();
                    DestroyIcon(h);
                    return owned;
                }
            }
            catch (Exception ex)
            {
                CatLog.Error("AppIcons.FromSprites", ex);
                return (Icon)SystemIcons.Application.Clone();
            }
        }
    }
}
