using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arp
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(uint processId);

        [STAThread]
        private static int Main(string[] args)
        {
            // Relaunched by an update: delete the executable the previous
            // version left behind. This runs before anything else so the
            // cleanup happens even when the relaunch also carries a switch.
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] != "--finish-update") continue;
                AttachConsole(unchecked((uint)-1));
                int.TryParse(args[i + 1], out int pid);
                Updater.FinishUpdate(pid);
                break;
            }

            // Sweep any leftovers from an interrupted update. This runs for
            // every invocation, not just a full launch, so the promise that an
            // update leaves nothing behind does not depend on how the program
            // was started. It only ever removes this application's own
            // update temporaries.
            Updater.CleanupOnStartup();

            foreach (string a in args)
            {
                if (a != "--selftest" && a != "--uitest" && a != "--captest" && a != "--signaltest" &&
                    a != "--speech" && a != "--config" && a != "--checkupdate" && a != "--update" && a != "--timing" && a != "--sounds" &&
                    a != "--devices" && a != "--version") continue;

                // A WinExe has no console of its own; borrow the caller's so the
                // diagnostic switches are usable from a terminal.
                AttachConsole(unchecked((uint)-1));

                if (a == "--selftest")
                {
                    // Optional trailing path: a folder of reference WAVs from
                    // gen_sounds.py to compare the regenerated cues against.
                    int i = Array.IndexOf(args, a);
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        SelfTest.ReferenceSoundsDir = args[i + 1];
                    return SelfTest.Run();
                }
                if (a == "--uitest") return UiTest.Run();
                if (a == "--captest") return CaptureTest.Run(args);
                if (a == "--signaltest") return SignalTest.Run(args);
                if (a == "--speech") return SpeechCheck(Array.IndexOf(args, "say") >= 0);
                if (a == "--config") return DumpConfig();
                if (a == "--sounds") return ListSounds();
                if (a == "--timing") return TimingTest.Run();
                if (a == "--checkupdate") return Updater.CheckHeadless();
                if (a == "--update") return Updater.UpdateHeadless();
                if (a == "--devices") return ListDevices();
                Console.WriteLine(Updater.CurrentVersion + " (" + Updater.ArchSuffix + ")");
                return 0;
            }

            try
            {
                return RunApp();
            }
            catch (Exception e)
            {
                Log.Error("Fatal error: " + e.Message, e);
                Win32.MessageBoxW(IntPtr.Zero,
                    "Audio Recorder Pro hit an unrecoverable error and has to close.\r\n\r\n" + e +
                    "\r\n\r\nDetails were written to:\r\n" + Log.FilePath,
                    "Audio Recorder Pro", Win32.MB_OK | Win32.MB_ICONERROR | Win32.MB_SETFOREGROUND);
                return 1;
            }
        }

        private static int RunApp()
        {
            Thread.CurrentThread.Name = "MainThread";
            Log.Info("App started. OS: " + Environment.OSVersion.VersionString +
                     " / " + RuntimeInformation.OSArchitecture +
                     ", process " + RuntimeInformation.ProcessArchitecture +
                     ", version " + Updater.CurrentVersion);

            Win32.EnableDpiAwareness();
            Win32.InitCommonControls();
            Speech.Init();
            // Sounds are rendered on first use, not up front. With a large
            // library there is no reason to spend startup time building cues
            // that may never be played.

            var cfg = new Config();
            var window = new MainWindow(cfg);
            IntPtr hwnd = window.CreateModeless(IntPtr.Zero);
            Win32.ShowWindow(hwnd, Win32.SW_SHOWNORMAL);
            Win32.SetForegroundWindow(hwnd);

            // IsDialogMessage is what makes Tab, arrow-key groups, Alt-mnemonics
            // and Enter/Escape work in a modeless dialog.
            while (true)
            {
                int r = Win32.GetMessageW(out Win32.MSG msg, IntPtr.Zero, 0, 0);
                if (r == 0 || r == -1) break;
                if (Win32.IsWindow(hwnd) && Win32.IsDialogMessageW(hwnd, ref msg)) continue;
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessageW(ref msg);
            }

            Log.Info("App exiting normally.");
            return 0;
        }

        /// <summary>
        /// Reports which speech backend loaded and whether NVDA is reachable.
        /// Add the word "say" to also send a test phrase to NVDA.
        /// </summary>
        private static int SpeechCheck(bool speak)
        {
            Speech.Init();
            Console.WriteLine("Process architecture : " + RuntimeInformation.ProcessArchitecture);
            Console.WriteLine("Controller client    : " + (Speech.HasNvda ? "loaded" : "NOT FOUND"));
            Console.WriteLine("NVDA running         : " + (Speech.NvdaRunning() ? "yes" : "no"));
            Console.WriteLine("Backend              : " + Speech.Describe());

            if (!Speech.HasNvda)
            {
                Console.WriteLine();
                Console.WriteLine("Place nvdaControllerClient" +
                    (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "Arm64" : "64") +
                    ".dll next to this executable.");
                return 1;
            }

            if (speak)
            {
                Console.WriteLine();
                Console.WriteLine("Sending a test phrase to NVDA...");
                Speech.SpeakRaw("Audio Recorder Pro speech test. If you can hear this, announcements are working.");
            }
            return Speech.NvdaRunning() ? 0 : 2;
        }

        /// <summary>
        /// Reads the shared configuration file and prints what this build makes
        /// of it, without writing anything. Confirms that the file the Python
        /// version wrote is understood here, including which device it resolves
        /// to. Read-only on purpose.
        /// </summary>
        private static int DumpConfig()
        {
            string path = Path.Combine(Config.AppDataDir, "recorder_config.json");
            Console.WriteLine("Config file : " + path);
            Console.WriteLine("Exists      : " + File.Exists(path));
            if (!File.Exists(path))
            {
                Console.WriteLine();
                Console.WriteLine("No configuration file yet; defaults would be used.");
                return 1;
            }

            var cfg = new Config(path);
            Console.WriteLine();
            Console.WriteLine("As read by this build:");
            void W(string k, object v) => Console.WriteLine("  {0,-26} {1}", k, v);

            W("save_folder", cfg.SaveFolder);
            W("sample_rate", cfg.SampleRate);
            W("bit_depth", cfg.BitDepth);
            W("channels", cfg.Channels);
            W("buffer_size", cfg.BufferSize);
            W("filename_prefix", "\"" + cfg.FilenamePrefix + "\"");
            W("auto_start", cfg.AutoStart);
            W("auto_start_delay", cfg.AutoStartDelay + " s");
            W("auto_split_secs", cfg.AutoSplitSecs + " s");
            W("max_length_secs", cfg.MaxLengthSecs + " s");
            W("group_splits", cfg.GroupSplits);
            W("in1_route", cfg.In1Route);
            W("in2_route", cfg.In2Route);
            W("in1_gain / in2_gain", cfg.In1Gain + " / " + cfg.In2Gain);
            W("window_title", cfg.WindowTitle);
            W("confirm_exit", cfg.ConfirmExit);
            W("speak_in_focus_only", cfg.SpeakInFocusOnly);
            W("auto_resume_unattended", cfg.AutoResumeUnattended);
            W("continue_on_mic_disconnect", cfg.ContinueOnMicDisconnect);
            W("check_updates_startup", cfg.CheckUpdatesStartup);
            W("snd_volume", cfg.SndVolume);
            foreach (string ev in Config.SoundEvents)
                W("sound for " + ev, cfg.SoundFor(ev) + (cfg.SndEnabled(ev) ? "" : "  (disabled)"));
            W("device_id", cfg.DeviceId);
            W("device2_id", cfg.Device2Id);

            Console.WriteLine();
            Console.WriteLine("Device resolution:");
            try
            {
                Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_APARTMENTTHREADED);
                var devices = Wasapi.EnumerateDevices();
                var d1 = devices.Find(d => d.Id == cfg.DeviceId);
                Console.WriteLine("  Input 1  " + (d1 != null ? "FOUND   " + d1.DisplayName : "NOT CONNECTED"));
                if (cfg.Device2Id == "none")
                {
                    Console.WriteLine("  Input 2  not configured");
                }
                else
                {
                    var d2 = devices.Find(d => d.Id == cfg.Device2Id);
                    Console.WriteLine("  Input 2  " + (d2 != null ? "FOUND   " + d2.DisplayName : "NOT CONNECTED"));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("  device enumeration failed: " + e.Message);
            }

            Console.WriteLine();
            Console.WriteLine("Keys present in the file that this build does not use:");
            var known = new HashSet<string>(StringComparer.Ordinal)
            {
                "auto_start","auto_start_delay","save_folder","sample_rate","bit_depth","channels",
                "filename_prefix","auto_split_secs","max_length_secs","group_splits","buffer_size",
                "device_id","device2_id","in1_route","in2_route","in1_gain","in2_gain","window_title",
                "notify_start_stop","notify_split","notify_error","notify_drive_disconnect",
                "notify_mic_disconnect","speak_in_focus_only","auto_resume_unattended",
                "continue_on_mic_disconnect","confirm_exit","check_updates_startup",
                "snd_start","snd_stop","snd_pause","snd_unpause","snd_volume","device_sort_order",
                "snd_start_sound","snd_stop_sound","snd_pause_sound","snd_unpause_sound",
            };
            var raw = JsonObject.Parse(File.ReadAllText(path));
            bool any = false;
            foreach (string k in raw.Keys)
            {
                if (known.Contains(k)) continue;
                Console.WriteLine("  " + k + "   (preserved on save)");
                any = true;
            }
            if (!any) Console.WriteLine("  none");

            Console.WriteLine();
            Console.WriteLine("Nothing was written. This command is read-only.");
            return 0;
        }

        /// <summary>Lists the built-in cue library with duration and peak level.</summary>
        private static int ListSounds()
        {
            Console.WriteLine("{0,-22} {1,9} {2,8}", "SOUND", "DURATION", "PEAK");
            foreach (string name in SoundLibrary.Names)
            {
                var s = SoundLibrary.Get(name);
                if (s.Length == 0) { Console.WriteLine("{0,-22} {1,9} {2,8}", name, "-", "-"); continue; }
                short peak = 0;
                foreach (short v in s) if (Math.Abs((int)v) > Math.Abs((int)peak)) peak = v;
                Console.WriteLine("{0,-22} {1,8:N0}ms {2,8}", name,
                    s.Length * 1000.0 / SoundLibrary.Rate, Math.Abs((int)peak));
            }
            Console.WriteLine();
            Console.WriteLine(SoundLibrary.Names.Length + " sounds.");
            return 0;
        }

        /// <summary>Prints the device list the settings dialog would show. Diagnostic only.</summary>
        private static int ListDevices()
        {
            try
            {
                Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_APARTMENTTHREADED);
                foreach (var d in Wasapi.EnumerateDevices())
                    Console.WriteLine(d.DisplayName + "\n    id: " + d.Id);
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Device enumeration failed: " + e);
                return 1;
            }
        }
    }
}
