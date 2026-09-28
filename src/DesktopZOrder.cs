using System;

namespace DesktopCat
{
    public static class DesktopZOrder
    {
        const uint Flags = WorldNative.SWP_NOMOVE | WorldNative.SWP_NOSIZE | WorldNative.SWP_NOACTIVATE | WorldNative.SWP_NOOWNERZORDER;
        const int MaxWalk = 4096;
        static int errorsLogged;
        static IntPtr cachedHost;
        static readonly object Sync = new object();

        public static void Apply(IntPtr catHwnd, bool alwaysOnTop)
        {
            try
            {
                if (catHwnd == IntPtr.Zero || !WorldNative.IsWindow(catHwnd)) return;
                lock (Sync)
                {
                    if (alwaysOnTop) ApplyTopmost(catHwnd);
                    else ApplyDesktop(catHwnd);
                }
            }
            catch (Exception ex)
            {
                if (errorsLogged++ < 5) CatLog.Error("DesktopZOrder.Apply", ex);
            }
        }

        static bool IsTopmost(IntPtr h) { return (WorldNative.GetExStyle(h) & WorldNative.WS_EX_TOPMOST) != 0; }

        static bool Shows(IntPtr h, out WorldNative.RECT frame)
        {
            frame = new WorldNative.RECT();
            if (!WorldNative.IsWindowVisible(h) || WorldNative.IsIconic(h) || WorldNative.IsCloaked(h)) return false;
            if ((WorldNative.GetExStyle(h) & WorldNative.WS_EX_TRANSPARENT) != 0) return false;
            if (!WorldNative.GetFrame(h, out frame)) return false;
            return frame.Width > 2 && frame.Height > 2;
        }

        static void ApplyTopmost(IntPtr h)
        {
            if (IsTopmost(h) && !AnythingAboveOverlaps(h)) return;
            WorldNative.SetWindowPos(h, WorldNative.HWND_TOPMOST, 0, 0, 0, 0, Flags);
        }

        static bool AnythingAboveOverlaps(IntPtr h)
        {
            WorldNative.RECT me;
            if (!WorldNative.GetWindowRect(h, out me)) return true;
            uint myPid;
            WorldNative.GetWindowThreadProcessId(h, out myPid);
            IntPtr w = WorldNative.GetWindow(h, WorldNative.GW_HWNDPREV);
            for (int i = 0; i < MaxWalk && w != IntPtr.Zero; i++)
            {
                WorldNative.RECT f;
                if (Shows(w, out f) && f.Left < me.Right && f.Right > me.Left && f.Top < me.Bottom && f.Bottom > me.Top)
                {
                    uint pid;
                    WorldNative.GetWindowThreadProcessId(w, out pid);
                    if (pid != myPid) return true; // our own menus and settings window may stay above the cat
                }
                w = WorldNative.GetWindow(w, WorldNative.GW_HWNDPREV);
            }
            return false;
        }

        // desktop mode: sit directly above the window hosting the icons, i.e. over the wallpaper and the icons but
        // under every normal window. HWND_BOTTOM would hide the cat under the icon WorkerW on pre-24h2 layouts, and
        // making Progman the owner would attach our input queue to explorer's, so a hang on one side freezes the other
        static void ApplyDesktop(IntPtr h)
        {
            if (IsTopmost(h)) WorldNative.SetWindowPos(h, WorldNative.HWND_NOTOPMOST, 0, 0, 0, 0, Flags);
            IntPtr host = IconHost();
            if (host == IntPtr.Zero)
            {
                WorldNative.SetWindowPos(h, WorldNative.HWND_BOTTOM, 0, 0, 0, 0, Flags);
                return;
            }
            if (DirectlyAbove(h, host)) return;
            IntPtr prev = WorldNative.GetWindow(host, WorldNative.GW_HWNDPREV);
            if (prev == h) return;
            // "show desktop" raises the host to the top of the normal band; otherwise going below prev means right above the host
            if (prev == IntPtr.Zero || IsTopmost(prev))
                WorldNative.SetWindowPos(h, WorldNative.HWND_TOP, 0, 0, 0, 0, Flags);
            else
                WorldNative.SetWindowPos(h, prev, 0, 0, 0, 0, Flags);
        }

        static IntPtr IconHost()
        {
            IntPtr c = cachedHost;
            if (c != IntPtr.Zero && WorldNative.IsWindow(c) &&
                WorldNative.FindWindowEx(c, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero) return c;
            IntPtr dv, lv;
            if (!DesktopWindows.Find(out dv, out lv)) { cachedHost = IntPtr.Zero; return IntPtr.Zero; }
            IntPtr root = WorldNative.GetAncestor(dv, WorldNative.GA_ROOT);
            cachedHost = root;
            return root;
        }

        static bool DirectlyAbove(IntPtr h, IntPtr host)
        {
            uint myPid;
            WorldNative.GetWindowThreadProcessId(h, out myPid);
            IntPtr w = WorldNative.GetWindow(h, WorldNative.GW_HWNDNEXT);
            for (int i = 0; i < MaxWalk && w != IntPtr.Zero; i++)
            {
                if (w == host) return true;
                uint pid;
                WorldNative.GetWindowThreadProcessId(w, out pid);
                WorldNative.RECT f;
                // our own desktop-layer windows (claw marks on icons) may lie between us and the desktop
                if (pid != myPid && Shows(w, out f)) return false;
                w = WorldNative.GetWindow(w, WorldNative.GW_HWNDNEXT);
            }
            return false;
        }
    }
}
