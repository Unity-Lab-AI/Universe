// DNA-AND · platform · keyboard and mouse, read from the window's own messages.
using System;

namespace DnaAnd.Platform
{
    internal sealed class Input
    {
        private readonly bool[] _down = new bool[256];
        private readonly bool[] _pressed = new bool[256];
        public int MouseX, MouseY, MouseDX, MouseDY;
        /// <summary>The render scale: mouse positions are given in the pixels the game draws, not the window's.</summary>
        public double Scale = 1;
        /// <summary>How far to zoom this frame, in notches (+ away from you). The real wheel's notches are not applied at once:
        /// they glide in over about a tenth of a second (<see cref="Glide"/>), and each counts for 0.7 of a step, so zooming is smooth and steady.</summary>
        public double Wheel;
        private double _pending;

        /// <summary>Adds wheel notches to glide in (the real wheel, and test runs).</summary>
        public void Notch(double n) { _pending += n * 0.7; }

        /// <summary>Lets this frame's share of the waiting notches through. <paramref name="fast"/>: eight times as fast (Ctrl held).</summary>
        public void Glide(double dt, bool fast)
        {
            double take = Math.Abs(_pending) < 0.02 ? _pending : _pending * (1 - Math.Exp(-dt * 12));
            _pending -= take; Wheel = take * (fast ? 8 : 1);
        }
        public bool LeftDown, RightDown, LeftClicked, RightClicked;
        private int _lastX, _lastY; private bool _haveLast;

        /// <summary>For test runs: act as if the key was just pressed.</summary>
        public void Press(int key) { if (key >= 0 && key < 256) _pressed[key] = true; }

        public bool IsDown(int key) { return key >= 0 && key < 256 && _down[key]; }
        public bool WasPressed(int key) { return key >= 0 && key < 256 && _pressed[key]; }

        /// <summary>Call once at the start of each frame, after messages are pumped.</summary>
        public void EndFrame()
        {
            Array.Clear(_pressed, 0, 256);
            MouseDX = MouseDY = 0; Wheel = 0; LeftClicked = false; RightClicked = false;
        }

        /// <summary>Feeds one window message. Returns true if it was an input message.</summary>
        /// <summary>Timed test runs ignore the real keyboard and mouse, so the person at the computer is never disturbed by them, and never throws them off.</summary>
        public bool IgnoreReal;

        public bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (IgnoreReal && (msg == Win32.WM_KEYDOWN || msg == Win32.WM_SYSKEYDOWN || msg == Win32.WM_MOUSEMOVE || msg == Win32.WM_LBUTTONDOWN || msg == Win32.WM_RBUTTONDOWN || msg == Win32.WM_MOUSEWHEEL)) return true;
            switch (msg)
            {
                case Win32.WM_KEYDOWN:
                case Win32.WM_SYSKEYDOWN:
                {
                    int k = (int)wParam.ToInt64() & 0xFF;
                    if (!_down[k]) _pressed[k] = true;
                    _down[k] = true; return true;
                }
                case Win32.WM_KEYUP:
                case Win32.WM_SYSKEYUP:
                    _down[(int)wParam.ToInt64() & 0xFF] = false; return true;
                case Win32.WM_MOUSEMOVE:
                {
                    long lp = lParam.ToInt64();
                    int x = (int)((short)(lp & 0xFFFF) * Scale), y = (int)((short)((lp >> 16) & 0xFFFF) * Scale);
                    if (_haveLast) { MouseDX += x - _lastX; MouseDY += y - _lastY; }
                    _lastX = MouseX = x; _lastY = MouseY = y; _haveLast = true; return true;
                }
                case Win32.WM_LBUTTONDOWN: LeftDown = true; LeftClicked = true; return true;
                case Win32.WM_LBUTTONUP: LeftDown = false; return true;
                case Win32.WM_RBUTTONDOWN: RightDown = true; RightClicked = true; return true;
                case Win32.WM_RBUTTONUP: RightDown = false; return true;
                case Win32.WM_MOUSEWHEEL:
                    Notch((short)((wParam.ToInt64() >> 16) & 0xFFFF) / 120.0); return true;
            }
            return false;
        }
    }

    /// <summary>High-resolution frame clock from Windows' performance counter.</summary>
    internal sealed class Clock
    {
        private readonly long _freq; private long _last; private readonly long _start;
        public double Delta { get; private set; }   // seconds since last Tick
        public double Total { get { long n; Win32.QueryPerformanceCounter(out n); return (n - _start) / (double)_freq; } }

        public Clock()
        {
            Win32.QueryPerformanceFrequency(out _freq);
            Win32.QueryPerformanceCounter(out _start); _last = _start;
        }

        public void Tick()
        {
            long now; Win32.QueryPerformanceCounter(out now);
            Delta = Math.Min(0.25, (now - _last) / (double)_freq);
            _last = now;
        }
    }
}
