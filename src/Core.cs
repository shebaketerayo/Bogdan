using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace DesktopCat
{
    public enum PlatformKind { Floor = 0, Icon = 1, WindowTop = 2 }

    // a one-way surface: the cat passes through it from below and lands on it from above;
    // all coordinates are physical pixels (the process is per-monitor-v2 dpi aware)
    public sealed class Platform
    {
        public PlatformKind Kind;
        public string Key;
        public int Left;
        public int Right; // exclusive
        public int Y;
        public IntPtr Hwnd;
        public string Name;
        public bool IsFolder;
        public Rectangle Bounds;
        public int MonitorIndex;
    }

    public sealed class MonitorInfo
    {
        public string DeviceName;
        public Rectangle Bounds;
        public Rectangle WorkArea;
        public double DpiScale;
        public bool Primary;
    }

    // immutable once published: scanner threads hand it to the ui thread without locking
    public sealed class WorldSnapshot
    {
        public long Version;
        public List<MonitorInfo> Monitors = new List<MonitorInfo>();
        public List<Platform> Platforms = new List<Platform>();
        public List<Platform> DesktopIcons = new List<Platform>();
        public Rectangle VirtualScreen;
        public bool FullscreenAppActive;
        public bool DesktopIconsVisible;
        public IntPtr ForegroundWindow;

        public int MonitorIndexAt(int x, int y)
        {
            int best = -1;
            long bestD = long.MaxValue;
            for (int i = 0; i < Monitors.Count; i++)
            {
                Rectangle b = Monitors[i].Bounds;
                if (b.Contains(x, y)) return i;
                long dx = x < b.Left ? b.Left - x : (x >= b.Right ? x - b.Right + 1 : 0);
                long dy = y < b.Top ? b.Top - y : (y >= b.Bottom ? y - b.Bottom + 1 : 0);
                long d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public MonitorInfo PrimaryMonitor
        {
            get
            {
                for (int i = 0; i < Monitors.Count; i++) if (Monitors[i].Primary) return Monitors[i];
                return Monitors.Count > 0 ? Monitors[0] : null;
            }
        }
    }

    public sealed class WorldOptions
    {
        public bool IncludeWindowTops = true;
        public bool OccludeByWindows = true;
        public IntPtr[] ExcludeWindows = new IntPtr[0];

        public WorldOptions Clone()
        {
            WorldOptions o = new WorldOptions();
            o.IncludeWindowTops = IncludeWindowTops;
            o.OccludeByWindows = OccludeByWindows;
            o.ExcludeWindows = (IntPtr[])ExcludeWindows.Clone();
            return o;
        }
    }

    public interface IWorldProvider : IDisposable
    {
        WorldSnapshot Current { get; }
        void Configure(WorldOptions options);
        void Start();
        void RequestRescan();
        bool TryGetWindowFrame(IntPtr hwnd, out Rectangle frame);
    }

    public enum CatAnim
    {
        Sit,
        SitBlink,
        SitLookUp,
        Stand,
        Walk,
        Run,
        Crouch,
        JumpUp,
        JumpFall,
        Land,
        LieDown,
        Loaf,
        Sleep,
        WakeUp,
        Groom,
        Stretch,
        Yawn,
        Wiggle,
        Swat,
        Held,
        Fall,
        Happy,
        Meow,
        StandUp,
        SitDown,
        Knead,
        Scratch,
        SlowBlink
    }

    public enum PaletteSlot
    {
        Outline = 0,
        Fur,
        FurShade,
        Stripes,
        Light,
        Paws,
        Points,
        Muzzle,
        PatchA,
        PatchB,
        Eye,
        Pink
    }

    public sealed class CatPalette
    {
        public const int SlotCount = 12;
        public string Name = "";
        public Color[] Colors = new Color[SlotCount];

        public Color this[PaletteSlot s]
        {
            get { return Colors[(int)s]; }
            set { Colors[(int)s] = value; }
        }

        public CatPalette Clone()
        {
            CatPalette p = new CatPalette();
            p.Name = Name;
            p.Colors = (Color[])Colors.Clone();
            return p;
        }
    }

    public interface ICatSprites : IDisposable
    {
        int FrameWidth { get; }
        int FrameHeight { get; }
        // feet contact point; x == FrameWidth / 2 lies between two pixel columns, so mirroring keeps it fixed
        Point Anchor { get; }
        int FrameCount(CatAnim anim);
        double FrameDuration(CatAnim anim, int frame);
        double Duration(CatAnim anim);
        int FrameAt(CatAnim anim, double t);
        // the bitmap is cached and shared: callers must not dispose or modify it
        Bitmap GetFrame(CatAnim anim, int frame, bool faceLeft);
        Point HeadPoint(CatAnim anim, int frame);
        Rectangle BodyBounds(CatAnim anim, int frame);
    }

    public enum PlayMode { Never = 0, Sometimes = 1, Often = 2 }

    public sealed class CatSettings
    {
        public const string CustomPaletteName = "Свой";

        public string CatName = "Богдан";
        public string PaletteName = "";
        public CatPalette CustomPalette;
        // art-pixel multiplier at 100% dpi; screen scale = max(1, round(Size * monitor DpiScale))
        public int Size = 2;
        public PlayMode Play = PlayMode.Sometimes;
        public bool AlwaysOnTop = true;
        public bool WalkOnWindows = true;
        public bool SleepOnFolders = true;
        public bool HideInFullscreen = true;
        public bool ShowEffects = true;
        public bool ScratchIcons = true;
        public bool Autostart = false;

        public CatSettings Clone()
        {
            CatSettings s = (CatSettings)MemberwiseClone();
            if (CustomPalette != null) s.CustomPalette = CustomPalette.Clone();
            return s;
        }
    }

    public sealed class CatInput
    {
        public double Dt;
        public Point Cursor;
        public WorldSnapshot World;
        public IWorldProvider Provider;
        public int Scale;

        // edge-triggered flags (true for exactly one Update): clicks, the drop and the tray commands
        public bool Clicked;
        public bool DoubleClicked;
        public bool Petting;
        public bool DragActive;
        public bool DragReleased;
        public PointF ReleaseVelocity;

        public bool CmdCall;
        public bool CmdSleep;
        public bool CmdWake;
    }

    public enum CatEffect
    {
        Heart,
        Hearts,
        Zzz,
        Meow,
        Dust,
        Exclaim,
        Note,
        Shred
    }

    public sealed class CatFrameOutput
    {
        public double X, Y;
        public CatAnim Anim;
        public double AnimTime;
        public bool FaceLeft;
        public bool Visible = true;
        public bool Sleeping;
        public List<CatEffect> Effects = new List<CatEffect>();
        public bool WantsHighFps;
        public string ScratchIconKey;
        public double ScratchX, ScratchY;
    }

    public interface ICatBrain
    {
        void SetSprites(ICatSprites sprites);
        void ApplySettings(CatSettings settings);
        CatFrameOutput Update(CatInput input);
    }

    public static class AppPaths
    {
        public static string DataDir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DesktopCat");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }
        public static string SettingsFile { get { return Path.Combine(DataDir, "settings.ini"); } }
        public static string LogFile { get { return Path.Combine(DataDir, "log.txt"); } }
    }

    public static class CatLog
    {
        static readonly object Sync = new object();

        public static void Write(string message)
        {
            try
            {
                lock (Sync)
                {
                    string path = AppPaths.LogFile;
                    FileInfo fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > 512 * 1024)
                    {
                        string old = Path.Combine(AppPaths.DataDir, "log.old.txt");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(path, old);
                    }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Error(string where, Exception ex)
        {
            Write("ERROR in " + where + ": " + (ex == null ? "(null)" : ex.ToString()));
        }
    }
}
