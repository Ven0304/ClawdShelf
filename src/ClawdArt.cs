using System;
using System.Collections.Generic;

namespace ClawdShelf
{
    /// <summary>Tiny straight-alpha BGRA pixel buffer (uint = 0xAARRGGBB).</summary>
    public class PixelBuf
    {
        public readonly int W, H;
        public readonly uint[] P;
        public PixelBuf(int w, int h) { W = w; H = h; P = new uint[w * h]; }

        public void Clear() { Array.Clear(P, 0, P.Length); }

        public void Set(int x, int y, uint c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            P[y * W + x] = c;
        }

        public void Fill(int x, int y, int w, int h, uint c)
        {
            for (int j = y; j < y + h; j++) for (int i = x; i < x + w; i++) Set(i, j, c);
        }

        public void Blend(int x, int y, uint c)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            uint sa = c >> 24;
            if (sa == 0) return;
            if (sa == 255) { P[y * W + x] = c; return; }
            uint d = P[y * W + x];
            uint dA = d >> 24;
            if (dA == 0) { P[y * W + x] = c; return; }
            // straight-alpha "over", integer maths (runs for every particle pixel, so no allocations here)
            uint inv = 255 - sa;
            uint oa = sa + dA * inv / 255;
            uint wd = dA * inv / 255;
            uint r = (((c >> 16) & 255) * sa + ((d >> 16) & 255) * wd) / oa;
            uint g = (((c >> 8) & 255) * sa + ((d >> 8) & 255) * wd) / oa;
            uint b = ((c & 255) * sa + (d & 255) * wd) / oa;
            P[y * W + x] = (oa << 24) | (r << 16) | (g << 8) | b;
        }

