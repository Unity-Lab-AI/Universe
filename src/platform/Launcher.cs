// Universe · platform · the landing page: a real window before the universe is made. Its name, one line, the seed (typed or
// chosen at random), Begin or the tour (filmed if you like), the clips filmed so far to play or find, a few settings, the guide and wiki. Windows' own controls, in Segoe UI, dark.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Platform
{
    internal static class Launcher
    {
        public enum Choice { Quit, Begin, Tour }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, string l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
        [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr h, bool enable);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool IsDialogMessage(IntPtr h, ref Win32.MSG msg);
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int max);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr dc, uint color);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ShellExecute(IntPtr h, string op, string file, string args, string dir, int show);

        private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_VSCROLL = 0x00200000;
        private const uint BS_DEFPUSHBUTTON = 0x1, BS_AUTOCHECKBOX = 0x3, ES_NUMBER = 0x2000, ES_AUTOHSCROLL = 0x80, CBS_DROPDOWNLIST = 0x3, WS_EX_CLIENTEDGE = 0x200;
        private const uint WM_CLOSE = 0x10, WM_CHAR = 0x102, WM_SETFONT = 0x30, WM_COMMAND = 0x111, EM_SETSEL = 0xB1, EN_MAXTEXT = 0x0501, WM_CTLCOLORSTATIC = 0x138, WM_CTLCOLOREDIT = 0x133, BM_GETCHECK = 0xF0, BM_SETCHECK = 0xF1;
        private const uint CB_SETDROPPEDWIDTH = 0x160, CB_RESETCONTENT = 0x14B, CB_ADDSTRING = 0x143, CB_SETCURSEL = 0x14E, CB_GETCURSEL = 0x147, EM_SETCUEBANNER = 0x1501, EM_LIMITTEXT = 0xC5;
        private const uint Back = 0x140C0A, Ink = 0xF2EEEA, Dim = 0xB4AAA0;                 // (colours as 0x00BBGGRR)
        private const int IdBegin = 1, IdTour = 2, IdGuide = 3, IdQuit = 4, IdRandom = 5, IdFps = 6, IdInvert = 7, IdDrag = 8, IdRecord = 9, IdClips = 10, IdPlay = 11, IdFolder = 12, IdStats = 13, IdClipSize = 14, IdSeed = 15, IdClipFormat = 16;

        private static Win32.WndProc _proc; private static bool _registered;
        private static IntPtr _wnd, _seed, _fps, _stats, _invert, _drag, _record, _clips, _clipSize, _brush, _font, _big, _small; private static string[] _clipFiles = new string[0];
        private static Choice? _done; private static Action _guide, _alive;
        /// <summary>The tour is to be filmed (a .cdf97w into the captures folder): ticked on the page.</summary>
        public static bool RecordTour;
        /// <summary>A universe clip (.cdf97u) chosen to play: its tour, flown again through the universe made from its seed (null: none).</summary>
        public static string ReplayTour; private static ulong? _replaySeed;
        /// <summary>A clip of the universe itself chosen to play (its path; null: none).</summary>
        public static string ReplayClip;
        /// <summary>Where screenshots and clips go: the captures folder beside the program.</summary>
        public static string Captures { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "captures"); } }

        /// <summary>Shows the landing page over <paramref name="owner"/> until Begin, the tour or Quit; the seed typed (null: chosen at random).</summary>
        public static Choice Run(IntPtr owner, Action openGuide, Action alive, out ulong? seed)
        {
            _guide = openGuide; _alive = alive; _done = null; seed = null; ReplayTour = null; _replaySeed = null; ReplayClip = null;
            var inst = Win32.GetModuleHandle(null);
            _brush = CreateSolidBrush(Back);
            if (!_registered)
            {
                _proc = Proc;
                var wc = new Win32.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX)), lpfnWndProc = _proc, hInstance = inst, hCursor = Win32.LoadCursor(IntPtr.Zero, (IntPtr)32512), hbrBackground = _brush, lpszClassName = Universe.Shell.PreviewText.AppName.Replace("-", "") + "Landing" };
                Win32.OwnIcons(out wc.hIcon, out wc.hIconSm);
                Win32.RegisterClassEx(ref wc); _registered = true;
            }
            float s = Math.Max(1f, SafeDpi(owner) / 96f);
            int W = (int)(560 * s), H = (int)(744 * s), sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
            _wnd = Win32.CreateWindowEx(0, Universe.Shell.PreviewText.AppName.Replace("-", "") + "Landing", Universe.Shell.PreviewText.AppName, WS_CAPTION | WS_SYSMENU | WS_VISIBLE, (sw - W) / 2, (sh - H) / 2, W, H, owner, IntPtr.Zero, inst, IntPtr.Zero);
            _big = CreateFont(-(int)(46 * s), 0, 0, 0, 300, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI Light");
            _font = CreateFont(-(int)(16 * s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            _small = CreateFont(-(int)(14 * s), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            int pad = (int)(32 * s), x = pad, y = (int)(24 * s), cw = W - pad * 2 - (int)(16 * s), row = (int)(34 * s);
            Child("STATIC", Universe.Shell.PreviewText.AppName, 0, 0, x, y, cw, (int)(60 * s), _big); y += (int)(64 * s);
            Child("STATIC", "From the cosmic web down to the quarks.", 0, 0, x, y, cw, (int)(24 * s), _font); y += (int)(48 * s);
            Child("STATIC", "Seed", 0, 0, x, y + (int)(6 * s), (int)(60 * s), row, _font);
            _seed = Child("EDIT", "", ES_NUMBER | ES_AUTOHSCROLL | WS_TABSTOP, IdSeed, x + (int)(64 * s), y, cw - (int)(64 * s) - (int)(110 * s), row, _font, WS_EX_CLIENTEDGE);
            SendMessage(_seed, EM_SETCUEBANNER, (IntPtr)1, "blank for a new one"); SendMessage(_seed, EM_LIMITTEXT, (IntPtr)19, IntPtr.Zero);
            Child("BUTTON", "Random", WS_TABSTOP, IdRandom, x + cw - (int)(100 * s), y, (int)(100 * s), row, _font); y += row + (int)(22 * s);
            Child("BUTTON", "Begin", BS_DEFPUSHBUTTON | WS_TABSTOP, IdBegin, x, y, (cw - (int)(12 * s)) / 2, (int)(48 * s), _font);
            Child("BUTTON", "Take the tour", WS_TABSTOP, IdTour, x + (cw + (int)(12 * s)) / 2, y, (cw - (int)(12 * s)) / 2, (int)(48 * s), _font); y += (int)(48 * s) + (int)(8 * s);
            _record = Child("BUTTON", "Film the tour (a clip)", BS_AUTOCHECKBOX | WS_TABSTOP, IdRecord, x + (cw + (int)(12 * s)) / 2, y, (cw - (int)(12 * s)) / 2, row, _small);
            // the clip's size against the screen (the same choice as View > Clip size in flight): full (4K on a 4K screen), half or a quarter
            Child("STATIC", "Clip size", 0, 0, x, y + (int)(6 * s), (int)(70 * s), row, _small);
            _clipSize = Child("COMBOBOX", "", CBS_DROPDOWNLIST | WS_TABSTOP | WS_VSCROLL, IdClipSize, x + (int)(74 * s), y, (cw - (int)(12 * s)) / 2 - (int)(74 * s), (int)(300 * s), _small);
            foreach (var nm in new[] { "Full: the screen's size (4K on a 4K screen)", "Half: half the width and height (about a quarter of the pixels)", "Quarter: a quarter each way (about a sixteenth)" }) SendMessage(_clipSize, CB_ADDSTRING, IntPtr.Zero, nm);
            { int cd = Universe.Render.ClipWriters.Down; SendMessage(_clipSize, CB_SETCURSEL, (IntPtr)(cd == 2 ? 1 : cd == 4 ? 2 : 0), IntPtr.Zero); }
            y += row + (int)(10 * s);
            // the clips filmed so far, newest first: play one, or open their folder
            Child("STATIC", "Clips", 0, 0, x, y + (int)(6 * s), (int)(60 * s), row, _small);
            _clips = Child("COMBOBOX", "", CBS_DROPDOWNLIST | WS_TABSTOP | WS_VSCROLL, IdClips, x + (int)(64 * s), y, cw - (int)(64 * s), (int)(300 * s), _small);   // the whole width, so a clip's name fits
            SendMessage(_clips, CB_SETDROPPEDWIDTH, (IntPtr)(cw - (int)(64 * s)), IntPtr.Zero); y += row + (int)(6 * s);
            Child("BUTTON", "Play", WS_TABSTOP, IdPlay, x + (int)(64 * s), y, (int)(100 * s), row, _small);
            Child("BUTTON", "Open folder", WS_TABSTOP, IdFolder, x + (int)(172 * s), y, (int)(120 * s), row, _small); y += row + (int)(24 * s);
            ListClips();
            _fps = Child("BUTTON", "Show frames a second", BS_AUTOCHECKBOX | WS_TABSTOP, IdFps, x, y, cw / 2, row, _small);
            _invert = Child("BUTTON", "Invert up and down", BS_AUTOCHECKBOX | WS_TABSTOP, IdInvert, x + cw / 2, y, cw / 2, row, _small); y += row + (int)(6 * s);
            _stats = Child("BUTTON", "Show GPU and CPU stats (time a frame, cores, memory)", BS_AUTOCHECKBOX | WS_TABSTOP, IdStats, x, y, cw, row, _small); y += row + (int)(6 * s);
            Child("STATIC", "Drag speed", 0, 0, x, y + (int)(6 * s), (int)(100 * s), row, _small);
            _drag = Child("COMBOBOX", "", CBS_DROPDOWNLIST | WS_TABSTOP | WS_VSCROLL, IdDrag, x + (int)(104 * s), y, (int)(120 * s), (int)(300 * s), _small);
            for (int i = 1; i <= 10; i++) SendMessage(_drag, CB_ADDSTRING, IntPtr.Zero, i + "x");
            SendMessage(_drag, CB_SETCURSEL, (IntPtr)(Window.DragSpeed - 1), IntPtr.Zero); y += row + (int)(26 * s);
            Child("BUTTON", "Guide and wiki", WS_TABSTOP, IdGuide, x, y, (cw - (int)(12 * s)) / 2, (int)(40 * s), _font);
            Child("BUTTON", "Quit", WS_TABSTOP, IdQuit, x + (cw + (int)(12 * s)) / 2, y, (cw - (int)(12 * s)) / 2, (int)(40 * s), _font);
            SendMessage(_fps, BM_SETCHECK, (IntPtr)(Settings.Get("show-fps", true) ? 1 : 0), IntPtr.Zero);
            SendMessage(_stats, BM_SETCHECK, (IntPtr)(Settings.Get("show-stats", true) ? 1 : 0), IntPtr.Zero);
            SendMessage(_invert, BM_SETCHECK, (IntPtr)(Settings.Get("invert-up-down", false) ? 1 : 0), IntPtr.Zero);
            SendMessage(_record, BM_SETCHECK, (IntPtr)(Settings.Get("film-tour", false) ? 1 : 0), IntPtr.Zero);
            Window.LandingOpen = true; Window.CloseAsked = false; SetForegroundWindow(_wnd); SetFocus(_seed);   // (the game's window stays enabled: its own close button quits too, never a refusing beep; the seed field has the focus from the start, so typing goes into it)
            Win32.MSG msg;
            while (_done == null)
            {
                if (Window.CloseAsked) { _done = Choice.Quit; break; }
                while (Win32.PeekMessage(out msg, IntPtr.Zero, 0, 0, 1))
                {
                    // a digit typed anywhere on the page but in a field or a list (on the page itself, or with a button holding the
                    // focus after a click) goes into the seed field: the page's own key handling had hunted for a shortcut letter
                    // for it, found none, and refused it with Windows' beep, so no seed could be typed
                    if (msg.message == WM_CHAR && msg.hwnd != _seed && (long)msg.wParam >= '0' && (long)msg.wParam <= '9')
                    {
                        var cls = new StringBuilder(32); GetClassName(msg.hwnd, cls, 32); string cn = cls.ToString();
                        if (!cn.Equals("Edit", StringComparison.OrdinalIgnoreCase) && !cn.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) { SetFocus(_seed); SendMessage(_seed, WM_CHAR, msg.wParam, msg.lParam); continue; }
                    }
                    if (!IsDialogMessage(_wnd, ref msg)) { Win32.TranslateMessage(ref msg); Win32.DispatchMessage(ref msg); }
                }
                if (alive != null) alive();                                           // (the watchdog knows the page is open, not hung)
                System.Threading.Thread.Sleep(10);
            }
            var b = new StringBuilder(32); GetWindowText(_seed, b, 32); ulong v;
            if (b.Length > 0 && ulong.TryParse(b.ToString(), out v)) seed = v;
            if (_replaySeed.HasValue) seed = _replaySeed;                                  // (a universe clip played: its own seed)
            Settings.Set("show-fps", SendMessage(_fps, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1);
            Settings.Set("show-stats", SendMessage(_stats, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1);
            Settings.Set("invert-up-down", SendMessage(_invert, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1);
            Settings.Set("drag-speed", (int)SendMessage(_drag, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero) + 1);
            RecordTour = SendMessage(_record, BM_GETCHECK, IntPtr.Zero, IntPtr.Zero) == (IntPtr)1; Settings.Set("film-tour", RecordTour); { int ci = (int)SendMessage(_clipSize, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero); Settings.Set("clip-down", ci == 1 ? 2 : ci == 2 ? 4 : 1); }
            Window.LandingOpen = false; Win32.DestroyWindow(_wnd); _wnd = IntPtr.Zero; SetForegroundWindow(owner);
            return _done.Value;
        }

        private static IntPtr Child(string cls, string text, uint style, int id, int x, int y, int w, int h, IntPtr font, uint ex = 0)
        {
            var c = Win32.CreateWindowEx(ex, cls, text, WS_CHILD | WS_VISIBLE | style, x, y, w, h, _wnd, (IntPtr)id, Win32.GetModuleHandle(null), IntPtr.Zero);
            SendMessage(c, WM_SETFONT, font, (IntPtr)1); return c;
        }
        // the clips in the captures folder, newest first, by name and date
        private static void ListClips()
        {
            SendMessage(_clips, CB_RESETCONTENT, IntPtr.Zero, IntPtr.Zero); _clipFiles = new string[0];
            try
            {
                if (System.IO.Directory.Exists(Captures))
                {
                    var f = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Concat(System.IO.Directory.GetFiles(Captures, "*.cdf97w"), System.IO.Directory.GetFiles(Captures, "*.cdf97u"))); Array.Sort(f, (a, b) => System.IO.File.GetLastWriteTime(b).CompareTo(System.IO.File.GetLastWriteTime(a))); _clipFiles = f;
                }
            }
            catch { }
            if (_clipFiles.Length == 0) SendMessage(_clips, CB_ADDSTRING, IntPtr.Zero, "none yet (Film the tour, or F9 in flight)");
            foreach (var f in _clipFiles) SendMessage(_clips, CB_ADDSTRING, IntPtr.Zero, System.IO.Path.GetFileName(f));
            SendMessage(_clips, CB_SETCURSEL, IntPtr.Zero, IntPtr.Zero);
        }
        private static uint SafeDpi(IntPtr h) { try { return GetDpiForWindow(h); } catch { return 96; } }

        private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == WM_CTLCOLORSTATIC) { SetTextColor(w, Ink); SetBkColor(w, Back); return _brush; }   // light words on the dark page
            if (msg == WM_CLOSE) { _done = Choice.Quit; return IntPtr.Zero; }
            if (msg == WM_COMMAND)
            {
                int id = (int)((long)w & 0xFFFF), code = (int)(((long)w >> 16) & 0xFFFF);
                if (id == IdSeed) { if (code == EN_MAXTEXT) SendMessage(_seed, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1)); }   // (the field full at nineteen digits: the whole number selected, so the next digit typed starts a new one instead of being refused with a beep)
                else if (id == IdBegin) _done = Choice.Begin;
                else if (id == IdTour) _done = Choice.Tour;
                else if (id == IdQuit) _done = Choice.Quit;
                else if (id == IdGuide && _guide != null) _guide();
                else if (id == IdPlay)
                {
                    // the clip chosen: played here (the universe made again from its seed, or the waveforms in this program's own player)
                    int i = (int)SendMessage(_clips, CB_GETCURSEL, IntPtr.Zero, IntPtr.Zero);
                    if (i >= 0 && i < _clipFiles.Length && System.IO.File.Exists(_clipFiles[i])) {
                        ulong us; string ut;
                        if (_clipFiles[i].EndsWith(".cdf97u", StringComparison.OrdinalIgnoreCase) && Universe.Render.ClipWriters.ReadUniverse(_clipFiles[i], out us, out ut)) { _replaySeed = us; ReplayTour = ut; _done = Choice.Tour; }   // (the universe made again from its seed, the tour flown again through it, not filmed)
                        else if (Universe.Render.UniverseClipReader.Is(_clipFiles[i]))
                        {
                            // a clip of the universe itself: the universe made again from its seed, the clip played through it
                            try { var rc = new Universe.Render.UniverseClipReader(_clipFiles[i]); if (rc.Seed.HasValue) _replaySeed = rc.Seed; ReplayClip = _clipFiles[i]; _done = Choice.Begin; } catch { }
                        }
                        else if (ClipPlayer.Plays(_clipFiles[i])) ClipPlayer.Play(_wnd, _clipFiles[i], _alive); else ShellExecute(_wnd, "open", _clipFiles[i], null, null, 1);
                    }
                }
                else if (id == IdFolder) { try { System.IO.Directory.CreateDirectory(Captures); } catch { } ShellExecute(_wnd, "open", Captures, null, null, 1); }
                else if (id == IdRandom) { var r = (ulong)DateTime.UtcNow.Ticks ^ BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0); Win32.SetWindowText(_seed, (r % 10000000000000000000UL).ToString()); SetFocus(_seed); SendMessage(_seed, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1)); }   // (the focus back in the field with the number selected: typing replaces it, instead of the button holding the focus and every digit refused)
                return IntPtr.Zero;
            }
            return Win32.DefWindowProc(h, msg, w, l);
        }
    }
}
