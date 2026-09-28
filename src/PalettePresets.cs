using System;
using System.Drawing;

namespace DesktopCat
{
    public static class PalettePresets
    {
        // slot order: Outline, Fur, FurShade, Stripes, Light, Paws, Points, Muzzle, PatchA, PatchB, Eye, Pink
        static readonly string[][] Table = new string[][]
        {
            new string[] { "Британский лиловый табби",
                "3f3533", "a99d95", "8e827b", "7d706a", "cfc4ba", "cdc2b9", "9b8f88", "ece5dc", "a99d95", "a99d95", "c98533", "cf9f98" },
            new string[] { "Рыжий полосатый",
                "3d2516", "f2a452", "d9853a", "c4682a", "fff1dc", "fff1dc", "c4682a", "fff1dc", "f2a452", "f2a452", "8fc24a", "f59aa5" },
            new string[] { "Серый полосатый",
                "24262e", "a4a8b2", "878b96", "5e626d", "eef0f4", "eef0f4", "5e626d", "eef0f4", "a4a8b2", "a4a8b2", "e2b640", "f2a0ac" },
            new string[] { "Чёрный",
                "08080c", "3b3b46", "2d2d36", "3b3b46", "464652", "464652", "3b3b46", "4a4a57", "3b3b46", "3b3b46", "f0d23c", "d98c9a" },
            new string[] { "Чёрно-белый",
                "08080c", "363640", "292931", "363640", "f7f7f7", "f7f7f7", "363640", "f7f7f7", "363640", "363640", "a8d050", "f2a0ac" },
            new string[] { "Белый",
                "4c4a5a", "fbfbfb", "e2e2ea", "fbfbfb", "ffffff", "ffffff", "f3dfe2", "ffffff", "fbfbfb", "fbfbfb", "69b2f2", "f5a3b0" },
            new string[] { "Трёхцветный",
                "3a3030", "fbf7f0", "e3dccf", "fbf7f0", "ffffff", "ffffff", "e8913f", "ffffff", "eb9a4a", "4a4242", "9cc54a", "f59aa5" },
            new string[] { "Сиамский",
                "3b2a20", "f3e6cf", "dcc7a4", "f3e6cf", "fbf4e6", "5b4032", "5b4032", "6a4c3b", "f3e6cf", "f3e6cf", "5fa8e8", "e88c98" },
            new string[] { "Британский голубой",
                "2a2e38", "8e98a8", "768193", "8e98a8", "a0a9b8", "a0a9b8", "8e98a8", "a3acba", "8e98a8", "8e98a8", "ec9c38", "c99aa4" },
            new string[] { "Серо-белый",
                "26262e", "999ca6", "7f828c", "999ca6", "ffffff", "ffffff", "999ca6", "ffffff", "999ca6", "999ca6", "9cc54a", "f2a0ac" },
        };

        static CatPalette Make(string[] row)
        {
            CatPalette p = new CatPalette();
            p.Name = row[0];
            for (int i = 0; i < CatPalette.SlotCount; i++)
            {
                int v = Convert.ToInt32(row[i + 1], 16);
                p.Colors[i] = Color.FromArgb(255, (v >> 16) & 255, (v >> 8) & 255, v & 255);
            }
            return p;
        }

        public static CatPalette[] All()
        {
            CatPalette[] a = new CatPalette[Table.Length];
            for (int i = 0; i < Table.Length; i++) a[i] = Make(Table[i]);
            return a;
        }

        public static CatPalette Find(string name)
        {
            if (name == null) return null;
            string key = Normalize(name);
            if (key.Length == 0) return null;
            for (int i = 0; i < Table.Length; i++)
                if (Normalize(Table[i][0]) == key) return Make(Table[i]);
            return null;
        }

        public static CatPalette Default()
        {
            return Make(Table[0]);
        }

        static string Normalize(string s)
        {
            return s.Trim().ToLowerInvariant().Replace('ё', 'е');
        }
    }
}
