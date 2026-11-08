using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using WinClip = System.Windows.Forms.Clipboard;

namespace ClawdShelf
{
    /// <summary>
    /// Catches new screenshots from two roads:
    ///  1. files appearing in Pictures\Screenshots (Win+Shift+S, PrtSc, Win+PrtSc) -> referenced in place
    ///  2. images landing on the clipboard only (WeChat / QQ / Snipaste ...) -> saved once to Pictures\ClawdShelf
    /// The same shot arriving on both roads is merged into one item.
    /// </summary>
    public class Catcher
    {
        public const string SelfFormat = "ClawdShelf.Self";
        const double TwinWindowSec = 25;
        const int SaveDelayMs = 3000;

        readonly ShelfStore store;
        readonly Settings settings;
        readonly Dispatcher ui;
        FileSystemWatcher fsw;
        readonly DispatcherTimer clipDebounce;
        uint lastSeq, ignoreSeq;
        readonly HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly uint cfPng, cfSelf, cfChromeUrl, cfMozCtx;

        public Catcher(ShelfStore store, Settings settings, Dispatcher ui)
        {
            this.store = store; this.settings = settings; this.ui = ui;
            cfPng = Native.RegisterClipboardFormat("PNG");
            cfSelf = Native.RegisterClipboardFormat(SelfFormat);
            cfChromeUrl = Native.RegisterClipboardFormat("Chromium internal source URL");
            cfMozCtx = Native.RegisterClipboardFormat("text/_moz_htmlcontext");
            clipDebounce = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(250) };
            clipDebounce.Tick += delegate { clipDebounce.Stop(); ReadClipboard(); };
            foreach (var it in store.Items) if (it.Path != null) seenPaths.Add(it.Path);
        }

