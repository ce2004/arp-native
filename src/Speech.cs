using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Arp
{
    /// <summary>
    /// Screen-reader output through the NVDA controller client.
    ///
    /// NVDA only, by design. There is no SAPI fallback: a synthesiser talking
    /// over the top of NVDA is worse than silence, and every status message
    /// here is also written to the dashboard, which NVDA reads from the control
    /// itself. If the controller client is missing, announcements are logged
    /// and dropped rather than spoken by something else.
    /// </summary>
    internal static unsafe class Speech
    {
        private static bool _init;
        private static IntPtr _nvdaLib;
        private static delegate* unmanaged[Stdcall]<char*, int> _speakText;
        private static delegate* unmanaged[Stdcall]<int> _testIfRunning;
        private static delegate* unmanaged[Stdcall]<int> _cancelSpeech;

        public static bool HasNvda => _speakText != null;

        /// <summary>Set by the app so "speak only when focused" can be honoured.</summary>
        public static Func<bool> ShouldSpeak { get; set; }

        public static void Init()
        {
            if (_init) return;
            _init = true;
            LoadNvda();
            Log.Info("Speech backend: " + Describe());
        }

        public static string Describe()
        {
            if (_speakText == null) return "none (NVDA controller client not found)";
            return NvdaRunning() ? "NVDA controller client (NVDA running)" : "NVDA controller client (NVDA not running)";
        }

        private static void LoadNvda()
        {
            // The controller client must match the *calling process*
            // architecture, not NVDA's. An ARM64 build therefore needs
            // nvdaControllerClientArm64.dll next to the exe; an x64 build needs
            // nvdaControllerClient64.dll.
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "Arm64",
                Architecture.X64 => "64",
                Architecture.X86 => "32",
                _ => "64",
            };

            string baseDir = AppContext.BaseDirectory;
            var candidates = new[]
            {
                Path.Combine(baseDir, "nvdaControllerClient" + arch + ".dll"),
                Path.Combine(baseDir, "nvdaControllerClient.dll"),
                "nvdaControllerClient" + arch + ".dll",
                "nvdaControllerClient.dll",
            };

            foreach (string c in candidates)
            {
                if (!NativeLibrary.TryLoad(c, out IntPtr lib)) continue;
                try
                {
                    if (!NativeLibrary.TryGetExport(lib, "nvdaController_speakText", out IntPtr speak) ||
                        !NativeLibrary.TryGetExport(lib, "nvdaController_testIfRunning", out IntPtr test))
                    {
                        NativeLibrary.Free(lib);
                        continue;
                    }
                    NativeLibrary.TryGetExport(lib, "nvdaController_cancelSpeech", out IntPtr cancel);

                    _nvdaLib = lib;
                    _speakText = (delegate* unmanaged[Stdcall]<char*, int>)speak;
                    _testIfRunning = (delegate* unmanaged[Stdcall]<int>)test;
                    _cancelSpeech = (delegate* unmanaged[Stdcall]<int>)cancel;
                    Log.Info("Loaded NVDA controller client: " + c);
                    return;
                }
                catch (Exception e)
                {
                    Log.Warn("Failed binding NVDA client " + c + ": " + e.Message);
                    try { NativeLibrary.Free(lib); } catch { }
                }
            }
            Log.Warn("No NVDA controller client found for " + arch +
                     "; spoken announcements are disabled. Place nvdaControllerClient" + arch +
                     ".dll next to the executable to enable them.");
        }

        public static bool NvdaRunning()
        {
            if (_testIfRunning == null) return false;
            try { return _testIfRunning() == 0; }
            catch { return false; }
        }

        public static void Speak(string message)
        {
            var gate = ShouldSpeak;
            if (gate != null && !gate()) return;
            SpeakRaw(message);
        }

        /// <summary>Bypasses the focus gate, for messages that must always land.</summary>
        public static void SpeakRaw(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_speakText == null) return;

            try
            {
                if (!NvdaRunning()) return;
                fixed (char* p = message)
                {
                    if (_speakText(p) != 0) Log.Warn("NVDA rejected an announcement.");
                }
            }
            catch (Exception e)
            {
                Log.Warn("NVDA speak failed: " + e.Message);
            }
        }

        public static void Cancel()
        {
            try
            {
                if (_cancelSpeech != null && NvdaRunning()) _cancelSpeech();
            }
            catch
            {
            }
        }
    }
}
