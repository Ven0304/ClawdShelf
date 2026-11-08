using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClawdShelf
{
    enum Act { Catch, Startle, Spin, Angry, Eat, Wave, Lifted, Wobble, Lift, Hop }

    class Job
    {
        public Act Kind;
        public double Start, Dur;
        public int N;
        public int Side = 1;
        public Action OnMid, OnEnd;
        public bool MidFired;
    }

    class Particle
    {
        public char Type;   // z Z s(smoke) d(drop) p(puff) h(heart)
        public double X, Y, VX, VY, Age, Life;
    }

    /// <summary>
    /// The desktop pet. A transparent, always-on-top, non-activating window that draws Clawd in pixels.
    /// Clawd's feet are the "anchor": it stands on the little rod of the clothes rack.
    /// </summary>
    public class ClawdWindow : Window
    {
        const int CW = 44, CH = 46, CX = 22, BaseY = 44;

        readonly Settings settings;
        readonly ShelfStore store;
        public Catcher Catcher;
        public RackWindow Rack;
        readonly BubbleWindow bubble = new BubbleWindow();
        MenuWindow menu;

        readonly double dpi;
        double S;                          // DIPs per art pixel (snapped to whole physical pixels)
        double ax, ay;                     // where the feet stand, screen DIPs
        readonly Image img;
        readonly WriteableBitmap wb;
        readonly PixelBuf buf = new PixelBuf(CW, CH);
        readonly RotateTransform rot = new RotateTransform();
        readonly Border hit;

        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly DispatcherTimer frameTimer, watchTimer;
        double lastTick;
        uint lastHash;
        readonly Random rnd = new Random();

        Job cur;
        readonly List<Job> queue = new List<Job>();
        readonly List<Particle> parts = new List<Particle>();
        double lastActivity, nextBlink, blinkUntil, blushUntil, nextZ, nextSweat, nextSmoke, pressUntil, happyUntil, lastToggle = -10, lastClick = -10;
        bool sleeping;
        readonly List<double> pokes = new List<double>();

        bool pressed, dragging, complained, rackWasOpen, hintShown;
        // petting: rubbing the mouse back and forth over Clawd
        double petLastX, petUntil, petTotal, nextPetHeart;
        int petDir;
        readonly List<double> petFlips = new List<double>();
        Native.POINT downPt;
        double downLeft, downTop, swing, lastMoveX, lastMoveT;
        bool hiddenForFullscreen, exiting;

        double Now { get { return clock.Elapsed.TotalSeconds; } }
        IntPtr Hwnd { get { return new WindowInteropHelper(this).Handle; } }

        public double PixelSize { get { return S; } }
        public double BodyHeightDip { get { return (10 + ClawdArt.HatRise(settings.Hat)) * S; } }

        public ClawdWindow(Settings settings, ShelfStore store)
        {
            this.settings = settings;
            this.store = store;
            dpi = Native.Dpi;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            ShowActivated = false;
            Title = "Clawd";
            UseLayoutRounding = true;
            Cursor = Cursors.Hand;

            wb = new WriteableBitmap(CW, CH, 96, 96, PixelFormats.Bgra32, null);
            img = new Image { Source = wb, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            img.RenderTransform = rot;
            // invisible hit area: the pet catches the mouse only around its body, the rest is click-through
            hit = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)) };
            var root = new Canvas();
            root.Children.Add(img);
            root.Children.Add(hit);
            Content = root;

            frameTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
            frameTimer.Tick += delegate { Tick(); };
            watchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            watchTimer.Tick += delegate { CheckFullscreen(); };

            lastActivity = Now;
            nextBlink = Now + 2;

            store.Added += it => OnShotAdded();
            // run after the rack has worked out which side the cards fly in from
            store.Removed += (list, eaten) => { if (eaten) Dispatcher.BeginInvoke(new Action(() => Eat(list.Count))); };

            ApplyScale();
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate { PlaceInitially(); frameTimer.Start(); watchTimer.Start(); Tick(); };
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            Native.AddExStyle(Hwnd, Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            ApplyCaptureAffinity();
            if (Catcher != null) Catcher.Start(HwndSource.FromHwnd(Hwnd));
        }

        public void ApplyCaptureAffinity()
        {
            Native.ExcludeFromCapture(Hwnd, settings.HideFromCapture);
            if (Rack != null) Rack.ApplyCaptureAffinity();
        }

        // ------------------------------------------------------------------ size & placement

        void ApplyScale()
        {
            S = Math.Max(1, Math.Round(settings.Scale * dpi, MidpointRounding.AwayFromZero)) / dpi;
            Width = CW * S;
            Height = CH * S;
            img.Width = CW * S;
            img.Height = CH * S;
            hit.Width = 20 * S;
            hit.Height = (12 + ClawdArt.HatRise(settings.Hat) + 3) * S;
            Canvas.SetLeft(hit, (CX - 10) * S);
            Canvas.SetTop(hit, (CH - 12 - ClawdArt.HatRise(settings.Hat) - 3) * S);
            if (IsLoaded) MoveTo(ax, ay);
        }

        void MoveTo(double x, double y)
        {
            ax = x; ay = y;
            Left = ax - CX * S;
            Top = ay - CH * S;
        }

        public Point HomePoint { get { return Home(); } }
        public bool IsHome { get { var h = Home(); return Math.Abs(ax - h.X) < 1 && Math.Abs(ay - h.Y) < 1; } }

        /// <summary>Default home: standing at the top centre of the main screen, on the long pole.</summary>
        Point Home()
        {
            var wa = SystemParameters.WorkArea;
            return new Point(Math.Round(wa.Left + wa.Width / 2), wa.Top + Math.Round(24 * S));
        }

        void PlaceInitially()
        {
            var h = Home();
            double x = double.IsNaN(settings.AnchorX) ? h.X : settings.AnchorX;
            double y = double.IsNaN(settings.AnchorY) ? h.Y : settings.AnchorY;
            MoveTo(x, y);
            ClampToScreen();
            if (Rack != null) Rack.MovePerch(ax, ay, false);
            Native.BringToTop(Hwnd);
        }

        /// <summary>Clawd's head on screen (DIPs), for the speech bubble.</summary>
        Rect HeadRect()
        {
            int up = 10 + Math.Max(ClawdArt.HatRise(CurrentHat()), Rack != null && Rack.IsOpen ? 0 : StackPx() - 2);
            return new Rect(ax - 8 * S, ay - up * S, 16 * S, (up - 4) * S);
        }

        void ClampToScreen()
        {
            var wa = Native.WorkAreaAt((int)(ax * dpi), (int)((ay - 5 * S) * dpi));
            double l = wa.Left / dpi, t = wa.Top / dpi, r = wa.Right / dpi, b = wa.Bottom / dpi;
            double x = Math.Max(l + 10 * S, Math.Min(r - 10 * S, ax));
            double y = Math.Max(t + (12 + ClawdArt.HatRise(settings.Hat)) * S, Math.Min(b - 6, ay));
            MoveTo(x, y);
            settings.AnchorX = ax; settings.AnchorY = ay;
        }

        public void ChangeSize(int dir)
        {
            var list = Settings.Scales;
            int i = 0;
            while (i < list.Length - 1 && list[i] < settings.Scale - 0.01) i++;
            int j = Math.Max(0, Math.Min(list.Length - 1, i + dir));
            if (j == i && Math.Abs(list[i] - settings.Scale) < 0.01)
            {
                Say(dir > 0 ? Loc.T("不能再大啦", "That's as big as I get") : Loc.T("不能再小啦", "That's as small as I get"), 1.2);
                return;
            }
            bool wasHome = IsHome;
            settings.Scale = list[j];
            ApplyScale();
            if (wasHome) MoveTo(Home().X, Home().Y);
            ClampToScreen();
            settings.Save();
            if (Rack != null) Rack.MovePerch(ax, ay, false);
            Enqueue(new Job { Kind = Act.Hop, Dur = 0.35 }, true);
            if (menu != null) menu.Refresh();
        }

        /// <summary>Walk back to the top centre and hang everything on the long pole.</summary>
        public void GoHome()
        {
            if (Rack != null && Rack.IsOpen) Rack.CloseRack(true);
            var h = Home();
            MoveTo(h.X, h.Y);
            ClampToScreen();
            settings.Save();
            Enqueue(new Job { Kind = Act.Hop, Dur = 0.4 }, true);
            Say(Loc.T("回家啦~", "Home sweet home~"), 1.2);
            if (Rack == null) return;
            Rack.MovePerch(ax, ay, false);
            OpenRackSoon(380);
        }

        void OpenRackSoon(int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); if (Rack != null && !Rack.IsOpen) Rack.Open(); };
            t.Start();
        }

        public void SetHat(Hat h)
        {
            settings.Hat = h;
            settings.Save();
            ApplyScale();
            Enqueue(new Job { Kind = Act.Hop, Dur = 0.35 }, true);
            happyUntil = Now + 0.8;
        }

        Hat CurrentHat() { return sleeping ? Hat.Night : settings.Hat; }

        // ------------------------------------------------------------------ input

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            CaptureMouse();
            Native.GetCursorPos(out downPt);
            downLeft = Left; downTop = Top;
            pressed = true; dragging = false;
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!pressed) { TrackPetting(); return; }
            Native.POINT p;
            Native.GetCursorPos(out p);
            double dx = p.X - downPt.X, dy = p.Y - downPt.Y;
            if (!dragging && dx * dx + dy * dy > 36)
            {
                dragging = true;
                complained = false;
                StartLifted();
            }
            if (dragging)
            {
                Left = downLeft + dx / dpi;
                Top = downTop + dy / dpi;
                double t = Now, dt = Math.Max(0.001, t - lastMoveT);
                double vx = (p.X - lastMoveX) / dt / dpi;
                swing = swing * 0.7 + Math.Max(-28, Math.Min(28, -vx * 0.035)) * 0.3;
                lastMoveX = p.X; lastMoveT = t;

                // carried near home: the long pole lights up to show where it will snap
                var h = Home();
                double fx = Left + CX * S, fy = Top + CH * S;
                bool near = Math.Abs(fx - h.X) < 150 && Math.Abs(fy - h.Y) < 90;
                if (near != hintShown)
                {
                    hintShown = near;
                    if (Rack != null) Rack.ShowHomeHint(near);
                    if (near) Native.BringToTop(Hwnd);
                }
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (!pressed) return;
            pressed = false;
            ReleaseMouseCapture();
            if (dragging) EndLifted();
            else Clicked();
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            if (pressed && dragging) { pressed = false; EndLifted(); }
            pressed = false;
        }

        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            Touch();
            if (menu == null) menu = new MenuWindow(this, settings, store);
            menu.ShowAt(new Rect(ax - 8 * S, ay - 12 * S, 16 * S, 12 * S));
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (Native.CtrlDown) ChangeSize(e.Delta > 0 ? 1 : -1);
            else AdjustCapacity(e.Delta > 0 ? 5 : -5);
            e.Handled = true;
        }

        /// <summary>Rubbing the mouse back and forth over Clawd = petting it.</summary>
        void TrackPetting()
        {
            Native.POINT p;
            if (!Native.GetCursorPos(out p)) return;
            double dx = p.X - petLastX;
            if (Math.Abs(dx) < 3 * dpi) return;
            int dir = Math.Sign(dx);
            double t = Now;
            if (petDir != 0 && dir != petDir) petFlips.Add(t);
            petDir = dir;
            petLastX = p.X;
            petFlips.RemoveAll(x => t - x > 1.0);
            if (petFlips.Count < 3) return;
            if (t > petUntil && !sleeping) lastActivity = t;
            if (t > petUntil && sleeping) Say(Loc.T("呼噜…", "purr…"), 1.2);
            petUntil = t + 0.6;
            frameTimer.Interval = TimeSpan.FromMilliseconds(33);
        }

        void Touch()
        {
            lastActivity = Now;
            if (sleeping) { sleeping = false; Enqueue(new Job { Kind = Act.Startle, Dur = 0.5 }, true); }
        }

        // ------------------------------------------------------------------ behaviours

        /// <summary>One click opens the rack, the next click takes it down again. Poking fast makes Clawd shy, then cross.</summary>
        void Clicked()
        {
            bool wasAsleep = sleeping;
            Touch();
            double t = Now;
            bool rapid = t - lastClick < 0.45;
            lastClick = t;
            pokes.Add(t);
            pokes.RemoveAll(x => t - x > 2.2);

            if (cur != null && cur.Kind == Act.Angry)
            {
                if (rapid)
                {
                    // still being poked: stays cross a little longer
                    cur.Start = t;
                    Say(Loc.T("哼！别戳了！", "Hmph! Stop poking!"), 1.2);
                    return;
                }
                // a calm click: Clawd gets over it and does what it is asked
                cur = null;
                pokes.Clear();
                Say(Loc.T("哼…好吧", "Hmph… fine"), 0.9);
            }
            else if (pokes.Count >= 4)
            {
                pokes.Clear();
                queue.Clear();
                Enqueue(new Job { Kind = Act.Angry, Dur = 1.6 }, true);
                Say(Loc.T("哼！", "Hmph!"), 1.2);
                return;
            }
            if (t - lastToggle < 0.8)
            {
                // clicked again while still busy opening/closing: that's a poke
                Enqueue(new Job { Kind = Act.Spin, Dur = 0.75 }, true);
                Say(pokes.Count >= 3 ? Loc.T("别、别戳啦…", "D-don't poke me…") : Loc.T("嘿嘿~", "Hehe~"), 1.1);
                AddHeart();
                return;
            }
            lastToggle = t;
            if (Rack == null) return;
            if (Rack.IsOpen)
            {
                Rack.CloseRack(false);
                Enqueue(new Job { Kind = Act.Hop, Dur = 0.4 }, true);
                happyUntil = t + 1.0;
            }
            else
            {
                // turns around, grabs the pile off its back and hangs it up
                var spin = new Job { Kind = Act.Spin, Dur = 0.75, OnMid = () => { if (Rack != null && !Rack.IsOpen) Rack.Open(); } };
                Enqueue(spin, !wasAsleep);
                AddHeart();
            }
        }

        void StartLifted()
        {
            Touch();
            queue.Clear();
            FinishCurrentEarly();
            rackWasOpen = Rack != null && Rack.IsOpen;
            if (rackWasOpen) Rack.CloseRack(true);
            cur = new Job { Kind = Act.Lifted, Start = Now, Dur = 1e9 };
            swing = 0;
            Native.POINT p; Native.GetCursorPos(out p);
            lastMoveX = p.X; lastMoveT = Now;
        }

        void EndLifted()
        {
            dragging = false;
            double x = Left + CX * S, y = Top + CH * S;
            // dropped while the home pole was lit: snap back home and hang everything on the long pole
            bool snap = hintShown;
            if (snap) { var h = Home(); x = h.X; y = h.Y; }
            if (hintShown && Rack != null) Rack.ShowHomeHint(false);
            hintShown = false;
            MoveTo(x, y);
            ClampToScreen();
            settings.Save();
            cur = null;
            Enqueue(new Job { Kind = Act.Wobble, Dur = 0.8 }, true);
            if (Rack != null)
            {
                Rack.MovePerch(ax, ay, !snap);
                if (snap) Say(Loc.T("回家啦~", "Home sweet home~"), 1.2);
                if (snap || rackWasOpen) OpenRackSoon(380);
            }
        }

        void OnShotAdded()
        {
            bool wasAsleep = sleeping;
            lastActivity = Now;
            sleeping = false;
            if (dragging) return;
            if (wasAsleep)
            {
                Enqueue(new Job { Kind = Act.Startle, Dur = 0.55 }, false);
                Say(Loc.T("！醒了醒了", "!! I'm up, I'm up"), 1.0);
            }
            if (queue.Count(j => j.Kind == Act.Catch) < 2)
                Enqueue(new Job { Kind = Act.Catch, Dur = 0.75 }, false);
        }

        public void Eat(int n)
        {
            lastActivity = Now;
            sleeping = false;
            if (dragging) return;
            int side = Rack != null ? Rack.LastEatSide : 1;
            var last = queue.LastOrDefault();
            if (last != null && last.Kind == Act.Eat) { last.N += n; return; }
            Enqueue(new Job { Kind = Act.Eat, Dur = 1.75, N = n, Side = side }, false);
        }

        public void Wave(int n)
        {
            lastActivity = Now;
            if (dragging) return;
            if (queue.Any(j => j.Kind == Act.Wave) || (cur != null && cur.Kind == Act.Wave)) return;
            Enqueue(new Job { Kind = Act.Wave, Dur = 1.3 }, false);
            Say(n > 1 ? Loc.T(n + " 张都送出去啦，拜拜~", "Bye-bye, all " + n + "!") : Loc.T("拜拜~", "Bye-bye~"), 1.3);
        }

        public void SayCopied(int n)
        {
            lastActivity = Now;
            happyUntil = Now + 1.0;
            if (cur == null) Enqueue(new Job { Kind = Act.Hop, Dur = 0.35 }, false);
            Say(n > 1 ? Loc.T("复制了 " + n + " 张，去粘贴吧", n + " copied — paste away!") : Loc.T("复制好啦，去粘贴吧", "Copied — paste away!"), 1.4);
        }

        public void AdjustCapacity(int step)
        {
            Touch();
            int cap = Math.Max(Settings.MinCapacity, Math.Min(Settings.MaxCapacity, store.Capacity + step));
            if (cap == store.Capacity)
            {
                Say(step > 0 ? Loc.T("最多扛 100 张啦", "100 is my limit") : Loc.T("至少得扛 5 张", "I hold at least 5"), 1.2);
                return;
            }
            store.Capacity = cap;
            settings.Capacity = cap;
            settings.Save();
            if (step > 0) pressUntil = Now + 0.15;
            if (cur != null && cur.Kind == Act.Lift) cur.Dur = Now - cur.Start + 1.4;
            else
            {
                queue.RemoveAll(j => j.Kind == Act.Lift);
                Enqueue(new Job { Kind = Act.Lift, Dur = 1.4, OnEnd = () => store.Trim() }, true);
            }
            Say(Loc.T("能扛 " + cap + " 张", "I can hold " + cap) + (store.Count > cap ? Loc.T("（多的吃掉）", " (eating the rest)") : ""), 1.6);
            if (Rack != null) Rack.RefreshHeader();
            if (menu != null) menu.Refresh();
        }

        public void SayBye(Action then)
        {
            exiting = true;
            queue.Clear();
            cur = null;
            if (Rack != null) Rack.CloseRack(true);
            Enqueue(new Job { Kind = Act.Wave, Dur = 1.0, OnEnd = then }, true);
            Say(Loc.T("拜拜~", "Bye-bye~"), 1.0);
        }

        public void Say(string text, double seconds)
        {
            if (hiddenForFullscreen) return;
            bubble.Say(text, seconds, HeadRect());
        }

        void Enqueue(Job j, bool now)
        {
            if (now || cur == null)
            {
                FinishCurrentEarly();
                j.Start = Now;
                cur = j;
            }
            else queue.Add(j);
            frameTimer.Interval = TimeSpan.FromMilliseconds(33);
        }

        /// <summary>An interrupted job still delivers its side effects (open the rack, trim the pile...).</summary>
        void FinishCurrentEarly()
        {
            if (cur == null) return;
            var j = cur;
            cur = null;
            if (j.OnMid != null && !j.MidFired) { j.MidFired = true; j.OnMid(); }
            if (j.OnEnd != null) { var end = j.OnEnd; j.OnEnd = null; end(); }
        }

        void AddHeart()
        {
            parts.Add(new Particle { Type = 'h', X = CX + 6, Y = BaseY - 14, VX = 4, VY = -9, Life = 0.9 });
        }

        // ------------------------------------------------------------------ frame loop

        int StackPx()
        {
            if (store.Count == 0) return 0;
            return Math.Max(2, (int)Math.Round(store.Fullness * 18));
        }

        void Tick()
        {
            double t = Now, dt = Math.Min(0.1, t - lastTick);
            lastTick = t;

            if (cur != null && !cur.MidFired && cur.OnMid != null && t - cur.Start >= cur.Dur * 0.45)
            {
                cur.MidFired = true; cur.OnMid();
            }
            if (cur != null && t - cur.Start >= cur.Dur)
            {
                var done = cur;
                cur = null;
                if (done.OnEnd != null) done.OnEnd();
            }
            if (cur == null && queue.Count > 0)
            {
                cur = queue[0];
                queue.RemoveAt(0);
                cur.Start = t;
            }

            if (!sleeping && cur == null && !exiting && t - lastActivity > settings.SleepAfterSec)
            {
                sleeping = true;
                nextZ = t;
            }

            // petted long enough: shy little twirl
            if (t < petUntil) petTotal += dt; else petTotal = Math.Max(0, petTotal - dt * 0.5);
            if (petTotal > 2.6 && cur == null && !sleeping)
            {
                petTotal = 0;
                Enqueue(new Job { Kind = Act.Spin, Dur = 0.75 }, true);
                Say(Loc.T("嘿嘿，好舒服~", "Hehe, that tickles~"), 1.4);
            }

            var p = BuildPose(t);
            UpdateParticles(t, dt, p);
            Render(p, t);

            // 30 fps only while something is actually moving; sleepy zzz drift happily at ~8 fps
            bool busy = cur != null || queue.Count > 0 || (parts.Count > 0 && !sleeping);
            var want = TimeSpan.FromMilliseconds(busy ? 33 : (sleeping ? 125 : 80));
            if (frameTimer.Interval != want) frameTimer.Interval = want;
        }

        static double Ease(double x) { x = Math.Max(0, Math.Min(1, x)); return x * x * (3 - 2 * x); }

        Pose BuildPose(double t)
        {
            bool rackOpen = Rack != null && Rack.IsOpen;
            var p = new Pose { StackPx = rackOpen ? 0 : StackPx(), Hat = CurrentHat() };
            double full = store.Fullness;
            if (full >= 0.8 && !rackOpen) p.StackSway = 1;

            if (t > nextBlink) { blinkUntil = t + 0.12; nextBlink = t + 2.2 + rnd.NextDouble() * 4; }
            if (t < blinkUntil) p.Eyes = Eyes.Blink;
            if (t < happyUntil) p.Eyes = Eyes.Happy;
            if (t < blushUntil) p.Blush = true;
            if (t < petUntil) { p.Blush = true; p.Eyes = Eyes.Happy; }

            if (full >= 1.0 && !rackOpen && cur == null && !sleeping)
            {
                // can't carry any more: trembling legs, straining now and then
                if (Math.Sin(t * 1.3) > 0.55) { p.Eyes = Eyes.Strain; p.X = ((int)(t * 14) % 2 == 0) ? 0 : 1; }
            }

            if (sleeping && cur == null)
            {
                p.Eyes = Eyes.Closed;
                p.Blush = t < petUntil;          // petted in its sleep: smiles, does not wake up
                p.Stretch = Math.Sin(t * 2.2) > 0.3 ? -1 : 0;
                return p;
            }
            if (cur == null) return p;

            double e = t - cur.Start, k = e / cur.Dur;
            switch (cur.Kind)
            {
                case Act.Startle:
                    p.Eyes = Eyes.Wide; p.LookY = -1;
                    p.Y = e < 0.18 ? -(int)Math.Round(3 * Math.Sin(Math.PI * e / 0.18)) : 0;
                    p.LeftArm = p.RightArm = Arm.Up;
                    break;

                case Act.Catch:
                    if (e < 0.08) p.Stretch = -1;
                    else if (e < 0.42)
                    {
                        p.Y = -(int)Math.Round(6 * Math.Sin(Math.PI * (e - 0.08) / 0.34));
                        p.Stretch = 1;
                        p.Eyes = Eyes.Wide; p.LookY = -1;
                        p.LeftArm = p.RightArm = Arm.Up;
                    }
                    else if (e < 0.52) p.Stretch = -1;
                    else p.Eyes = Eyes.Happy;
                    break;

                case Act.Spin:
                    double a = Math.Round(Ease(k) * 16) / 16.0 * Math.PI * 2;
                    p.Angle = a % (Math.PI * 2);
                    p.Eyes = Eyes.Happy; p.Blush = true;
                    if (k > 0.2 && k < 0.8) p.Y = -1;
                    if (cur.MidFired) p.StackPx = 0;
                    blushUntil = t + 1.0;
                    break;

                case Act.Angry:
                    p.Eyes = Eyes.Angry;
                    p.Anger = 0.55 + 0.25 * Math.Sin(e * 9);
                    p.Stretch = ((int)(e * 7) % 2 == 0) ? 0 : -1;
                    p.X = ((int)(e * 18) % 2 == 0) ? 0 : (e < 0.6 ? 1 : 0);
                    p.LeftArm = p.RightArm = ((int)(e * 7) % 2 == 0) ? Arm.Mid : Arm.Down;
                    break;

                case Act.Eat:
                    p.LookX = cur.Side;
                    if (e < 0.25) { p.Jaw = (int)Math.Round(3 * e / 0.25); p.Eyes = Eyes.Wide; }
                    else if (e < 0.55) { p.Jaw = 3; p.Eyes = Eyes.Wide; }
                    else if (e < 0.65) p.Jaw = (int)Math.Round(3 * (0.65 - e) / 0.1);
                    else if (e < 1.15) { p.Eyes = Eyes.Happy; p.Stretch = ((int)(e * 9) % 2 == 0) ? -1 : 0; p.LookX = 0; }
                    else if (e < 1.5) { p.Jaw = 1; p.Stretch = 1; p.Eyes = Eyes.Closed; p.LookX = 0; }
                    else { p.Eyes = Eyes.Happy; p.LookX = 0; }
                    break;

                case Act.Wave:
                    p.Eyes = Eyes.Happy;
                    p.RightArm = ((int)(e / 0.16) % 2 == 0) ? Arm.Up : Arm.Mid;
                    break;

                case Act.Hop:
                    p.Eyes = Eyes.Happy;
                    p.Y = -(int)Math.Round(2 * Math.Sin(Math.PI * k));
                    break;

                case Act.Lifted:
                    p.Stretch = 1;
                    p.Eyes = ((int)(e * 2.5) % 3 == 2) ? Eyes.Strain : Eyes.Wide;
                    p.Legs = Legs.Kick;
                    p.LegFrame = (int)(e * 14);
                    p.LeftArm = ((int)(e * 9) % 2 == 0) ? Arm.Mid : Arm.Down;
                    p.RightArm = ((int)(e * 9) % 2 == 1) ? Arm.Mid : Arm.Down;
                    if (!complained && e > 1.6) { complained = true; Say(Loc.T("放我下来！", "Put me down!"), 1.3); }
                    break;

                case Act.Wobble:
                    if (e < 0.1) p.Stretch = -1;
                    break;

                case Act.Lift:
                    int cap = store.Capacity;
                    p.LeftArm = p.RightArm = Arm.Hold;
                    p.Plate = 3 + (int)Math.Round((cap - Settings.MinCapacity) * 6.0 / (Settings.MaxCapacity - Settings.MinCapacity));
                    p.BarLift = t < pressUntil ? 2 : 0;
                    p.Eyes = cap >= 60 ? Eyes.Strain : (cap >= 30 ? Eyes.Open : Eyes.Happy);
                    p.Stretch = cap >= 60 ? -1 : 0;
                    p.LookY = -1;
                    if (cap >= 80 && cap < 100) p.X = ((int)(t * 16) % 2 == 0) ? 0 : 1;
                    if (cap >= 100) p.X = ((int)(t * 22) % 2 == 0) ? 0 : 1;
                    p.StackPx = 0;
                    break;
            }
            return p;
        }

        void UpdateParticles(double t, double dt, Pose p)
        {
            int top = ClawdArt.BodyTop(p, BaseY) - ClawdArt.HatRise(p.Hat);
            if (t < petUntil && t > nextPetHeart) { nextPetHeart = t + 0.45; AddHeart(); }
            if (sleeping && cur == null && t > nextZ)
            {
                nextZ = t + 1.1;
                parts.Add(new Particle { Type = rnd.Next(2) == 0 ? 'z' : 'Z', X = CX + 6, Y = top - 1, VX = 3.5, VY = -5, Life = 2.4 });
            }
            bool sweaty = (store.Fullness >= 0.8 && (Rack == null || !Rack.IsOpen) && cur == null && !sleeping) || (cur != null && cur.Kind == Act.Lift && store.Capacity >= 80);
            if (sweaty && t > nextSweat)
            {
                nextSweat = t + 1.1 + rnd.NextDouble() * 1.2;
                int side = rnd.Next(2) == 0 ? -1 : 1;
                parts.Add(new Particle { Type = 'd', X = CX + side * 6, Y = top + 2, VX = side * (6 + rnd.NextDouble() * 5), VY = -10, Life = 0.7 });
            }
            if (cur != null && cur.Kind == Act.Angry && t > nextSmoke)
            {
                nextSmoke = t + 0.12;
                parts.Add(new Particle { Type = 's', X = CX - 3 + rnd.Next(7), Y = top, VX = rnd.NextDouble() * 6 - 3, VY = -12 - rnd.NextDouble() * 6, Life = 0.9 });
            }
            if (cur != null && cur.Kind == Act.Eat && !cur.MidFired && t - cur.Start > 1.18)
            {
                cur.MidFired = true;
                parts.Add(new Particle { Type = 'p', X = CX + 5, Y = ClawdArt.BodyTop(p, BaseY) + 3, VX = 8, VY = -6, Life = 0.8 });
                Say(cur.N >= 5 ? Loc.T("嗝——！吃撑了", "BURP! So full") : Loc.T("嗝~", "*burp*"), 1.2);
            }

            for (int i = parts.Count - 1; i >= 0; i--)
            {
                var q = parts[i];
                q.Age += dt;
                q.X += q.VX * dt; q.Y += q.VY * dt;
                if (q.Type == 'd') q.VY += 40 * dt;
                if (q.Age >= q.Life) parts.RemoveAt(i);
            }
        }

        void Render(Pose p, double t)
        {
            buf.Clear();
            ClawdArt.Draw(buf, p, CX, BaseY, t);

            int top = ClawdArt.BodyTop(p, BaseY);
            int hatTop = top - ClawdArt.HatRise(p.Hat);
            if (cur != null)
            {
                double e = t - cur.Start;
                switch (cur.Kind)
                {
                    case Act.Catch:
                        if (e < 0.34 && (Rack == null || !Rack.IsOpen))
                        {
                            int landY = BaseY - 8 - StackPx() - 6;
                            double f = e / 0.34;
                            int y = (int)Math.Round(-6 + (landY + 6) * f * f);
                            ClawdArt.Glyph(buf, ClawdArt.Card, CX + 2, y, 0xFFFFFFFF);
                        }
                        break;
                    case Act.Startle:
                        ClawdArt.Glyph(buf, ClawdArt.Bang, CX + 8, hatTop - 3, 0xFFE5484D);
                        break;
                    case Act.Angry:
                        ClawdArt.Glyph(buf, ClawdArt.AngerMark, CX + 6, hatTop - 3, 0xFFE5484D);
                        break;
                    case Act.Eat:
                        if (e > 0.15 && e < 0.58)
                        {
                            double f = Ease((e - 0.15) / 0.4);
                            int from = cur.Side * 20;
                            int x = (int)Math.Round(CX + from * (1 - f)) - 3;
                            int y = top + 1 + (int)Math.Round(6 * (1 - f));
                            int shown = Math.Min(3, Math.Max(1, cur.N));
                            for (int i = shown - 1; i >= 0; i--)
                                ClawdArt.Glyph(buf, ClawdArt.Card, x + i * cur.Side * 2, y - i, 0xFFFFFFFF);
                        }
                        break;
                }
            }

            foreach (var q in parts)
            {
                double fade = 1 - q.Age / q.Life;
                int x = (int)Math.Round(q.X), y = (int)Math.Round(q.Y);
                switch (q.Type)
                {
                    case 'z': ClawdArt.Glyph(buf, ClawdArt.ZSmall, x, y, 0xFF7D8FE6, Math.Min(1, fade * 2)); break;
                    case 'Z': ClawdArt.Glyph(buf, ClawdArt.ZBig, x, y, 0xFF7D8FE6, Math.Min(1, fade * 2)); break;
                    case 'd': ClawdArt.Glyph(buf, ClawdArt.Drop, x, y, 0xFF6EC1F0, Math.Min(1, fade * 2)); break;
                    case 'h': ClawdArt.Glyph(buf, ClawdArt.Heart, x, y, 0xFFF2899A, Math.Min(1, fade * 2)); break;
                    case 'p': ClawdArt.Glyph(buf, ClawdArt.Puff, x, y, 0xFFFFFFFF, fade); break;
                    case 's':
                        int r = 1 + (int)(q.Age / q.Life * 2.5);
                        uint gray = (uint)(0x80 + (int)(q.Age / q.Life * 0x40));
                        uint col = ((uint)(fade * 220) << 24) | (gray << 16) | (gray << 8) | gray;
                        for (int j = -r; j <= r; j++)
                            for (int i = -r; i <= r; i++)
                                if (i * i + j * j <= r * r + 1) buf.Blend(x + i, y + j, col);
                        break;
                }
            }

            // only hand a frame to WPF when it actually changed - most idle frames are identical
            uint hash = buf.Hash();
            if (hash != lastHash)
            {
                lastHash = hash;
                wb.WritePixels(new Int32Rect(0, 0, CW, CH), buf.P, CW * 4, 0);
            }

            // whole-sprite rotation: swinging while carried, wobbling after being put down
            double angle = 0, pivotY = CH * S;
            if (cur != null && cur.Kind == Act.Lifted)
            {
                swing *= 0.92;
                angle = swing;
                pivotY = (hatTop - 1) * S;
            }
            else if (cur != null && cur.Kind == Act.Wobble)
            {
                double e = t - cur.Start;
                angle = 11 * Math.Exp(-4.5 * e) * Math.Sin(2 * Math.PI * e * 2.6);
            }
            angle = Math.Round(angle, 1);
            if (rot.Angle != angle || rot.CenterY != pivotY || rot.CenterX != CX * S)
            {
                rot.CenterX = CX * S;
                rot.CenterY = pivotY;
                rot.Angle = angle;
            }
        }

        // ------------------------------------------------------------------ fullscreen apps

        static readonly uint MyPid = (uint)Process.GetCurrentProcess().Id;

        void CheckFullscreen()
        {
            if (!settings.AutoHideFullscreen || exiting) { if (hiddenForFullscreen) ShowBack(); return; }
            IntPtr fg = Native.GetForegroundWindow();
            bool full = false;
            if (fg != IntPtr.Zero)
            {
                uint pid;
                Native.GetWindowThreadProcessId(fg, out pid);
                string cls = Native.ClassOf(fg);
                if (pid != MyPid && cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd")
                {
                    Native.RECT wr;
                    var mi = new Native.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                    if (Native.GetWindowRect(fg, out wr) && Native.GetMonitorInfo(Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST), ref mi))
                    {
                        var m = mi.rcMonitor;
                        var myMon = Native.MonitorFromPoint(new Native.POINT { X = (int)(ax * dpi), Y = (int)((ay - 5) * dpi) }, Native.MONITOR_DEFAULTTONEAREST);
                        full = wr.Left <= m.Left && wr.Top <= m.Top && wr.Right >= m.Right && wr.Bottom >= m.Bottom
                               && myMon == Native.MonitorFromWindow(fg, Native.MONITOR_DEFAULTTONEAREST);
                    }
                }
            }
            if (full && !hiddenForFullscreen && !pressed)
            {
                hiddenForFullscreen = true;
                Hide();
                if (Rack != null) Rack.Hide();
                bubble.Hide();
            }
            else if (!full && hiddenForFullscreen) ShowBack();
        }

        void ShowBack()
        {
            hiddenForFullscreen = false;
            if (Rack != null) Rack.Show();
            Show();
            Native.BringToTop(Hwnd);
        }
    }
}
