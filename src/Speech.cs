using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Arp
{
    /// <summary>
    /// Screen-reader output, standing in for accessible_output2's Auto backend.
    /// Prefers the NVDA controller client and falls back to SAPI so unattended
    /// status announcements still happen if NVDA is not running.
    /// </summary>
    internal static unsafe class Speech
    {
        private static bool _init;
        private static IntPtr _nvdaLib;
        private static delegate* unmanaged[Stdcall]<char*, int> _speakText;
        private static delegate* unmanaged[Stdcall]<int> _testIfRunning;
        private static delegate* unmanaged[Stdcall]<int> _cancelSpeech;

        private static IntPtr _sapiVoice;
        private static bool _sapiTried;

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
            if (_speakText != null) return "NVDA controller client";
            EnsureSapi();
            if (_sapiVoice != IntPtr.Zero) return "SAPI (NVDA client not available)";
            return "none";
        }

        private static void LoadNvda()
        {
            // The controller client must match the *calling process*
            // architecture, not NVDA's. An ARM64 build therefore needs
            // nvdaControllerClientArm64.dll next to the exe; an x64 build needs
            // nvdaControllerClient64.dll. Probe by architecture, then fall back
            // to any of the usual names in case only one was shipped.
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
            Log.Info("No NVDA controller client found for " + arch + "; will use SAPI if available.");
        }

        public static bool NvdaRunning()
        {
            if (_testIfRunning == null) return false;
            try { return _testIfRunning() == 0; }
            catch { return false; }
        }

        public static void Speak(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            var gate = ShouldSpeak;
            if (gate != null && !gate()) return;
            SpeakRaw(message);
        }

        /// <summary>Bypasses the focus gate, for messages that must always land.</summary>
        public static void SpeakRaw(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            try
            {
                if (_speakText != null && NvdaRunning())
                {
                    fixed (char* p = message)
                    {
                        if (_speakText(p) == 0) return;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Warn("NVDA speak failed: " + e.Message);
            }

            try { SapiSpeak(message); }
            catch (Exception e) { Log.Warn("SAPI speak failed: " + e.Message); }
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

        // ---- SAPI fallback, reached through vtable slots for NativeAOT ----
        private static readonly Guid CLSID_SpVoice = new("96749377-3391-11D2-9EE3-00C04F797396");
        private static readonly Guid IID_ISpVoice = new("6C44DF74-72B9-4992-A1EC-EF996E0422D4");
        private const int SPF_ASYNC = 1;
        private const int ISpVoice_Speak = 20;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int ctx, in Guid iid, out IntPtr obj);

        private static void EnsureSapi()
        {
            if (_sapiTried) return;
            _sapiTried = true;
            try
            {
                int hr = CoCreateInstance(CLSID_SpVoice, IntPtr.Zero, 23, IID_ISpVoice, out IntPtr voice);
                if (hr >= 0) _sapiVoice = voice;
                else Log.Info("SAPI unavailable (0x" + hr.ToString("X8") + ")");
            }
            catch (Exception e)
            {
                Log.Info("SAPI unavailable: " + e.Message);
            }
        }

        private static void SapiSpeak(string message)
        {
            EnsureSapi();
            if (_sapiVoice == IntPtr.Zero) return;
            void** vt = *(void***)_sapiVoice;
            fixed (char* p = message)
            {
                uint stream;
                ((delegate* unmanaged[Stdcall]<IntPtr, char*, int, uint*, int>)vt[ISpVoice_Speak])(
                    _sapiVoice, p, SPF_ASYNC, &stream);
            }
        }
    }
}
