using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DesktopCat
{
    internal sealed class ScratchMark
    {
        public double RelX;
        public int Seed;
    }

    internal sealed class ScratchOverlay : Form
    {
        CatWindowSurface surface;
        bool shown;

        public ScratchOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Text = "DesktopCat marks";
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = AppNative.WS_POPUP;
                cp.ExStyle = AppNative.WS_EX_LAYERED | AppNative.WS_EX_TRANSPARENT | AppNative.WS_EX_TOOLWINDOW | AppNative.WS_EX_NOACTIVATE;
                cp.Parent = IntPtr.Zero;
                cp.X = -32000; cp.Y = -32000; cp.Width = 1; cp.Height = 1;
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == AppNative.WM_MOUSEACTIVATE) { m.Result = (IntPtr)AppNative.MA_NOACTIVATE; return; }
            if (m.Msg == AppNative.WM_ERASEBKGND) { m.Result = (IntPtr)1; return; }
            base.WndProc(ref m);
        }

        public bool IsShown { get { return shown; } }

        public void Render(Rectangle pic, List<ScratchMark> marks, int artScale, int supportY)
        {
            if (!IsHandleCreated) { IntPtr h = Handle; }
            int w = Math.Max(4, pic.Width), hgt = Math.Max(4, pic.Height);
            if (surface == null || surface.Width != w || surface.Height != hgt)
            {
                if (surface != null) surface.Dispose();
                surface = new CatWindowSurface(w, hgt);
            }
            Graphics g = surface.Graphics;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            ScratchArt.Draw(g, w, hgt, marks, artScale, (supportY - pic.Top) / Math.Max(1, artScale));
            AppNative.GdiFlush();
            AppNative.POINT dst = new AppNative.POINT(pic.Left, pic.Top);
            AppNative.SIZE size = new AppNative.SIZE(w, hgt);
            AppNative.POINT src = new AppNative.POINT(0, 0);
            AppNative.BLENDFUNCTION bf = new AppNative.BLENDFUNCTION();
            bf.BlendOp = 0; bf.BlendFlags = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
            AppNative.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, surface.MemDC, ref src, 0, ref bf, AppNative.ULW_ALPHA);
        }

        public void SetShown(bool want)
        {
            if (want == shown) return;
            if (!IsHandleCreated) { IntPtr h = Handle; }
            AppNative.ShowWindow(Handle, want ? AppNative.SW_SHOWNOACTIVATE : AppNative.SW_HIDE);
            shown = want;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
            base.Dispose(disposing);
        }
    }

    internal static class ScratchArt
    {
        static readonly Color Cut = Color.FromArgb(215, 60, 45, 41);
        static readonly Color CutSoft = Color.FromArgb(120, 60, 45, 41);
        static readonly Color Torn = Color.FromArgb(135, 255, 250, 240);

        public static int ScaleFor(int picWidth, int catScale)
        {
            int s = (int)Math.Round(picWidth / 24.0);
            if (s > catScale) s = catScale;
            return s < 1 ? 1 : s;
        }

        public static void Draw(Graphics g, int w, int h, List<ScratchMark> marks, int s, int topArt)
        {
            if (marks == null || marks.Count == 0) return;
            if (s < 1) s = 1;
            int artW = w / s, artH = h / s;
            if (topArt < 0) topArt = 0;
            if (topArt > artH - 6) topArt = Math.Max(0, artH - 6);
            using (SolidBrush cut = new SolidBrush(Cut))
            using (SolidBrush cutSoft = new SolidBrush(CutSoft))
            using (SolidBrush torn = new SolidBrush(Torn))
            {
                for (int i = 0; i < marks.Count; i++)
                {
                    ScratchMark mk = marks[i];
                    Random r = new Random(mk.Seed);
                    int len = Math.Min(artH - topArt - 2, 5 + r.Next(3));
                    int cx = (int)Math.Round(mk.RelX * artW);
                    int top = topArt + 1 + r.Next(2);
                    int lean = r.Next(2) == 0 ? -1 : 1;
                    for (int k = 0; k < 3; k++)
                    {
                        int x0 = cx - 2 + k * 2;
                        int y0 = top + (k == 1 ? 0 : 1);
                        int l = len - (k == 1 ? 0 : 1 + r.Next(2));
                        for (int j = 0; j < l; j++)
                        {
                            int x = x0 + lean * (j / 2), y = y0 + j;
                            if (x < 0 || x >= artW - 1 || y < 0 || y >= artH) continue;
                            bool tip = j == 0 || j == l - 1;
                            g.FillRectangle(tip ? cutSoft : cut, x * s, y * s, s, s);
                            if (!tip && (j + k) % 2 == 0) g.FillRectangle(torn, (x + 1) * s, y * s, s, s);
                        }
                    }
                }
            }
        }
    }

    public sealed class ScratchMarks : IDisposable
    {
        public const int MaxIcons = 12, MaxMarksPerIcon = 5;

        sealed class Entry
        {
            public string Key;
            public List<ScratchMark> Marks = new List<ScratchMark>();
            public long Order;
            public ScratchOverlay Win;
            public Rectangle Pic = Rectangle.Empty;
            public int Scale, SupportY;
            public int Count;
        }

        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Random rng = new Random();
        long order;
        double lastSync = -10, lastZ = -10;
        bool dirty;
        string filePath;

        public ScratchMarks()
        {
            try { filePath = Path.Combine(AppPaths.DataDir, "scratches.txt"); } catch { filePath = null; }
            Load();
        }

        public int IconCount { get { return entries.Count; } }

        public IntPtr CatHwnd;
        public bool CatOnTop = true;

        void PlaceZ(ScratchOverlay w)
        {
            // in desktop mode the marks go right under the cat, so the cat always stands in front of them
            if (!CatOnTop && CatHwnd != IntPtr.Zero && AppNative.IsWindowVisible(CatHwnd))
                AppNative.SetWindowPos(w.Handle, CatHwnd, 0, 0, 0, 0,
                    AppNative.SWP_NOMOVE | AppNative.SWP_NOSIZE | AppNative.SWP_NOACTIVATE | AppNative.SWP_NOOWNERZORDER);
            else
                DesktopZOrder.Apply(w.Handle, false);      // the desktop layer: above the icons, below every window
        }

        public void ReassertZ()
        {
            foreach (Entry e in entries.Values)
            {
                if (e.Win == null || !e.Win.IsShown) continue;
                try { PlaceZ(e.Win); } catch (Exception ex) { CatLog.Error("ScratchMarks.ReassertZ", ex); }
            }
        }

        public void AddMark(string key, double clawX, WorldSnapshot snap)
        {
            if (string.IsNullOrEmpty(key) || snap == null) return;
            Platform icon = FindIcon(snap, key);
            if (icon == null || icon.Bounds.Width <= 0) return;
            Entry e;
            if (!entries.TryGetValue(key, out e))
            {
                if (entries.Count >= MaxIcons) DropOldest();
                e = new Entry();
                e.Key = key;
                entries[key] = e;
            }
            e.Order = ++order;
            Rectangle pic = icon.Bounds;
            double claw = (clawX - pic.Left) / pic.Width;
            // near the claws, but on a free spot so the marks do not pile up into one blot
            double best = claw, bestGap = -1;
            double[] offs = { 0, 0.14, -0.14, 0.28, -0.28, 0.42, -0.42 };
            for (int i = 0; i < offs.Length; i++)
            {
                double c = Math.Max(0.22, Math.Min(0.78, claw + offs[i]));
                double gap = 1;
                for (int k = 0; k < e.Marks.Count; k++) gap = Math.Min(gap, Math.Abs(e.Marks[k].RelX - c));
                if (gap > bestGap + 0.02) { bestGap = gap; best = c; }
            }
            ScratchMark m = new ScratchMark();
            m.RelX = best + (rng.NextDouble() - 0.5) * 0.04;
            m.Seed = rng.Next(1, int.MaxValue);
            if (e.Marks.Count >= MaxMarksPerIcon) e.Marks.RemoveAt(0);
            e.Marks.Add(m);
            e.Count = -1;                            // forces a redraw
            dirty = true;
            lastSync = -10;
        }

        public void Clear()
        {
            foreach (Entry e in entries.Values) DisposeWin(e);
            entries.Clear();
            dirty = true;
            Save();
        }

        public void Sync(WorldSnapshot snap, int catScale, double now, bool allowed)
        {
            if (dirty && now - lastSync > 2) Save();
            if (now - lastSync < 0.4) return;
            lastSync = now;
            bool reassert = now - lastZ > 3.0;
            if (reassert) lastZ = now;
            foreach (Entry e in entries.Values)
            {
                try
                {
                    Platform icon = allowed && snap != null && snap.DesktopIconsVisible ? FindIcon(snap, e.Key) : null;
                    if (icon == null || icon.Bounds.Width <= 0 || e.Marks.Count == 0)
                    {
                        if (e.Win != null) e.Win.SetShown(false);
                        continue;
                    }
                    Rectangle pic = icon.Bounds;
                    int s = ScratchArt.ScaleFor(pic.Width, catScale);
                    if (e.Win == null) e.Win = new ScratchOverlay();
                    bool fresh = !e.Win.IsShown;
                    if (pic != e.Pic || s != e.Scale || e.Count != e.Marks.Count || icon.Y != e.SupportY)
                    {
                        e.Win.Render(pic, e.Marks, s, icon.Y);
                        e.Pic = pic; e.Scale = s; e.Count = e.Marks.Count; e.SupportY = icon.Y;
                    }
                    e.Win.SetShown(true);
                    if (fresh || reassert) PlaceZ(e.Win);
                }
                catch (Exception ex) { CatLog.Error("ScratchMarks.Sync", ex); }
            }
        }

        static Platform FindIcon(WorldSnapshot snap, string key)
        {
            List<Platform> list = snap.DesktopIcons;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++) if (list[i].Key == key) return list[i];
            return null;
        }

        void DropOldest()
        {
            Entry oldest = null;
            foreach (Entry e in entries.Values) if (oldest == null || e.Order < oldest.Order) oldest = e;
            if (oldest == null) return;
            DisposeWin(oldest);
            entries.Remove(oldest.Key);
        }

        static void DisposeWin(Entry e)
        {
            if (e.Win == null) return;
            try { e.Win.SetShown(false); e.Win.Dispose(); } catch { }
            e.Win = null;
        }

        // one line per icon: "relx:seed,relx:seed,...|icon key", relx in 1/1000
        void Load()
        {
            try
            {
                if (filePath == null || !File.Exists(filePath)) return;
                foreach (string raw in File.ReadAllLines(filePath, Encoding.UTF8))
                {
                    int bar = raw.IndexOf('|');
                    if (bar <= 0 || bar >= raw.Length - 1) continue;
                    string key = raw.Substring(bar + 1);
                    Entry e = new Entry();
                    e.Key = key;
                    e.Order = ++order;
                    foreach (string part in raw.Substring(0, bar).Split(','))
                    {
                        string[] kv = part.Split(':');
                        int rx, seed;
                        if (kv.Length != 2 || !int.TryParse(kv[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out rx) ||
                            !int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out seed)) continue;
                        ScratchMark m = new ScratchMark();
                        m.RelX = Math.Max(0.1, Math.Min(0.9, rx / 1000.0));
                        m.Seed = seed;
                        if (e.Marks.Count < MaxMarksPerIcon) e.Marks.Add(m);
                    }
                    if (e.Marks.Count > 0 && entries.Count < MaxIcons) entries[key] = e;
                }
            }
            catch (Exception ex) { CatLog.Error("ScratchMarks.Load", ex); }
        }

        public void Save()
        {
            dirty = false;
            if (filePath == null) return;
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (Entry e in entries.Values)
                {
                    if (e.Marks.Count == 0 || e.Key.IndexOf('\n') >= 0) continue;
                    for (int i = 0; i < e.Marks.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(((int)Math.Round(e.Marks[i].RelX * 1000)).ToString(CultureInfo.InvariantCulture))
                          .Append(':').Append(e.Marks[i].Seed.ToString(CultureInfo.InvariantCulture));
                    }
                    sb.Append('|').Append(e.Key).Append("\r\n");
                }
                if (sb.Length == 0) { if (File.Exists(filePath)) File.Delete(filePath); return; }
                string tmp = filePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(filePath)) File.Delete(filePath);
                File.Move(tmp, filePath);
            }
            catch (Exception ex) { CatLog.Error("ScratchMarks.Save", ex); }
        }

        public void Dispose()
        {
            if (dirty) Save();
            foreach (Entry e in entries.Values) DisposeWin(e);
        }
    }
}
