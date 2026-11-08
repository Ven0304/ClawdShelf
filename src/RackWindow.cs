using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace ClawdShelf
{
    /// <summary>One screenshot hanging on the line by a clothespin.</summary>
    class HangCard : Grid
    {
        public ShotItem Item;
        public Border Frame, Thumb, Check, Close, Flash;
        public TextBlock Time, CheckIcon;
        public Grid Lift;
        public readonly RotateTransform Rot = new RotateTransform();
        public readonly ScaleTransform Scale = new ScaleTransform(1, 1);
        public readonly ScaleTransform Base = new ScaleTransform(1, 1);   // shrinks cards when the long rod gets crowded
        public readonly RotateTransform Tilt = new RotateTransform();      // follows the slope of the bent rod
        public bool Hover, Leaving, Placed;
    }

    /// <summary>
    /// The clothes rack. Closed, it is just the little rod Clawd stands on. Open, the rod stretches out
    /// sideways, more rods hang below it, and the screenshots hang from them with clothespins.
    /// It never takes focus (so a click-to-copy leaves your chat box focused), never closes when you
    /// click elsewhere, and the gaps between the photos are click-through.
    /// </summary>
    public partial class RackWindow : Window
    {
        const double CardW = 124, CardH = 106, PinH = 8, SlotW = 138, RowH = 146, RodH = 6, SideL = 62, SideR = 74, TopM = 12;
        const int MaxRowsCap = 3;

        readonly ShelfStore store;
        readonly Settings settings;
        readonly Catcher catcher;
        public ClawdWindow Clawd;
        Theme th = Theme.Current();
        readonly double dpi;
        readonly Random rnd = new Random();

        readonly Canvas root = new Canvas();
        Border perch;                                   // the short rod under Clawd's feet
        readonly List<Border> rods = new List<Border>();
        readonly List<Border> ropes = new List<Border>();
        FrameworkElement countTag, toolTag, emptyNote;
        Border actionBar;
        TextBlock countText, actionText, moreText;
        StackPanel actionButtons;
        readonly Dictionary<ShotItem, HangCard> cards = new Dictionary<ShotItem, HangCard>();
        readonly HashSet<ShotItem> selected = new HashSet<ShotItem>();
        ShotItem anchorItem;
        bool multiMode;

        bool open;
        int perRow = 3, visRows = 1, scrollRow;
        bool upward;                                    // not enough room below Clawd: the rack goes above it
        double anchorX = double.NaN, anchorY = double.NaN;
        double rack0Y, perchY;                          // window-local rows
        public int LastEatSide = 1;

        public bool IsOpen { get { return open; } }
        IntPtr Hwnd { get { return new WindowInteropHelper(this).Handle; } }

        HangCard pressCard;
        Point pressPt;
        bool pressOnCheck;
        DragGhost ghost;

        public RackWindow(ShelfStore store, Settings settings, Catcher catcher)
        {
            this.store = store; this.settings = settings; this.catcher = catcher;
            dpi = Native.Dpi;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Title = "ClawdShelf";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = root;

            perch = MakeRod();
            root.Children.Add(perch);
            root.Children.Add(lineLayer);

            store.Added += OnAdded;
            store.Removed += OnRemoved;
            store.Updated += it => { HangCard c; if (cards.TryGetValue(it, out c)) RefreshCard(c); };

            SourceInitialized += delegate
            {
                Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                ApplyCaptureAffinity();
            };
            MouseWheel += OnWheel;
        }

        public void ApplyCaptureAffinity()
        {
            Native.ExcludeFromCapture(Hwnd, settings.HideFromCapture);
        }

        double PerchW { get { return Clawd == null ? 64 : Math.Max(46, Clawd.PixelSize * 24); } }

        // ================================================================== rods & tags

        Border MakeRod()
        {
            var wood = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            wood.GradientStops.Add(new GradientStop(Color.FromRgb(0xE2, 0xAE, 0x78), 0));
            wood.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0x88, 0x52), 0.45));
            wood.GradientStops.Add(new GradientStop(Color.FromRgb(0x9A, 0x63, 0x3A), 1));
            wood.Freeze();
            return new Border
            {
                Height = RodH, CornerRadius = new CornerRadius(RodH / 2), Background = wood,
                BorderBrush = Theme.B(Color.FromArgb(0x55, 0x5A, 0x35, 0x18)), BorderThickness = new Thickness(0.6),
                IsHitTestVisible = false,
            };
        }

        Border MakeRope(double height)
        {
            return new Border { Width = 1.6, Height = height, Background = Theme.B(Color.FromRgb(0x9C, 0x7A, 0x5C)), IsHitTestVisible = false };
        }

        FrameworkElement MakeCountTag()
        {
            countText = th.Label("", 15, th.Text, FontWeights.Bold);
            countText.HorizontalAlignment = HorizontalAlignment.Center;
            var unit = th.Label(Loc.T("张截图", "shots"), 10, th.Sub, FontWeights.Normal);
            unit.HorizontalAlignment = HorizontalAlignment.Center;
            var sp = new StackPanel();
            sp.Children.Add(countText);
            sp.Children.Add(unit);
            var tag = new Border
            {
                Width = 58, Padding = new Thickness(4, 5, 4, 6), CornerRadius = new CornerRadius(8),
                Background = Theme.B(th.Bg), BorderBrush = Theme.B(th.Border), BorderThickness = new Thickness(1),
                Child = sp, Cursor = Cursors.SizeNS,
                ToolTip = Loc.T("滚动滚轮：调整能挂几张（也可以在 Clawd 身上滚）", "Scroll to change how many shots the rack holds (works on Clawd too)"),
            };
            tag.MouseWheel += (s, e) => { e.Handled = true; if (Clawd != null) Clawd.AdjustCapacity(e.Delta > 0 ? 5 : -5); };
            return Hanging(tag, 12);
        }

        FrameworkElement MakeToolTag()
        {
            var sp = new StackPanel();
            sp.Children.Add(th.IconButton("", Loc.T("全选", "Select all"), SelectAll));
            sp.Children.Add(th.IconButton("", Loc.T("打开截图文件夹", "Open screenshots folder"), () => OpenFolder(Settings.ScreenshotsDir)));
            sp.Children.Add(th.IconButton("", Loc.T("全部收走（让 Clawd 吃掉，原图不删）", "Clear the rack (Clawd eats them; files are kept)"), () => { if (store.Count > 0) store.Clear(); }));
            var tag = new Border
            {
                Padding = new Thickness(3), CornerRadius = new CornerRadius(9),
                Background = Theme.B(th.Bg), BorderBrush = Theme.B(th.Border), BorderThickness = new Thickness(1), Child = sp,
            };
            return Hanging(tag, 12);
        }

        /// <summary>Wrap an element so it hangs from a string with a little shadow.</summary>
        FrameworkElement Hanging(FrameworkElement body, double stringLen)
        {
            var sp = new StackPanel { RenderTransformOrigin = new Point(0.5, 0), RenderTransform = new RotateTransform() };
            var str = MakeRope(stringLen);
            str.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(str);
            var g = new Grid();
            g.Children.Add(th.SoftShadow(9, 5, 2, th.Dark ? 0.4 : 0.14));
            g.Children.Add(body);
            sp.Children.Add(g);
            return sp;
        }

        FrameworkElement MakeEmptyNote()
        {
            var sp = new StackPanel { Margin = new Thickness(12, 10, 12, 10) };
            var a = th.Label(Loc.T("还没有截图", "No screenshots yet"), 13, Color.FromRgb(0x2A, 0x26, 0x22), FontWeights.SemiBold);
            a.HorizontalAlignment = HorizontalAlignment.Center;
            var b = th.Label(Loc.T("按 Win+Shift+S 截一张，\n我来接住", "Press Win+Shift+S —\nI'll catch it"), 11.5, Color.FromRgb(0x7A, 0x72, 0x66), FontWeights.Normal);
            b.TextAlignment = TextAlignment.Center;
            b.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(a);
            sp.Children.Add(b);
            var note = new Border { Width = 170, Background = Theme.B(Color.FromRgb(0xFF, 0xFC, 0xF4)), CornerRadius = new CornerRadius(3), Child = sp };
            var g = new Grid { RenderTransformOrigin = new Point(0.5, 0), RenderTransform = new RotateTransform(-2) };
            g.Children.Add(th.SoftShadow(3, 6, 3, th.Dark ? 0.45 : 0.16));
            g.Children.Add(note);
            var outer = new Grid();
            outer.Children.Add(g);
            var pin = MakePin();
            pin.Margin = new Thickness(0, -PinH, 0, 0);
            outer.Children.Add(pin);
            return outer;
        }

        static Border MakePin()
        {
            return new Border
            {
                Width = 8, Height = 15, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                Background = Theme.B(Color.FromRgb(0xE3, 0xB2, 0x7A)), BorderBrush = Theme.B(Color.FromRgb(0xA8, 0x74, 0x45)), BorderThickness = new Thickness(1),
                Child = new Border { Height = 2.5, Margin = new Thickness(0.5, 0, 0.5, 0), Background = Theme.B(Color.FromRgb(0x9A, 0x9A, 0xA0)) },
                IsHitTestVisible = false,
            };
        }

        Border MakeActionBar()
        {
            actionText = th.Label("", 12, th.Text, FontWeights.SemiBold);
            actionText.Margin = new Thickness(6, 0, 6, 0);
            actionButtons = new StackPanel { Orientation = Orientation.Horizontal };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(actionText);
            sp.Children.Add(actionButtons);
            return new Border
            {
                Padding = new Thickness(8, 5, 6, 5), CornerRadius = new CornerRadius(14), Background = Theme.B(th.Bg),
                BorderBrush = Theme.B(th.Border), BorderThickness = new Thickness(1), Child = sp, Visibility = Visibility.Collapsed,
            };
        }

        // ================================================================== cards

        HangCard MakeCard(ShotItem it)
        {
            var c = new HangCard { Item = it, Width = CardW, Height = CardH + PinH, Cursor = Cursors.Hand };
            var tg = new TransformGroup();
            tg.Children.Add(c.Base);
            tg.Children.Add(c.Tilt);
            tg.Children.Add(c.Scale);
            tg.Children.Add(c.Rot);
            c.RenderTransform = tg;
            c.RenderTransformOrigin = new Point(0.5, 0);    // swings around its clothespin

            var body = new Grid { Margin = new Thickness(0, PinH, 0, 0) };
            c.Lift = th.SoftShadow(4, 7, 3, th.Dark ? 0.5 : 0.2);
            body.Children.Add(c.Lift);
            c.Frame = new Border
            {
                CornerRadius = new CornerRadius(4), Background = Brushes.White, BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2), Padding = new Thickness(3, 3, 3, 0),
            };
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19) });
            c.Thumb = new Border { CornerRadius = new CornerRadius(2), Background = Theme.B(Color.FromRgb(0xEE, 0xEB, 0xE4)) };
            g.Children.Add(c.Thumb);
            c.Flash = new Border
            {
                CornerRadius = new CornerRadius(2), Background = Theme.B(Color.FromArgb(0xE0, th.Accent.R, th.Accent.G, th.Accent.B)),
                Opacity = 0, IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = Loc.T("已复制 ✓", "Copied ✓"), Foreground = Brushes.White, FontFamily = Theme.UiFont, FontSize = 13, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            g.Children.Add(c.Flash);
            c.Time = new TextBlock
            {
                FontFamily = Theme.UiFont, FontSize = 10.5, Foreground = Theme.B(Color.FromRgb(0x8A, 0x83, 0x78)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(c.Time, 1);
            g.Children.Add(c.Time);
            c.Frame.Child = g;
            body.Children.Add(c.Frame);

            c.CheckIcon = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 9, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            c.Check = new Border
            {
                Width = 19, Height = 19, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.White, Background = Theme.B(Color.FromArgb(0x70, 0, 0, 0)),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 8, 0, 0),
                Child = c.CheckIcon, Opacity = 0, ToolTip = Loc.T("多选", "Select"),
            };
            body.Children.Add(c.Check);
            c.Close = new Border
            {
                Width = 19, Height = 19, CornerRadius = new CornerRadius(10), Background = Theme.B(Color.FromArgb(0xA0, 0x1F, 0x1E, 0x1D)),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 8, 0),
                Child = new TextBlock { Text = "", FontFamily = Theme.IconFont, FontSize = 8, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                Opacity = 0, ToolTip = Loc.T("取下来（原图不删）", "Take it down (the file is kept)"),
            };
            c.Close.MouseLeftButtonDown += (s, e) => e.Handled = true;
            c.Close.MouseLeftButtonUp += (s, e) => { e.Handled = true; store.Remove(new[] { it }); };
            c.Close.MouseEnter += (s, e) => c.Close.Background = Theme.B(th.Danger);
            c.Close.MouseLeave += (s, e) => c.Close.Background = Theme.B(Color.FromArgb(0xA0, 0x1F, 0x1E, 0x1D));
            body.Children.Add(c.Close);
            c.Children.Add(body);
            c.Children.Add(MakePin());

            c.MouseEnter += (s, e) => { c.Hover = true; HoverVisual(c, true); };
            c.MouseLeave += (s, e) => { c.Hover = false; HoverVisual(c, false); };
            c.MouseLeftButtonDown += (s, e) => CardDown(c, e);
            c.MouseMove += (s, e) => CardMove(c, e);
            c.MouseLeftButtonUp += (s, e) => CardUp(c, e);
            c.GiveFeedback += OnGiveFeedback;
            c.QueryContinueDrag += OnQueryContinueDrag;

            cards[it] = c;
            root.Children.Add(c);
            c.Visibility = Visibility.Collapsed;
            RefreshCard(c);
            return c;
        }

        void RefreshCard(HangCard c)
        {
            var it = c.Item;
            c.Time.Text = Theme.Ago(it.Time);
            c.ToolTip = it.Path != null ? Path.GetFileName(it.Path) : Loc.T("刚从剪贴板接住，正在保存…", "Just caught from the clipboard, saving…");
            if (it.Thumb != null)
            {
                double ar = it.Thumb.PixelWidth / (double)Math.Max(1, it.Thumb.PixelHeight);
                double box = (CardW - 10) / (CardH - 10 - 19);
                var brush = new ImageBrush(it.Thumb)
                {
                    Stretch = (ar > box * 0.55 && ar < box * 1.9) ? Stretch.UniformToFill : Stretch.Uniform,
                    AlignmentY = AlignmentY.Top,
                };
                RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
                brush.Freeze();
                c.Thumb.Child = new Border { CornerRadius = new CornerRadius(2), Background = brush };
            }
        }

        public void RefreshThumb(ShotItem it)
        {
            HangCard c;
            if (cards.TryGetValue(it, out c)) RefreshCard(c);
        }

        void HoverVisual(HangCard c, bool wiggle)
        {
            bool sel = selected.Contains(c.Item);
            if (homeMode && open) Panel.SetZIndex(c, c.Hover ? 5000 : 1000 - Math.Max(0, store.Items.IndexOf(c.Item)));
            c.Close.Opacity = c.Hover ? 1 : 0;
            c.Check.Opacity = (c.Hover || multiMode || sel) ? 1 : 0;
            c.CheckIcon.Opacity = sel ? 1 : 0;
            c.Check.Background = sel ? Theme.B(th.Accent) : Theme.B(Color.FromArgb(0x70, 0, 0, 0));
            c.Frame.BorderBrush = sel ? Theme.B(th.Accent) : Brushes.White;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            double s = c.Hover ? 1.05 : 1;
            Theme.Animate(c.Scale, ScaleTransform.ScaleXProperty, s, 160, ease);
            Theme.Animate(c.Scale, ScaleTransform.ScaleYProperty, s, 160, ease);
            if (wiggle && c.Hover)
                Theme.Animate(c.Rot, RotateTransform.AngleProperty, 0, 700, new ElasticEase { Oscillations = 2, Springiness = 4 }, 0, rnd.Next(2) == 0 ? -4 : 4);
        }

        // ================================================================== geometry

        /// <summary>Clawd's feet moved (or the screen changed): put the rod under them.</summary>
        public void MovePerch(double ax, double ay, bool pop)
        {
            anchorX = ax; anchorY = ay;
            if (open) { Relayout(false); return; }
            ApplyClosedGeometry();
            if (!IsVisible) Show();
            if (pop)
            {
                var st = new ScaleTransform(0.2, 1);
                perch.RenderTransformOrigin = new Point(0.5, 0.5);
                perch.RenderTransform = st;
                Theme.Animate(st, ScaleTransform.ScaleXProperty, 1, 420, new ElasticEase { Oscillations = 1, Springiness = 4 });
            }
            KeepClawdOnTop();
        }

        void ApplyClosedGeometry()
        {
            double w = PerchW + 24;
            SetBounds(anchorX - w / 2, anchorY - TopM, w, TopM + RodH + 10);
            perchY = TopM;
            Canvas.SetLeft(perch, 12); Canvas.SetTop(perch, perchY); perch.Width = PerchW;
            foreach (var r in rods) r.Visibility = Visibility.Collapsed;
            foreach (var r in ropes) r.Visibility = Visibility.Collapsed;
            foreach (var c in cards.Values) c.Visibility = Visibility.Collapsed;
            if (countTag != null) countTag.Visibility = Visibility.Collapsed;
            if (toolTag != null) toolTag.Visibility = Visibility.Collapsed;
            if (emptyNote != null) emptyNote.Visibility = Visibility.Collapsed;
            if (actionBar != null) actionBar.Visibility = Visibility.Collapsed;
            if (moreText != null) moreText.Visibility = Visibility.Collapsed;
            hintOn = false;
            StopPassTimer();
            HideLine();
            perch.Visibility = Visibility.Visible;
        }

        void SetBounds(double l, double t, double w, double h)
        {
            Left = l; Top = t; Width = w; Height = h;
            root.Width = w; root.Height = h;
        }

        Native.RECT WorkArea()
        {
            return Native.WorkAreaAt((int)(anchorX * dpi), (int)((anchorY - 10) * dpi));
        }

        /// <summary>Work out rows/columns and window bounds for the open rack.</summary>
        void ComputeOpenGeometry(out double l, out double t, out double w, out double h)
        {
            var wa = WorkArea();
            double wl = wa.Left / dpi, wt = wa.Top / dpi, wr = wa.Right / dpi, wb = wa.Bottom / dpi;
            int n = store.Count;
            int maxPer = Math.Max(2, Math.Min(6, (int)((wr - wl - SideL - SideR - 24) / SlotW)));
            perRow = Math.Max(Math.Min(3, maxPer), Math.Min(n, maxPer));
            int rows = Math.Max(1, (n + perRow - 1) / perRow);

            double clawdH = Clawd == null ? 40 : Clawd.BodyHeightDip + 8;
            double barH = 46;
            int fitBelow = (int)((wb - anchorY - barH) / RowH);
            int fitAbove = (int)((anchorY - clawdH - wt - TopM - barH) / RowH);
            upward = fitBelow < 1 && fitAbove > fitBelow;
            int maxRows = Math.Max(1, Math.Min(MaxRowsCap, upward ? fitAbove : fitBelow));
            visRows = Math.Min(rows, maxRows);
            scrollRow = Math.Max(0, Math.Min(scrollRow, rows - visRows));

            w = SideL + perRow * SlotW + SideR;
            double rowsH = visRows * RowH;
            if (!upward)
            {
                rack0Y = TopM;
                perchY = TopM;
                h = TopM + rowsH + barH;
            }
            else
            {
                rack0Y = TopM;
                perchY = TopM + rowsH + barH + clawdH;
                h = perchY + RodH + 10;
            }
            l = anchorX - w / 2;
            if (w <= wr - wl) l = Math.Max(wl + 2, Math.Min(wr - w - 2, l)); else l = wl;
            t = anchorY - perchY;
        }

        Point CardSlot(int index)
        {
            int r = index / perRow - scrollRow, c = index % perRow;
            return new Point(SideL + c * SlotW + (SlotW - CardW) / 2, rack0Y + r * RowH + RodH / 2 - PinH + 1);
        }

        bool SlotVisible(int index)
        {
            int r = index / perRow - scrollRow;
            return r >= 0 && r < visRows;
        }

        /// <summary>Re-place everything for the open rack. Cards glide from where they are on screen.</summary>
        void Relayout(bool animateCards)
        {
            if (homeMode) RelayoutHome(animateCards); else RelayoutAway(animateCards);
        }

        void RelayoutAway(bool animateCards)
        {
            var screenPos = ScreenPositions();
            double l, t, w, h;
            ComputeOpenGeometry(out l, out t, out w, out h);
            SetBounds(l, t, w, h);
            HideLine();
            perch.Visibility = Visibility.Visible;
            double clawdLocalX = anchorX - l;

            // perch: only visible on its own when the rack opened upwards
            Canvas.SetLeft(perch, clawdLocalX - PerchW / 2); Canvas.SetTop(perch, perchY); perch.Width = PerchW;

            while (rods.Count < visRows) { var r = MakeRod(); rods.Add(r); root.Children.Insert(1, r); }
            while (ropes.Count < visRows * 2) { var r = MakeRope(RowH); ropes.Add(r); root.Children.Insert(1, r); }
            for (int i = 0; i < rods.Count; i++)
            {
                var r = rods[i];
                r.Visibility = i < visRows ? Visibility.Visible : Visibility.Collapsed;
                r.BeginAnimation(WidthProperty, null);
                r.BeginAnimation(Canvas.LeftProperty, null);
                Canvas.SetLeft(r, 10); r.Width = w - 20;
                Canvas.SetTop(r, rack0Y + i * RowH);
            }
            for (int i = 0; i < ropes.Count; i++)
            {
                int row = i / 2 + 1;
                var r = ropes[i];
                bool vis = row < visRows;
                r.Visibility = vis ? Visibility.Visible : Visibility.Collapsed;
                if (!vis) continue;
                r.Height = RowH - RodH;
                Canvas.SetLeft(r, i % 2 == 0 ? 22 : w - 23);
                Canvas.SetTop(r, rack0Y + (row - 1) * RowH + RodH);
            }

            EnsureTags();
            countTag.Visibility = toolTag.Visibility = Visibility.Visible;
            Canvas.SetLeft(countTag, w - SideR + 8); Canvas.SetTop(countTag, rack0Y + RodH / 2);
            Canvas.SetLeft(toolTag, 8); Canvas.SetTop(toolTag, rack0Y + RodH / 2);
            emptyNote.Visibility = store.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(emptyNote, SideL + (perRow * SlotW - 170) / 2.0); Canvas.SetTop(emptyNote, rack0Y + RodH / 2 + 1);
            RefreshHeader();

            int below = Math.Max(0, store.Count - (scrollRow + visRows) * perRow);
            moreText.Text = below > 0 ? Loc.T("滚轮往下还有 " + below + " 张 ↓", below + " more below — scroll ↓") : (scrollRow > 0 ? Loc.T("滚轮往上翻 ↑", "scroll up ↑") : "");
            moreText.Visibility = moreText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            Canvas.SetLeft(moreText, SideL + 6);
            Canvas.SetTop(moreText, rack0Y + visRows * RowH - 22);

            for (int i = 0; i < store.Items.Count; i++)
            {
                var c = CardFor(store.Items[i]);
                if (c.Leaving) continue;
                if (!SlotVisible(i)) { c.Visibility = Visibility.Collapsed; c.Placed = false; continue; }
                var p = CardSlot(i);
                PlaceCard(c, p.X, p.Y, 1, 0, 0, screenPos, animateCards);
            }
            UpdateActionBar();
            KeepClawdOnTop();
        }

        HangCard CardFor(ShotItem it)
        {
            HangCard c;
            return cards.TryGetValue(it, out c) ? c : MakeCard(it);
        }

        /// <summary>Where each visible card is on screen, taken before the window moves or resizes.</summary>
        Dictionary<HangCard, Point> ScreenPositions()
        {
            var d = new Dictionary<HangCard, Point>();
            foreach (var c in cards.Values)
                if (c.Visibility == Visibility.Visible && c.Placed)
                    d[c] = new Point(Left + Canvas.GetLeft(c), Top + Canvas.GetTop(c));
            return d;
        }

        /// <summary>Put a card in its slot; with animate on it glides there from where it was on screen.</summary>
        void PlaceCard(HangCard c, double x, double y, double scale, double tilt, int z, Dictionary<HangCard, Point> before, bool animate)
        {
            Point from;
            bool had = before.TryGetValue(c, out from);
            c.Visibility = Visibility.Visible;
            Panel.SetZIndex(c, c.Hover ? 5000 : z);
            if (had && animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                Theme.Animate(c, Canvas.LeftProperty, x, 320, ease, 0, from.X - Left);
                Theme.Animate(c, Canvas.TopProperty, y, 320, ease, 0, from.Y - Top);
                Theme.Animate(c.Base, ScaleTransform.ScaleXProperty, scale, 320, ease);
                Theme.Animate(c.Base, ScaleTransform.ScaleYProperty, scale, 320, ease);
                Theme.Animate(c.Tilt, RotateTransform.AngleProperty, tilt, 320, ease);
            }
            else
            {
                c.BeginAnimation(Canvas.LeftProperty, null); c.BeginAnimation(Canvas.TopProperty, null);
                c.Base.BeginAnimation(ScaleTransform.ScaleXProperty, null); c.Base.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                c.Tilt.BeginAnimation(RotateTransform.AngleProperty, null);
                Canvas.SetLeft(c, x); Canvas.SetTop(c, y);
                c.Base.ScaleX = c.Base.ScaleY = scale;
                c.Tilt.Angle = tilt;
                if (!had && animate) DropIn(c, 0);
            }
            c.Placed = true;
        }

        /// <summary>A card shrinks and fades away; with a target it flies there first (into Clawd's mouth).</summary>
        void FlyAway(HangCard c, Point? target, double spin, int delay, int ms)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            if (target.HasValue)
            {
                Theme.Animate(c, Canvas.LeftProperty, target.Value.X - CardW / 2, ms, ease, delay, Canvas.GetLeft(c));
                Theme.Animate(c, Canvas.TopProperty, target.Value.Y, ms, ease, delay, Canvas.GetTop(c));
            }
            if (spin != 0) Theme.Animate(c.Rot, RotateTransform.AngleProperty, spin, ms, ease, delay);
            Theme.Animate(c.Scale, ScaleTransform.ScaleXProperty, 0.1, ms, ease, delay);
            Theme.Animate(c.Scale, ScaleTransform.ScaleYProperty, 0.1, ms, ease, delay);
            Theme.Animate(c, OpacityProperty, 0, ms, ease, delay);
        }

        /// <summary>Tags, the empty note, the selection bar and the "more below" hint.</summary>
        IEnumerable<FrameworkElement> Extras()
        {
            foreach (var el in new FrameworkElement[] { countTag, toolTag, emptyNote, actionBar, moreText })
                if (el != null) yield return el;
        }

        void EnsureTags()
        {
            if (countTag != null) return;
            countTag = MakeCountTag(); toolTag = MakeToolTag(); emptyNote = MakeEmptyNote(); actionBar = MakeActionBar();
            moreText = th.Label("", 11, th.Sub, FontWeights.Normal);
            foreach (var el in Extras()) root.Children.Add(el);
        }

        void DropIn(HangCard c, int delay)
        {
            var back = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            double top = Canvas.GetTop(c);
            c.BeginAnimation(Canvas.TopProperty, null);
            Theme.Animate(c, Canvas.TopProperty, top, 380, back, delay, top - 34);
            Theme.Animate(c.Rot, RotateTransform.AngleProperty, 0, 900, new ElasticEase { Oscillations = 3, Springiness = 4 }, delay, rnd.Next(2) == 0 ? -22 : 22);
            Theme.Animate(c, OpacityProperty, 1, 200, null, delay, 0);
        }

        void KeepClawdOnTop()
        {
            if (Clawd != null) Native.BringToTop(new WindowInteropHelper(Clawd).Handle);
        }

        // ================================================================== open / close

        public void Open()
        {
            if (open || double.IsNaN(anchorX)) return;
            catcher.PruneMissing();
            open = true;
            StartPassTimer();
            var now = Theme.Current();
            if (now.Dark != th.Dark)
            {
                // Windows switched between light and dark: rebuild the tags in the new colours
                th = now;
                foreach (var el in Extras().ToList()) root.Children.Remove(el);
                countTag = null;
            }
            selected.Clear(); multiMode = false;
            scrollRow = 0;
            foreach (var c in cards.Values) { RefreshCard(c); c.Hover = false; HoverVisual(c, false); c.Placed = false; c.Opacity = 1; }
            homeMode = Clawd != null && Clawd.IsHome;
            if (homeMode) { OpenHome(); return; }
            Relayout(false);
            if (!IsVisible) Show();

            // the top rod shoots out sideways from under Clawd
            double clawdLocalX = anchorX - Left;
            var out1 = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (rods.Count > 0)
            {
                var r0 = rods[0];
                double fullW = r0.Width;
                Theme.Animate(r0, WidthProperty, fullW, 300, out1, 0, PerchW);
                Theme.Animate(r0, Canvas.LeftProperty, 10, 300, out1, 0, clawdLocalX - PerchW / 2);
                for (int i = 1; i < rods.Count && i < visRows; i++)
                {
                    Theme.Animate(rods[i], OpacityProperty, 1, 200, null, 180 + i * 80, 0);
                    Theme.Animate(rods[i], Canvas.TopProperty, rack0Y + i * RowH, 380, new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }, 180 + i * 80, rack0Y);
                }
            }
            foreach (var rope in ropes) Theme.Animate(rope, OpacityProperty, 1, 200, null, 260, 0);
            SwingIn(countTag, 260);
            SwingIn(toolTag, 300);
            if (emptyNote.Visibility == Visibility.Visible) { SwingIn(emptyNote, 280); }

            int k = 0;
            for (int i = 0; i < store.Items.Count; i++)
            {
                HangCard c;
                if (!cards.TryGetValue(store.Items[i], out c) || c.Visibility != Visibility.Visible) continue;
                c.Opacity = 0;
                DropIn(c, 200 + Math.Min(k, 18) * 40);
                k++;
            }

        }

        void SwingIn(FrameworkElement el, int delay)
        {
            var rt = el.RenderTransform as RotateTransform;
            el.RenderTransformOrigin = new Point(0.5, 0);
            if (rt == null) { rt = new RotateTransform(); el.RenderTransform = rt; }
            Theme.Animate(el, OpacityProperty, 1, 160, null, delay, 0);
            Theme.Animate(rt, RotateTransform.AngleProperty, 0, 900, new ElasticEase { Oscillations = 3, Springiness = 4 }, delay, 25);
        }

        /// <summary>Clawd takes everything down again and puts the pile on its back.</summary>
        public void CloseRack(bool instant)
        {
            if (!open) return;
            open = false;
            selected.Clear(); multiMode = false;
            if (instant) { ApplyClosedGeometry(); return; }

            var cl = ClawdLocal();
            double cx = cl.X;
            var inEase = new CubicEase { EasingMode = EasingMode.EaseIn };
            int k = 0;
            // nearest first, the way Clawd would take them off the line
            foreach (var c in cards.Values.Where(x => x.Visibility == Visibility.Visible).OrderBy(x => Math.Abs(Canvas.GetLeft(x) + CardW / 2 - cx)))
            {
                FlyAway(c, new Point(cx, cl.Y - 10), 0, Math.Min(k, 14) * 22, 260);
                k++;
            }
            int last = Math.Min(k, 14) * 22 + 200;
            foreach (var el in Extras()) Theme.Animate(el, OpacityProperty, 0, 150, null, 0);
            if (homeMode) RetractLine(last);
            for (int i = 1; i < rods.Count; i++) Theme.Animate(rods[i], OpacityProperty, 0, 160, null, last - 120);
            foreach (var r in ropes) Theme.Animate(r, OpacityProperty, 0, 160, null, last - 120);
            if (rods.Count > 0 && !homeMode)
            {
                Theme.Animate(rods[0], WidthProperty, PerchW, 220, inEase, last);
                Theme.Animate(rods[0], Canvas.LeftProperty, cx - PerchW / 2, 220, inEase, last);
            }
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(last + 240) };
            t.Tick += delegate
            {
                t.Stop();
                if (open) return;
                foreach (var c in cards.Values)
                {
                    c.BeginAnimation(Canvas.LeftProperty, null); c.BeginAnimation(Canvas.TopProperty, null);
                    c.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); c.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    c.BeginAnimation(OpacityProperty, null); c.Opacity = 1; c.Placed = false;
                }
                foreach (var el in rods.Concat(ropes).Concat(Extras())) { el.BeginAnimation(OpacityProperty, null); el.Opacity = 1; }
                ApplyClosedGeometry();
            };
            t.Start();
        }

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (!open || homeMode) return;
            int rows = (store.Count + perRow - 1) / Math.Max(1, perRow);
            int next = Math.Max(0, Math.Min(rows - visRows, scrollRow + (e.Delta > 0 ? -1 : 1)));
            e.Handled = true;
            if (next == scrollRow) return;
            scrollRow = next;
            foreach (var c in cards.Values) c.Placed = false;
            Relayout(false);
            foreach (var c in cards.Values.Where(x => x.Visibility == Visibility.Visible))
            {
                double top = Canvas.GetTop(c);
                Theme.Animate(c, Canvas.TopProperty, top, 260, new CubicEase { EasingMode = EasingMode.EaseOut }, 0, top + (e.Delta > 0 ? -40 : 40));
                Theme.Animate(c, OpacityProperty, 1, 200, null, 0, 0.2);
            }
        }

        public void RefreshHeader()
        {
            if (countText != null) countText.Text = store.Count + "/" + store.Capacity;
        }

        // ================================================================== store events

        void OnAdded(ShotItem it)
        {
            MakeCard(it);
            RefreshHeader();
            if (!open) return;
            scrollRow = 0;
            Relayout(true);
            HangCard c;
            if (cards.TryGetValue(it, out c) && c.Visibility == Visibility.Visible) DropIn(c, 80);
        }

        void OnRemoved(List<ShotItem> items, bool eaten)
        {
            foreach (var it in items) { selected.Remove(it); if (anchorItem == it) anchorItem = null; }
            if (selected.Count == 0) multiMode = false;
            RefreshHeader();
            if (!open)
            {
                foreach (var it in items) RemoveCard(it);
                return;
            }

            // eaten cards fly up into Clawd's mouth; cards whose files vanished just fade where they are
            var cl = ClawdLocal();
            double cx = cl.X;
            int i = 0, last = 0;
            double sideSum = 0;
            foreach (var it in items)
            {
                HangCard c;
                if (!cards.TryGetValue(it, out c) || c.Leaving) continue;
                c.Leaving = true;
                c.IsHitTestVisible = false;
                if (c.Visibility != Visibility.Visible) continue;
                int delay = Math.Min(i, 12) * 35;
                last = Math.Max(last, delay);
                double x0 = Canvas.GetLeft(c) + CardW / 2;
                sideSum += x0 - cx;
                if (eaten) FlyAway(c, new Point(cx, cl.Y - 24), x0 < cx ? 30 : -30, delay, 340);
                else FlyAway(c, null, 0, delay, 340);
                i++;
            }
            LastEatSide = sideSum < 0 ? -1 : 1;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(last + 360) };
            t.Tick += delegate
            {
                t.Stop();
                foreach (var it in items) RemoveCard(it);
                if (open) Relayout(true);
            };
            t.Start();
        }

        void RemoveCard(ShotItem it)
        {
            HangCard c;
            if (!cards.TryGetValue(it, out c)) return;
            root.Children.Remove(c);
            cards.Remove(it);
        }

        // ================================================================== selection

        List<ShotItem> SelectedInOrder() { return store.Items.Where(selected.Contains).ToList(); }

        void SelectAll()
        {
            multiMode = true;
            foreach (var it in store.Items) selected.Add(it);
            foreach (var c in cards.Values) HoverVisual(c, false);
            UpdateActionBar();
        }

        void ClearSelection()
        {
            selected.Clear();
            multiMode = false;
            foreach (var c in cards.Values) HoverVisual(c, false);
            UpdateActionBar();
        }

        void UpdateActionBar()
        {
            if (actionBar == null) return;
            bool show = open && multiMode && selected.Count > 0;
            actionBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) return;
            actionText.Text = Loc.T("已选 " + selected.Count + " 张", selected.Count + " selected");
            actionButtons.Children.Clear();
            actionButtons.Children.Add(th.TextButton(Loc.T("复制", "Copy"), true, CopySelectedAsFiles));
            actionButtons.Children.Add(th.TextButton(Loc.T("取下", "Take down"), false, () => store.Remove(SelectedInOrder())));
            actionButtons.Children.Add(th.TextButton(Loc.T("取消", "Cancel"), false, ClearSelection));
            actionBar.UpdateLayout();
            actionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(actionBar, homeMode ? hCx - actionBar.DesiredSize.Width / 2 : (Width - actionBar.DesiredSize.Width) / 2);
            Canvas.SetTop(actionBar, homeMode ? homeBarY : rack0Y + visRows * RowH - 6);
            Panel.SetZIndex(actionBar, 6000);
        }

        void CardDown(HangCard c, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount == 2) { OpenFile(c.Item); return; }
            pressCard = c;
            pressPt = e.GetPosition(this);
            var pc = e.GetPosition(c.Check);
            pressOnCheck = pc.X >= -3 && pc.Y >= -3 && pc.X <= 23 && pc.Y <= 23;
            c.CaptureMouse();
        }

        void CardMove(HangCard c, MouseEventArgs e)
        {
            if (pressCard != c || e.LeftButton != MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - pressPt.X) < SystemParameters.MinimumHorizontalDragDistance + 2 &&
                Math.Abs(p.Y - pressPt.Y) < SystemParameters.MinimumVerticalDragDistance + 2) return;
            pressCard = null;
            c.ReleaseMouseCapture();
            StartDrag(c);
        }

        void CardUp(HangCard c, MouseButtonEventArgs e)
        {
            if (pressCard != c) return;
            pressCard = null;
            c.ReleaseMouseCapture();
            e.Handled = true;
            var it = c.Item;
            bool ctrl = Native.CtrlDown, shift = Native.ShiftDown;

            if (shift && anchorItem != null && store.Items.Contains(anchorItem))
            {
                int a = store.Items.IndexOf(anchorItem), b = store.Items.IndexOf(it);
                if (!ctrl) selected.Clear();
                for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) selected.Add(store.Items[i]);
                multiMode = true;
            }
            else if (ctrl || pressOnCheck || multiMode)
            {
                if (!selected.Remove(it)) selected.Add(it);
                anchorItem = it;
                multiMode = selected.Count > 0;
            }
            else
            {
                selected.Clear();
                selected.Add(it);
                anchorItem = it;
                CopyImage(c);
            }
            foreach (var card in cards.Values) HoverVisual(card, false);
            UpdateActionBar();
        }

        // ================================================================== copy / open

        void CopyImage(HangCard c)
        {
            var it = c.Item;
            byte[] bytes = it.PendingPng ?? (it.Path != null ? Img.LoadBytes(it.Path) : null);
            if (bytes == null) { Say(Loc.T("找不到原图了…", "Can't find that file…")); return; }
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var src = System.Drawing.Image.FromStream(ms))
                {
                    var bmp = new System.Drawing.Bitmap(src);
                    byte[] png = bytes;
                    bool isPng = bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50;
                    if (!isPng) using (var o = new MemoryStream()) { bmp.Save(o, System.Drawing.Imaging.ImageFormat.Png); png = o.ToArray(); }
                    var data = new WF.DataObject();
                    data.SetData(WF.DataFormats.Bitmap, true, bmp);
                    data.SetData("PNG", false, new MemoryStream(png));
                    data.SetData(Catcher.SelfFormat, false, new MemoryStream(new byte[] { 1 }));
                    WF.Clipboard.SetDataObject(data, true, 6, 80);
                }
                catcher.IgnoreCurrentClipboard();
            }
            catch { Say(Loc.T("剪贴板被占用了，再点一下试试", "The clipboard is busy, try again")); return; }

            var f = new DoubleAnimationUsingKeyFrames();
            f.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(90))));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(650))));
            f.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(900))));
            c.Flash.BeginAnimation(OpacityProperty, f);
            Theme.Animate(c.Rot, RotateTransform.AngleProperty, 0, 700, new ElasticEase { Oscillations = 2, Springiness = 3 }, 0, 6);
            if (Clawd != null) Clawd.SayCopied(1);
        }

        void CopySelectedAsFiles()
        {
            var paths = SavedPaths(SelectedInOrder());
            if (paths.Length == 0) return;
            try
            {
                var data = new WF.DataObject();
                var sc = new StringCollection();
                sc.AddRange(paths);
                data.SetFileDropList(sc);
                data.SetData("Preferred DropEffect", false, new MemoryStream(BitConverter.GetBytes(1)));
                data.SetData(Catcher.SelfFormat, false, new MemoryStream(new byte[] { 1 }));
                WF.Clipboard.SetDataObject(data, true, 6, 80);
                catcher.IgnoreCurrentClipboard();
                if (Clawd != null) Clawd.SayCopied(paths.Length);
            }
            catch { Say(Loc.T("剪贴板被占用了，再试一次", "The clipboard is busy, try again")); }
        }

        void Say(string msg) { if (Clawd != null) Clawd.Say(msg, 1.6); }

        string[] SavedPaths(IEnumerable<ShotItem> items)
        {
            var list = new List<string>();
            foreach (var it in items)
            {
                catcher.EnsureSaved(it);
                if (it.Path != null && File.Exists(it.Path)) list.Add(it.Path);
            }
            return list.ToArray();
        }

        void OpenFile(ShotItem it)
        {
            catcher.EnsureSaved(it);
            if (it.Path == null || !File.Exists(it.Path)) { Say(Loc.T("找不到原图了…", "Can't find that file…")); return; }
            try { Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true }); } catch { }
        }

        public static void OpenFolder(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }
            try { Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true }); } catch { }
        }

        // ================================================================== drag out

        DragDropEffects lastEffect;
        bool dropped;

        void StartDrag(HangCard from)
        {
            var items = selected.Contains(from.Item) && selected.Count > 1 ? SelectedInOrder() : new List<ShotItem> { from.Item };
            var paths = SavedPaths(items);
            if (paths.Length == 0) { Say(Loc.T("找不到原图了…", "Can't find that file…")); return; }

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, paths);
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));

            if (ghost == null) ghost = new DragGhost();
            ghost.ShowFor(items, th);
            lastEffect = DragDropEffects.None;
            dropped = false;
            inDrag = true;
            SetPass(true);
            Theme.Animate(this, OpacityProperty, 0.3, 160);
            foreach (var it in items) { HangCard c; if (cards.TryGetValue(it, out c)) c.Opacity = 0.35; }

            DragDropEffects res = DragDropEffects.None;
            try { res = DragDrop.DoDragDrop(from, data, DragDropEffects.Copy); }
            catch { }

            ghost.Hide();
            inDrag = false;
            SetPass(false);
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            foreach (var it in items) { HangCard c; if (cards.TryGetValue(it, out c)) c.Opacity = 1; }

            if (res != DragDropEffects.None || (dropped && lastEffect != DragDropEffects.None))
                if (Clawd != null) Clawd.Wave(paths.Length);
        }

        void OnGiveFeedback(object sender, GiveFeedbackEventArgs e)
        {
            lastEffect = e.Effects;
            e.UseDefaultCursors = true;
        }

        void OnQueryContinueDrag(object sender, QueryContinueDragEventArgs e)
        {
            if (e.EscapePressed) { e.Action = DragAction.Cancel; return; }
            if ((e.KeyStates & DragDropKeyStates.LeftMouseButton) == 0) dropped = true;
            Native.POINT p;
            if (Native.GetCursorPos(out p) && ghost != null) ghost.MoveTo(p.X / dpi + 14, p.Y / dpi + 12);
        }

        void SetClickThrough(bool on)
        {
            if (on) Native.AddExStyle(Hwnd, Native.WS_EX_TRANSPARENT); else Native.RemoveExStyle(Hwnd, Native.WS_EX_TRANSPARENT);
        }
    }

    /// <summary>The little stack of thumbnails that follows the cursor while dragging shots out.</summary>
    public class DragGhost : Window
    {
        readonly Grid stack = new Grid();

        public DragGhost()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 130; Height = 110;
            IsHitTestVisible = false;
            Content = stack;
            SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
                Native.ExcludeFromCapture(h, true);
            };
        }

        public void ShowFor(List<ShotItem> items, Theme th)
        {
            stack.Children.Clear();
            var show = items.Where(i => i.Thumb != null).Take(3).ToList();
            double[] angles = { -7, 5, 0 };
            int k = 3 - show.Count;
            for (int i = show.Count - 1; i >= 0; i--, k++)
            {
                var brush = new ImageBrush(show[i].Thumb) { Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top };
                var g = new Grid
                {
                    Width = 92, Height = 64, Margin = new Thickness(8, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    RenderTransform = new RotateTransform(angles[k]), RenderTransformOrigin = new Point(0.5, 0.5),
                };
                g.Children.Add(th.SoftShadow(6, 4, 2, 0.25));
                g.Children.Add(new Border { Background = Brushes.White, Padding = new Thickness(3), CornerRadius = new CornerRadius(5), Child = new Border { CornerRadius = new CornerRadius(3), Background = brush } });
                stack.Children.Add(g);
            }
            var badge = new Border
            {
                MinWidth = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = Theme.B(th.Accent),
                BorderBrush = Brushes.White, BorderThickness = new Thickness(2), Padding = new Thickness(5, 0, 5, 0),
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(88, 4, 0, 0),
                Child = new TextBlock
                {
                    Text = items.Count.ToString(), Foreground = Brushes.White, FontFamily = Theme.UiFont, FontSize = 12, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                },
            };
            stack.Children.Add(badge);
            Native.POINT p;
            Native.GetCursorPos(out p);
            Left = p.X / Native.Dpi + 14; Top = p.Y / Native.Dpi + 12;
            Show();
        }

        public void MoveTo(double x, double y) { Left = x; Top = y; }
    }
}
