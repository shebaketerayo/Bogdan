using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;

namespace DesktopCat
{
    internal sealed class SpriteFrame
    {
        public string Name;
        public byte[] Pix;
        public Point Head;
        public Rectangle Bounds;
    }

    internal sealed class SpriteAnimDef
    {
        public bool Loop;
        public int[] Frames;
        public double[] Durations;
        public double Total;
    }

    internal static class SpriteLibrary
    {
        public const byte Clear = 255;
        public const byte Pupil = 12;
        public const byte Shine = 13;
        public const byte ChestStripe = 14;
        public const int CodeCount = 15;

        public static readonly int Width;
        public static readonly int Height;
        public static readonly SpriteFrame[] Frames;
        public static readonly SpriteAnimDef[] Anims;
        public static readonly int AnimCount;

        static SpriteLibrary()
        {
            AnimCount = Enum.GetValues(typeof(CatAnim)).Length;
            Width = SpriteArt.Width;
            Height = SpriteArt.Height;
            List<SpriteFrame> frames = new List<SpriteFrame>();
            SpriteAnimDef[] anims = new SpriteAnimDef[AnimCount];
            string err = null;
            try
            {
                ParseFrames(SpriteArt.Frames, frames);
                ParseAnims(SpriteArt.Anims, frames, anims, ref err);
            }
            catch (Exception ex)
            {
                err = "sprite art failed to parse: " + ex.Message;
                frames.Clear();
                for (int i = 0; i < anims.Length; i++) anims[i] = null;
            }
            if (frames.Count == 0) frames.Add(Placeholder());
            for (int i = 0; i < anims.Length; i++)
            {
                if (anims[i] != null) continue;
                SpriteAnimDef fallback = anims[(int)CatAnim.Sit];
                if (fallback == null)
                {
                    fallback = new SpriteAnimDef();
                    fallback.Loop = true;
                    fallback.Frames = new int[] { 0 };
                    fallback.Durations = new double[] { 0.5 };
                    fallback.Total = 0.5;
                }
                anims[i] = fallback;
                if (err == null) err = "missing animation " + (CatAnim)i;
            }
            Frames = frames.ToArray();
            Anims = anims;
            if (err != null) CatLog.Write("sprites: " + err);
        }

        public static byte Code(char c)
        {
            switch (c)
            {
                case 'o': return (byte)PaletteSlot.Outline;
                case 'b': return (byte)PaletteSlot.Fur;
                case 'd': return (byte)PaletteSlot.FurShade;
                case 's': return (byte)PaletteSlot.Stripes;
                case 'l': return (byte)PaletteSlot.Light;
                case 'f': return (byte)PaletteSlot.Paws;
                case 'x': return (byte)PaletteSlot.Points;
                case 'm': return (byte)PaletteSlot.Muzzle;
                case 'p': return (byte)PaletteSlot.PatchA;
                case 'q': return (byte)PaletteSlot.PatchB;
                case 'e': return (byte)PaletteSlot.Eye;
                case 'n': return (byte)PaletteSlot.Pink;
                case 'k': return Pupil;
                case 'h': return Shine;
                case 'c': return ChestStripe;
                default: return Clear;
            }
        }

        // rows are bottom-aligned: missing top rows stay clear
        static void ParseFrames(string text, List<SpriteFrame> frames)
        {
            string[] lines = text.Replace("\r", "").Split('\n');
            string name = null, header = null;
            List<string> rows = new List<string>();
            for (int i = 0; i <= lines.Length; i++)
            {
                string line = i < lines.Length ? lines[i].TrimEnd() : "@";
                if (line.Length == 0) continue;
                if (line[0] == '@')
                {
                    if (name != null) frames.Add(MakeFrame(name, header, rows));
                    rows.Clear();
                    header = line.Substring(1).Trim();
                    int sp = header.IndexOf(' ');
                    name = sp < 0 ? header : header.Substring(0, sp);
                    if (name.Length == 0) name = null;
                    continue;
                }
                if (name != null) rows.Add(line);
            }
        }

