using System;
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
            foreach (string a in args)
            {
                if (a != "--selftest" && a != "--uitest" && a != "--captest" && a != "--signaltest" &&
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
            Sounds.Preload();

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
