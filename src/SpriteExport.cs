using System;
using System.Drawing;
using System.IO;

namespace DesktopCat
{
    public static class SpriteExport
    {
        public static Icon MakeIcon(ICatSprites sprites, int size)
        {
            if (size < 8) size = 8;
            if (size > 256) size = 256;
            try
            {
                int[] px = IconPixels(sprites, size);
                return IconFromPixels(px, size);
            }
            catch (Exception ex)
            {
                CatLog.Error("SpriteExport.MakeIcon", ex);
                try { return (Icon)SystemIcons.Application.Clone(); } catch { return null; }
            }
        }

        internal static int[] IconPixels(ICatSprites sprites, int size)
        {
            CatSprites cs = sprites as CatSprites;
            CatSprites own = null;
            if (cs == null) { own = new CatSprites(null); cs = own; }
            try
            {
                string[] rows = SpriteArt.Icon16.Replace("\r", "").Trim('\n').Split('\n');
                int aw = 16, ah = 16;
                int s = Math.Max(1, size / aw);
                int ox = (size - aw * s) / 2, oy = (size - ah * s) / 2;
                int[] px = new int[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int ax, ay;
                        if (size < aw) { ax = x * aw / size; ay = y * ah / size; }
                        else
                        {
                            if (x < ox || y < oy) continue;
                            ax = (x - ox) / s; ay = (y - oy) / s;
                        }
                        if (ay >= ah || ay >= rows.Length || ax >= aw || ax >= rows[ay].Length) continue;
                        byte code = SpriteLibrary.Code(rows[ay][ax]);
                        if (code == SpriteLibrary.Clear) continue;
                        px[y * size + x] = cs.ColorOf(code);
                    }
                return px;
            }
            finally
            {
                if (own != null) own.Dispose();
            }
        }

        // an .ico loaded from memory owns its handle (Bitmap.GetHicon would leak one);
        // the dib height is doubled because the and mask follows the color rows
        static Icon IconFromPixels(int[] px, int size)
        {
            int maskStride = ((size + 31) / 32) * 4;
            int xorBytes = size * size * 4, andBytes = maskStride * size;
            int imageBytes = 40 + xorBytes + andBytes;
            using (MemoryStream ms = new MemoryStream(22 + imageBytes))
            using (BinaryWriter w = new BinaryWriter(ms))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)1);
                w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(imageBytes); w.Write(22);
                w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
                w.Write(0); w.Write(xorBytes + andBytes); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                for (int y = size - 1; y >= 0; y--)
                    for (int x = 0; x < size; x++) w.Write(px[y * size + x]);
                byte[] mask = new byte[maskStride];
                for (int y = size - 1; y >= 0; y--)
                {
                    Array.Clear(mask, 0, mask.Length);
                    for (int x = 0; x < size; x++)
                        if (((px[y * size + x] >> 24) & 255) == 0) mask[x >> 3] |= (byte)(0x80 >> (x & 7));
                    w.Write(mask);
                }
                w.Flush();
                ms.Position = 0;
                return new Icon(ms, size, size);
            }
        }
    }
}
