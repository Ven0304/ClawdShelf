using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ClawdShelf;

// Builds clawd.ico (PNG-compressed entries) from the same pixel art the app draws.
static class MakeIcon
{
    static void Main(string[] args)
    {
        // Clawd is 16x10 art pixels; centre it in a 16x16 square
        var buf = new PixelBuf(16, 16);
        ClawdArt.Draw(buf, new Pose(), 8, 13, 0);

        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var pngs = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        uint c = buf.P[(y * 16 / size) * 16 + (x * 16 / size)];
                        bmp.SetPixel(x, y, Color.FromArgb((int)c));
                    }
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            }
        }

        using (var fs = new FileStream(args[0], FileMode.Create))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
    }
}
