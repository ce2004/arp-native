using System;
using System.Collections.Generic;

namespace Arp
{
    /// <summary>
    /// "Notification Settings" — the eight toggles from the Python dialog, in
    /// the same order.
    /// </summary>
    internal sealed class NotificationsDialog : DialogBase
    {
        internal const int IdStartStop = 1101;
        internal const int IdSplit = 1102;
        internal const int IdError = 1103;
        internal const int IdDrive = 1104;
        internal const int IdMic = 1105;
        internal const int IdConfirmExit = 1106;
        internal const int IdFocusSpeak = 1107;
        internal const int IdAutoResume = 1108;

        private readonly Config _cfg;

        public NotificationsDialog(Config cfg) => _cfg = cfg;

        protected override byte[] BuildTemplate()
        {
            var b = new DialogBuilder("Notification Settings", 300, 172);
            int y = 8;
            void Row(int id, string text)
            {
                b.CheckBox(id, text, 8, y, 284, 14);
                y += 16;
            }

            Row(IdStartStop, "Notify on Start/Stop Recording");
            Row(IdSplit, "Notify on Auto-Split");
            Row(IdError, "Notify on recording errors");
            Row(IdDrive, "Notify when output drive disconnects");
            Row(IdMic, "Notify on microphone disconnect");
            Row(IdConfirmExit, "Confirm exit while recording");
            Row(IdFocusSpeak, "Speak announcements only when window is in focus");
            Row(IdAutoResume, "Auto-resume unattended recording if drive/mic reconnects");

            b.DefButton(Win32.IDOK, "Save && Close", 8, y + 6, 80, 16);
            return b.Build();
        }

        protected override void OnInit()
        {
            Checked(IdStartStop, _cfg.NotifyStartStop);
            Checked(IdSplit, _cfg.NotifySplit);
            Checked(IdError, _cfg.NotifyError);
            Checked(IdDrive, _cfg.NotifyDriveDisconnect);
            Checked(IdMic, _cfg.NotifyMicDisconnect);
            Checked(IdConfirmExit, _cfg.ConfirmExit);
            Checked(IdFocusSpeak, _cfg.SpeakInFocusOnly);
            Checked(IdAutoResume, _cfg.AutoResumeUnattended);
        }

        protected override bool OnCommand(int id, int code)
        {
            if (id == Win32.IDOK)
            {
                _cfg.NotifyStartStop = Checked(IdStartStop);
                _cfg.NotifySplit = Checked(IdSplit);
                _cfg.NotifyError = Checked(IdError);
                _cfg.NotifyDriveDisconnect = Checked(IdDrive);
                _cfg.NotifyMicDisconnect = Checked(IdMic);
                _cfg.SpeakInFocusOnly = Checked(IdFocusSpeak);
                _cfg.AutoResumeUnattended = Checked(IdAutoResume);
                _cfg.ConfirmExit = Checked(IdConfirmExit);
                _cfg.Save();
                Close(1);
                return true;
            }
            if (id == Win32.IDCANCEL) { Close(0); return true; }
            return false;
        }
    }

    /// <summary>
    /// "Configure Sounds" — for each event, whether it plays and which of the
    /// built-in sounds it uses.
    ///
    /// Choosing a sound plays it immediately. Picking a cue by name is
    /// meaningless without hearing it, so arrowing through the list auditions
    /// each one at the volume it will actually be used at.
    /// </summary>
    internal sealed class SoundsDialog : DialogBase
    {
        internal const int IdStart = 1201;
        internal const int IdStop = 1202;
        internal const int IdPause = 1203;
        internal const int IdUnpause = 1204;
        internal const int IdVolume = 1205;

        internal const int IdStartSound = 1211;
        internal const int IdStopSound = 1212;
        internal const int IdPauseSound = 1213;
        internal const int IdUnpauseSound = 1214;

        private static readonly (string Event, int Check, int Combo, string Label)[] Rows =
        {
            ("start",   IdStart,   IdStartSound,   "Play sound on &Start"),
            ("stop",    IdStop,    IdStopSound,    "Play sound on Sto&p"),
            ("pause",   IdPause,   IdPauseSound,   "Play sound on Pau&se"),
            ("unpause", IdUnpause, IdUnpauseSound, "Play sound on &Unpause"),
        };

