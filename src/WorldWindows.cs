using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Text;

namespace DesktopCat
{
    internal sealed class WorldWindowInfo
    {
        public IntPtr Hwnd;
        public int Z;
        public string Class = "";
        public string Title = "";
        public uint Style, ExStyle, Pid;
        public Rectangle Frame;
        public bool Minimized, Maximized, Cloaked, Topmost;
        public bool Occluder;
        public bool Platform;
    }

    internal sealed class WorldWindowScan
    {
        public List<WorldWindowInfo> Windows = new List<WorldWindowInfo>(); // visible windows only, z-order top to bottom
        public IntPtr Foreground;
        public bool Fullscreen;
    }

    internal sealed class WorldWindowScanner
    {
        public const int MinTopHeadroom = 40;
        public const int MinPlatformWidth = 160, MinPlatformHeight = 100;

        static readonly string[] ShellClasses = {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "SHELLDLL_DefView",
        };
        // shell ui that may occlude while shown but must never become a platform
        static readonly string[] NoPlatformClasses = {
            "Windows.UI.Core.CoreWindow", "Xaml_WindowedPopupClass", "TopLevelWindowForOverflowXamlIsland",
            "NotifyIconOverflowWindow", "#32768", "tooltips_class32", "ForegroundStaging", "MultitaskingViewFrame",
            "TaskListThumbnailWnd", "XamlExplorerHostIslandWindow", "XamlExplorerHostIslandWindow_WASDK",
            "Shell_InputSwitchTopLevelWindow", "SysShadow", "Ghost",
        };

        readonly List<IntPtr> hwnds = new List<IntPtr>(512);
        readonly StringBuilder sb = new StringBuilder(512);
        readonly WorldNative.EnumWindowsProc enumProc;
        readonly uint ourPid;

        public WorldWindowScanner()
        {
            enumProc = OnEnum;
            ourPid = (uint)Process.GetCurrentProcess().Id;
        }

        bool OnEnum(IntPtr h, IntPtr l) { hwnds.Add(h); return hwnds.Count < 20000; }

        public WorldWindowScan Scan(IntPtr[] exclude, List<MonitorInfo> monitors, bool detectFullscreen)
        {
            WorldWindowScan res = new WorldWindowScan();
            hwnds.Clear();
            WorldNative.EnumWindows(enumProc, IntPtr.Zero);
            res.Foreground = WorldNative.GetForegroundWindow();
            for (int i = 0; i < hwnds.Count; i++)
            {
                IntPtr h = hwnds[i];
                if (!WorldNative.IsWindowVisible(h)) continue;
                WorldWindowInfo w = new WorldWindowInfo();
                w.Hwnd = h;
                w.Z = i;
                Classify(w, exclude, monitors);
                res.Windows.Add(w);
            }
            if (detectFullscreen) DetectFullscreen(res, exclude, monitors);
            return res;
        }

        void Classify(WorldWindowInfo w, IntPtr[] exclude, List<MonitorInfo> monitors)
        {
            IntPtr h = w.Hwnd;
            WorldNative.GetWindowThreadProcessId(h, out w.Pid);
            w.Style = WorldNative.GetStyle(h);
            w.ExStyle = WorldNative.GetExStyle(h);
            w.Topmost = (w.ExStyle & WorldNative.WS_EX_TOPMOST) != 0;
            w.Class = WorldNative.ClassName(h, sb);
            if (w.Pid == ourPid || Contains(exclude, h)) return;
            w.Title = WorldNative.WindowText(h, sb);
            w.Minimized = WorldNative.IsIconic(h);
            w.Maximized = WorldNative.IsZoomed(h);
            w.Cloaked = WorldNative.IsCloaked(h);
            WorldNative.RECT fr;
            if (WorldNative.GetFrame(h, out fr)) w.Frame = fr.ToRectangle();

            if (w.Cloaked) return;
            if (w.Minimized) return;
            if (IsIn(ShellClasses, w.Class)) return;
            if (w.Frame.Width <= 2 || w.Frame.Height <= 2) return;
            if (!OnAnyMonitor(w.Frame, monitors)) return;
            if ((w.ExStyle & WorldNative.WS_EX_TRANSPARENT) != 0) return;
            if ((w.ExStyle & WorldNative.WS_EX_LAYERED) != 0)
            {
                uint key, fl; byte alpha;
                if (WorldNative.GetLayeredWindowAttributes(h, out key, out alpha, out fl) && (fl & WorldNative.LWA_ALPHA) != 0 && alpha == 0)
                    return;
            }
            bool coversMonitor = CoversAMonitor(w.Frame, monitors);
            bool toolish = (w.ExStyle & (WorldNative.WS_EX_TOOLWINDOW | WorldNative.WS_EX_NOACTIVATE)) != 0;
            if (coversMonitor && w.Topmost && toolish && (w.ExStyle & WorldNative.WS_EX_LAYERED) != 0) return;
            if (coversMonitor && w.Title.Length == 0 && (w.ExStyle & WorldNative.WS_EX_NOACTIVATE) != 0) return;

            w.Occluder = true;

            bool tool = (w.ExStyle & WorldNative.WS_EX_TOOLWINDOW) != 0;
            bool app = (w.ExStyle & WorldNative.WS_EX_APPWINDOW) != 0;
            IntPtr owner = WorldNative.GetWindow(h, WorldNative.GW_OWNER);
            if (w.Title.Length == 0) return;
            if (tool && !app) return;
            if (owner != IntPtr.Zero && !app) return;
            if (IsIn(NoPlatformClasses, w.Class)) return;
            if (w.Frame.Width < MinPlatformWidth || w.Frame.Height < MinPlatformHeight) return;
            if (w.Maximized) return;
            if (coversMonitor) return;
            int mi = MonitorOfTopEdge(w.Frame, monitors);
            if (mi < 0) return;
            Rectangle mb = monitors[mi].Bounds;
            if (w.Frame.Top - mb.Top < MinTopHeadroom) return;
            if (w.Frame.Top >= monitors[mi].WorkArea.Bottom) return;
            w.Platform = true;
        }

