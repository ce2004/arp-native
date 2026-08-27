using System;
using System.Collections.Generic;
using System.IO;

namespace Arp
{
    /// <summary>
    /// Creates each dialog for real — hidden, initialised, inspected, destroyed —
    /// and asserts the controls exist and were populated from config. The dialog
    /// templates are hand-assembled binary DLGTEMPLATEs, so an off-by-one in the
    /// alignment or an item-count mismatch would otherwise only show up as a
    /// window that silently fails to appear.
    ///
    /// Deliberately does not touch the main window, the audio devices, or the
    /// real configuration file: nothing here starts a recording.
    /// Run with: ArpRecorder.exe --uitest
    /// </summary>
    internal static class UiTest
    {
        private static int _passed;
        private static readonly List<string> Failures = new();
        private static readonly System.Text.StringBuilder Transcript = new();

        public static string ReportPath { get; } =
            Path.Combine(Path.GetTempPath(), "arp_uitest_report.txt");

        private static void Say(string s)
        {
            Transcript.AppendLine(s);
            Console.WriteLine(s);
        }

        private static void Check(bool ok, string what)
        {
            if (ok) _passed++;
            else Failures.Add(what);
        }

        private static void Eq<T>(T actual, T expected, string what)
        {
            if (EqualityComparer<T>.Default.Equals(actual, expected)) _passed++;
            else Failures.Add(what + " (expected <" + expected + ">, got <" + actual + ">)");
        }

        public static int Run()
        {
            Win32.EnableDpiAwareness();
            Win32.InitCommonControls();

            string cfgPath = Path.Combine(Path.GetTempPath(), "arp_uitest_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var cfg = new Config(cfgPath);
                cfg.SaveFolder = @"D:\Recordings";
                cfg.AutoSplitSecs = 5400;
                cfg.MaxLengthSecs = 0;
                cfg.AutoStartDelay = 90;
                cfg.SndVolume = 5;
                cfg.FilenamePrefix = "Interview";
                cfg.WindowTitle = "ARP";
                cfg.AutoStart = true;
                cfg.GroupSplits = false;
                cfg.SampleRate = "96000";
                cfg.BitDepth = "32";
                cfg.Channels = "1";
                cfg.BufferSize = 8192;
                cfg.In1Route = "Left Channel Only";
                cfg.In2Route = "Right Channel Only";
                cfg.ContinueOnMicDisconnect = true;
                cfg.NotifySplit = false;
                cfg.SpeakInFocusOnly = true;
                cfg.SndPause = false;
                cfg.SetSoundFor("start", "Soft Chime");

                TestSettings(cfg);
                TestNotifications(cfg);
                TestSounds(cfg);
                TestChannels(cfg);
                TestSimpleDialogs();
                TestListText();
            }
            catch (Exception e)
            {
                Failures.Add("Harness crashed: " + e);
            }
            finally
            {
                try { File.Delete(cfgPath); } catch { }
            }

            Say("");
            Say(_passed + " UI checks passed, " + Failures.Count + " failed.");
            foreach (string f in Failures) Say("  FAIL: " + f);
            try { File.WriteAllText(ReportPath, Transcript.ToString()); } catch { }
            return Failures.Count == 0 ? 0 : 1;
        }

        /// <summary>Creates the dialog hidden, runs its WM_INITDIALOG, hands back the HWND.</summary>
        private static IntPtr Open(DialogBase d, string name)
        {
            try
            {
                IntPtr h = d.CreateModeless(IntPtr.Zero);
                Check(h != IntPtr.Zero, name + " window created");
                return h;
            }
            catch (Exception e)
            {
                Failures.Add(name + " failed to create: " + e.Message);
                return IntPtr.Zero;
            }
        }

        private static void Close(IntPtr h)
        {
            if (h != IntPtr.Zero) Win32.DestroyWindow(h);
        }

        private static bool Has(IntPtr h, int id, string label)
        {
            bool ok = Win32.GetDlgItem(h, id) != IntPtr.Zero;
            Check(ok, label + " control (" + id + ") exists");
            return ok;
        }

        private static int ComboCount(IntPtr h, int id) =>
            (int)Win32.SendDlgItemMessageW(h, id, Win32.CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);

