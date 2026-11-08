using System;
using System.Runtime.InteropServices;
using System.Text;

namespace ClawdShelf
{
    internal static class Native
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
        public const uint CF_TEXT = 1, CF_BITMAP = 2, CF_DIB = 8, CF_UNICODETEXT = 13, CF_HDROP = 15, CF_DIBV5 = 17;
        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsClipboardFormatAvailable(uint format);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterClipboardFormat(string name);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern short GetKeyState(int vk);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

        /// <summary>System DPI scale (1.0 = 96 dpi). The app is system-DPI aware, so this is fixed for its lifetime.</summary>
        public static readonly double Dpi = ReadDpi();

        static double ReadDpi()
        {
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0;
        }

        /// <summary>Keep a window out of screenshots and screen recordings (Windows 10 2004+).</summary>
        public static void ExcludeFromCapture(IntPtr hwnd, bool on)
        {
            if (hwnd != IntPtr.Zero) SetWindowDisplayAffinity(hwnd, on ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
        }

        /// <summary>Put a topmost window at the very top of the topmost band without activating it.</summary>
        public static void BringToTop(IntPtr hwnd)
        {
            if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
        }

        // The shelf never takes keyboard focus, so read modifier keys straight from Windows.
        public static bool CtrlDown { get { return (GetKeyState(0x11) & 0x8000) != 0; } }
        public static bool ShiftDown { get { return (GetKeyState(0x10) & 0x8000) != 0; } }

        [DllImport("shell32.dll")]
        public static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr token, out IntPtr path);

        public static void AddExStyle(IntPtr hwnd, int style)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | style);
        }

        public static void RemoveExStyle(IntPtr hwnd, int style)
        {
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) & ~style);
        }

        public static string KnownFolder(Guid id)
        {
            IntPtr p;
            if (SHGetKnownFolderPath(id, 0, IntPtr.Zero, out p) == 0)
            {
                string s = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return s;
            }
            return null;
        }

        public static string ClassOf(IntPtr hwnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString();
        }

        /// <summary>Work area (physical pixels) of the monitor nearest to a point.</summary>
        public static RECT WorkAreaAt(int x, int y)
        {
            var pt = new POINT { X = x, Y = y };
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            GetMonitorInfo(MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST), ref mi);
            return mi.rcWork;
        }
    }
}