        void DetectFullscreen(WorldWindowScan res, IntPtr[] exclude, List<MonitorInfo> monitors)
        {
            int st = 0;
            try { if (WorldNative.SHQueryUserNotificationState(out st) != 0) st = 0; } catch { st = 0; }
            // 2 = QUNS_BUSY, 3 = QUNS_RUNNING_D3D_FULL_SCREEN, 4 = QUNS_PRESENTATION_MODE
            if (st == 2 || st == 3 || st == 4) { res.Fullscreen = true; return; }
            IntPtr fg = res.Foreground;
            if (fg == IntPtr.Zero) return;
            WorldWindowInfo fw = null;
            for (int i = 0; i < res.Windows.Count; i++) if (res.Windows[i].Hwnd == fg) { fw = res.Windows[i]; break; }
            if (fw == null)
            {
                // the foreground may be a popup we skipped; resolve it to its root owner
                IntPtr root = WorldNative.GetAncestor(fg, WorldNative.GA_ROOTOWNER);
                for (int i = 0; i < res.Windows.Count; i++) if (res.Windows[i].Hwnd == root) { fw = res.Windows[i]; break; }
                if (fw == null) return;
            }
            if (fw.Pid == ourPid || Contains(exclude, fw.Hwnd) || fw.Cloaked || fw.Minimized) return;
            if (IsIn(ShellClasses, fw.Class) || IsIn(NoPlatformClasses, fw.Class)) return;
            WorldNative.RECT wr;
            if (!WorldNative.GetWindowRect(fw.Hwnd, out wr)) return;
            if (FullscreenMonitor(fw, wr.ToRectangle(), monitors) >= 0) res.Fullscreen = true;
        }

        internal static int FullscreenMonitor(WorldWindowInfo fw, Rectangle windowRect, List<MonitorInfo> monitors)
        {
            // a maximized captioned window also covers the monitor when the taskbar auto-hides, but it is not fullscreen
            if (fw.Maximized && (fw.Style & WorldNative.WS_CAPTION) == WorldNative.WS_CAPTION) return -1;
            Rectangle r = windowRect;
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle mb = monitors[i].Bounds;
                if (r.Left <= mb.Left && r.Top <= mb.Top && r.Right >= mb.Right && r.Bottom >= mb.Bottom &&
                    fw.Frame.Left <= mb.Left + 1 && fw.Frame.Top <= mb.Top + 1 && fw.Frame.Right >= mb.Right - 1 && fw.Frame.Bottom >= mb.Bottom - 1)
                    return i;
            }
            return -1;
        }

        public static List<int[]> VisibleSegments(int left, int right, int y, List<WorldWindowInfo> windows, int zLimit, int minWidth)
        {
            List<int[]> segs = new List<int[]>(2);
            if (right - left <= 0) return segs;
            segs.Add(new int[] { left, right });
            for (int i = 0; i < windows.Count && segs.Count > 0; i++)
            {
                WorldWindowInfo o = windows[i];
                if (o.Z >= zLimit) break;
                if (!o.Occluder) continue;
                Rectangle f = o.Frame;
                if (y < f.Top || y >= f.Bottom) continue;
                for (int s = segs.Count - 1; s >= 0; s--)
                {
                    int a = segs[s][0], b = segs[s][1];
                    if (f.Right <= a || f.Left >= b) continue;
                    segs.RemoveAt(s);
                    if (f.Left > a) segs.Insert(s, new int[] { a, f.Left });
                    if (f.Right < b) segs.Insert(f.Left > a ? s + 1 : s, new int[] { f.Right, b });
                }
            }
            for (int s = segs.Count - 1; s >= 0; s--) if (segs[s][1] - segs[s][0] < minWidth) segs.RemoveAt(s);
            segs.Sort(delegate(int[] p, int[] q) { return p[0].CompareTo(q[0]); });
            return segs;
        }

        static bool Contains(IntPtr[] arr, IntPtr h)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i] == h) return true;
            return false;
        }

        static bool IsIn(string[] arr, string s)
        {
            for (int i = 0; i < arr.Length; i++) if (string.Equals(arr[i], s, StringComparison.Ordinal)) return true;
            return false;
        }

        static bool OnAnyMonitor(Rectangle r, List<MonitorInfo> monitors)
        {
            for (int i = 0; i < monitors.Count; i++) if (monitors[i].Bounds.IntersectsWith(r)) return true;
            return false;
        }

        static bool CoversAMonitor(Rectangle r, List<MonitorInfo> monitors)
        {
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle m = monitors[i].Bounds;
                if (r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom) return true;
            }
            return false;
        }

        public static int MonitorOfTopEdge(Rectangle f, List<MonitorInfo> monitors)
        {
            int cx = f.Left + f.Width / 2;
            for (int i = 0; i < monitors.Count; i++) if (monitors[i].Bounds.Contains(cx, f.Top)) return i;
            for (int i = 0; i < monitors.Count; i++)
            {
                Rectangle m = monitors[i].Bounds;
                if (f.Top >= m.Top && f.Top < m.Bottom && f.Right > m.Left && f.Left < m.Right) return i;
            }
            return -1;
        }
    }
}
