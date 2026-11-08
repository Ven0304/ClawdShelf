using System;
using System.Drawing;
using System.Drawing.Imaging;
using ClawdShelf;

// Renders a strip of Clawd poses standing on its rod, for checking the pixel art and for the README.
//   preview.exe out.png [scale]
static class Preview
{
    static void Main(string[] args)
    {
        var poses = new Pose[]
        {
            new Pose { Hat = Hat.Straw },
            new Pose { Hat = Hat.Straw, Eyes = Eyes.Happy, Blush = true, Angle = 0.6 },
            new Pose { Hat = Hat.Straw, Eyes = Eyes.Wide, LeftArm = Arm.Up, RightArm = Arm.Up, Y = -4, Stretch = 1 },
            new Pose { Hat = Hat.Night, Eyes = Eyes.Closed, Stretch = -1 },
            new Pose { Hat = Hat.Straw, Jaw = 3 },
            new Pose { Hat = Hat.Straw, Eyes = Eyes.Happy, RightArm = Arm.Up },
            new Pose { Hat = Hat.Straw, Eyes = Eyes.Angry, Anger = 0.6 },
            new Pose { Hat = Hat.Straw, Eyes = Eyes.Strain, LeftArm = Arm.Hold, RightArm = Arm.Hold, Plate = 6, Stretch = -1 },
            new Pose { Hat = Hat.Straw, StackPx = 16, Eyes = Eyes.Strain, StackSway = 1 },
            new Pose { Hat = Hat.Beanie },
            new Pose { Hat = Hat.TopHat },
        };
        int cw = 34, ch = 40, s = args.Length > 1 ? int.Parse(args[1]) : 4;
        using (var sheet = new Bitmap(cw * s * poses.Length, ch * s))
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.Transparent);
            for (int i = 0; i < poses.Length; i++)
            {
                var buf = new PixelBuf(cw, ch);
                ClawdArt.Draw(buf, poses[i], 17, 36, 0.3);
                int ox = i * cw * s;
                using (var rod = new SolidBrush(Color.FromArgb(0xC4, 0x88, 0x52)))
                    g.FillRectangle(rod, ox + 5 * s, 38 * s, 24 * s, s * 3 / 2);
                for (int y = 0; y < ch; y++)
                    for (int x = 0; x < cw; x++)
                    {
                        uint c = buf.P[y * cw + x];
                        if ((c >> 24) == 0) continue;
                        using (var br = new SolidBrush(Color.FromArgb((int)c)))
                            g.FillRectangle(br, ox + x * s, y * s, s, s);
                    }
            }
            sheet.Save(args[0], ImageFormat.Png);
        }
    }
}
