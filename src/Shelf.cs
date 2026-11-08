using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Media.Imaging;

namespace ClawdShelf
{
    /// <summary>The list of shots on the shelf, newest first. Never deletes original files.</summary>
    public class ShelfStore
    {
        public readonly List<ShotItem> Items = new List<ShotItem>();
        public int Capacity;

        public event Action<ShotItem> Added;
        public event Action<ShotItem> Updated;
        /// <summary>items, eaten: true when the user removed them (Clawd eats), false when they just fell off the end.</summary>
        public event Action<List<ShotItem>, bool> Removed;

        public ShelfStore(int capacity) { Capacity = capacity; }

        public int Count { get { return Items.Count; } }
        public double Fullness { get { return Capacity <= 0 ? 0 : Math.Min(1.0, Items.Count / (double)Capacity); } }

        public void Add(ShotItem it)
        {
            Items.Insert(0, it);
            if (Added != null) Added(it);
            if (Items.Count > Capacity)
            {
                var drop = Items.Skip(Capacity).ToList();
                Items.RemoveRange(Capacity, Items.Count - Capacity);
                if (Removed != null) Removed(drop, false);
            }
            Save();
        }

        public void NotifyUpdated(ShotItem it)
        {
            if (Updated != null) Updated(it);
            Save();
        }

        /// <summary>The user took these down: Clawd eats them (the files stay where they are).</summary>
        public void Remove(IEnumerable<ShotItem> items) { TakeOff(items, true); }

        public void Clear() { Remove(Items.ToList()); }

        /// <summary>The file is gone from disk (deleted or moved away): quietly take it off the rack.</summary>
        public void Drop(IEnumerable<ShotItem> items) { TakeOff(items, false); }

        void TakeOff(IEnumerable<ShotItem> items, bool eaten)
        {
            var list = items.Where(Items.Contains).ToList();
            if (list.Count == 0) return;
            foreach (var it in list) Items.Remove(it);
            if (Removed != null) Removed(list, eaten);
            Save();
        }

        /// <summary>Drop whatever no longer fits after the capacity shrank (Clawd eats them).</summary>
        public void Trim()
        {
            if (Items.Count > Capacity) Remove(Items.Skip(Capacity).ToList());
        }

        public void Save() { Settings.SaveShelf(Items); }
    }

    internal static class Img
    {
        public const int ThumbWidth = 320;

        /// <summary>Hash the full 32-bit pixels without copying the whole bitmap.</summary>
        public static byte[] Fingerprint(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new byte[w * 4];
                using (var hash = SHA256.Create())
                {
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                        hash.TransformBlock(row, 0, row.Length, row, 0);
                    }
                    hash.TransformFinalBlock(row, 0, 0);
                    return hash.Hash;
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        public static bool Same(ShotItem a, int w, int h, byte[] fp)
        {
            return a.W == w && a.H == h && a.Print != null && fp != null && a.Print.SequenceEqual(fp);
        }

        public static BitmapSource Thumb(byte[] bytes)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bi.DecodePixelWidth = ThumbWidth;
                bi.StreamSource = new MemoryStream(bytes);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        /// <summary>Read a file that may still be being written by another program.</summary>
        public static byte[] ReadWhenReady(string path, int timeoutMs)
        {
            var start = Environment.TickCount;
            long lastLen = -1;
            while (Environment.TickCount - start < timeoutMs)
            {
                try
                {
                    var fi = new FileInfo(path);
                    if (!fi.Exists) return null;
                    if (fi.Length > 0 && fi.Length == lastLen)
                    {
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        {
                            var buf = new byte[fs.Length];
                            int n = 0;
                            while (n < buf.Length) { int r = fs.Read(buf, n, buf.Length - n); if (r <= 0) break; n += r; }
                            if (n == buf.Length) return buf;
                        }
                    }
                    lastLen = fi.Length;
                }
                catch { }
                Thread.Sleep(120);
            }
            return null;
        }

        public static byte[] LoadBytes(string path)
        {
            try { return File.ReadAllBytes(path); } catch { return null; }
        }
    }
}
