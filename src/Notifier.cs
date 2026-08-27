using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Desktop notifications via a shell tray balloon, which Windows 10 and 11
    /// surface as a toast. Replaces plyer from the Python build.
    ///
    /// The shell imposes hard limits on this structure: 63 characters of title
    /// and 255 of body, and it silently drops whatever does not fit. On top of
    /// that the toast itself only renders two or three lines before ellipsizing.
    /// So text is flattened to a single line and cut on a word boundary here,
    /// and callers keep bodies to one short sentence; the full wording still
    /// goes to the dialog and to the screen reader, which have no such limits.
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

        // Capacities of the fixed-size buffers, minus room for the terminator.
        public const int MaxTitle = 63;
        public const int MaxBody = 255;

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

        /// <summary>
        /// The name notifications identify themselves by. Follows the window
        /// title from settings, so renaming the window to "ARP" renames what
        /// the notifications say too.
        /// </summary>
        public static string AppName { get; set; } = "Audio Recorder Pro";

        public static void Attach(IntPtr ownerWindow) => _owner = ownerWindow;

        public enum Level { Info, Warning, Error }

        public static void Notify(string title, string message, Level level = Level.Info)
        {
            if (_owner == IntPtr.Zero) return;

            string appName = Flatten(string.IsNullOrWhiteSpace(AppName) ? "Audio Recorder Pro" : AppName);
            string shortTitle = Fit(Flatten(title), MaxTitle, title);
            string shortBody = Fit(Flatten(message), MaxBody, message);

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
            Copy(data.szTip, 128, Fit(appName, 127, appName));

            if (!Shell_NotifyIconW(NIM_ADD, ref data))
            {
                Log.Warn("Shell_NotifyIcon(NIM_ADD) failed for notification: " + title);
                return;
            }

            data.uFlags = NIF_INFO;
            Copy(data.szInfoTitle, 64, shortTitle);
            Copy(data.szInfo, 256, shortBody);
            data.dwInfoFlags = level switch
            {
                Level.Warning => NIIF_WARNING,
                Level.Error => NIIF_ERROR,
                _ => NIIF_INFO,
            };

            if (!Shell_NotifyIconW(NIM_MODIFY, ref data))
                Log.Warn("Shell_NotifyIcon(NIM_MODIFY) failed for notification: " + title);

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

        /// <summary>
        /// Collapses newlines and runs of whitespace into single spaces. A
        /// balloon renders as one paragraph, so embedded line breaks turn into
        /// stray gaps and waste the character budget.
        /// </summary>
        internal static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length);
            bool lastWasSpace = false;
            foreach (char c in s)
            {
                bool isSpace = c == ' ' || c == '\t' || c == '\r' || c == '\n';
                if (isSpace)
                {
                    if (!lastWasSpace && sb.Length > 0) sb.Append(' ');
                    lastWasSpace = true;
                }
                else
                {
                    sb.Append(c);
                    lastWasSpace = false;
                }
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Trims to fit, breaking on a word boundary and marking the cut so a
        /// truncated notification does not read as a complete sentence.
        /// </summary>
        internal static string Fit(string s, int max, string original = null)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.Length <= max) return s;

            int cut = s.LastIndexOf(' ', Math.Min(max - 1, s.Length - 1));
            if (cut < max / 2) cut = max - 1; // no sensible break; hard cut
            string result = s.Substring(0, cut).TrimEnd(' ', ',', ';', ':', '.') + "…";

            Log.Warn("Notification text truncated to " + max + " characters. Full text: " +
                     Flatten(original ?? s));
            return result;
        }

        private static void Copy(char* dest, int capacity, string s)
        {
            int n = Math.Min(s.Length, capacity - 1);
            for (int i = 0; i < n; i++) dest[i] = s[i];
            dest[n] = '\0';
        }
    }
}
