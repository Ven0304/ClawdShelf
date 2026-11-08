using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

[assembly: System.Reflection.AssemblyTitle("ClawdShelf")]
[assembly: System.Reflection.AssemblyProduct("ClawdShelf")]
[assembly: System.Reflection.AssemblyDescription("Clawd keeps your recent screenshots on a little shelf.")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace ClawdShelf
{
    static class Program
    {
        const string OpenSignal = "ClawdShelf.ToggleShelf";

        [STAThread]
        static void Main(string[] args)
        {
            bool open = args.Any(a => a.Equals("--open", StringComparison.OrdinalIgnoreCase));
            bool created;
            var mutex = new Mutex(true, "ClawdShelf.SingleInstance", out created);
            if (!created)
            {
                // Clawd is already on the desktop; "ClawdShelf.exe --open" pops its shelf open / closed
                if (open) try { EventWaitHandle.OpenExisting(OpenSignal).Set(); } catch { }
                return;
            }

            // Even one GPU-rendered window makes the graphics driver pin 150+ MB in this process.
            // Everything here is small and flat, so the software rasteriser keeps Clawd light (~70 MB).
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += (s, e) => { Log(e.Exception); e.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log(e.ExceptionObject as Exception);

            var settings = Settings.Load();
            Loc.Apply(settings.Language);
            var store = new ShelfStore(settings.Capacity);
            store.Items.AddRange(Settings.LoadShelf().Take(settings.Capacity));

            var catcher = new Catcher(store, settings, app.Dispatcher);
            var rack = new RackWindow(store, settings, catcher);   // created first: its event handlers run before Clawd's
            var clawd = new ClawdWindow(settings, store);
            clawd.Catcher = catcher;
            clawd.Rack = rack;
            rack.Clawd = clawd;
            catcher.LoadThumbs(it => rack.RefreshThumb(it));
            catcher.FilesGone += n => clawd.Say(n > 1
                ? Loc.T("诶？有 " + n + " 张图的原文件被删了，我取下来了", "Huh? " + n + " files were deleted, took them down")
                : Loc.T("诶？有张图的原文件被删了，我取下来了", "Huh? That file was deleted, took it down"), 2.0);

            Action save = () => { catcher.Stop(); settings.Save(); store.Save(); };
            app.Exit += (s, e) => save();
            app.SessionEnding += (s, e) => save();

            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSignal);
            var listener = new Thread(() =>
            {
                while (signal.WaitOne())
                    app.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (rack.IsOpen) rack.CloseRack(false); else rack.Open();
                    }));
            }) { IsBackground = true };
            listener.Start();

            clawd.Show();
            if (open) clawd.Dispatcher.BeginInvoke(new Action(() => rack.Open()), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            app.Run();
            GC.KeepAlive(mutex);
        }

        static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                Directory.CreateDirectory(Settings.DataDir);
                File.AppendAllText(Path.Combine(Settings.DataDir, "error.log"), DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine + Environment.NewLine);
            }
            catch { }
        }
    }
}
