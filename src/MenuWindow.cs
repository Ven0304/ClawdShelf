using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClawdShelf
{
    /// <summary>Right-click menu of Clawd.</summary>
    public class MenuWindow : Window
    {
        const double Pad = 18;
        readonly ClawdWindow clawd;
        readonly Settings settings;
        readonly ShelfStore store;
        Theme th;
        Grid panel;
        TextBlock capText, sizeText;
        StackPanel hatRow;
        readonly double dpi;

        public MenuWindow(ClawdWindow clawd, Settings settings, ShelfStore store)
        {
            this.clawd = clawd; this.settings = settings; this.store = store;
            dpi = Native.Dpi;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Deactivated += delegate { Hide(); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Hide(); };
            SourceInitialized += delegate
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.AddExStyle(h, Native.WS_EX_TOOLWINDOW);
                Native.ExcludeFromCapture(h, true);
            };
        }

        void Build()
        {
            th = Theme.Current();
            var sp = new StackPanel { Width = 268, Margin = new Thickness(6) };

            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 8) };
            var icon = Theme.PixelClawd(new Pose { Eyes = Eyes.Happy, Blush = true, Hat = settings.Hat }, 1.5);
            icon.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(icon);
            head.Children.Add(th.Label("ClawdShelf", 13, th.Text, FontWeights.Bold));
            head.Children.Add(th.Label("  " + Loc.T("挂着 " + store.Count + " 张", store.Count + " on the rack"), 11.5, th.Sub, FontWeights.Normal));
            sp.Children.Add(head);
            sp.Children.Add(Sep());

            bool open = clawd.Rack != null && clawd.Rack.IsOpen;
            sp.Children.Add(Item(open ? "" : "", open ? Loc.T("把截图收起来", "Take the shots down") : Loc.T("把截图挂出来", "Hang the shots up"), () =>
            {
                Hide();
                if (clawd.Rack == null) return;
                if (clawd.Rack.IsOpen) clawd.Rack.CloseRack(false); else clawd.Rack.Open();
            }));

            if (!clawd.IsHome)
                sp.Children.Add(Item("", Loc.T("回老地方（屏幕顶部正中）", "Go home (top centre)"), () => { Hide(); clawd.GoHome(); }));

            capText = th.Label("", 13, th.Accent, FontWeights.Bold);
            sp.Children.Add(Stepper("", Loc.T("能挂几张", "Rack holds"), Loc.T("在 Clawd 身上滚滚轮也行", "or scroll the wheel on Clawd"), capText,
                () => clawd.AdjustCapacity(-5), () => clawd.AdjustCapacity(5)));
            sizeText = th.Label("", 13, th.Accent, FontWeights.Bold);
            sp.Children.Add(Stepper("", Loc.T("Clawd 大小", "Clawd size"), Loc.T("Ctrl + 滚轮也行", "or Ctrl + scroll on Clawd"), sizeText,
                () => clawd.ChangeSize(-1), () => clawd.ChangeSize(1)));

            // hat picker
            var hatBox = new StackPanel { Margin = new Thickness(12, 4, 10, 6) };
            hatBox.Children.Add(th.Label(Loc.T("帽子", "Hat"), 12.5, th.Text, FontWeights.Normal));
            hatRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var h in new[] { Hat.Straw, Hat.Beanie, Hat.TopHat, Hat.None })
                hatRow.Children.Add(HatChip(h));
            hatBox.Children.Add(hatRow);
            sp.Children.Add(hatBox);

            sp.Children.Add(Sep());
            sp.Children.Add(Item("", Loc.T("打开截图文件夹", "Open screenshots folder"), () => { Hide(); RackWindow.OpenFolder(Settings.ScreenshotsDir); }));
            sp.Children.Add(Item("", Loc.T("打开剪贴板截图保存处", "Open saved clipboard shots"), () => { Hide(); RackWindow.OpenFolder(Settings.OwnDir); }));
            sp.Children.Add(Sep());
            sp.Children.Add(Toggle(Loc.T("截图时隐身（不会被截进图里）", "Stay out of screenshots"), settings.HideFromCapture, v => { settings.HideFromCapture = v; settings.Save(); clawd.ApplyCaptureAffinity(); }));
            sp.Children.Add(Toggle(Loc.T("全屏看视频 / 玩游戏时躲起来", "Hide while an app is fullscreen"), settings.AutoHideFullscreen, v => { settings.AutoHideFullscreen = v; settings.Save(); }));
            sp.Children.Add(Toggle(Loc.T("接住只进剪贴板的截图（微信 / QQ 等）", "Catch clipboard-only screenshots (chat apps, Snipaste…)"), settings.CatchClipboard, v => { settings.CatchClipboard = v; settings.Save(); }));
            sp.Children.Add(Toggle(Loc.T("开机自动启动", "Start with Windows"), Settings.AutoStart, v => Settings.AutoStart = v));
            sp.Children.Add(Sep());
            sp.Children.Add(Item("", Loc.T("退出", "Quit"), () =>
            {
                Hide();
                clawd.SayBye(() => Application.Current.Shutdown());
            }));

            var box = new Border
            {
                CornerRadius = new CornerRadius(14), Background = Theme.B(th.Bg),
                BorderBrush = Theme.B(th.Border), BorderThickness = new Thickness(1), Child = sp,
            };
            panel = new Grid { Margin = new Thickness(Pad), RenderTransform = new ScaleTransform(1, 1) };
            panel.Children.Add(th.SoftShadow(14, 12, 4, th.Dark ? 0.5 : 0.16));
            panel.Children.Add(box);
            Content = panel;
            Refresh();
        }

        Border HatChip(Hat h)
        {
            var img = Theme.PixelClawd(new Pose { Hat = h, Eyes = Eyes.Happy }, 1.4);
            var b = new Border
            {
                Width = 52, Height = 40, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1.5), Child = img, Tag = h,
                ToolTip = h == Hat.Straw ? Loc.T("草帽", "Straw hat") : h == Hat.Beanie ? Loc.T("毛线帽", "Beanie") : h == Hat.TopHat ? Loc.T("礼帽", "Top hat") : Loc.T("不戴", "No hat"),
            };
            img.VerticalAlignment = VerticalAlignment.Bottom;
            img.Margin = new Thickness(0, 0, 0, 2);
            b.MouseLeftButtonUp += (s, e) => { clawd.SetHat(h); Refresh(); };
            return b;
        }

        Border Sep()
        {
            return new Border { Height = 1, Background = Theme.B(th.Border), Margin = new Thickness(10, 4, 10, 4) };
        }

        Border Row(UIElement content, Action click, double height)
        {
            var b = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Padding = new Thickness(10, 0, 10, 0), MinHeight = height, Child = content, Cursor = Cursors.Hand, Margin = new Thickness(2, 0, 2, 0) };
            b.MouseEnter += (s, e) => b.Background = Theme.B(th.Hover);
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => click();
            return b;
        }

        Border Item(string glyph, string text, Action click)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 14, Foreground = Theme.B(th.Sub), Width = 26, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(th.Label(text, 12.5, th.Text, FontWeights.Normal));
            return Row(sp, click, 36);
        }

        Grid Stepper(string glyph, string title, string hint, TextBlock value, Action minus, Action plus)
        {
            var g = new Grid { Height = 42, Margin = new Thickness(2, 0, 2, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 14, Foreground = Theme.B(th.Sub), Width = 26, VerticalAlignment = VerticalAlignment.Center });
            var texts = new StackPanel();
            texts.Children.Add(th.Label(title, 12.5, th.Text, FontWeights.Normal));
            texts.Children.Add(th.Label(hint, 10.5, th.Sub, FontWeights.Normal));
            label.Children.Add(texts);
            g.Children.Add(label);
            var stepper = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
            stepper.Children.Add(th.IconButton("", "−", minus));
            value.MinWidth = 30;
            value.TextAlignment = TextAlignment.Center;
            stepper.Children.Add(value);
            stepper.Children.Add(th.IconButton("", "+", plus));
            Grid.SetColumn(stepper, 1);
            g.Children.Add(stepper);
            g.MouseWheel += (s, e) => { e.Handled = true; if (e.Delta > 0) plus(); else minus(); };
            return g;
        }

        Border Toggle(string text, bool on, Action<bool> set)
        {
            var g = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = th.Label(text, 12, th.Text, FontWeights.Normal);
            label.TextWrapping = TextWrapping.Wrap;
            g.Children.Add(label);
            var knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Left };
            var track = new Border { Width = 32, Height = 18, CornerRadius = new CornerRadius(9), Child = knob, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(track, 1);
            g.Children.Add(track);
            bool state = on;
            Action paint = () =>
            {
                track.Background = Theme.B(state ? th.Accent : (th.Dark ? Color.FromRgb(0x55, 0x54, 0x50) : Color.FromRgb(0xCF, 0xCA, 0xBF)));
                knob.Margin = new Thickness(state ? 17 : 3, 0, 3, 0);
            };
            paint();
            return Row(g, () => { state = !state; paint(); set(state); }, 36);
        }

        public void Refresh()
        {
            if (capText != null) capText.Text = store.Capacity.ToString();
            if (sizeText != null) sizeText.Text = Math.Round(settings.Scale / Settings.DefaultScale * 100) + "%";
            if (hatRow != null)
                foreach (Border b in hatRow.Children)
                {
                    bool on = (Hat)b.Tag == settings.Hat;
                    b.BorderBrush = Theme.B(on ? th.Accent : th.Border);
                    b.Background = Theme.B(on ? th.AccentSoft : Colors.Transparent);
                }
        }

        public void ShowAt(Rect clawdRect)
        {
            Build();
            Show();
            UpdateLayout();
            var wa = Native.WorkAreaAt((int)((clawdRect.Left + clawdRect.Width / 2) * dpi), (int)((clawdRect.Top + clawdRect.Height / 2) * dpi));
            double wl = wa.Left / dpi, wt = wa.Top / dpi, wr = wa.Right / dpi, wb = wa.Bottom / dpi;
            double w = ActualWidth, h = ActualHeight;
            // open below Clawd when it sits high on the screen, otherwise above
            bool below = clawdRect.Top - wt < h;
            double x = clawdRect.Left + clawdRect.Width / 2 - w / 2;
            double y = below ? clawdRect.Bottom - Pad + 6 : clawdRect.Top - h + Pad - 6;
            Left = Math.Max(wl - Pad + 4, Math.Min(wr - w + Pad - 4, x));
            Top = Math.Max(wt - Pad + 4, Math.Min(wb - h + Pad - 4, y));
            Activate();
            Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);

            var st = (ScaleTransform)panel.RenderTransform;
            panel.RenderTransformOrigin = new Point(0.5, below ? 0 : 1);
            var ease = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut };
            Theme.Animate(st, ScaleTransform.ScaleXProperty, 1, 220, ease, 0, 0.8);
            Theme.Animate(st, ScaleTransform.ScaleYProperty, 1, 220, ease, 0, 0.8);
            Theme.Animate(panel, OpacityProperty, 1, 120, null, 0, 0);
        }
    }
}
