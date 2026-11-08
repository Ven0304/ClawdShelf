using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ClawdShelf
{
    /// <summary>Warm Claude-ish palette, light or dark following the Windows app theme.</summary>
    public class Theme
    {
        public Color Bg, Border, Text, Sub, Card, CardBorder, Thumb, Accent, AccentSoft, Hover, Danger, Shadow;
        public bool Dark;

        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI");
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public static Theme Current()
        {
            bool dark = false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    dark = v is int && (int)v == 0;
                }
            }
            catch { }
            return dark ? DarkTheme() : LightTheme();
        }

        static Color C(uint argb) { return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb); }

        static Theme LightTheme()
        {
            return new Theme
            {
                Bg = C(0xFFFAF9F5), Border = C(0xFFE6E1D6), Text = C(0xFF1F1E1D), Sub = C(0xFF77736A),
                Card = C(0xFFFFFFFF), CardBorder = C(0xFFE9E4DA), Thumb = C(0xFFF0EEE6),
                Accent = C(0xFFD97757), AccentSoft = C(0x26D97757), Hover = C(0x0F1F1E1D), Danger = C(0xFFC4492F),
                Shadow = C(0xFF5A4636),
            };
        }

        static Theme DarkTheme()
        {
            return new Theme
            {
                Dark = true,
                Bg = C(0xFF262624), Border = C(0xFF3B3A37), Text = C(0xFFF3F1EA), Sub = C(0xFFA6A196),
                Card = C(0xFF30302E), CardBorder = C(0xFF41403C), Thumb = C(0xFF1E1E1C),
                Accent = C(0xFFE0896A), AccentSoft = C(0x33E0896A), Hover = C(0x14FFFFFF), Danger = C(0xFFE5735A),
                Shadow = C(0xFF000000),
            };
        }

        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        /// <summary>
        /// A soft shadow made of stacked translucent rounded rectangles - looks like a blur shadow
        /// but costs nothing for the software renderer (unlike DropShadowEffect).
        /// </summary>
        public Grid SoftShadow(double radius, int spread, double offsetY, double strength)
        {
            var g = new Grid { IsHitTestVisible = false };
            for (int i = spread; i >= 1; i--)
            {
                double a = strength * (1.0 - (i - 1.0) / spread) / spread * 2.2;
                g.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(radius + i),
                    Margin = new Thickness(-i, -i + offsetY, -i, -i - offsetY),
                    Background = B(Color.FromArgb((byte)Math.Min(255, a * 255), Shadow.R, Shadow.G, Shadow.B)),
                });
            }
            return g;
        }

        public TextBlock Label(string text, double size, Color color, FontWeight weight)
        {
            return new TextBlock { Text = text, FontFamily = UiFont, FontSize = size, Foreground = B(color), FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        }

        /// <summary>A small rounded icon button.</summary>
        public Border IconButton(string glyph, string tip, Action click)
        {
            var t = new TextBlock
            {
                Text = glyph, FontFamily = IconFont, FontSize = 14, Foreground = B(Sub),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            var b = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = t, ToolTip = tip, Cursor = Cursors.Hand };
            Hoverable(b, t);
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        public Border TextButton(string text, bool accent, Action click)
        {
            var t = Label(text, 12, accent ? Colors.White : Text, FontWeights.SemiBold);
            var b = new Border
            {
                Padding = new Thickness(10, 4, 10, 5), CornerRadius = new CornerRadius(8), Child = t, Cursor = Cursors.Hand,
                Background = accent ? B(Accent) : B(Hover), Margin = new Thickness(6, 0, 0, 0),
            };
            b.MouseEnter += (s, e) => b.Opacity = 0.85;
            b.MouseLeave += (s, e) => b.Opacity = 1;
            b.MouseLeftButtonUp += (s, e) => { e.Handled = true; click(); };
            return b;
        }

        void Hoverable(Border b, TextBlock icon)
        {
            b.MouseEnter += (s, e) => { b.Background = B(Hover); if (icon != null) icon.Foreground = B(Text); };
            b.MouseLeave += (s, e) => { b.Background = Brushes.Transparent; if (icon != null) icon.Foreground = B(Sub); };
        }

        /// <summary>A still pixel Clawd, used in the right-click menu.</summary>
        public static Image PixelClawd(Pose pose, double scale)
        {
            var buf = new PixelBuf(22, 17);
            ClawdArt.Draw(buf, pose, 11, 15, 0);
            var wb = new WriteableBitmap(22, 17, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, 22, 17), buf.P, 22 * 4, 0);
            wb.Freeze();
            var img = new Image { Source = wb, Width = 22 * scale, Height = 17 * scale, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            return img;
        }

        public static void Animate(DependencyObject target, DependencyProperty prop, double to, int ms, IEasingFunction ease = null, int delayMs = 0, double? from = null)
        {
            var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(ms), EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            if (from.HasValue) a.From = from.Value;
            var anim = target as IAnimatable;
            if (anim != null) anim.BeginAnimation(prop, a);
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return Loc.T("刚刚", "just now");
            if (d.TotalMinutes < 60) return Loc.T((int)d.TotalMinutes + " 分钟前", (int)d.TotalMinutes + " min ago");
            if (t.Date == DateTime.Today) return Loc.T("今天 ", "today ") + t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return Loc.T("昨天 ", "yesterday ") + t.ToString("HH:mm");
            return t.ToString(Loc.T("M月d日 HH:mm", "MMM d, HH:mm"), System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
