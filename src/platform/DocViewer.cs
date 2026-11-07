// Universe · platform · the document viewer: a real window for the program's own documents (About, Readme, How it works, the
// plain-words guide to the universe, the technical build guide, Credits), beside the game, never a pop-up. A contents list of
// every document and its headings on the left (click one to go there); the page on the right, formatted (headings, bold,
// lists, code, tables) with links that open in your browser; search across all the documents (Ctrl+F, Enter or F3 for the
// next match); text size (A- and A+, or Ctrl with the wheel); Back; copy (select, Ctrl+C). Windows' own rich text control
// does the drawing; the documents are written in a small part of Markdown and turned into its rich text here.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Platform
{
    internal static class DocViewer
    {
        public sealed class Doc { public string Title, Text; }

        // ── Windows ──
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibrary(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
        [DllImport("user32.dll", CharSet = CharSet.Ansi, EntryPoint = "SendMessageA")] private static extern IntPtr SendMessageAnsi(IntPtr h, uint msg, IntPtr w, byte[] l);
        [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")] private static extern short GetKeyState(int key);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ShellExecute(IntPtr h, string op, string file, string args, string dir, int show);

        private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_VSCROLL = 0x00200000, WS_BORDER = 0x00800000, WS_TABSTOP = 0x00010000;
        private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000, WS_EX_CLIENTEDGE = 0x200;
        private const uint ES_MULTILINE = 0x4, ES_AUTOVSCROLL = 0x40, ES_READONLY = 0x800, ES_AUTOHSCROLL = 0x80, ES_NOHIDESEL = 0x100;
        private const uint LBS_NOTIFY = 0x1, LBS_NOINTEGRALHEIGHT = 0x100;
        private const uint WM_DESTROY = 0x2, WM_SIZE = 0x5, WM_CLOSE = 0x10, WM_SETFONT = 0x30, WM_NOTIFY = 0x4E, WM_COMMAND = 0x111, WM_KEYDOWN = 0x100, WM_SETTEXT = 0xC, WM_LBUTTONUP = 0x202;
        private const uint LB_ADDSTRING = 0x180, LB_RESETCONTENT = 0x184, LB_SETCURSEL = 0x186, LB_GETCURSEL = 0x188, LBN_SELCHANGE = 1, BN_CLICKED = 0;
        private const uint EM_SETRECT = 0xB3, EM_SETSEL = 0xB1, EM_SCROLLCARET = 0xB7, EM_GETFIRSTVISIBLELINE = 0xCE, EM_LINESCROLL = 0xB6, EM_LINEFROMCHAR = 0xC9, EM_SETCUEBANNER = 0x1501;
        private const uint WM_USER = 0x400, EM_EXGETSEL = WM_USER + 52, EM_EXSETSEL = WM_USER + 55, EM_SETEVENTMASK = WM_USER + 69, EM_SETBKGNDCOLOR = WM_USER + 67;
        private const uint EM_GETTEXTRANGE = WM_USER + 75, EM_AUTOURLDETECT = WM_USER + 91, EM_FINDTEXTEXW = WM_USER + 124, EM_SETZOOM = WM_USER + 225;
        private const int ENM_LINK = 0x04000000, EN_LINK = 0x070B, FR_DOWN = 1;

        private static Win32.WndProc _proc; private static bool _registered;
        private static IntPtr _wnd, _list, _page, _search, _find, _smaller, _bigger, _back, _status, _font;
        private static List<Doc> _docs = new List<Doc>();
        private static readonly List<int[]> _items = new List<int[]>();               // list row -> { document, heading (-1 for the document itself) }
        private static readonly List<List<string>> _heads = new List<List<string>>();  // each document's headings
        private static int _doc = -1, _zoom = 100;
        private static readonly Stack<int[]> _history = new Stack<int[]>();           // { document, first visible line }

        private const int IdList = 10, IdFind = 11, IdSmaller = 12, IdBigger = 13, IdBack = 14;

        /// <summary>Opens the viewer (or brings it forward) at document <paramref name="open"/>.</summary>
        public static void Show(IntPtr owner, List<Doc> docs, int open)
        {
            _docs = docs;
            if (_wnd == IntPtr.Zero || !IsWindow(_wnd)) Create(owner);
            Fill();
            Open(open, -1, true);
            Win32.ShowWindow(_wnd, 5); SetForegroundWindow(_wnd); SetFocus(_page);
        }

        private static void Create(IntPtr owner)
        {
            LoadLibrary("Msftedit.dll");
            var inst = Win32.GetModuleHandle(null);
            if (!_registered)
            {
                _proc = Proc;
                var wc = new Win32.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX)), lpfnWndProc = _proc, hInstance = inst, hCursor = Win32.LoadCursor(IntPtr.Zero, (IntPtr)32512), hbrBackground = (IntPtr)16, lpszClassName = Universe.Shell.PreviewText.AppName.Replace("-", "") + "Docs" };
                Win32.RegisterClassEx(ref wc); _registered = true;
            }
            int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1), w = (int)(sw * 0.62), h = (int)(sh * 0.78);
            _wnd = Win32.CreateWindowEx(0, Universe.Shell.PreviewText.AppName.Replace("-", "") + "Docs", Universe.Shell.PreviewText.AppName + " · Documents", WS_OVERLAPPEDWINDOW, (sw - w) / 2, (sh - h) / 2, w, h, owner, IntPtr.Zero, inst, IntPtr.Zero);
            float s = Scale();
            _font = CreateFont(-(int)(15 * s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            _list = Child(WS_EX_CLIENTEDGE, "LISTBOX", "", WS_VSCROLL | LBS_NOTIFY | LBS_NOINTEGRALHEIGHT | WS_TABSTOP, IdList);
            _page = Child(WS_EX_CLIENTEDGE, "RICHEDIT50W", "", WS_VSCROLL | ES_MULTILINE | ES_AUTOVSCROLL | ES_READONLY | ES_NOHIDESEL | WS_TABSTOP, 0);
            _search = Child(WS_EX_CLIENTEDGE, "EDIT", "", ES_AUTOHSCROLL | WS_TABSTOP, 0);
            _find = Child(0, "BUTTON", "Find next", WS_TABSTOP, IdFind);
            _smaller = Child(0, "BUTTON", "A-", WS_TABSTOP, IdSmaller);
            _bigger = Child(0, "BUTTON", "A+", WS_TABSTOP, IdBigger);
            _back = Child(0, "BUTTON", "Back", WS_TABSTOP, IdBack);
            _status = Child(0, "STATIC", "", 0, 0);
            SendMessage(_search, EM_SETCUEBANNER, (IntPtr)1, "Search all documents (Ctrl+F)");
            SendMessage(_page, EM_SETEVENTMASK, IntPtr.Zero, (IntPtr)ENM_LINK);
            SendMessage(_page, EM_AUTOURLDETECT, (IntPtr)1, IntPtr.Zero);
            SendMessage(_page, EM_SETBKGNDCOLOR, IntPtr.Zero, (IntPtr)0xFBFAF8);
            Layout();
        }

        private static IntPtr Child(uint ex, string cls, string text, uint style, int id)
        {
            var h = Win32.CreateWindowEx(ex, cls, text, WS_CHILD | WS_VISIBLE | style, 0, 0, 10, 10, _wnd, (IntPtr)id, Win32.GetModuleHandle(null), IntPtr.Zero);
            SendMessage(h, WM_SETFONT, _font, (IntPtr)1);
            return h;
        }

        private static float Scale() { try { return Math.Max(1f, GetDpiForWindow(_wnd != IntPtr.Zero ? _wnd : IntPtr.Zero) / 96f); } catch { return 1f; } }

        private static void Layout()
        {
            Win32.RECT r; Win32.GetClientRect(_wnd, out r); float s = Scale();
            int W = r.Right, H = r.Bottom, pad = (int)(8 * s), bar = (int)(34 * s), left = (int)(270 * s), bh = bar - pad;
            int x = pad;
            MoveWindow(_search, x, pad, (int)(320 * s), bh, true); x += (int)(320 * s) + pad;
            MoveWindow(_find, x, pad, (int)(90 * s), bh, true); x += (int)(90 * s) + pad;
            MoveWindow(_smaller, x, pad, (int)(40 * s), bh, true); x += (int)(40 * s) + pad / 2;
            MoveWindow(_bigger, x, pad, (int)(40 * s), bh, true); x += (int)(40 * s) + pad;
            MoveWindow(_back, x, pad, (int)(64 * s), bh, true); x += (int)(64 * s) + pad;
            MoveWindow(_status, x, pad + (int)(5 * s), Math.Max(10, W - x - pad), bh, true);
            MoveWindow(_list, pad, bar + pad, left, H - bar - pad * 2, true);
            MoveWindow(_page, left + pad * 2, bar + pad, Math.Max(10, W - left - pad * 3), H - bar - pad * 2, true);
        }

        // the contents: every document, and under it its headings
        private static void Fill()
        {
            SendMessage(_list, LB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero); _items.Clear(); _heads.Clear();
            for (int d = 0; d < _docs.Count; d++)
            {
                var hs = new List<string>();
                foreach (var line in _docs[d].Text.Replace("\r", "").Split('\n'))
                    if (line.StartsWith("## ")) hs.Add(Plain(line.Substring(3).Trim()));
                _heads.Add(hs);
                SendMessage(_list, LB_ADDSTRING, IntPtr.Zero, _docs[d].Title); _items.Add(new[] { d, -1 });
                for (int k = 0; k < hs.Count; k++) { SendMessage(_list, LB_ADDSTRING, IntPtr.Zero, "      " + hs[k]); _items.Add(new[] { d, k }); }
            }
        }

        private static void Open(int d, int heading, bool remember)
        {
            if (d < 0 || d >= _docs.Count) return;
            if (remember && _doc >= 0) _history.Push(new[] { _doc, (int)SendMessage(_page, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero) });
            if (d != _doc)
            {
                _doc = d;
                // the page as formatted text, said outright (EM_SETTEXTEX: default flags, the ANSI code page); handed over as plain
                // window text the box can take the formatting itself for words and show it raw
                // the page's margins, and its tables fitted to the width between them
                Win32.RECT pr; Win32.GetClientRect(_page, out pr); int m = (int)(Math.Max(24, pr.Right * 0.05)), mt = (int)(18 * Scale());
                Win32.RECT fr = new Win32.RECT { Left = m, Top = mt, Right = Math.Max(m + 50, pr.Right - m), Bottom = Math.Max(mt + 50, pr.Bottom - mt) };
                IntPtr frp = Marshal.AllocHGlobal(16); Marshal.StructureToPtr(fr, frp, false); SendMessage(_page, EM_SETRECT, IntPtr.Zero, frp); Marshal.FreeHGlobal(frp);
                int twips = (int)((fr.Right - fr.Left) * 1440.0 / (96 * Scale()) * 100 / Math.Max(60, _zoom)) - 240;
                _links.Clear();
                byte[] rtf = Encoding.ASCII.GetBytes(ToRtf(_docs[d].Text, Math.Max(4000, twips)) + "\0");
                IntPtr ste = Marshal.AllocHGlobal(8), txt = Marshal.AllocHGlobal(rtf.Length);
                Marshal.WriteInt32(ste, 0, 0); Marshal.WriteInt32(ste, 4, 0); Marshal.Copy(rtf, 0, txt, rtf.Length);
                SendMessage(_page, WM_USER + 97, ste, txt);
                Marshal.FreeHGlobal(ste); Marshal.FreeHGlobal(txt);
                SendMessage(_page, EM_SETZOOM, (IntPtr)_zoom, (IntPtr)100);
                Win32.SetWindowText(_wnd, Universe.Shell.PreviewText.AppName + " · " + _docs[d].Title);
            }
            int pos = 0;
            if (heading >= 0 && heading < _heads[d].Count) { int found = Find(_heads[d][heading], 0, true); if (found >= 0) pos = found; }
            Top(pos);
            for (int i = 0; i < _items.Count; i++) if (_items[i][0] == d && _items[i][1] == heading) { SendMessage(_list, LB_SETCURSEL, (IntPtr)i, IntPtr.Zero); break; }
            SetStatus("");
        }

        // put the line holding character pos at the top of the page
        private static void Top(int pos)
        {
            SendMessage(_page, EM_SETSEL, (IntPtr)pos, (IntPtr)pos);
            int line = (int)SendMessage(_page, EM_LINEFROMCHAR, (IntPtr)pos, IntPtr.Zero), first = (int)SendMessage(_page, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
            SendMessage(_page, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(line - first));
        }

        // where text first appears at or after from (or -1); select it if asked
        private static int Find(string text, int from, bool select)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            IntPtr str = Marshal.StringToHGlobalUni(text), ft = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.WriteInt32(ft, 0, from); Marshal.WriteInt32(ft, 4, -1); Marshal.WriteIntPtr(ft, 8, str);
                int at = (int)SendMessage(_page, EM_FINDTEXTEXW, (IntPtr)FR_DOWN, ft);
                if (at >= 0 && select)
                {
                    int a = Marshal.ReadInt32(ft, 16), b = Marshal.ReadInt32(ft, 20);
                    IntPtr cr = Marshal.AllocHGlobal(8); Marshal.WriteInt32(cr, 0, a); Marshal.WriteInt32(cr, 4, b);
                    SendMessage(_page, EM_EXSETSEL, IntPtr.Zero, cr); Marshal.FreeHGlobal(cr);
                }
                return at;
            }
            finally { Marshal.FreeHGlobal(str); Marshal.FreeHGlobal(ft); }
        }

        // the next match of the search text: on in this document, then on through the others, round to the start
        private static void FindNext()
        {
            var sb = new StringBuilder(GetWindowTextLength(_search) + 2); GetWindowText(_search, sb, sb.Capacity); string q = sb.ToString().Trim();
            if (q.Length == 0) { SetFocus(_search); return; }
            IntPtr cr = Marshal.AllocHGlobal(8); SendMessage(_page, EM_EXGETSEL, IntPtr.Zero, cr); int selEnd = Marshal.ReadInt32(cr, 4); Marshal.FreeHGlobal(cr);
            if (Find(q, selEnd, true) >= 0) { SendMessage(_page, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero); SetStatus(""); return; }
            for (int step = 1; step <= _docs.Count; step++)
            {
                int d = (_doc + step) % _docs.Count;
                if (_docs[d].Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 && d != _doc) continue;
                Open(d, -1, true);
                if (Find(q, 0, true) >= 0) { SendMessage(_page, EM_SCROLLCARET, IntPtr.Zero, IntPtr.Zero); SetStatus(step == _docs.Count ? "back to the first match" : "found in " + _docs[d].Title); return; }
            }
            SetStatus("\"" + q + "\" is not in any document");
        }

        private static void SetStatus(string s) { Win32.SetWindowText(_status, s); }

        private static void Zoom(int by) { _zoom = Math.Max(60, Math.Min(260, _zoom + by)); SendMessage(_page, EM_SETZOOM, (IntPtr)_zoom, (IntPtr)100); SetStatus("text size " + _zoom + "%"); }

        /// <summary>Keys for the viewer, before Windows hands them on: Enter or F3 finds the next match, Ctrl+F goes to the search
        /// box, Ctrl and + or - change the text size, Esc closes. True when the key was the viewer's.</summary>
        public static bool PreTranslate(ref Win32.MSG msg)
        {
            if (_wnd == IntPtr.Zero || msg.message != WM_KEYDOWN || !(msg.hwnd == _wnd || IsChild(_wnd, msg.hwnd))) return false;
            int key = (int)msg.wParam; bool ctrl = GetKeyState(0x11) < 0;
            if ((key == 0x0D && msg.hwnd == _search) || key == 0x72) { FindNext(); return true; }
            if (ctrl && key == 0x46) { SetFocus(_search); SendMessage(_search, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1)); return true; }
            if (ctrl && (key == 0xBB || key == 0x6B)) { Zoom(10); return true; }
            if (ctrl && (key == 0xBD || key == 0x6D)) { Zoom(-10); return true; }
            if (key == 0x1B) { Win32.DestroyWindow(_wnd); return true; }
            return false;
        }

        private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            switch (msg)
            {
                case WM_SIZE:
                    if (_page != IntPtr.Zero)
                    {
                        Layout();
                        // laid out again to the new width (its tables and margins), at the same place
                        if (_doc >= 0) { int d = _doc, first = (int)SendMessage(_page, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero); _doc = -1; Open(d, -1, false); int now = (int)SendMessage(_page, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero); SendMessage(_page, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(first - now)); }
                    }
                    return IntPtr.Zero;
                case WM_COMMAND:
                    {
                        int id = (int)((long)w & 0xFFFF), code = (int)(((long)w >> 16) & 0xFFFF);
                        if (id == IdList && code == LBN_SELCHANGE)
                        {
                            int row = (int)SendMessage(_list, LB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                            if (row >= 0 && row < _items.Count) Open(_items[row][0], _items[row][1], true);
                        }
                        else if (code == BN_CLICKED && id == IdFind) FindNext();
                        else if (code == BN_CLICKED && id == IdSmaller) Zoom(-10);
                        else if (code == BN_CLICKED && id == IdBigger) Zoom(10);
                        else if (code == BN_CLICKED && id == IdBack && _history.Count > 0)
                        {
                            var b = _history.Pop(); Open(b[0], -1, false);
                            int first = (int)SendMessage(_page, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
                            SendMessage(_page, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(b[1] - first));
                        }
                        return IntPtr.Zero;
                    }
                case WM_NOTIFY:
                    {
                        // a link clicked: open it in the browser
                        if (Marshal.ReadInt32(l, 16) == EN_LINK && Marshal.ReadInt32(l, 24) == WM_LBUTTONUP)
                        {
                            int a = Marshal.ReadInt32(l, 48), b = Marshal.ReadInt32(l, 52);
                            if (b > a && b - a < 2000)
                            {
                                IntPtr buf = Marshal.AllocHGlobal((b - a + 1) * 2), tr = Marshal.AllocHGlobal(16);
                                Marshal.WriteInt32(tr, 0, a); Marshal.WriteInt32(tr, 4, b); Marshal.WriteIntPtr(tr, 8, buf);
                                SendMessage(_page, EM_GETTEXTRANGE, IntPtr.Zero, tr);
                                string url = Marshal.PtrToStringUni(buf); Marshal.FreeHGlobal(buf); Marshal.FreeHGlobal(tr);
                                // a bare address, a link's hidden address (HYPERLINK "address"), or a link's words
                                string go = null; int q = url.IndexOf("\"http");
                                if (url.StartsWith("http://") || url.StartsWith("https://")) go = url;
                                else if (q >= 0) { int q2 = url.IndexOf('"', q + 1); go = q2 > q ? url.Substring(q + 1, q2 - q - 1) : null; }
                                else _links.TryGetValue(url.Trim(), out go);
                                if (go != null) ShellExecute(IntPtr.Zero, "open", go, null, null, 1);
                            }
                            return (IntPtr)1;
                        }
                        break;
                    }
                case WM_CLOSE: Win32.DestroyWindow(h); return IntPtr.Zero;
                case WM_DESTROY: _wnd = IntPtr.Zero; _page = IntPtr.Zero; _doc = -1; _history.Clear(); return IntPtr.Zero;
            }
            return Win32.DefWindowProc(h, msg, w, l);
        }

        // ── Markdown, into rich text: headings with a rule under them, bold, italic, code, links that show their words and open
        // in the browser, bullet and numbered lists, quotes in a shaded box, dividers, and real tables with borders and a
        // shaded header row, laid out to the page's width ──

        private static string Plain(string s) { return s.Replace("**", "").Replace("`", ""); }

        // the links on the page: the words shown, and where they go (a click finds the address from the words)
        private static readonly Dictionary<string, string> _links = new Dictionary<string, string>();

        private static string Esc(string s)
        {
            var b = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                if (c == '\\' || c == '{' || c == '}') { b.Append('\\').Append(c); }
                else if (c > 127) b.Append("\\u").Append((int)(short)c).Append('?');
                else b.Append(c);
            }
            return b.ToString();
        }

        // **bold**, *italic*, `code`, and [words](address) as a link showing its words
        private static string Inline(string s)
        {
            var b = new StringBuilder(); bool bold = false, ital = false, code = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!code && i + 1 < s.Length && s[i] == '*' && s[i + 1] == '*') { b.Append(bold ? "\\b0 " : "\\b "); bold = !bold; i++; continue; }
                if (!code && s[i] == '*' && (ital || (i + 1 < s.Length && s[i + 1] != ' '))) { b.Append(ital ? "\\i0 " : "\\i "); ital = !ital; continue; }
                if (s[i] == '`') { b.Append(code ? "\\f0\\highlight0\\cf1 " : "\\f1\\highlight4\\cf3 "); code = !code; continue; }
                if (!code && s[i] == '!' && i + 1 < s.Length && s[i + 1] == '[') continue;   // (a picture's words shown as a link)
                if (!code && s[i] == '[')
                {
                    int close = s.IndexOf("](", i), end = close > 0 ? s.IndexOf(')', close) : -1;
                    if (close > i && end > close)
                    {
                        string words = s.Substring(i + 1, close - i - 1), url = s.Substring(close + 2, end - close - 2).Trim();
                        if (url.StartsWith("http://") || url.StartsWith("https://"))
                        {
                            _links[Plain(words)] = url;
                            b.Append("{\\field{\\*\\fldinst{HYPERLINK \"").Append(Esc(url)).Append("\"}}{\\fldrslt{\\ul\\cf2 ").Append(Inline(words)).Append("}}}");
                        }
                        else b.Append(Inline(words));   // (a page of the project, not on the web: its words alone)
                        i = end; continue;
                    }
                }
                b.Append(Esc(s[i].ToString()));
            }
            if (bold) b.Append("\\b0 "); if (ital) b.Append("\\i0 "); if (code) b.Append("\\f0\\highlight0\\cf1 ");
            return b.ToString();
        }

        /// <summary>The page in rich text, its tables fitted to <paramref name="widthTwips"/>.</summary>
        public static string ToRtf(string md, int widthTwips = 11000)
        {
            const string Body = "\\pard\\sl300\\slmult1\\sa140\\f0\\fs22\\cf1 ";
            var o = new StringBuilder();
            o.Append("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fswiss Segoe UI;}{\\f1\\fmodern Consolas;}{\\f2\\fswiss Segoe UI Semibold;}}");
            // 1 text, 2 accent (headings, links), 3 code, 4 code ground, 5 rules, 6 quote ground, 7 table header ground, 8 quiet text
            o.Append("{\\colortbl;\\red32\\green33\\blue40;\\red52\\green84\\blue178;\\red150\\green62\\blue40;\\red240\\green238\\blue234;\\red206\\green208\\blue216;\\red236\\green241\\blue252;\\red226\\green231\\blue243;\\red104\\green108\\blue120;}");
            o.Append("\\viewkind4\\uc1\\f0\\fs22\\cf1 ");
            var lines = md.Replace("\r", "").Split('\n');
            bool fence = false; var table = new List<string[]>();
            Action flushTable = () =>
            {
                if (table.Count == 0) return;
                int cols = 0; foreach (var r in table) cols = Math.Max(cols, r.Length);
                // each column as wide as its longest words want (within reason), the whole table the page's width
                var want = new double[cols]; double sum = 0;
                for (int c = 0; c < cols; c++) { int m = 4; foreach (var r in table) if (c < r.Length) m = Math.Max(m, Plain(r[c]).Length); want[c] = Math.Min(60, m) + 6; sum += want[c]; }
                for (int ri = 0; ri < table.Count; ri++)
                {
                    var r = table[ri]; bool head = ri == 0;
                    o.Append("\\trowd\\trgaph120\\trleft0\\trpaddt60\\trpaddb60\\trpaddft3\\trpaddfb3 ");
                    double x = 0;
                    for (int c = 0; c < cols; c++)
                    {
                        x += want[c] / sum * widthTwips;
                        o.Append("\\clbrdrt\\brdrs\\brdrw10\\brdrcf5\\clbrdrl\\brdrs\\brdrw10\\brdrcf5\\clbrdrb\\brdrs\\brdrw10\\brdrcf5\\clbrdrr\\brdrs\\brdrw10\\brdrcf5");
                        if (head) o.Append("\\clcbpat7");
                        o.Append("\\cellx").Append((int)x).Append(' ');
                    }
                    for (int c = 0; c < cols; c++)
                    {
                        string cell = c < r.Length ? r[c] : "";
                        o.Append("\\pard\\intbl\\sl260\\slmult1\\sa0\\fs20 ").Append(head ? "\\f2\\cf2 " : "").Append(Inline(cell)).Append(head ? "\\f0\\cf1 " : "").Append("\\cell ");
                    }
                    o.Append("\\row ");
                }
                o.Append("\\pard\\sa200\\fs8\\par\\fs22 "); table.Clear();
            };
            foreach (var raw in lines)
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("```")) { flushTable(); fence = !fence; if (!fence) o.Append("\\pard\\sa100\\fs8\\par\\fs22 "); continue; }
                if (fence) { o.Append("\\pard\\li240\\ri240\\sa0\\sl240\\slmult1\\cbpat4\\f1\\fs19\\cf1 ").Append(Esc(line.Length == 0 ? " " : line)).Append("\\f0\\fs22\\par "); continue; }
                if (line.StartsWith("|"))
                {
                    var cells = line.Trim('|').Split('|'); for (int c = 0; c < cells.Length; c++) cells[c] = cells[c].Trim();
                    bool rule = true; foreach (var c in cells) if (c.Trim('-', ':', ' ').Length > 0) rule = false;
                    if (!rule) table.Add(cells);
                    continue;
                }
                flushTable();
                if (line.Length == 0) continue;
                string t = line.TrimStart(); int indent = line.Length - t.Length;
                if (t == "---" || t == "***" || t == "___") { o.Append("\\pard\\sb60\\sa260\\brdrb\\brdrs\\brdrw10\\brdrcf5\\fs8\\par\\fs22 "); continue; }
                if (t.StartsWith("# ")) { o.Append("\\pard\\sb80\\sa240\\brdrb\\brdrs\\brdrw30\\brsp100\\brdrcf2\\f2\\fs48\\cf2 ").Append(Inline(t.Substring(2))).Append("\\f0\\fs22\\cf1\\par "); continue; }
                if (t.StartsWith("## ")) { o.Append("\\pard\\sb360\\sa160\\brdrb\\brdrs\\brdrw10\\brsp60\\brdrcf5\\f2\\fs32\\cf2 ").Append(Inline(t.Substring(3))).Append("\\f0\\fs22\\cf1\\par "); continue; }
                if (t.StartsWith("### ")) { o.Append("\\pard\\sb240\\sa100\\f2\\fs26\\cf1 ").Append(Inline(t.Substring(4))).Append("\\f0\\fs22\\par "); continue; }
                if (t.StartsWith("#### ")) { o.Append("\\pard\\sb160\\sa80\\f2\\fs23\\cf8 ").Append(Inline(t.Substring(5))).Append("\\f0\\fs22\\cf1\\par "); continue; }
                if (t.StartsWith(">")) { o.Append("\\pard\\li300\\ri300\\sb60\\sa180\\sl300\\slmult1\\brdrl\\brdrs\\brdrw40\\brsp160\\brdrcf2\\cbpat6\\cf1 ").Append(Inline(t.TrimStart('>').Trim())).Append("\\par "); continue; }
                if (t.StartsWith("- ") || t.StartsWith("* ")) { o.Append("\\pard\\fi-280\\li").Append(480 + indent * 140).Append("\\sl280\\slmult1\\sa80\\cf2\\u8226?\\cf1\\tab ").Append(Inline(t.Substring(2))).Append("\\par "); continue; }
                int dot = t.IndexOf(". "); int num;
                if (dot > 0 && dot < 4 && int.TryParse(t.Substring(0, dot), out num)) { o.Append("\\pard\\fi-340\\li").Append(520 + indent * 140).Append("\\sl280\\slmult1\\sa80\\f2\\cf2 ").Append(num).Append(".\\f0\\cf1\\tab ").Append(Inline(t.Substring(dot + 2))).Append("\\par "); continue; }
                o.Append(Body).Append(Inline(line)).Append("\\par ");
            }
            flushTable();
            o.Append("}");
            return o.ToString();
        }
    }
}
