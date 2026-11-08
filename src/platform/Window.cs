// Universe · platform · the game window, made with direct Windows calls.
using System;
using System.Runtime.InteropServices;

namespace Universe.Platform
{
    internal sealed class Window
    {
        private static readonly string ClassName = Universe.Shell.PreviewText.AppName.Replace("-", "") + "Window";   // (its own: UniverseWindow)
        private readonly Win32.WndProc _proc; // kept alive so Windows can call it
        public IntPtr Handle { get; private set; }
        [DllImport("gdi32.dll")] private static extern IntPtr GetStockObject(int i);
        public bool IsOpen { get; private set; }
        public readonly Input Input = new Input();
        public int Width { get { Win32.RECT r; Win32.GetClientRect(Handle, out r); return r.Right - r.Left; } }
        public int Height { get { Win32.RECT r; Win32.GetClientRect(Handle, out r); return r.Bottom - r.Top; } }
        public bool Resized;
        /// <summary>How many pixels the game draws per window pixel (0.5 to 2); the picture is stretched to fit the window.</summary>
        public double RenderScale { get { return Input.Scale; } set { Input.Scale = Math.Max(0.5, Math.Min(2, value)); Resized = true; } }
        public int RenderWidth { get { return Math.Max(1, (int)(Width * Input.Scale)); } }
        public int RenderHeight { get { return Math.Max(1, (int)(Height * Input.Scale)); } }
        public static readonly int[] ScalePercents = { 50, 75, 100, 150, 200 };
        public const int CmdScaleBase = 50;   // render scale i is CmdScaleBase + i
        public const int CmdGpuBase = 70;
        public const int CmdDragBase = 160;   // drag speed i (1 to 10 times) is CmdDragBase + i
        public static int DragSpeed { get { return Math.Max(1, Math.Min(10, Settings.Get("drag-speed", 4))); } }     // GPU use i is CmdGpuBase + i
        /// <summary>How much of the graphics card's time the universe may use, in per cent of each frame (never more than 90).</summary>
        public static readonly int[] GpuPercents = { 10, 25, 40, 50, 60, 75, 90 };
        /// <summary>The GPU use asked for, as a fraction (0.9 = 90%); the detail is turned up or down until the card is that busy.</summary>
        public static double GpuUse = 0.6;
        /// <summary>The detail now (1 = the usual): raised or lowered a little at a time to keep the card at the GPU use asked for.</summary>
        public static double Detail = 1;
        /// <summary>How busy the card was, last measured (0 to 1).</summary>
        public static double GpuBusy;
        public static string GpuName = ""; public static long GpuMB;
        public int MenuCommand; // the last menu item picked, 0 when none

        public const int CmdExit = 1, CmdAskNow = 2, CmdClearMarks = 3;

        public const int CmdLegend = 4, CmdKeeperBase = 100; // keeper i is CmdKeeperBase + i
        private IntPtr _menu = IntPtr.Zero;

