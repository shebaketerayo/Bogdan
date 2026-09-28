using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopCat
{
    internal sealed class CatWindowSurface : IDisposable
    {
        public int Width, Height;
        public IntPtr MemDC, HBitmap, Bits;
        IntPtr oldBitmap;
        public Bitmap Bitmap;
        public Graphics Graphics;

        public CatWindowSurface(int w, int h)
        {
            Width = Math.Max(1, w);
            Height = Math.Max(1, h);
            MemDC = AppNative.CreateCompatibleDC(IntPtr.Zero);
            if (MemDC == IntPtr.Zero) throw new InvalidOperationException("CreateCompatibleDC failed");
            AppNative.BITMAPINFOHEADER bi = new AppNative.BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(AppNative.BITMAPINFOHEADER));
            bi.biWidth = Width;
            bi.biHeight = -Height;         // top-down
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            bi.biCompression = 0;
            IntPtr bits;
            HBitmap = AppNative.CreateDIBSection(MemDC, ref bi, 0, out bits, IntPtr.Zero, 0);
            if (HBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                AppNative.DeleteDC(MemDC);
                MemDC = IntPtr.Zero;
                throw new InvalidOperationException("CreateDIBSection failed");
            }
            Bits = bits;
            oldBitmap = AppNative.SelectObject(MemDC, HBitmap);
            Bitmap = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, Bits);
            Graphics = Graphics.FromImage(Bitmap);
            Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            Graphics.SmoothingMode = SmoothingMode.None;
            Graphics.CompositingMode = CompositingMode.SourceOver;
            Graphics.CompositingQuality = CompositingQuality.HighSpeed;
            Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        }

        public void Dispose()
        {
            if (Graphics != null) { Graphics.Dispose(); Graphics = null; }
            if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
            if (MemDC != IntPtr.Zero)
            {
                if (oldBitmap != IntPtr.Zero) AppNative.SelectObject(MemDC, oldBitmap);
                AppNative.DeleteDC(MemDC);
                MemDC = IntPtr.Zero;
            }
            if (HBitmap != IntPtr.Zero) { AppNative.DeleteObject(HBitmap); HBitmap = IntPtr.Zero; }
            Bits = IntPtr.Zero;
        }
    }

    public sealed class CatWindow : Form
    {
        // free canvas around the sprite frame in art px, room for effects
        public const int MarginL = 16, MarginR = 16, MarginT = 24, MarginB = 4;

        readonly CatWindowInput input = new CatWindowInput();
        readonly EffectsSystem effects = new EffectsSystem();
        readonly ImageAttributes spriteAttrs = new ImageAttributes();
        CatWindowSurface surface;
        ICatSprites sprites;

        Bitmap shownFrame;
        int shownScale = -1, shownFW = -1, shownFH = -1, shownFxSig = int.MinValue;
        bool contentValid;
        int winX = int.MinValue, winY = int.MinValue;
        bool shown;

        Bitmap curFrame;
        byte[] curMask;
        Bitmap maskOf;
        int curScale = 3;
        Point curAnchor;
        int curFW, curFH;
        Rectangle curBody;
        bool curFaceLeft;
        double lastX, lastY;
        bool havePos;
        double lastMouseMsg = -10;
        Func<double> clock;

        public event Action<Point> MenuRequested;

        public bool EffectsEnabled = true;

        public CatWindow(Func<double> clock)
        {
            this.clock = clock;
            FormBorderStyle = FormBorderStyle.None;
            // no ShowInTaskbar=false: winforms implements it with a hidden owner window, desktop mode needs an
            // unowned one, and the tool-window style already keeps the cat out of the taskbar and alt+tab
            StartPosition = FormStartPosition.Manual;
            Text = "DesktopCat";
            spriteAttrs.SetWrapMode(WrapMode.TileFlipXY);
            input.DoubleClickTime = Math.Max(0.2, Math.Min(1.5, AppNative.GetDoubleClickTime() / 1000.0));
        }

        public EffectsSystem Effects { get { return effects; } }
        public CatWindowInput Input { get { return input; } }
        public bool IsShown { get { return shown; } }
        public Rectangle WindowRect
        {
            get { return surface == null ? Rectangle.Empty : new Rectangle(winX, winY, surface.Width, surface.Height); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style = AppNative.WS_POPUP;
                cp.ExStyle = AppNative.WS_EX_LAYERED | AppNative.WS_EX_TOOLWINDOW | AppNative.WS_EX_NOACTIVATE;
                cp.ClassStyle |= AppNative.CS_DBLCLKS;
                cp.Parent = IntPtr.Zero;
                cp.X = -32000; cp.Y = -32000; cp.Width = 1; cp.Height = 1;
                return cp;
            }
        }

        public void SetSprites(ICatSprites s)
        {
            sprites = s;
            shownFrame = null;
            curFrame = null;
            maskOf = null;
            contentValid = false;
        }

        protected override void WndProc(ref Message m)
        {
            try
            {
                switch (m.Msg)
                {
                    case AppNative.WM_MOUSEACTIVATE:
                        m.Result = (IntPtr)AppNative.MA_NOACTIVATE;
                        return;
                    case AppNative.WM_ERASEBKGND:
                        m.Result = (IntPtr)1;
                        return;
                    case AppNative.WM_DPICHANGED:
                        m.Result = IntPtr.Zero;   // we size ourselves in physical pixels
                        return;
                    case AppNative.WM_SETCURSOR:
                        if ((m.LParam.ToInt64() & 0xFFFF) == AppNative.HTCLIENT)
                        {
                            Point c = AppNative.CursorPos();
                            bool hand = input.Pressed || HitCat(c);
                            AppNative.SetCursor(hand ? AppNative.HandCursor : AppNative.ArrowCursor);
                            m.Result = (IntPtr)1;
                            return;
                        }
                        break;
                    case AppNative.WM_MOUSEMOVE:
                        {
                            double t = clock();
                            lastMouseMsg = t;
                            input.MouseMove(t, AppNative.MessagePoint(Handle, m.LParam));
                            m.Result = IntPtr.Zero;
                            return;
                        }
                    case AppNative.WM_LBUTTONDOWN:
                    case AppNative.WM_LBUTTONDBLCLK:
                        {
                            double t = clock();
                            Point c = AppNative.MessagePoint(Handle, m.LParam);
                            bool over = HitCat(c);
                            input.MouseDown(t, c, over, m.Msg == AppNative.WM_LBUTTONDBLCLK);
                            if (over) AppNative.SetCapture(Handle);
                            m.Result = IntPtr.Zero;
                            return;
                        }
                    case AppNative.WM_LBUTTONUP:
                        {
                            input.MouseUp(clock(), AppNative.MessagePoint(Handle, m.LParam));
                            if (AppNative.GetCapture() == Handle) AppNative.ReleaseCapture();
                            m.Result = IntPtr.Zero;
                            return;
                        }
                    case AppNative.WM_CAPTURECHANGED:
                        if (m.LParam != Handle && input.Pressed)
                            input.Cancel(clock(), true);
                        m.Result = IntPtr.Zero;
                        return;
                    case AppNative.WM_RBUTTONDOWN:
                    case AppNative.WM_RBUTTONDBLCLK:
                    case AppNative.WM_MBUTTONDOWN:
                    case AppNative.WM_MBUTTONUP:
                        m.Result = IntPtr.Zero;
                        return;
                    case AppNative.WM_RBUTTONUP:
                        {
                            m.Result = IntPtr.Zero;
                            if (!input.Pressed && MenuRequested != null)
                            {
                                Point c = AppNative.CursorPos();
                                MenuRequested(c);
                            }
                            return;
                        }
                }
            }
            catch (Exception ex)
            {
                CatLog.Error("CatWindow.WndProc " + m.Msg, ex);
            }
            base.WndProc(ref m);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public bool HitCat(Point screen)
        {
            if (curFrame == null || curMask == null || surface == null || curScale <= 0) return false;
            int ax = FloorDiv(screen.X - winX, curScale) - MarginL;
            int ay = FloorDiv(screen.Y - winY, curScale) - MarginT;
            if (ax < 0 || ay < 0 || ax >= curFW || ay >= curFH) return false;
            return curMask[ay * curFW + ax] >= 40;
        }

        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        bool CursorOverCatUncovered(Point c)
        {
            if (!shown || !HitCat(c)) return false;
            AppNative.POINT p = new AppNative.POINT(c.X, c.Y);
            IntPtr w = AppNative.WindowFromPoint(p);
            return w == Handle;
        }

        Rectangle BodyScreenRect(int padArt)
        {
            Rectangle b = curBody;
            if (b.Width <= 0 || b.Height <= 0) b = new Rectangle(0, 0, curFW, curFH);
            b.Inflate(padArt, padArt);
            return new Rectangle(winX + (MarginL + b.X) * curScale, winY + (MarginT + b.Y) * curScale,
                                 b.Width * curScale, b.Height * curScale);
        }

        public void PollInput(double t, Point cursor, CatInput ci)
        {
            bool over = false, near = false, fxPixel = false;
            if (shown && surface != null && WindowRect.Contains(cursor))
            {
                bool catPixel = HitCat(cursor);
                if (catPixel)
                {
                    SetClickThrough(false);                 // before WindowFromPoint, it skips transparent windows
                    over = CursorOverCatUncovered(cursor);
                }
                else fxPixel = SurfaceAlphaAt(cursor) != 0;
                if (over) near = true;
                else if (BodyScreenRect(2).Contains(cursor))
                {
                    // near the body counts only while mouse messages keep arriving, i.e. the cat is not covered
                    near = (t - lastMouseMsg) < 0.35;
                }
            }
            double step = Math.Max(4, 2.0 * curScale);
            double longPath = Math.Max(120, 60.0 * curScale);
            input.Tick(t, cursor, over, near, AppNative.LeftButtonDown(), step, longPath);
            input.Fill(t, ci);
            // a button-up we never received (detected by polling) must not leave the mouse captured
            if (!input.Pressed && IsHandleCreated && AppNative.GetCapture() == Handle) AppNative.ReleaseCapture();
            // only the cat's own pixels may block clicks: over an effect pixel the whole window is click-through
            // until the cursor leaves it
            SetClickThrough(fxPixel && !input.Pressed && !input.Dragging);
        }

        bool clickThrough;

        void SetClickThrough(bool on)
        {
            if (on == clickThrough || !IsHandleCreated) return;
            try
            {
                int ex = AppNative.GetWindowLong(Handle, AppNative.GWL_EXSTYLE);
                int want = on ? (ex | AppNative.WS_EX_TRANSPARENT) : (ex & ~AppNative.WS_EX_TRANSPARENT);
                if (want != ex) AppNative.SetWindowLong(Handle, AppNative.GWL_EXSTYLE, want);
                clickThrough = on;
            }
            catch (Exception ex2) { CatLog.Error("CatWindow.SetClickThrough", ex2); }
        }

        int SurfaceAlphaAt(Point screen)
        {
            if (surface == null || !contentValid) return 0;
            int x = screen.X - winX, y = screen.Y - winY;
            if (x < 0 || y < 0 || x >= surface.Width || y >= surface.Height) return 0;
            return Marshal.ReadByte(surface.Bits, (y * surface.Width + x) * 4 + 3);
        }

        public bool CursorNear(Point cursor)
        {
            if (!shown || surface == null) return false;
            Rectangle r = WindowRect;
            r.Inflate(8, 8);
            return r.Contains(cursor);
        }

        public void CancelInteraction()
        {
            input.Cancel(clock(), false);
            if (IsHandleCreated && AppNative.GetCapture() == Handle) AppNative.ReleaseCapture();
        }

        public void SpawnEffect(CatEffect e, CatFrameOutput o, int scale)
        {
            if (!EffectsEnabled || sprites == null || o == null) return;
            try
            {
                if (e == CatEffect.Shred)
                {
                    // fluff flies from the claws, not from the head
                    double s = scale >= 1 ? scale : 1;
                    effects.Spawn(e, (o.ScratchX - o.X) / s, (o.ScratchY - o.Y) / s, o.FaceLeft);
                    return;
                }
                int frame = sprites.FrameAt(o.Anim, o.AnimTime);
                Point hp = sprites.HeadPoint(o.Anim, frame);
                Point a = sprites.Anchor;
                int hx = o.FaceLeft ? sprites.FrameWidth - 1 - hp.X : hp.X;
                effects.Spawn(e, hx - a.X + 0.5, hp.Y - a.Y + 0.5, o.FaceLeft);
            }
            catch (Exception ex) { CatLog.Error("CatWindow.SpawnEffect", ex); }
        }

        public bool SetShown(bool want)
        {
            if (!IsHandleCreated) { IntPtr h = Handle; }
            if (want == shown) return false;
            if (want)
            {
                if (!contentValid) return false;   // never flash an empty or stale window
                AppNative.ShowWindow(Handle, AppNative.SW_SHOWNOACTIVATE);
                shown = true;
            }
            else
            {
                CancelInteraction();
                AppNative.ShowWindow(Handle, AppNative.SW_HIDE);
                shown = false;
                effects.Clear();
            }
            return true;
        }

        public void Present(CatFrameOutput o, int scale, double dt)
        {
            if (o == null || sprites == null) return;
            if (!IsHandleCreated) { IntPtr h = Handle; }
            if (scale < 1) scale = 1;

            int fw = sprites.FrameWidth, fh = sprites.FrameHeight;
            Point anchor = sprites.Anchor;
            int frameIdx = sprites.FrameAt(o.Anim, o.AnimTime);
            Bitmap frame = sprites.GetFrame(o.Anim, frameIdx, o.FaceLeft);

            double dxArt = havePos ? (o.X - lastX) / scale : 0;
            double dyArt = havePos ? (o.Y - lastY) / scale : 0;
            lastX = o.X; lastY = o.Y; havePos = true;
            effects.Update(dt, dxArt, dyArt);
            if (!EffectsEnabled && effects.Alive) effects.Clear();

            curFrame = frame;
            curScale = scale;
            curAnchor = anchor;
            curFW = fw; curFH = fh;
            curFaceLeft = o.FaceLeft;
            try
            {
                Rectangle b = sprites.BodyBounds(o.Anim, frameIdx);
                if (o.FaceLeft) b = new Rectangle(fw - b.Right, b.Y, b.Width, b.Height);
                curBody = b;
            }
            catch { curBody = new Rectangle(0, 0, fw, fh); }
            if (frame != null && !ReferenceEquals(frame, maskOf)) BuildMask(frame);

            int cw = (fw + MarginL + MarginR) * scale;
            int chh = (fh + MarginT + MarginB) * scale;
            Rectangle canvasArt = new Rectangle(-(anchor.X + MarginL), -(anchor.Y + MarginT), fw + MarginL + MarginR, fh + MarginT + MarginB);
            int fxSig = effects.Alive ? effects.Signature(canvasArt) : 0;

            bool sizeChanged = surface == null || surface.Width != cw || surface.Height != chh;
            if (sizeChanged)
            {
                if (surface != null) { surface.Dispose(); surface = null; }
                surface = new CatWindowSurface(cw, chh);
                contentValid = false;
            }

            int left = (int)Math.Round(o.X - (anchor.X + MarginL) * (double)scale);
            int top = (int)Math.Round(o.Y - (anchor.Y + MarginT) * (double)scale);

            bool needRender = !contentValid || !ReferenceEquals(frame, shownFrame) || scale != shownScale ||
                              fw != shownFW || fh != shownFH || fxSig != shownFxSig;
            if (needRender)
            {
                Render(frame, anchor, scale, canvasArt);
                shownFrame = frame; shownScale = scale; shownFW = fw; shownFH = fh; shownFxSig = fxSig;
                if (Upload(left, top)) { contentValid = true; winX = left; winY = top; }
            }
            else if (left != winX || top != winY)
            {
                AppNative.BLENDFUNCTION bf = Blend();
                AppNative.POINT dst = new AppNative.POINT(left, top);
                if (!AppNative.UpdateLayeredWindowMove(Handle, IntPtr.Zero, ref dst, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref bf, AppNative.ULW_ALPHA))
                    AppNative.SetWindowPos(Handle, IntPtr.Zero, left, top, 0, 0,
                        AppNative.SWP_NOSIZE | AppNative.SWP_NOZORDER | AppNative.SWP_NOACTIVATE | AppNative.SWP_NOOWNERZORDER);
                winX = left; winY = top;
            }
        }

        void Render(Bitmap frame, Point anchor, int scale, Rectangle canvasArt)
        {
            Graphics g = surface.Graphics;
            g.Clear(Color.Transparent);
            int ax = (anchor.X + MarginL) * scale, ay = (anchor.Y + MarginT) * scale;
            if (effects.Alive) effects.Draw(g, ax, ay, scale, true, canvasArt);
            if (frame != null)
            {
                Rectangle dest = new Rectangle(MarginL * scale, MarginT * scale, frame.Width * scale, frame.Height * scale);
                g.DrawImage(frame, dest, 0, 0, frame.Width, frame.Height, GraphicsUnit.Pixel, spriteAttrs);
            }
            if (effects.Alive) effects.Draw(g, ax, ay, scale, false, canvasArt);
            g.Flush(FlushIntention.Sync);
        }

        static AppNative.BLENDFUNCTION Blend()
        {
            AppNative.BLENDFUNCTION bf = new AppNative.BLENDFUNCTION();
            bf.BlendOp = AppNative.AC_SRC_OVER;
            bf.SourceConstantAlpha = 255;
            bf.AlphaFormat = AppNative.AC_SRC_ALPHA;
            return bf;
        }

        bool Upload(int left, int top)
        {
            AppNative.BLENDFUNCTION bf = Blend();
            AppNative.POINT dst = new AppNative.POINT(left, top);
            AppNative.SIZE size = new AppNative.SIZE(surface.Width, surface.Height);
            AppNative.POINT src = new AppNative.POINT(0, 0);
            bool ok = AppNative.UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, surface.MemDC, ref src, 0, ref bf, AppNative.ULW_ALPHA);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                if (uploadErrors++ < 5) CatLog.Write("UpdateLayeredWindow failed, error " + err);
            }
            return ok;
        }
        int uploadErrors;

        void BuildMask(Bitmap frame)
        {
            maskOf = frame;
            int w = frame.Width, h = frame.Height;
            if (curMask == null || curMask.Length != w * h) curMask = new byte[w * h];
            BitmapData d = null;
            try
            {
                d = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] row = rowBuf != null && rowBuf.Length >= w * 4 ? rowBuf : (rowBuf = new byte[w * 4]);
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, w * 4);
                    for (int x = 0; x < w; x++) curMask[y * w + x] = row[x * 4 + 3];
                }
            }
            catch (Exception ex)
            {
                CatLog.Error("CatWindow.BuildMask", ex);
                for (int i = 0; i < curMask.Length; i++) curMask[i] = 255;
            }
            finally
            {
                if (d != null) try { frame.UnlockBits(d); } catch { }
            }
            // the mask is indexed with the sprite's FrameWidth; keep them consistent
            curFW = w; curFH = h;
        }
        byte[] rowBuf;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (surface != null) { surface.Dispose(); surface = null; }
                spriteAttrs.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
