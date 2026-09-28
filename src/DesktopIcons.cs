using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopCat
{
    internal sealed class DesktopIconItem
    {
        public string Name = "";
        public bool IsFolder;
        public Point Position;
        public Rectangle Square;
        public Rectangle Picture;
        public int SupportY;
    }

    internal sealed class DesktopIconResult
    {
        public bool Ok;
        public IntPtr ListView;
        public Size Spacing;
        public int ViewMode;
        public int TopOffset;
        public List<DesktopIconItem> Items = new List<DesktopIconItem>();
        public string Error = "";
    }

    // holds shell com objects, so it must be used from a single sta thread
    internal sealed class DesktopIconReader : IDisposable
    {
        WorldNative.IFolderView view;
        WorldNative.IFolderView2 view2;
        object viewObj;
        IntPtr listView;
        WorldNative.IShellFolder localDesktop;
        readonly Dictionary<string, ItemInfo> cache = new Dictionary<string, ItemInfo>();
        readonly StringBuilder sb = new StringBuilder(520);
        IntPtr strret = IntPtr.Zero;
        IntPtr bmiBuf = IntPtr.Zero;
        int failures;
        public string LastAcquireInfo = "";
        public int MaxShapesPerScan = 400;
        public int ShapeTimeBudgetMs = 150;
        public bool ShapesPending;

        sealed class ItemInfo
        {
            public string Name = "";
            public uint Attr;
            public bool IsFolder;
            public bool Seen;
            public int ShapeSize;
            public PictureShape Shape;
        }

        sealed class PictureShape
        {
            public double L, T, R, B, Support;
        }

        public int ConsecutiveFailures { get { return failures; } }

        public DesktopIconResult Scan()
        {
            DesktopIconResult res = new DesktopIconResult();
            try
            {
                if (view == null || !WorldNative.IsWindow(listView))
                {
                    Release();
                    // no desktop listview means explorer is not running; activating ShellWindows then would
                    // launch a rundll32 local server on every retry
                    IntPtr dv0, lv0;
                    if (!DesktopWindows.Find(out dv0, out lv0))
                    {
                        LastAcquireInfo = "no desktop listview (Explorer not running?)";
                        res.Error = LastAcquireInfo;
                        failures++;
                        return res;
                    }
                    Acquire();
                }
                if (view == null)
                {
                    res.Error = "desktop shell view not found (" + LastAcquireInfo + ")";
                    failures++;
                    return res;
                }
                res.ListView = listView;

                int mode = 0, size = 48;
                if (view2 != null) CheckHr(view2.GetViewModeAndIconSize(out mode, out size), "GetViewModeAndIconSize");
                else { uint m; CheckHr(view.GetCurrentViewMode(out m), "GetCurrentViewMode"); mode = (int)m; }
                if (size <= 0 || size > 512) size = 48;
                WorldNative.POINT sp = new WorldNative.POINT();
                CheckHr(view.GetSpacing(ref sp), "GetSpacing");
                res.Spacing = new Size(sp.X, sp.Y);
                res.ViewMode = mode;
                double dpi = WorldDpi.ForWindow(listView);
                // the item position is the top-left of the icon cell; the image square starts 5 px (at 96 dpi) below it
                res.TopOffset = (int)Math.Round(5 * dpi);

                WorldNative.POINT org = new WorldNative.POINT();
                WorldNative.ClientToScreen(listView, ref org);

                foreach (ItemInfo ii in cache.Values) ii.Seen = false;
                int shapeBudget = MaxShapesPerScan;
                Stopwatch shapeClock = new Stopwatch();
                bool pending = false;
                List<IntPtr> pidls = EnumPidls();
                try
                {
                    for (int i = 0; i < pidls.Count; i++)
                    {
                        IntPtr pidl = pidls[i];
                        WorldNative.POINT pt;
                        int hr = view.GetItemPosition(pidl, out pt);
                        if (hr < 0) { CheckHr(hr, "GetItemPosition"); continue; }
                        ItemInfo ni = Describe(pidl);
                        if (ni.ShapeSize != size)
                        {
                            if (shapeBudget > 0 && shapeClock.ElapsedMilliseconds < ShapeTimeBudgetMs)
                            {
                                shapeBudget--;
                                shapeClock.Start();
                                ni.Shape = ComputeShape(pidl, size, (ni.Attr & WorldNative.SFGAO_FOLDER) != 0);
                                shapeClock.Stop();
                                ni.ShapeSize = size;
                            }
                            else pending = true;
                        }
                        DesktopIconItem it = new DesktopIconItem();
                        it.Name = ni.Name;
                        it.IsFolder = ni.IsFolder;
                        it.Position = new Point(pt.X + org.X, pt.Y + org.Y);
                        it.Square = new Rectangle(it.Position.X, it.Position.Y + res.TopOffset, size, size);
                        PictureShape s = ni.ShapeSize == size ? ni.Shape : null;
                        if (s != null)
                        {
                            int l = it.Square.X + (int)Math.Floor(s.L + 0.001), r = it.Square.X + (int)Math.Ceiling(s.R - 0.001);
                            int t = it.Square.Y + (int)Math.Floor(s.T + 0.001), b = it.Square.Y + (int)Math.Ceiling(s.B - 0.001);
                            it.Picture = Rectangle.FromLTRB(l, t, Math.Max(r, l + 1), Math.Max(b, t + 1));
                            it.SupportY = it.Square.Y + (int)Math.Round(s.Support);
                        }
                        else
                        {
                            it.Picture = it.Square;
                            it.SupportY = it.Square.Y;
                        }
                        res.Items.Add(it);
                    }
                }
                finally
                {
                    for (int i = 0; i < pidls.Count; i++) Marshal.FreeCoTaskMem(pidls[i]);
                }
                if (cache.Count > 64) TrimCache();
                ShapesPending = pending;
                res.Ok = true;
                failures = 0;
            }
            catch (Exception ex)
            {
                failures++;
                res.Ok = false;
                res.Error = ex.GetType().Name + ": " + ex.Message;
                Release();
            }
            return res;
        }

        static void CheckHr(int hr, string what)
        {
            if (hr < 0) throw new COMException(what + " failed 0x" + hr.ToString("X8"), hr);
        }

        List<IntPtr> EnumPidls()
        {
            List<IntPtr> list = new List<IntPtr>();
            Guid iid = WorldNative.IID_IEnumIDList;
            object eo;
            int hr = view.Items(WorldNative.SVGIO_ALLVIEW, ref iid, out eo);
            if (hr < 0 || eo == null)
            {
                int count;
                CheckHr(view.ItemCount(WorldNative.SVGIO_ALLVIEW, out count), "ItemCount");
                for (int i = 0; i < count && i < 4096; i++)
                {
                    IntPtr p;
                    if (view.Item(i, out p) >= 0 && p != IntPtr.Zero) list.Add(p);
                }
                return list;
            }
            try
            {
                WorldNative.IEnumIDList en = (WorldNative.IEnumIDList)eo;
                IntPtr[] buf = new IntPtr[256];
                for (int guard = 0; guard < 64; guard++)
                {
                    uint got;
                    Array.Clear(buf, 0, buf.Length);
                    hr = en.Next((uint)buf.Length, buf, out got);
                    for (int i = 0; i < got && i < buf.Length; i++) if (buf[i] != IntPtr.Zero) list.Add(buf[i]);
                    if (hr != 0 || got == 0) break;
                }
            }
            catch
            {
                for (int i = 0; i < list.Count; i++) Marshal.FreeCoTaskMem(list[i]);
                throw;
            }
            finally
            {
                SafeRelease(eo);
            }
            return list;
        }

        ItemInfo Describe(IntPtr pidl)
        {
            string key = PidlKey(pidl);
            ItemInfo ni;
            if (key != null && cache.TryGetValue(key, out ni)) { ni.Seen = true; return ni; }
            ni = new ItemInfo();
            ni.Seen = true;
            try
            {
                if (localDesktop == null)
                {
                    WorldNative.IShellFolder f;
                    if (WorldNative.SHGetDesktopFolder(out f) >= 0) localDesktop = f;
                }
                if (localDesktop != null)
                {
                    if (strret == IntPtr.Zero) strret = Marshal.AllocCoTaskMem(1024);
                    for (int i = 0; i < 1024; i += 8) Marshal.WriteInt64(strret, i, 0);
                    if (localDesktop.GetDisplayNameOf(pidl, WorldNative.SHGDN_INFOLDER, strret) >= 0)
                    {
                        sb.Length = 0;
                        sb.EnsureCapacity(520);
                        if (WorldNative.StrRetToBufW(strret, pidl, sb, (uint)sb.Capacity) >= 0) ni.Name = sb.ToString();
                    }
                    uint attr = WorldNative.SFGAO_FOLDER | WorldNative.SFGAO_STREAM | WorldNative.SFGAO_LINK | WorldNative.SFGAO_FILESYSTEM;
                    if (localDesktop.GetAttributesOf(1, new IntPtr[] { pidl }, ref attr) >= 0)
                    {
                        ni.Attr = attr;
                        // zip and cab files report SFGAO_FOLDER too, but are streams
                        bool folder = (attr & WorldNative.SFGAO_FOLDER) != 0 && (attr & WorldNative.SFGAO_STREAM) == 0;
                        if (!folder && (attr & WorldNative.SFGAO_LINK) != 0) folder = LinkPointsToFolder(pidl);
                        ni.IsFolder = folder;
                    }
                }
            }
            catch (Exception ex)
            {
                CatLog.Write("world: icon name lookup failed: " + ex.Message);
            }
            if (ni.Name == null) ni.Name = "";
            if (key != null) cache[key] = ni;
            return ni;
        }

        bool LinkPointsToFolder(IntPtr pidl)
        {
            object o = null;
            try
            {
                Guid iid = WorldNative.IID_IShellLinkW;
                if (localDesktop.GetUIObjectOf(IntPtr.Zero, 1, new IntPtr[] { pidl }, ref iid, IntPtr.Zero, out o) < 0 || o == null) return false;
                WorldNative.IShellLinkW link = o as WorldNative.IShellLinkW;
                if (link == null) return false;
                StringBuilder p = new StringBuilder(1024);
                if (link.GetPath(p, p.Capacity, IntPtr.Zero, 0) != 0) return false;
                string path = p.ToString();
                return path.Length > 0 && path.Length < 260 && Directory.Exists(path);
            }
            catch { return false; }
            finally { SafeRelease(o); }
        }

        PictureShape ComputeShape(IntPtr pidl, int size, bool folderItem)
        {
            object o = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = WorldNative.IID_IShellItemImageFactory;
                if (WorldNative.SHCreateItemFromIDList(pidl, ref iid, out o) < 0 || o == null) return null;
                WorldNative.IShellItemImageFactory f = o as WorldNative.IShellItemImageFactory;
                if (f == null) return null;
                WorldNative.SIZE sz = new WorldNative.SIZE();
                sz.cx = size; sz.cy = size;
                bool thumb = false;
                // a cached thumbnail means the desktop shows it; folders with content previews are still drawn like icons
                if (f.GetImage(sz, WorldNative.SIIGBF_THUMBNAILONLY | WorldNative.SIIGBF_INCACHEONLY, out hbm) >= 0 && hbm != IntPtr.Zero) thumb = !folderItem;
                else hbm = IntPtr.Zero;
                if (hbm == IntPtr.Zero)
                {
                    if (f.GetImage(sz, WorldNative.SIIGBF_ICONONLY, out hbm) < 0) hbm = IntPtr.Zero;
                }
                if (hbm == IntPtr.Zero) return null;
                int w, h;
                int[] px = ReadBitmap(hbm, out w, out h);
                if (px == null) return null;
                return Analyze(px, w, h, size, thumb);
            }
            catch (Exception ex)
            {
                CatLog.Write("world: icon shape failed: " + ex.Message);
                return null;
            }
            finally
            {
                if (hbm != IntPtr.Zero) WorldNative.DeleteObject(hbm);
                SafeRelease(o);
            }
        }

        int[] ReadBitmap(IntPtr hbm, out int w, out int h)
        {
            w = h = 0;
            WorldNative.BITMAP bm;
            if (WorldNative.GetObjectBitmap(hbm, Marshal.SizeOf(typeof(WorldNative.BITMAP)), out bm) == 0) return null;
            w = bm.bmWidth; h = Math.Abs(bm.bmHeight);
            if (w <= 0 || h <= 0 || w > 1024 || h > 1024) return null;
            if (bmiBuf == IntPtr.Zero) bmiBuf = Marshal.AllocHGlobal(64);
            for (int i = 0; i < 64; i += 4) Marshal.WriteInt32(bmiBuf, i, 0);
            // fill a BITMAPINFOHEADER: size, width, negative height (top-down rows), 1 plane, 32 bpp
            Marshal.WriteInt32(bmiBuf, 0, 40);
            Marshal.WriteInt32(bmiBuf, 4, w);
            Marshal.WriteInt32(bmiBuf, 8, -h);
            Marshal.WriteInt16(bmiBuf, 12, 1);
            Marshal.WriteInt16(bmiBuf, 14, 32);
            int[] px = new int[w * h];
            IntPtr dc = WorldNative.GetDC(IntPtr.Zero);
            try
            {
                if (WorldNative.GetDIBits(dc, hbm, 0, (uint)h, px, bmiBuf, 0) == 0) return null;
            }
            finally { WorldNative.ReleaseDC(IntPtr.Zero, dc); }
            return px;
        }

        static PictureShape Analyze(int[] px, int w, int h, int size, bool thumb)
        {
            bool anyAlpha = false;
            for (int i = 0; i < px.Length; i++) if ((px[i] & unchecked((int)0xFF000000)) != 0) { anyAlpha = true; break; }
            int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int a = anyAlpha ? (px[y * w + x] >> 24) & 0xFF : 255;
                    if (a >= 128)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            if (maxX < 0) return null;
            int cw = maxX - minX + 1;
            // the cat stands on the first row that is at least 45% solid (so a folder tab or a bin lid does not count),
            // but never deeper than 40% into the picture
            int support = minY;
            double need = Math.Max(3, 0.45 * cw);
            for (int y = minY; y <= maxY; y++)
            {
                int n = 0;
                for (int x = minX; x <= maxX; x++)
                {
                    int a = anyAlpha ? (px[y * w + x] >> 24) & 0xFF : 255;
                    if (a >= 128) n++;
                }
                if (n >= need) { support = y; break; }
            }
            if (support - minY > 0.4 * (maxY - minY + 1)) support = minY;

            PictureShape s = new PictureShape();
            double k, ox, oy;
            if (thumb)
            {
                double box = size * 45.0 / 48.0; // thumbnails are bottom-aligned with a 3 px shadow right and below
                k = box / Math.Max(w, h);
                ox = (box - w * k) / 2;
                oy = box - h * k;
            }
            else
            {
                k = (double)size / Math.Max(w, h);
                ox = (size - w * k) / 2;
                oy = (size - h * k) / 2;
            }
            s.L = ox + minX * k;
            s.R = ox + (maxX + 1) * k;
            s.T = oy + minY * k;
            s.B = oy + (maxY + 1) * k;
            s.Support = oy + support * k;
            return s;
        }

        void TrimCache()
        {
            List<string> dead = null;
            foreach (KeyValuePair<string, ItemInfo> kv in cache)
                if (!kv.Value.Seen) { if (dead == null) dead = new List<string>(); dead.Add(kv.Key); }
            if (dead != null) foreach (string k in dead) cache.Remove(k);
        }

        static string PidlKey(IntPtr pidl)
        {
            try
            {
                uint n = WorldNative.ILGetSize(pidl);
                if (n == 0 || n > 65536) return null;
                byte[] b = new byte[n];
                Marshal.Copy(pidl, b, 0, (int)n);
                return Convert.ToBase64String(b);
            }
            catch { return null; }
        }

        void Acquire()
        {
            object swObj = null, disp = null, browserObj = null;
            try
            {
                Type t = Type.GetTypeFromCLSID(WorldNative.CLSID_ShellWindows, false);
                if (t == null) { LastAcquireInfo = "ShellWindows not registered"; return; }
                swObj = Activator.CreateInstance(t);
                WorldNative.IShellWindows sw = (WorldNative.IShellWindows)swObj;
                object loc = WorldNative.CSIDL_DESKTOP;
                object root = null;
                int hwnd;
                int hr = sw.FindWindowSW(ref loc, ref root, WorldNative.SWC_DESKTOP, out hwnd, WorldNative.SWFO_NEEDDISPATCH, out disp);
                if (hr != 0 || disp == null) { LastAcquireInfo = "FindWindowSW hr=0x" + hr.ToString("X8"); return; }
                WorldNative.IOleServiceProvider sp = (WorldNative.IOleServiceProvider)disp;
                Guid sid = WorldNative.SID_STopLevelBrowser, iid = WorldNative.IID_IShellBrowser;
                hr = sp.QueryService(ref sid, ref iid, out browserObj);
                if (hr < 0 || browserObj == null) { LastAcquireInfo = "QueryService hr=0x" + hr.ToString("X8"); return; }
                WorldNative.IShellBrowser browser = (WorldNative.IShellBrowser)browserObj;
                object vo;
                hr = browser.QueryActiveShellView(out vo);
                if (hr < 0 || vo == null) { LastAcquireInfo = "QueryActiveShellView hr=0x" + hr.ToString("X8"); return; }
                WorldNative.IFolderView fv = vo as WorldNative.IFolderView;
                if (fv == null) { SafeRelease(vo); LastAcquireInfo = "no IFolderView"; return; }
                IntPtr dv = IntPtr.Zero;
                WorldNative.IOleWindow ow = vo as WorldNative.IOleWindow;
                if (ow != null) ow.GetWindow(out dv);
                IntPtr lv = dv != IntPtr.Zero ? WorldNative.FindWindowEx(dv, IntPtr.Zero, "SysListView32", null) : IntPtr.Zero;
                if (lv == IntPtr.Zero) DesktopWindows.Find(out dv, out lv);
                if (lv == IntPtr.Zero) { SafeRelease(vo); LastAcquireInfo = "desktop listview not found"; return; }
                viewObj = vo;
                view = fv;
                view2 = vo as WorldNative.IFolderView2;
                listView = lv;
            }
            catch (Exception ex)
            {
                LastAcquireInfo = ex.GetType().Name + ": " + ex.Message;
                Release();
            }
            finally
            {
                SafeRelease(browserObj);
                SafeRelease(disp);
                SafeRelease(swObj);
            }
        }

        void Release()
        {
            object v = viewObj;
            viewObj = null; view = null; view2 = null;
            listView = IntPtr.Zero;
            SafeRelease(v);
        }

        internal static void SafeRelease(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
        }

        public void Dispose()
        {
            Release();
            SafeRelease(localDesktop);
            localDesktop = null;
            if (strret != IntPtr.Zero) { Marshal.FreeCoTaskMem(strret); strret = IntPtr.Zero; }
            if (bmiBuf != IntPtr.Zero) { Marshal.FreeHGlobal(bmiBuf); bmiBuf = IntPtr.Zero; }
            cache.Clear();
        }
    }

    internal static class DesktopWindows
    {
        public static IntPtr Progman()
        {
            return WorldNative.FindWindow("Progman", null);
        }

        public static bool Find(out IntPtr defView, out IntPtr listView)
        {
            // windows 11 24h2+ puts SHELLDLL_DefView inside Progman, older builds inside a top-level WorkerW
            defView = IntPtr.Zero; listView = IntPtr.Zero;
            IntPtr progman = Progman();
            if (progman != IntPtr.Zero) defView = WorldNative.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView == IntPtr.Zero)
            {
                IntPtr w = IntPtr.Zero;
                for (int guard = 0; guard < 1024; guard++)
                {
                    w = WorldNative.FindWindowEx(IntPtr.Zero, w, "WorkerW", null);
                    if (w == IntPtr.Zero) break;
                    IntPtr dv = WorldNative.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (dv != IntPtr.Zero) { defView = dv; break; }
                }
            }
            if (defView == IntPtr.Zero) return false;
            listView = WorldNative.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
            return listView != IntPtr.Zero;
        }
    }

    internal static class WorldDpi
    {
        static bool noGetDpiForWindow;

        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        public static double ForWindow(IntPtr hwnd)
        {
            if (!noGetDpiForWindow)
            {
                try
                {
                    uint d = GetDpiForWindow(hwnd);
                    if (d >= 48 && d <= 960) return d / 96.0;
                }
                catch (EntryPointNotFoundException) { noGetDpiForWindow = true; }
                catch { }
            }
            return 1.0;
        }

        public static double ForMonitor(IntPtr hmon)
        {
            try
            {
                uint x, y;
                if (WorldNative.GetDpiForMonitor(hmon, WorldNative.MDT_EFFECTIVE_DPI, out x, out y) == 0 && x >= 48 && x <= 960) return x / 96.0;
            }
            catch { }
            return 1.0;
        }
    }
}
