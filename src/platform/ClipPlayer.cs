// Universe · platform · plays a .cdf97w clip in a window of its own: each frame's wavelet terms (whole, or the change from the
// frame before added in) rebuilt into its picture by the inverse CDF 9/7 (src/core/Wavelet97.cs, on every core) and shown at
// the time it was filmed. Space pauses, Escape closes, it loops.
using System;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Render;

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

        private sealed class Frame { public uint Ms; public bool Key; public byte[] Data; }
        private static Win32.WndProc _proc; private static bool _registered, _closed, _paused;

        /// <summary>True for a clip this program plays itself: a .cdf97w, or an MP4 carrying one (the front man).</summary>
        public static bool Plays(string path)
        {
            if (path == null) return false;
            if (path.EndsWith(".cdf97w", StringComparison.OrdinalIgnoreCase)) return true;
            if (!path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)) return false;
            try { using (var s = System.IO.File.OpenRead(path)) { long e; return FrontmanWriter.Find(s, out e) >= 0; } } catch { return false; }
        }

        /// <summary>Plays <paramref name="path"/> until its window is closed; <paramref name="alive"/> is called while it plays.</summary>
        public static void Play(IntPtr owner, string path, Action alive)
        {
            int w, h, pw, ph; float[] steps; var frames = new System.Collections.Generic.List<Frame>();
            try
            {
                using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path)))
                {
                    long start = 0, len = r.BaseStream.Length;
                    if (r.ReadUInt32() != Cdf97wCodec.Magic) { long e; start = FrontmanWriter.Find(r.BaseStream, out e); if (start < 0) throw new InvalidOperationException("this file carries no equations"); len = e; }   // (an MP4 outside, the equations in its box)
                    r.BaseStream.Position = start;
                    if (r.ReadUInt32() != Cdf97wCodec.Magic) throw new InvalidOperationException("not a .cdf97w clip");
                    { int ver = r.ReadUInt16(); if (ver == Universe.Render.UniverseClipWriter.Version) throw new InvalidOperationException("this is a clip of the universe itself (the seed and the camera's track): its playback, the universe made again and drawn along the track, is being built"); if (ver != Cdf97wCodec.Version) throw new InvalidOperationException("an older .cdf97w (film it again)"); }
                    w = r.ReadUInt16(); h = r.ReadUInt16(); pw = r.ReadUInt16(); ph = r.ReadUInt16(); r.ReadInt32(); steps = new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle() }; { int ml = r.ReadUInt16(); r.ReadBytes(ml); }   // (the metadata: what made it, when, its seed and tour)
                    // every frame to the end of the file (a clip cut off before its count was written still plays to where it stops)
                    while (r.BaseStream.Position + 9 < len)
                    {
                        try { var f = new Frame { Ms = r.ReadUInt32(), Key = r.ReadByte() == 0 }; int n = r.ReadInt32(); f.Data = r.ReadBytes(n); if (f.Data.Length < n) break; frames.Add(f); }
                        catch { break; }
                    }
                }
            }
            catch (Exception ex) { MessageBox(owner, "That clip could not be read: " + ex.Message, Universe.Shell.PreviewText.AppName, 0); return; }
            while (frames.Count > 0 && !frames[0].Key) frames.RemoveAt(0);
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

            // the frames rebuilt on a thread of their own, always the one the clock has reached (one that falls behind is skipped,
            // its change still added in, so the terms stay right)
            var cur = new[] { new short[pw * ph], new short[pw * ph], new short[pw * ph] };
            var delta = new[] { new short[pw * ph], new short[pw * ph], new short[pw * ph] };
            int[] shown = null; int want = 0, built = -1; var gate = new object(); bool stop = false;
            var decoder = new System.Threading.Thread(() =>
            {
                while (!stop)
                {
                    int i; lock (gate) i = want;
                    if (i == built) { System.Threading.Thread.Sleep(2); continue; }
                    int from;
                    if (built >= 0 && built < i && !AnyKey(frames, built + 1, i)) from = built + 1;
                    else { from = i; while (from > 0 && !frames[from].Key) from--; }
                    for (int j = from; j <= i; j++)
                    {
                        if (frames[j].Key) Cdf97wCodec.Unpack(frames[j].Data, cur, pw, ph);
                        else { Cdf97wCodec.Unpack(frames[j].Data, delta, pw, ph); for (int c = 0; c < 3; c++) { var a = cur[c]; var d = delta[c]; for (int k = 0; k < a.Length; k++) a[k] = unchecked((short)(a[k] + d[k])); } }
                    }
                    var pl = new double[3][];
                    for (int c = 0; c < 3; c++) { var g = pl[c] = new double[pw * ph]; var a = cur[c]; float st = steps[c]; var lw = Cdf97wCodec.LevelWeights(pw, ph); for (int k = 0; k < g.Length; k++) g[k] = Cdf97wCodec.Value(a[k], st * lw[k]); Wavelet97.InverseDyadic(g, pw, ph); }
                    var Y = pl[0]; var Cb = pl[1]; var Cr = pl[2];
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
            double clock = frames[0].Ms; long lastTick = System.Diagnostics.Stopwatch.GetTimestamp(); int[] drawn = null;
            while (!_closed)
            {
                Win32.MSG msg;
                while (Win32.PeekMessage(out msg, IntPtr.Zero, 0, 0, 1)) { Win32.TranslateMessage(ref msg); Win32.DispatchMessage(ref msg); }
                if (alive != null) alive();
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!_paused) clock += (now - lastTick) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                lastTick = now;
                if (clock >= frames[frames.Count - 1].Ms + 1000) clock = frames[0].Ms;     // (it loops)
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

        private static bool AnyKey(System.Collections.Generic.List<Frame> f, int a, int b) { for (int i = a; i <= b; i++) if (f[i].Key) return true; return false; }
        private static int Clamp(double v) { int i = (int)(v * 255 + 0.5); return i < 0 ? 0 : i > 255 ? 255 : i; }

        private static IntPtr Proc(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (msg == 0x10) { _closed = true; return IntPtr.Zero; }                             // WM_CLOSE
            if (msg == 0x100) { if ((long)w == 0x1B) _closed = true; else if ((long)w == 0x20) _paused = !_paused; return IntPtr.Zero; }   // Escape closes, Space pauses
            return Win32.DefWindowProc(h, msg, w, l);
        }
    }
}