        public uint Hash()
        {
            uint h = 2166136261;
            for (int i = 0; i < P.Length; i++) h = (h ^ P[i]) * 16777619;
            return h;
        }

    }

    public enum Eyes { Open, Blink, Closed, Happy, Wide, Strain, Angry }
    public enum Arm { Down, Mid, Up, Hold }
    public enum Legs { Stand, Kick }

    /// <summary>Everything that describes one frame of Clawd.</summary>
    public class Pose
    {
        public double Angle;          // spin around the vertical axis, 0 = facing you
        public int X, Y;              // body offset, Y < 0 is up in the air
        public int Stretch;           // body height change (+ taller, - squashed)
        public int Jaw;               // mouth opening in px
        public Eyes Eyes = Eyes.Open;
        public int LookX, LookY;
        public Arm LeftArm = Arm.Down, RightArm = Arm.Down;
        public Legs Legs = Legs.Stand;
        public int LegFrame;
        public bool Blush;
        public double Anger;          // 0..1 red flush
        public int Plate;             // dumbbell plate height, 0 = no dumbbell
        public int BarLift;
        public int StackPx;           // height of the screenshot pile behind Clawd
        public int StackSway;
        public Hat Hat = Hat.None;
    }

    public enum Hat { None, Straw, Beanie, TopHat, Night }

    public static class ClawdArt
    {
        public const uint Body = 0xFFD97757;
        public const uint BodyDark = 0xFFC2603F;
        public const uint EyeC = 0xFF1E1A18;
        public const uint MouthC = 0xFF3B1E18;
        public const uint Tongue = 0xFFF07A84;
        public const uint Fang = 0xFFFFFFFF;
        public const uint BlushC = 0xFFF4A0A8;
        public const uint Paper = 0xFFFBF8F2, PaperEdge = 0xFFD9CFC1, PaperSide = 0xFFC7BBAA;

        // ---- glyphs (flat colours, no outlines) ----
        public static readonly string[] ZBig = { "#####", "...#.", "..#..", ".#...", "#####" };
        public static readonly string[] ZSmall = { "###", ".#.", "###" };
        public static readonly string[] Bang = { "#", "#", "#", ".", "#" };
        public static readonly string[] Heart = { "##.##", "#####", ".###.", "..#.." };
        public static readonly string[] AngerMark = { ".#.#.", "##.##", ".....", "##.##", ".#.#." };
        public static readonly string[] Card = { "eeeeee", "eSSSYe", "eSGSSe", "eGGGGe", "eeeeee" };
        public static readonly string[] Drop = { ".B.", "BHB", "BBB" };
        public static readonly string[] Puff = { ".ggg.", "gWWWg", ".ggg." };
        public static readonly string[] Spark = { ".#.", "###", ".#." };

        // ---- hats: rows sit on top of the head; the last 'sink' rows overlap the head ----
        static readonly string[] StrawHat = { "....YYYYYY....", "...YYYYYYYY...", "...RRRRRRRR...", "bbbbbbbbbbbbbb" };
        static readonly string[] BeanieHat = { ".....pp.....", "...KKKKKK...", "..KKKKKKKK..", ".KKKKKKKKKK.", "cccccccccccc" };
        static readonly string[] TopHatArt = { "..ldddDD..", "..lDDDDD..", "..lDDDDD..", "..rrrrrr..", "lDDDDDDDDD" };
        static readonly string[] NightCap = { "...........p", "........NNN.", ".....NNNNN..", "..NNNNNNNN..", "nnnnnnnnnnnn" };

        static readonly Dictionary<char, uint> Palette = new Dictionary<char, uint>
        {
            { 'W', 0xFFFFFFFF }, { 'S', 0xFF9CCBF7 }, { 'Y', 0xFFF0CB6B }, { 'G', 0xFF7CC37A },
            { 'B', 0xFF6EC1F0 }, { 'H', 0xFFE8F7FF }, { 'e', 0xFFD8D1C5 }, { 'g', 0xFFDDDDDD },
            { 'R', 0xFFD9534F }, { 'b', 0xFFD9AA4E },
            { 'K', 0xFF5B86E0 }, { 'c', 0xFFDCE6F7 }, { 'p', 0xFFF6EFE3 },
            { 'D', 0xFF2D2B30 }, { 'd', 0xFF3E3B44 }, { 'l', 0xFF5E5A66 }, { 'r', 0xFFC8483E },
            { 'N', 0xFF6A7FE0 }, { 'n', 0xFFDDE3FA },
        };

        /// <summary>Stamp a little pixel glyph. '#' takes the fill colour, other letters come from the palette.</summary>
        public static void Glyph(PixelBuf b, string[] g, int x, int y, uint fill, double alpha = 1)
        {
            for (int j = 0; j < g.Length; j++)
                for (int i = 0; i < g[j].Length; i++)
                {
                    char ch = g[j][i];
                    if (ch == '.') continue;
                    uint c = ch == '#' ? fill : (Palette.ContainsKey(ch) ? Palette[ch] : fill);
                    c = (uint)(((c >> 24) * alpha)) << 24 | (c & 0xFFFFFF);
                    b.Blend(x + i, y + j, c);
                }
        }
        static uint Mix(uint a, uint b, double t)
        {
            uint r = (uint)Math.Round(((a >> 16) & 255) * (1 - t) + ((b >> 16) & 255) * t);
            uint g = (uint)Math.Round(((a >> 8) & 255) * (1 - t) + ((b >> 8) & 255) * t);
            uint bl = (uint)Math.Round((a & 255) * (1 - t) + (b & 255) * t);
            return 0xFF000000 | (r << 16) | (g << 8) | bl;
        }

        static readonly int[][] KickLen = { new[] { 3, 1, 3, 1 }, new[] { 2, 3, 1, 3 }, new[] { 1, 3, 1, 3 }, new[] { 3, 1, 3, 2 } };
        static readonly int[][] KickDx = { new[] { -1, 0, 0, 1 }, new[] { 0, 0, 0, 0 }, new[] { 0, -1, 1, 0 }, new[] { -1, 0, 0, 1 } };

        public static int BodyTop(Pose p, int baseY) { return baseY + p.Y - (8 + p.Stretch); }

        /// <summary>
        /// Draw Clawd. cx = horizontal centre (pixel boundary), baseY = row just below the body (legs start here).
        /// Proportions follow the Claude Code mascot: 12x8 body, 1x2 eyes, 2x2 arm stubs, four 1x2 legs.
        /// </summary>
        public static void Draw(PixelBuf L, Pose p, int cx, int baseY, double time)
        {
            cx += p.X;
            int bodyH = 8 + p.Stretch;
            int bottom = baseY + p.Y;           // first row below the body
            int top = bottom - bodyH;
            int armRow = bottom - 4;
            uint body = p.Anger > 0 ? Mix(Body, 0xFFE24A33, Math.Min(1, p.Anger)) : Body;
            uint dark = p.Anger > 0 ? Mix(BodyDark, 0xFFB8321F, Math.Min(1, p.Anger)) : BodyDark;

            double th = p.Angle, cos = Math.Cos(th), sin = Math.Sin(th);
            bool front = Math.Abs(th) < 1e-6;
            double hw = Math.Sqrt(36 * cos * cos + 9 * sin * sin);
            int left = (int)Math.Round(cx - hw), right = (int)Math.Round(cx + hw) - 1;

            // ---- pile of screenshots on Clawd's back (in front of him when he faces away) ----
            bool pile = p.StackPx > 0 && Math.Abs(cos) > 0.3;
            if (pile && cos > 0) DrawStack(L, cx, top, p.StackPx, p.StackSway, time);

            // ---- arms (3D: those behind the body first) ----
            var armsFront = new List<Action>();
            for (int side = -1; side <= 1; side += 2)
            {
                Arm pose = side < 0 ? p.LeftArm : p.RightArm;
                double zr = -(side * 6) * sin;
                if (front && pose != Arm.Down)
                {
                    int s = side;
                    armsFront.Add(() => DrawRaisedArm(L, cx, s, armRow, pose, top, p, body));
                    continue;
                }
                double x0 = side * 6 * cos, x1 = side * 8 * cos;
                int a0 = (int)Math.Floor(cx + Math.Min(x0, x1)), a1 = (int)Math.Ceiling(cx + Math.Max(x0, x1)) - 1;
                if (a1 < a0) a1 = a0;
                if (a1 - a0 > 1) { if (side > 0) a0 = a1 - 1; else a1 = a0 + 1; }
                bool inFront = zr >= 0;
                int ar = armRow, b0 = a0, b1 = a1;
                Action draw = () => L.Fill(b0, ar, b1 - b0 + 1, 2, inFront && !front ? dark : body);
                if (inFront) armsFront.Add(draw); else draw();
            }

            // ---- body (with optional open mouth: head lifts up by Jaw) ----
            int jaw = front ? p.Jaw : 0;
            int w = right - left + 1;
            L.Fill(left, top - jaw, w, 4, body);                    // head
            L.Fill(left, top + 4, w, bodyH - 4, body);              // lower body
            if (jaw > 0)
            {
                L.Fill(left, top + 4 - jaw, 1, jaw, body);
                L.Fill(right, top + 4 - jaw, 1, jaw, body);
                L.Fill(left + 1, top + 4 - jaw, w - 2, jaw, MouthC);
                if (jaw >= 2) { L.Set(left + 2, top + 4 - jaw, Fang); L.Set(right - 2, top + 4 - jaw, Fang); }
                L.Fill(cx - 2, top + 3, 4, 1, Tongue);
            }

            // ---- eyes ----
            int ey = top - jaw + 2 + p.LookY;
            for (int side = -1; side <= 1; side += 2)
            {
                double ex = side * 3.5, ez = 2.44;
                double xr = ex * cos + ez * sin, zr = -ex * sin + ez * cos;
                if (zr < 0.4) continue;
                int px = (int)Math.Floor(cx + xr) + (front ? p.LookX : 0);
                DrawEye(L, px, ey, side, p.Eyes);
                if (p.Blush) L.Fill(side < 0 ? px - 1 : px, top - jaw + 4, 2, 1, BlushC);
            }

            DrawHat(L, p.Hat, cx, top - jaw, cos);
            foreach (var a in armsFront) a();
            if (pile && cos <= 0) DrawStack(L, cx - 3, top, p.StackPx, p.StackSway, time);

            // ---- legs ----
            double[] lx = { -4.5, -2.5, 2.5, 4.5 }, lz = { -1, 1, 1, -1 };
            for (int i = 0; i < 4; i++)
            {
                double xr = lx[i] * cos + lz[i] * sin;
                int px = (int)Math.Floor(cx + xr);
                int len = 2, dx = 0;
                if (p.Legs == Legs.Kick)
                {
                    int f = p.LegFrame % 4;
                    len = KickLen[f][i];
                    dx = KickDx[f][i];
                }
                L.Fill(px + dx, bottom, 1, len, body);
            }

            // ---- dumbbell ----
            if (p.Plate > 0)
            {
                int bar = BarRow(p, top);
                L.Fill(cx - 10, bar, 20, 1, 0xFF505050);
                int ph = p.Plate;
                foreach (int px in new[] { cx - 12, cx + 10 })
                {
                    L.Fill(px, bar - ph / 2, 2, ph, 0xFF2F2F2F);
                    L.Set(px, bar - ph / 2, 0xFF777777);
                }
            }
        }

        static string[] HatArt(Hat hat, out int sink)
        {
            sink = 0;
            switch (hat)
            {
                case Hat.Straw: return StrawHat;
                case Hat.Beanie: sink = 1; return BeanieHat;
                case Hat.TopHat: return TopHatArt;
                case Hat.Night: sink = 1; return NightCap;
                default: return null;
            }
        }

        /// <summary>How many rows the hat sticks out above the head.</summary>
        public static int HatRise(Hat hat)
        {
            int sink;
            var m = HatArt(hat, out sink);
            return m == null ? 0 : m.Length - sink;
        }

        static int BarRow(Pose p, int top) { return top - 3 - p.BarLift - HatRise(p.Hat); }

        static void DrawHat(PixelBuf L, Hat hat, int cx, int headTop, double cos)
        {
            int sink;
            var m = HatArt(hat, out sink);
            if (m == null) return;
            int w = m[0].Length, x0 = cx - w / 2, y0 = headTop - (m.Length - sink);
            bool flip = cos < 0;   // facing away: the floppy nightcap droops to the other side
            for (int j = 0; j < m.Length; j++)
                for (int i = 0; i < w; i++)
                {
                    char ch = m[j][i];
                    if (ch == '.') continue;
                    L.Set(x0 + (flip ? w - 1 - i : i), y0 + j, Palette[ch]);
                }
        }

        // raised-arm masks for the right arm; column 0 touches the body, rows end at the shoulder row
        static readonly string[] ArmMid = { "...##", "..##.", "###..", "##..." };
        static readonly string[] ArmUp = { "..##", "..##", "..#.", ".##.", "##..", "##.." };

        static void DrawRaisedArm(PixelBuf L, int cx, int side, int armRow, Arm pose, int top, Pose p, uint body)
        {
            if (pose == Arm.Hold)
            {
                int bar = BarRow(p, top);
                int col = side > 0 ? cx + 7 : cx - 8;
                L.Fill(side > 0 ? cx + 6 : cx - 8, armRow, 2, 2, body);
                L.Fill(col, bar + 1, 1, armRow - bar - 1, body);
                L.Fill(side > 0 ? col : col - 1, bar + 1, 2, 1, body);
                return;
            }
            var m = pose == Arm.Mid ? ArmMid : ArmUp;
            int rowTop = armRow + 2 - m.Length;
            for (int j = 0; j < m.Length; j++)
                for (int i = 0; i < m[j].Length; i++)
                    if (m[j][i] == '#')
                        L.Set(side > 0 ? cx + 6 + i : cx - 7 - i, rowTop + j, body);
        }

        static void DrawEye(PixelBuf L, int x, int y, int side, Eyes e)
        {
            switch (e)
            {
                case Eyes.Open: L.Fill(x, y, 1, 2, EyeC); break;
                case Eyes.Blink: L.Fill(x, y + 1, 1, 1, EyeC); break;
                case Eyes.Closed: L.Fill(side < 0 ? x - 1 : x, y + 1, 2, 1, EyeC); break;
                case Eyes.Happy: L.Set(x - 1, y + 1, EyeC); L.Set(x, y, EyeC); L.Set(x + 1, y + 1, EyeC); break;
                case Eyes.Wide: L.Fill(side < 0 ? x - 1 : x, y - 1, 2, 3, EyeC); break;
                case Eyes.Strain:
                    int o = side < 0 ? -1 : 1;
                    L.Set(x + o, y - 1, EyeC); L.Set(x, y, EyeC); L.Set(x + o, y + 1, EyeC);
                    break;
                case Eyes.Angry:
                    L.Set(side < 0 ? x - 1 : x + 1, y - 1, EyeC); L.Fill(x, y, 1, 2, EyeC);
                    break;
            }
        }

        /// <summary>The pile of screenshots Clawd carries on its back.</summary>
        public static void DrawStack(PixelBuf L, int cx, int top, int h, int sway, double time)
        {
            const int W = 10;
            int baseRow = top + 5;              // bottom sheets hide behind the body
            int left = cx - 1;                  // sits over the right shoulder
            int layers = (h + 5 + 1) / 2;
            for (int i = 0; i < layers; i++)
            {
                int y = baseRow - i * 2;
                double lean = layers > 1 ? (double)i / (layers - 1) : 0;
                int jitter = ((i * 7) % 3) - 1;
                int dx = jitter + (int)Math.Round(sway * lean * Math.Sin(time * 5.0));
                int x = left + dx;
                L.Fill(x, y - 1, W, 1, Paper);
                L.Fill(x, y, W, 1, PaperEdge);
                L.Set(x, y - 1, PaperSide); L.Set(x + W - 1, y - 1, PaperSide);
                if (i == layers - 1)
                {
                    // the top sheet is a tiny screenshot
                    L.Fill(x + 1, y - 2, W - 2, 1, Paper);
                    L.Fill(x + 2, y - 2, 3, 1, 0xFF9CCBF7);
                    L.Fill(x + 5, y - 2, 2, 1, 0xFF7CC37A);
                }
            }
        }
    }
}
