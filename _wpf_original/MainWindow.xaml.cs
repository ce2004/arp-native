using System;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace AudioRecorderPro
{
    public partial class MainWindow : Window
    {
        private bool isRecording = false;
        private bool isPaused = false;
        private ConfigManager configManager;
        private AudioEngine engine;
        private DispatcherTimer driveCheckTimer;

        public MainWindow()
        {
            InitializeComponent();
            configManager = new ConfigManager();
            
            // Implementing a robust drive monitor checking the output folder
            driveCheckTimer = new DispatcherTimer();
            driveCheckTimer.Interval = TimeSpan.FromSeconds(2);
            driveCheckTimer.Tick += DriveCheckTimer_Tick;
        }

        private void DriveCheckTimer_Tick(object sender, EventArgs e)
        {
            if (isRecording)
            {
                var cfg = configManager.CurrentConfig;
                try
                {
                    string pathRoot = Path.GetPathRoot(cfg.SaveFolder);
                    if (!string.IsNullOrEmpty(pathRoot))
                    {
                        DriveInfo di = new DriveInfo(pathRoot);
                        if (!di.IsReady)
                        {
                            StopRecordingGracefully();
                            if (cfg.NotifyDriveDisconnect)
                            {
                                NvdaController.SpeakText("Error: Drive disconnected. Recording stopped.");
                                SystemSounds.Hand.Play();
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore transient path errors
                }
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsWindow settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void BtnToggleRecord_Click(object sender, RoutedEventArgs e)
        {
            if (isRecording)
            {
                StopRecordingGracefully();
            }
            else
            {
                var cfg = configManager.CurrentConfig;
                if (cfg.AutoStartDelay > 0)
                {
                    BtnToggleRecord.IsEnabled = false;
                    BtnToggleRecord.Content = $"Starting in {cfg.AutoStartDelay}s...";
                    
                    DispatcherTimer delayTimer = new DispatcherTimer();
                    delayTimer.Interval = TimeSpan.FromSeconds(cfg.AutoStartDelay);
                    delayTimer.Tick += (s, args) =>
                    {
                        delayTimer.Stop();
                        BtnToggleRecord.IsEnabled = true;
                        StartRecording();
                    };
                    delayTimer.Start();
                }
                else
                {
                    StartRecording();
                }
            }
        }

        private void StartRecording()
        {
            var cfg = configManager.CurrentConfig;
            
            isRecording = true;
            isPaused = false;
            BtnToggleRecord.Content = "S_top Recording";
            BtnPause.IsEnabled = true;
            BtnPause.Content = "_Pause";
            BtnSettings.IsEnabled = false;
            LiveStatsBorder.Visibility = Visibility.Visible;
            
            if (cfg.SndStart)
            {
                SystemSounds.Beep.Play();
            }
            if (cfg.NotifyStartStop)
            {
                NvdaController.SpeakText("Recording Started");
            }
            
            engine = new AudioEngine(cfg.DeviceId, cfg.Device2Id, cfg.SaveFolder, cfg.FilenamePrefix, cfg.BufferSize, cfg.AutoSplitSecs);
            engine.Start();
            
            driveCheckTimer.Start();
        }

        private void StopRecordingGracefully()
        {
            var cfg = configManager.CurrentConfig;
            
            isRecording = false;
            BtnToggleRecord.Content = "_Start Recording";
            BtnPause.IsEnabled = false;
            BtnSettings.IsEnabled = true;
            LiveStatsBorder.Visibility = Visibility.Collapsed;
            
            engine?.Stop();
            engine?.Dispose();
            engine = null;
            
            driveCheckTimer.Stop();
            
            if (cfg.SndStop)
            {
                SystemSounds.Exclamation.Play();
            }
            if (cfg.NotifyStartStop)
            {
                NvdaController.SpeakText("Recording Stopped");
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            var cfg = configManager.CurrentConfig;
            if (isPaused)
            {
                isPaused = false;
                BtnPause.Content = "_Pause";
                engine?.Resume();
                
                if (cfg.SndUnpause)
                {
                    SystemSounds.Asterisk.Play();
                }
                if (cfg.NotifyStartStop)
                {
                    NvdaController.SpeakText("Recording Resumed");
                }
            }
            else
            {
                isPaused = true;
                BtnPause.Content = "R_esume";
                engine?.Pause();
                
                if (cfg.SndPause)
                {
                    SystemSounds.Asterisk.Play();
                }
                if (cfg.NotifyStartStop)
                {
                    NvdaController.SpeakText("Recording Paused");
                }
            }
        }
    }
}
