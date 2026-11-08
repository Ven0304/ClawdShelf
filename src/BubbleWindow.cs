using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ClawdShelf
{
    /// <summary>Clawd's speech bubble: a tiny click-through window that sits above its head, or beside it at the top of the screen.</summary>
    public class BubbleWindow : Window
    {
        readonly Border box;
        readonly TextBlock text;
        readonly ScaleTransform pop = new ScaleTransform(1, 1);
        readonly DispatcherTimer hideTimer = new DispatcherTimer();
        readonly double dpi;

        public BubbleWindow()
        {
            dpi = Native.Dpi;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            IsHitTestVisible = false;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            text = new TextBlock
            {
                FontFamily = Theme.UiFont, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Theme.B(Color.FromRgb(0x2A, 0x1F, 0x1A)),
            };
            box = new Border
            {
                Background = Theme.B(Color.FromRgb(0xFF, 0xFD, 0xF8)),
                BorderBrush = Theme.B(Color.FromRgb(0xE3, 0xD9, 0xCC)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 3, 9, 4),
                Child = text,
                Margin = new Thickness(6),
                RenderTransform = pop,
                RenderTransformOrigin = new Point(0.5, 1),
            };
            var root = new Grid();
            var shadow = Theme.Current().SoftShadow(10, 4, 2, 0.12);
            shadow.Margin = new Thickness(6);
            root.Children.Add(shadow);
            root.Children.Add(box);
            Content = root;

            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
                fade.Completed += delegate { if (Opacity < 0.05) Hide(); };
                BeginAnimation(OpacityProperty, fade);
            };
            SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                Native.ExcludeFromCapture(h, true);
            };
        }

        /// <summary>head = Clawd's head on screen (DIPs).</summary>
        public void Say(string msg, double seconds, Rect head)
        {
            text.Text = msg;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible) Show();
            UpdateLayout();
            double w = ActualWidth, h = ActualHeight;
            var wa = Native.WorkAreaAt((int)((head.Left + head.Width / 2) * dpi), (int)((head.Top + head.Height / 2) * dpi));
            double wl = wa.Left / dpi, wt = wa.Top / dpi, wr = wa.Right / dpi;

            double x, y;
            if (head.Top - h + 4 >= wt)
            {
                // room above the head
                x = head.Left + head.Width / 2 - w / 2;
                y = head.Top - h + 4;
                box.RenderTransformOrigin = new Point(0.5, 1);
            }
            else if (head.Right + w - 4 <= wr)
            {
                x = head.Right - 2;
                y = head.Top + head.Height / 2 - h / 2;
                box.RenderTransformOrigin = new Point(0, 0.5);
            }
            else
            {
                x = head.Left - w + 2;
                y = head.Top + head.Height / 2 - h / 2;
                box.RenderTransformOrigin = new Point(1, 0.5);
            }
            Left = Math.Max(wl, Math.Min(wr - w, x));
            Top = Math.Max(wt, y);
            Native.BringToTop(new WindowInteropHelper(this).Handle);

            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            hideTimer.Stop();
            hideTimer.Interval = TimeSpan.FromSeconds(seconds);
            hideTimer.Start();
        }
    }
}
