using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Times each phase of startup and shutdown, so the cost of opening and
    /// closing the window is measured rather than guessed at.
    /// Run with: ArpRecorder.exe --timing
    /// </summary>
    internal static class TimingTest
    {
        private static readonly System.Text.StringBuilder Transcript = new();

        public static string ReportPath { get; } =
            Path.Combine(Path.GetTempPath(), "arp_timing_report.txt");

        private static void Say(string s)
        {
            Transcript.AppendLine(s);
            Console.WriteLine(s);
        }

        private static long Time(string label, Action work)
        {
            var sw = Stopwatch.StartNew();
            try { work(); }
            catch (Exception e) { Say(string.Format("  {0,-42} FAILED: {1}", label, e.Message)); return -1; }
            sw.Stop();
            Say(string.Format(CultureInfo.InvariantCulture, "  {0,-42} {1,7:N1} ms", label, sw.Elapsed.TotalMilliseconds));
            return sw.ElapsedMilliseconds;
        }

        public static int Run()
        {
            Say("STARTUP PHASES");
            long total = 0;

            total += Time("Updater.CleanupOnStartup", () => Updater.CleanupOnStartup());
            total += Time("Win32.EnableDpiAwareness + common controls", () =>
            {
                Win32.EnableDpiAwareness();
                Win32.InitCommonControls();
            });
            total += Time("Speech.Init (load + unpack NVDA client)", () => Speech.Init());
            total += Time("Config load", () => { var _ = new Config(); });

            long devices = 0;
            Time("WASAPI device enumeration", () =>
            {
                Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_APARTMENTTHREADED);
                var d = Wasapi.EnumerateDevices();
                devices = d.Count;
            });
            Say("      (" + devices + " endpoints)");

            total += Time("Sound library, render every cue", () => SoundLibrary.Preload());
            Say("      (" + SoundLibrary.Names.Length + " sounds)");

            // The one that actually blocks the window appearing.
            var sw = Stopwatch.StartNew();
            string updateResult;
            try
            {
                var info = Updater.Check();
                updateResult = info == null ? "up to date" : info.Version + " available";
            }
            catch (Exception e)
            {
                updateResult = "failed: " + e.Message;
            }
            sw.Stop();
            Say(string.Format(CultureInfo.InvariantCulture,
                "  {0,-42} {1,7:N1} ms   <-- network", "Updater.Check (startup update check)",
                sw.Elapsed.TotalMilliseconds));
            Say("      (" + updateResult + ")");

            Say("");
            Say("SHUTDOWN PHASES");

            var monitor = new DriveMonitor(() => @"C:\", _ => { });
            monitor.Start();
            Thread.Sleep(300); // let it settle into its wait
            Time("DriveMonitor.Stop", () => monitor.Stop());

            var resume = new AutoResumeWatcher(@"C:\", "none", "none", "drive", _ => { });
            resume.Start();
            Thread.Sleep(300);
            Time("AutoResumeWatcher.Stop", () => resume.Stop());

            Say("");
            Say("Anything above roughly 100 ms is felt as a delay.");

            try { File.WriteAllText(ReportPath, Transcript.ToString()); } catch { }
            return 0;
        }
    }
}
