using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// Proves real audio survives the whole path, not just silence: plays a
    /// known stereo tone into a virtual cable, captures that cable's loopback
    /// through the normal recording pipeline, then reads the file back and
    /// checks amplitude, frequency and channel placement.
    ///
    /// This is what catches a wrong conversion scale, swapped channels, a
    /// half-rate stream or a botched 24-bit pack — none of which a silent
    /// capture can reveal. Uses a virtual cable, so nothing is audible and no
    /// microphone is opened.
    ///
    /// Run with: ArpRecorder.exe --signaltest [--cable "Line 1"]
    /// </summary>
    internal static class SignalTest
    {
        private static readonly System.Text.StringBuilder Transcript = new();
        private static readonly List<string> Failures = new();
        private static int _passed;

        public static string ReportPath { get; } =
            Path.Combine(Path.GetTempPath(), "arp_signaltest_report.txt");

        private static void Say(string s)
        {
            Transcript.AppendLine(s);
            Console.WriteLine(s);
        }

        private static void Check(bool ok, string what)
        {
            if (ok) { _passed++; Say("   ok   " + what); }
            else { Failures.Add(what); Say("   FAIL " + what); }
        }

        private const int Rate = 48000;
        private const int Bits = 24;
        private const double ToneHz = 1000.0;
        private const double Amplitude = 0.5;
        private const int Seconds = 4;

        public static int Run(string[] args)
        {
            string cable = "Line 1";
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--cable") cable = args[i + 1];

            string dir = Path.Combine(Path.GetTempPath(), "arp_signaltest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_APARTMENTTHREADED);

                var devices = Wasapi.EnumerateDevices();
                var loopback = devices.Find(d => d.IsLoopback &&
                    d.Name.IndexOf(cable, StringComparison.OrdinalIgnoreCase) >= 0);

                uint outDevice = WaveOut.FindDevice(cable);

                if (loopback == null || outDevice == 0xFFFFFFFF)
                {
                    Say("Skipped: no virtual cable matching \"" + cable + "\" found.");
                    Say("  loopback endpoint: " + (loopback?.DisplayName ?? "not found"));
                    Say("  waveOut device   : " + (outDevice == 0xFFFFFFFF ? "not found" : outDevice.ToString()));
                    Say("");
                    Say("This test needs a virtual audio cable so nothing is played aloud.");
                    return 0;
                }

                Say("Capturing : " + loopback.DisplayName);
                Say("Playing   : waveOut device " + outDevice + " (\"" + cable + "\")");
                Say("Tone      : " + ToneHz + " Hz, amplitude " + Amplitude + ", both channels");
                Say("");

                var cfg = new Config(Path.Combine(dir, "config.json"))
                {
                    SaveFolder = dir,
                    SampleRate = Rate.ToString(CultureInfo.InvariantCulture),
                    BitDepth = Bits.ToString(CultureInfo.InvariantCulture),
                    Channels = "2",
                    BufferSize = 2048,
                    AutoSplitSecs = 0,
                    GroupSplits = false,
                    DeviceId = loopback.Id,
                    Device2Id = "none",
                };

                var errors = new List<string>();
                var rec = new Recorder(cfg)
                {
                    OnError = m => { lock (errors) errors.Add(m); Say("   [error] " + m); },
                };

                rec.Start(loopback, null, Rate, 2, Bits, 2048, "Signal");

                // Give the capture a moment to settle before the tone starts.
                Thread.Sleep(400);
                PlayTone(outDevice);

                rec.Stop();
                var sw = Stopwatch.StartNew();
                while ((rec.WriterAlive || rec.ReadersAlive) && sw.Elapsed.TotalSeconds < 15) Thread.Sleep(50);

                Check(errors.Count == 0, "no errors during capture");

                var files = Directory.GetFiles(rec.SessionFolder, "*.wav");
                Check(files.Length == 1, "exactly one file produced");
                if (files.Length == 0) return Finish();

                string path = files[0];
                Check(WavFile.Verify(path, 2, Rate), "file verifies");

                var (left, right) = ReadPcm24Stereo(path);
                Say("");
                Say("Read back " + left.Length + " frames (" +
                    (left.Length / (double)Rate).ToString("F2", CultureInfo.InvariantCulture) + " s)");

                Check(left.Length > Rate, "at least a second of audio captured");
                if (left.Length <= Rate) return Finish();

                // Analyse the middle, away from the fade edges and the settle.
                int from = left.Length / 3, len = Math.Min(Rate, left.Length - from - 1);

                double peakL = Peak(left, from, len);
                double peakR = Peak(right, from, len);
                Say("Peak amplitude: left " + peakL.ToString("F4", CultureInfo.InvariantCulture) +
                    ", right " + peakR.ToString("F4", CultureInfo.InvariantCulture));

                // The scale must survive unchanged: a factor-of-two error in the
                // 24-bit pack or the float conversion would show up right here.
                Check(peakL > Amplitude * 0.85 && peakL < Amplitude * 1.15,
                    "left channel amplitude is ~" + Amplitude + " (got " + peakL.ToString("F3", CultureInfo.InvariantCulture) + ")");
                Check(peakR > Amplitude * 0.85 && peakR < Amplitude * 1.15,
                    "right channel amplitude is ~" + Amplitude + " (got " + peakR.ToString("F3", CultureInfo.InvariantCulture) + ")");

                double atTone = Goertzel(left, from, len, ToneHz, Rate);
                double atHalf = Goertzel(left, from, len, ToneHz / 2, Rate);
                double atDouble = Goertzel(left, from, len, ToneHz * 2, Rate);
                double atOther = Goertzel(left, from, len, 3300, Rate);

                Say("Goertzel magnitude: " + ToneHz + " Hz = " + atTone.ToString("F4", CultureInfo.InvariantCulture) +
                    ", " + (ToneHz / 2) + " Hz = " + atHalf.ToString("F4", CultureInfo.InvariantCulture) +
                    ", " + (ToneHz * 2) + " Hz = " + atDouble.ToString("F4", CultureInfo.InvariantCulture) +
                    ", 3300 Hz = " + atOther.ToString("F4", CultureInfo.InvariantCulture));

                Check(atTone > atOther * 20, "energy is concentrated at " + ToneHz + " Hz");
                // Half or double showing up instead would mean the stream ran at
                // the wrong rate despite reporting 48 kHz.
                Check(atTone > atHalf * 10, "not running at half the expected sample rate");
                Check(atTone > atDouble * 10, "not running at double the expected sample rate");
                Check(atTone > Amplitude * 0.4, "tone magnitude is in the right range");

                return Finish();
            }
            catch (Exception e)
            {
                Say("Harness crashed: " + e);
                Failures.Add("harness crash");
                return Finish();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        private static int Finish()
        {
            Say("");
            Say(_passed + " signal checks passed, " + Failures.Count + " failed.");
            foreach (string f in Failures) Say("  FAIL: " + f);
            try { File.WriteAllText(ReportPath, Transcript.ToString()); } catch { }
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void PlayTone(uint outDevice)
        {
            int n = Rate * Seconds;
            var buf = new short[n * 2];
            for (int i = 0; i < n; i++)
            {
                double v = Math.Sin(2 * Math.PI * ToneHz * i / Rate) * Amplitude;
                short s = (short)(int)(v * 32767);
                buf[i * 2] = s;
                buf[i * 2 + 1] = s;
            }
            WaveOut.PlayAsync(buf, Rate, 2, outDevice);
            Thread.Sleep(Seconds * 1000 + 300);
        }

        private static double Peak(float[] a, int from, int len)
        {
            double p = 0;
            for (int i = from; i < from + len; i++)
            {
                double v = Math.Abs(a[i]);
                if (v > p) p = v;
            }
            return p;
        }

        /// <summary>Goertzel magnitude at one frequency, normalised to amplitude.</summary>
        private static double Goertzel(float[] a, int from, int len, double freq, int rate)
        {
            double w = 2 * Math.PI * freq / rate;
            double coeff = 2 * Math.Cos(w);
            double s1 = 0, s2 = 0;
            for (int i = from; i < from + len; i++)
            {
                double s0 = a[i] + coeff * s1 - s2;
                s2 = s1;
                s1 = s0;
            }
            double mag = Math.Sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2);
            return mag / (len / 2.0);
        }

        private static (float[] left, float[] right) ReadPcm24Stereo(string path)
        {
            var bytes = File.ReadAllBytes(path);
            int dataOffset = 80; // RF64 header written by Rf64Writer
            long dataBytes = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(28));
            int frames = (int)(dataBytes / 6);

            var l = new float[frames];
            var r = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                int o = dataOffset + i * 6;
                l[i] = Sample24(bytes, o) / 8388607f;
                r[i] = Sample24(bytes, o + 3) / 8388607f;
            }
            return (l, r);
        }

        private static int Sample24(byte[] b, int o)
        {
            int v = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
            if ((v & 0x800000) != 0) v |= unchecked((int)0xFF000000); // sign extend
            return v;
        }
    }
}
