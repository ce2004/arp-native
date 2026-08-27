using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Desktop notifications via a shell tray balloon, which Windows 10 and 11
    /// surface as a toast. This replaces plyer from the Python build; the icon
    /// is added for the life of the notification and then removed, matching how
    /// plyer behaves so no permanent tray icon appears.
    /// </summary>
    internal static unsafe class Notifier
    {
        private const int NIM_ADD = 0;
        private const int NIM_MODIFY = 1;
        private const int NIM_DELETE = 2;

        private const uint NIF_MESSAGE = 0x01;
        private const uint NIF_ICON = 0x02;
        private const uint NIF_TIP = 0x04;
        private const uint NIF_INFO = 0x10;

        private const uint NIIF_INFO = 0x01;
        private const uint NIIF_WARNING = 0x02;
        private const uint NIIF_ERROR = 0x03;

        private static readonly IntPtr IDI_APPLICATION = 32512;

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONDATAW
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            public fixed char szTip[128];
            public uint dwState;
            public uint dwStateMask;
            public fixed char szInfo[256];
            public uint uTimeoutOrVersion;
            public fixed char szInfoTitle[64];
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(int message, ref NOTIFYICONDATAW data);

        private static IntPtr _owner;
        private static int _nextId = 1;
        private static readonly object Gate = new();

        public static void Attach(IntPtr ownerWindow) => _owner = ownerWindow;

        public enum Level { Info, Warning, Error }

        public static void Notify(string title, string message, Level level = Level.Info)
        {
            if (_owner == IntPtr.Zero) return;

            uint id;
            lock (Gate) id = (uint)_nextId++;

            var data = new NOTIFYICONDATAW
            {
                cbSize = (uint)sizeof(NOTIFYICONDATAW),
                hWnd = _owner,
                uID = id,
                uFlags = NIF_ICON | NIF_TIP | NIF_MESSAGE,
                uCallbackMessage = (uint)(Win32.WM_APP + 100),
                hIcon = Win32.LoadIconW(IntPtr.Zero, IDI_APPLICATION),
            };
            Copy(data.szTip, 128, "Audio Recorder Pro");

            if (!Shell_NotifyIconW(NIM_ADD, ref data))
            {
                Log.Warn("Shell_NotifyIcon(NIM_ADD) failed for notification: " + title);
                return;
            }

            data.uFlags = NIF_INFO;
            Copy(data.szInfoTitle, 64, title ?? string.Empty);
            Copy(data.szInfo, 256, message ?? string.Empty);
            data.dwInfoFlags = level switch
            {
                Level.Warning => NIIF_WARNING,
                Level.Error => NIIF_ERROR,
                _ => NIIF_INFO,
            };
            Shell_NotifyIconW(NIM_MODIFY, ref data);

            // Windows keeps the toast alive once shown; the icon only needs to
            // survive long enough for the shell to pick it up.
            var owner = _owner;
            var t = new Thread(() =>
            {
                Thread.Sleep(10000);
                var del = new NOTIFYICONDATAW
                {
                    cbSize = (uint)sizeof(NOTIFYICONDATAW),
                    hWnd = owner,
                    uID = id,
                };
                try { Shell_NotifyIconW(NIM_DELETE, ref del); } catch { }
            })
            { IsBackground = true, Name = "NotifyReaper" };
            t.Start();
        }

        private static void Copy(char* dest, int capacity, string s)
        {
            int n = Math.Min(s.Length, capacity - 1);
            for (int i = 0; i < n; i++) dest[i] = s[i];
            dest[n] = '\0';
        }
    }
}
