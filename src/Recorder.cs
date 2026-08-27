using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Arp
{
    internal enum FinalizationStatus
    {
        Recording,
        Success,
        ClosedWithWarnings,
        FinalizationFailed,
    }

    internal sealed class RecordingSession
    {
        public string SessionId;
        public readonly ManualResetEventSlim StopEvent = new(false);
        public int DroppedBlocks;
        public int SilenceBlocks;
        public int FileSplits;
        public string ErrorState;
        public FinalizationStatus Finalization = FinalizationStatus.Recording;
        public readonly Stopwatch Clock = Stopwatch.StartNew();
        public DateTime StartedAt = DateTime.Now;
    }

    /// <summary>
    /// One capture device feeding fixed-size blocks into a bounded queue, the
    /// direct equivalent of the Python build's reader_thread.
    /// </summary>
    internal sealed class InputReader
    {
        private readonly string _deviceId;
        private readonly bool _loopback;
        private readonly int _sampleRate;
        private readonly int _blockFrames;
        private readonly RecordingSession _session;
        private readonly Stopwatch _clock;
        private Thread _thread;
        private volatile bool _active = true;

        public readonly BlockingCollection<(double Ts, float[] Data, int Length)> Queue =
            new(new ConcurrentQueue<(double, float[], int)>(), 100);

        public volatile bool Failed;
        public volatile bool Ready;
        public string LastError;
        public int Number { get; }

        public InputReader(int number, string deviceId, bool loopback, int sampleRate, int blockFrames,
            RecordingSession session, Stopwatch clock)
        {
            Number = number;
            _deviceId = deviceId;
            _loopback = loopback;
            _sampleRate = sampleRate;
            _blockFrames = blockFrames;
            _session = session;
            _clock = clock;
        }

        public bool IsAlive => _thread != null && _thread.IsAlive;

        public void Start()
        {
            _thread = new Thread(Run) { IsBackground = true, Name = "Reader" + Number };
            _thread.Start();
        }

        public void Stop() => _active = false;

        public bool Join(int ms) => _thread == null || _thread.Join(ms);

        private void Run()
        {
            Wasapi.CaptureStream stream = null;

            // Each reader activates its own IAudioClient, so this thread needs
            // its own COM apartment; without it CoCreateInstance fails with
            // CO_E_NOTINITIALIZED and the input never opens.
            int coHr = Wasapi.CoInitializeEx(IntPtr.Zero, Wasapi.COINIT_MULTITHREADED);
            bool coInitialized = coHr >= 0;

            try
            {
                // Always capture stereo; downmix and routing happen in the
                // writer, exactly like mic.recorder(channels=2) upstream.
                stream = Wasapi.CaptureStream.Open(_deviceId, _loopback, _sampleRate, 2);

                // Ready means "the device opened", not "the device produced
                // audio". A loopback endpoint with nothing playing delivers no
                // packets at all, so gating the writer on a first block would
                // stall the whole recording on an input that may never speak.
                // The writer substitutes silence for a quiet input instead.
                Ready = true;

                int blockFloats = _blockFrames * 2;
                var accum = new float[blockFloats];
                int filled = 0;
                bool discardedFirst = false;

                // When the device signals us there is nothing to tune: the wait
                // ends the moment audio is ready. The timeout only bounds how
                // long a stop request can take to notice. Polling devices get a
                // wait of about half a block, comfortably inside the writer's
                // silence-substitution deadline and the one second ring buffer,
                // while waking far less than a fixed short poll would.
                int waitMs = stream.IsEventDriven
                    ? 200
                    : Math.Clamp((_blockFrames * 500) / _sampleRate, 5, 30);

                Log.Info("Reader " + Number + (stream.IsEventDriven
                    ? " is event driven." : " is polling every " + waitMs + " ms."));

                while (_active && !_session.StopEvent.IsSet)
                {
                    int got = stream.Read(accum, filled, blockFloats - filled);
                    filled += got;

                    if (filled < blockFloats)
                    {
                        if (got == 0) stream.WaitForData(waitMs);
                        continue;
                    }

                    filled = 0;

                    if (!discardedFirst)
                    {
                        // Drop one block to flush WASAPI start-up artefacts.
                        discardedFirst = true;
                        continue;
                    }

                    var buf = ArrayPool<float>.Shared.Rent(blockFloats);
                    Array.Copy(accum, buf, blockFloats);

                    if (!Queue.TryAdd((_clock.Elapsed.TotalSeconds, buf, blockFloats), 1000))
                    {
                        Interlocked.Increment(ref _session.DroppedBlocks);
                        ArrayPool<float>.Shared.Return(buf);
                    }
                }
            }
            catch (Exception e)
            {
                Failed = true;
                LastError = e.Message;
                Log.Error("Reader " + Number + " error: " + e.Message);
            }
            finally
            {
                Ready = true; // never leave a startup wait hanging
                try { stream?.Dispose(); } catch { }
                try { Queue.CompleteAdding(); } catch { }
                if (coInitialized) Wasapi.CoUninitialize();
                Log.Info("Reader " + Number + " thread shutting down");
            }
        }
    }

    internal sealed class Recorder
    {
        private readonly Config _cfg;
        private RecordingSession _session;
        private Thread _writer;
        private InputReader _r1, _r2;

        private long _totalFramesWritten;
        private volatile string _currentFilename;
        private volatile bool _isPaused;
        private volatile bool _needsSplit;
        private volatile int _splitCount = 1;

        public Action<string> OnError;
        public Action OnSplit;
        public Action<int, bool> OnMicDisconnected;

        /// <summary>
        /// Raised when an input stops delivering audio for two seconds, and
        /// again with false when it starts delivering again. This is a warning
        /// only: the recording keeps running and silence is written for the
        /// quiet input, so a transient driver hiccup costs a gap rather than
        /// the rest of the session.
        /// </summary>
        public Action<int, bool> OnStallChanged;

        public string SessionFolder { get; private set; }
        public string CurrentFilename => _currentFilename;
        public long TotalFramesWritten => Interlocked.Read(ref _totalFramesWritten);
        public int SplitCount => _splitCount;
        public RecordingSession Session => _session;

        public bool IsPaused
        {
            get => _isPaused;
            set => _isPaused = value;
        }

        public Recorder(Config cfg) => _cfg = cfg;

        public bool WriterAlive => _writer != null && _writer.IsAlive;

        public bool ReadersAlive => (_r1?.IsAlive ?? false) || (_r2?.IsAlive ?? false);

        public void RequestSplit() => _needsSplit = true;

        public void Stop()
        {
            _session?.StopEvent.Set();
            _r1?.Stop();
            _r2?.Stop();
        }

        /// <summary>
        /// Prepares the session folder and first file name, then launches the
        /// reader and writer threads. Throws if setup fails, leaving nothing
        /// running, so the caller can report and stay in the stopped state.
        /// </summary>
        public void Start(AudioDevice mic1, AudioDevice mic2, int sampleRate, int channels, int bitDepth,
            int bufferFrames, string prefix)
        {
            int splitSecs = _cfg.AutoSplitSecs;
            string sessionId = Naming.Timestamp(DateTime.Now);

            _session = new RecordingSession { SessionId = sessionId };
            _splitCount = 1;
            _needsSplit = false;
            _isPaused = false;
            Interlocked.Exchange(ref _totalFramesWritten, 0);

            if (_cfg.GroupSplits && splitSecs > 0)
            {
                string folderName = string.IsNullOrEmpty(prefix) ? sessionId : prefix + "_" + sessionId;
                SessionFolder = Path.Combine(_cfg.SaveFolder, folderName);
            }
            else
            {
                SessionFolder = _cfg.SaveFolder;
            }
            Directory.CreateDirectory(SessionFolder);

            string baseName = Path.Combine(SessionFolder,
                string.IsNullOrEmpty(prefix) ? sessionId : prefix + "_" + sessionId);
            string partStr = splitSecs > 0 ? "_part" + _splitCount.ToString("D4", CultureInfo.InvariantCulture) : "";

            string candidate = baseName + partStr + ".wav";
            int counter = 1;
            while (File.Exists(candidate))
            {
                candidate = baseName + "_" + counter + partStr + ".wav";
                counter++;
            }
            _currentFilename = candidate;

            Log.Info("Starting recording session: " + sessionId);
            Log.Info(string.Format(CultureInfo.InvariantCulture,
                "Sample Rate: {0}, Channels: {1}, Bits: {2}, Buffer: {3}", sampleRate, channels, bitDepth, bufferFrames));
            Log.Info("Mic 1: " + mic1.Id);
            if (mic2 != null) Log.Info("Mic 2: " + mic2.Id);

            _r1 = new InputReader(1, mic1.Id, mic1.IsLoopback, sampleRate, bufferFrames, _session, _session.Clock);
            _r2 = mic2 == null
                ? null
                : new InputReader(2, mic2.Id, mic2.IsLoopback, sampleRate, bufferFrames, _session, _session.Clock);

            _writer = new Thread(() => WriteLoop(sampleRate, channels, bitDepth, bufferFrames, prefix, mic2 != null))
            {
                IsBackground = true,
                Name = "Writer",
            };

            _r1.Start();
            _r2?.Start();
            _writer.Start();
        }

        private void WriteLoop(int sr, int ch, int bitDepth, int bufferFrames, string prefix, bool hasMic2)
        {
            Rf64Writer file = null;
            var session = _session;
            string in1Route = _cfg.In1Route;
            string in2Route = _cfg.In2Route;
            float gain1 = (float)_cfg.In1Gain;
            float gain2 = (float)_cfg.In2Gain;
            bool continueOnDisconnect = _cfg.ContinueOnMicDisconnect;

            int blockFloats = bufferFrames * 2;
            var routed1 = new float[bufferFrames * ch];
            var routed2 = new float[bufferFrames * ch];
            var silence = new float[blockFloats];

            try
            {
                // Wait for every input to finish opening, the equivalent of the
                // Barrier in the Python build. Readers set Ready on open or on
                // failure, so this cannot outlive a device that never speaks.
                var startWait = Stopwatch.StartNew();
                while (startWait.ElapsedMilliseconds < 10000)
                {
                    if (session.StopEvent.IsSet) return;
                    bool ready1 = _r1.Ready, ready2 = _r2 == null || _r2.Ready;
                    if (ready1 && ready2) break;
                    Thread.Sleep(10);
                }

                if (!_r1.Ready || (_r2 != null && !_r2.Ready))
                {
                    Log.Error("Timed out waiting for audio devices to open.");
                    OnError?.Invoke("Timed out waiting for the audio devices to start.");
                    return;
                }

                if (_r1.Failed || (_r2 != null && _r2.Failed))
                {
                    string detail = _r1.Failed ? _r1.LastError : _r2.LastError;
                    OnError?.Invoke("Failed to initialize audio devices.\n\n" + detail);
                    return;
                }

                file = new Rf64Writer(_currentFilename, sr, ch, bitDepth);

                bool mic1Failed = false, mic2Failed = false, abort = false;
                bool mic1Silent = false, mic2Silent = false;
                bool mic1Stalled = false, mic2Stalled = false;

                double blockDuration = (double)bufferFrames / sr;
                double now = Now(session);
                double lastFlush = now, lastFrameTime = now;
                double lastMic1Real = now, lastMic2Real = now;
                double lastJournal = now;

                while (!session.StopEvent.IsSet && !abort)
                {
                    int s1 = _r1.Queue.Count;
                    int s2 = _r2?.Queue.Count ?? 0;

                    if (s1 > 0) mic1Silent = false;
                    if (s2 > 0) mic2Silent = false;

                    if (s1 == 0 && s2 == 0)
                    {
                        Thread.Sleep(10);

                        if (!_r1.IsAlive && !mic1Failed)
                        {
                            Log.Warn("Mic 1 thread died unexpectedly");
                            mic1Failed = true;
                            bool willContinue = continueOnDisconnect && _r2 != null && !mic2Failed;
                            OnMicDisconnected?.Invoke(1, willContinue);
                            if (!willContinue) break;
                        }
                        if (_r2 != null && !_r2.IsAlive && !mic2Failed)
                        {
                            Log.Warn("Mic 2 thread died unexpectedly");
                            mic2Failed = true;
                            bool willContinue = continueOnDisconnect && !mic1Failed;
                            OnMicDisconnected?.Invoke(2, willContinue);
                            if (!willContinue) break;
                        }

                        // Below the deadline there is nothing to do but spin;
                        // past it, fall through and let the timed dequeue insert
                        // silence so the timeline keeps advancing.
                        if (Now(session) - lastFrameTime <= blockDuration + 0.05) continue;
                        lastFrameTime = Now(session);
                    }

                    float[] data1;
                    int len1;
                    double ts1;
                    int t1Timeout = mic1Silent ? 0 : (int)((blockDuration + 0.05) * 1000);
                    if (_r1.Queue.TryTake(out var item1, t1Timeout))
                    {
                        ts1 = item1.Ts;
                        data1 = item1.Data;
                        len1 = item1.Length;
                        mic1Silent = false;
                    }
                    else
                    {
                        mic1Silent = true;
                        ts1 = Now(session);
                        data1 = silence;
                        len1 = blockFloats;
                        Interlocked.Increment(ref session.SilenceBlocks);
                    }

                    float[] data2 = null;
                    int len2 = 0;
                    bool data2Pooled = false;
                    if (_r2 != null)
                    {
                        int t2Timeout = mic2Silent ? 0 : (int)((blockDuration + 0.05) * 1000);
                        while (true)
                        {
                            if (_r2.Queue.TryTake(out var item2, t2Timeout))
                            {
                                // Discard input 2 blocks that lag input 1 by more
                                // than half a second: two devices on independent
                                // clocks drift, and dropping is how the Python
                                // build keeps them aligned.
                                if (item2.Ts < ts1 - 0.5)
                                {
                                    ArrayPool<float>.Shared.Return(item2.Data);
                                    continue;
                                }
                                data2 = item2.Data;
                                len2 = item2.Length;
                                data2Pooled = true;
                                mic2Silent = false;
                                break;
                            }

                            mic2Silent = true;
                            data2 = silence;
                            len2 = blockFloats;
                            Interlocked.Increment(ref session.SilenceBlocks);
                            break;
                        }
                    }

                    try
                    {
                        lastFrameTime = Now(session);
                        double curr = lastFrameTime;

                        if (!mic1Silent) lastMic1Real = curr;
                        if (_r2 != null && !mic2Silent) lastMic2Real = curr;

                        // A stall is reported, not acted on. Recovery is
                        // reported too, which is why the flag is cleared rather
                        // than latched: an input that comes back should say so.
                        if (curr - lastMic1Real > 2.0 && !mic1Stalled)
                        {
                            mic1Stalled = true;
                            Log.Warn("Mic 1 stalled: no blocks received for 2 seconds. Recording continues.");
                            OnStallChanged?.Invoke(1, true);
                        }
                        else if (curr - lastMic1Real <= 2.0 && mic1Stalled)
                        {
                            mic1Stalled = false;
                            Log.Info("Mic 1 recovered and is delivering audio again.");
                            OnStallChanged?.Invoke(1, false);
                        }

                        if (_r2 != null)
                        {
                            if (curr - lastMic2Real > 2.0 && !mic2Stalled)
                            {
                                mic2Stalled = true;
                                Log.Warn("Mic 2 stalled: no blocks received for 2 seconds. Recording continues.");
                                OnStallChanged?.Invoke(2, true);
                            }
                            else if (curr - lastMic2Real <= 2.0 && mic2Stalled)
                            {
                                mic2Stalled = false;
                                Log.Info("Mic 2 recovered and is delivering audio again.");
                                OnStallChanged?.Invoke(2, false);
                            }
                        }

                        if (_isPaused) continue;

                        int frames = len1 / 2;
                        ApplyRouting(data1, frames, in1Route, ch, gain1, routed1);
                        int outCount = frames * ch;

                        if (_r2 != null)
                        {
                            ApplyRouting(data2, len2 / 2, in2Route, ch, gain2, routed2);
                            for (int i = 0; i < outCount; i++)
                            {
                                float v = routed1[i] + routed2[i];
                                routed1[i] = v > 1f ? 1f : v < -1f ? -1f : v;
                            }
                        }

                        file.Write(routed1, 0, outCount);
                        Interlocked.Add(ref _totalFramesWritten, frames);

                        if (curr - lastFlush > 2.0)
                        {
                            file.Flush();
                            lastFlush = curr;
                            WriteJournal(sr, ch, bitDepth, false);
                            lastJournal = curr;
                        }

                        if (_needsSplit)
                        {
                            _needsSplit = false;

                            string drive = DriveRootOf(_cfg.SaveFolder);

                            try
                            {
                                file.Close();
                                Log.Info("Split file closed successfully: " + _currentFilename);
                            }
                            catch (Exception closeErr)
                            {
                                Log.Error("Failed to close split file: " + closeErr.Message);
                                OnError?.Invoke("Failed to close split file: " + closeErr.Message);
                                break;
                            }

                            if (drive != null && !IsSystemDrive(drive) && !Directory.Exists(drive))
                            {
                                OnError?.Invoke("Output drive disconnected during auto-split.");
                                break;
                            }

                            session.FileSplits++;
                            _splitCount++;

                            string timestamp = Naming.Timestamp(DateTime.Now);
                            string nextBase = Path.Combine(SessionFolder,
                                string.IsNullOrEmpty(prefix) ? timestamp : prefix + "_" + timestamp);
                            string partStr = "_part" + _splitCount.ToString("D4", CultureInfo.InvariantCulture);

                            string next = nextBase + partStr + ".wav";
                            int counter = 1;
                            while (File.Exists(next))
                            {
                                next = nextBase + "_" + counter + partStr + ".wav";
                                counter++;
                            }
                            _currentFilename = next;

                            try
                            {
                                file = new Rf64Writer(_currentFilename, sr, ch, bitDepth);
                                Log.Info("Successfully opened next split file: " + _currentFilename);
                            }
                            catch (Exception e)
                            {
                                Log.Error("Failed to open next split file: " + e.Message);
                                OnError?.Invoke("Failed to open next split file: " + e.Message);
                                file = null;
                                break;
                            }

                            Interlocked.Exchange(ref _totalFramesWritten, 0);
                            OnSplit?.Invoke();
                        }
                    }
                    finally
                    {
                        if (!ReferenceEquals(data1, silence)) ArrayPool<float>.Shared.Return(data1);
                        if (data2Pooled) ArrayPool<float>.Shared.Return(data2);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error("Worker died with exception: " + e.Message, e);
                session.ErrorState = e.Message;
                OnError?.Invoke("An error occurred during recording: " + e.Message);
            }
            finally
            {
                Finalize(file, sr, ch, bitDepth);
            }
        }

        private void Finalize(Rf64Writer file, int sr, int ch, int bitDepth)
        {
            var session = _session;
            _r1?.Stop();
            _r2?.Stop();

            if (file != null && !file.IsClosed)
            {
                string path = file.Path;
                try
                {
                    file.Close();
                    WriteJournal(sr, ch, bitDepth, true);
                    session.Finalization = FinalizationStatus.Success;

                    if (!WavFile.Verify(path, ch, sr))
                    {
                        session.Finalization = FinalizationStatus.ClosedWithWarnings;
                        Log.Warn("File verification warning for " + path);
                    }
                }
                catch (Exception closeErr)
                {
                    session.Finalization = FinalizationStatus.FinalizationFailed;
                    session.ErrorState = closeErr.Message;
                    Log.Error("Finalization failed: " + closeErr.Message, closeErr);
                }
            }
            else
            {
                session.Finalization = FinalizationStatus.Success;
                WriteJournal(sr, ch, bitDepth, true);
            }

            foreach (var r in new[] { _r1, _r2 })
            {
                if (r == null) continue;
                if (!r.Join(3000)) Log.Warn("Reader thread " + r.Number + " did not shut down within 3 seconds");
                DrainQueue(r);
            }
        }

        private static void DrainQueue(InputReader r)
        {
            while (r.Queue.TryTake(out var item))
            {
                try { ArrayPool<float>.Shared.Return(item.Data); } catch { }
            }
        }

        private static double Now(RecordingSession s) => s.Clock.Elapsed.TotalSeconds;

        /// <summary>
        /// Downmix and channel placement for one input, matching the Python
        /// build's apply_routing. Source is always interleaved stereo.
        /// </summary>
        internal static void ApplyRouting(float[] src, int frames, string route, int outChannels, float gain, float[] dst)
        {
            bool left = route == "Left Channel Only";
            bool right = route == "Right Channel Only";

            if (outChannels == 1)
            {
                for (int i = 0; i < frames; i++)
                {
                    float l = src[i * 2], r = src[i * 2 + 1];
                    dst[i] = (left ? l : right ? r : (l + r) * 0.5f) * gain;
                }
                return;
            }

            for (int i = 0; i < frames; i++)
            {
                float l = src[i * 2], r = src[i * 2 + 1];
                if (left)
                {
                    dst[i * 2] = l * gain;
                    dst[i * 2 + 1] = 0f;
                }
                else if (right)
                {
                    dst[i * 2] = 0f;
                    dst[i * 2 + 1] = r * gain;
                }
                else
                {
                    dst[i * 2] = l * gain;
                    dst[i * 2 + 1] = r * gain;
                }
            }
        }

        // ---- crash-recovery journal ----

        public string JournalPath =>
            Path.Combine(string.IsNullOrEmpty(SessionFolder) ? Config.AppDataDir : SessionFolder, "active_recording.json");

        private void WriteJournal(int sr, int ch, int bitDepth, bool closed)
        {
            try
            {
                string path = JournalPath;
                if (closed)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }

                var j = new JsonObject();
                j.Set("session_id", _session.SessionId);
                j.Set("start_time", _session.StartedAt.ToString("s", CultureInfo.InvariantCulture));
                j.Set("sample_rate", (double)sr);
                j.Set("channels", (double)ch);
                j.Set("bit_depth", (double)bitDepth);
                j.Set("current_file", _currentFilename);
                j.Set("frames_committed", (double)TotalFramesWritten);
                j.Set("last_flush", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
                j.Set("split_number", (double)_splitCount);
                j.Set("clean_close", false);

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, j.ToJson());
                File.Move(tmp, path, true);
            }
            catch
            {
                // Journalling is best effort and must never stop a recording.
            }
        }

        internal static string DriveRootOf(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return null;
            try
            {
                string root = Path.GetPathRoot(folder);
                return string.IsNullOrEmpty(root) ? null : root;
            }
            catch
            {
                return null;
            }
        }

        internal static bool IsSystemDrive(string root)
        {
            if (string.IsNullOrEmpty(root)) return true;
            return root.TrimEnd('\\', '/').Equals("C:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
