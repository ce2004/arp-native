using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Plays a named cue from <see cref="SoundLibrary"/> at the configured
    /// volume. Every sound is generated in code, so there is no sounds folder
    /// and nothing to ship beside the executable.
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = SoundLibrary.Rate;

        public static void Preload() => SoundLibrary.Preload();

        /// <summary>Plays a cue by library name, scaled to the given volume.</summary>
        public static void Play(string soundName, int volumePercent)
        {
            short[] src = SoundLibrary.Get(soundName);
            if (src == null || src.Length == 0) return;

            double gain = Math.Clamp(volumePercent, 0, 100) / 100.0;
            if (gain <= 0) return;

            var scaled = new short[src.Length];
            for (int i = 0; i < src.Length; i++) scaled[i] = (short)(int)(src[i] * gain);

            try { WaveOut.PlayAsync(scaled, Rate); }
            catch (Exception e) { Log.Warn("Sound playback failed: " + e.Message); }
        }
    }

    /// <summary>
    /// Minimal waveOut playback. winmm is used instead of PlaySound because
    /// PlaySound cuts off whatever is already playing and offers no volume
    /// control, and instead of a higher-level API because this has to link
    /// cleanly under NativeAOT.
    /// </summary>
    internal static class WaveOut
    {
        private const uint WAVE_MAPPER = 0xFFFFFFFF;
        private const uint CALLBACK_EVENT = 0x00050000;
        private const uint WHDR_DONE = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength;
            public uint dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags;
            public uint dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [DllImport("winmm.dll")]
        private static extern int waveOutOpen(out IntPtr hwo, uint deviceId, ref WAVEFORMATEX fmt,
            IntPtr callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll")]
        private static extern int waveOutPrepareHeader(IntPtr hwo, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutWrite(IntPtr hwo, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutUnprepareHeader(IntPtr hwo, IntPtr hdr, int size);

        [DllImport("winmm.dll")]
        private static extern int waveOutReset(IntPtr hwo);

        [DllImport("winmm.dll")]
        private static extern int waveOutClose(IntPtr hwo);

        [DllImport("winmm.dll")]
        private static extern uint waveOutGetNumDevs();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WAVEOUTCAPSW
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
            public uint dwSupport;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int waveOutGetDevCapsW(IntPtr deviceId, ref WAVEOUTCAPSW caps, int size);

        /// <summary>Finds a waveOut device by substring, or WAVE_MAPPER if none matches.</summary>
        public static uint FindDevice(string nameContains)
        {
            uint count = waveOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                var caps = new WAVEOUTCAPSW();
                if (waveOutGetDevCapsW((IntPtr)i, ref caps, Marshal.SizeOf<WAVEOUTCAPSW>()) != 0) continue;
                if (caps.szPname != null &&
                    caps.szPname.IndexOf(nameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return WAVE_MAPPER;
        }

        public static void PlayAsync(short[] samples, int sampleRate, int channels = 1, uint deviceId = WAVE_MAPPER)
        {
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1,
                nChannels = (ushort)channels,
                nSamplesPerSec = (uint)sampleRate,
                nBlockAlign = (ushort)(2 * channels),
                wBitsPerSample = 16,
                nAvgBytesPerSec = (uint)(sampleRate * 2 * channels),
                cbSize = 0,
            };

            var doneEvent = new ManualResetEvent(false);
            if (waveOutOpen(out IntPtr hwo, deviceId, ref fmt, doneEvent.SafeWaitHandle.DangerousGetHandle(),
                    IntPtr.Zero, CALLBACK_EVENT) != 0)
            {
                doneEvent.Dispose();
                return;
            }

            int bytes = samples.Length * 2;
            IntPtr data = Marshal.AllocHGlobal(bytes);
            Marshal.Copy(samples, 0, data, samples.Length);

            IntPtr hdrPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEHDR>());
            var hdr = new WAVEHDR { lpData = data, dwBufferLength = (uint)bytes };
            Marshal.StructureToPtr(hdr, hdrPtr, false);

            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            if (waveOutPrepareHeader(hwo, hdrPtr, hdrSize) != 0 || waveOutWrite(hwo, hdrPtr, hdrSize) != 0)
            {
                Cleanup(hwo, hdrPtr, data, doneEvent);
                return;
            }

            // The cues are a fifth of a second; a background thread reaping them
            // keeps playback off the UI thread without a callback into managed
            // code from an unmanaged audio thread.
            var t = new Thread(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int timeoutMs = (int)(samples.Length * 1000L / sampleRate) + 2000;
                    while (sw.ElapsedMilliseconds < timeoutMs)
                    {
                        var cur = Marshal.PtrToStructure<WAVEHDR>(hdrPtr);
                        if ((cur.dwFlags & WHDR_DONE) != 0) break;
                        doneEvent.WaitOne(50);
                    }
                }
                catch
                {
                }
                finally
                {
                    Cleanup(hwo, hdrPtr, data, doneEvent);
                }
            })
            { IsBackground = true, Name = "SoundReaper" };
            t.Start();
        }

        private static void Cleanup(IntPtr hwo, IntPtr hdrPtr, IntPtr data, ManualResetEvent ev)
        {
            try { waveOutReset(hwo); } catch { }
            try { waveOutUnprepareHeader(hwo, hdrPtr, Marshal.SizeOf<WAVEHDR>()); } catch { }
            try { waveOutClose(hwo); } catch { }
            try { Marshal.FreeHGlobal(hdrPtr); } catch { }
            try { Marshal.FreeHGlobal(data); } catch { }
            try { ev.Dispose(); } catch { }
        }
    }
}
