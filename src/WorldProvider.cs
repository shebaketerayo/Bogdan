using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace DesktopCat
{
    public static class WorldFactory
    {
        public static IWorldProvider Create()
        {
            return new WorldProvider();
        }
    }

    internal sealed class WorldProvider : IWorldProvider
    {
        public const int WindowPeriodMs = 150;
        public const int IconPeriodMs = 2000;
        const int IconMinGapMs = 250;
        const int IconCallTimeoutMs = 4000;
        const int IconStaleDropMs = 60000;

        volatile WorldSnapshot current;
        readonly object sync = new object();
        WorldOptions options = new WorldOptions();
        Thread winThread, iconThread;
        readonly AutoResetEvent winWake = new AutoResetEvent(false);
        readonly AutoResetEvent iconWake = new AutoResetEvent(false);
        readonly ManualResetEvent firstIcons = new ManualResetEvent(false);
        volatile bool stopping;
        bool started, disposed;

        volatile DesktopIconResult lastGoodIcons;
        long lastIconSuccessTicks;
        volatile bool iconsStale;
        volatile uint iconThreadId;
        long iconBusySince;
        long lastCancelTicks;
        int cancelledCalls;

        readonly WorldWindowScanner scanner = new WorldWindowScanner();
        long version;
        readonly Stopwatch clock = Stopwatch.StartNew();
        int logBudget = 40;

        public WorldProvider()
        {
            WorldSnapshot s = new WorldSnapshot();
            try
            {
                s.Monitors = WorldMonitors.Enumerate();
                s.VirtualScreen = WorldMonitors.VirtualScreen(s.Monitors);
                AddFloors(s);
            }
            catch (Exception ex) { CatLog.Error("WorldProvider()", ex); }
            current = s;
        }

        public WorldSnapshot Current { get { return current; } }

        public void Configure(WorldOptions opts)
        {
            if (opts == null) return;
            WorldOptions o = opts.Clone();
            if (o.ExcludeWindows == null) o.ExcludeWindows = new IntPtr[0];
            lock (sync)
            {
                if (SameOptions(options, o)) return;
                options = o;
            }
            SafeSet(winWake); // options only affect the windows pass
        }

        static bool SameOptions(WorldOptions a, WorldOptions b)
        {
            if (a == null || b == null) return false;
            if (a.IncludeWindowTops != b.IncludeWindowTops || a.OccludeByWindows != b.OccludeByWindows) return false;
            IntPtr[] x = a.ExcludeWindows ?? new IntPtr[0], y = b.ExcludeWindows ?? new IntPtr[0];
            if (x.Length != y.Length) return false;
            for (int i = 0; i < x.Length; i++) if (x[i] != y[i]) return false;
            return true;
        }

        WorldOptions Options { get { lock (sync) { return options; } } }

        public void Start()
        {
            lock (sync)
            {
                if (started || disposed) return;
                started = true;
            }
            try
            {
                iconThread = new Thread(IconLoop);
                iconThread.Name = "DesktopCat.WorldIcons";
                iconThread.IsBackground = true;
                iconThread.Priority = ThreadPriority.BelowNormal;
                iconThread.SetApartmentState(ApartmentState.STA);
                iconThread.Start();
                firstIcons.WaitOne(3000);
            }
            catch (Exception ex) { CatLog.Error("World.Start icons", ex); }
            try { WindowPass(); } catch (Exception ex) { CatLog.Error("World.Start scan", ex); }
            try
            {
                winThread = new Thread(WindowLoop);
                winThread.Name = "DesktopCat.World";
                winThread.IsBackground = true;
                winThread.Priority = ThreadPriority.BelowNormal;
                winThread.Start();
            }
            catch (Exception ex) { CatLog.Error("World.Start thread", ex); }
        }

        public void RequestRescan()
        {
            SafeSet(winWake);
            SafeSet(iconWake);
        }

        public bool TryGetWindowFrame(IntPtr hwnd, out Rectangle frame)
        {
            frame = Rectangle.Empty;
            try
            {
                if (hwnd == IntPtr.Zero || !WorldNative.IsWindow(hwnd) || !WorldNative.IsWindowVisible(hwnd) ||
                    WorldNative.IsIconic(hwnd) || WorldNative.IsCloaked(hwnd)) return false;
                WorldNative.RECT r;
                if (!WorldNative.GetFrame(hwnd, out r) || r.Width <= 0 || r.Height <= 0) return false;
                frame = r.ToRectangle();
                return true;
            }
            catch { return false; }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
            }
            stopping = true;
            SafeSet(winWake);
            SafeSet(iconWake);
            try { if (winThread != null && !winThread.Join(1500)) CatLog.Write("world: window thread did not stop in time"); } catch { }
            try
            {
                if (iconThread != null && !iconThread.Join(1500))
                {
                    uint tid = iconThreadId;
                    if (tid != 0) WorldNative.CoCancelCall(tid, 0);
                    if (!iconThread.Join(1000)) CatLog.Write("world: icon thread did not stop in time (Explorer busy?)");
                }
            }
            catch { }
        }

        void WindowLoop()
        {
            while (!stopping)
            {
                try { winWake.WaitOne(WindowPeriodMs); } catch { }
                if (stopping) break;
                try { WindowPass(); }
                catch (Exception ex) { LogThrottled("world window pass", ex); }
                try { Watchdog(); }
                catch (Exception ex) { LogThrottled("world watchdog", ex); }
            }
        }

        void WindowPass()
        {
            WorldOptions opt = Options;
            List<MonitorInfo> mons = WorldMonitors.Enumerate();
            WorldWindowScan ws = scanner.Scan(opt.ExcludeWindows, mons, true);
            DesktopIconResult icons = lastGoodIcons;
            bool iconsVisible = false;
            if (icons != null)
            {
                // cheap checks without messaging explorer: the listview is gone while explorer restarts,
                // and hidden when "show desktop icons" is off
                if (!WorldNative.IsWindow(icons.ListView) || iconsStale) icons = null;
                else iconsVisible = WorldNative.IsWindowVisible(icons.ListView);
            }
            WorldSnapshot s = Build(opt, mons, ws, icons, iconsVisible);
            WorldSnapshot prev = current;
            if (prev == null || !WorldSnapshotCompare.SameContent(prev, s))
            {
                s.Version = Interlocked.Increment(ref version);
                current = s;
            }
        }

        void Watchdog()
        {
            long busy = Interlocked.Read(ref iconBusySince);
            if (busy == 0) { lastCancelTicks = 0; }
            else
            {
                // a scan stuck in a call into a busy explorer: cancel the com call (the icon thread gets
                // RPC_E_CALL_CANCELED and retries later); re-issued every 2 s since a cancel can race the call boundaries
                long now = clock.ElapsedTicks;
                double ms = (now - busy) * 1000.0 / Stopwatch.Frequency;
                double sinceCancel = lastCancelTicks == 0 ? double.MaxValue : (now - lastCancelTicks) * 1000.0 / Stopwatch.Frequency;
                if (ms > IconCallTimeoutMs && sinceCancel > 2000)
                {
                    lastCancelTicks = now;
                    uint tid = iconThreadId;
                    if (tid != 0)
                    {
                        int hr = WorldNative.CoCancelCall(tid, 0);
                        cancelledCalls++;
                        if (cancelledCalls <= 20 || cancelledCalls % 100 == 0)
                            CatLog.Write("world: icon scan busy for " + (int)ms + " ms, cancelling the COM call (hr=0x" + hr.ToString("X8") + ")");
                    }
                }
            }
            // keep publishing the last good icons while explorer is merely busy; give up if it stays unreachable too long
            long ok = Interlocked.Read(ref lastIconSuccessTicks);
            if (ok != 0 && lastGoodIcons != null)
            {
                double since = (clock.ElapsedTicks - ok) * 1000.0 / Stopwatch.Frequency;
                iconsStale = since > IconStaleDropMs;
            }
        }

        void IconLoop()
        {
            DesktopIconReader reader = null;
            try
            {
                iconThreadId = WorldNative.GetCurrentThreadId();
                try { WorldNative.CoEnableCallCancellation(IntPtr.Zero); } catch { }
                reader = new DesktopIconReader();
                long lastScan = 0;
                while (!stopping)
                {
                    int wait = IconPeriodMs;
                    try
                    {
                        long now = clock.ElapsedTicks;
                        double gap = (now - lastScan) * 1000.0 / Stopwatch.Frequency;
                        if (lastScan != 0 && gap < IconMinGapMs) Thread.Sleep(IconMinGapMs - (int)gap);
                        if (stopping) break;
                        Interlocked.Exchange(ref iconBusySince, clock.ElapsedTicks);
                        DesktopIconResult r;
                        try { r = reader.Scan(); }
                        finally { Interlocked.Exchange(ref iconBusySince, 0); }
                        lastScan = clock.ElapsedTicks;
                        if (r.Ok)
                        {
                            lastGoodIcons = r;
                            iconsStale = false;
                            Interlocked.Exchange(ref lastIconSuccessTicks, clock.ElapsedTicks);
                            if (reader.ShapesPending) wait = 150;
                        }
                        else
                        {
                            if (reader.ConsecutiveFailures == 1 || reader.ConsecutiveFailures % 30 == 0)
                                CatLog.Write("world: desktop icon scan failed (" + reader.ConsecutiveFailures + "x): " + r.Error);
                            if (lastGoodIcons == null) Interlocked.Exchange(ref lastIconSuccessTicks, clock.ElapsedTicks);
                            wait = reader.ConsecutiveFailures <= 15 ? 1000 : 5000;
                        }
                        SafeSet(firstIcons);
                        SafeSet(winWake);
                    }
                    catch (Exception ex)
                    {
                        LogThrottled("world icon pass", ex);
                        SafeSet(firstIcons);
                        wait = 3000;
                    }
                    if (stopping) break;
                    try { iconWake.WaitOne(wait); } catch { }
                }
            }
            catch (Exception ex) { LogThrottled("world icon thread", ex); }
            finally
            {
                SafeSet(firstIcons);
                if (reader != null) { try { reader.Dispose(); } catch { } }
            }
        }

        internal static WorldSnapshot Build(WorldOptions opt, List<MonitorInfo> mons, WorldWindowScan ws, DesktopIconResult icons, bool iconsVisible)
        {
            WorldSnapshot s = new WorldSnapshot();
            s.Monitors = mons;
            s.VirtualScreen = WorldMonitors.VirtualScreen(mons);
            s.ForegroundWindow = ws != null ? ws.Foreground : IntPtr.Zero;
            s.FullscreenAppActive = ws != null && ws.Fullscreen;
            AddFloors(s);
            List<WorldWindowInfo> wins = ws != null ? ws.Windows : new List<WorldWindowInfo>();
            bool occlude = opt.OccludeByWindows;

            if (icons != null && icons.Ok && iconsVisible)
            {
                s.DesktopIconsVisible = true;
                Dictionary<string, int> seen = new Dictionary<string, int>();
                for (int i = 0; i < icons.Items.Count; i++)
                {
                    DesktopIconItem it = icons.Items[i];
                    string name = it.Name ?? "";
                    int n;
                    seen.TryGetValue(name, out n);
                    n++;
                    seen[name] = n;
                    string key = "icon:" + name + (n > 1 ? "#" + n : "");
                    Rectangle pic = it.Picture;
                    int mi = MonitorContaining(mons, pic.Left + pic.Width / 2, pic.Top + pic.Height / 2);
                    if (mi < 0) continue;
                    int left = pic.Left, right = pic.Right, y = it.SupportY;
                    s.DesktopIcons.Add(IconPlatform(key, it, left, right, y, mi)); // unclipped: decorations need the whole icon
                    if (occlude)
                    {
                        List<int[]> segs = WorldWindowScanner.VisibleSegments(left, right, y, wins, int.MaxValue, 24);
                        for (int k = 0; k < segs.Count; k++) s.Platforms.Add(IconPlatform(key, it, segs[k][0], segs[k][1], y, mi));
                    }
                    else s.Platforms.Add(IconPlatform(key, it, left, right, y, mi));
                }
            }

            if (opt.IncludeWindowTops)
            {
                for (int i = 0; i < wins.Count; i++)
                {
                    WorldWindowInfo w = wins[i];
                    if (!w.Platform) continue;
                    string key = "win:" + w.Hwnd.ToInt64().ToString("X");
                    int y = w.Frame.Top;
                    for (int m = 0; m < mons.Count; m++)
                    {
                        Rectangle mb = mons[m].Bounds;
                        if (y < mb.Top || y >= mb.Bottom) continue;
                        int l = Math.Max(w.Frame.Left, mb.Left), r = Math.Min(w.Frame.Right, mb.Right);
                        if (r - l < 24) continue;
                        if (occlude)
                        {
                            List<int[]> segs = WorldWindowScanner.VisibleSegments(l, r, y, wins, w.Z, 24);
                            for (int k = 0; k < segs.Count; k++) s.Platforms.Add(WindowPlatform(key, w, segs[k][0], segs[k][1], y, m));
                        }
                        else s.Platforms.Add(WindowPlatform(key, w, l, r, y, m));
                    }
                }
            }
            return s;
        }

        static void AddFloors(WorldSnapshot s)
        {
            for (int i = 0; i < s.Monitors.Count; i++)
            {
                MonitorInfo m = s.Monitors[i];
                Platform p = new Platform();
                p.Kind = PlatformKind.Floor;
                p.Key = "floor:" + m.DeviceName;
                p.Left = m.WorkArea.Left;
                p.Right = m.WorkArea.Right;
                p.Y = m.WorkArea.Bottom;
                p.Name = m.DeviceName ?? "";
                p.Bounds = m.WorkArea;
                p.MonitorIndex = i;
                s.Platforms.Add(p);
            }
        }

        static Platform IconPlatform(string key, DesktopIconItem it, int l, int r, int y, int mi)
        {
            Platform p = new Platform();
            p.Kind = PlatformKind.Icon;
            p.Key = key;
            p.Left = l; p.Right = r; p.Y = y;
            p.Name = it.Name ?? "";
            p.IsFolder = it.IsFolder;
            p.Bounds = it.Picture;
            p.MonitorIndex = mi;
            return p;
        }

        static Platform WindowPlatform(string key, WorldWindowInfo w, int l, int r, int y, int mi)
        {
            Platform p = new Platform();
            p.Kind = PlatformKind.WindowTop;
            p.Key = key;
            p.Left = l; p.Right = r; p.Y = y;
            p.Hwnd = w.Hwnd;
            p.Name = w.Title ?? "";
            p.Bounds = w.Frame;
            p.MonitorIndex = mi;
            return p;
        }

        static int MonitorContaining(List<MonitorInfo> mons, int x, int y)
        {
            for (int i = 0; i < mons.Count; i++) if (mons[i].Bounds.Contains(x, y)) return i;
            return -1;
        }

        void LogThrottled(string where, Exception ex)
        {
            if (logBudget <= 0) return;
            logBudget--;
            CatLog.Error(where, ex);
        }

        static void SafeSet(EventWaitHandle e)
        {
            try { e.Set(); } catch { }
        }
    }

    internal static class WorldSnapshotCompare
    {
        public static bool SameContent(WorldSnapshot a, WorldSnapshot b)
        {
            if (a.FullscreenAppActive != b.FullscreenAppActive || a.DesktopIconsVisible != b.DesktopIconsVisible ||
                a.ForegroundWindow != b.ForegroundWindow || a.VirtualScreen != b.VirtualScreen) return false;
            if (a.Monitors.Count != b.Monitors.Count || a.Platforms.Count != b.Platforms.Count) return false;
            for (int i = 0; i < a.Monitors.Count; i++)
            {
                MonitorInfo x = a.Monitors[i], y = b.Monitors[i];
                if (x.DeviceName != y.DeviceName || x.Bounds != y.Bounds || x.WorkArea != y.WorkArea || x.DpiScale != y.DpiScale || x.Primary != y.Primary) return false;
            }
            for (int i = 0; i < a.Platforms.Count; i++)
            {
                Platform x = a.Platforms[i], y = b.Platforms[i];
                if (x.Kind != y.Kind || x.Left != y.Left || x.Right != y.Right || x.Y != y.Y || x.Hwnd != y.Hwnd ||
                    x.IsFolder != y.IsFolder || x.Bounds != y.Bounds || x.MonitorIndex != y.MonitorIndex ||
                    x.Key != y.Key || x.Name != y.Name) return false;
            }
            if (a.DesktopIcons.Count != b.DesktopIcons.Count) return false;
            for (int i = 0; i < a.DesktopIcons.Count; i++)
            {
                Platform x = a.DesktopIcons[i], y = b.DesktopIcons[i];
                if (x.Bounds != y.Bounds || x.Y != y.Y || x.Key != y.Key) return false;
            }
            return true;
        }
    }

    internal static class WorldMonitors
    {
        public static List<MonitorInfo> Enumerate()
        {
            List<MonitorInfo> list = new List<MonitorInfo>(2);
            WorldNative.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, IntPtr rc, IntPtr data)
            {
                try
                {
                    WorldNative.MONITORINFOEX mi = new WorldNative.MONITORINFOEX();
                    mi.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(WorldNative.MONITORINFOEX));
                    if (WorldNative.GetMonitorInfo(hMon, ref mi))
                    {
                        MonitorInfo m = new MonitorInfo();
                        m.DeviceName = mi.szDevice ?? "";
                        m.Bounds = mi.rcMonitor.ToRectangle();
                        m.WorkArea = mi.rcWork.ToRectangle();
                        m.Primary = (mi.dwFlags & WorldNative.MONITORINFOF_PRIMARY) != 0;
                        m.DpiScale = WorldDpi.ForMonitor(hMon);
                        if (m.Bounds.Width > 0 && m.Bounds.Height > 0) list.Add(m);
                    }
                }
                catch { }
                return true;
            };
            try { WorldNative.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero); } catch { }
            GC.KeepAlive(cb);
            if (list.Count == 0)
            {
                MonitorInfo m = new MonitorInfo();
                m.DeviceName = "primary";
                m.Bounds = new Rectangle(0, 0, Math.Max(1, WorldNative.GetSystemMetrics(0)), Math.Max(1, WorldNative.GetSystemMetrics(1)));
                m.WorkArea = m.Bounds;
                m.Primary = true;
                m.DpiScale = 1.0;
                list.Add(m);
            }
            list.Sort(delegate(MonitorInfo a, MonitorInfo b)
            {
                if (a.Primary != b.Primary) return a.Primary ? -1 : 1;
                int c = a.Bounds.Left.CompareTo(b.Bounds.Left);
                return c != 0 ? c : a.Bounds.Top.CompareTo(b.Bounds.Top);
            });
            return list;
        }

        public static Rectangle VirtualScreen(List<MonitorInfo> mons)
        {
            Rectangle r = Rectangle.Empty;
            for (int i = 0; i < mons.Count; i++) r = i == 0 ? mons[i].Bounds : Rectangle.Union(r, mons[i].Bounds);
            return r;
        }
    }
}
