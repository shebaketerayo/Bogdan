using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopCat
{
    internal static class WorldNative
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width { get { return Right - Left; } }
            public int Height { get { return Bottom - Top; } }
            public System.Drawing.Rectangle ToRectangle() { return System.Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        public const uint WS_CAPTION = 0x00C00000;

        public const uint WS_EX_TOPMOST = 0x00000008;
        public const uint WS_EX_TRANSPARENT = 0x00000020;
        public const uint WS_EX_TOOLWINDOW = 0x00000080;
        public const uint WS_EX_APPWINDOW = 0x00040000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint WS_EX_NOACTIVATE = 0x08000000;

        public const uint GW_HWNDNEXT = 2;
        public const uint GW_HWNDPREV = 3;
        public const uint GW_OWNER = 4;
        public const uint GA_ROOT = 2;
        public const uint GA_ROOTOWNER = 3;

        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_NOOWNERZORDER = 0x0200;

        public static readonly IntPtr HWND_TOP = IntPtr.Zero;
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);

        public const uint LWA_ALPHA = 0x2;

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;

        public const uint MONITORINFOF_PRIMARY = 1;
        public const int MDT_EFFECTIVE_DPI = 0;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        // reads the title without sending WM_GETTEXT, so a hung window can never block the caller
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int InternalGetWindowText(IntPtr hwnd, StringBuilder sb, int max);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong32(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

        public static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        {
            if (IntPtr.Size == 8) return GetWindowLongPtr64(hwnd, index);
            return new IntPtr(GetWindowLong32(hwnd, index));
        }

        public static uint GetStyle(IntPtr hwnd) { return (uint)GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64(); }
        public static uint GetExStyle(IntPtr hwnd) { return (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64(); }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rc);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClientToScreen(IntPtr hwnd, ref POINT pt);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindow(string cls, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetLayeredWindowAttributes(IntPtr hwnd, out uint crKey, out byte alpha, out uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr obj);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int state);

        [DllImport("shell32.dll")]
        public static extern int SHGetDesktopFolder([MarshalAs(UnmanagedType.Interface)] out IShellFolder ppshf);

        [DllImport("shell32.dll")]
        public static extern uint ILGetSize(IntPtr pidl);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrRetToBufW(IntPtr pstr, IntPtr pidl, StringBuilder buf, uint cch);

        [DllImport("ole32.dll")]
        public static extern int CoEnableCallCancellation(IntPtr reserved);

        [DllImport("ole32.dll")]
        public static extern int CoCancelCall(uint threadId, uint timeoutMs);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        public static string WindowText(IntPtr hwnd, StringBuilder sb)
        {
            try
            {
                sb.Length = 0;
                if (sb.Capacity < 512) sb.Capacity = 512;
                int n = InternalGetWindowText(hwnd, sb, 512);
                return n > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        public static string ClassName(IntPtr hwnd, StringBuilder sb)
        {
            try
            {
                sb.Length = 0;
                if (sb.Capacity < 256) sb.Capacity = 256;
                int n = GetClassName(hwnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : "";
            }
            catch { return ""; }
        }

        public static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked;
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, 4) == 0) return cloaked != 0;
            }
            catch { }
            return false;
        }

        public static bool GetFrame(IntPtr hwnd, out RECT rc)
        {
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rc, Marshal.SizeOf(typeof(RECT))) == 0
                    && rc.Right > rc.Left && rc.Bottom > rc.Top)
                    return true;
            }
            catch { }
            return GetWindowRect(hwnd, out rc);
        }

        public const int CSIDL_DESKTOP = 0;
        public const int SWC_DESKTOP = 8;
        public const int SWFO_NEEDDISPATCH = 1;
        public const uint SVGIO_ALLVIEW = 2;
        public const uint SHGDN_INFOLDER = 1;

        public const uint SFGAO_LINK = 0x00010000;
        public const uint SFGAO_STREAM = 0x00400000;
        public const uint SFGAO_FOLDER = 0x20000000;
        public const uint SFGAO_FILESYSTEM = 0x40000000;

        public static readonly Guid CLSID_ShellWindows = new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
        public static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        public static readonly Guid IID_IShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");
        public static readonly Guid IID_IEnumIDList = new Guid("000214F2-0000-0000-C000-000000000046");
        public static readonly Guid IID_IShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");

        // com interop does not inherit vtables: the slots of base interfaces (IDispatch, IOleWindow, IFolderView)
        // are redeclared first, in vtable order
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellWindows
        {
            [PreserveSig] int GetTypeInfoCount(out uint n);
            [PreserveSig] int GetTypeInfo(uint i, uint lcid, out IntPtr ti);
            [PreserveSig] int GetIDsOfNames(IntPtr riid, IntPtr names, uint c, uint lcid, IntPtr ids);
            [PreserveSig] int Invoke(int id, IntPtr riid, uint lcid, ushort flags, IntPtr p, IntPtr r, IntPtr e, IntPtr a);
            [PreserveSig] int get_Count(out int count);
            [PreserveSig] int Item_(IntPtr v1, IntPtr v2, IntPtr v3, out IntPtr folder); // placeholder slot: the real one takes a VARIANT by value
            [PreserveSig] int _NewEnum(out IntPtr ppunk);
            [PreserveSig] int Register(IntPtr pid, int hwnd, int swClass, out int cookie);
            [PreserveSig] int RegisterPending(int threadId, IntPtr loc, IntPtr locRoot, int swClass, out int cookie);
            [PreserveSig] int Revoke(int cookie);
            [PreserveSig] int OnNavigate(int cookie, IntPtr loc);
            [PreserveSig] int OnActivated(int cookie, short active);
            [PreserveSig] int FindWindowSW([In] ref object pvarLoc, [In] ref object pvarLocRoot, int swClass, out int phwnd,
                                           int swfwOptions, [MarshalAs(UnmanagedType.IDispatch)] out object ppdispOut);
            [PreserveSig] int OnCreated(int cookie, IntPtr punk);
            [PreserveSig] int ProcessAttachDetach(short attach);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleServiceProvider
        {
            [PreserveSig] int QueryService([In] ref Guid guidService, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleWindow
        {
            [PreserveSig] int GetWindow(out IntPtr phwnd);
            [PreserveSig] int ContextSensitiveHelp(int enter);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr phwnd);
            [PreserveSig] int ContextSensitiveHelp(int enter);
            [PreserveSig] int InsertMenusSB(IntPtr hmenuShared, IntPtr lpMenuWidths);
            [PreserveSig] int SetMenuSB(IntPtr hmenuShared, IntPtr holemenuRes, IntPtr hwndActiveObject);
            [PreserveSig] int RemoveMenusSB(IntPtr hmenuShared);
            [PreserveSig] int SetStatusTextSB(IntPtr text);
            [PreserveSig] int EnableModelessSB(int enable);
            [PreserveSig] int TranslateAcceleratorSB(IntPtr pmsg, ushort wID);
            [PreserveSig] int BrowseObject(IntPtr pidl, uint flags);
            [PreserveSig] int GetViewStateStream(uint mode, out IntPtr strm);
            [PreserveSig] int GetControlWindow(uint id, out IntPtr phwnd);
            [PreserveSig] int SendControlMsg(uint id, uint msg, IntPtr wParam, IntPtr lParam, out IntPtr ret);
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object ppshv);
            [PreserveSig] int OnViewWindowActive(IntPtr pshv);
            [PreserveSig] int SetToolbarItems(IntPtr lpButtons, uint nButtons, uint flags);
        }

        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFolderView
        {
            [PreserveSig] int GetCurrentViewMode(out uint viewMode);
            [PreserveSig] int SetCurrentViewMode(uint viewMode);
            [PreserveSig] int GetFolder([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int Item(int index, out IntPtr ppidl);
            [PreserveSig] int ItemCount(uint flags, out int count);
            [PreserveSig] int Items(uint flags, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int GetSelectionMarkedItem(out int item);
            [PreserveSig] int GetFocusedItem(out int item);
            [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT pt);
            [PreserveSig] int GetSpacing(ref POINT pt);
            [PreserveSig] int GetDefaultSpacing(out POINT pt);
            [PreserveSig] int GetAutoArrange();
            [PreserveSig] int SelectItem(int item, uint flags);
            [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint flags);
        }

        [ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IFolderView2
        {
            [PreserveSig] int GetCurrentViewMode(out uint viewMode);
            [PreserveSig] int SetCurrentViewMode(uint viewMode);
            [PreserveSig] int GetFolder([In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int Item(int index, out IntPtr ppidl);
            [PreserveSig] int ItemCount(uint flags, out int count);
            [PreserveSig] int Items(uint flags, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int GetSelectionMarkedItem(out int item);
            [PreserveSig] int GetFocusedItem(out int item);
            [PreserveSig] int GetItemPosition(IntPtr pidl, out POINT pt);
            [PreserveSig] int GetSpacing(ref POINT pt);
            [PreserveSig] int GetDefaultSpacing(out POINT pt);
            [PreserveSig] int GetAutoArrange();
            [PreserveSig] int SelectItem(int item, uint flags);
            [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint flags);
            [PreserveSig] int SetGroupBy(IntPtr key, int ascending);
            [PreserveSig] int GetGroupBy(IntPtr pkey, out int ascending);
            [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr key, IntPtr propvar);
            [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr key, IntPtr propvar);
            [PreserveSig] int SetTileViewProperties(IntPtr pidl, IntPtr propList);
            [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, IntPtr propList);
            [PreserveSig] int SetText(int type, IntPtr text);
            [PreserveSig] int SetCurrentFolderFlags(uint mask, uint flags);
            [PreserveSig] int GetCurrentFolderFlags(out uint flags);
            [PreserveSig] int GetSortColumnCount(out int count);
            [PreserveSig] int SetSortColumns(IntPtr cols, int count);
            [PreserveSig] int GetSortColumns(IntPtr cols, int count);
            [PreserveSig] int GetItem(int item, IntPtr riid, out IntPtr ppv);
            [PreserveSig] int GetVisibleItem(int start, int previous, out int item);
            [PreserveSig] int GetSelectedItem(int start, out int item);
            [PreserveSig] int GetSelection(int noneImpliesFolder, out IntPtr ppsia);
            [PreserveSig] int GetSelectionState(IntPtr pidl, out uint flags);
            [PreserveSig] int InvokeVerbOnSelection(IntPtr verb);
            [PreserveSig] int SetViewModeAndIconSize(int viewMode, int imageSize);
            [PreserveSig] int GetViewModeAndIconSize(out int viewMode, out int imageSize);
            [PreserveSig] int SetGroupSubsetCount(uint rows);
            [PreserveSig] int GetGroupSubsetCount(out uint rows);
            [PreserveSig] int SetRedraw(int redrawOn);
            [PreserveSig] int IsMoveInSameFolder();
            [PreserveSig] int DoRename();
        }

        [ComImport, Guid("000214F2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IEnumIDList
        {
            [PreserveSig] int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] rgelt, out uint fetched);
            [PreserveSig] int Skip(uint celt);
            [PreserveSig] int Reset();
            [PreserveSig] int Clone(out IntPtr ppenum);
        }

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr pchEaten, out IntPtr ppidl, IntPtr pdwAttributes);
            [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr ppenum);
            [PreserveSig] int BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
            [PreserveSig] int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
            [PreserveSig] int GetAttributesOf(uint cidl, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
            [PreserveSig] int GetUIObjectOf(IntPtr hwndOwner, uint cidl, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl,
                                            [In] ref Guid riid, IntPtr rgfReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
            [PreserveSig] int GetDisplayNameOf(IntPtr pidl, uint flags, IntPtr pName);
            [PreserveSig] int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out IntPtr ppidlOut);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAP
        {
            public int bmType, bmWidth, bmHeight, bmWidthBytes;
            public short bmPlanes, bmBitsPixel;
            public IntPtr bmBits;
        }

        public const int SIIGBF_ICONONLY = 0x4, SIIGBF_THUMBNAILONLY = 0x8, SIIGBF_INCACHEONLY = 0x10;
        public static readonly Guid IID_IShellItemImageFactory = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll")]
        public static extern int SHCreateItemFromIDList(IntPtr pidl, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
        public static extern int GetObjectBitmap(IntPtr h, int size, out BITMAP bm);

        [DllImport("gdi32.dll")]
        public static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, [Out] int[] bits, IntPtr bmi, uint usage);

        // declared only up to the slots we call; the order up to them must match the vtable
        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IShellLinkW
        {
            [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr pfd, uint flags);
            [PreserveSig] int GetIDList(out IntPtr ppidl);
        }
    }
}
