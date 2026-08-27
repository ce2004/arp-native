using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Arp
{
    internal static class Win32
    {
        // ---- messages ----
        public const int WM_DESTROY = 0x0002;
        public const int WM_CLOSE = 0x0010;
        public const int WM_SETTEXT = 0x000C;
        public const int WM_GETTEXT = 0x000D;
        public const int WM_GETTEXTLENGTH = 0x000E;
        public const int WM_SETFONT = 0x0030;
        public const int WM_ACTIVATE = 0x0006;
        public const int WM_SETFOCUS = 0x0007;
        public const int WM_INITDIALOG = 0x0110;
        public const int WM_COMMAND = 0x0111;
        public const int WM_SYSCOMMAND = 0x0112;
        public const int WM_TIMER = 0x0113;
        public const int WM_KEYDOWN = 0x0100;
        public const int WM_APP = 0x8000;
        public const int WM_USER = 0x0400;
        public const int WM_QUERYENDSESSION = 0x0011;
        public const int WM_ENDSESSION = 0x0016;

        // Windows broadcasts volume arrival and removal to every top-level
        // window, with no registration needed. Listening costs nothing and
        // removes the need to poll the drive at all.
        public const int WM_DEVICECHANGE = 0x0219;
        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        public const int DBT_DEVICEQUERYREMOVE = 0x8001;
        public const int DBT_DEVNODES_CHANGED = 0x0007;
        public const int SC_CLOSE = 0xF060;

        // ---- button / combo / edit / listbox ----
        public const int BM_GETCHECK = 0x00F0;
        public const int BM_SETCHECK = 0x00F1;
        public const int BST_UNCHECKED = 0;
        public const int BST_CHECKED = 1;
        public const int BN_CLICKED = 0;

        public const int CB_ADDSTRING = 0x0143;
        public const int CB_GETCOUNT = 0x0146;
        public const int CB_GETCURSEL = 0x0147;
        public const int CB_GETLBTEXT = 0x0148;
        public const int CB_GETLBTEXTLEN = 0x0149;
        public const int CB_INSERTSTRING = 0x014A;
        public const int CB_RESETCONTENT = 0x014B;
        public const int CB_SETCURSEL = 0x014E;
        public const int CB_GETITEMDATA = 0x0150;
        public const int CB_SETITEMDATA = 0x0151;
        public const int CB_ERR = -1;
        public const int CBN_SELCHANGE = 1;

        public const int LB_ADDSTRING = 0x0180;
        public const int LB_RESETCONTENT = 0x0184;
        public const int LB_SETCURSEL = 0x0186;
        public const int LB_GETTEXT = 0x0189;
        public const int LB_GETTEXTLEN = 0x018A;
        public const int LB_GETCOUNT = 0x018B;
        public const int LB_SETHORIZONTALEXTENT = 0x0194;

        public const int EM_SETSEL = 0x00B1;
        public const int EM_SETREADONLY = 0x00CF;

        // ---- window styles ----
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_CHILD = 0x40000000;
        public const uint WS_VISIBLE = 0x10000000;
        public const uint WS_DISABLED = 0x08000000;
        public const uint WS_CLIPSIBLINGS = 0x04000000;
        public const uint WS_CAPTION = 0x00C00000;
        public const uint WS_BORDER = 0x00800000;
        public const uint WS_VSCROLL = 0x00200000;
        public const uint WS_HSCROLL = 0x00100000;
        public const uint WS_SYSMENU = 0x00080000;
        public const uint WS_THICKFRAME = 0x00040000;
        public const uint WS_GROUP = 0x00020000;
        public const uint WS_MINIMIZEBOX = 0x00020000;
        public const uint WS_TABSTOP = 0x00010000;
        public const uint WS_MAXIMIZEBOX = 0x00010000;

        public const uint WS_EX_APPWINDOW = 0x00040000;
        public const uint WS_EX_CONTROLPARENT = 0x00010000;
        public const uint WS_EX_CLIENTEDGE = 0x00000200;

        public const uint DS_SETFONT = 0x0040;
        public const uint DS_MODALFRAME = 0x0080;
        public const uint DS_CENTER = 0x0800;
        public const uint DS_NOIDLEMSG = 0x0100;

        // ---- control styles ----
        public const uint BS_PUSHBUTTON = 0x0000;
        public const uint BS_DEFPUSHBUTTON = 0x0001;
        public const uint BS_AUTOCHECKBOX = 0x0003;
        public const uint BS_GROUPBOX = 0x0007;
        public const uint BS_MULTILINE = 0x2000;

        public const uint ES_LEFT = 0x0000;
        public const uint ES_MULTILINE = 0x0004;
        public const uint ES_AUTOVSCROLL = 0x0040;
        public const uint ES_AUTOHSCROLL = 0x0080;
        public const uint ES_READONLY = 0x0800;

        public const uint SS_LEFT = 0x0000;
        public const uint SS_NOPREFIX = 0x0080;

        public const uint CBS_DROPDOWNLIST = 0x0003;
        public const uint CBS_AUTOHSCROLL = 0x0040;
        public const uint CBS_HASSTRINGS = 0x0200;

        public const uint LBS_NOTIFY = 0x0001;
        public const uint LBS_HASSTRINGS = 0x0040;
        public const uint LBS_NOINTEGRALHEIGHT = 0x0100;

        // ---- dialog control class atoms ----
        public const ushort ATOM_BUTTON = 0x0080;
        public const ushort ATOM_EDIT = 0x0081;
        public const ushort ATOM_STATIC = 0x0082;
        public const ushort ATOM_LISTBOX = 0x0083;
        public const ushort ATOM_COMBOBOX = 0x0085;

        // ---- MessageBox ----
        public const uint MB_OK = 0x0;
        public const uint MB_OKCANCEL = 0x1;
        public const uint MB_YESNO = 0x4;
        public const uint MB_ICONERROR = 0x10;
        public const uint MB_ICONQUESTION = 0x20;
        public const uint MB_ICONWARNING = 0x30;
        public const uint MB_ICONINFORMATION = 0x40;
        public const uint MB_DEFBUTTON2 = 0x100;
        public const uint MB_SETFOREGROUND = 0x10000;
        public const int IDOK = 1;
        public const int IDCANCEL = 2;
        public const int IDYES = 6;
        public const int IDNO = 7;

        public const int SW_SHOW = 5;
        public const int SW_SHOWNORMAL = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        public delegate IntPtr DlgProc(IntPtr hDlg, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CreateDialogIndirectParamW(IntPtr hInstance, byte[] lpTemplate,
            IntPtr hWndParent, DlgProc lpDialogFunc, IntPtr lParamInit);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr DialogBoxIndirectParamW(IntPtr hInstance, byte[] lpTemplate,
            IntPtr hWndParent, DlgProc lpDialogFunc, IntPtr lParamInit);

        [DllImport("user32.dll")]
        public static extern bool EndDialog(IntPtr hDlg, IntPtr nResult);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetDlgItemTextW(IntPtr hDlg, int id, string s);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern uint GetDlgItemTextW(IntPtr hDlg, int id, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageString(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessageSb(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendDlgItemMessageW(IntPtr hDlg, int id, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendDlgItemMessageW")]
        public static extern IntPtr SendDlgItemMessageString(IntPtr hDlg, int id, uint msg, IntPtr wParam, string lParam);

        [DllImport("user32.dll")]
        public static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool CheckDlgButton(IntPtr hDlg, int id, uint check);

        [DllImport("user32.dll")]
        public static extern uint IsDlgButtonChecked(IntPtr hDlg, int id);

        [DllImport("user32.dll")]
        public static extern bool EnableWindow(IntPtr hWnd, bool enable);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetFocus();

        public const int LBN_SETFOCUS = 4;
        public const int LBN_KILLFOCUS = 5;

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowTextW(IntPtr hWnd, string s);

        [DllImport("user32.dll")]
        public static extern bool IsDialogMessageW(IntPtr hDlg, ref MSG msg);

        [DllImport("user32.dll")]
        public static extern int GetMessageW(out MSG msg, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern bool PeekMessageW(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll")]
        public static extern UIntPtr SetTimer(IntPtr hWnd, UIntPtr id, uint elapseMs, IntPtr proc);

        [DllImport("user32.dll")]
        public static extern bool KillTimer(IntPtr hWnd, UIntPtr id);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll")]
        public static extern bool OpenClipboard(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool EmptyClipboard();

        [DllImport("user32.dll")]
        public static extern IntPtr SetClipboardData(uint format, IntPtr hMem);

        [DllImport("user32.dll")]
        public static extern bool CloseClipboard();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr iconName);

        [DllImport("user32.dll")]
        public static extern IntPtr GetActiveWindow();

        public const int GWLP_WNDPROC = -4;
        public const int GWL_STYLE = -16;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassNameW(IntPtr hWnd, StringBuilder name, int max);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr newLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        public static extern IntPtr CallWindowProc(IntPtr prevProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public const int VK_UP = 0x26;
        public const int VK_DOWN = 0x28;
        public const int VK_HOME = 0x24;
        public const int VK_END = 0x23;
        public const int VK_PRIOR = 0x21;
        public const int VK_NEXT = 0x22;

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("kernel32.dll")]
        public static extern IntPtr GetModuleHandleW(IntPtr name);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateMutexW(IntPtr attr, bool initialOwner, string name);

        [DllImport("kernel32.dll")]
        public static extern uint GetLastError();

        public const uint GMEM_MOVEABLE = 0x0002;
        public const uint CF_UNICODETEXT = 13;
        public const uint ERROR_ALREADY_EXISTS = 183;

        [DllImport("shcore.dll", EntryPoint = "SetProcessDpiAwareness")]
        private static extern int SetProcessDpiAwarenessRaw(int value);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        public static void EnableDpiAwareness()
        {
            try
            {
                // 2 == PROCESS_PER_MONITOR_DPI_AWARE
                if (SetProcessDpiAwarenessRaw(2) == 0) return;
            }
            catch
            {
            }
            try { SetProcessDPIAware(); } catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct INITCOMMONCONTROLSEX
        {
            public int dwSize;
            public int dwICC;
        }

        [DllImport("comctl32.dll")]
        public static extern bool InitCommonControlsEx(ref INITCOMMONCONTROLSEX icc);

        public static void InitCommonControls()
        {
            var icc = new INITCOMMONCONTROLSEX
            {
                dwSize = Marshal.SizeOf<INITCOMMONCONTROLSEX>(),
                dwICC = 0x00004000 | 0x00000001, // ICC_STANDARD_CLASSES | ICC_LISTVIEW_CLASSES
            };
            try { InitCommonControlsEx(ref icc); } catch { }
        }

        public static string GetDlgItemText(IntPtr hDlg, int id)
        {
            IntPtr h = GetDlgItem(hDlg, id);
            if (h == IntPtr.Zero) return string.Empty;
            int len = (int)SendMessageW(h, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero);
            var sb = new StringBuilder(len + 2);
            GetDlgItemTextW(hDlg, id, sb, sb.Capacity);
            return sb.ToString();
        }

        public static void SetClipboardText(IntPtr owner, string text)
        {
            if (!OpenClipboard(owner)) return;
            try
            {
                EmptyClipboard();
                var bytes = (text.Length + 1) * 2;
                IntPtr hMem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
                if (hMem == IntPtr.Zero) return;
                IntPtr p = GlobalLock(hMem);
                if (p == IntPtr.Zero) return;
                try
                {
                    unsafe
                    {
                        fixed (char* src = text)
                        {
                            Buffer.MemoryCopy(src, (void*)p, bytes, text.Length * 2);
                            ((char*)p)[text.Length] = '\0';
                        }
                    }
                }
                finally { GlobalUnlock(hMem); }
                SetClipboardData(CF_UNICODETEXT, hMem);
            }
            finally { CloseClipboard(); }
        }

        // Combo boxes carry the device id as item data. Storing an index into a
        // side table rather than a raw pointer keeps this safe under a moving GC.
        public static int ComboAdd(IntPtr hDlg, int id, string text, int data)
        {
            IntPtr idx = SendDlgItemMessageString(hDlg, id, CB_ADDSTRING, IntPtr.Zero, text);
            SendDlgItemMessageW(hDlg, id, CB_SETITEMDATA, idx, (IntPtr)data);
            return (int)idx;
        }

        public static int ComboInsert(IntPtr hDlg, int id, int at, string text, int data)
        {
            IntPtr idx = SendDlgItemMessageString(hDlg, id, CB_INSERTSTRING, (IntPtr)at, text);
            SendDlgItemMessageW(hDlg, id, CB_SETITEMDATA, idx, (IntPtr)data);
            return (int)idx;
        }

        public static int ComboGetSel(IntPtr hDlg, int id) =>
            (int)SendDlgItemMessageW(hDlg, id, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);

        public static void ComboSetSel(IntPtr hDlg, int id, int index) =>
            SendDlgItemMessageW(hDlg, id, CB_SETCURSEL, (IntPtr)index, IntPtr.Zero);

        public static int ComboGetData(IntPtr hDlg, int id)
        {
            int sel = ComboGetSel(hDlg, id);
            if (sel < 0) return -1;
            return (int)SendDlgItemMessageW(hDlg, id, CB_GETITEMDATA, (IntPtr)sel, IntPtr.Zero);
        }

        public static string ComboGetText(IntPtr hDlg, int id)
        {
            int sel = ComboGetSel(hDlg, id);
            if (sel < 0) return string.Empty;
            IntPtr h = GetDlgItem(hDlg, id);
            int len = (int)SendMessageW(h, CB_GETLBTEXTLEN, (IntPtr)sel, IntPtr.Zero);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 2);
            SendMessageSb(h, CB_GETLBTEXT, (IntPtr)sel, sb);
            return sb.ToString();
        }

        public static void ComboSelectByText(IntPtr hDlg, int id, string text)
        {
            int count = (int)SendDlgItemMessageW(hDlg, id, CB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);
            IntPtr h = GetDlgItem(hDlg, id);
            for (int i = 0; i < count; i++)
            {
                int len = (int)SendMessageW(h, CB_GETLBTEXTLEN, (IntPtr)i, IntPtr.Zero);
                var sb = new StringBuilder(len + 2);
                SendMessageSb(h, CB_GETLBTEXT, (IntPtr)i, sb);
                if (string.Equals(sb.ToString(), text, StringComparison.Ordinal))
                {
                    ComboSetSel(hDlg, id, i);
                    return;
                }
            }
            if (count > 0) ComboSetSel(hDlg, id, 0);
        }

        // Read-only blocks of text are presented as list boxes rather than
        // read-only edit controls. A screen reader announces a list and then
        // reads each line as its own item on arrow-down, which is how someone
        // actually wants to consume a status block; a read-only edit announces
        // itself as an editable text field, which is both wrong and annoying.
        public static void ListSetLines(IntPtr hDlg, int id, string text)
        {
            IntPtr h = GetDlgItem(hDlg, id);
            if (h == IntPtr.Zero) return;

            SendMessageW(h, LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero);
            if (string.IsNullOrEmpty(text)) return;

            int widest = 0;
            foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                // A blank entry would read as an unhelpful "blank" item.
                string line = raw.TrimEnd();
                if (line.Length == 0) continue;
                SendMessageString(h, (uint)LB_ADDSTRING, IntPtr.Zero, line);
                if (line.Length > widest) widest = line.Length;
            }

            // Roughly four dialog units per character; enough for the
            // horizontal scrollbar to expose long paths.
            SendMessageW(h, LB_SETHORIZONTALEXTENT, (IntPtr)(widest * 7), IntPtr.Zero);
        }

        public static int ListCount(IntPtr hDlg, int id) =>
            (int)SendDlgItemMessageW(hDlg, id, LB_GETCOUNT, IntPtr.Zero, IntPtr.Zero);

        public static string ListGetLine(IntPtr hDlg, int id, int index)
        {
            IntPtr h = GetDlgItem(hDlg, id);
            if (h == IntPtr.Zero) return string.Empty;
            int len = (int)SendMessageW(h, LB_GETTEXTLEN, (IntPtr)index, IntPtr.Zero);
            if (len <= 0) return string.Empty;
            var sb = new StringBuilder(len + 2);
            SendMessageSb(h, LB_GETTEXT, (IntPtr)index, sb);
            return sb.ToString();
        }

        public static string ListGetAll(IntPtr hDlg, int id)
        {
            int n = ListCount(hDlg, id);
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(ListGetLine(hDlg, id, i));
            }
            return sb.ToString();
        }

        public static bool IsChecked(IntPtr hDlg, int id) => IsDlgButtonChecked(hDlg, id) == BST_CHECKED;

        public static void SetChecked(IntPtr hDlg, int id, bool on) =>
            CheckDlgButton(hDlg, id, on ? (uint)BST_CHECKED : (uint)BST_UNCHECKED);
    }
}