        private readonly Config _cfg;
        private SpinEdit _volume;
        private bool _ready; // suppresses previews while populating

        public SoundsDialog(Config cfg) => _cfg = cfg;

        protected override byte[] BuildTemplate()
        {
            var b = new DialogBuilder("Configure Sounds", 300, 130);
            int y = 8;
            foreach (var r in Rows)
            {
                b.CheckBox(r.Check, r.Label, 8, y + 1, 118, 13);
                b.Combo(r.Combo, 132, y, 160, 120);
                y += 18;
            }

            b.Label("Sound Effects &Volume:", 8, y + 4, 100, 10);
            b.Edit(IdVolume, 132, y, 90, 13);
            y += 20;

            b.DefButton(Win32.IDOK, "Save && Close", 8, y + 4, 80, 16);
            return b.Build();
        }

        protected override void OnInit()
        {
            Checked(IdStart, _cfg.SndStart);
            Checked(IdStop, _cfg.SndStop);
            Checked(IdPause, _cfg.SndPause);
            Checked(IdUnpause, _cfg.SndUnpause);

            foreach (var r in Rows)
            {
                foreach (string name in SoundLibrary.Names)
                    Win32.ComboAdd(Hwnd, r.Combo, name, 0);
                Win32.ComboSelectByText(Hwnd, r.Combo, _cfg.SoundFor(r.Event));
            }

            _volume = new SpinEdit(Hwnd, IdVolume, 1, 100, 1,
                PercentText.Format, t => PercentText.Parse(t, _cfg.SndVolume));
            _volume.Value = _cfg.SndVolume;

            _ready = true;
        }

        protected override bool OnCommand(int id, int code)
        {
            if (_ready && code == Win32.CBN_SELCHANGE)
            {
                foreach (var r in Rows)
                {
                    if (r.Combo != id) continue;
                    Sounds.Play(Win32.ComboGetText(Hwnd, id), _volume?.Value ?? _cfg.SndVolume);
                    return true;
                }
            }

            if (id == Win32.IDOK)
            {
                _cfg.SndStart = Checked(IdStart);
                _cfg.SndStop = Checked(IdStop);
                _cfg.SndPause = Checked(IdPause);
                _cfg.SndUnpause = Checked(IdUnpause);
                _cfg.SndVolume = _volume.Value;

                foreach (var r in Rows)
                    _cfg.SetSoundFor(r.Event, Win32.ComboGetText(Hwnd, r.Combo));

                _cfg.Save();
                Close(1);
                return true;
            }
            if (id == Win32.IDCANCEL) { Close(0); return true; }
            return false;
        }
    }

    /// <summary>"Configure Audio Channels" — per-input routing and the mic-loss policy.</summary>
    internal sealed class ChannelsDialog : DialogBase
    {
        internal const int IdIn1 = 1301;
        internal const int IdIn2 = 1302;
        internal const int IdContinue = 1303;

        private static readonly string[] Routes = { "Both Channels", "Left Channel Only", "Right Channel Only" };

        private readonly Config _cfg;

        public ChannelsDialog(Config cfg) => _cfg = cfg;

        protected override byte[] BuildTemplate()
        {
            var b = new DialogBuilder("Configure Audio Channels", 280, 92);
            b.Label("Input &1 Routing:", 8, 11, 90, 10);
            b.Combo(IdIn1, 102, 8, 168, 80);
            b.Label("Input &2 Routing:", 8, 33, 90, 10);
            b.Combo(IdIn2, 102, 30, 168, 80);
            b.CheckBox(IdContinue, "Continue recording if a mic disconnects", 8, 52, 264, 14);
            b.DefButton(Win32.IDOK, "Save && Close", 8, 70, 80, 16);
            return b.Build();
        }

        protected override void OnInit()
        {
            foreach (string r in Routes)
            {
                Win32.ComboAdd(Hwnd, IdIn1, r, 0);
                Win32.ComboAdd(Hwnd, IdIn2, r, 0);
            }
            Win32.ComboSelectByText(Hwnd, IdIn1, _cfg.In1Route);
            Win32.ComboSelectByText(Hwnd, IdIn2, _cfg.In2Route);
            Checked(IdContinue, _cfg.ContinueOnMicDisconnect);
        }

