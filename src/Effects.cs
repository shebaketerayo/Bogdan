using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DesktopCat
{
    internal enum EffectsGlyph
    {
        HeartBig = 0, HeartSmall, Z1, Z2, Z3, Note, Exclaim, DustBig, DustSmall, BubbleTailLeft, BubbleTailRight, FluffA, FluffB, Count
    }

    // every fade level is pre-baked as a premultiplied bitmap so drawing never needs a slow gdi+ colour matrix
    internal static class EffectsArt
    {
        public const int AlphaLevels = 8;
        static Bitmap[] glyphs;
        static Bitmap[,] faded;
        static Point[] tips;
        static ImageAttributes wrapAttrs;

        static readonly string[] HeartBig = {
            ".RR.RR.",
            "RWRRRRR",
            "RRRRRRR",
            ".RRRRS.",
            "..RRS..",
            "...S..." };
        static readonly string[] HeartSmall = {
            "RR.RR",
            "RWRRR",
            ".RRS.",
            "..S.." };
        static readonly string[] Z1 = {
            "ZZZZ",
            "..Z.",
            ".Z..",
            "ZZZZ" };
        static readonly string[] Z2 = {
            "ZZZZZ",
            "...Z.",
            "..Z..",
            ".Z...",
            "ZZZZZ" };
        static readonly string[] Z3 = {
            "ZZZZZZ",
            "....Z.",
            "...Z..",
            "..Z...",
            ".Z....",
            "ZZZZZZ" };
        static readonly string[] Note = {
            "...NN..",
            "...N.N.",
            "...N..N",
            "...N..N",
            "...N...",
            ".NNN...",
            "NWNN...",
            ".NN...." };
        static readonly string[] Exclaim = {
            "EE",
            "EE",
            "EE",
            "ee",
            "..",
            "EE" };
        static readonly string[] DustBig = {
            ".D.",
            "DDD",
            ".D." };
        static readonly string[] DustSmall = {
            "DD",
            "DD" };
        static readonly string[] FluffA = {
            "FF",
            ".F" };
        static readonly string[] FluffB = {
            ".F.",
            "FFF" };

        // 9-row pixel font for "Мяу!": rows 0-6 caps, 2-6 x-height, 7-8 descender
        static readonly string[][] MeowFont = {
            new string[] { "X...X", "XX.XX", "X.X.X", "X.X.X", "X...X", "X...X", "X...X", ".....", "....." },
            new string[] { "....", "....", ".XXX", "X..X", ".XXX", ".X.X", "X..X", "....", "...." },
            new string[] { "....", "....", "X..X", "X..X", "X..X", "X..X", ".XXX", "...X", ".XX." },
            new string[] { "X", "X", "X", "X", "X", ".", "X", ".", "." } };

        public static Bitmap Get(EffectsGlyph g)
        {
            Ensure();
            return glyphs[(int)g];
        }

        public static Point Tip(EffectsGlyph g)
        {
            Ensure();
            return tips[(int)g];
        }

        public static Bitmap Get(EffectsGlyph g, int level)
        {
            Ensure();
            if (level < 1) level = 1;
            if (level > AlphaLevels - 1) level = AlphaLevels - 1;
            return faded[(int)g, level];
        }

        // the flip-tile wrap mode stops edge bleeding when scaling with nearest neighbour
        public static ImageAttributes Attrs
        {
            get { Ensure(); return wrapAttrs; }
        }

        static void Ensure()
        {
            if (glyphs != null) return;
            Bitmap[] g = new Bitmap[(int)EffectsGlyph.Count];
            Point[] t = new Point[(int)EffectsGlyph.Count];

            Dictionary<char, Color> heart = new Dictionary<char, Color>();
            heart['R'] = Color.FromArgb(255, 111, 145);
            heart['W'] = Color.FromArgb(255, 218, 228);
            heart['S'] = Color.FromArgb(222, 72, 116);
            Color heartOutline = Color.FromArgb(122, 31, 63);
            g[(int)EffectsGlyph.HeartBig] = Build(HeartBig, heart, heartOutline);
            g[(int)EffectsGlyph.HeartSmall] = Build(HeartSmall, heart, heartOutline);

            Dictionary<char, Color> z = new Dictionary<char, Color>();
            z['Z'] = Color.FromArgb(238, 243, 255);
            Color zOutline = Color.FromArgb(55, 64, 110);
            g[(int)EffectsGlyph.Z1] = Build(Z1, z, zOutline);
            g[(int)EffectsGlyph.Z2] = Build(Z2, z, zOutline);
            g[(int)EffectsGlyph.Z3] = Build(Z3, z, zOutline);

            Dictionary<char, Color> note = new Dictionary<char, Color>();
            note['N'] = Color.FromArgb(255, 210, 77);
            note['W'] = Color.FromArgb(255, 244, 196);
            g[(int)EffectsGlyph.Note] = Build(Note, note, Color.FromArgb(110, 74, 14));

            Dictionary<char, Color> ex = new Dictionary<char, Color>();
            ex['E'] = Color.FromArgb(255, 224, 74);
            ex['e'] = Color.FromArgb(255, 170, 46);
            g[(int)EffectsGlyph.Exclaim] = Build(Exclaim, ex, Color.FromArgb(107, 58, 0));

            Dictionary<char, Color> dust = new Dictionary<char, Color>();
            dust['D'] = Color.FromArgb(246, 242, 236);
            Color dustOutline = Color.FromArgb(176, 167, 157);
            g[(int)EffectsGlyph.DustBig] = Build(DustBig, dust, dustOutline);
            g[(int)EffectsGlyph.DustSmall] = Build(DustSmall, dust, dustOutline);

            Dictionary<char, Color> fluff = new Dictionary<char, Color>();
            fluff['F'] = Color.FromArgb(255, 250, 240);
            Color fluffOutline = Color.FromArgb(120, 104, 92);
            g[(int)EffectsGlyph.FluffA] = Build(FluffA, fluff, fluffOutline);
            g[(int)EffectsGlyph.FluffB] = Build(FluffB, fluff, fluffOutline);

            Point tip;
            g[(int)EffectsGlyph.BubbleTailLeft] = BuildBubble(false, out tip);
            t[(int)EffectsGlyph.BubbleTailLeft] = tip;
            g[(int)EffectsGlyph.BubbleTailRight] = BuildBubble(true, out tip);
            t[(int)EffectsGlyph.BubbleTailRight] = tip;

            Bitmap[,] f = new Bitmap[g.Length, AlphaLevels];
            for (int i = 0; i < g.Length; i++)
                for (int lv = 1; lv < AlphaLevels; lv++)
                    f[i, lv] = Fade(g[i], lv / (double)(AlphaLevels - 1));
            ImageAttributes ia = new ImageAttributes();
            ia.SetWrapMode(WrapMode.TileFlipXY);
            wrapAttrs = ia;
            faded = f;
            tips = t;
            glyphs = g;
        }

        static Bitmap Fade(Bitmap src, double k)
        {
            Bitmap b = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppPArgb);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Color c = src.GetPixel(x, y);
                    int a = (int)Math.Round(c.A * k);
                    b.SetPixel(x, y, a <= 0 ? Color.Transparent : Color.FromArgb(a, c.R, c.G, c.B));
                }
            return b;
        }

        static Bitmap Build(string[] rows, Dictionary<char, Color> map, Color outline)
        {
            int h = rows.Length, w = rows[0].Length;
            int W = w + 2, H = h + 2;
            Color[,] px = new Color[W, H];
            bool[,] fill = new bool[W, H];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    Color c;
                    char ch = x < rows[y].Length ? rows[y][x] : '.';
                    if (map.TryGetValue(ch, out c)) { px[x + 1, y + 1] = c; fill[x + 1, y + 1] = true; }
                }
            return Finish(px, fill, W, H, outline);
        }

        static Bitmap Finish(Color[,] px, bool[,] fill, int W, int H, Color outline)
        {
            Bitmap b = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (fill[x, y]) { b.SetPixel(x, y, px[x, y]); continue; }
                    bool edge = (x > 0 && fill[x - 1, y]) || (x < W - 1 && fill[x + 1, y]) ||
                                (y > 0 && fill[x, y - 1]) || (y < H - 1 && fill[x, y + 1]);
                    b.SetPixel(x, y, edge ? outline : Color.Transparent);
                }
            return b;
        }

        static Bitmap BuildBubble(bool tailRight, out Point tip)
        {
            int textW = 0;
            for (int i = 0; i < MeowFont.Length; i++) textW += MeowFont[i][0].Length + (i > 0 ? 1 : 0);
            const int textH = 9, padX = 2, padTop = 2, padBottom = 1, tailRows = 3;
            int fw = textW + padX * 2, fh = textH + padTop + padBottom;
            int W = fw + 2, H = fh + 2 + tailRows;
            Color[,] px = new Color[W, H];
            bool[,] fill = new bool[W, H];
            Color white = Color.FromArgb(255, 255, 255);
            Color shade = Color.FromArgb(236, 229, 240);
            Color ink = Color.FromArgb(74, 59, 79);

            for (int y = 0; y < fh; y++)
                for (int x = 0; x < fw; x++)
                {
                    bool corner = (x == 0 || x == fw - 1) && (y == 0 || y == fh - 1);
                    if (corner) continue;
                    fill[x + 1, y + 1] = true;
                    px[x + 1, y + 1] = y == fh - 1 ? shade : white;
                }
            int[][] tail = { new int[] { 4, 5, 6 }, new int[] { 3, 4 }, new int[] { 2 } };
            for (int r = 0; r < tailRows; r++)
                foreach (int cx in tail[r])
                {
                    int x = tailRight ? W - 1 - cx : cx;
                    int y = fh + 1 + r;
                    fill[x, y] = true;
                    px[x, y] = shade;
                }
            int tipX = tailRight ? W - 1 - 2 : 2;
            tip = new Point(tipX, H - 1);

            int ox = 1 + padX, oy = 1 + padTop;
            for (int i = 0; i < MeowFont.Length; i++)
            {
                string[] gl = MeowFont[i];
                for (int y = 0; y < gl.Length; y++)
                    for (int x = 0; x < gl[y].Length; x++)
                        if (gl[y][x] == 'X') px[ox + x, oy + y] = ink;
                ox += gl[0].Length + 1;
            }
            return Finish(px, fill, W, H, ink);
        }
    }

    // particles live in art px relative to the cat's feet, so they follow the cat; world-fixed ones stay put
    public sealed class EffectsSystem
    {
        sealed class Particle
        {
            public CatEffect Kind;
            public double X, Y, VX, VY, Age, Life, Phase, WobbleAmp, WobbleFreq, Drag, FadeFrac, MaxAlpha, Grav;
            public bool Behind, WorldFixed, TailRight;
            public int Variant;
        }

        const int MaxParticles = 48;
        readonly List<Particle> live = new List<Particle>(MaxParticles);
        readonly Stack<Particle> pool = new Stack<Particle>();
        readonly Random rnd = new Random();

        public bool Alive { get { return live.Count > 0; } }

        public bool OnlySlow
        {
            get
            {
                for (int i = 0; i < live.Count; i++) if (live[i].Kind != CatEffect.Zzz) return false;
                return true;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < live.Count; i++) pool.Push(live[i]);
            live.Clear();
        }

        Particle New(CatEffect kind)
        {
            if (live.Count >= MaxParticles) { pool.Push(live[0]); live.RemoveAt(0); }
            Particle p = pool.Count > 0 ? pool.Pop() : new Particle();
            p.Kind = kind;
            p.X = p.Y = p.VX = p.VY = p.Age = p.Phase = p.WobbleAmp = p.WobbleFreq = p.Drag = p.Grav = 0;
            p.Life = 1; p.FadeFrac = 0.35; p.MaxAlpha = 1;
            p.Behind = p.WorldFixed = p.TailRight = false;
            p.Variant = 0;
            live.Add(p);
            return p;
        }

        double R(double a, double b) { return a + rnd.NextDouble() * (b - a); }

        public void Spawn(CatEffect e, double headX, double headY, bool faceLeft)
        {
            double face = faceLeft ? -1 : 1;
            Particle p;
            switch (e)
            {
                case CatEffect.Heart:
                    p = New(e);
                    p.X = headX + R(-2, 2); p.Y = headY - 7;
                    p.VY = -9; p.VX = R(-1, 1);
                    p.WobbleAmp = 1.2; p.WobbleFreq = 1.6; p.Phase = R(0, 6.28);
                    p.Life = 1.6; p.Variant = 0;
                    break;
                case CatEffect.Hearts:
                    {
                        int n = 3 + rnd.Next(3);
                        for (int i = 0; i < n; i++)
                        {
                            p = New(CatEffect.Heart);
                            p.X = headX + R(-7, 7); p.Y = headY - R(5, 9);
                            p.VY = -R(8, 13); p.VX = R(-3, 3);
                            p.WobbleAmp = R(0.8, 1.6); p.WobbleFreq = R(1.2, 2.0); p.Phase = R(0, 6.28);
                            p.Life = R(1.3, 1.9);
                            p.Age = -i * 0.13;
                            p.Variant = rnd.Next(2);
                        }
                    }
                    break;
                case CatEffect.Zzz:
                    p = New(e);
                    p.X = headX + face * 3; p.Y = headY - 5;
                    p.VX = face * 4.5; p.VY = -6.5;
                    p.WobbleAmp = 0.8; p.WobbleFreq = 0.7; p.Phase = R(0, 6.28);
                    p.Life = 2.6; p.FadeFrac = 0.3;
                    break;
                case CatEffect.Meow:
                    for (int i = live.Count - 1; i >= 0; i--)
                        if (live[i].Kind == CatEffect.Meow) { pool.Push(live[i]); live.RemoveAt(i); }
                    p = New(e);
                    p.TailRight = faceLeft;
                    p.X = headX + face * 1; p.Y = headY - 8;
                    p.Life = 1.3; p.FadeFrac = 0.15;
                    break;
                case CatEffect.Exclaim:
                    for (int i = live.Count - 1; i >= 0; i--)
                        if (live[i].Kind == CatEffect.Exclaim) { pool.Push(live[i]); live.RemoveAt(i); }
                    p = New(e);
                    p.X = headX + face * 1; p.Y = headY - 13;
                    p.Life = 0.95; p.FadeFrac = 0.2;
                    break;
                case CatEffect.Note:
                    p = New(e);
                    p.X = headX + face * 5; p.Y = headY - 8;
                    p.VX = face * 3; p.VY = -7;
                    p.WobbleAmp = 1.4; p.WobbleFreq = 1.1; p.Phase = R(0, 6.28);
                    p.Life = 1.7;
                    break;
                case CatEffect.Shred:
                    {
                        // for shred headX/headY is the claw point
                        int n = 2 + rnd.Next(2);
                        for (int i = 0; i < n; i++)
                        {
                            p = New(e);
                            p.X = headX + R(-2, 2); p.Y = headY - R(0.5, 2);
                            p.VX = R(-16, 16); p.VY = -R(10, 20);
                            p.Grav = 55; p.Drag = 1.5;
                            p.Life = R(0.55, 0.9); p.FadeFrac = 0.45;
                            p.WorldFixed = true;
                            p.Variant = rnd.Next(2);
                        }
                    }
                    break;
                case CatEffect.Dust:
                    {
                        int n = 4 + rnd.Next(3);
                        for (int i = 0; i < n; i++)
                        {
                            p = New(e);
                            double side = (i % 2 == 0) ? -1 : 1;
                            p.X = side * R(2, 6); p.Y = -R(1, 2.5);
                            p.VX = side * R(9, 20); p.VY = -R(2, 7);
                            p.Drag = 3.5;
                            p.Life = R(0.4, 0.62); p.FadeFrac = 0.5; p.MaxAlpha = 0.9;
                            p.Behind = rnd.Next(3) != 0;
                            p.WorldFixed = true;
                        }
                    }
                    break;
            }
        }

        public void Update(double dt, double catDxArt, double catDyArt)
        {
            if (live.Count == 0) return;
            if (Math.Abs(catDxArt) > 400 || Math.Abs(catDyArt) > 400) { catDxArt = 0; catDyArt = 0; }
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Particle p = live[i];
                p.Age += dt;
                if (p.Age >= p.Life) { pool.Push(p); live.RemoveAt(i); continue; }
                if (p.Age < 0) continue;
                if (p.Drag > 0)
                {
                    double k = Math.Max(0, 1 - p.Drag * dt);
                    p.VX *= k; p.VY *= k;
                }
                p.VY += p.Grav * dt;
                p.X += p.VX * dt;
                p.Y += p.VY * dt;
                if (p.WorldFixed) { p.X -= catDxArt; p.Y -= catDyArt; }
            }
        }

        EffectsGlyph GlyphOf(Particle p, double t)
        {
            switch (p.Kind)
            {
                case CatEffect.Heart:
                case CatEffect.Hearts:
                    if (p.Variant == 1) return EffectsGlyph.HeartSmall;
                    return p.Age < 0.1 ? EffectsGlyph.HeartSmall : EffectsGlyph.HeartBig;
                case CatEffect.Zzz: return t < 0.3 ? EffectsGlyph.Z1 : (t < 0.62 ? EffectsGlyph.Z2 : EffectsGlyph.Z3);
                case CatEffect.Meow: return p.TailRight ? EffectsGlyph.BubbleTailRight : EffectsGlyph.BubbleTailLeft;
                case CatEffect.Exclaim: return EffectsGlyph.Exclaim;
                case CatEffect.Note: return EffectsGlyph.Note;
                case CatEffect.Shred: return p.Variant == 0 ? EffectsGlyph.FluffA : EffectsGlyph.FluffB;
                default: return t < 0.45 ? EffectsGlyph.DustBig : EffectsGlyph.DustSmall;
            }
        }

        int AlphaLevel(Particle p, double t)
        {
            double a = p.MaxAlpha;
            double f = p.FadeFrac;
            if (f > 0 && t > 1 - f) a *= (1 - t) / f;
            int lv = (int)Math.Round(a * (EffectsArt.AlphaLevels - 1));
            if (lv < 0) lv = 0;
            if (lv > EffectsArt.AlphaLevels - 1) lv = EffectsArt.AlphaLevels - 1;
            return lv;
        }

        void Place(Particle p, EffectsGlyph gl, Bitmap bmp, Rectangle canvasArt, out int gx, out int gy)
        {
            double wob = p.WobbleAmp > 0 ? p.WobbleAmp * Math.Sin(p.Phase + p.Age * p.WobbleFreq * 6.2832) : 0;
            if (p.Kind == CatEffect.Meow)
            {
                Point tip = EffectsArt.Tip(gl);
                int hop = p.Age < 0.07 ? 1 : 0;
                gx = (int)Math.Floor(p.X) - tip.X;
                gy = (int)Math.Floor(p.Y) - tip.Y + hop;
            }
            else
            {
                int hop = (p.Kind == CatEffect.Exclaim && p.Age < 0.12) ? -2 : 0;
                gx = (int)Math.Floor(p.X + wob) - bmp.Width / 2;
                gy = (int)Math.Floor(p.Y) - bmp.Height / 2 + hop;
            }
            if (p.Kind == CatEffect.Meow || p.Kind == CatEffect.Exclaim)
            {
                if (gx + bmp.Width > canvasArt.Right) gx = canvasArt.Right - bmp.Width;
                if (gx < canvasArt.Left) gx = canvasArt.Left;
                if (gy < canvasArt.Top) gy = canvasArt.Top;
            }
        }

        // cheap hash of what would be drawn: an unchanged signature means identical pixels, so no re-render
        public int Signature(Rectangle canvasArt)
        {
            unchecked
            {
                int h = 17 + live.Count;
                for (int i = 0; i < live.Count; i++)
                {
                    Particle p = live[i];
                    if (p.Age < 0) { h = h * 31 + 7; continue; }
                    double t = p.Age / p.Life;
                    EffectsGlyph gl = GlyphOf(p, t);
                    int gx, gy;
                    Place(p, gl, EffectsArt.Get(gl), canvasArt, out gx, out gy);
                    h = h * 31 + (int)gl;
                    h = h * 31 + gx;
                    h = h * 31 + gy;
                    h = h * 31 + AlphaLevel(p, t);
                }
                return h;
            }
        }

        public void Draw(Graphics g, int anchorPxX, int anchorPxY, int scale, bool behind, Rectangle canvasArt)
        {
            for (int i = 0; i < live.Count; i++)
            {
                Particle p = live[i];
                if (p.Behind != behind || p.Age < 0) continue;
                double t = p.Age / p.Life;
                int lv = AlphaLevel(p, t);
                if (lv <= 0) continue;
                EffectsGlyph gl = GlyphOf(p, t);
                Bitmap bmp = EffectsArt.Get(gl, lv);
                int gx, gy;
                Place(p, gl, bmp, canvasArt, out gx, out gy);
                Rectangle dest = new Rectangle(anchorPxX + gx * scale, anchorPxY + gy * scale, bmp.Width * scale, bmp.Height * scale);
                g.DrawImage(bmp, dest, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, EffectsArt.Attrs);
            }
        }
    }
}
