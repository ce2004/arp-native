using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioRecorderPro
{
    public class Config
    {
        [JsonPropertyName("auto_start")]
        public bool AutoStart { get; set; } = false;

        [JsonPropertyName("auto_start_delay")]
        public int AutoStartDelay { get; set; } = 0;

        [JsonPropertyName("save_folder")]
        public string SaveFolder { get; set; } = Directory.GetCurrentDirectory();

        [JsonPropertyName("sample_rate")]
        public string SampleRate { get; set; } = "48000";

        [JsonPropertyName("bit_depth")]
        public string BitDepth { get; set; } = "24";

        [JsonPropertyName("channels")]
        public string Channels { get; set; } = "2";

        [JsonPropertyName("filename_prefix")]
        public string FilenamePrefix { get; set; } = "";

        [JsonPropertyName("auto_split_secs")]
        public int AutoSplitSecs { get; set; } = 0;

        [JsonPropertyName("max_length_secs")]
        public int MaxLengthSecs { get; set; } = 0;

        [JsonPropertyName("group_splits")]
        public bool GroupSplits { get; set; } = true;

        [JsonPropertyName("buffer_size")]
        public int BufferSize { get; set; } = 2048;

        [JsonPropertyName("device_id")]
        public string DeviceId { get; set; } = "";

        [JsonPropertyName("device2_id")]
        public string Device2Id { get; set; } = "none";

        [JsonPropertyName("in1_route")]
        public string In1Route { get; set; } = "Both Channels";

        [JsonPropertyName("in2_route")]
        public string In2Route { get; set; } = "Both Channels";

        [JsonPropertyName("window_title")]
        public string WindowTitle { get; set; } = "Accessible Advanced Audio Recorder";

        [JsonPropertyName("notify_start_stop")]
        public bool NotifyStartStop { get; set; } = true;

        [JsonPropertyName("notify_split")]
        public bool NotifySplit { get; set; } = true;

        [JsonPropertyName("notify_error")]
        public bool NotifyError { get; set; } = true;

        [JsonPropertyName("notify_drive_disconnect")]
        public bool NotifyDriveDisconnect { get; set; } = true;

        [JsonPropertyName("notify_mic_disconnect")]
        public bool NotifyMicDisconnect { get; set; } = true;

        [JsonPropertyName("speak_in_focus_only")]
        public bool SpeakInFocusOnly { get; set; } = false;

        [JsonPropertyName("auto_resume_unattended")]
        public bool AutoResumeUnattended { get; set; } = false;

        [JsonPropertyName("continue_on_mic_disconnect")]
        public bool ContinueOnMicDisconnect { get; set; } = false;

        [JsonPropertyName("confirm_exit")]
        public bool ConfirmExit { get; set; } = true;

        [JsonPropertyName("check_updates_startup")]
        public bool CheckUpdatesStartup { get; set; } = true;

        [JsonPropertyName("snd_start")]
        public bool SndStart { get; set; } = true;

        [JsonPropertyName("snd_stop")]
        public bool SndStop { get; set; } = true;

        [JsonPropertyName("snd_pause")]
        public bool SndPause { get; set; } = true;

        [JsonPropertyName("snd_unpause")]
        public bool SndUnpause { get; set; } = true;

        [JsonPropertyName("snd_volume")]
        public int SndVolume { get; set; } = 100;
    }

    public class ConfigManager
    {
        private static string GetAppDataDir()
        {
            string baseDir = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? 
                             Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string d = Path.Combine(baseDir, "Audio Recorder Pro");
            Directory.CreateDirectory(d);
            return d;
        }

        public string ConfigFile { get; private set; }
        public Config CurrentConfig { get; private set; }

        public ConfigManager()
        {
            ConfigFile = Path.Combine(GetAppDataDir(), "recorder_config.json");
            LoadConfig();
        }

        public Config LoadConfig()
        {
            CurrentConfig = new Config();
            if (File.Exists(ConfigFile))
            {
                try
                {
                    string json = File.ReadAllText(ConfigFile);
                    var config = JsonSerializer.Deserialize<Config>(json);
                    if (config != null)
                    {
                        CurrentConfig = config;
                    }
                }
                catch
                {
                    // Ignore load errors and use defaults
                }
            }
            return CurrentConfig;
        }

        public void SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(CurrentConfig, options);
                File.WriteAllText(ConfigFile, json);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to save config: " + e.Message);
            }
        }
    }
}