        protected override bool OnCommand(int id, int code)
        {
            if (id == Win32.IDOK)
            {
                _cfg.In1Route = Win32.ComboGetText(Hwnd, IdIn1);
                _cfg.In2Route = Win32.ComboGetText(Hwnd, IdIn2);
                _cfg.ContinueOnMicDisconnect = Checked(IdContinue);
                _cfg.Save();
                Close(1);
                return true;
            }
            if (id == Win32.IDCANCEL) { Close(0); return true; }
            return false;
        }
    }

    /// <summary>
    /// "Incomplete Recording Detected" — offers to rebuild the header of a file
    /// left behind by a crash. Returns 1 to repair, 2 to forget, 0 to leave alone.
    /// </summary>
    internal sealed class RepairDialog : DialogBase
    {
        internal const int IdText = 1401;
        internal const int IdRepair = 1402;
        internal const int IdLeave = 1403;
        internal const int IdForget = 1404;

        private readonly string _filepath;

        public RepairDialog(string filepath) => _filepath = filepath;

        protected override byte[] BuildTemplate()
        {
            // The message is static text so the screen reader speaks the whole
            // prompt as the dialog opens, and Tab then cycles the buttons.
            string message =
                "Audio Recorder Pro was closed unexpectedly during your last session, " +
                "and a recording may not have been finalized correctly.\n\n" +
                "File: " + _filepath + "\n\n" +
                "Would you like to attempt to repair this audio file now?";

            var b = new DialogBuilder("Incomplete Recording Detected", 320, 138);
            b.MessageText(IdText, message, 10, 10, 300, 76);
            b.DefButton(IdRepair, "Yes, &Repair Recording", 10, 94, 104, 16);
            b.Button(IdLeave, "&No, Leave it alone", 120, 94, 96, 16);
            b.Button(IdForget, "&Forget this recovery information", 10, 116, 150, 16);
            return b.Build();
        }

        protected override void OnInit() => Focus(IdRepair);

        protected override bool OnCommand(int id, int code)
        {
            switch (id)
            {
                case IdRepair: Close(1); return true;
                case IdLeave:
                case Win32.IDCANCEL: Close(0); return true;
                case IdForget: Close(2); return true;
            }
            return false;
        }
    }

    /// <summary>
    /// "Update Available" — the release-notes list from the Python updater.
    /// Reachable once a real release feed is wired into <see cref="Updater"/>.
    /// </summary>
    internal sealed class UpdateDialog : DialogBase
    {
        internal const int IdInfo = 1501;
        internal const int IdWhatsNew = 1502;
        internal const int IdList = 1503;
        internal const int IdUpdate = 1504;
        internal const int IdSkip = 1505;

        private readonly string _current, _newVersion, _notes;

        public UpdateDialog(string current, string newVersion, string notes)
        {
            _current = current;
            _newVersion = newVersion;
            _notes = notes ?? string.Empty;
        }

        protected override byte[] BuildTemplate()
        {
            // The headline is static text so it is spoken as the dialog opens.
            // The notes stay a list, because that is genuinely a list of
            // changes worth arrowing through one at a time.
            string headline = "There's an update available. You will be upgrading from version " +
                              _current + " to " + _newVersion + ".";

            var b = new DialogBuilder("Update Available", 320, 190);
            b.MessageText(IdInfo, headline, 10, 8, 300, 20);
            b.Label("&What's new:", 10, 34, 100, 10, IdWhatsNew);
            b.ListBox(IdList, 10, 48, 300, 100);
            b.DefButton(IdUpdate, "&Update Now", 10, 158, 80, 16);
            b.Button(IdSkip, "&Don't Update", 96, 158, 80, 16);
            return b.Build();
        }

        protected override void OnInit()
        {
            foreach (string raw in _notes.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("-", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
                    line = line.Substring(1).Trim();
                if (line.Length > 0)
                    Win32.SendDlgItemMessageString(Hwnd, IdList, Win32.LB_ADDSTRING, IntPtr.Zero, line);
            }
            Win32.SendDlgItemMessageW(Hwnd, IdList, Win32.LB_SETCURSEL, IntPtr.Zero, IntPtr.Zero);
            Focus(IdUpdate);
        }

        protected override bool OnCommand(int id, int code)
        {
            if (id == IdUpdate) { Close(1); return true; }
            if (id == IdSkip || id == Win32.IDCANCEL) { Close(0); return true; }
            return false;
        }
    }
}