        private static void TestSettings(Config cfg)
        {
            Say("-- Recording Settings");

            var devices = new List<AudioDevice>();
            try { devices = Wasapi.EnumerateDevices(); }
            catch (Exception e) { Say("   (device enumeration unavailable: " + e.Message + ")"); }

            var dlg = new SettingsDialog(cfg, devices);
            IntPtr h = Open(dlg, "Settings");
            if (h == IntPtr.Zero) return;

            try
            {
                foreach (var (id, label) in new (int, string)[]
                {
                    (SettingsDialog.IdSort, "Sort order"),
                    (SettingsDialog.IdDevice1, "Primary input"),
                    (SettingsDialog.IdDevice2, "Secondary input"),
                    (SettingsDialog.IdFolder, "Output folder"),
                    (SettingsDialog.IdChangeFolder, "Change folder"),
                    (SettingsDialog.IdSampleRate, "Sample rate"),
                    (SettingsDialog.IdBitDepth, "Bit depth"),
                    (SettingsDialog.IdChannels, "Channels"),
                    (SettingsDialog.IdBuffer, "Buffer size"),
                    (SettingsDialog.IdPrefix, "File prefix"),
                    (SettingsDialog.IdAutoStart, "Auto-start"),
                    (SettingsDialog.IdDelay, "Start delay"),
                    (SettingsDialog.IdDelayUnit, "Start delay unit"),
                    (SettingsDialog.IdMaxLen, "Max length"),
                    (SettingsDialog.IdMaxLenUnit, "Max length unit"),
                    (SettingsDialog.IdSplit, "Auto-split"),
                    (SettingsDialog.IdSplitUnit, "Auto-split unit"),
                    (SettingsDialog.IdGroupSplits, "Group splits"),
                    (SettingsDialog.IdTitle, "Window title"),
                    (SettingsDialog.IdUpdateStartup, "Update on startup"),
                    (SettingsDialog.IdCheckUpdates, "Check updates now"),
                    (SettingsDialog.IdNotifications, "Configure notifications"),
                    (SettingsDialog.IdSounds, "Configure sounds"),
                    (SettingsDialog.IdChannelsCfg, "Configure audio channels"),
                    (SettingsDialog.IdCopyLogs, "Copy diagnostic logs"),
                    (Win32.IDOK, "Save and close"),
                })
                {
                    Has(h, id, label);
                }

                Eq(ComboCount(h, SettingsDialog.IdSort), 2, "sort order has two entries");
                Eq(ComboCount(h, SettingsDialog.IdSampleRate), 6, "six sample rates");
                Eq(ComboCount(h, SettingsDialog.IdBitDepth), 3, "three bit depths");
                Eq(ComboCount(h, SettingsDialog.IdChannels), 2, "mono and stereo");
                Eq(ComboCount(h, SettingsDialog.IdBuffer), 5, "five buffer sizes");

                Eq(Win32.ComboGetText(h, SettingsDialog.IdSampleRate), "96000", "sample rate selected from config");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdBitDepth), "32", "bit depth selected from config");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdChannels), "1 (Mono)", "mono selected from config");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdBuffer), "8192", "buffer size selected from config");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdSort), "Inputs First", "sort order selected from config");

                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdFolder), @"D:\Recordings", "output folder shown");
                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdPrefix), "Interview", "prefix shown");
                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdTitle), "ARP", "window title shown");

                // Durations split into a number and a unit, so 5400 seconds
                // shows as "90" + "Minutes" rather than needing 5400 arrow
                // presses or knowledge of a shorthand.
                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdSplit), "90 minutes", "auto-split shows the number");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdSplitUnit), "Minutes", "auto-split unit is Minutes");

                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdDelay), "90 seconds", "start delay shows the number");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdDelayUnit), "Seconds", "start delay unit is Seconds");

                Eq(Win32.GetDlgItemText(h, SettingsDialog.IdMaxLen), "0 seconds", "max length shows zero");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdMaxLenUnit), "Seconds", "max length unit is Seconds");

                foreach (var (id, label) in new[]
                {
                    (SettingsDialog.IdDelayUnit, "start delay"),
                    (SettingsDialog.IdMaxLenUnit, "max length"),
                    (SettingsDialog.IdSplitUnit, "auto-split"),
                })
                {
                    Eq(ComboCount(h, id), 3, label + " offers Seconds, Minutes and Hours");
                }

                Eq(Win32.IsChecked(h, SettingsDialog.IdAutoStart), true, "auto-start checked from config");
                Eq(Win32.IsChecked(h, SettingsDialog.IdGroupSplits), false, "group splits unchecked from config");
                Eq(Win32.IsChecked(h, SettingsDialog.IdUpdateStartup), true, "update check checked from config");

                // Input 2 carries an extra "None" row.
                Eq(ComboCount(h, SettingsDialog.IdDevice2), ComboCount(h, SettingsDialog.IdDevice1) + 1,
                    "secondary input list has a None entry");
                Eq(Win32.ComboGetText(h, SettingsDialog.IdDevice2), "None", "secondary input defaults to None");
            }
            finally
            {
                Close(h);
            }
        }

        private static void TestNotifications(Config cfg)
        {
            Say("-- Notification Settings");
            var dlg = new NotificationsDialog(cfg);
            IntPtr h = Open(dlg, "Notifications");
            if (h == IntPtr.Zero) return;
            try
            {
                Has(h, NotificationsDialog.IdStartStop, "Start/stop");
                Has(h, NotificationsDialog.IdSplit, "Split");
                Has(h, NotificationsDialog.IdError, "Error");
                Has(h, NotificationsDialog.IdDrive, "Drive");
                Has(h, NotificationsDialog.IdMic, "Mic");
                Has(h, NotificationsDialog.IdConfirmExit, "Confirm exit");
                Has(h, NotificationsDialog.IdFocusSpeak, "Focus speak");
                Has(h, NotificationsDialog.IdAutoResume, "Auto resume");

                Eq(Win32.IsChecked(h, NotificationsDialog.IdSplit), false, "notify_split false reflected");
                Eq(Win32.IsChecked(h, NotificationsDialog.IdStartStop), true, "notify_start_stop true reflected");
                Eq(Win32.IsChecked(h, NotificationsDialog.IdFocusSpeak), true, "speak_in_focus_only reflected");
            }
            finally { Close(h); }
        }

        private static void TestSounds(Config cfg)
        {
            Say("-- Configure Sounds");
            var dlg = new SoundsDialog(cfg);
            IntPtr h = Open(dlg, "Sounds");
            if (h == IntPtr.Zero) return;
            try
            {
                Has(h, SoundsDialog.IdStart, "Start cue");
                Has(h, SoundsDialog.IdStop, "Stop cue");
                Has(h, SoundsDialog.IdPause, "Pause cue");
                Has(h, SoundsDialog.IdUnpause, "Unpause cue");
                Has(h, SoundsDialog.IdVolume, "Volume");

                Eq(Win32.IsChecked(h, SoundsDialog.IdPause), false, "snd_pause false reflected");
                Eq(Win32.IsChecked(h, SoundsDialog.IdStart), true, "snd_start true reflected");
                Eq(Win32.GetDlgItemText(h, SoundsDialog.IdVolume), "5 percent", "volume reads as a percentage");

                // Each event picks its own sound from the built-in library.
                var soundCombos = new[]
                {
                    (SoundsDialog.IdStartSound, "start"),
                    (SoundsDialog.IdStopSound, "stop"),
                    (SoundsDialog.IdPauseSound, "pause"),
                    (SoundsDialog.IdUnpauseSound, "unpause"),
                };

                foreach (var (id, ev) in soundCombos)
                {
                    Has(h, id, ev + " sound");
                    Eq(ComboCount(h, id), SoundLibrary.Names.Length,
                        ev + " lists every built-in sound");
                    Eq(Win32.ComboGetText(h, id), cfg.SoundFor(ev),
                        ev + " selects the configured sound");
                }

                Eq(Win32.ComboGetText(h, SoundsDialog.IdStartSound), "Soft Chime",
                    "a non-default sound is preselected from config");
                Eq(Win32.ComboGetText(h, SoundsDialog.IdStopSound), "Falling Sweep",
                    "an unset event keeps its default sound");
            }
            finally { Close(h); }
        }

        private static void TestChannels(Config cfg)
        {
            Say("-- Configure Audio Channels");
            var dlg = new ChannelsDialog(cfg);
            IntPtr h = Open(dlg, "Channels");
            if (h == IntPtr.Zero) return;
            try
            {
                Has(h, ChannelsDialog.IdIn1, "Input 1 routing");
                Has(h, ChannelsDialog.IdIn2, "Input 2 routing");
                Has(h, ChannelsDialog.IdContinue, "Continue on disconnect");

                Eq(ComboCount(h, ChannelsDialog.IdIn1), 3, "three routing options for input 1");
                Eq(ComboCount(h, ChannelsDialog.IdIn2), 3, "three routing options for input 2");
                Eq(Win32.ComboGetText(h, ChannelsDialog.IdIn1), "Left Channel Only", "in1_route selected");
                Eq(Win32.ComboGetText(h, ChannelsDialog.IdIn2), "Right Channel Only", "in2_route selected");
                Eq(Win32.IsChecked(h, ChannelsDialog.IdContinue), true, "continue_on_mic_disconnect reflected");
            }
            finally { Close(h); }
        }

        /// <summary>
        /// Exercises the list-of-lines helper against a real list box, since it
        /// is what every read-only block of text in the app now goes through.
        /// </summary>
        private static void TestListText()
        {
            Say("-- Read-only text lists");

            var dlg = new ConfirmDialog("probe", "seed", "ok", "no");
            IntPtr h = Open(dlg, "ListProbe");
            if (h == IntPtr.Zero) return;

            try
            {
                int id = ConfirmDialog.IdText;

                Win32.ListSetLines(h, id, "alpha\nbeta\ngamma");
                Eq(Win32.ListCount(h, id), 3, "three lines become three items");
                Eq(Win32.ListGetLine(h, id, 0), "alpha", "first item");
                Eq(Win32.ListGetLine(h, id, 2), "gamma", "last item");

                // Windows line endings must not leave stray carriage returns.
                Win32.ListSetLines(h, id, "one\r\ntwo");
                Eq(Win32.ListCount(h, id), 2, "CRLF splits into two items");
                Eq(Win32.ListGetLine(h, id, 0), "one", "no trailing carriage return");

                // A blank item would read as an unhelpful "blank".
                Win32.ListSetLines(h, id, "a\n\n\nb");
                Eq(Win32.ListCount(h, id), 2, "blank lines are dropped");

                Win32.ListSetLines(h, id, "");
                Eq(Win32.ListCount(h, id), 0, "empty text clears the list");

                // Setting again must replace, not append.
                Win32.ListSetLines(h, id, "x\ny");
                Win32.ListSetLines(h, id, "z");
                Eq(Win32.ListCount(h, id), 1, "a second set replaces the contents");
                Eq(Win32.ListGetAll(h, id), "z", "contents are the newest text");

                string longLine = "Output Folder is set to: " + new string('p', 200);
                Win32.ListSetLines(h, id, longLine);
                Eq(Win32.ListGetLine(h, id, 0), longLine, "a long path survives intact");
            }
            finally
            {
                Close(h);
            }
        }

        private static void TestSimpleDialogs()
        {
            Say("-- Repair / Update / Confirm");

            var repair = new RepairDialog(@"D:\Recordings\20260826_140309.wav");
            IntPtr h = Open(repair, "Repair");
            if (h != IntPtr.Zero)
            {
                try
                {
                    Has(h, RepairDialog.IdRepair, "Repair button");
                    Has(h, RepairDialog.IdLeave, "Leave alone button");
                    Has(h, RepairDialog.IdForget, "Forget button");
                    Check(Win32.ListGetAll(h, RepairDialog.IdText).Contains("20260826_140309.wav"),
                        "repair prompt names the file");
                    Check(Win32.ListCount(h, RepairDialog.IdText) >= 3,
                        "repair prompt reads as separate lines");
                }
                finally { Close(h); }
            }

            var update = new UpdateDialog("v2.0.0", "v2.1.0", "Added X\n- Fixed Y\n* Changed Z\n\n");
            h = Open(update, "Update");
            if (h != IntPtr.Zero)
            {
                try
                {
                    Has(h, UpdateDialog.IdList, "Release notes list");
                    Has(h, UpdateDialog.IdUpdate, "Update now button");
                    Has(h, UpdateDialog.IdSkip, "Skip button");
                    Check(Win32.ListGetAll(h, UpdateDialog.IdInfo).Contains("v2.0.0 to v2.1.0"),
                        "update prompt names both versions");
                    int count = (int)Win32.SendDlgItemMessageW(h, UpdateDialog.IdList, 0x018B, IntPtr.Zero, IntPtr.Zero);
                    Eq(count, 3, "blank lines dropped from release notes");
                }
                finally { Close(h); }
            }

            var confirm = new ConfirmDialog("External Drive Warning", "line one\nline two", "I understand", "Revert");
            h = Open(confirm, "Confirm");
            if (h != IntPtr.Zero)
            {
                try
                {
                    Has(h, ConfirmDialog.IdAccept, "Accept button");
                    Has(h, ConfirmDialog.IdReject, "Reject button");
                    Check(Win32.ListGetAll(h, ConfirmDialog.IdText).Contains("line two"),
                        "confirm text carries both lines");
                    Eq(Win32.ListCount(h, ConfirmDialog.IdText), 2, "confirm text is two list items");
                }
                finally { Close(h); }
            }
        }
    }
}