        /// <summary>Builds the review menu bar: Exit, Keep going or revert, Legend, and Keepers with a tick box per sample.</summary>
        public void SetReviewMenu(string[] keeperLabels, bool[] ticked)
        {
            IntPtr bar = Win32.CreateMenu(), keep = Win32.CreatePopupMenu();
            for (int i = 0; i < keeperLabels.Length; i++)
                Win32.AppendMenu(keep, Win32.MF_STRING | (ticked[i] ? Win32.MF_CHECKED : 0), (IntPtr)(CmdKeeperBase + i), keeperLabels[i]);
            Win32.AppendMenu(keep, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(keep, Win32.MF_STRING, (IntPtr)CmdClearMarks, "Clear all keepers in this set");
            Win32.AppendMenu(bar, Win32.MF_STRING, (IntPtr)CmdExit, "Exit");
            Win32.AppendMenu(bar, Win32.MF_STRING, (IntPtr)CmdAskNow, "Keep going or revert...");
            Win32.AppendMenu(bar, Win32.MF_POPUP, keep, "Keepers");
            Win32.AppendMenu(bar, Win32.MF_STRING, (IntPtr)CmdLegend, "Legend");
            IntPtr scale = Win32.CreatePopupMenu();
            for (int i = 0; i < ScalePercents.Length; i++)
                Win32.AppendMenu(scale, Win32.MF_STRING | (Math.Abs(ScalePercents[i] - Input.Scale * 100) < 1 ? Win32.MF_CHECKED : 0), (IntPtr)(CmdScaleBase + i), ScalePercents[i] + "%" + (ScalePercents[i] < 100 ? " (faster, softer)" : ScalePercents[i] > 100 ? " (sharper, slower)" : " (the window's own size)"));
            Win32.AppendMenu(bar, Win32.MF_POPUP, scale, "Render scale");
            Win32.SetMenu(Handle, bar);
            if (_menu != IntPtr.Zero) Win32.DestroyMenu(_menu);
            _menu = bar;
            Win32.DrawMenuBar(Handle);
        }

        public const int CmdAbout = 200, CmdReadme = 201, CmdCredits = 202, CmdScaleHud = 203, CmdInvertX = 204, CmdInvertY = 205, CmdHowItWorks = 206, CmdPause = 207, CmdFps = 208, CmdShot = 209, CmdRecord = 210, CmdTour = 211, CmdLanding = 212, CmdAlwaysDay = 213, CmdStats = 214, CmdDocs = 215, CmdCompared = 216, CmdElements = 217, CmdGravity = 230, CmdClipBase = 220, CmdClipFormatBase = 225;   // clip size i (0 auto, 1 full, 2 half, 4 quarter) is CmdClipBase + i
        /// <summary>Whether a clip is being recorded, and the panoramic tour is playing (ticked in the View menu).</summary>
        public static bool Recording, Touring;
        /// <summary>Gravity on (the Time menu): off, every body goes straight on at its speed, every star is blown apart by its own pressure, a galaxy's stars fly off (content/physics.unt @law gravity-off).</summary>
        public static bool GravityOn = true;
        /// <summary>The landing page is up (the game's own close button then quits from it); and its close was pressed.</summary>
        public static volatile bool LandingOpen, CloseAsked;
        /// <summary>Frames a second, measured over the last half second or so (shown on screen when View > Show FPS is on).</summary>
        public static double Fps;
        public const int CmdTimeBase = 90;    // time speed i is CmdTimeBase + i
        public static string[] TimeNames = new string[0]; public static int TimeStep;
        /// <summary>The preview's menu bar: Exit; View (the scale on screen, render scale); Help (Readme, Credits, About).</summary>
        public void SetWorldMenu(bool scaleOn)
        {
            IntPtr bar = Win32.CreateMenu(), view = Win32.CreatePopupMenu(), help = Win32.CreatePopupMenu(), scale = Win32.CreatePopupMenu();
            Win32.AppendMenu(view, Win32.MF_STRING | (scaleOn ? Win32.MF_CHECKED : 0), (IntPtr)CmdScaleHud, "Scale on screen	H");
            for (int i = 0; i < ScalePercents.Length; i++)
                Win32.AppendMenu(scale, Win32.MF_STRING | (Math.Abs(ScalePercents[i] - Input.Scale * 100) < 1 ? Win32.MF_CHECKED : 0), (IntPtr)(CmdScaleBase + i), ScalePercents[i] + "%" + (ScalePercents[i] < 100 ? " (faster, softer)" : ScalePercents[i] > 100 ? " (sharper, slower)" : " (the window's own size)"));
            Win32.AppendMenu(view, Win32.MF_POPUP, scale, "Render scale");
            IntPtr gpuMenu = Win32.CreatePopupMenu();
            if (GpuName.Length > 0) { Win32.AppendMenu(gpuMenu, Win32.MF_STRING | 0x1u, IntPtr.Zero, GpuName + (GpuMB > 0 ? ", " + GpuMB + " MB" : "")); Win32.AppendMenu(gpuMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null); }
            for (int i = 0; i < GpuPercents.Length; i++)
                Win32.AppendMenu(gpuMenu, Win32.MF_STRING | (Math.Abs(GpuPercents[i] - GpuUse * 100) < 1 ? Win32.MF_CHECKED : 0), (IntPtr)(CmdGpuBase + i),
                    GpuPercents[i] + "%" + (GpuPercents[i] <= 25 ? " (light)" : GpuPercents[i] <= 50 ? " (balanced)" : GpuPercents[i] < 90 ? " (high)" : " (the most: never more than 90%)"));
            Win32.AppendMenu(gpuMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(gpuMenu, Win32.MF_STRING | 0x1u, IntPtr.Zero, "now: card " + (int)Math.Round(GpuBusy * 100) + "% busy, detail " + (int)Math.Round(Detail * 100) + "%");
            Win32.AppendMenu(view, Win32.MF_POPUP, gpuMenu, "GPU use");
            IntPtr timeMenu = Win32.CreatePopupMenu();
            Win32.AppendMenu(timeMenu, Win32.MF_STRING | (Settings.Get("always-day", true) ? Win32.MF_CHECKED : 0), (IntPtr)CmdAlwaysDay, "Always day (no night on the worlds)");
            Win32.AppendMenu(timeMenu, Win32.MF_STRING | (GravityOn ? Win32.MF_CHECKED : 0), (IntPtr)CmdGravity, "Gravity (off: every body straight on at its speed, every star blown apart by its own pressure, a galaxy's stars flung off)");
            Win32.AppendMenu(timeMenu, Win32.MF_STRING | (TimeStep == 0 ? Win32.MF_CHECKED : 0), (IntPtr)CmdPause, "Pause\tSpace");
            Win32.AppendMenu(timeMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            for (int i = 1; i < TimeNames.Length; i++) Win32.AppendMenu(timeMenu, Win32.MF_STRING | (TimeStep == i ? Win32.MF_CHECKED : 0), (IntPtr)(CmdTimeBase + i), TimeNames[i]);
            Win32.AppendMenu(timeMenu, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(timeMenu, Win32.MF_STRING | 0x1u, IntPtr.Zero, "slower: ,   faster: .");
            Win32.AppendMenu(view, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(view, Win32.MF_STRING | (Settings.Get("show-fps", true) ? Win32.MF_CHECKED : 0), (IntPtr)CmdFps, "Show FPS (frames a second)");
            Win32.AppendMenu(view, Win32.MF_STRING | (Settings.Get("show-stats", true) ? Win32.MF_CHECKED : 0), (IntPtr)CmdStats, "Show GPU and CPU stats (time a frame, cores, memory)");
            Win32.AppendMenu(view, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(view, Win32.MF_STRING | (Touring ? Win32.MF_CHECKED : 0), (IntPtr)CmdTour, "Panoramic tour (galaxies down to the quarks)\tT");
            Win32.AppendMenu(view, Win32.MF_STRING, (IntPtr)CmdElements, "The periodic table (all 118 elements, each as it really is; Escape comes back)");
            Win32.AppendMenu(view, Win32.MF_STRING, (IntPtr)CmdShot, "Screenshot (PNG, in captures)\tF12");
            Win32.AppendMenu(view, Win32.MF_STRING | (Recording ? Win32.MF_CHECKED : 0), (IntPtr)CmdRecord, "Record a clip (in captures, in the clip format below)\tF9");
            {
                // the clip's format: an MP4 (any player), or a .cdf97w (the pictures as CDF 9/7 wavelet equations; this program plays it)
                IntPtr fmtMenu = Win32.CreatePopupMenu(); int cf = Settings.Get("clip-format", 0);
                Win32.AppendMenu(fmtMenu, Win32.MF_STRING | (cf < 1 ? Win32.MF_CHECKED : 0), (IntPtr)CmdClipFormatBase, "MP4 (H.264 video: plays in any player)");
                Win32.AppendMenu(fmtMenu, Win32.MF_STRING | (cf >= 1 ? Win32.MF_CHECKED : 0), (IntPtr)(CmdClipFormatBase + 1), ".cdf97w (the equations, and a video of them solved: players play it, this program the equations)");
                Win32.AppendMenu(view, Win32.MF_POPUP, fmtMenu, "Clip format");
            }
            {
                // the clip's size against the screen: smaller clips for sharing (a quarter of the pixels is about a quarter of the file)
                IntPtr clipMenu = Win32.CreatePopupMenu(); int cd = Settings.Get("clip-down", 0);
                int[] downs = { 0, 1, 2, 4 }; string[] names = { "Auto (full size up to 1920 wide, half above that)", "Full (the screen's size)", "Half (half the width and height: about a quarter of the file)", "Quarter (about a sixteenth of the file)" };
                for (int i = 0; i < downs.Length; i++) Win32.AppendMenu(clipMenu, Win32.MF_STRING | (cd == downs[i] ? Win32.MF_CHECKED : 0), (IntPtr)(CmdClipBase + downs[i]), names[i]);
                Win32.AppendMenu(view, Win32.MF_POPUP, clipMenu, "Clip size");
            }
            IntPtr dragMenu = Win32.CreatePopupMenu();
            for (int i = 1; i <= 10; i++) Win32.AppendMenu(dragMenu, Win32.MF_STRING | (DragSpeed == i ? Win32.MF_CHECKED : 0), (IntPtr)(CmdDragBase + i), i + "x" + (i == 1 ? " (the view moves with the mouse exactly)" : i == 4 ? " (the usual)" : ""));
            Win32.AppendMenu(view, Win32.MF_POPUP, dragMenu, "Drag speed (look and turn)");
            Win32.AppendMenu(view, Win32.MF_STRING | (Settings.Get("invert-left-right", false) ? Win32.MF_CHECKED : 0), (IntPtr)CmdInvertX, "Invert left-right drag");
            Win32.AppendMenu(view, Win32.MF_STRING | (Settings.Get("invert-up-down", false) ? Win32.MF_CHECKED : 0), (IntPtr)CmdInvertY, "Invert up-down drag");
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdDocs, "Documents: what's here");
            Win32.AppendMenu(help, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdReadme, "Readme");
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdHowItWorks, "How it works (waveform, infinite zoom)");
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdCompared, "Against the field (size, scope, performance)");
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdCredits, "Credits");
            Win32.AppendMenu(help, Win32.MF_SEPARATOR, IntPtr.Zero, null);
            Win32.AppendMenu(help, Win32.MF_STRING, (IntPtr)CmdAbout, "About " + Universe.Shell.PreviewText.AppName);
            Win32.AppendMenu(bar, Win32.MF_STRING, (IntPtr)CmdExit, "Exit");
            Win32.AppendMenu(bar, Win32.MF_STRING, (IntPtr)CmdLanding, "Start page");   // back to the landing page: a new seed, the tour, the clips
            Win32.AppendMenu(bar, Win32.MF_POPUP, view, "View");
            Win32.AppendMenu(bar, Win32.MF_POPUP, timeMenu, "Time");
            Win32.AppendMenu(bar, Win32.MF_POPUP, help, "Help");
            Win32.SetMenu(Handle, bar);
            if (_menu != IntPtr.Zero) Win32.DestroyMenu(_menu);
            _menu = bar;
            Win32.DrawMenuBar(Handle);
        }

        /// <summary>A Windows pop-up with just an OK button.</summary>
        public void Tell(string text, string caption)
        {
            InSizeMove = true;
            try { Win32.MessageBoxTimeout(Handle, text, caption, Win32.MB_OK | Win32.MB_ICONINFORMATION | Win32.MB_TOPMOST, 0, 0xFFFFFFFF); }
            finally { InSizeMove = false; }
        }

        /// <summary>A Windows pop-up with Yes and No buttons. Returns Win32.IDYES, another button id, or Win32.MB_TIMEDOUT.
        /// The game is held still while it is open, so the watchdog is told to wait.</summary>
        public int Ask(string text, string caption, uint seconds)
        {
            InSizeMove = true;
            try { return Win32.MessageBoxTimeout(Handle, text, caption, Win32.MB_YESNO | Win32.MB_ICONQUESTION | Win32.MB_TOPMOST, 0, seconds * 1000); }
            finally { InSizeMove = false; }
        }
        public static volatile bool InSizeMove; // the game is held still on purpose: window moved or resized, or a pop-up is open
        public static volatile bool InMenu;     // a menu is open: Windows runs its own loop while it is, and the game's main loop waits (not a hang)
        public static volatile int ModalIdleTick; // the clock's last tick inside any modal loop of Windows' (a menu, a dialog, a message box): WM_ENTERIDLE comes while one waits

        /// <summary><paramref name="quiet"/>: a short automatic check. It opens small in the bottom-right corner and does not
        /// take the keyboard, so it never gets in the way of the window the player is using.</summary>
        /// <param name="native">Open as a normal window covering the whole screen at its own resolution (no display change), for checks at the size the player plays at; it does not take the keyboard.</param>
        public Window(string title, int width, int height, bool quiet = false, bool native = false)
        {
            _proc = OnMessage;
            IntPtr instance = Win32.GetModuleHandle(null);
            var wc = new Win32.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX)),
                style = Win32.CS_HREDRAW | Win32.CS_VREDRAW,
                lpfnWndProc = _proc,
                hInstance = instance,
                hCursor = Win32.LoadCursor(IntPtr.Zero, (IntPtr)Win32.IDC_ARROW),
                lpszClassName = ClassName
            };
            Win32.OwnIcons(out wc.hIcon, out wc.hIconSm);                                   // its own icon in the title bar and the taskbar
            wc.hbrBackground = GetStockObject(4);                                          // black from the first moment (before the card draws its first frame), never a white box
            if (Win32.RegisterClassEx(ref wc) == 0)
                throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());

