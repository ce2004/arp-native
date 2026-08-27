using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// The four UI cues. Rather than shipping the WAVs alongside the exe, the
    /// tones are regenerated at startup with the exact algorithm from the Python
    /// build's gen_sounds.py, so a single self-contained binary stays a single
    /// file. A sounds\*.wav next to the exe still wins if present, which keeps
    /// custom cues working.
    /// </summary>
    internal static class Sounds
    {
        private const int Rate = 44100;

        private static readonly Dictionary<string, short[]> Cache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new();

        public static void Preload()
        {
            foreach (string n in new[] { "start", "stop", "pause", "unpause" }) Get(n);
        }

        private static short[] Get(string name)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue(name, out var cached)) return cached;

                short[] samples = null;
                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "sounds", name + ".wav");
                    if (File.Exists(path)) samples = ReadPcm16Mono(path);
                }
                catch (Exception e)
                {
                    Log.Warn("Failed reading sounds\\" + name + ".wav: " + e.Message);
                }

                samples ??= Generate(name);
                Cache[name] = samples;
                return samples;
            }
        }

        // Identical to gen_sounds.py, including the 400-sample fades and the
        // phase-accumulated sweep, so the cues sound the same as before.
        internal static short[] Generate(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "start": return Tone(440, 880, 200);
                case "stop": return Tone(880, 440, 200);
                case "pause": return DoubleBeep(400);
                case "unpause": return DoubleBeep(800);
                default: return Array.Empty<short>();
            }
        }

        private static short[] Tone(double fStart, double fEnd, int durationMs)
        {
            int n = (int)(Rate * (durationMs / 1000.0));
            var outp = new short[n];
            double phase = 0.0;
            for (int i = 0; i < n; i++)
            {
                double freq = fStart + (fEnd - fStart) * ((double)i / n);
                phase += 2 * Math.PI * freq / Rate;
                double val = Math.Sin(phase);
                outp[i] = (short)(int)(val * Envelope(i, n) * 32767);
            }
            return outp;
        }

        private static short[] DoubleBeep(double freq, int durationMs = 80, int gapMs = 40)
        {
            int beat = (int)(Rate * (durationMs / 1000.0));
            int gap = (int)(Rate * (gapMs / 1000.0));
            var outp = new short[beat * 2 + gap];
            int p = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < beat; i++)
                {
                    double t = (double)i / Rate;
                    double val = Math.Sin(2 * Math.PI * freq * t);
                    outp[p++] = (short)(int)(val * Envelope(i, beat) * 32767);
                }
                if (pass == 0) p += gap; // silence
            }
            return outp;
        }

        private static double Envelope(int i, int n)
        {
            if (i < 400) return i / 400.0;
            if (i > n - 400) return (n - i) / 400.0;
            return 1.0;
        }

        private static short[] ReadPcm16Mono(string path)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 44) return null;
            int dataPos = -1, dataLen = 0;
            int channels = 1, bits = 16;

            for (int i = 12; i + 8 <= bytes.Length;)
            {
                string id = System.Text.Encoding.ASCII.GetString(bytes, i, 4);
                int size = BitConverter.ToInt32(bytes, i + 4);
                if (size < 0 || i + 8 + size > bytes.Length) size = bytes.Length - i - 8;
                if (id == "fmt ")
                {
                    channels = BitConverter.ToUInt16(bytes, i + 10);
                    bits = BitConverter.ToUInt16(bytes, i + 22);
                }
                else if (id == "data")
                {
                    dataPos = i + 8;
                    dataLen = size;
                    break;
                }
                i += 8 + size + (size % 2);
            }

            if (dataPos < 0 || bits != 16) return null;

            int frames = dataLen / 2 / Math.Max(1, channels);
            var outp = new short[frames];
            for (int f = 0; f < frames; f++)
                outp[f] = BitConverter.ToInt16(bytes, dataPos + f * channels * 2);
            return outp;
        }

        /// <summary>Plays a cue at the configured volume, mixed down in software.</summary>
        public static void Play(string name, int volumePercent)
        {
            short[] src = Get(name);
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
