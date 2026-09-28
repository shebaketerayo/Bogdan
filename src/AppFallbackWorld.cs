using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopCat
{
    internal sealed class AppFallbackWorld : IWorldProvider
    {
        WorldSnapshot current;
        long version;
        int lastRefresh = int.MinValue;

        public WorldSnapshot Current
        {
            get
            {
                int now = Environment.TickCount;
                if (current == null || unchecked(now - lastRefresh) > 3000) Refresh();
                return current;
            }
        }

        public void Configure(WorldOptions options) { }
        public void Start() { Refresh(); }
        public void RequestRescan() { lastRefresh = int.MinValue; }

        public bool TryGetWindowFrame(IntPtr hwnd, out Rectangle frame)
        {
            frame = Rectangle.Empty;
            AppNative.RECT r;
            if (hwnd == IntPtr.Zero || !AppNative.IsWindowVisible(hwnd) || !AppNative.GetWindowRect(hwnd, out r)) return false;
            frame = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return true;
        }

        void Refresh()
        {
            lastRefresh = Environment.TickCount;
            try
            {
                WorldSnapshot s = new WorldSnapshot();
                s.Version = ++version;
                Screen[] screens = Screen.AllScreens;
                Rectangle vs = Rectangle.Empty;
                for (int i = 0; i < screens.Length; i++)
                {
                    MonitorInfo m = new MonitorInfo();
                    m.DeviceName = screens[i].DeviceName ?? ("monitor" + i);
                    m.Bounds = screens[i].Bounds;
                    m.WorkArea = screens[i].WorkingArea;
                    m.DpiScale = 1.0;
                    m.Primary = screens[i].Primary;
                    s.Monitors.Add(m);
                    vs = i == 0 ? m.Bounds : Rectangle.Union(vs, m.Bounds);
                    Platform p = new Platform();
                    p.Kind = PlatformKind.Floor;
                    p.Key = "floor:" + m.DeviceName;
                    p.Left = m.WorkArea.Left;
                    p.Right = m.WorkArea.Right;
                    p.Y = m.WorkArea.Bottom;
                    p.Name = "";
                    p.Bounds = m.WorkArea;
                    p.MonitorIndex = i;
                    s.Platforms.Add(p);
                }
                s.VirtualScreen = vs;
                s.DesktopIconsVisible = false;
                s.ForegroundWindow = AppNative.GetForegroundWindow();
                current = s;
            }
            catch (Exception ex)
            {
                CatLog.Error("AppFallbackWorld.Refresh", ex);
                if (current == null)
                {
                    WorldSnapshot s = new WorldSnapshot();
                    MonitorInfo m = new MonitorInfo();
                    m.DeviceName = "fallback"; m.Bounds = new Rectangle(0, 0, 1920, 1080); m.WorkArea = new Rectangle(0, 0, 1920, 1040);
                    m.DpiScale = 1.0; m.Primary = true;
                    s.Monitors.Add(m);
                    Platform p = new Platform();
                    p.Kind = PlatformKind.Floor; p.Key = "floor:fallback"; p.Left = 0; p.Right = 1920; p.Y = 1040; p.Name = "";
                    p.Bounds = m.WorkArea;
                    s.Platforms.Add(p);
                    s.VirtualScreen = m.Bounds;
                    current = s;
                }
            }
        }

        public void Dispose() { }
    }
}