            int x = Win32.CW_USEDEFAULT, y = Win32.CW_USEDEFAULT;
            if (native)
            {
                width = Win32.GetSystemMetrics(0); height = Win32.GetSystemMetrics(1); x = 0; y = 0; quiet = true;
            }
            else if (quiet)
            {
                int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);   // the screen size
                width = 640; height = 360; x = sw - width - 8; y = sh - height - 56;
            }
            Handle = Win32.CreateWindowEx(quiet ? Win32.WS_EX_NOACTIVATE : 0u, ClassName, title, Win32.WS_OVERLAPPEDWINDOW | (quiet ? 0u : Win32.WS_VISIBLE),
                x, y, width, height,
                IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (Handle == IntPtr.Zero)
                throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());

            // normal play opens maximized, so the game draws at the screen's full resolution (still a window: display settings untouched)
            Win32.ShowWindow(Handle, quiet ? Win32.SW_SHOWNOACTIVATE : Win32.SW_SHOWMAXIMIZED);
            IsOpen = true;
        }

        /// <summary>Handles every waiting Windows message. Returns false once the window has closed.</summary>
        public bool Pump()
        {
            Win32.MSG msg;
            while (Win32.PeekMessage(out msg, IntPtr.Zero, 0, 0, Win32.PM_REMOVE))
            {
                if (msg.message == Win32.WM_QUIT) { IsOpen = false; break; }
                if (DocViewer.PreTranslate(ref msg)) continue;                        // the document viewer's own keys
                Win32.TranslateMessage(ref msg);
                Win32.DispatchMessage(ref msg);
            }
            return IsOpen;
        }

        public void SetTitle(string title) { Win32.SetWindowText(Handle, title); }

        public void Close()
        {
            if (Handle != IntPtr.Zero) Win32.DestroyWindow(Handle);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
        private IntPtr OnMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            // a click anywhere in the window brings it to the front and gives it the keyboard (never left behind other windows
            // after a click on it), and Windows' question "may I activate you on this click?" is answered yes outright
            if (msg == 0x21 /* WM_MOUSEACTIVATE */) return (IntPtr)1;   // MA_ACTIVATE
            if ((msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN || msg == 0x207 /* WM_MBUTTONDOWN */) && GetForegroundWindow() != hWnd) { BringWindowToTop(hWnd); SetForegroundWindow(hWnd); }
            if (Input.OnMessage(msg, wParam, lParam)) return IntPtr.Zero;
            if (msg == Win32.WM_SIZE) Resized = true;
            if (msg == Win32.WM_COMMAND) { MenuCommand = (int)((long)wParam & 0xFFFF); return IntPtr.Zero; }
            if (msg == Win32.WM_ENTERSIZEMOVE) InSizeMove = true;
            if (msg == Win32.WM_EXITSIZEMOVE) InSizeMove = false;
            if (msg == 0x211) { InMenu = true; ModalIdleTick = Environment.TickCount; }      // WM_ENTERMENULOOP: a menu held open is not a hang (the watchdog had shut the program down after six seconds of one)
            if (msg == 0x212) InMenu = false;                                                 // WM_EXITMENULOOP
            if (msg == 0x121) ModalIdleTick = Environment.TickCount;                           // WM_ENTERIDLE: inside a menu's, a dialog's or a message box's own loop, waiting on the user
            if (msg == 0x10 && LandingOpen) { CloseAsked = true; return IntPtr.Zero; }   // the window's own close while the landing page is up: Quit
            if (msg == Win32.WM_DESTROY)
            {
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