        static SpriteFrame MakeFrame(string name, string header, List<string> rows)
        {
            int w = Width, h = Height;
            SpriteFrame f = new SpriteFrame();
            f.Name = name;
            f.Pix = new byte[w * h];
            for (int i = 0; i < f.Pix.Length; i++) f.Pix[i] = Clear;
            int top = h - rows.Count;
            for (int r = 0; r < rows.Count; r++)
            {
                int y = top + r;
                if (y < 0) continue;
                string row = rows[r];
                for (int x = 0; x < w && x < row.Length; x++) f.Pix[y * w + x] = Code(row[x]);
            }
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (f.Pix[y * w + x] != Clear)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            f.Bounds = maxX < 0 ? new Rectangle(w / 2, h - 1, 1, 1) : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            f.Head = new Point(f.Bounds.Left + f.Bounds.Width / 2, f.Bounds.Top + Math.Min(6, f.Bounds.Height / 2));
            foreach (string kv in header.Split(' '))
            {
                if (!kv.StartsWith("head=", StringComparison.Ordinal)) continue;
                string[] xy = kv.Substring(5).Split(',');
                int hx, hy;
                if (xy.Length == 2 && int.TryParse(xy[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hx)
                    && int.TryParse(xy[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hy))
                    f.Head = new Point(Math.Max(0, Math.Min(w - 1, hx)), Math.Max(0, Math.Min(h - 1, hy)));
            }
            return f;
        }

        static void ParseAnims(string text, List<SpriteFrame> frames, SpriteAnimDef[] anims, ref string err)
        {
            Dictionary<string, int> index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < frames.Count; i++) index[frames[i].Name] = i;
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string[] tok = raw.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (tok.Length < 3) continue;
                CatAnim anim;
                try { anim = (CatAnim)Enum.Parse(typeof(CatAnim), tok[0], false); }
                catch { if (err == null) err = "unknown animation " + tok[0]; continue; }
                List<int> fr = new List<int>();
                List<double> du = new List<double>();
                for (int k = 2; k < tok.Length; k++)
                {
                    int c = tok[k].IndexOf(':');
                    string fn = c < 0 ? tok[k] : tok[k].Substring(0, c);
                    double d = 0.2;
                    if (c >= 0) double.TryParse(tok[k].Substring(c + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
                    if (!(d > 0.005) || d > 60) d = 0.2;
                    int fi;
                    if (!index.TryGetValue(fn, out fi)) { if (err == null) err = "missing frame " + fn; continue; }
                    fr.Add(fi);
                    du.Add(d);
                }
                if (fr.Count == 0) continue;
                SpriteAnimDef def = new SpriteAnimDef();
                def.Loop = tok[1] == "loop";
                def.Frames = fr.ToArray();
                def.Durations = du.ToArray();
                double t = 0;
                for (int k = 0; k < def.Durations.Length; k++) t += def.Durations[k];
                def.Total = t;
                anims[(int)anim] = def;
            }
        }

        static SpriteFrame Placeholder()
        {
            List<string> rows = new List<string>();
            rows.Add("oooooo");
            rows.Add("obbbbo");
            rows.Add("obbbbo");
            rows.Add("oooooo");
            for (int i = 0; i < rows.Count; i++) rows[i] = new string('.', Width / 2 - 3) + rows[i];
            return MakeFrame("placeholder", "placeholder", rows);
        }

        public static SpriteAnimDef Def(CatAnim anim)
        {
            int i = (int)anim;
            if (i < 0 || i >= Anims.Length) i = (int)CatAnim.Sit;
            return Anims[i];
        }

        public static int FrameIndex(CatAnim anim, int frame)
        {
            SpriteAnimDef d = Def(anim);
            int n = d.Frames.Length;
            if (frame < 0) frame = 0;
            if (frame >= n) frame = d.Loop ? frame % n : n - 1;
            return d.Frames[frame];
        }
    }

    internal sealed class CatSprites : ICatSprites
    {
        readonly CatPalette palette;
        readonly int[] argb = new int[SpriteLibrary.CodeCount];
        readonly object sync = new object();
        Bitmap[] cache;

        public CatSprites(CatPalette pal)
        {
            CatPalette def = PalettePresets.Default();
            palette = new CatPalette();
            palette.Name = pal != null && pal.Name != null ? pal.Name : def.Name;
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                Color c = def.Colors[i];
                if (pal != null && pal.Colors != null && i < pal.Colors.Length && !pal.Colors[i].IsEmpty) c = pal.Colors[i];
                c = Color.FromArgb(255, c.R, c.G, c.B);
                palette.Colors[i] = c;
                argb[i] = c.ToArgb();
            }
            argb[SpriteLibrary.Pupil] = Color.FromArgb(255, 0x14, 0x14, 0x14).ToArgb();
            argb[SpriteLibrary.Shine] = Color.FromArgb(255, 255, 255, 255).ToArgb();
            // tabby necklaces on the chest only exist on striped coats (a tuxedo's white chest stays white)
            argb[SpriteLibrary.ChestStripe] = palette[PaletteSlot.Stripes].ToArgb() != palette[PaletteSlot.Fur].ToArgb()
                ? palette[PaletteSlot.Stripes].ToArgb() : palette[PaletteSlot.Light].ToArgb();
        }

        public int ColorOf(byte code) { return code < argb.Length ? argb[code] : 0; }

        public int FrameWidth { get { return SpriteLibrary.Width; } }
        public int FrameHeight { get { return SpriteLibrary.Height; } }
        public Point Anchor { get { return new Point(SpriteLibrary.Width / 2, SpriteLibrary.Height); } }

        public int FrameCount(CatAnim anim) { return SpriteLibrary.Def(anim).Frames.Length; }
        public double Duration(CatAnim anim) { return SpriteLibrary.Def(anim).Total; }

        public double FrameDuration(CatAnim anim, int frame)
        {
            SpriteAnimDef d = SpriteLibrary.Def(anim);
            int n = d.Durations.Length;
            if (frame < 0) frame = 0;
            if (frame >= n) frame = d.Loop ? frame % n : n - 1;
            return d.Durations[frame];
        }

        public int FrameAt(CatAnim anim, double t)
        {
            SpriteAnimDef d = SpriteLibrary.Def(anim);
            int n = d.Frames.Length;
            if (n <= 1 || double.IsNaN(t) || t <= 0) return 0;
            if (d.Loop)
            {
                if (double.IsInfinity(t)) return 0;
                t = t % d.Total;
            }
            else if (t >= d.Total) return n - 1;
            for (int i = 0; i < n; i++)
            {
                if (t < d.Durations[i]) return i;
                t -= d.Durations[i];
            }
            return n - 1;
        }

        public Point HeadPoint(CatAnim anim, int frame)
        {
            return SpriteLibrary.Frames[SpriteLibrary.FrameIndex(anim, frame)].Head;
        }

        public Rectangle BodyBounds(CatAnim anim, int frame)
        {
            return SpriteLibrary.Frames[SpriteLibrary.FrameIndex(anim, frame)].Bounds;
        }

        public Bitmap GetFrame(CatAnim anim, int frame, bool faceLeft)
        {
            int fi = SpriteLibrary.FrameIndex(anim, frame);
            int k = fi * 2 + (faceLeft ? 1 : 0);
            lock (sync)
            {
                // after Dispose a late caller still gets a valid bitmap (rebuilt) instead of a crash
                if (cache == null) cache = new Bitmap[SpriteLibrary.Frames.Length * 2];
                Bitmap b = cache[k];
                if (b == null)
                {
                    b = Render(SpriteLibrary.Frames[fi], faceLeft);
                    cache[k] = b;
                }
                return b;
            }
        }

        Bitmap Render(SpriteFrame f, bool mirror)
        {
            int w = SpriteLibrary.Width, h = SpriteLibrary.Height;
            int[] px = new int[w * h];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    byte c = f.Pix[row + (mirror ? w - 1 - x : x)];
                    px[row + x] = c == SpriteLibrary.Clear ? 0 : argb[c];
                }
            }
            Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            BitmapData bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    Marshal.Copy(px, y * w, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), w);
            }
            finally { bmp.UnlockBits(bd); }
            return bmp;
        }

        public void Dispose()
        {
            Bitmap[] c;
            lock (sync)
            {
                c = cache;
                cache = null;
            }
            if (c == null) return;
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] == null) continue;
                try { c[i].Dispose(); } catch { }
                c[i] = null;
            }
        }
    }

    public static class CatSpriteFactory
    {
        public static ICatSprites Create(CatPalette palette)
        {
            try { return new CatSprites(palette); }
            catch (Exception ex)
            {
                CatLog.Error("CatSpriteFactory.Create", ex);
                return new CatSprites(null);
            }
        }
    }
}
