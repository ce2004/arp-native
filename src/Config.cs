using System;
using System.IO;

namespace Arp
{
    // Reads and writes the same %LOCALAPPDATA%\Audio Recorder Pro\recorder_config.json
    // the Python build uses, so the two are drop-in interchangeable. Keys this
    // build does not recognise (split_silence_sec, split_threshold_db, and
    // anything a future version adds) are carried through untouched on save.
    internal sealed class Config
    {
        private JsonObject _obj = new();

        public string FilePath { get; }

        public static string AppDataDir
        {
            get
            {
                string b = Environment.GetEnvironmentVariable("LOCALAPPDATA");
                if (string.IsNullOrEmpty(b))
                    b = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string d = Path.Combine(b, "Audio Recorder Pro");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public Config() : this(Path.Combine(AppDataDir, "recorder_config.json")) { }

        public Config(string path)
        {
            FilePath = path;
            Load();
        }

        public void Load()
        {
            _obj = new JsonObject();
            if (File.Exists(FilePath))
            {
                try
                {
                    _obj = JsonObject.Parse(File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    // Matches the Python build: a corrupt config falls back to
                    // defaults rather than blocking startup.
                    Log.Warn("Config parse failed, using defaults: " + e.Message);
                    _obj = new JsonObject();
                }
            }

            // Migration carried over from the Python build.
            if (_obj.Has("auto_split_mins"))
            {
                int mins = _obj.GetInt("auto_split_mins", 0);
                _obj.Remove("auto_split_mins");
                _obj.Set("auto_split_secs", (double)(mins * 60));
            }
        }

        public void Save()
        {
            try
            {
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, _obj.ToJson());
                File.Move(tmp, FilePath, true);
            }
            catch (Exception e)
            {
                Log.Error("Failed to save config: " + e.Message);
            }
        }

        private string S(string k, string d) => _obj.GetString(k, d);
        private int I(string k, int d) => _obj.GetInt(k, d);
        private bool B(string k, bool d) => _obj.GetBool(k, d);
        private void Set(string k, string v) => _obj.Set(k, v);
        private void Set(string k, int v) => _obj.Set(k, (double)v);
        private void Set(string k, bool v) => _obj.Set(k, v);

        public bool AutoStart { get => B("auto_start", false); set => Set("auto_start", value); }
        public int AutoStartDelay { get => I("auto_start_delay", 0); set => Set("auto_start_delay", value); }
        public string SaveFolder { get => S("save_folder", Directory.GetCurrentDirectory()); set => Set("save_folder", value); }
        public string SampleRate { get => S("sample_rate", "48000"); set => Set("sample_rate", value); }
        public string BitDepth { get => S("bit_depth", "24"); set => Set("bit_depth", value); }
        public string Channels { get => S("channels", "2"); set => Set("channels", value); }
        public string FilenamePrefix { get => S("filename_prefix", ""); set => Set("filename_prefix", value); }
        public int AutoSplitSecs { get => I("auto_split_secs", 0); set => Set("auto_split_secs", value); }
        public int MaxLengthSecs { get => I("max_length_secs", 0); set => Set("max_length_secs", value); }
        public bool GroupSplits { get => B("group_splits", true); set => Set("group_splits", value); }
        public int BufferSize { get => I("buffer_size", 2048); set => Set("buffer_size", value); }
        public string DeviceId { get => S("device_id", ""); set => Set("device_id", value); }
        public string Device2Id { get => S("device2_id", "none"); set => Set("device2_id", value); }
        public string In1Route { get => S("in1_route", "Both Channels"); set => Set("in1_route", value); }
        public string In2Route { get => S("in2_route", "Both Channels"); set => Set("in2_route", value); }
        public string WindowTitle { get => S("window_title", "Accessible Advanced Audio Recorder"); set => Set("window_title", value); }
        public bool NotifyStartStop { get => B("notify_start_stop", true); set => Set("notify_start_stop", value); }
        public bool NotifySplit { get => B("notify_split", true); set => Set("notify_split", value); }
        public bool NotifyError { get => B("notify_error", true); set => Set("notify_error", value); }
        public bool NotifyDriveDisconnect { get => B("notify_drive_disconnect", true); set => Set("notify_drive_disconnect", value); }
        public bool NotifyMicDisconnect { get => B("notify_mic_disconnect", true); set => Set("notify_mic_disconnect", value); }
        public bool SpeakInFocusOnly { get => B("speak_in_focus_only", false); set => Set("speak_in_focus_only", value); }
        public bool AutoResumeUnattended { get => B("auto_resume_unattended", false); set => Set("auto_resume_unattended", value); }
        public bool ContinueOnMicDisconnect { get => B("continue_on_mic_disconnect", false); set => Set("continue_on_mic_disconnect", value); }
        public bool ConfirmExit { get => B("confirm_exit", true); set => Set("confirm_exit", value); }
        public bool CheckUpdatesStartup { get => B("check_updates_startup", true); set => Set("check_updates_startup", value); }
        public bool SndStart { get => B("snd_start", true); set => Set("snd_start", value); }
        public bool SndStop { get => B("snd_stop", true); set => Set("snd_stop", value); }
        public bool SndPause { get => B("snd_pause", true); set => Set("snd_pause", value); }
        public bool SndUnpause { get => B("snd_unpause", true); set => Set("snd_unpause", value); }
        public int SndVolume { get => I("snd_volume", 100); set => Set("snd_volume", value); }
        public string DeviceSortOrder { get => S("device_sort_order", "Inputs First"); set => Set("device_sort_order", value); }

        // Read from config by the Python build's mixer but never exposed in its
        // UI. Kept readable here for identical mix behaviour; see the notes for
        // the case for giving these a real control.
        public double In1Gain
        {
            get
            {
                var raw = _obj.GetRaw("in1_gain");
                if (raw is double d) return d;
                if (raw is string s && double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double p)) return p;
                return Device2Id != "none" ? 0.5 : 1.0;
            }
        }

        public double In2Gain
        {
            get
            {
                var raw = _obj.GetRaw("in2_gain");
                if (raw is double d) return d;
                if (raw is string s && double.TryParse(s, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double p)) return p;
                return 0.5;
            }
        }

        public bool SndEnabled(string eventName) => eventName switch
        {
            "start" => SndStart,
            "stop" => SndStop,
            "pause" => SndPause,
            "unpause" => SndUnpause,
            _ => true,
        };

        public bool NotifyEnabled(string key) => B(key, true);
    }
}
