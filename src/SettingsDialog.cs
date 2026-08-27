using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Arp
{
    /// <summary>
    /// A message box with custom button labels, which MessageBox cannot do.
    /// Used for the external-drive warning so the choices read as
    /// "I understand" / "Revert" the way they do in the Python build.
    /// </summary>
    internal sealed class ConfirmDialog : DialogBase
    {
        internal const int IdText = 1601;
        internal const int IdAccept = 1602;
        internal const int IdReject = 1603;

        private readonly string _title, _message, _acceptText, _rejectText;

        public ConfirmDialog(string title, string message, string acceptText, string rejectText)
        {
            _title = title;
            _message = message;
            _acceptText = acceptText;
            _rejectText = rejectText;
        }

        protected override byte[] BuildTemplate()
        {
            var b = new DialogBuilder(_title, 300, 138);
            b.MessageText(IdText, _message, 10, 10, 280, 92);
            b.DefButton(IdAccept, _acceptText, 10, 110, 110, 16);
            b.Button(IdReject, _rejectText, 128, 110, 90, 16);
            return b.Build();
        }

        protected override void OnInit() => Focus(IdAccept);

        protected override bool OnCommand(int id, int code)
        {
            if (id == IdAccept) { Close(1); return true; }
            if (id == IdReject || id == Win32.IDCANCEL) { Close(0); return true; }
            return false;
        }
    }

    /// <summary>
    /// "Recording Settings" — every control from the Python dialog, in the same
    /// order, with the same mnemonics and the same validation.
    /// </summary>
    internal sealed class SettingsDialog : DialogBase
    {
        internal const int IdSort = 2001;
        internal const int IdDevice1 = 2002;
        internal const int IdDevice2 = 2003;
        internal const int IdFolder = 2004;
        internal const int IdChangeFolder = 2005;
        internal const int IdSampleRate = 2006;
        internal const int IdBitDepth = 2007;
        internal const int IdChannels = 2008;
        internal const int IdBuffer = 2009;
        internal const int IdPrefix = 2010;
        internal const int IdAutoStart = 2011;
        internal const int IdDelay = 2012;
        internal const int IdMaxLen = 2013;
        internal const int IdSplit = 2014;
        internal const int IdGroupSplits = 2015;
        internal const int IdTitle = 2016;
        internal const int IdUpdateStartup = 2017;
        internal const int IdCheckUpdates = 2018;
        internal const int IdNotifications = 2019;
        internal const int IdSounds = 2020;
        internal const int IdChannelsCfg = 2021;
        internal const int IdCopyLogs = 2022;
        internal const int IdDelayUnit = 2023;
        internal const int IdMaxLenUnit = 2024;
        internal const int IdSplitUnit = 2025;

        private readonly Config _cfg;
        private readonly List<AudioDevice> _devices;

        // Combo item data is an index into this list; -1 means "None".
        private readonly List<string> _deviceIds = new();

        private DurationField _delay, _maxLen, _split;

        public SettingsDialog(Config cfg, List<AudioDevice> devices)
        {
            _cfg = cfg;
            _devices = devices;
        }

        protected override byte[] BuildTemplate()
        {
            var b = new DialogBuilder("Recording Settings", 340, 402);

            b.GroupBox("Audio Devices", 4, 4, 332, 56);
            b.Label("Device &Sort Order:", 10, 17, 84, 9);
            b.Combo(IdSort, 98, 14, 230, 120);
            b.Label("Primary Input &1:", 10, 31, 84, 9);
            b.Combo(IdDevice1, 98, 28, 230, 160);
            b.Label("Secondary Input &2:", 10, 45, 84, 9);
            b.Combo(IdDevice2, 98, 42, 230, 160);

            b.GroupBox("Output Directory", 4, 62, 332, 28);
            b.Edit(IdFolder, 10, 72, 240, 13, Win32.ES_READONLY);
            b.Button(IdChangeFolder, "C&hange Folder", 256, 71, 72, 15);

            b.GroupBox("Audio Format Settings", 4, 92, 332, 86);
            b.Label("To avoid downsampling/resampling garbage, select the\r\nEXACT rate your Windows device is set to!",
                10, 102, 318, 18);
            b.Label("Sa&mple Rate (Hz):", 10, 125, 84, 9);
            b.Combo(IdSampleRate, 98, 122, 110, 120);
            b.Label("&Bit Depth:", 10, 139, 84, 9);
            b.Combo(IdBitDepth, 98, 136, 110, 100);
            b.Label("&Channels:", 10, 153, 84, 9);
            b.Combo(IdChannels, 98, 150, 110, 80);
            b.Label("B&uffer Size (Frames):", 10, 167, 84, 9);
            b.Combo(IdBuffer, 98, 164, 110, 100);

            b.GroupBox("File, Delay && Auto-Split Settings", 4, 180, 332, 100);
            b.Label("File &Prefix:", 10, 193, 84, 9);
            b.Edit(IdPrefix, 98, 190, 230, 13);
            b.CheckBox(IdAutoStart, "Auto-st&art recording on launch", 10, 206, 318, 13);
            // Each duration is a number plus a unit combo, so a two hour split
            // is "2" and "Hours" rather than a very long press of the up arrow.
            b.Label("Start De&lay:", 10, 225, 104, 9);
            b.Edit(IdDelay, 118, 222, 46, 13);
            b.Combo(IdDelayUnit, 168, 222, 78, 90);
            b.Label("Max Recording Length (0=off):", 10, 239, 104, 9);
            b.Edit(IdMaxLen, 118, 236, 46, 13);
            b.Combo(IdMaxLenUnit, 168, 236, 78, 90);
            b.Label("Time Auto-S&plit every:", 10, 253, 104, 9);
            b.Edit(IdSplit, 118, 250, 46, 13);
            b.Combo(IdSplitUnit, 168, 250, 78, 90);
            b.CheckBox(IdGroupSplits, "Automatically place all splits into a unified folder", 10, 264, 318, 13);

            b.GroupBox("Appearance && Extra Options", 4, 282, 332, 96);
            b.Label("Window &Title:", 10, 295, 84, 9);
            b.Edit(IdTitle, 98, 292, 230, 13);
            b.CheckBox(IdUpdateStartup, "Check for &updates on startup", 10, 308, 318, 13);
            b.Button(IdCheckUpdates, "Check for &Updates Now", 10, 324, 150, 15);
            b.Button(IdNotifications, "Configure Notifications", 168, 324, 150, 15);
            b.Button(IdSounds, "Configure Sounds", 10, 342, 150, 15);
            b.Button(IdChannelsCfg, "Configure Audio Channels", 168, 342, 150, 15);
            b.Button(IdCopyLogs, "Copy Diagnostic Logs", 10, 360, 150, 15);

            b.DefButton(Win32.IDOK, "Save && Close", 4, 382, 80, 15);
            return b.Build();
        }

        protected override void OnInit()
        {
            Win32.ComboAdd(Hwnd, IdSort, "Inputs First", 0);
            Win32.ComboAdd(Hwnd, IdSort, "Outputs First", 0);
            Win32.ComboSelectByText(Hwnd, IdSort, _cfg.DeviceSortOrder);

            PopulateDevices();

            foreach (string r in new[] { "44100", "48000", "88200", "96000", "192000", "384000" })
                Win32.ComboAdd(Hwnd, IdSampleRate, r, 0);
            foreach (string r in new[] { "16", "24", "32" })
                Win32.ComboAdd(Hwnd, IdBitDepth, r, 0);
            foreach (string r in new[] { "1 (Mono)", "2 (Stereo)" })
                Win32.ComboAdd(Hwnd, IdChannels, r, 0);
            foreach (string r in new[] { "512", "1024", "2048", "4096", "8192" })
                Win32.ComboAdd(Hwnd, IdBuffer, r, 0);

            Win32.ComboSelectByText(Hwnd, IdSampleRate, _cfg.SampleRate);
            Win32.ComboSelectByText(Hwnd, IdBitDepth, _cfg.BitDepth);
            Win32.ComboSetSel(Hwnd, IdChannels, _cfg.Channels == "1" ? 0 : 1);
            Win32.ComboSelectByText(Hwnd, IdBuffer, _cfg.BufferSize.ToString(CultureInfo.InvariantCulture));

            Text(IdFolder, _cfg.SaveFolder);
            Text(IdPrefix, _cfg.FilenamePrefix);
            Text(IdTitle, _cfg.WindowTitle);
            Checked(IdAutoStart, _cfg.AutoStart);
            Checked(IdGroupSplits, _cfg.GroupSplits);
            Checked(IdUpdateStartup, _cfg.CheckUpdatesStartup);

            // Same ranges as the Python spin boxes, including the max-length cap
            // that keeps the timer inside a 32-bit millisecond count.
            _delay = new DurationField(Hwnd, IdDelay, IdDelayUnit, 3600);
            _maxLen = new DurationField(Hwnd, IdMaxLen, IdMaxLenUnit, 2000000);
            _split = new DurationField(Hwnd, IdSplit, IdSplitUnit, 3600 * 24);

            _delay.TotalSeconds = _cfg.AutoStartDelay;
            _maxLen.TotalSeconds = _cfg.MaxLengthSecs;
            _split.TotalSeconds = _cfg.AutoSplitSecs;
        }

        private void PopulateDevices()
        {
            _deviceIds.Clear();

            // Sort order matches the Python key: inputs before loopbacks (or the
            // reverse), then by name. It is applied when the dialog opens, so a
            // change takes effect the next time Settings is opened.
            bool inputsFirst = _cfg.DeviceSortOrder != "Outputs First";
            var sorted = new List<AudioDevice>(_devices);
            sorted.Sort((a, x) =>
            {
                bool ka = inputsFirst ? a.IsLoopback : !a.IsLoopback;
                bool kx = inputsFirst ? x.IsLoopback : !x.IsLoopback;
                if (ka != kx) return ka ? 1 : -1;
                return string.Compare(a.Name, x.Name, StringComparison.Ordinal);
            });

            Win32.ComboAdd(Hwnd, IdDevice2, "None", -1);

            foreach (var d in sorted)
            {
                int data = _deviceIds.Count;
                _deviceIds.Add(d.Id);
                Win32.ComboAdd(Hwnd, IdDevice1, d.DisplayName, data);
                Win32.ComboAdd(Hwnd, IdDevice2, d.DisplayName, data);
            }

            SelectDevice(IdDevice1, _cfg.DeviceId, 0);
            SelectDevice(IdDevice2, _cfg.Device2Id, 1);
        }

        private void SelectDevice(int comboId, string wantedId, int insertAt)
        {
            if (string.IsNullOrEmpty(wantedId)) { Win32.ComboSetSel(Hwnd, comboId, 0); return; }
            if (wantedId == "none") { Win32.ComboSetSel(Hwnd, comboId, 0); return; }

            int index = _deviceIds.IndexOf(wantedId);
            if (index >= 0)
            {
                int count = (int)Win32.SendDlgItemMessageW(Hwnd, comboId, Win32.CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
                for (int i = 0; i < count; i++)
                {
                    int data = (int)Win32.SendDlgItemMessageW(Hwnd, comboId, Win32.CB_GETITEMDATA, (IntPtr)i, IntPtr.Zero);
                    if (data == index) { Win32.ComboSetSel(Hwnd, comboId, i); return; }
                }
            }

            // Keep an absent device selected and clearly labelled rather than
            // silently falling back to another one.
            int newData = _deviceIds.Count;
            _deviceIds.Add(wantedId);
            Win32.ComboInsert(Hwnd, comboId, insertAt, "DISCONNECTED: " + wantedId, newData);
            Win32.ComboSetSel(Hwnd, comboId, insertAt);
        }

        private string SelectedDeviceId(int comboId)
        {
            int data = Win32.ComboGetData(Hwnd, comboId);
            if (data < 0 || data >= _deviceIds.Count) return "none";
            return _deviceIds[data];
        }

        protected override bool OnCommand(int id, int code)
        {
            // A unit change re-labels and re-clamps its own number field.
            if (_delay != null && (_delay.HandleCommand(id, code) ||
                                   _maxLen.HandleCommand(id, code) ||
                                   _split.HandleCommand(id, code)))
                return true;

            switch (id)
            {
                case IdChangeFolder:
                    ChooseFolder();
                    return true;

                case IdCheckUpdates:
                    Updater.CheckNow(Hwnd);
                    return true;

                case IdNotifications:
                    new NotificationsDialog(_cfg).ShowModal(Hwnd);
                    return true;

                case IdSounds:
                    new SoundsDialog(_cfg).ShowModal(Hwnd);
                    return true;

                case IdChannelsCfg:
                    new ChannelsDialog(_cfg).ShowModal(Hwnd);
                    return true;

                case IdCopyLogs:
                    CopyLogs();
                    return true;

                case Win32.IDOK:
                    SaveAndClose();
                    return true;

                case Win32.IDCANCEL:
                    Close(0);
                    return true;
            }
            return false;
        }

        private void CopyLogs()
        {
            try
            {
                Win32.SetClipboardText(Hwnd, Log.ReadAll());
                Info("Diagnostic logs have been copied to your clipboard!", "Logs Copied");
            }
            catch (Exception e)
            {
                Critical("Failed to copy logs: " + e.Message, "Error");
            }
        }

        private void ChooseFolder()
        {
            string picked = FolderPicker.Pick(Hwnd, "Select Output Directory", Text(IdFolder));
            if (string.IsNullOrEmpty(picked)) return;

            string root = Recorder.DriveRootOf(picked);
            if (root != null && !Recorder.IsSystemDrive(root))
            {
                string drive = root.TrimEnd('\\', '/');
                var dlg = new ConfirmDialog("External Drive Warning",
                    "You have selected a directory on drive " + drive + ".\n\n" +
                    "While this will work perfectly, it can be dangerous. If this is a removable USB drive or " +
                    "external hard drive and it gets disconnected or bumped while the app is running, your " +
                    "recording could be abruptly terminated.\n\n" +
                    "Please ensure the drive remains securely connected.",
                    "I understand", "Revert");

                if ((long)dlg.ShowModal(Hwnd) != 1) return;
            }

            Text(IdFolder, picked);
        }

        private void SaveAndClose()
        {
            string d1 = SelectedDeviceId(IdDevice1);
            string d2 = SelectedDeviceId(IdDevice2);

            if (d2 != "none" && d1 == d2)
            {
                Warn("Input 1 and Input 2 cannot be the same device.", "Invalid Selection");
                return;
            }

            if (d2 != "none" && d1 != d2)
            {
                bool proceed = AskYesNo(
                    "You have selected two independent audio devices.\r\n\r\n" +
                    "Because they use separate hardware clocks, they will naturally drift apart over time. " +
                    "This application does not perform adaptive resampling. Over long recordings, you may experience " +
                    "audio dropouts, clicks, or sync issues as the application drops blocks to keep them aligned.\r\n\r\n" +
                    "Do you still want to use two independent devices?",
                    "Hardware Clock Drift Warning");
                if (!proceed) return;
            }

            _cfg.DeviceId = d1;
            _cfg.Device2Id = d2;

            _cfg.AutoStart = Checked(IdAutoStart);
            _cfg.AutoStartDelay = _delay.TotalSeconds;
            _cfg.AutoSplitSecs = _split.TotalSeconds;
            _cfg.MaxLengthSecs = _maxLen.TotalSeconds;
            _cfg.GroupSplits = Checked(IdGroupSplits);

            _cfg.SampleRate = Win32.ComboGetText(Hwnd, IdSampleRate);
            _cfg.BitDepth = Win32.ComboGetText(Hwnd, IdBitDepth);
            _cfg.Channels = Win32.ComboGetSel(Hwnd, IdChannels) == 0 ? "1" : "2";
            _cfg.FilenamePrefix = Text(IdPrefix);

            string bufText = Win32.ComboGetText(Hwnd, IdBuffer);
            if (int.TryParse(bufText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int buf) && buf > 0)
                _cfg.BufferSize = buf;

            _cfg.WindowTitle = Text(IdTitle);
            _cfg.CheckUpdatesStartup = Checked(IdUpdateStartup);
            _cfg.SaveFolder = Text(IdFolder);
            _cfg.DeviceSortOrder = Win32.ComboGetText(Hwnd, IdSort);

            _cfg.Save();
            Close(1);
        }
    }
}
