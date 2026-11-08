using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ClawdShelf
{
    /// <summary>
    /// "Home" layout: when Clawd stands at its home spot (top centre), the rack is one long wooden pole
    /// fixed at both screen edges and bent down in the middle under Clawd's weight. Screenshots hang along
    /// the curve, newest next to Clawd, spreading outwards. A crowded pole shrinks and overlaps the photos.
    /// </summary>
    public partial class RackWindow
    {
        const double CenterGap = 30, EndRoom = 88, MinSpacing = 54, EdgeMargin = 170;

        bool homeMode;
        readonly Canvas lineLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
        RectangleGeometry lineClip;
        Path glow;
        bool hintOn;
        double hCx, hx0, hx1, hy0, hSag, hRowH, hSpacing, hScale, homeBarY;
        int hLines, hPerLine;

        /// <summary>Where Clawd's body is, in window coordinates (cards fly there to be eaten).</summary>
        Point ClawdLocal()
        {
            return homeMode ? new Point(hCx, hy0 + hSag - 18) : new Point(anchorX - Left, perchY - 18);
        }

        double LineY(double x, int k)
        {
            double mid = (hx0 + hx1) / 2, half = (hx1 - hx0) / 2, u = (x - mid) / half;
            return hy0 + k * hRowH + hSag * (1 - u * u);
        }

        double SlopeDeg(double x)
        {
            double mid = (hx0 + hx1) / 2, half = (hx1 - hx0) / 2;
            return Math.Atan(-2 * hSag * (x - mid) / (half * half)) * 180 / Math.PI;
        }

        void ComputeHome(out double l, out double t, out double w, out double h)
        {
            var wa = SystemParameters.WorkArea;
            var home = Clawd.HomePoint;
            l = wa.Left; t = wa.Top; w = wa.Width;
            hCx = home.X - l;
            // keep clear of the minimise / maximise / close buttons of maximised windows (about 140 DIPs at the
            // top-right corner), and shorten the left end by the same amount so the pole stays centred
            double half = Math.Min(hCx, w - hCx) - EdgeMargin;
            hx0 = hCx - half; hx1 = hCx + half;
            hy0 = 10;
            hSag = Math.Max(14, home.Y - t - hy0);

            int n = store.Count;
            double side = Math.Max(SlotW, half - CenterGap - EndRoom);
            int perLineMax = Math.Max(2, 2 * (int)(side / MinSpacing));
            hLines = Math.Max(1, Math.Min(4, (n + perLineMax - 1) / perLineMax));
            hPerLine = Math.Max(2, (int)Math.Ceiling(n / (double)hLines));
            int perSide = (hPerLine + 1) / 2;
            hSpacing = Math.Min(SlotW, side / perSide);
            hScale = Math.Max(0.68, Math.Min(1, hSpacing / SlotW + 0.3));
            hRowH = (CardH + PinH) * hScale + 30;
            double cardsBottom = hy0 + hSag + (hLines - 1) * hRowH + (CardH + PinH) * hScale + 4;
            homeBarY = cardsBottom + 8;
            h = homeBarY + 48;
        }

        Point HomeSlot(int i)
        {
            int line = i / hPerLine;
            int j = i % hPerLine;
            int side = j % 2 == 0 ? 1 : -1;     // newest just right of Clawd, next just left, and so on outwards
            double x = hCx + side * (CenterGap + hSpacing * (j / 2 + 0.5));
            return new Point(x, LineY(x, line));
        }

        static PathGeometry Curve(double x0, double x1, double y, double sag)
        {
            var fig = new PathFigure { StartPoint = new Point(x0, y) };
            // a quadratic Bézier whose control point is 2*sag down passes exactly sag below the ends in the middle
            fig.Segments.Add(new QuadraticBezierSegment(new Point((x0 + x1) / 2, y + 2 * sag), new Point(x1, y), true));
            var g = new PathGeometry();
            g.Figures.Add(fig);
            g.Freeze();
            return g;
        }

        void BuildLines(int lines)
        {
            lineLayer.Children.Clear();
            lineLayer.Width = Width; lineLayer.Height = Height;
            lineClip = new RectangleGeometry(new Rect(0, 0, Width, Height));
            lineLayer.Clip = lineClip;

            var g0 = Curve(hx0, hx1, hy0, hSag);
            glow = new Path { Data = g0, Stroke = Theme.B(Color.FromArgb(0x70, 0xFF, 0xC8, 0x6A)), StrokeThickness = 16, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed };
            lineLayer.Children.Add(glow);

            for (int k = 0; k < lines; k++)
            {
                double y = hy0 + k * hRowH;
                var geo = Curve(hx0, hx1, y, hSag);
                if (k > 0)
                {
                    // lower poles hang from the one above by cords at both ends
                    foreach (double x in new[] { hx0 + 3, hx1 - 4 })
                    {
                        var cord = new Rectangle { Width = 1.6, Height = hRowH, Fill = Theme.B(Color.FromRgb(0x9C, 0x7A, 0x5C)) };
                        Canvas.SetLeft(cord, x); Canvas.SetTop(cord, y - hRowH);
                        lineLayer.Children.Add(cord);
                    }
                }
                lineLayer.Children.Add(new Path { Data = geo, Stroke = Theme.B(Color.FromArgb(0x30, 0, 0, 0)), StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform(0, 3) });
                lineLayer.Children.Add(new Path { Data = geo, Stroke = Theme.B(Color.FromRgb(0xC4, 0x88, 0x52)), StrokeThickness = 5.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                lineLayer.Children.Add(new Path { Data = geo, Stroke = Theme.B(Color.FromRgb(0xEA, 0xB9, 0x88)), StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, RenderTransform = new TranslateTransform(0, -1.3) });
                if (k == 0)
                {
                    // little wall brackets holding the pole at both screen edges
                    foreach (double x in new[] { hx0 - 6, hx1 - 3 })
                    {
                        var hook = new Border { Width = 9, Height = 18, CornerRadius = new CornerRadius(3), Background = Theme.B(Color.FromRgb(0x8A, 0x5A, 0x33)) };
                        Canvas.SetLeft(hook, x); Canvas.SetTop(hook, y - 12);
                        lineLayer.Children.Add(hook);
                    }
                }
            }
        }

        void HideLine()
        {
            if (glow != null) glow.BeginAnimation(OpacityProperty, null);
            if (lineClip != null) lineClip.BeginAnimation(RectangleGeometry.RectProperty, null);
            lineLayer.BeginAnimation(OpacityProperty, null);
            lineLayer.Opacity = 1;
            lineLayer.Visibility = Visibility.Collapsed;
        }

        void RelayoutHome(bool animateCards)
        {
            var screenPos = ScreenPositions();
            double l, t, w, h;
            ComputeHome(out l, out t, out w, out h);
            SetBounds(l, t, w, h);
            perch.Visibility = Visibility.Collapsed;
            foreach (var r in rods) r.Visibility = Visibility.Collapsed;
            foreach (var r in ropes) r.Visibility = Visibility.Collapsed;
            BuildLines(hLines);
            lineLayer.Visibility = Visibility.Visible;

            EnsureTags();
            moreText.Visibility = Visibility.Collapsed;
            countTag.Visibility = toolTag.Visibility = Visibility.Visible;
            double tx = hx0 + 14, cx2 = hx1 - 14 - 58;
            Canvas.SetLeft(toolTag, tx); Canvas.SetTop(toolTag, LineY(tx + 18, 0) + RodH / 2 - 1);
            Canvas.SetLeft(countTag, cx2); Canvas.SetTop(countTag, LineY(cx2 + 29, 0) + RodH / 2 - 1);
            emptyNote.Visibility = store.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            double ex = hCx + CenterGap + 10;
            Canvas.SetLeft(emptyNote, ex); Canvas.SetTop(emptyNote, LineY(ex + 85, 0) + RodH / 2 + 1);
            RefreshHeader();

            for (int i = 0; i < store.Items.Count; i++)
            {
                var c = CardFor(store.Items[i]);
                if (c.Leaving) continue;
                var pin = HomeSlot(i);
                // newer photos overlap older ones; each tilts a little with the slope of the pole
                PlaceCard(c, pin.X - CardW / 2, pin.Y - PinH + 1, hScale, SlopeDeg(pin.X) * 0.6, 1000 - i, screenPos, animateCards);
            }
            UpdateActionBar();
            KeepClawdOnTop();
        }

        void OpenHome()
        {
            RelayoutHome(false);
            if (!IsVisible) Show();

            // the long pole slides out from under Clawd's feet to both screen edges
            var grow = new RectAnimation(new Rect(hCx - 30, 0, 60, Height), new Rect(0, 0, Width, Height), TimeSpan.FromMilliseconds(480))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            lineClip.BeginAnimation(RectangleGeometry.RectProperty, grow);
            SwingIn(toolTag, 420);
            SwingIn(countTag, 420);
            if (emptyNote.Visibility == Visibility.Visible) SwingIn(emptyNote, 260);

            // Clawd hangs them up nearest first, working outwards
            int k = 0;
            foreach (var c in cards.Values.Where(x => x.Visibility == Visibility.Visible && !x.Leaving)
                                          .OrderBy(x => Math.Abs(Canvas.GetLeft(x) + CardW / 2 - hCx)))
            {
                c.Opacity = 0;
                DropIn(c, 140 + Math.Min(k, 26) * 26);
                k++;
            }
        }

        /// <summary>Slide the pole back in under Clawd (part of closing).</summary>
        void RetractLine(int delay)
        {
            if (lineClip == null) return;
            var shrink = new RectAnimation(new Rect(hCx - 30, 0, 60, Height), TimeSpan.FromMilliseconds(260))
            { BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            lineClip.BeginAnimation(RectangleGeometry.RectProperty, shrink);
        }

        // ------------------------------------------------------------------ click-through
        // A layered window swallows clicks on every non-transparent pixel, so the pole (which runs right
        // past the title-bar buttons of maximised windows) would block them. Instead the whole window stays
        // click-through and only becomes clickable while the pointer is over a photo, a tag or the bar.

        System.Windows.Threading.DispatcherTimer passTimer;
        bool passThrough, inDrag;

        void StartPassTimer()
        {
            if (passTimer == null)
            {
                passTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
                passTimer.Tick += delegate { PassTick(); };
            }
            passTimer.Start();
        }

        void StopPassTimer()
        {
            if (passTimer != null) passTimer.Stop();
            SetPass(true);      // closed: just the little rod, never in the way
        }

        void SetPass(bool on)
        {
            if (on == passThrough) return;
            passThrough = on;
            SetClickThrough(on);
        }

        void PassTick()
        {
            if (!IsVisible || inDrag) return;
            if (pressCard != null || System.Windows.Input.Mouse.Captured != null) { SetPass(false); return; }
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            var pt = new Point(p.X / dpi - Left, p.Y / dpi - Top);
            HangCard over = null;
            bool hit = false;
            if (pt.X >= 0 && pt.Y >= 0 && pt.X < Width && pt.Y < Height)
            {
                var r = VisualTreeHelper.HitTest(root, pt);
                if (r != null && r.VisualHit != null && r.VisualHit != root)
                {
                    hit = true;
                    DependencyObject d = r.VisualHit;
                    while (d != null && !(d is HangCard)) d = VisualTreeHelper.GetParent(d);
                    over = d as HangCard;
                }
            }
            SetPass(!hit);
            // once click-through, the window no longer hears MouseLeave: drop stale hover states ourselves
            foreach (var c in cards.Values)
                if (c.Hover && c != over) { c.Hover = false; HoverVisual(c, false); }
        }

        /// <summary>While Clawd is carried near home, the long pole lights up to show it will snap back there.</summary>
        public void ShowHomeHint(bool on)
        {
            if (on == hintOn || open || Clawd == null) return;
            hintOn = on;
            if (!on) { ApplyClosedGeometry(); return; }

            double l, t, w, h;
            ComputeHome(out l, out t, out w, out h);
            SetBounds(l, t, w, hy0 + hSag + 30);
            perch.Visibility = Visibility.Collapsed;
            BuildLines(1);
            lineLayer.Visibility = Visibility.Visible;
            glow.Visibility = Visibility.Visible;
            Theme.Animate(lineLayer, OpacityProperty, 0.8, 200, null, 0, 0);
            glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(520)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            if (!IsVisible) Show();
            KeepClawdOnTop();
        }
    }
}
