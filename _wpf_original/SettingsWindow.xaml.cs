using System;
using System.Windows;
using NAudio.CoreAudioApi;

namespace AudioRecorderPro
{
    public partial class SettingsWindow : Window
    {
        private ConfigManager configManager;

        public SettingsWindow()
        {
            InitializeComponent();
            configManager = new ConfigManager();
            PopulateDevices();
            LoadSettingsToUI();
        }

        private void PopulateDevices()
        {
            var enumerator = new MMDeviceEnumerator();
            var captureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

            CbDevice1.Items.Add("Default Microphone");
            CbDevice2.Items.Add("None");
            CbDevice2.Items.Add("Default Speakers (Loopback)");

            foreach (var d in captureDevices)
            {
                CbDevice1.Items.Add(d.FriendlyName);
                CbDevice2.Items.Add(d.FriendlyName);
            }
            foreach (var d in renderDevices)
            {
                CbDevice1.Items.Add("[Loopback] " + d.FriendlyName);
                CbDevice2.Items.Add("[Loopback] " + d.FriendlyName);
            }

            CbDevice1.SelectedIndex = 0;
            CbDevice2.SelectedIndex = 0;
        }

        private void LoadSettingsToUI()
        {
            var cfg = configManager.CurrentConfig;
            TxtFolder.Text = cfg.SaveFolder;
            TxtPrefix.Text = cfg.FilenamePrefix;
            TxtDelay.Text = cfg.AutoStartDelay.ToString();
            TxtMaxLen.Text = cfg.MaxLengthSecs.ToString();
            TxtSplit.Text = cfg.AutoSplitSecs.ToString();
            TxtTitle.Text = cfg.WindowTitle;
            ChkAutoStart.IsChecked = cfg.AutoStart;
            ChkGroupSplits.IsChecked = cfg.GroupSplits;
            ChkUpdateStartup.IsChecked = cfg.CheckUpdatesStartup;
            
            CbSampleRate.Text = cfg.SampleRate;
            CbBitDepth.Text = cfg.BitDepth;
            CbChannels.Text = cfg.Channels == "1" ? "1 (Mono)" : "2 (Stereo)";
            CbBufferSize.Text = cfg.BufferSize.ToString();
            
            // Note: Device names require complex matching in real apps, 
            // but for MVP we match text roughly if it exists.
            foreach (var item in CbDevice1.Items)
            {
                if (item.ToString() == cfg.DeviceId) CbDevice1.SelectedItem = item;
            }
            foreach (var item in CbDevice2.Items)
            {
                if (item.ToString() == cfg.Device2Id) CbDevice2.SelectedItem = item;
            }
        }

        private void BtnChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog())
            {
                fbd.Description = "Select Output Directory";
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    TxtFolder.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnSaveClose_Click(object sender, RoutedEventArgs e)
        {
            string dev1 = CbDevice1.SelectedItem?.ToString() ?? "";
            string dev2 = CbDevice2.SelectedItem?.ToString() ?? "None";

            if (dev1 == dev2 && dev1 != "None" && dev2 != "None")
            {
                MessageBox.Show("Primary and Secondary inputs cannot be exactly the same device. Please select a different secondary input or set it to 'None'.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var cfg = configManager.CurrentConfig;
            cfg.SaveFolder = TxtFolder.Text;
            cfg.FilenamePrefix = TxtPrefix.Text;
            
            if (int.TryParse(TxtDelay.Text, out int delay)) cfg.AutoStartDelay = Math.Max(0, delay);
            if (int.TryParse(TxtMaxLen.Text, out int maxLen)) cfg.MaxLengthSecs = Math.Max(0, maxLen);
            if (int.TryParse(TxtSplit.Text, out int split)) cfg.AutoSplitSecs = Math.Max(0, split);
            
            cfg.WindowTitle = TxtTitle.Text;
            cfg.AutoStart = ChkAutoStart.IsChecked ?? false;
            cfg.GroupSplits = ChkGroupSplits.IsChecked ?? true;
            cfg.CheckUpdatesStartup = ChkUpdateStartup.IsChecked ?? true;

            cfg.SampleRate = CbSampleRate.Text;
            cfg.BitDepth = CbBitDepth.Text;
            cfg.Channels = CbChannels.Text.StartsWith("1") ? "1" : "2";
            
            if (int.TryParse(CbBufferSize.Text, out int buf) && buf > 0) cfg.BufferSize = buf;

            cfg.DeviceId = dev1;
            cfg.Device2Id = dev2;

            configManager.SaveConfig();

            this.DialogResult = true;
            this.Close();
        }
    }
}
