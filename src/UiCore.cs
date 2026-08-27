using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Arp
{
    /// <summary>
    /// Shared plumbing for the in-memory dialogs: keeps the dialog procedure
    /// delegate alive for the lifetime of the window (the GC would otherwise
    /// collect it out from under Windows) and routes messages to overrides.
    /// </summary>
    internal abstract class DialogBase
    {
        private Win32.DlgProc _proc;
        protected IntPtr Hwnd;

        protected abstract byte[] BuildTemplate();

        /// <summary>Lets the self-test inspect a template without reflection.</summary>
        internal byte[] TemplateForTest() => BuildTemplate();

        protected virtual void OnInit() { }

        /// <summary>Return true when the command was handled.</summary>
        protected virtual bool OnCommand(int id, int notifyCode) => false;

        protected virtual bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            result = IntPtr.Zero;
            return false;
        }

        protected virtual void OnDestroy() { }

        public IntPtr ShowModal(IntPtr owner)
        {
            _proc = DlgProc;
            return Win32.DialogBoxIndirectParamW(IntPtr.Zero, BuildTemplate(), owner, _proc, IntPtr.Zero);
        }

        public IntPtr CreateModeless(IntPtr owner)
        {
            _proc = DlgProc;
            Hwnd = Win32.CreateDialogIndirectParamW(IntPtr.Zero, BuildTemplate(), owner, _proc, IntPtr.Zero);
            if (Hwnd == IntPtr.Zero)
                throw new InvalidOperationException("CreateDialogIndirectParam failed, error " + Win32.GetLastError());
            return Hwnd;
        }

        protected void Close(int result) => Win32.EndDialog(Hwnd, (IntPtr)result);

        private IntPtr DlgProc(IntPtr hDlg, uint msg, IntPtr wParam, IntPtr lParam)
        {
            Hwnd = hDlg;
            switch (msg)
            {
                case Win32.WM_INITDIALOG:
                    OnInit();
                    return (IntPtr)1;

                case Win32.WM_COMMAND:
                {
                    int id = (int)((long)wParam & 0xFFFF);
                    int code = (int)(((long)wParam >> 16) & 0xFFFF);
                    if (OnCommand(id, code)) return (IntPtr)1;
                    return IntPtr.Zero;
                }

                case Win32.WM_DESTROY:
                    OnDestroy();
                    return IntPtr.Zero;

                default:
                    if (OnMessage(msg, wParam, lParam, out IntPtr r)) return r;
                    return IntPtr.Zero;
            }
        }

        // ---- convenience wrappers ----
        protected string Text(int id) => Win32.GetDlgItemText(Hwnd, id);
        protected void Text(int id, string s) => Win32.SetDlgItemTextW(Hwnd, id, s ?? string.Empty);
        protected bool Checked(int id) => Win32.IsChecked(Hwnd, id);
        protected void Checked(int id, bool v) => Win32.SetChecked(Hwnd, id, v);
        protected void Enable(int id, bool v) => Win32.EnableWindow(Win32.GetDlgItem(Hwnd, id), v);
        protected void Focus(int id) => Win32.SetFocus(Win32.GetDlgItem(Hwnd, id));

        protected int Info(string text, string caption) =>
            Win32.MessageBoxW(Hwnd, text, caption, Win32.MB_OK | Win32.MB_ICONINFORMATION);

        protected int Warn(string text, string caption) =>
            Win32.MessageBoxW(Hwnd, text, caption, Win32.MB_OK | Win32.MB_ICONWARNING);

        protected int Critical(string text, string caption) =>
            Win32.MessageBoxW(Hwnd, text, caption, Win32.MB_OK | Win32.MB_ICONERROR);

        protected bool AskYesNo(string text, string caption, bool defaultNo = true) =>
            Win32.MessageBoxW(Hwnd, text, caption,
                Win32.MB_YESNO | Win32.MB_ICONQUESTION | (defaultNo ? Win32.MB_DEFBUTTON2 : 0)) == Win32.IDYES;
    }

    /// <summary>
    /// An edit control that behaves like the Python build's spin boxes: Up and
    /// Down step the value, Home and End jump to the limits, and the text is
    /// always the spoken form ("1 hour, 30 minutes" / "15 percent") rather than
    /// a bare number. The new value is spoken explicitly on each change, because
    /// a plain EDIT does not raise a value-changed event a screen reader would
    /// otherwise pick up.
    /// </summary>
    internal sealed class SpinEdit
    {
        private readonly IntPtr _hwnd;
        private readonly IntPtr _oldProc;
        private readonly Win32.WndProc _proc; // must outlive the control
        private readonly Func<int, string> _format;
        private readonly Func<string, int> _parse;
        private readonly int _min, _step;

        private static readonly List<SpinEdit> Alive = new(); // pins instances

        /// <summary>Upper bound, which moves when a duration field changes unit.</summary>
        public int Max { get; set; }

        private int _max => Max;

        public SpinEdit(IntPtr dlg, int controlId, int min, int max, int step,
            Func<int, string> format, Func<string, int> parse)
        {
            _hwnd = Win32.GetDlgItem(dlg, controlId);
            _min = min;
            Max = max;
            _step = step;
            _format = format;
            _parse = parse;

            _proc = Proc;
            IntPtr fp = Marshal.GetFunctionPointerForDelegate(_proc);
            _oldProc = Win32.SetWindowLongPtr(_hwnd, Win32.GWLP_WNDPROC, fp);

            lock (Alive) Alive.Add(this);
        }

        public int Value
        {
            get => Math.Clamp(_parse(GetText()), _min, _max);
            set
            {
                int v = Math.Clamp(value, _min, _max);
                Win32.SetWindowTextW(_hwnd, _format(v));
            }
        }

        /// <summary>Rewrites free text as the canonical form, e.g. "90m" to "1 hour, 30 minutes".</summary>
        public void Normalize() => Value = Value;

        private string GetText()
        {
            int len = (int)Win32.SendMessageW(_hwnd, Win32.WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            var sb = new System.Text.StringBuilder(len + 2);
            Win32.SendMessageSb(_hwnd, Win32.WM_GETTEXT, (IntPtr)sb.Capacity, sb);
            return sb.ToString();
        }

        private IntPtr Proc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == Win32.WM_KEYDOWN)
            {
                int key = (int)wParam;
                int current = Value;
                int next = current;
                switch (key)
                {
                    case Win32.VK_UP: next = current + _step; break;
                    case Win32.VK_DOWN: next = current - _step; break;
                    case Win32.VK_HOME: next = _min; break;
                    case Win32.VK_END: next = _max; break;
                    default: return Win32.CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
                }

                next = Math.Clamp(next, _min, _max);
                Value = next;
                // Put the caret at the end so the control does not read as
                // partially selected after the rewrite.
                Win32.SendMessageW(_hwnd, Win32.EM_SETSEL, (IntPtr)(-1), (IntPtr)(-1));
                Speech.SpeakRaw(_format(next));
                return IntPtr.Zero;
            }

            if (msg == 0x0008) // WM_KILLFOCUS
            {
                Normalize();
                return Win32.CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
            }

            return Win32.CallWindowProc(_oldProc, hWnd, msg, wParam, lParam);
        }
    }

    /// <summary>
    /// A duration entered as a plain number plus a unit chosen from a combo box
    /// (Seconds, Minutes, Hours).
    ///
    /// The Python build used a single field holding the whole duration in
    /// seconds, so setting a two hour split meant arrowing 7200 times or
    /// knowing the shorthand. Splitting the unit out means typing "2" and
    /// picking "Hours". Arrow keys still nudge the number.
    /// </summary>
    internal sealed class DurationField
    {
        internal static readonly string[] UnitNames = { "Seconds", "Minutes", "Hours" };
        private static readonly int[] UnitSeconds = { 1, 60, 3600 };

        private readonly IntPtr _dlg;
        private readonly int _editId;
        private readonly int _comboId;
        private readonly int _maxSeconds;
        private readonly SpinEdit _spin;

        public DurationField(IntPtr dlg, int editId, int comboId, int maxSeconds)
        {
            _dlg = dlg;
            _editId = editId;
            _comboId = comboId;
            _maxSeconds = maxSeconds;

            foreach (string u in UnitNames) Win32.ComboAdd(dlg, comboId, u, 0);
            Win32.ComboSetSel(dlg, comboId, 0);

            _spin = new SpinEdit(dlg, editId, 0, maxSeconds, 1,
                n => n.ToString(CultureInfo.InvariantCulture) + " " + CurrentUnitName(n),
                ParseNumber);
        }

        private int UnitIndex
        {
            get
            {
                int i = Win32.ComboGetSel(_dlg, _comboId);
                return i < 0 || i >= UnitSeconds.Length ? 0 : i;
            }
        }

        private int Multiplier => UnitSeconds[UnitIndex];

        /// <summary>Singularised unit name, so a screen reader says "1 minute".</summary>
        private string CurrentUnitName(int quantity)
        {
            string name = UnitNames[UnitIndex];
            return quantity == 1 ? name.Substring(0, name.Length - 1).ToLowerInvariant() : name.ToLowerInvariant();
        }

        private static int ParseNumber(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int i = 0;
            while (i < text.Length && !char.IsAsciiDigit(text[i])) i++;
            int start = i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            if (i == start) return 0;
            return long.TryParse(text.AsSpan(start, i - start), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long v)
                ? (v > int.MaxValue ? int.MaxValue : (int)v)
                : 0;
        }

        /// <summary>
        /// Picks the largest unit that divides the duration exactly, so 5400
        /// reads back as 90 minutes rather than 5400 seconds, and 7200 as
        /// 2 hours.
        /// </summary>
        internal static (int Quantity, int UnitIndex) Decompose(int totalSeconds)
        {
            if (totalSeconds <= 0) return (0, 0);
            if (totalSeconds % 3600 == 0) return (totalSeconds / 3600, 2);
            if (totalSeconds % 60 == 0) return (totalSeconds / 60, 1);
            return (totalSeconds, 0);
        }

        public int TotalSeconds
        {
            get
            {
                long total = (long)ParseNumber(GetEditText()) * Multiplier;
                if (total < 0) return 0;
                return total > _maxSeconds ? _maxSeconds : (int)total;
            }
            set
            {
                var (quantity, unit) = Decompose(Math.Clamp(value, 0, _maxSeconds));
                Win32.ComboSetSel(_dlg, _comboId, unit);
                UpdateMax();
                Win32.SetDlgItemTextW(_dlg, _editId,
                    quantity.ToString(CultureInfo.InvariantCulture) + " " + CurrentUnitName(quantity));
            }
        }

        private string GetEditText() => Win32.GetDlgItemText(_dlg, _editId);

        private void UpdateMax() => _spin.Max = Math.Max(1, _maxSeconds / Multiplier);

        /// <summary>
        /// Re-labels the number when the unit changes and clamps it to the new
        /// ceiling. Returns true when the command belonged to this field.
        /// </summary>
        public bool HandleCommand(int id, int code)
        {
            if (id != _comboId || code != Win32.CBN_SELCHANGE) return false;

            UpdateMax();
            int quantity = Math.Clamp(ParseNumber(GetEditText()), 0, _spin.Max);
            string text = quantity.ToString(CultureInfo.InvariantCulture) + " " + CurrentUnitName(quantity);
            Win32.SetDlgItemTextW(_dlg, _editId, text);
            return true;
        }

        /// <summary>Rewrites the field as the canonical "&lt;n&gt; &lt;unit&gt;" form.</summary>
        public void Normalize() => TotalSeconds = TotalSeconds;
    }

    internal static class FolderPicker
    {
        // IFileOpenDialog with FOS_PICKFOLDERS. The modern dialog is used rather
        // than SHBrowseForFolder because screen readers navigate it far better.
        private static readonly Guid CLSID_FileOpenDialog = new("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid IID_IFileOpenDialog = new("D57C7288-D4AD-4768-BE02-9D969532D960");

        private const int FOS_PICKFOLDERS = 0x00000020;
        private const int FOS_FORCEFILESYSTEM = 0x00000040;
        private const int FOS_PATHMUSTEXIST = 0x00000800;
        private const int SIGDN_FILESYSPATH = unchecked((int)0x80058000);

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid clsid, IntPtr outer, int ctx, in Guid iid, out IntPtr obj);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr p);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bc, in Guid iid, out IntPtr item);

        private static readonly Guid IID_IShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        public static unsafe string Pick(IntPtr owner, string title, string initialFolder)
        {
            IntPtr dlg = IntPtr.Zero, result = IntPtr.Zero, initial = IntPtr.Zero;
            try
            {
                if (CoCreateInstance(CLSID_FileOpenDialog, IntPtr.Zero, 1, IID_IFileOpenDialog, out dlg) < 0)
                    return null;

                void** vt = *(void***)dlg;

                // IFileDialog: SetOptions is slot 9, GetOptions slot 10,
                // SetFolder slot 12, SetTitle slot 17, Show slot 3,
                // GetResult slot 20.
                int options;
                ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)vt[10])(dlg, &options);
                options |= FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST;
                ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)vt[9])(dlg, options);

                if (!string.IsNullOrEmpty(title))
                {
                    fixed (char* t = title)
                        ((delegate* unmanaged[Stdcall]<IntPtr, char*, int>)vt[17])(dlg, t);
                }

                if (!string.IsNullOrEmpty(initialFolder) && System.IO.Directory.Exists(initialFolder))
                {
                    if (SHCreateItemFromParsingName(initialFolder, IntPtr.Zero, IID_IShellItem, out initial) >= 0)
                        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vt[12])(dlg, initial);
                }

                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)vt[3])(dlg, owner);
                if (hr < 0) return null; // includes the user cancelling

                if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)vt[20])(dlg, &result) < 0 || result == IntPtr.Zero)
                    return null;

                void** ivt = *(void***)result;
                IntPtr pathPtr;
                // IShellItem::GetDisplayName is slot 5.
                if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)ivt[5])(result, SIGDN_FILESYSPATH, &pathPtr) < 0)
                    return null;

                try { return Marshal.PtrToStringUni(pathPtr); }
                finally { CoTaskMemFree(pathPtr); }
            }
            catch (Exception e)
            {
                Log.Warn("Folder picker failed: " + e.Message);
                return null;
            }
            finally
            {
                Wasapi.Release(initial);
                Wasapi.Release(result);
                Wasapi.Release(dlg);
            }
        }
    }
}
