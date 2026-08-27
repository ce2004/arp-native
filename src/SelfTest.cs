using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Arp
{
    /// <summary>
    /// Headless checks for everything that does not need audio hardware or a
    /// window: config round-tripping, the duration grammar, routing maths, the
    /// RF64 container, the repair path and the dialog templates.
    /// Run with: ArpRecorder.exe --selftest
    /// </summary>
    internal static class SelfTest
    {
        private static int _passed;
        private static readonly List<string> Failures = new();
        private static readonly System.Text.StringBuilder Transcript = new();

        /// <summary>Mirrors output to a file, since a WinExe's console is not always captured.</summary>
        public static string ReportPath { get; } =
            Path.Combine(Path.GetTempPath(), "arp_selftest_report.txt");

        private static void Say(string line)
        {
            Transcript.AppendLine(line);
            Console.WriteLine(line);
        }

        /// <summary>
        /// Optional folder of reference WAVs produced by the Python build's
        /// gen_sounds.py. When supplied, the regenerated cues are compared
        /// sample for sample against them.
        /// </summary>
        public static string ReferenceSoundsDir;

        public static int Run()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "arp_selftest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                TestJson(tmp);
                TestConfig(tmp);
                TestTimeText();
                TestPercentAndNaming();
                TestRouting();
                TestDurationUnits();
                TestNotificationText();
                TestRf64(tmp);
                TestRepair(tmp);
                TestSounds();
                TestUpdater(tmp);
                TestTemplates();
            }
            catch (Exception e)
            {
                Failures.Add("Harness crashed: " + e);
            }
            finally
            {
                try { Directory.Delete(tmp, true); } catch { }
            }

            Say("");
            Say(_passed + " checks passed, " + Failures.Count + " failed.");
            foreach (string f in Failures) Say("  FAIL: " + f);

            try { File.WriteAllText(ReportPath, Transcript.ToString()); } catch { }
            return Failures.Count == 0 ? 0 : 1;
        }

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            Failures.Add(what);
        }

        private static void Eq<T>(T actual, T expected, string what)
        {
            if (EqualityComparer<T>.Default.Equals(actual, expected)) { _passed++; return; }
            Failures.Add(what + " (expected <" + expected + ">, got <" + actual + ">)");
        }

        private static void Section(string name) => Say("-- " + name);

        // ---------------------------------------------------------------

        private static void TestJson(string dir)
        {
            Section("JSON");

            // The exact config shipped with the Python build.
            const string sample = @"{
    ""auto_start"": true,
    ""auto_start_delay"": 5,
    ""save_folder"": ""C:/Users/Admin/Downloads/code"",
    ""sample_rate"": ""44100"",
    ""bit_depth"": ""16"",
    ""channels"": ""2"",
    ""group_splits"": true,
    ""buffer_size"": 512,
    ""device_id"": ""{0.0.1.00000000}.{1bbd4f04-1fa9-44a5-9db6-1c80c85190e2}"",
    ""device2_id"": ""none"",
    ""snd_volume"": 15,
    ""split_silence_sec"": 0,
    ""split_threshold_db"": -40
}";
            var obj = JsonObject.Parse(sample);
            Eq(obj.GetBool("auto_start", false), true, "auto_start parsed");
            Eq(obj.GetInt("auto_start_delay", 0), 5, "auto_start_delay parsed");
            Eq(obj.GetString("sample_rate", ""), "44100", "quoted sample_rate kept as string");
            Eq(obj.GetInt("sample_rate", 0), 44100, "quoted sample_rate readable as int");
            Eq(obj.GetInt("split_threshold_db", 0), -40, "negative number parsed");
            Eq(obj.GetString("device_id", ""), "{0.0.1.00000000}.{1bbd4f04-1fa9-44a5-9db6-1c80c85190e2}",
                "device id with braces parsed");

            string round = obj.ToJson();
            var again = JsonObject.Parse(round);
            Eq(again.GetInt("buffer_size", 0), 512, "round-trip keeps numbers");
            Eq(again.GetInt("split_silence_sec", -1), 0, "round-trip keeps unknown keys");
            Check(round.Contains("\"split_threshold_db\": -40"), "negative number re-serialised without decimals");

            // Backslashes in Windows paths have to survive.
            var esc = new JsonObject();
            esc.Set("save_folder", @"D:\Recordings\Take ""1""");
            string encoded = esc.ToJson();
            Eq(JsonObject.Parse(encoded).GetString("save_folder", ""), @"D:\Recordings\Take ""1""",
                "backslashes and quotes round-trip");

            Check(new List<string>(obj.Keys).IndexOf("auto_start") == 0, "key order preserved");
        }

        private static void TestConfig(string dir)
        {
            Section("Config");

            string path = Path.Combine(dir, "recorder_config.json");
            File.WriteAllText(path,
                "{\"auto_split_mins\": 7, \"window_title\": \"ARP\", \"future_key\": \"keep me\"}");

            var cfg = new Config(path);
            Eq(cfg.AutoSplitSecs, 420, "auto_split_mins migrated to seconds");
            Eq(cfg.WindowTitle, "ARP", "window_title read");
            Eq(cfg.SampleRate, "48000", "missing key falls back to default");
            Eq(cfg.In1Gain, 1.0, "in1_gain defaults to 1.0 with no second input");

            cfg.Device2Id = "{0.0.1.0}.{abc}";
            Eq(cfg.In1Gain, 0.5, "in1_gain drops to 0.5 once a second input is set");

            cfg.SndVolume = 42;
            cfg.Save();

            string saved = File.ReadAllText(path);
            Check(saved.Contains("future_key"), "unknown key survives a save");
            Check(!saved.Contains("auto_split_mins"), "migrated key is not written back");

            var reloaded = new Config(path);
            Eq(reloaded.SndVolume, 42, "saved value reloads");
            Eq(reloaded.AutoSplitSecs, 420, "migrated value persisted");

            // A corrupt file must not throw.
            File.WriteAllText(path, "{ this is not json");
            var broken = new Config(path);
            Eq(broken.SampleRate, "48000", "corrupt config falls back to defaults");
        }

        private static void TestTimeText()
        {
            Section("Duration text");

            Eq(TimeText.Format(0), "0 seconds (off)", "zero formats as off");
            Eq(TimeText.Format(1), "1 second", "singular second");
            Eq(TimeText.Format(60), "1 minute", "singular minute");
            Eq(TimeText.Format(3600), "1 hour", "singular hour");
            Eq(TimeText.Format(5400), "1 hour, 30 minutes", "hour plus minutes");
            Eq(TimeText.Format(3661), "1 hour, 1 minute, 1 second", "all three units");
            Eq(TimeText.Format(7325), "2 hours, 2 minutes, 5 seconds", "plurals");
            Eq(TimeText.Format(86400), "24 hours", "a full day");

            Eq(TimeText.Parse("90"), 90, "bare number is seconds");
            Eq(TimeText.Parse("90s"), 90, "suffix s");
            Eq(TimeText.Parse("5m"), 300, "suffix m");
            Eq(TimeText.Parse("2h"), 7200, "suffix h");
            Eq(TimeText.Parse("1h30m"), 5400, "compound with no spaces");
            Eq(TimeText.Parse("1 hour, 30 minutes"), 5400, "spoken form parses back");
            Eq(TimeText.Parse("0 seconds (off)"), 0, "off form parses back to zero");
            Eq(TimeText.Parse("1h 30"), 5400, "bare number after hours means minutes");
            Eq(TimeText.Parse("5m 30"), 330, "bare number after minutes means seconds");
            Eq(TimeText.Parse(""), 0, "empty is zero");
            Eq(TimeText.Parse("abc"), 0, "no digits is zero");

            // Round-tripping every formatted value is the property that matters:
            // whatever the field displays must parse back to the same number.
            foreach (int v in new[] { 0, 1, 7, 59, 60, 61, 119, 3599, 3600, 3601, 5400, 7325, 86399, 86400, 2000000 })
                Eq(TimeText.Parse(TimeText.Format(v)), v, "round-trip " + v);

            Eq(TimeText.Normalize("90m", 0, 86400), "1 hour, 30 minutes", "normalise rewrites shorthand");
            Eq(TimeText.Normalize("999999", 0, 3600), "1 hour", "normalise clamps to max");
        }

        private static void TestPercentAndNaming()
        {
            Section("Percent and file naming");

            Eq(PercentText.Format(15), "15 percent", "percent formatting");
            Eq(PercentText.Parse("15 percent", 100), 15, "percent parses back");
            Eq(PercentText.Parse("", 77), 77, "empty percent uses fallback");
            Eq(PercentText.Parse("abc", 77), 77, "non-numeric percent uses fallback");
            foreach (int v in new[] { 1, 15, 50, 100 })
                Eq(PercentText.Parse(PercentText.Format(v), -1), v, "percent round-trip " + v);

            Eq(Naming.SanitizePrefix("  My*Rec:ord/ing?  "), "MyRecording", "illegal characters stripped");
            Eq(Naming.SanitizePrefix(""), "", "empty prefix stays empty");
            Eq(Naming.SanitizePrefix("Interview 12"), "Interview 12", "spaces and digits kept");
            Eq(Naming.Timestamp(new DateTime(2026, 8, 26, 14, 3, 9)), "20260826_140309", "timestamp format");
        }

        private static void TestRouting()
        {
            Section("Channel routing");

            // Two frames of interleaved stereo: L=0.8/0.4, R=-0.6/0.2
            var src = new[] { 0.8f, -0.6f, 0.4f, 0.2f };
            var dst = new float[8];

            Recorder.ApplyRouting(src, 2, "Both Channels", 2, 1.0f, dst);
            Eq(dst[0], 0.8f, "stereo both: L preserved");
            Eq(dst[1], -0.6f, "stereo both: R preserved");
            Eq(dst[3], 0.2f, "stereo both: second frame R");

            Recorder.ApplyRouting(src, 2, "Left Channel Only", 2, 1.0f, dst);
            Eq(dst[0], 0.8f, "stereo left-only: L on left");
            Eq(dst[1], 0f, "stereo left-only: right silent");

            Recorder.ApplyRouting(src, 2, "Right Channel Only", 2, 1.0f, dst);
            Eq(dst[0], 0f, "stereo right-only: left silent");
            Eq(dst[1], -0.6f, "stereo right-only: R on right");

            Recorder.ApplyRouting(src, 2, "Both Channels", 1, 1.0f, dst);
            Eq(dst[0], (0.8f + -0.6f) / 2f, "mono both: average of L and R");
            Eq(dst[1], (0.4f + 0.2f) / 2f, "mono both: second frame average");

            Recorder.ApplyRouting(src, 2, "Left Channel Only", 1, 1.0f, dst);
            Eq(dst[0], 0.8f, "mono left-only takes L");

            Recorder.ApplyRouting(src, 2, "Right Channel Only", 1, 1.0f, dst);
            Eq(dst[0], -0.6f, "mono right-only takes R");

            Recorder.ApplyRouting(src, 2, "Both Channels", 2, 0.5f, dst);
            Eq(dst[0], 0.4f, "gain applied");
        }

        private static void TestDurationUnits()
        {
            Section("Duration units");

            // The decomposition picks the largest unit that divides exactly, so
            // a two hour split reads as "2 Hours" rather than "7200 Seconds".
            Eq(DurationField.Decompose(0), (0, 0), "zero is 0 seconds");
            Eq(DurationField.Decompose(45), (45, 0), "45 stays in seconds");
            Eq(DurationField.Decompose(60), (1, 1), "60 becomes 1 minute");
            Eq(DurationField.Decompose(90), (90, 0), "90 stays in seconds, not 1.5 minutes");
            Eq(DurationField.Decompose(300), (5, 1), "300 becomes 5 minutes");
            Eq(DurationField.Decompose(3600), (1, 2), "3600 becomes 1 hour");
            Eq(DurationField.Decompose(5400), (90, 1), "5400 becomes 90 minutes, not 1.5 hours");
            Eq(DurationField.Decompose(7200), (2, 2), "7200 becomes 2 hours");
            Eq(DurationField.Decompose(86400), (24, 2), "a full day becomes 24 hours");
            Eq(DurationField.Decompose(3661), (3661, 0), "an awkward value stays in seconds");
            Eq(DurationField.Decompose(-5), (0, 0), "a negative duration clamps to zero");

            // Every decomposition must multiply back to the original value,
            // otherwise a saved duration would drift each time it is reopened.
            int[] units = { 1, 60, 3600 };
            foreach (int v in new[] { 0, 1, 30, 59, 60, 61, 120, 299, 300, 3599, 3600, 5400, 7200, 86400, 2000000 })
            {
                var (q, u) = DurationField.Decompose(v);
                Eq(q * units[u], v, "round-trip " + v + " seconds");
            }

            Eq(DurationField.UnitNames.Length, 3, "three unit choices");
            Eq(DurationField.UnitNames[0], "Seconds", "first unit is Seconds");
            Eq(DurationField.UnitNames[2], "Hours", "last unit is Hours");
        }

        private static void TestNotificationText()
        {
            Section("Notification text");

            Eq(Notifier.Flatten("one\r\ntwo\nthree"), "one two three", "newlines collapse to spaces");
            Eq(Notifier.Flatten("a    b"), "a b", "runs of spaces collapse");
            Eq(Notifier.Flatten("  padded  "), "padded", "surrounding whitespace trimmed");
            Eq(Notifier.Flatten(""), "", "empty stays empty");

            Eq(Notifier.Fit("short", 50), "short", "text within the limit is untouched");

            string longText = new string('a', 40) + " " + new string('b', 40);
            string fitted = Notifier.Fit(longText, 50);
            Check(fitted.Length <= 50, "truncated text respects the limit (" + fitted.Length + ")");
            Check(fitted.EndsWith("…", StringComparison.Ordinal), "truncation is marked with an ellipsis");
            Check(!fitted.Contains("bbbb", StringComparison.Ordinal), "truncation broke on the word boundary");

            // A single unbroken run has no word boundary to use.
            string unbroken = new string('x', 100);
            string hard = Notifier.Fit(unbroken, 20);
            Check(hard.Length <= 20, "unbreakable text is hard-cut to the limit");

            // The real messages must fit the shell's buffers without cutting.
            var messages = new[]
            {
                "Recording has begun.",
                "File safely saved to disk.",
                "Saved, but 12 blocks dropped and 34 silence blocks substituted.",
                "Saved with warnings. Please verify the recording.",
                "File finalization failed. The recording may be incomplete.",
                "Stopped: maximum recording length reached.",
                "Drive D: disconnected. Recording stopped.",
                "Microphone 1 disconnected. Continuing on the remaining input.",
                "Microphone 2 disconnected. Recording stopped.",
                "Split 12 started",
                "Recording failed.",
            };
            foreach (string m in messages)
                Check(m.Length <= Notifier.MaxBody && m == Notifier.Fit(Notifier.Flatten(m), Notifier.MaxBody),
                    "notification body fits uncut: \"" + m + "\"");

            foreach (string t in new[]
            {
                "Recording Started", "Recording Saved", "File Split", "Max Length Reached",
                "Drive Disconnected", "Microphone Disconnected", "Error",
            })
                Check(t.Length <= Notifier.MaxTitle, "notification title fits uncut: \"" + t + "\"");
        }

        private static void TestRf64(string dir)
        {
            Section("RF64 writer");

            string path = Path.Combine(dir, "test24.wav");
            const int rate = 48000, channels = 2, bits = 24, frames = 1000;

            using (var w = new Rf64Writer(path, rate, channels, bits))
            {
                var block = new float[frames * channels];
                for (int i = 0; i < block.Length; i++)
                    block[i] = MathF.Sin(i * 0.01f);
                w.Write(block, 0, block.Length);
                Eq(w.Frames, (long)frames, "frame count tracked");
                w.Close();
            }

            var bytes = File.ReadAllBytes(path);
            long expectedData = (long)frames * channels * 3;

            Eq(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), "RF64", "RF64 magic");
            Eq(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4)), 0xFFFFFFFFu, "riff size is the -1 sentinel");
            Eq(System.Text.Encoding.ASCII.GetString(bytes, 8, 4), "WAVE", "WAVE signature");
            Eq(System.Text.Encoding.ASCII.GetString(bytes, 12, 4), "ds64", "ds64 chunk first");
            Eq(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)), 28u, "ds64 chunk size");
            Eq((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20)), bytes.Length - 8L, "ds64 riff size");
            Eq((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(28)), expectedData, "ds64 data size");
            Eq((long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(36)), (long)frames, "ds64 sample count");
            Eq(System.Text.Encoding.ASCII.GetString(bytes, 48, 4), "fmt ", "fmt chunk offset");
            Eq(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(56)), (ushort)1, "PCM format tag");
            Eq(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(58)), (ushort)channels, "channel count");
            Eq(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(60)), (uint)rate, "sample rate");
            Eq(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(68)), (ushort)(channels * 3), "block align");
            Eq(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(70)), (ushort)bits, "bit depth");

            // These are the exact offsets the Python repair routine seeks to.
            Eq(System.Text.Encoding.ASCII.GetString(bytes, 72, 4), "data", "data chunk at offset 72");
            Eq(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(76)), 0xFFFFFFFFu, "data size is the -1 sentinel");
            Eq(bytes.Length, (int)(80 + expectedData), "total file size");

            Check(WavFile.Verify(path, channels, rate), "written file verifies");
            Check(!WavFile.Verify(path, 1, rate), "verify rejects a channel mismatch");

            // Clipping at both rails, 16-bit.
            string clipPath = Path.Combine(dir, "clip16.wav");
            using (var w = new Rf64Writer(clipPath, 44100, 1, 16))
                w.Write(new[] { 2.0f, -2.0f, 0f }, 0, 3);

            var clip = File.ReadAllBytes(clipPath);
            Eq(BinaryPrimitives.ReadInt16LittleEndian(clip.AsSpan(80)), (short)32767, "positive clip");
            Eq(BinaryPrimitives.ReadInt16LittleEndian(clip.AsSpan(82)), (short)-32767, "negative clip");
            Eq(BinaryPrimitives.ReadInt16LittleEndian(clip.AsSpan(84)), (short)0, "silence stays zero");

            // 24-bit mono leaves an odd data length and must be padded.
            string oddPath = Path.Combine(dir, "odd24.wav");
            using (var w = new Rf64Writer(oddPath, 44100, 1, 24))
                w.Write(new[] { 0.5f, 0.25f, 0.125f }, 0, 3);
            Eq(new FileInfo(oddPath).Length % 2, 0L, "odd data length padded to an even file size");
            Check(WavFile.Verify(oddPath), "padded 24-bit mono file verifies");

            // 32-bit path.
            string p32 = Path.Combine(dir, "test32.wav");
            using (var w = new Rf64Writer(p32, 96000, 2, 32))
                w.Write(new[] { 1.0f, -1.0f }, 0, 2);
            var b32 = File.ReadAllBytes(p32);
            Eq(BinaryPrimitives.ReadInt32LittleEndian(b32.AsSpan(80)), int.MaxValue, "32-bit positive full scale");
            Eq(BinaryPrimitives.ReadInt32LittleEndian(b32.AsSpan(84)), -2147483647, "32-bit negative full scale");
            Check(WavFile.Verify(p32, 2, 96000), "32-bit file verifies");
        }

        private static void TestRepair(string dir)
        {
            Section("Crash repair");

            // A file whose sizes were never written back, the state a hard crash
            // leaves behind in the Python build.
            string path = Path.Combine(dir, "unfinalised.wav");
            const int frames = 500;
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var header = new byte[80];
                Ascii(header, 0, "RF64");
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 0xFFFFFFFF);
                Ascii(header, 8, "WAVE");
                Ascii(header, 12, "ds64");
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 28);
                Ascii(header, 48, "fmt ");
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), 16);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(56), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(58), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(60), 48000);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(64), 48000 * 6);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(68), 6);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(70), 24);
                Ascii(header, 72, "data");
                // sizes deliberately left at zero
                fs.Write(header);
                fs.Write(new byte[frames * 6]);
            }

            Check(!WavFile.Verify(path), "unfinalised file fails verification");
            Check(WavFile.Repair(path), "repair reports success");
            Check(WavFile.Verify(path, 2, 48000), "repaired file verifies");
            Check(!File.Exists(path + ".backup"), "backup removed after a successful repair");

            var repaired = File.ReadAllBytes(path);
            Eq((long)BinaryPrimitives.ReadUInt64LittleEndian(repaired.AsSpan(28)), (long)frames * 6,
                "repair restored the data size");
            Eq((long)BinaryPrimitives.ReadUInt64LittleEndian(repaired.AsSpan(36)), (long)frames,
                "repair restored the sample count");

            // Plain RIFF path.
            string riff = Path.Combine(dir, "unfinalised_riff.wav");
            using (var fs = new FileStream(riff, FileMode.Create, FileAccess.Write))
            {
                var h = new byte[44];
                Ascii(h, 0, "RIFF");
                Ascii(h, 8, "WAVE");
                Ascii(h, 12, "fmt ");
                BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(16), 16);
                BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(20), 1);
                BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(22), 2);
                BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(24), 44100);
                BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(28), 44100 * 4);
                BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(32), 4);
                BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(34), 16);
                Ascii(h, 36, "data");
                fs.Write(h);
                fs.Write(new byte[400]);
            }
            Check(WavFile.Repair(riff), "RIFF repair reports success");
            Check(WavFile.Verify(riff, 2, 44100), "repaired RIFF file verifies");

            // A file still being written must never be offered for repair.
            string live = Path.Combine(dir, "live.wav");
            using (var held = new FileStream(live, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                held.Write(new byte[100]);
                held.Flush();
                Check(MainWindow.IsInUse(live), "a file held open for writing is detected as in use");
            }
            Check(!MainWindow.IsInUse(live), "the same file is free once the writer closes it");
            Check(!MainWindow.IsInUse(Path.Combine(dir, "does-not-exist.wav")),
                "a missing file is not reported as in use");

            // A non-WAV file must be refused, not mangled.
            string junk = Path.Combine(dir, "junk.wav");
            File.WriteAllBytes(junk, new byte[200]);
            Check(!WavFile.Repair(junk), "repair refuses a file with no RIFF/RF64 magic");
        }

        private static void Ascii(byte[] buf, int offset, string s)
        {
            for (int i = 0; i < s.Length; i++) buf[offset + i] = (byte)s[i];
        }

        private static void TestSounds()
        {
            Section("Sound cues");

            // gen_sounds.py produces four 44 byte + 17640 byte files, i.e. 8820
            // mono 16-bit frames each. Regenerating them has to match.
            // The four original cues are still the defaults and must be
            // unchanged, so they are compared against gen_sounds.py output.
            var legacy = new[]
            {
                ("start", "Rising Sweep"),
                ("stop", "Falling Sweep"),
                ("pause", "Low Double Beep"),
                ("unpause", "High Double Beep"),
            };

            foreach (var (file, sound) in legacy)
            {
                var samples = SoundLibrary.Get(sound);
                Eq(samples.Length, 8820, sound + " length matches gen_sounds.py");
                Eq(samples[0], (short)0, sound + " starts at silence (fade in)");
                Check(Math.Abs((int)samples[samples.Length - 1]) < 100,
                    sound + " ends inside the fade-out (" + samples[samples.Length - 1] + ")");

                short peak = 0;
                foreach (short s in samples) if (Math.Abs((int)s) > Math.Abs((int)peak)) peak = s;
                Check(Math.Abs((int)peak) > 30000, sound + " reaches near full scale");

                CompareToReference(file, samples);
                Eq(Config.DefaultSoundFor(file), sound, file + " still defaults to " + sound);
            }

            // The gap in a double beep must be actual silence.
            Eq(SoundLibrary.Get("Low Double Beep")[3528 + 800], (short)0, "double beep gap is silent");

            // Every selectable sound must render cleanly: no clipping, a real
            // signal, and no click from starting mid-waveform.
            Eq(SoundLibrary.Names[0], SoundLibrary.None, "None is the first choice");
            Eq(SoundLibrary.Get(SoundLibrary.None).Length, 0, "None renders as no audio");

            foreach (string name in SoundLibrary.Names)
            {
                Check(SoundLibrary.IsKnown(name), name + " is recognised by IsKnown");
                if (name == SoundLibrary.None) continue;

                var s = SoundLibrary.Get(name);
                Check(s.Length > 0, name + " renders audio");
                Check(s.Length <= SoundLibrary.Rate, name + " is at most one second (" + s.Length + " samples)");

                short peak = 0;
                foreach (short v in s) if (Math.Abs((int)v) > Math.Abs((int)peak)) peak = v;
                Check(Math.Abs((int)peak) > 8000, name + " is loud enough to hear (peak " + peak + ")");
                Check(Math.Abs((int)peak) < 32767, name + " does not hit full scale (peak " + peak + ")");

                // A cue that starts or ends abruptly produces an audible click.
                Check(Math.Abs((int)s[0]) < 1500, name + " starts near silence (" + s[0] + ")");
                Check(Math.Abs((int)s[s.Length - 1]) < 1500, name + " ends near silence (" + s[s.Length - 1] + ")");
            }

            Check(!SoundLibrary.IsKnown("Nonexistent Sound"), "an unknown name is rejected");

            // No two cues may be interchangeable: with a library this size it
            // is easy for a generator tweak to collapse two entries into the
            // same sound, and a picker full of duplicates is useless.
            Eq(new List<string>(SoundLibrary.Names).Count,
                new HashSet<string>(SoundLibrary.Names, StringComparer.OrdinalIgnoreCase).Count,
                "every sound name is unique");

            var rendered = new List<(string Name, short[] Data)>();
            foreach (string n in SoundLibrary.Names)
            {
                if (n == SoundLibrary.None) continue;
                rendered.Add((n, SoundLibrary.Get(n)));
            }

            int identical = 0;
            string firstClash = null;
            for (int i = 0; i < rendered.Count; i++)
            {
                for (int j = i + 1; j < rendered.Count; j++)
                {
                    if (!SameAudio(rendered[i].Data, rendered[j].Data)) continue;
                    identical++;
                    firstClash ??= rendered[i].Name + " and " + rendered[j].Name;
                }
            }
            Check(identical == 0, "no two sounds are identical" +
                (firstClash != null ? " (" + firstClash + ")" : ""));

            Check(rendered.Count >= 60, "the library offers at least 60 sounds (" + rendered.Count + ")");
        }

        private static bool SameAudio(short[] a, short[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;

            // A hand-edited or stale config must not silence a cue.
            string probe = Path.Combine(Path.GetTempPath(), "arp_sound_probe.json");
            try
            {
                File.WriteAllText(probe, "{\"snd_start_sound\": \"Deleted Sound\"}");
                var cfg = new Config(probe);
                Eq(cfg.SoundFor("start"), "Rising Sweep", "an unknown sound name falls back to the default");

                cfg.SetSoundFor("start", "Soft Chime");
                Eq(cfg.SoundFor("start"), "Soft Chime", "a chosen sound is returned");
                cfg.Save();
                Eq(new Config(probe).SoundFor("start"), "Soft Chime", "the chosen sound persists");

                foreach (string ev in Config.SoundEvents)
                    Check(SoundLibrary.IsKnown(Config.DefaultSoundFor(ev)),
                        "default sound for " + ev + " exists in the library");
            }
            finally
            {
                try { File.Delete(probe); } catch { }
            }
        }

        private static void CompareToReference(string name, short[] generated)
        {
            if (string.IsNullOrEmpty(ReferenceSoundsDir)) return;
            string path = Path.Combine(ReferenceSoundsDir, name + ".wav");
            if (!File.Exists(path)) { Failures.Add("reference " + name + ".wav not found"); return; }

            var raw = File.ReadAllBytes(path);
            int frames = (raw.Length - 44) / 2;
            Eq(frames, generated.Length, name + " reference frame count");

            int mismatches = 0, firstBad = -1;
            for (int i = 0; i < Math.Min(frames, generated.Length); i++)
            {
                short expected = BinaryPrimitives.ReadInt16LittleEndian(raw.AsSpan(44 + i * 2));
                if (expected != generated[i])
                {
                    if (firstBad < 0) firstBad = i;
                    mismatches++;
                }
            }
            Check(mismatches == 0,
                name + " is sample-identical to gen_sounds.py output (" + mismatches +
                " mismatches, first at index " + firstBad + ")");
        }

        private static void TestUpdater(string dir)
        {
            Section("Updater");

            // Numeric comparison, so v1.10.0 beats v1.9.0 where a string
            // compare would get it backwards.
            Check(Updater.CompareVersions("v1.10.0", "v1.9.0") > 0, "v1.10.0 is newer than v1.9.0");
            Check(Updater.CompareVersions("v2.0.0", "v2.0.0") == 0, "identical versions compare equal");
            Check(Updater.CompareVersions("v2.0.1", "v2.0.0") > 0, "patch bump is newer");
            Check(Updater.CompareVersions("v1.9.9", "v2.0.0") < 0, "major bump is newer");
            Check(Updater.CompareVersions("2.1", "v2.0.5") > 0, "a missing v and short form still compare");
            Check(Updater.CompareVersions("v2.0", "v2.0.0") == 0, "absent components count as zero");
            Check(Updater.CompareVersions("garbage", "v0.0.1") < 0, "unparseable is treated as oldest");

            Eq(string.Join(".", Updater.ParseVersion("v3.4.5")), "3.4.5", "tag parses to components");

            // The checksum line has to be matched by architecture, or a build
            // could be verified against the wrong hash.
            const string sums =
                "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA  ArpRecorder-win-arm64.exe\n" +
                "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB  ArpRecorder-win-x64.exe";
            Eq(Updater.FindHashFor(sums, "win-arm64"), new string('A', 64), "arm64 hash selected");
            Eq(Updater.FindHashFor(sums, "win-x64"), new string('B', 64), "x64 hash selected");
            Check(Updater.FindHashFor(sums, "win-x86") == null, "a missing architecture yields no hash");
            Check(Updater.FindHashFor("", "win-x64") == null, "an empty checksum file yields no hash");
            Check(Updater.FindHashFor("not a checksum line", "win-x64") == null, "junk yields no hash");

            // Release notes are read aloud, so markdown symbols must go.
            string notes = Updater.CleanNotes("## Heading\n- Fixed **a thing**\n\n* Added `stuff`\n");
            Check(!notes.Contains('#') && !notes.Contains('*') && !notes.Contains('`'),
                "markdown symbols stripped from release notes");
            Check(notes.Contains("Fixed a thing"), "release note text survives cleaning");
            Check(!notes.Contains("\n\n"), "blank lines removed from release notes");

            // Leftovers from an interrupted update must be swept away, and
            // nothing else in the folder may be touched.
            string sweep = Path.Combine(dir, "sweep");
            Directory.CreateDirectory(sweep);
            File.WriteAllText(Path.Combine(sweep, "ArpRecorder.exe"), "current");
            File.WriteAllText(Path.Combine(sweep, "ArpRecorder.exe.old-update"), "previous");
            File.WriteAllText(Path.Combine(sweep, "ArpRecorder.exe.new"), "staged");
            File.WriteAllText(Path.Combine(sweep, "recording.wav"), "keep me");

            int remaining = Updater.CleanupLeftovers(sweep);
            Eq(remaining, 0, "sweep reports nothing left behind");
            Check(!File.Exists(Path.Combine(sweep, "ArpRecorder.exe.old-update")), "old executable deleted");
            Check(!File.Exists(Path.Combine(sweep, "ArpRecorder.exe.new")), "staged download deleted");
            Check(File.Exists(Path.Combine(sweep, "ArpRecorder.exe")), "the live executable is kept");
            Check(File.Exists(Path.Combine(sweep, "recording.wav")), "unrelated files are untouched");

            Eq(Directory.GetFiles(sweep).Length, 2, "only the executable and the recording remain");
            Eq(Updater.CleanupLeftovers(sweep), 0, "sweeping a clean folder is harmless");

            // The asset name the updater looks for must match what CI publishes.
            Check(Updater.ArchSuffix is "win-arm64" or "win-x64" or "win-x86",
                "architecture suffix is a known value (" + Updater.ArchSuffix + ")");
            Check(("ArpRecorder-" + Updater.ArchSuffix + ".exe")
                    .Contains(Updater.ArchSuffix, StringComparison.OrdinalIgnoreCase),
                "published asset name carries the architecture the updater matches on");
        }

        private static void TestTemplates()
        {
            Section("Dialog templates");

            var cfg = new Config(Path.Combine(Path.GetTempPath(), "arp_template_probe.json"));
            var templates = new (string Name, byte[] Data)[]
            {
                ("MainWindow", Build(new MainWindow(cfg))),
                ("Settings", Build(new SettingsDialog(cfg, new List<AudioDevice>()))),
                ("Notifications", Build(new NotificationsDialog(cfg))),
                ("Sounds", Build(new SoundsDialog(cfg))),
                ("Channels", Build(new ChannelsDialog(cfg))),
                ("Repair", Build(new RepairDialog(@"C:\x.wav"))),
                ("Update", Build(new UpdateDialog("v1", "v2", "one\ntwo"))),
                ("Confirm", Build(new ConfirmDialog("t", "m", "a", "r"))),
            };

            foreach (var (name, data) in templates)
            {
                Check(data.Length > 32, name + " template is non-empty");

                // DLGTEMPLATE: style, exStyle, item count, then x/y/cx/cy.
                ushort itemCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
                short cx = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(14));
                short cy = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(16));
                Check(itemCount > 0, name + " declares controls");
                Check(cx > 0 && cy > 0, name + " has a positive size");
                Check(cy <= 420, name + " fits a 768px-tall screen (" + cy + " dlu)");

                uint style = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0));
                Check((style & Win32.DS_SETFONT) != 0, name + " sets a dialog font");
            }

            try { File.Delete(Path.Combine(Path.GetTempPath(), "arp_template_probe.json")); } catch { }
        }

        private static byte[] Build(DialogBase d) => d.TemplateForTest();
    }
}