        public void Start(HwndSource hwnd)
        {
            hwnd.AddHook(WndProc);
            Native.AddClipboardFormatListener(hwnd.Handle);
            lastSeq = Native.GetClipboardSequenceNumber();

            string dir = Settings.ScreenshotsDir;
            try { Directory.CreateDirectory(dir); } catch { }
            try
            {
                fsw = new FileSystemWatcher(dir);
                fsw.IncludeSubdirectories = false;
                fsw.NotifyFilter = NotifyFilters.FileName;
                fsw.Created += (s, e) => OnFile(e.FullPath);
                fsw.Renamed += (s, e) => OnRenamed(e.OldFullPath, e.FullPath);
                fsw.Deleted += (s, e) => OnGone(e.FullPath);
                fsw.EnableRaisingEvents = true;
            }
            catch { }
            try
            {
                Directory.CreateDirectory(Settings.OwnDir);
                ownWatch = new FileSystemWatcher(Settings.OwnDir) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName };
                ownWatch.Renamed += (s, e) => OnRenamed(e.OldFullPath, e.FullPath);
                ownWatch.Deleted += (s, e) => OnGone(e.FullPath);
                ownWatch.EnableRaisingEvents = true;
            }
            catch { }
        }

        /// <summary>Raised (on the UI thread) when shots left the rack because their files were deleted or moved away.</summary>
        public event Action<int> FilesGone;
        FileSystemWatcher ownWatch;

        void OnGone(string path)
        {
            ui.BeginInvoke(new Action(() =>
            {
                var gone = store.Items.Where(it => it.Path != null && string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase) && !File.Exists(it.Path)).ToList();
                if (gone.Count == 0) return;
                store.Drop(gone);
                if (FilesGone != null) FilesGone(gone.Count);
            }));
        }

        void OnRenamed(string oldPath, string newPath)
        {
            ui.BeginInvoke(new Action(() =>
            {
                var it = store.Items.FirstOrDefault(x => x.Path != null && string.Equals(x.Path, oldPath, StringComparison.OrdinalIgnoreCase));
                if (it == null) { OnFile(newPath); return; }   // e.g. Snipping Tool writing a temp file, then renaming it
                lock (seenPaths) seenPaths.Add(newPath);
                string ext = (Path.GetExtension(newPath) ?? "").ToLowerInvariant();
                if (!Exts.Contains(ext)) { OnGone(oldPath); return; }
                it.Path = newPath;
                store.NotifyUpdated(it);
            }));
        }

        /// <summary>Take off the rack anything whose file has disappeared (checked whenever the rack opens).</summary>
        public void PruneMissing()
        {
            var gone = store.Items.Where(it => it.Path != null && !File.Exists(it.Path)).ToList();
            if (gone.Count == 0) return;
            store.Drop(gone);
            if (FilesGone != null) FilesGone(gone.Count);
        }

        public void Stop()
        {
            if (fsw != null) fsw.Dispose();
            if (ownWatch != null) ownWatch.Dispose();
            // write anything still only in memory
            foreach (var it in store.Items.ToList()) EnsureSaved(it);
        }

        /// <summary>Call right after ClawdShelf itself put something on the clipboard.</summary>
        public void IgnoreCurrentClipboard() { ignoreSeq = Native.GetClipboardSequenceNumber(); lastSeq = ignoreSeq; }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == Native.WM_CLIPBOARDUPDATE && settings.CatchClipboard)
            {
                clipDebounce.Stop();
                clipDebounce.Start();
            }
            return IntPtr.Zero;
        }

        // ---------------- clipboard road ----------------

        void ReadClipboard()
        {
            uint seq = Native.GetClipboardSequenceNumber();
            if (seq == lastSeq || seq == ignoreSeq) return;
            lastSeq = seq;

            bool hasImage = Native.IsClipboardFormatAvailable(Native.CF_DIB) || Native.IsClipboardFormatAvailable(Native.CF_BITMAP)
                         || Native.IsClipboardFormatAvailable(Native.CF_DIBV5) || Native.IsClipboardFormatAvailable(cfPng);
            if (!hasImage) return;
            // Office / Excel / browsers / Explorer also put images on the clipboard - those are not screenshots.
            if (Native.IsClipboardFormatAvailable(Native.CF_UNICODETEXT) || Native.IsClipboardFormatAvailable(Native.CF_TEXT)
                || Native.IsClipboardFormatAvailable(Native.CF_HDROP) || Native.IsClipboardFormatAvailable(cfSelf)
                || Native.IsClipboardFormatAvailable(cfChromeUrl) || Native.IsClipboardFormatAvailable(cfMozCtx))
                return;

            Bitmap bmp = GrabClipboardBitmap();
            if (bmp == null) return;
            var when = DateTime.Now;

            Task.Factory.StartNew(() =>
            {
                try
                {
                    int w = bmp.Width, h = bmp.Height;
                    var fp = Img.Fingerprint(bmp);
                    byte[] png;
                    using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
                    bmp.Dispose();
                    var thumb = Img.Thumb(png);
                    ui.BeginInvoke(new Action(() => AcceptClip(png, w, h, fp, thumb, when)));
                }
                catch { }
            });
        }

        static Bitmap GrabClipboardBitmap()
        {
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    var data = WinClip.GetDataObject();
                    if (data == null) return null;
                    if (data.GetDataPresent("PNG"))
                    {
                        var ms = data.GetData("PNG") as MemoryStream;
                        if (ms != null)
                            using (var b = new Bitmap(ms)) return new Bitmap(b);
                    }
                    var img = WinClip.GetImage();
                    if (img == null) return null;
                    var copy = new Bitmap(img);
                    img.Dispose();
                    return copy;
                }
                catch { Thread.Sleep(60); }
            }
            return null;
        }

        void AcceptClip(byte[] png, int w, int h, byte[] fp, System.Windows.Media.Imaging.BitmapSource thumb, DateTime when)
        {
            // the screenshot file already arrived -> same shot, nothing new
            var twin = FindTwin(false, w, h, fp, when);
            if (twin != null) { twin.HasClip = true; return; }
            // identical to something that just came in (some tools fire twice)
            if (store.Items.Any(it => Math.Abs((it.Time - when).TotalSeconds) < 4 && Img.Same(it, w, h, fp))) return;

            var item = new ShotItem
            {
                Source = ShotSource.Clipboard, Time = when, HasClip = true,
                PendingPng = png, W = w, H = h, Print = fp, Thumb = thumb,
            };
            store.Add(item);

            // give the Screenshots folder a moment to deliver the same shot before saving our own copy
            var t = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(SaveDelayMs) };
            t.Tick += delegate { t.Stop(); EnsureSaved(item); };
            t.Start();
        }

        /// <summary>Make sure the item has a real file on disk (needed before dragging it out).</summary>
        public void EnsureSaved(ShotItem it)
        {
            if (it.Path != null || it.PendingPng == null) return;
            try
            {
                string dir = Settings.OwnDir;
                Directory.CreateDirectory(dir);
                string baseName = Loc.T("剪贴板截图 ", "Clipboard shot ") + it.Time.ToString("yyyy-MM-dd HHmmss");
                string path = Path.Combine(dir, baseName + ".png");
                for (int n = 2; File.Exists(path); n++) path = Path.Combine(dir, baseName + " (" + n + ").png");
                File.WriteAllBytes(path, it.PendingPng);
                lock (seenPaths) seenPaths.Add(path);
                it.Path = path;
                it.OwnCopy = true;
                it.PendingPng = null;
                if (store.Items.Contains(it)) store.NotifyUpdated(it);
            }
            catch { }
        }

        // ---------------- Screenshots folder road ----------------

        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp" };

        void OnFile(string path)
        {
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (!Exts.Contains(ext)) return;
            if (Path.GetFileName(path).StartsWith("~")) return;
            lock (seenPaths) { if (!seenPaths.Add(path)) return; }
            var when = DateTime.Now;

            Task.Factory.StartNew(() =>
            {
                var bytes = Img.ReadWhenReady(path, 8000);
                if (bytes == null) { lock (seenPaths) seenPaths.Remove(path); return; }
                try
                {
                    int w, h; byte[] fp;
                    using (var ms = new MemoryStream(bytes))
                    using (var bmp = new Bitmap(ms))
                    using (var b32 = new Bitmap(bmp))
                    {
                        w = b32.Width; h = b32.Height;
                        fp = Img.Fingerprint(b32);
                    }
                    var thumb = Img.Thumb(bytes);
                    ui.BeginInvoke(new Action(() => AcceptFile(path, w, h, fp, thumb, when)));
                }
                catch { }
            });
        }

        void AcceptFile(string path, int w, int h, byte[] fp, System.Windows.Media.Imaging.BitmapSource thumb, DateTime when)
        {
            if (store.Items.Any(it => string.Equals(it.Path, path, StringComparison.OrdinalIgnoreCase))) return;

            var twin = FindTwin(true, w, h, fp, when);
            if (twin != null)
            {
                // same shot already caught from the clipboard: point at the original instead of keeping a copy
                string own = twin.OwnCopy ? twin.Path : null;
                twin.Path = path; twin.HasFile = true; twin.OwnCopy = false; twin.PendingPng = null;
                twin.Source = ShotSource.Screenshot;
                if (own != null) { try { File.Delete(own); } catch { } }
                store.NotifyUpdated(twin);
                return;
            }

            store.Add(new ShotItem
            {
                Source = ShotSource.Screenshot, Time = when, HasFile = true,
                Path = path, W = w, H = h, Print = fp, Thumb = thumb,
            });
        }

        /// <summary>
        /// The same shot that came in a moment ago by the other road (clipboard vs Screenshots folder).
        /// fileArrived: the file is the newcomer, so look for a clipboard-only item, and the other way round.
        /// </summary>
        ShotItem FindTwin(bool fileArrived, int w, int h, byte[] fp, DateTime when)
        {
            return store.Items.FirstOrDefault(it => (fileArrived ? it.HasClip && !it.HasFile : it.HasFile && !it.HasClip)
                && Math.Abs((it.Time - when).TotalSeconds) < TwinWindowSec && Img.Same(it, w, h, fp));
        }

        /// <summary>Thumbnails for items restored from last session.</summary>
        public void LoadThumbs(Action<ShotItem> done)
        {
            var items = store.Items.ToList();
            Task.Factory.StartNew(() =>
            {
                foreach (var it in items)
                {
                    var bytes = Img.LoadBytes(it.Path);
                    if (bytes == null) continue;
                    var th = Img.Thumb(bytes);
                    var item = it;
                    ui.BeginInvoke(new Action(() => { item.Thumb = th; done(item); }));
                }
            });
        }
    }
}
