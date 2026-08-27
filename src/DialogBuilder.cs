using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Arp
{
    // Builds a classic DLGTEMPLATE in memory so dialogs can be defined in C#
    // without an .rc file or a UI framework.
    //
    // Going through a real dialog template (rather than raw CreateWindowEx) is
    // what buys correct keyboard behaviour for free: tab order, arrow-key group
    // navigation, Alt-mnemonics, default-button Enter, Escape to cancel, and the
    // MSAA dialog semantics NVDA reads best.
    internal sealed class DialogBuilder
    {
        private readonly List<Action<BinaryWriter>> _items = new();
        private readonly string _title;
        private readonly uint _style;
        private readonly uint _exStyle;
        private readonly short _x, _y, _cx, _cy;
        private readonly ushort _fontSize;
        private readonly string _fontFace;

        public DialogBuilder(string title, int cx, int cy,
            uint style = Win32.WS_POPUP | Win32.WS_CAPTION | Win32.WS_SYSMENU | Win32.DS_SETFONT | Win32.DS_MODALFRAME | Win32.DS_CENTER,
            uint exStyle = Win32.WS_EX_CONTROLPARENT,
            // MS Shell Dlg at 8pt keeps dialog units at the classic 6x13 base,
            // which is what the layouts below are measured in and what keeps the
            // settings window inside a 768px-tall screen.
            ushort fontSize = 8, string fontFace = "MS Shell Dlg")
        {
            _title = title ?? string.Empty;
            _cx = (short)cx;
            _cy = (short)cy;
            _x = 0;
            _y = 0;
            _style = style;
            _exStyle = exStyle;
            _fontSize = fontSize;
            _fontFace = fontFace;
        }

        private void AddItem(uint style, uint exStyle, int x, int y, int cx, int cy, int id, ushort atom, string text)
        {
            _items.Add(w =>
            {
                Align(w);
                w.Write(style | Win32.WS_CHILD | Win32.WS_VISIBLE);
                w.Write(exStyle);
                w.Write((short)x);
                w.Write((short)y);
                w.Write((short)cx);
                w.Write((short)cy);
                w.Write((ushort)id);
                w.Write((ushort)0xFFFF);
                w.Write(atom);
                WriteSz(w, text ?? string.Empty);
                w.Write((ushort)0); // no creation data
            });
        }

        public DialogBuilder Button(int id, string text, int x, int y, int cx, int cy, uint extra = 0)
        {
            AddItem(Win32.BS_PUSHBUTTON | Win32.WS_TABSTOP | extra, 0, x, y, cx, cy, id, Win32.ATOM_BUTTON, text);
            return this;
        }

        public DialogBuilder DefButton(int id, string text, int x, int y, int cx, int cy)
        {
            AddItem(Win32.BS_DEFPUSHBUTTON | Win32.WS_TABSTOP, 0, x, y, cx, cy, id, Win32.ATOM_BUTTON, text);
            return this;
        }

        public DialogBuilder CheckBox(int id, string text, int x, int y, int cx, int cy)
        {
            AddItem(Win32.BS_AUTOCHECKBOX | Win32.BS_MULTILINE | Win32.WS_TABSTOP, 0, x, y, cx, cy, id, Win32.ATOM_BUTTON, text);
            return this;
        }

        public DialogBuilder GroupBox(string text, int x, int y, int cx, int cy)
        {
            AddItem(Win32.BS_GROUPBOX, 0, x, y, cx, cy, -1, Win32.ATOM_BUTTON, text);
            return this;
        }

        // A label whose text contains "&x" gives the following control its
        // Alt-shortcut, exactly like the Qt buddy labels in the Python build.
        public DialogBuilder Label(string text, int x, int y, int cx, int cy, int id = -1)
        {
            AddItem(Win32.SS_LEFT, 0, x, y, cx, cy, id, Win32.ATOM_STATIC, text);
            return this;
        }

        /// <summary>
        /// The body of a prompt: wrapping static text that is not focusable.
        ///
        /// This is what makes a prompt behave like a message box. A screen
        /// reader announces the caption and the whole of this text as the
        /// dialog opens, and Tab then moves only between the buttons. Putting
        /// the text in a focusable control instead forces the reader to be
        /// tabbed into and arrowed through before the question can be heard.
        ///
        /// SS_NOPREFIX keeps ampersands in file paths from being eaten as
        /// Alt-shortcuts.
        /// </summary>
        public DialogBuilder MessageText(int id, string text, int x, int y, int cx, int cy)
        {
            AddItem(Win32.SS_LEFT | Win32.SS_NOPREFIX, 0, x, y, cx, cy, id, Win32.ATOM_STATIC, text);
            return this;
        }

        public DialogBuilder Edit(int id, int x, int y, int cx, int cy, uint extra = 0)
        {
            AddItem(Win32.ES_LEFT | Win32.ES_AUTOHSCROLL | Win32.WS_BORDER | Win32.WS_TABSTOP | extra,
                Win32.WS_EX_CLIENTEDGE, x, y, cx, cy, id, Win32.ATOM_EDIT, string.Empty);
            return this;
        }

        /// <summary>
        /// A focusable block of read-only text, presented as a list of lines.
        ///
        /// A Win32 STATIC cannot take focus, and a read-only EDIT announces
        /// itself to a screen reader as an editable text field, which is both
        /// misleading and irritating. A list box reads as a list, gives each
        /// line its own item on arrow-down, and is what these status and
        /// message blocks actually are.
        /// </summary>
        public DialogBuilder TextList(int id, int x, int y, int cx, int cy)
        {
            AddItem(Win32.LBS_NOTIFY | Win32.LBS_HASSTRINGS | Win32.LBS_NOINTEGRALHEIGHT |
                    Win32.WS_BORDER | Win32.WS_VSCROLL | Win32.WS_HSCROLL | Win32.WS_TABSTOP,
                Win32.WS_EX_CLIENTEDGE, x, y, cx, cy, id, Win32.ATOM_LISTBOX, string.Empty);
            return this;
        }

        public DialogBuilder Combo(int id, int x, int y, int cx, int cyDropped)
        {
            AddItem(Win32.CBS_DROPDOWNLIST | Win32.CBS_HASSTRINGS | Win32.CBS_AUTOHSCROLL |
                    Win32.WS_TABSTOP | Win32.WS_VSCROLL,
                0, x, y, cx, cyDropped, id, Win32.ATOM_COMBOBOX, string.Empty);
            return this;
        }

        public DialogBuilder ListBox(int id, int x, int y, int cx, int cy)
        {
            AddItem(Win32.LBS_NOTIFY | Win32.LBS_HASSTRINGS | Win32.LBS_NOINTEGRALHEIGHT |
                    Win32.WS_BORDER | Win32.WS_VSCROLL | Win32.WS_TABSTOP,
                Win32.WS_EX_CLIENTEDGE, x, y, cx, cy, id, Win32.ATOM_LISTBOX, string.Empty);
            return this;
        }

        public byte[] Build()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Encoding.Unicode, true);

            w.Write(_style);
            w.Write(_exStyle);
            w.Write((ushort)_items.Count);
            w.Write(_x);
            w.Write(_y);
            w.Write(_cx);
            w.Write(_cy);

            w.Write((ushort)0); // no menu
            w.Write((ushort)0); // predefined dialog class
            WriteSz(w, _title);

            if ((_style & Win32.DS_SETFONT) != 0)
            {
                w.Write(_fontSize);
                WriteSz(w, _fontFace);
            }

            foreach (var item in _items) item(w);

            w.Flush();
            return ms.ToArray();
        }

        private static void WriteSz(BinaryWriter w, string s)
        {
            foreach (char c in s) w.Write((ushort)c);
            w.Write((ushort)0);
        }

        private static void Align(BinaryWriter w)
        {
            long pad = w.BaseStream.Position % 4;
            if (pad != 0)
                for (long i = pad; i < 4; i++) w.Write((byte)0);
        }
    }
}
