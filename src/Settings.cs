using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ClawdShelf
{
    public enum ShotSource { Screenshot, Clipboard }

    /// <summary>One screenshot sitting on the shelf. Only references a file on disk.</summary>
    public class ShotItem
    {
        public string Path;          // null while a clipboard shot is still waiting to be saved
        public ShotSource Source;
        public DateTime Time;
        public bool HasFile;         // arrived through the Screenshots folder
        public bool HasClip;         // arrived through the clipboard
        public bool OwnCopy;         // Path is a file ClawdShelf saved itself
        public byte[] PendingPng;    // clipboard pixels not yet written to disk
        public int W, H;
        public byte[] Print;         // tiny fingerprint used to spot the same shot coming in twice
        public BitmapSource Thumb;
    }

    public class Settings
    {
        public int Capacity = 30;
        /// <summary>Where Clawd's feet stand (DIPs). NaN = top centre of the main screen.</summary>
        public double AnchorX = double.NaN, AnchorY = double.NaN;
        /// <summary>Size of one art pixel in DIPs.</summary>
        public double Scale = DefaultScale;
        public Hat Hat = Hat.Straw;
        public string Language = "auto";
        public int SleepAfterSec = 180;
        public bool HideFromCapture = true;
        public bool AutoHideFullscreen = true;
        public bool CatchClipboard = true;

        public const int MinCapacity = 5, MaxCapacity = 100;
        public const double DefaultScale = 2.5;
        public static readonly double[] Scales = { 1.5, 2, 2.5, 3, 3.5, 4, 5, 6 };

        public static string DataDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClawdShelf"); }
        }

        static string SettingsFile { get { return System.IO.Path.Combine(DataDir, "settings.ini"); } }
        static string ShelfFile { get { return System.IO.Path.Combine(DataDir, "shelf.txt"); } }

        public static string ScreenshotsDir
        {
            get
            {
                string s = Native.KnownFolder(new Guid("b7bede81-df94-4682-a7d8-57a52620b86f"));
                if (string.IsNullOrEmpty(s))
                    s = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
                return s;
            }
        }

        /// <summary>
        /// Where clipboard-only shots (WeChat / QQ / Snipaste ...) get saved. It lives in ClawdShelf's own app data,
        /// not in Pictures, so it never looks like clutter to clean up - the rack depends on these files.
        /// </summary>
        public static string OwnDir
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClawdShelf", "clips"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(SettingsFile)) return s;
                foreach (var raw in File.ReadAllLines(SettingsFile))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = raw.Substring(eq + 1).Trim();
                    var ci = CultureInfo.InvariantCulture;
                    switch (k)
                    {
                        case "capacity": int.TryParse(v, out s.Capacity); break;
                        case "anchorx": double.TryParse(v, NumberStyles.Float, ci, out s.AnchorX); break;
                        case "anchory": double.TryParse(v, NumberStyles.Float, ci, out s.AnchorY); break;
                        case "scale": double.TryParse(v, NumberStyles.Float, ci, out s.Scale); break;
                        case "hat": Enum.TryParse(v, true, out s.Hat); break;
                        case "language": s.Language = v.ToLowerInvariant(); break;
                        case "sleepaftersec": int.TryParse(v, out s.SleepAfterSec); break;
                        case "hidefromcapture": s.HideFromCapture = v == "1" || v.ToLower() == "true"; break;
                        case "autohidefullscreen": s.AutoHideFullscreen = v == "1" || v.ToLower() == "true"; break;
                        case "catchclipboard": s.CatchClipboard = v == "1" || v.ToLower() == "true"; break;
                    }
                }
            }
            catch { }
            s.Capacity = Math.Max(MinCapacity, Math.Min(MaxCapacity, s.Capacity));
            s.Scale = Math.Max(1, Math.Min(10, s.Scale));
            s.SleepAfterSec = Math.Max(20, s.SleepAfterSec);
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var ci = CultureInfo.InvariantCulture;
                File.WriteAllLines(SettingsFile, new[]
                {
                    "capacity=" + Capacity,
                    "anchorX=" + AnchorX.ToString("0.#", ci),
                    "anchorY=" + AnchorY.ToString("0.#", ci),
                    "scale=" + Scale.ToString("0.##", ci),
                    "hat=" + Hat,
                    "language=" + Language,
                    "sleepAfterSec=" + SleepAfterSec,
                    "hideFromCapture=" + (HideFromCapture ? 1 : 0),
                    "autoHideFullscreen=" + (AutoHideFullscreen ? 1 : 0),
                    "catchClipboard=" + (CatchClipboard ? 1 : 0),
                });
            }
            catch { }
        }

        public static List<ShotItem> LoadShelf()
        {
            var list = new List<ShotItem>();
            try
            {
                if (!File.Exists(ShelfFile)) return list;
                foreach (var line in File.ReadAllLines(ShelfFile))
                {
                    var p = line.Split(new[] { '|' }, 4);
                    if (p.Length < 4 || !File.Exists(p[3])) continue;
                    long ticks;
                    if (!long.TryParse(p[0], out ticks)) continue;
                    list.Add(new ShotItem
                    {
                        Time = new DateTime(ticks),
                        Source = p[1] == "c" ? ShotSource.Clipboard : ShotSource.Screenshot,
                        OwnCopy = p[2] == "1",
                        Path = p[3],
                        HasFile = p[1] != "c",
                        HasClip = p[1] == "c",
                    });
                }
            }
            catch { }
            return list;
        }

        public static void SaveShelf(IEnumerable<ShotItem> items)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var lines = new List<string>();
                foreach (var it in items)
                {
                    if (it.Path == null) continue;
                    lines.Add(it.Time.Ticks + "|" + (it.Source == ShotSource.Clipboard ? "c" : "s") + "|" + (it.OwnCopy ? "1" : "0") + "|" + it.Path);
                }
                File.WriteAllLines(ShelfFile, lines);
            }
            catch { }
        }

        // ---- start with Windows (HKCU Run key) ----
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool AutoStart
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                        return k != null && k.GetValue("ClawdShelf") != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (value) k.SetValue("ClawdShelf", "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\"");
                        else k.DeleteValue("ClawdShelf", false);
                    }
                }
                catch { }
            }
        }
    }
}
