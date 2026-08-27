using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Arp
{
    internal sealed class UpdateInfo
    {
        public string Version;
        public string DownloadUrl;
        public string ReleaseNotes;
        public string Sha256;
        public long Size;
    }

    /// <summary>
    /// Self-updater for the single-executable build.
    ///
    /// Because the whole application is one file, updating is a file swap
    /// rather than a directory merge. A running executable cannot be deleted on
    /// Windows, but it can be renamed, so the sequence is: download, verify the
    /// hash, rename the running file aside, move the new one into its place,
    /// relaunch, and have the new process delete the renamed old one once this
    /// one has exited. Nothing is left behind, and a sweep on every startup
    /// clears any leftover from an interrupted attempt.
    /// </summary>
    internal static class Updater
    {
        public const string CurrentVersion = "v2.0.2";
        private const string Repo = "ce2004/arp-native";

        /// <summary>Suffix given to the outgoing executable while it is still running.</summary>
        private const string OldSuffix = ".old-update";

        public static string ArchSuffix => RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.Arm64 => "win-arm64",
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            _ => "win-x64",
        };

        private static string ExePath => Environment.ProcessPath ??
                                         Path.Combine(AppContext.BaseDirectory, "ArpRecorder.exe");

        // ---- version comparison ----

        /// <summary>
        /// Compares "v1.2.3" style tags numerically, so v1.10.0 correctly beats
        /// v1.9.0 where a string compare would not.
        /// </summary>
        internal static int CompareVersions(string a, string b)
        {
            int[] pa = ParseVersion(a), pb = ParseVersion(b);
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int x = i < pa.Length ? pa[i] : 0;
                int y = i < pb.Length ? pb[i] : 0;
                if (x != y) return x < y ? -1 : 1;
            }
            return 0;
        }

        internal static int[] ParseVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return new[] { 0 };
            v = v.Trim();
            if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v.Substring(1);

            var parts = v.Split('.');
            var outp = new List<int>(parts.Length);
            foreach (string p in parts)
            {
                int i = 0;
                while (i < p.Length && char.IsAsciiDigit(p[i])) i++;
                outp.Add(i > 0 && int.TryParse(p.AsSpan(0, i), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int n) ? n : 0);
            }
            return outp.Count == 0 ? new[] { 0 } : outp.ToArray();
        }

        // ---- feed ----

        /// <summary>
        /// Returns the newest release if it is newer than this build, or null.
        /// Picks the asset whose name carries this process's architecture.
        /// </summary>
        internal static UpdateInfo Check()
        {
            string json = Http.GetString("https://api.github.com/repos/" + Repo + "/releases/latest");
            var release = JsonObject.Parse(json);
            string tag = release.GetString("tag_name", "");
            if (string.IsNullOrEmpty(tag)) return null;
            if (CompareVersions(tag, CurrentVersion) <= 0) return null;

            string wanted = ArchSuffix;
            string url = null, shaUrl = null;
            long size = 0;

            if (release.GetRaw("assets") is List<object> assets)
            {
                foreach (object a in assets)
                {
                    if (a is not JsonObject asset) continue;
                    string name = asset.GetString("name", "");
                    string dl = asset.GetString("browser_download_url", "");
                    if (string.IsNullOrEmpty(dl)) continue;

                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        url = dl;
                        size = asset.GetLong("size", 0);
                    }
                    else if (name.StartsWith("sha256", StringComparison.OrdinalIgnoreCase) ||
                             name.Equals("sha256sums.txt", StringComparison.OrdinalIgnoreCase))
                    {
                        shaUrl = dl;
                    }
                }
            }

            if (url == null)
            {
                Log.Warn("Release " + tag + " has no executable for " + wanted + ".");
                return null;
            }

            string sha = null;
            if (shaUrl != null)
            {
                try
                {
                    sha = FindHashFor(Http.GetString(shaUrl), wanted);
                }
                catch (Exception e)
                {
                    Log.Warn("Could not read the checksum file: " + e.Message);
                }
            }

            return new UpdateInfo
            {
                Version = tag,
                DownloadUrl = url,
                ReleaseNotes = CleanNotes(release.GetString("body", "")),
                Sha256 = sha,
                Size = size,
            };
        }

        /// <summary>Finds the hash on the line naming this architecture.</summary>
        internal static string FindHashFor(string sums, string archSuffix)
        {
            if (string.IsNullOrEmpty(sums)) return null;
            foreach (string raw in sums.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length < 64) continue;
                if (!line.Contains(archSuffix, StringComparison.OrdinalIgnoreCase)) continue;

                foreach (string token in line.Split(' ', '\t'))
                {
                    string t = token.Trim();
                    if (t.Length == 64 && IsHex(t)) return t.ToUpperInvariant();
                }
            }
            return null;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!char.IsAsciiHexDigit(c)) return false;
            return true;
        }

        /// <summary>Strips markdown that a screen reader would read aloud as symbols.</summary>
        internal static string CleanNotes(string body)
        {
            if (string.IsNullOrEmpty(body)) return string.Empty;
            var sb = new StringBuilder(body.Length);
            foreach (string raw in body.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Replace("#", "").Replace("*", "").Replace("`", "").Trim();
                if (line.StartsWith("-", StringComparison.Ordinal)) line = line.Substring(1).Trim();
                if (line.Length > 0) sb.Append(line).Append('\n');
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ---- entry points ----

        public static void CheckOnStartup(IntPtr owner)
        {
            try
            {
                var info = Check();
                if (info == null) return;
                Offer(owner, info);
            }
            catch (Exception e)
            {
                // Never block startup because the network is unavailable.
                Log.Warn("Startup update check failed: " + e.Message);
            }
        }

        public static void CheckNow(IntPtr owner)
        {
            try
            {
                Log.Info("Manual update check triggered");
                var info = Check();
                if (info == null)
                {
                    Win32.MessageBoxW(owner,
                        "You are already on the latest version.\r\n\r\nInstalled version: " + CurrentVersion +
                        "\r\nBuild: " + ArchSuffix,
                        "Up to Date", Win32.MB_OK | Win32.MB_ICONINFORMATION);
                    return;
                }
                Offer(owner, info);
            }
            catch (Exception e)
            {
                Log.Error("Failed to check for updates: " + e.Message);
                Win32.MessageBoxW(owner, "Failed to check for updates.\r\n\r\n" + e.Message,
                    "Error", Win32.MB_OK | Win32.MB_ICONERROR);
            }
        }

        /// <summary>
        /// Reports to a dialog when there is a window, and to the console and
        /// log when running headless, so the same code path serves both the UI
        /// and the --update switch.
        /// </summary>
        private static void Report(IntPtr owner, string text, string caption, uint icon)
        {
            if (owner != IntPtr.Zero)
            {
                Win32.MessageBoxW(owner, text, caption, Win32.MB_OK | icon);
                return;
            }
            Console.WriteLine(caption + ": " + text.Replace("\r\n", " "));
            Log.Info(caption + ": " + text.Replace("\r\n", " "));
        }

        /// <summary>Headless check used by the --checkupdate switch.</summary>
        public static int CheckHeadless()
        {
            Console.WriteLine("Installed  : " + CurrentVersion + " (" + ArchSuffix + ")");
            Console.WriteLine("Feed       : https://github.com/" + Repo + "/releases/latest");
            try
            {
                var info = Check();
                if (info == null)
                {
                    Console.WriteLine("Result     : up to date");
                    return 0;
                }
                Console.WriteLine("Result     : " + info.Version + " available");
                Console.WriteLine("Download   : " + info.DownloadUrl);
                Console.WriteLine("Size       : " + info.Size.ToString("N0", CultureInfo.InvariantCulture) + " bytes");
                Console.WriteLine("SHA-256    : " + (info.Sha256 ?? "NOT PUBLISHED - would refuse to install"));
                if (!string.IsNullOrEmpty(info.ReleaseNotes))
                {
                    Console.WriteLine("Notes      :");
                    foreach (string line in info.ReleaseNotes.Split('\n'))
                        Console.WriteLine("             " + line);
                }
                return 10;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Update check failed: " + e.Message);
                return 1;
            }
        }

        /// <summary>Headless check-and-install used by the --update switch.</summary>
        public static int UpdateHeadless()
        {
            try
            {
                var info = Check();
                if (info == null)
                {
                    Console.WriteLine("Already up to date (" + CurrentVersion + ").");
                    return 0;
                }
                Console.WriteLine("Installing " + info.Version + " over " + CurrentVersion + "...");
                // Relaunch headless too: clean up, report the new version, exit.
                // A GUI appearing out of an unattended update would be wrong.
                Apply(IntPtr.Zero, info, "--version");
                // Apply exits the process on success, so reaching here failed.
                return 1;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Update failed: " + e.Message);
                return 1;
            }
        }

        private static void Offer(IntPtr owner, UpdateInfo info)
        {
            var dlg = new UpdateDialog(CurrentVersion, info.Version, info.ReleaseNotes);
            if ((long)dlg.ShowModal(owner) != 1) return;
            Apply(owner, info);
        }

        // ---- install ----

        private static void Apply(IntPtr owner, UpdateInfo info, string relaunchExtraArgs = "")
        {
            string exe = ExePath;
            string dir = Path.GetDirectoryName(exe);
            string staged = exe + ".new";
            string old = exe + OldSuffix;

            try
            {
                Speech.SpeakRaw("Downloading update. Please wait.");
                Log.Info("Downloading " + info.DownloadUrl);

                byte[] payload = Http.Get(info.DownloadUrl, 120);

                if (payload.Length < 1024)
                    throw new Exception("The downloaded file is too small to be the application.");

                // Refuse anything whose hash does not match the published one.
                // An update that cannot be verified is not installed.
                if (!string.IsNullOrEmpty(info.Sha256))
                {
                    string actual = Convert.ToHexString(SHA256.HashData(payload));
                    if (!string.Equals(actual, info.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Error("Checksum mismatch: expected " + info.Sha256 + ", got " + actual);
                        Speech.SpeakRaw("Update failed. The download did not match its checksum.");
                        Report(owner,
                            "The downloaded update did not match its published checksum, so it was not " +
                            "installed. Your current version is untouched.",
                            "Update Failed", Win32.MB_ICONERROR);
                        return;
                    }
                    Log.Info("Checksum verified.");
                }
                else
                {
                    Log.Error("No checksum published for this release; refusing to install.");
                    Speech.SpeakRaw("Update aborted. No checksum was published.");
                    Report(owner,
                        "This release did not publish a checksum, so the download could not be verified " +
                        "and was not installed.",
                        "Update Failed", Win32.MB_ICONERROR);
                    return;
                }

                CleanupLeftovers(dir);
                File.WriteAllBytes(staged, payload);

                // A running executable cannot be deleted, but it can be renamed.
                if (File.Exists(old)) TryDelete(old);
                File.Move(exe, old);

                try
                {
                    File.Move(staged, exe);
                }
                catch
                {
                    // Put the original back rather than leaving no executable.
                    File.Move(old, exe);
                    throw;
                }

                Log.Info("Update staged; relaunching " + info.Version);
                Speech.SpeakRaw("Update installed. Restarting.");

                // The new process removes the renamed old file once this one
                // has exited, so nothing is left in the folder.
                Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = ("--finish-update " + Environment.ProcessId + " " + relaunchExtraArgs).Trim(),
                    UseShellExecute = false,
                    WorkingDirectory = dir,
                });

                Environment.Exit(0);
            }
            catch (Exception e)
            {
                Log.Error("Update failed: " + e.Message, e);
                TryDelete(staged);
                Speech.SpeakRaw("Update failed.");
                Report(owner,
                    "The update could not be installed. Your current version is untouched.\r\n\r\n" + e.Message,
                    "Update Failed", Win32.MB_ICONERROR);
            }
        }

        /// <summary>
        /// Runs in the freshly installed process: waits for the previous one to
        /// exit, then deletes the executable it left behind.
        /// </summary>
        public static void FinishUpdate(int previousProcessId)
        {
            try
            {
                if (previousProcessId > 0)
                {
                    try
                    {
                        using var p = Process.GetProcessById(previousProcessId);
                        p.WaitForExit(15000);
                    }
                    catch (ArgumentException)
                    {
                        // Already gone, which is the normal case.
                    }
                }

                string dir = Path.GetDirectoryName(ExePath);
                // The handle can linger briefly after exit, so retry.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (CleanupLeftovers(dir) == 0) break;
                    Thread.Sleep(250);
                }
                Log.Info("Update complete; previous executable removed.");
            }
            catch (Exception e)
            {
                Log.Warn("Post-update cleanup had trouble: " + e.Message);
            }
        }

        /// <summary>
        /// Deletes anything an update left behind. Returns how many remain, so
        /// callers can retry. Also run at every startup, so an interrupted
        /// update never leaves a stray executable in the folder.
        /// </summary>
        public static int CleanupLeftovers(string dir)
        {
            int remaining = 0;
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
                foreach (string path in Directory.GetFiles(dir, "*" + OldSuffix))
                    if (!TryDelete(path)) remaining++;
                foreach (string path in Directory.GetFiles(dir, "*.exe.new"))
                    if (!TryDelete(path)) remaining++;
            }
            catch (Exception e)
            {
                Log.Warn("Could not sweep update leftovers: " + e.Message);
            }
            return remaining;
        }

        public static void CleanupOnStartup() => CleanupLeftovers(Path.GetDirectoryName(ExePath));

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
