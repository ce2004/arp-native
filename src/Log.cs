using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Arp
{
    // Mirrors the Python build's RotatingFileHandler: same file name, same 5 MB
    // cap, same 5 backups, same "time - thread - level - message" line format,
    // so the "Copy Diagnostic Logs" output stays familiar.
    internal static class Log
    {
        private const long MaxBytes = 5 * 1024 * 1024;
        private const int BackupCount = 5;
        private static readonly object Gate = new();
        private static string _path;

        public static string FilePath
        {
            get
            {
                if (_path == null) _path = Path.Combine(Config.AppDataDir, "arp_diagnostic.log");
                return _path;
            }
        }

        public static void Info(string msg) => Write("INFO", msg);
        public static void Warn(string msg) => Write("WARNING", msg);
        public static void Error(string msg) => Write("ERROR", msg);

        public static void Error(string msg, Exception ex) =>
            Write("ERROR", msg + Environment.NewLine + ex);

        private static void Write(string level, string msg)
        {
            try
            {
                string thread = Thread.CurrentThread.Name;
                if (string.IsNullOrEmpty(thread)) thread = "Thread-" + Environment.CurrentManagedThreadId;

                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-dd HH:mm:ss,fff} - {1} - {2} - {3}{4}",
                    DateTime.Now, thread, level, msg, Environment.NewLine);

                lock (Gate)
                {
                    Rotate();
                    File.AppendAllText(FilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Logging must never take the recorder down.
            }
        }

        private static void Rotate()
        {
            try
            {
                var fi = new FileInfo(FilePath);
                if (!fi.Exists || fi.Length < MaxBytes) return;

                string oldest = FilePath + "." + BackupCount;
                if (File.Exists(oldest)) File.Delete(oldest);

                for (int i = BackupCount - 1; i >= 1; i--)
                {
                    string src = FilePath + "." + i;
                    if (File.Exists(src)) File.Move(src, FilePath + "." + (i + 1), true);
                }
                File.Move(FilePath, FilePath + ".1", true);
            }
            catch
            {
            }
        }

        public static string ReadAll()
        {
            lock (Gate)
            {
                try
                {
                    using var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var sr = new StreamReader(fs, Encoding.UTF8);
                    return sr.ReadToEnd();
                }
                catch (Exception e)
                {
                    return "Failed to read log: " + e.Message;
                }
            }
        }
    }
}
