using System;
using System.Drawing;

namespace DesktopCat
{
    public static class AppPalette
    {
        public static CatPalette Resolve(CatSettings s)
        {
            CatPalette p = null;
            try
            {
                if (s != null && s.PaletteName == CatSettings.CustomPaletteName && s.CustomPalette != null)
                    p = Sanitize(s.CustomPalette, CatSettings.CustomPaletteName);
                else if (s != null && !string.IsNullOrEmpty(s.PaletteName))
                {
                    CatPalette f = PalettePresets.Find(s.PaletteName);
                    if (f != null) p = Sanitize(f, f.Name);
                }
            }
            catch (Exception ex) { CatLog.Error("AppPalette.Resolve", ex); }
            if (p == null) p = DefaultPalette();
            return p;
        }

        public static CatPalette DefaultPalette()
        {
            try
            {
                CatPalette d = PalettePresets.Default();
                if (d != null) return Sanitize(d, d.Name);
            }
            catch (Exception ex) { CatLog.Error("AppPalette.DefaultPalette", ex); }
            return Builtin();
        }

        public static CatPalette[] Presets()
        {
            try
            {
                CatPalette[] all = PalettePresets.All();
                if (all != null && all.Length > 0) return all;
            }
            catch (Exception ex) { CatLog.Error("AppPalette.Presets", ex); }
            return new CatPalette[] { Builtin() };
        }

        public static CatPalette Sanitize(CatPalette src, string name)
        {
            CatPalette b = Builtin();
            CatPalette p = new CatPalette();
            p.Name = name ?? (src != null ? src.Name : "") ?? "";
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                Color c = b.Colors[i];
                if (src != null && src.Colors != null && i < src.Colors.Length) c = src.Colors[i];
                p.Colors[i] = Color.FromArgb(255, c.R, c.G, c.B);
            }
            return p;
        }

        public static bool SameColors(CatPalette a, CatPalette b)
        {
            if (a == null || b == null) return a == b;
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                Color x = i < a.Colors.Length ? a.Colors[i] : Color.Empty;
                Color y = i < b.Colors.Length ? b.Colors[i] : Color.Empty;
                if (x.ToArgb() != y.ToArgb()) return false;
            }
            return true;
        }

        static CatPalette Builtin()
        {
            CatPalette p = new CatPalette();
            p.Name = "Британский лиловый табби";
            p[PaletteSlot.Outline] = Color.FromArgb(0x3e, 0x33, 0x38);
            p[PaletteSlot.Fur] = Color.FromArgb(0x9a, 0x8f, 0x95);
            p[PaletteSlot.FurShade] = Color.FromArgb(0x7f, 0x74, 0x7b);
            p[PaletteSlot.Stripes] = Color.FromArgb(0x6f, 0x63, 0x69);
            p[PaletteSlot.Light] = Color.FromArgb(0xd9, 0xc9, 0xb8);
            p[PaletteSlot.Paws] = Color.FromArgb(0xc2, 0xb4, 0xad);
            p[PaletteSlot.Points] = Color.FromArgb(0x8d, 0x82, 0x88);
            p[PaletteSlot.Muzzle] = Color.FromArgb(0xe3, 0xd3, 0xc2);
            p[PaletteSlot.PatchA] = Color.FromArgb(0xc7, 0xaa, 0x92);
            p[PaletteSlot.PatchB] = Color.FromArgb(0x9a, 0x8f, 0x95);
            p[PaletteSlot.Eye] = Color.FromArgb(0xc9, 0x8a, 0x3a);
            p[PaletteSlot.Pink] = Color.FromArgb(0xc9, 0x8e, 0x8e);
            return p;
        }
    }
}
