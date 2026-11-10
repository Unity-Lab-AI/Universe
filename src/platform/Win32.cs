// Universe · platform · direct calls into Windows' own system files.
// Our own declarations: no outside libraries.
using System;
using System.Runtime.InteropServices;

namespace Universe.Platform
{
    internal static class Win32
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint n);
        /// <summary>The program's own icon (large, small), from its file: for its windows and the taskbar.</summary>
        public static void OwnIcons(out IntPtr large, out IntPtr small)
        {
            large = small = IntPtr.Zero; var l = new IntPtr[1]; var s = new IntPtr[1];
            try { if (ExtractIconEx(System.Reflection.Assembly.GetEntryAssembly().Location, 0, l, s, 1) > 0) { large = l[0]; small = s[0]; } } catch (System.Exception ex) { Universe.Program.WriteLog("failed in the window icon: " + ex.Message); }
        }
        public const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const uint WS_VISIBLE = 0x10000000;
        public const int CW_USEDEFAULT = unchecked((int)0x80000000);
        public const uint WM_DESTROY = 0x0002;
        public const uint WM_CLOSE = 0x0010;
        public const uint WM_QUIT = 0x0012;
        public const uint PM_REMOVE = 0x0001;
        public const int SW_SHOW = 5, SW_SHOWNOACTIVATE = 4, SW_SHOWMAXIMIZED = 3;
        public const uint WS_EX_NOACTIVATE = 0x08000000;
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        public const int IDC_ARROW = 32512;
        public const uint CS_HREDRAW = 0x0002;
        public const uint CS_VREDRAW = 0x0001;
        public const uint WM_SIZE = 0x0005;
        public const uint WM_ENTERSIZEMOVE = 0x0231;
        public const uint WM_EXITSIZEMOVE = 0x0232;
        public const uint WM_KEYDOWN = 0x0100;
        public const uint WM_KEYUP = 0x0101;
        public const uint WM_SYSKEYDOWN = 0x0104;
        public const uint WM_SYSKEYUP = 0x0105;
        public const uint WM_MOUSEMOVE = 0x0200;
        public const uint WM_LBUTTONDOWN = 0x0201;
        public const uint WM_LBUTTONUP = 0x0202;
        public const uint WM_RBUTTONDOWN = 0x0204;
        public const uint WM_RBUTTONUP = 0x0205;
        public const uint WM_MOUSEWHEEL = 0x020A;
        public const int VK_ESCAPE = 0x1B, VK_SPACE = 0x20, VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
        public const int VK_F1 = 0x70;

        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public WndProc lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

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

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr GetModuleHandle(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int cmd);

        [DllImport("user32.dll")]
        public static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr DispatchMessage(ref MSG msg);

        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int code);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("kernel32.dll")]
        public static extern void Sleep(uint ms);

        // Per-monitor DPI awareness: the game draws at true pixels. Changes nothing about the display itself.
        [DllImport("user32.dll")]
        public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        public static readonly IntPtr DPI_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [DllImport("kernel32.dll")]
        public static extern bool QueryPerformanceCounter(out long count);

        [DllImport("kernel32.dll")]
        public static extern bool QueryPerformanceFrequency(out long freq);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")]
        public static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool SetWindowText(IntPtr hWnd, string text);

        // menu bar and pop-up boxes, all from Windows itself
        public const uint WM_COMMAND = 0x0111, MF_STRING = 0, MF_POPUP = 0x10;
        public const uint MB_YESNO = 4, MB_OK = 0, MB_ICONQUESTION = 0x20, MB_ICONINFORMATION = 0x40, MB_TOPMOST = 0x40000;
        public const int IDYES = 6, MB_TIMEDOUT = 32000;
        public const uint MF_CHECKED = 0x8, MF_SEPARATOR = 0x800;
        [DllImport("user32.dll")] public static extern IntPtr CreateMenu();
        [DllImport("user32.dll")] public static extern IntPtr CreatePopupMenu();
        [DllImport("user32.dll")] public static extern bool DestroyMenu(IntPtr menu);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(IntPtr menu, uint flags, IntPtr id, string text);
        [DllImport("user32.dll")] public static extern bool SetMenu(IntPtr hWnd, IntPtr menu);
        [DllImport("user32.dll")] public static extern bool DrawMenuBar(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxTimeoutW")]
        public static extern int MessageBoxTimeout(IntPtr hWnd, string text, string caption, uint type, ushort language, uint ms);
    }
}
