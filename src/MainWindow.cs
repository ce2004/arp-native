using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Arp
{
    internal sealed class MainWindow : DialogBase
    {
        private const int IdHeading = 3000;
        private const int IdExit = 3001;
        private const int IdSettings = 3002;
        private const int IdRecord = 3003;
        private const int IdPause = 3004;
        private const int IdOverview = 3005;
        private const int IdStats = 3006;

        private const uint WmInvoke = Win32.WM_APP + 1;

        private const int TimerLiveStats = 1;
        private const int TimerSplit = 2;
        private const int TimerMaxLen = 3;
        private const int TimerDisk = 4;
        private const int TimerShutdown = 5;
        private const int TimerAutoStart = 6;

        private readonly Config _cfg;
        private List<AudioDevice> _devices = new();
        private readonly ConcurrentQueue<Action> _uiQueue = new();

        private Recorder _rec;
        private DriveMonitor _driveMonitor;
        private AutoResumeWatcher _autoResume;

        private bool _isRecording;
        private bool _isPaused;
        private bool _shuttingDown;
        private bool _pendingClose;
        private bool _handlingDisconnect;
        private bool _notifyOnStop = true;
        private bool _statsVisible;
        private int _lastDiskWarningLevel;
        private int _shutdownPollCount;
        private int _splitCount = 1;
        private string _statusMsg = "Status: Ready";

        public MainWindow(Config cfg) => _cfg = cfg;

        protected override byte[] BuildTemplate()
        {
            uint style = Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.WS_MINIMIZEBOX |
                         Win32.DS_SETFONT | Win32.DS_CENTER;
            var b = new DialogBuilder(_cfg.WindowTitle, 270, 140, style,
                Win32.WS_EX_CONTROLPARENT | Win32.WS_EX_APPWINDOW);

            b.Label("Audio Recorder Dashboard", 5, 5, 260, 10, IdHeading);
            b.Button(IdExit, "E&xit", 5, 20, 62, 18);
            b.Button(IdSettings, "Se&ttings", 71, 20, 62, 18);
            b.Button(IdRecord, "&Start Recording", 137, 20, 62, 18);
            b.Button(IdPause, "&Pause", 203, 20, 62, 18);
            b.TextList(IdOverview, 5, 44, 260, 58);
            b.TextList(IdStats, 5, 108, 260, 24);
            return b.Build();
        }

        // ---- lifecycle ----

        protected override void OnInit()
        {
            Notifier.Attach(Hwnd);
            Notifier.AppName = _cfg.WindowTitle; // notifications follow the window title
            Speech.ShouldSpeak = () => !_cfg.SpeakInFocusOnly || IsForeground();

            PopulateDevices();

            Enable(IdPause, false);
            ShowStats(false);

            _driveMonitor = new DriveMonitor(() => _cfg.SaveFolder,
                drive => Post(() => HandleDriveDisconnect(drive)));
            _driveMonitor.Start();

            UpdateDashboard();

            string savedId = _cfg.DeviceId;
            if (!string.IsNullOrEmpty(savedId) && !_devices.Exists(d => d.Id == savedId))
            {
                Critical(
                    "CRITICAL: Your explicitly configured audio device is missing!\r\n\r\n" +
                    "The device matching ID:\r\n'" + savedId + "'\r\n\r\ncould not be found. " +
                    "It may be disconnected, powered off, or disabled in Windows.\r\n\r\n" +
                    "For safety and privacy, this application WILL NOT automatically fall back to another microphone.\r\n\r\n" +
                    "Action Required:\r\n" +
                    "1. Reconnect your device and restart this application, OR\r\n" +
                    "2. Open Settings and manually select a different input device.",
                    "Device Not Found");
            }

            CheckRecoveryJournal();

            if (_cfg.CheckUpdatesStartup) Updater.CheckOnStartup(Hwnd);

            if (_cfg.AutoStart)
            {
                int delay = _cfg.AutoStartDelay;
                if (delay > 0)
                {
                    _statusMsg = "Status: Auto-recording in " + delay + " seconds...";
                    UpdateDashboard();
                    Win32.SetTimer(Hwnd, (UIntPtr)TimerAutoStart, (uint)(delay * 1000), IntPtr.Zero);
                }
                else
                {
                    Win32.SetTimer(Hwnd, (UIntPtr)TimerAutoStart, 100, IntPtr.Zero);
                }
            }
        }

        private bool IsForeground()
        {
            IntPtr fg = Win32.GetForegroundWindow();
            return fg == Hwnd || Win32.GetActiveWindow() != IntPtr.Zero && fg == Win32.GetActiveWindow();
        }

        private void PopulateDevices()
        {
            try
            {
                _devices = Wasapi.EnumerateDevices();
            }
            catch (Exception e)
            {
                _devices = new List<AudioDevice>();
                Critical("Could not enumerate audio devices: " + e.Message, "Error");
            }
        }

        /// <summary>Runs an action on the UI thread; safe from any thread.</summary>
        private void Post(Action a)
        {
            _uiQueue.Enqueue(a);
            Win32.PostMessageW(Hwnd, WmInvoke, IntPtr.Zero, IntPtr.Zero);
        }

        protected override bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;

            switch (msg)
            {
                case WmInvoke:
                    while (_uiQueue.TryDequeue(out var a))
                    {
                        try { a(); }
                        catch (Exception e) { Log.Error("UI action failed: " + e.Message, e); }
                    }
                    return true;

                case Win32.WM_TIMER:
                    OnTimer((int)wParam);
                    return true;

                case Win32.WM_CLOSE:
                    HandleClose();
                    result = (IntPtr)1;
                    return true;

                case Win32.WM_QUERYENDSESSION:
                    HandleEndSession();
                    result = (IntPtr)1;
                    return true;
            }
            return false;
        }

        private void OnTimer(int id)
        {
            switch (id)
            {
                case TimerLiveStats: UpdateLiveStats(); break;
                case TimerSplit: if (_isRecording) _rec?.RequestSplit(); break;
                case TimerMaxLen:
                    Win32.KillTimer(Hwnd, (UIntPtr)TimerMaxLen);
                    ForceStopMaxLength();
                    break;
                case TimerDisk: CheckDiskSpace(); break;
                case TimerShutdown: PollSessionShutdown(); break;
                case TimerAutoStart:
                    Win32.KillTimer(Hwnd, (UIntPtr)TimerAutoStart);
                    StartRecording();
                    break;
            }
        }

        protected override bool OnCommand(int id, int code)
        {
            // Leaving one of the status lists is the moment it is safe to apply
            // an update that arrived while it was being read.
            if (code == Win32.LBN_KILLFOCUS && (id == IdOverview || id == IdStats))
            {
                FlushList(id);
                return true;
            }

            switch (id)
            {
                case IdExit:
                case Win32.IDCANCEL:
                    HandleClose();
                    return true;
                case IdSettings: OpenSettings(); return true;
                case IdRecord: ToggleRecording(); return true;
                case IdPause: TogglePause(); return true;
            }
            return false;
        }

        // ---- dashboard ----

        private void UpdateDashboard()
        {
            string devId = _cfg.DeviceId;
            var mic = _devices.Find(d => d.Id == devId);
            string micName = mic != null ? mic.Name : "None Selected (Will use default)";

            int split = _cfg.AutoSplitSecs;
            string overview =
                "Recording Device is set to: " + micName + "\n" +
                "Output Folder is set to: " + _cfg.SaveFolder + "\n" +
                "Auto-Recording is: " + (_cfg.AutoStart ? "On" : "Off") + "\n" +
                "Auto-Split is: " + (split > 0 ? TimeText.Format(split) : "Off") + "\n" +
                _statusMsg;

            SetList(IdOverview, overview);
        }

        private void ShowStats(bool visible)
        {
            _statsVisible = visible;
            IntPtr h = Win32.GetDlgItem(Hwnd, IdStats);
            if (h == IntPtr.Zero) return;

            // Hiding the control that currently has focus would leave focus
            // nowhere and the screen reader silent, so hand it to the record
            // button first.
            if (!visible && Win32.GetFocus() == h)
                Win32.SetFocus(Win32.GetDlgItem(Hwnd, IdRecord));

            Win32.ShowWindow(h, visible ? Win32.SW_SHOW : 0);
        }

        private readonly Dictionary<int, string> _listShown = new();
        private readonly Dictionary<int, string> _listPending = new();

        /// <summary>
        /// Updates one of the read-only lists, but never while it has keyboard
        /// focus.
        ///
        /// The live stats are rewritten once a second. Rebuilding a list box
        /// under a screen reader that is sitting in it makes it re-announce and
        /// loses the reading position, so an update that arrives while the user
        /// is in the control is held back and applied when focus leaves. The
        /// value is also skipped entirely when nothing actually changed.
        /// </summary>
        private void SetList(int id, string text)
        {
            if (_listShown.TryGetValue(id, out string current) && current == text)
            {
                _listPending.Remove(id);
                return;
            }

            IntPtr h = Win32.GetDlgItem(Hwnd, id);
            if (h != IntPtr.Zero && Win32.GetFocus() == h)
            {
                _listPending[id] = text;
                return;
            }

            _listPending.Remove(id);
            _listShown[id] = text;
            Win32.ListSetLines(Hwnd, id, text);
        }

        /// <summary>Applies an update that was deferred while the list was focused.</summary>
        private void FlushList(int id)
        {
            if (!_listPending.TryGetValue(id, out string text)) return;
            _listPending.Remove(id);
            _listShown[id] = text;
            Win32.ListSetLines(Hwnd, id, text);
        }

        private void SetStats(string text) => SetList(IdStats, text);

        private string StatsText() => Win32.ListGetAll(Hwnd, IdStats);

        private void UpdateLiveStats()
        {
            if (!_isRecording || _rec == null) return;
            try
            {
                int sr = ParseInt(_cfg.SampleRate, 48000);
                int ch = ParseInt(_cfg.Channels, 2);
                int bd = ParseInt(_cfg.BitDepth, 24);

                long frames = _rec.TotalFramesWritten;
                long seconds = sr > 0 ? frames / sr : 0;
                double bytes = (double)frames * ch * (bd / 8.0);
                double mb = bytes / (1024 * 1024);

                long m = seconds / 60, s = seconds % 60;
                string timeStr = "Recording time, " + (m > 0
                    ? m + " minutes, " + s + " seconds. "
                    : s + " seconds. ");

                string text;
                if (_cfg.AutoSplitSecs > 0)
                {
                    string splitInfo = _splitCount > 1 ? " (Split " + _splitCount + ")" : "";
                    text = timeStr + mb.ToString("F2", CultureInfo.InvariantCulture) +
                           " MB Storage used on current split" + splitInfo + ".";
                }
                else
                {
                    text = timeStr + mb.ToString("F2", CultureInfo.InvariantCulture) + " MB Storage used.";
                }
                SetStats(text);
            }
            catch
            {
            }
        }

        private static int ParseInt(string s, int fallback) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

        // ---- feedback helpers ----

        private void Speak(string msg) => Speech.Speak(msg);

        /// <summary>
        /// Notification bodies are kept to one short sentence on purpose. The
        /// shell clips a toast after two or three lines, so anything longer
        /// gets cut off mid-thought; the full wording goes to the dialog and to
        /// the screen reader instead, neither of which has that limit.
        /// </summary>
        private void Notify(string title, string msg, string settingKey, Notifier.Level level = Notifier.Level.Info)
        {
            if (!_cfg.NotifyEnabled(settingKey)) return;
            Notifier.Notify(title, msg, level);
        }

        private void PlaySound(string eventName)
        {
            if (!_cfg.SndEnabled(eventName)) return;
            Sounds.Play(_cfg.SoundFor(eventName), _cfg.SndVolume);
        }

        // ---- settings ----

        private void OpenSettings()
        {
            PopulateDevices();
            var dlg = new SettingsDialog(_cfg, _devices);
            if ((long)dlg.ShowModal(Hwnd) == 1)
            {
                Win32.SetWindowTextW(Hwnd, _cfg.WindowTitle);
                Notifier.AppName = _cfg.WindowTitle; // notifications follow the window title
                UpdateDashboard();
            }
        }

        // ---- recording ----

        private void ToggleRecording()
        {
            Win32.KillTimer(Hwnd, (UIntPtr)TimerAutoStart);
            if (_statusMsg.StartsWith("Status: Auto-recording", StringComparison.Ordinal))
            {
                _statusMsg = "Status: Ready";
                UpdateDashboard();
            }

            if (_isRecording) StopRecording();
            else StartRecording();
        }

        private void StartRecording()
        {
            if (_isRecording || _shuttingDown) return;

            string folder = _cfg.SaveFolder;
            if (string.IsNullOrEmpty(folder))
            {
                Critical("Please select an output directory in settings before recording.", "Output Directory Error");
                return;
            }

            string root = Recorder.DriveRootOf(folder);
            if (root != null && !Directory.Exists(root))
            {
                Warn("The output drive you configured is currently disconnected.\r\n\r\n" +
                     "Like your microphone settings, this app does not automatically default to another location " +
                     "to prevent lost files.\r\n\r\nPlease reconnect the drive or select a new output folder in Settings.",
                    "Output Drive Missing");
                return;
            }

            int sr = ParseInt(_cfg.SampleRate, 48000);
            int bd = ParseInt(_cfg.BitDepth, 24);
            int ch = ParseInt(_cfg.Channels, 2);
            int bufSize = _cfg.BufferSize;

            try
            {
                var di = new DriveInfo(root);
                double bytesPerHour = (double)sr * ch * (bd / 8.0) * 3600;
                if (di.AvailableFreeSpace < bytesPerHour * 0.25)
                    Warn("You have very little disk space left on this drive (less than 15 minutes of recording time)!",
                        "Low Disk Space");
            }
            catch (Exception e)
            {
                Log.Warn("Failed to check disk space: " + e.Message);
            }

            string devId = _cfg.DeviceId;
            if (string.IsNullOrEmpty(devId))
            {
                Warn("No audio device has been configured yet. Please open Settings and explicitly select an input device.",
                    "Warning");
                return;
            }

            var mic1 = _devices.Find(d => d.Id == devId);
            if (mic1 == null)
            {
                Warn("Input 1 is disconnected. Please reconnect it or open Settings and select a valid audio device.",
                    "Warning");
                return;
            }

            AudioDevice mic2 = null;
            string dev2Id = _cfg.Device2Id;
            if (dev2Id != "none" && !string.IsNullOrEmpty(dev2Id))
            {
                mic2 = _devices.Find(d => d.Id == dev2Id);
                if (mic2 == null)
                {
                    Warn("Input 2 is disconnected. Please reconnect or disable it in Settings.", "Warning");
                    return;
                }
            }

            if (bd != 16 && bd != 24 && bd != 32)
            {
                Critical("Unsupported bit depth: " + bd, "Audio Error");
                return;
            }

            string prefix = Naming.SanitizePrefix(_cfg.FilenamePrefix);

            try
            {
                _rec = new Recorder(_cfg)
                {
                    OnError = m => Post(() => HandleError(m)),
                    OnSplit = () => Post(OnSplitCompleted),
                    OnMicDisconnected = (n, cont) => Post(() => HandleMicDisconnect(n, cont)),
                    OnStallChanged = (n, stalled) => Post(() => HandleStallChanged(n, stalled)),
                };

                _isRecording = true;
                _isPaused = false;
                _splitCount = 1;
                _lastDiskWarningLevel = 0;

                Text(IdRecord, "S&top Recording");
                Enable(IdPause, true);
                Text(IdPause, "&Pause");
                Enable(IdSettings, false);

                _rec.Start(mic1, mic2, sr, ch, bd, bufSize, prefix);

                int splitSecs = _cfg.AutoSplitSecs;
                if (splitSecs > 0) Win32.SetTimer(Hwnd, (UIntPtr)TimerSplit, (uint)(splitSecs * 1000), IntPtr.Zero);

                int maxLen = _cfg.MaxLengthSecs;
                if (maxLen > 0) Win32.SetTimer(Hwnd, (UIntPtr)TimerMaxLen, (uint)(maxLen * 1000L), IntPtr.Zero);

                Win32.SetTimer(Hwnd, (UIntPtr)TimerDisk, 5000, IntPtr.Zero);

                SetStats(splitSecs > 0
                    ? "Recording time, 0 seconds. 0.00 MB Storage used on current split."
                    : "Recording time, 0 seconds. 0.00 MB Storage used.");
                ShowStats(true);
                Win32.SetTimer(Hwnd, (UIntPtr)TimerLiveStats, 1000, IntPtr.Zero);

                Speak("Recording started");
                _statusMsg = splitSecs > 0
                    ? "Status: Recording Split " + _splitCount + " to " + Path.GetFileName(_rec.CurrentFilename)
                    : "Status: Recording to " + Path.GetFileName(_rec.CurrentFilename);
                UpdateDashboard();

                PlaySound("start");
                Notify("Recording Started", "Audio recording has begun.", "notify_start_stop");
            }
            catch (Exception e)
            {
                _isRecording = false;
                Text(IdRecord, "&Start Recording");
                Enable(IdPause, false);
                Enable(IdSettings, true);
                ShowStats(false);
                Log.Error("Failed to start recording: " + e.Message, e);
                Critical("Failed to start recording.\r\n\r\nError: " + e.Message, "Audio Error");
            }
        }

        private void TogglePause()
        {
            if (_rec == null) return;
            _isPaused = !_isPaused;
            _rec.IsPaused = _isPaused;

            if (_isPaused)
            {
                PlaySound("pause");
                Speak("Recording paused");
                Text(IdPause, "R&esume");
                _statusMsg = _cfg.AutoSplitSecs > 0
                    ? "Status: Paused (Split " + _splitCount + ")"
                    : "Status: Paused";
            }
            else
            {
                PlaySound("unpause");
                Speak("Recording resumed");
                Text(IdPause, "&Pause");
                _statusMsg = _cfg.AutoSplitSecs > 0
                    ? "Status: Recording Split " + _splitCount + " to " + Path.GetFileName(_rec.CurrentFilename)
                    : "Status: Recording to " + Path.GetFileName(_rec.CurrentFilename);
            }
            UpdateDashboard();
        }

        private void OnSplitCompleted()
        {
            _splitCount++;
            string msg = "Split " + _splitCount + " started";
            Notify("File Split", msg, "notify_split");
            Speak(msg);
            _statusMsg = "Status: Recording Split " + _splitCount + " to " + Path.GetFileName(_rec?.CurrentFilename ?? "");
            UpdateDashboard();
        }

        private void ForceStopMaxLength()
        {
            if (!_isRecording) return;
            StopRecording(false);
            Notify("Max Length Reached", "Stopped: maximum recording length reached.", "notify_start_stop");
        }

        private void StopRecording(bool notify = true)
        {
            if (!_isRecording && !_shuttingDown) return;

            _isRecording = false;
            _isPaused = false;

            Win32.KillTimer(Hwnd, (UIntPtr)TimerSplit);
            Win32.KillTimer(Hwnd, (UIntPtr)TimerMaxLen);
            Win32.KillTimer(Hwnd, (UIntPtr)TimerLiveStats);
            Win32.KillTimer(Hwnd, (UIntPtr)TimerDisk);

            Enable(IdRecord, false);
            Enable(IdPause, false);
            Enable(IdSettings, false);

            _statusMsg = "Status: Stopping and finalizing recording...";
            UpdateDashboard();
            _shuttingDown = true;

            if (_rec?.Session != null) Log.Info("Stop event set for session: " + _rec.Session.SessionId);
            _rec?.Stop();

            _notifyOnStop = notify;
            _shutdownPollCount = 0;
            Win32.SetTimer(Hwnd, (UIntPtr)TimerShutdown, 100, IntPtr.Zero);
        }

        private void PollSessionShutdown()
        {
            _shutdownPollCount++;

            if (_rec != null && _rec.WriterAlive)
            {
                if (_shutdownPollCount > 100) Log.Error("Writer thread stuck during shutdown!");
                else return;
            }

            if (_rec != null && _rec.ReadersAlive)
            {
                if (_shutdownPollCount > 50)
                    Log.Warn("Reader threads stuck during shutdown! Forcing exit from shutdown poll.");
                else return;
            }

            Win32.KillTimer(Hwnd, (UIntPtr)TimerShutdown);
            _shuttingDown = false;

            if (_notifyOnStop) Speak("Recording stopped");

            Text(IdRecord, "&Start Recording");
            Enable(IdRecord, true);
            Text(IdPause, "&Pause");
            Enable(IdSettings, true);
            ShowStats(false);

            _statusMsg = "Status: Ready";
            UpdateDashboard();
            PlaySound("stop");

            var session = _rec?.Session;
            _rec = null;

            if (_notifyOnStop && session != null)
            {
                int dropped = session.DroppedBlocks;
                int silence = session.SilenceBlocks;
                string msg;
                var level = Notifier.Level.Info;

                switch (session.Finalization)
                {
                    case FinalizationStatus.FinalizationFailed:
                        msg = "File finalization failed. The recording may be incomplete.";
                        level = Notifier.Level.Error;
                        Speak("Warning. File finalization failed.");
                        break;
                    case FinalizationStatus.ClosedWithWarnings:
                        msg = "Saved with warnings. Please verify the recording.";
                        level = Notifier.Level.Warning;
                        Speak("Recording saved with verification warnings.");
                        break;
                    default:
                        if (dropped > 0 || silence > 0)
                        {
                            msg = "Saved, but " + dropped + " blocks dropped and " +
                                  silence + " silence blocks substituted.";
                            level = Notifier.Level.Warning;
                            Speak("Recording saved with quality warnings.");
                        }
                        else
                        {
                            msg = "File safely saved to disk.";
                            Speak("Recording saved successfully.");
                        }
                        break;
                }
                Notify("Recording Saved", msg, "notify_start_stop", level);
            }

            if (_pendingClose)
            {
                _pendingClose = false;
                DestroyAndQuit();
            }
        }

        // ---- failure handling ----

        private void HandleError(string message)
        {
            string root = Recorder.DriveRootOf(_cfg.SaveFolder);
            if (root != null && !Directory.Exists(root))
                return; // the drive monitor owns this case

            StopRecording(false);
            Notify("Error", "Recording failed.", "notify_error", Notifier.Level.Error);
            Critical(message, "Recording Error");
        }

        /// <summary>
        /// An input went quiet, or came back. This never stops the recording.
        ///
        /// The Python build routed this into the error path, so two seconds of
        /// no data from a device ended the session and popped a modal dialog,
        /// turning a transient glitch into the permanent end of an unattended
        /// recording. It is now a warning: the writer keeps going and fills the
        /// gap with silence, and recovery is announced when data returns.
        /// </summary>
        private void HandleStallChanged(int inputNumber, bool stalled)
        {
            if (!_isRecording) return;

            string what = _rec != null && _cfg.Device2Id != "none" ? "Input " + inputNumber : "The input";

            if (stalled)
            {
                Speak(what + " stopped delivering audio. Still recording.");
                Notify("Input Stalled",
                    what + " stopped delivering audio. Recording continues.",
                    "notify_error", Notifier.Level.Warning);
            }
            else
            {
                Speak(what + " is delivering audio again.");
                Notify("Input Recovered", what + " is delivering audio again.",
                    "notify_error");
            }
        }

        private void HandleDriveDisconnect(string drive)
        {
            if (_handlingDisconnect) return;
            _handlingDisconnect = true;
            try
            {
                bool wasRecording = _isRecording;
                if (_isRecording) StopRecording();

                if (_cfg.NotifyDriveDisconnect)
                {
                    Notify("Drive Disconnected",
                        wasRecording
                            ? "Drive " + drive + " disconnected. Recording stopped."
                            : "Drive " + drive + " disconnected.",
                        "notify_drive_disconnect", Notifier.Level.Error);
                }

                if (wasRecording && _cfg.AutoResumeUnattended)
                {
                    StartAutoResume("drive");
                    string msg = "Waiting for output drive to reconnect before resuming...";
                    Speak(msg);
                    _statusMsg = "Status: Error - " + msg;
                    UpdateDashboard();
                    return;
                }

                Critical("The output drive (" + drive + ") was suddenly removed or disconnected.\r\n\r\n" +
                    (wasRecording
                        ? "The active recording was automatically stopped. Recording is stopping. " +
                          "File integrity has not yet been confirmed.\r\n\r\n"
                        : "") +
                    "Please reconnect the drive or go to Settings to select a new Output Directory.",
                    "Drive Disconnected");
            }
            finally
            {
                _handlingDisconnect = false;
            }
        }

        private void HandleMicDisconnect(int micNum, bool willContinue)
        {
            string msg = "CRITICAL ERROR: Microphone " + micNum + " was unplugged or disabled during recording!";

            if (willContinue)
            {
                msg += "\r\n\r\nHowever, because 'Continue recording' is enabled, the recording will continue " +
                       "using the remaining input.";
                Notify("Microphone Disconnected",
                    "Microphone " + micNum + " disconnected. Continuing on the remaining input.",
                    "notify_mic_disconnect", Notifier.Level.Warning);
                Critical(msg, "Microphone Disconnected");
                return;
            }

            msg += "\r\n\r\nRecording is stopping. File integrity has not yet been confirmed.";
            StopRecording();
            Notify("Microphone Disconnected",
                "Microphone " + micNum + " disconnected. Recording stopped.",
                "notify_mic_disconnect", Notifier.Level.Error);

            if (_cfg.AutoResumeUnattended)
            {
                StartAutoResume("mic");
                string wait = "Waiting for microphone to reconnect before resuming...";
                Speak(wait);
                _statusMsg = "Status: Error - " + wait;
                UpdateDashboard();
                return;
            }

            Critical(msg, "Microphone Disconnected");
        }

        private void StartAutoResume(string missing)
        {
            if (_autoResume != null && _autoResume.IsRunning)
            {
                _autoResume.Missing = "both";
                return;
            }
            _autoResume = new AutoResumeWatcher(_cfg.SaveFolder, _cfg.DeviceId, _cfg.Device2Id, missing,
                m => Post(() => HandleAutoResume(m)));
            _autoResume.Start();
        }

        private void HandleAutoResume(string missing)
        {
            _autoResume?.Stop();

            Speak(missing switch
            {
                "drive" => "Output drive found. Recording again after error.",
                "mic" => "Microphone found. Recording again after error.",
                _ => "Devices found. Recording again after error.",
            });

            // The device list has to be rebuilt: the reconnected endpoint is a
            // fresh object that the cached list does not contain.
            PopulateDevices();
            StartRecording();
        }

        private void CheckDiskSpace()
        {
            if (!_isRecording || _rec == null || string.IsNullOrEmpty(_rec.SessionFolder)) return;

            try
            {
                string root = Recorder.DriveRootOf(_rec.SessionFolder);
                if (root == null) return;

                var di = new DriveInfo(root);
                long free = di.AvailableFreeSpace;

                int sr = ParseInt(_cfg.SampleRate, 48000);
                int ch = ParseInt(_cfg.Channels, 2);
                int bd = ParseInt(_cfg.BitDepth, 24);

                double bytesPerSecond = (double)sr * ch * (bd / 8.0);
                if (bytesPerSecond == 0) return;

                double remainingBytes = free - (256.0 * 1024 * 1024);
                if (remainingBytes < 0) remainingBytes = 0;
                double remainingMinutes = remainingBytes / (bytesPerSecond * 60);

                if (remainingMinutes <= 1 && _lastDiskWarningLevel < 4)
                {
                    _lastDiskWarningLevel = 4;
                    string msg = "Emergency: Less than 1 minute of disk space! Stopping recording.";
                    Log.Error(msg);
                    Speak(msg);
                    StopRecording();
                }
                else if (remainingMinutes <= 5 && _lastDiskWarningLevel < 3)
                {
                    _lastDiskWarningLevel = 3;
                    string msg = "Urgent Warning: Less than 5 minutes of disk space remaining!";
                    Log.Warn(msg);
                    Speak(msg);
                }
                else if (remainingMinutes <= 15 && _lastDiskWarningLevel < 2)
                {
                    _lastDiskWarningLevel = 2;
                    string msg = "Critical Warning: Less than 15 minutes of disk space remaining.";
                    Log.Warn(msg);
                    Speak(msg);
                }
                else if (remainingMinutes <= 60 && _lastDiskWarningLevel < 1)
                {
                    _lastDiskWarningLevel = 1;
                    string msg = "Warning: Less than 60 minutes of disk space remaining.";
                    Log.Warn(msg);
                    Speak(msg);
                }
                else if (remainingMinutes > 60)
                {
                    _lastDiskWarningLevel = 0;
                }
            }
            catch (Exception e)
            {
                Log.Error("Error checking disk space: " + e.Message);
            }
        }

        // ---- crash recovery ----

        private void CheckRecoveryJournal()
        {
            var paths = new List<string> { Path.Combine(Config.AppDataDir, "active_recording.json") };

            string saveFolder = _cfg.SaveFolder;
            if (!string.IsNullOrEmpty(saveFolder) && Directory.Exists(saveFolder))
            {
                paths.Add(Path.Combine(saveFolder, "active_recording.json"));
                try
                {
                    var subs = new List<DirectoryInfo>(new DirectoryInfo(saveFolder).GetDirectories());
                    subs.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    for (int i = 0; i < Math.Min(5, subs.Count); i++)
                        paths.Add(Path.Combine(subs[i].FullName, "active_recording.json"));
                }
                catch
                {
                }
            }

            foreach (string journalPath in paths)
            {
                if (!File.Exists(journalPath)) continue;
                try
                {
                    var data = JsonObject.Parse(File.ReadAllText(journalPath));
                    string filepath = data.GetString("current_file", "");

                    if (string.IsNullOrEmpty(filepath) || !File.Exists(filepath))
                    {
                        TryDelete(journalPath);
                        continue;
                    }

                    // A journal only means a recording did not close cleanly if
                    // nothing is still writing to it. The Python build and this
                    // one share the same journal path, so a session running in
                    // either will leave one sitting there live; offering to
                    // "repair" a file that is being recorded right now is both
                    // alarming and useless. If the file is still locked for
                    // writing, leave it alone.
                    if (IsInUse(filepath))
                    {
                        Log.Info("Skipping recovery prompt: " + filepath +
                                 " is still open, so a recording is in progress.");
                        continue;
                    }

                    long res = (long)new RepairDialog(filepath).ShowModal(Hwnd);
                    if (res == 1)
                    {
                        if (WavFile.Repair(filepath))
                        {
                            Info("The audio file was successfully repaired and should now be playable.",
                                "Repair Successful");
                            TryDelete(journalPath);
                        }
                        else
                        {
                            Warn("Could not repair the audio file. It may be too corrupted or not a valid recording.",
                                "Repair Failed");
                        }
                    }
                    else if (res == 2)
                    {
                        TryDelete(journalPath);
                    }
                }
                catch (Exception e)
                {
                    Log.Error("Failed to process recovery journal: " + e.Message);
                }
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        /// <summary>
        /// True when another process still holds the file open for writing.
        /// Opening with no sharing succeeds only if nobody else has it.
        /// </summary>
        internal static bool IsInUse(string path)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (FileNotFoundException)
            {
                // Derives from IOException, so it has to be caught first: a
                // file that is not there is not a file in use.
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch
            {
                // Permission problems are not evidence of an active recording.
                return false;
            }
        }

        // ---- shutdown ----

        private void HandleClose()
        {
            if (_pendingClose) { DestroyAndQuit(); return; }

            if (_isRecording && _cfg.ConfirmExit)
            {
                string stats = _statsVisible ? StatsText() : "Recording in progress.";
                bool leave = AskYesNo(
                    "You are currently recording.\r\n\r\n" + stats + "\r\n\r\n" +
                    "Are you sure you'd like to exit? If you do, your recording will be saved, " +
                    "and if you have notifications enabled, you will be sent a notification before the program exits.",
                    "Confirm Exit");
                if (!leave) return;
            }

            if (_isRecording || _shuttingDown)
            {
                _pendingClose = true;
                Speak("Finalizing recording before exit. Please do not turn off the computer.");
                StopRecording();
                return;
            }

            DestroyAndQuit();
        }

        /// <summary>
        /// Windows is shutting down. Finalize synchronously, pumping messages so
        /// the shutdown poll timer can still run, then let the session end.
        /// </summary>
        private void HandleEndSession()
        {
            if (!_isRecording && !_shuttingDown) return;

            _pendingClose = false; // do not destroy the window mid-pump
            StopRecording(false);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_shuttingDown && sw.Elapsed.TotalSeconds < 30)
            {
                while (Win32.PeekMessageW(out var m, IntPtr.Zero, 0, 0, 1))
                {
                    Win32.TranslateMessage(ref m);
                    Win32.DispatchMessageW(ref m);
                }
                System.Threading.Thread.Sleep(20);
            }
        }

        private void DestroyAndQuit()
        {
            _driveMonitor?.Stop();
            _autoResume?.Stop();
            Win32.DestroyWindow(Hwnd);
            Win32.PostQuitMessage(0);
        }
    }
}
