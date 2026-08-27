using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Arp
{
    /// <summary>
    /// End-to-end exercise of the real capture pipeline: WASAPI init, reader
    /// threads, the mixing writer loop, auto-split and finalisation, writing to
    /// a temporary folder.
    ///
    /// Defaults to a loopback (output) endpoint so no microphone is opened.
    /// Run with: ArpRecorder.exe --captest [seconds] [--device &lt;id&gt;] [--split &lt;secs&gt;]
    ///                                     [--rate N] [--bits N] [--channels N] [--buffer N]
    /// </summary>
    internal static class CaptureTest
    {
        private static readonly System.Text.StringBuilder Transcript = new();
        private static readonly List<string> Failures = new();
        private static int _passed;

        public static string ReportPath { get; } =
            Path.Combine(Path.GetTempPath(), "arp_captest_report.txt");

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

        public static int Run(string[] args)
        {
            int seconds = 4;
            int splitSecs = 0;
            int rate = 48000, bits = 24, channels = 2, buffer = 2048;
            string deviceId = null;
            string device2Id = null;
            bool keep = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--device": deviceId = Next(args, ref i); break;
                    case "--device2": device2Id = Next(args, ref i); break;
                    case "--keep": keep = true; break;
                    case "--split": splitSecs = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--rate": rate = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--bits": bits = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--channels": channels = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    case "--buffer": buffer = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture); break;
                    default:
                        if (args[i] != "--captest" && int.TryParse(args[i], NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out int s)) seconds = s;
                        break;
                }
            }

            string dir = Path.Combine(Path.GetTempPath(), "arp_captest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_APARTMENTTHREADED);
                var devices = Wasapi.EnumerateDevices();

                AudioDevice mic = deviceId != null
                    ? devices.Find(d => d.Id == deviceId)
                    : devices.Find(d => d.IsLoopback);

                if (mic == null)
                {
                    Say("No suitable capture device found" + (deviceId != null ? " for id " + deviceId : ""));
                    return 1;
                }

                AudioDevice mic2 = null;
                if (device2Id != null)
                {
                    mic2 = devices.Find(d => d.Id == device2Id);
                    if (mic2 == null) { Say("Second device not found: " + device2Id); return 1; }
                }

                Say("Device : " + mic.DisplayName);
                if (mic2 != null) Say("Device2: " + mic2.DisplayName);
                Say("Format : " + rate + " Hz, " + bits + "-bit, " + channels + " ch, buffer " + buffer + " frames");
                Say("Length : " + seconds + " s" + (splitSecs > 0 ? ", splitting every " + splitSecs + " s" : ""));
                Say("Folder : " + dir);
                Say("");

                var cfg = new Config(Path.Combine(dir, "config.json"))
                {
                    SaveFolder = dir,
                    SampleRate = rate.ToString(CultureInfo.InvariantCulture),
                    BitDepth = bits.ToString(CultureInfo.InvariantCulture),
                    Channels = channels.ToString(CultureInfo.InvariantCulture),
                    BufferSize = buffer,
                    AutoSplitSecs = splitSecs,
                    GroupSplits = splitSecs > 0,
                    DeviceId = mic.Id,
                    Device2Id = mic2?.Id ?? "none",
                };

                var errors = new List<string>();
                var stalls = new List<string>();
                int splitEvents = 0;

                var rec = new Recorder(cfg)
                {
                    OnError = m => { lock (errors) errors.Add(m); Say("   [error] " + m); },
                    OnSplit = () => { Interlocked.Increment(ref splitEvents); Say("   [split]"); },
                    OnMicDisconnected = (n, c) => { lock (errors) errors.Add("mic " + n + " disconnected"); },
                    // Tracked apart from errors: in the app this same callback
                    // routes into the error path and stops the recording, which
                    // is inherited from the Python build and is why an input
                    // that goes quiet for two seconds is worth flagging.
                    OnStallWarning = m => { lock (stalls) stalls.Add(m); Say("   [stall] " + m); },
                };

                var sw = Stopwatch.StartNew();
                rec.Start(mic, mic2, rate, channels, bits, buffer, "CapTest");

                // Drive the split timer the way the UI timer would.
                var splitClock = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    Thread.Sleep(50);
                    if (splitSecs > 0 && splitClock.Elapsed.TotalSeconds >= splitSecs)
                    {
                        splitClock.Restart();
                        rec.RequestSplit();
                    }
                }

                long framesBeforeStop = rec.TotalFramesWritten;
                rec.Stop();

                var shutdown = Stopwatch.StartNew();
                while ((rec.WriterAlive || rec.ReadersAlive) && shutdown.Elapsed.TotalSeconds < 15)
                    Thread.Sleep(50);

                Say("Shutdown took " + shutdown.Elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture) + " s");
                Say("");

                var session = rec.Session;
                Check(!rec.WriterAlive, "writer thread stopped");
                Check(!rec.ReadersAlive, "reader threads stopped");
                Check(errors.Count == 0, "no errors reported" + (errors.Count > 0 ? ": " + string.Join("; ", errors) : ""));
                Check(session.Finalization == FinalizationStatus.Success,
                    "finalisation succeeded (was " + session.Finalization + ")");
                Check(session.DroppedBlocks == 0, "no dropped blocks (" + session.DroppedBlocks + ")");
                Say("   Silence blocks substituted: " + session.SilenceBlocks);
                Say("   Stall warnings: " + stalls.Count +
                    (stalls.Count > 0 ? "  <- would stop the recording in the app" : ""));

                var files = new List<string>(Directory.GetFiles(rec.SessionFolder, "*.wav"));
                files.Sort(StringComparer.Ordinal);
                Say("");
                Say("Files written: " + files.Count);

                long totalFrames = 0;
                foreach (string f in files)
                {
                    var fi = new FileInfo(f);
                    long dataBytes = fi.Length - 80;
                    long frames = dataBytes / (channels * (bits / 8));
                    totalFrames += frames;
                    Say("   " + Path.GetFileName(f) + "  " + fi.Length + " bytes, " + frames + " frames, " +
                        (frames / (double)rate).ToString("F2", CultureInfo.InvariantCulture) + " s");
                    Check(WavFile.Verify(f, channels, rate), Path.GetFileName(f) + " verifies as a valid WAV");
                }

                Check(files.Count > 0, "at least one file produced");

                int expectedSplits = splitSecs > 0 ? seconds / splitSecs : 0;
                if (splitSecs > 0)
                {
                    Check(files.Count >= expectedSplits, "produced at least " + expectedSplits + " split files");
                    Check(splitEvents >= expectedSplits, "split callbacks fired (" + splitEvents + ")");
                    Check(rec.SessionFolder != dir, "splits grouped into their own folder");
                }

                double capturedSeconds = totalFrames / (double)rate;
                Say("");
                Say("Captured " + capturedSeconds.ToString("F2", CultureInfo.InvariantCulture) +
                    " s of audio over a " + seconds + " s run (" +
                    (100.0 * capturedSeconds / seconds).ToString("F1", CultureInfo.InvariantCulture) + "%).");

                // Real-time capture must land close to wall clock. The small
                // shortfall that remains is device-open time plus the one block
                // deliberately discarded to flush WASAPI start-up artefacts.
                Check(capturedSeconds > seconds * 0.95, "captured audio covers at least 95% of the run");
                Check(capturedSeconds < seconds * 1.15, "captured audio is not overrunning the run");
                Check(framesBeforeStop > 0, "frames were being written before stop was requested");

                Say("");
                Say(_passed + " capture checks passed, " + Failures.Count + " failed.");
                foreach (string f in Failures) Say("  FAIL: " + f);
                return Failures.Count == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Say("Harness crashed: " + e);
                return 1;
            }
            finally
            {
                try { File.WriteAllText(ReportPath, Transcript.ToString()); } catch { }
                if (!keep) { try { Directory.Delete(dir, true); } catch { } }
            }
        }

        private static string Next(string[] args, ref int i) =>
            i + 1 < args.Length ? args[++i] : throw new ArgumentException("Missing value after " + args[i]);
    }
}
