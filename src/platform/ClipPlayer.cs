// Universe · platform · plays a .cdf97w clip in a window of its own: each frame's wavelet equations rebuilt into its picture
// (the inverse CDF 9/7, src/core/Wavelet97.cs, on every core) and shown at the time it was filmed. Space pauses, Escape closes.
using System;
using System.Runtime.InteropServices;
using Universe.Core;

namespace Universe.Platform
{
    internal static class ClipPlayer
    {
        [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public int Size, W, H; public short Planes, Bits; public int Compression, ImageSize, Xppm, Yppm, Used, Important; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
        [DllImport("gdi32.dll")] private static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, int[] bits, ref BitmapInfo bi, uint usage, uint rop);
        [DllImport("gdi32.dll")] private static extern int SetStretchBltMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(IntPtr h, string text, string caption, uint type);

        private sealed class Frame { public uint Ms; public Wavelet97.Field[] Planes; }
        private static Win32.WndProc _proc; private static bool _registered, _closed, _paused;

        /// <summary>True for a clip only this program plays.</summary>
        public static bool Plays(string path) { return path != null && path.EndsWith(".cdf97w", StringComparison.OrdinalIgnoreCase); }

        /// <summary>Plays <paramref name="path"/> until its window is closed; <paramref name="alive"/> is called while it plays.</summary>
        public static void Play(IntPtr owner, string path, Action alive)
        {
            int w, h, pw, ph; var frames = new System.Collections.Generic.List<Frame>();
            try
            {
                using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                {
                    if (r.ReadUInt32() != Universe.Render.Cdf97wWriter.Magic) throw new InvalidOperationException("not a .cdf97w clip");
                    r.ReadUInt16(); w = r.ReadUInt16(); h = r.ReadUInt16(); pw = r.ReadUInt16(); ph = r.ReadUInt16(); int n = r.ReadInt32(); r.ReadSingle(); r.ReadSingle(); r.ReadSingle();
                    long len = r.BaseStream.Length;
                    // every frame to the end of the file (a clip cut off before its count was written still plays to where it stops)
                    while (r.BaseStream.Position + 4 < len) { try { var f = new Frame { Ms = r.ReadUInt32(), Planes = new[] { Wavelet97.Read(r), Wavelet97.Read(r), Wavelet97.Read(r) } }; frames.Add(f); } catch { break; } }
                }
            }
            catch (Exception ex) { MessageBox(owner, "That clip could not be read: " + ex.Message, Universe.Shell.PreviewText.AppName, 0); return; }
            if (frames.Count == 0) { MessageBox(owner, "That clip has no frames.", Universe.Shell.PreviewText.AppName, 0); return; }

            var inst = Win32.GetModuleHandle(null); string cls = Universe.Shell.PreviewText.AppName.Replace("-", "") + "Clip";
            if (!_registered)
            {
                _proc = Proc;
                var wc = new Win32.WNDCLASSEX { cbSize = (uint)Marshal.SizeOf(typeof(Win32.WNDCLASSEX)), lpfnWndProc = _proc, hInstance = inst, hCursor = Win32.LoadCursor(IntPtr.Zero, (IntPtr)32512), hbrBackground = CreateSolidBrush(0), lpszClassName = cls };
                Win32.OwnIcons(out wc.hIcon, out wc.hIconSm); Win32.RegisterClassEx(ref wc); _registered = true;
            }
            int sw = Win32.GetSystemMetrics(0), sh = Win32.GetSystemMetrics(1);
            double fit = Math.Min(1.0, Math.Min(sw * 0.8 / w, sh * 0.8 / h)); int ww = (int)(w * fit) + 16, wh = (int)(h * fit) + 39;
            IntPtr wnd = Win32.CreateWindowEx(0, cls, System.IO.Path.GetFileName(path), 0x00CF0000 | 0x10000000, (sw - ww) / 2, (sh - wh) / 2, ww, wh, owner, IntPtr.Zero, inst, IntPtr.Zero);
            _closed = false; _paused = false;

            // the frames rebuilt on a thread of their own, always the one the clock has reached (one that falls behind is skipped)
            int[] shown = null; int want = 0, built = -1; var gate = new object(); bool stop = false;
            var decoder = new System.Threading.Thread(() =>
            {
                while (!stop)
                {
                    int i; lock (gate) i = want;
                    if (i == built) { System.Threading.Thread.Sleep(2); continue; }
                    var f = frames[i]; var Y = Wavelet97.Rebuild(f.Planes[0]); var Cb = Wavelet97.Rebuild(f.Planes[1]); var Cr = Wavelet97.Rebuild(f.Planes[2]);
                    var px = new int[w * h];
                    Jobs.For(h, y =>
                    {
                        for (int x = 0; x < w; x++)
                        {
                            int g = y * pw + x; double yy = Y[g], cb = Cb[g] - 0.5, cr = Cr[g] - 0.5;
                            int R = Clamp(yy + 1.402 * cr), G = Clamp(yy - 0.344136 * cb - 0.714136 * cr), B = Clamp(yy + 1.772 * cb);
                            px[y * w + x] = (R << 16) | (G << 8) | B;
                        }
                    });
                    lock (gate) { shown = px; built = i; }
                }
            }) { IsBackground = true, Name = "cdf97w player" };
            decoder.Start();

            var bi = new BitmapInfo { Size = 40, W = w, H = -h, Planes = 1, Bits = 32 };
            double clock = 0; long lastTick = System.Diagnostics.Stopwatch.GetTimestamp(); int[] drawn = null;
            while (!_closed)
            {
                Win32.MSG msg;
                while (Win32.PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) { Win32.TranslateMessage(ref msg); Win32.DispatchMessage(ref msg); }
                if (alive != null) alive();
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!_paused) clock += (now - lastTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                lastTick = now;
                double end = frames[frames.Count - 1].Ms + 1000;
                if (clock >= end) clock = 0;                                                    // (it loops)
                int idx = 0; while (idx + 1 < frames.Count && frames[idx + 1].Ms <= clock) idx++;
                int[] pic; lock (gate) { want = idx; pic = shown; }
                if (pic != null && pic != drawn)
                {
                    RECT rc; GetClientRect(wnd, out rc); IntPtr dc = GetDC(wnd);
                    double s = Math.Min((rc.R - rc.L) / (double)w, (rc.B - rc.T) / (double)h); int dw = (int)(w * s), dh = (int)(h * s);
                    SetStretchBltMode(dc, 4);
                    StretchDIBits(dc, (rc.R - dw) / 2, (rc.B - dh) / 2, dw, dh, 0, 0, w, h, pic, ref bi, 0, 0x00CC0020);
                    ReleaseDC(wnd, dc); drawn = pic;
                }
                System.Threading.Thread.Sleep(5);
            }
            stop = true; decoder.Join(2000);
            Win32.DestroyWindow(wnd);
        }

        private static int Clamp(double v) { int i = (int)(v * 255 + 0.5); return i < 0 ? 0 : i > 255 ? 255 : i; }

        private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == 0x10) { _closed = true; return IntPtr.Zero; }                             // WM_CLOSE
            if (msg == 0x100) { if ((long)w == 0x1B) _closed = true; else if ((long)w == 0x20) _paused = !_paused; return IntPtr.Zero; }   // Escape closes, Space pauses
            return Win32.DefWindowProc(h, msg, w, l);
        }
    }
}
