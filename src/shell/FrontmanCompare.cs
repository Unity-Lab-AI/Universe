// Universe · shell · the front man compared, side by side over the universe (View > Front man compared): the frame as drawn, the
// same frame through our own .cdf97w (CDF 9/7 solved back into pixels) and through each front man built (our own H.264, VP9 and AV1 writers'
// own reconstruction: exactly what a chat app's player will show), each tile with its bytes for the frame and its quality against
// the frame as drawn. Right-click a tile to keep it (a green frame). Refreshed every few seconds from the live frame, worked out on
// a thread of its own so the universe never waits.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class FrontmanCompare : IDisposable
    {
        private sealed class Tile { public string Name; public byte[] Rgb; public long Bytes; public double Db; }
        private readonly IntPtr _device; private readonly ScaleHud _hud;
        private volatile Tile[] _ready; private Tile[] _shown; private UploadBuffer[] _bufs = new UploadBuffer[0]; private int[] _counts = new int[0];
        private bool _busy; private double _next; private int _tw, _th;
        public bool On;
        public FrontmanCompare(IntPtr device) { _device = device; _hud = new ScaleHud(device); }
        /// <summary>The grab's step down, so the frame worked on is about 960 wide.</summary>
        public int Down(int screenW) { return Math.Max(1, screenW / 960); }
        /// <summary>True when a fresh frame is wanted (every three seconds, one at a time).</summary>
        public bool Wants(double now) { return On && !_busy && now >= _next; }
        /// <summary>The frame as drawn (RGB, top row first), worked out on a thread of its own into the tiles.</summary>
        public void Take(byte[] rgb, int w, int h, double now)
        {
            _busy = true; _next = now + 3;
            var src = (byte[])rgb.Clone();
            new System.Threading.Thread(() => { try { _ready = Work(src, w, h); } catch (Exception ex) { Universe.Program.WriteLog("front man compared: " + ex.Message); } _busy = false; }) { IsBackground = true, Name = "front man compared" }.Start();
        }
        static double Db(byte[] a, byte[] b) { double se = 0; for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; se += d * d; } return se <= 0 ? 99 : 10 * Math.Log10(255.0 * 255 / (se / a.Length)); }
        private static Tile[] Work(byte[] rgb, int w, int h)
        {
            var tiles = new List<Tile> { new Tile { Name = "the frame as drawn", Rgb = rgb, Bytes = (long)w * h * 3, Db = 99 } };
            // ours: the .cdf97w's own sums (dyadic CDF 9/7, the steps by subband, our coder), a whole frame, solved back
            int pw = Wavelet97.PadSide(w), ph = Wavelet97.PadSide(h); var st = Cdf97wCodec.StepFor(w); var lw = Cdf97wCodec.LevelWeights(pw, ph);
            var q = new short[3][]; var rec = new double[3][];
            for (int c = 0; c < 3; c++)
            {
                var g = new double[pw * ph];
                for (int y = 0; y < ph; y++) { int ry = y < h ? y : Math.Max(0, 2 * (h - 1) - y); for (int x = 0; x < pw; x++) { int rx = x < w ? x : Math.Max(0, 2 * (w - 1) - x), o = (ry * w + rx) * 3; double r = rgb[o] / 255.0, gg = rgb[o + 1] / 255.0, b = rgb[o + 2] / 255.0;
                    g[y * pw + x] = c == 0 ? 0.299 * r + 0.587 * gg + 0.114 * b : c == 1 ? 0.5 - 0.168736 * r - 0.331264 * gg + 0.5 * b : 0.5 + 0.5 * r - 0.418688 * gg - 0.081312 * b; } }
                Wavelet97.ForwardDyadic(g, pw, ph); q[c] = new short[pw * ph];
                for (int i = 0; i < g.Length; i++) q[c][i] = Cdf97wCodec.Quantise(g[i], st[c] * lw[i]);
                var v = new double[pw * ph]; for (int i = 0; i < v.Length; i++) v[i] = Cdf97wCodec.Value(q[c][i], st[c] * lw[i]); Wavelet97.InverseDyadic(v, pw, ph); rec[c] = v;
            }
            var ours = new byte[w * h * 3];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) { int i = y * pw + x, o = (y * w + x) * 3; double Y = rec[0][i], cb = rec[1][i] - 0.5, cr = rec[2][i] - 0.5; ours[o] = B8(Y + 1.402 * cr); ours[o + 1] = B8(Y - 0.344136 * cb - 0.714136 * cr); ours[o + 2] = B8(Y + 1.772 * cb); }
            tiles.Add(new Tile { Name = ".cdf97w (ours)", Rgb = ours, Bytes = Cdf97wCodec.Pack(q, pw, ph).Length, Db = Db(rgb, ours) });
            // H.264: our own writer, one whole frame, and its own reconstruction (what a player shows), back to RGB
            string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "front-man-compare.mp4");
            var hw = new H264Writer(tmp, w, h);
            hw.Add(rgb, w, h, 33); long hb = hw.Bytes; int YW = (w + 15) / 16 * 16, CWd = YW / 2;
            var h264 = new byte[w * h * 3];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double Y = hw.Y[y * YW + x] - 16, cb = hw.Cb[(y / 2) * CWd + x / 2] - 128, cr = hw.Cr[(y / 2) * CWd + x / 2] - 128; int o = (y * w + x) * 3;
                h264[o] = B8((1.164 * Y + 1.596 * cr) / 255); h264[o + 1] = B8((1.164 * Y - 0.813 * cr - 0.392 * cb) / 255); h264[o + 2] = B8((1.164 * Y + 2.017 * cb) / 255);
            }
            tiles.Add(new Tile { Name = "H.264 (ours)", Rgb = h264, Bytes = hb, Db = Db(rgb, h264) });
            // VP9: our own writer, one whole frame, and its own reconstruction, back to RGB
            var vw = new Vp9Writer(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "front-man-compare.webm"), w, h);
            vw.Add(rgb, w, h, 33); long vb = vw.Bytes; int VS = vw.Stride, VC = VS / 2;
            var vp9 = new byte[w * h * 3];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double Y = vw.Y[y * VS + x] - 16, cb = vw.Cb[(y / 2) * VC + x / 2] - 128, cr = vw.Cr[(y / 2) * VC + x / 2] - 128; int o = (y * w + x) * 3;
                vp9[o] = B8((1.164 * Y + 1.596 * cr) / 255); vp9[o + 1] = B8((1.164 * Y - 0.813 * cr - 0.392 * cb) / 255); vp9[o + 2] = B8((1.164 * Y + 2.017 * cb) / 255);
            }
            tiles.Add(new Tile { Name = "VP9 (ours)", Rgb = vp9, Bytes = vb, Db = Db(rgb, vp9) });
            // AV1: our own writer, one whole frame, and its own reconstruction, back to RGB
            var aw = new Av1Writer(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "front-man-compare-av1.webm"), w, h);
            aw.Add(rgb, w, h, 33); long ab = aw.Bytes; int AS = aw.Stride, AC = AS / 2;
            var av1 = new byte[w * h * 3];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                double Y = aw.Y[y * AS + x] - 16, cb = aw.Cb[(y / 2) * AC + x / 2] - 128, cr = aw.Cr[(y / 2) * AC + x / 2] - 128; int o = (y * w + x) * 3;
                av1[o] = B8((1.164 * Y + 1.596 * cr) / 255); av1[o + 1] = B8((1.164 * Y - 0.813 * cr - 0.392 * cb) / 255); av1[o + 2] = B8((1.164 * Y + 2.017 * cb) / 255);
            }
            tiles.Add(new Tile { Name = "AV1 (ours)", Rgb = av1, Bytes = ab, Db = Db(rgb, av1) });
            foreach (var t in tiles) { t.Rgb = Shrink(t.Rgb, w, h, 320); }
            return tiles.ToArray();
        }
        static byte B8(double v) { int i = (int)(v * 255 + 0.5); return (byte)(i < 0 ? 0 : i > 255 ? 255 : i); }
        // a picture taken down to tw wide (each tile pixel a square on screen), its height kept in proportion
        static byte[] Shrink(byte[] rgb, int w, int h, int tw)
        {
            int th = Math.Max(1, h * tw / w); var o = new byte[tw * th * 3 + 8];
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++) { int sx = x * w / tw, sy = y * h / th; for (int k = 0; k < 3; k++) o[(y * tw + x) * 3 + k] = rgb[(sy * w + sx) * 3 + k]; }
            o[o.Length - 8] = (byte)(tw & 255); o[o.Length - 7] = (byte)(tw >> 8); o[o.Length - 6] = (byte)(th & 255); o[o.Length - 5] = (byte)(th >> 8);
            return o;
        }
        /// <summary>Draws the tiles over the universe (and a right-click on one keeps it).</summary>
        public void Draw(MeshPipes pipes, Universe.Platform.Input input, int w, int h)
        {
            if (!On) return;
            var t = _ready;
            if (t != null && t != _shown)
            {
                _shown = t; _tw = t[0].Rgb[t[0].Rgb.Length - 8] | (t[0].Rgb[t[0].Rgb.Length - 7] << 8); _th = t[0].Rgb[t[0].Rgb.Length - 6] | (t[0].Rgb[t[0].Rgb.Length - 5] << 8);
                foreach (var b in _bufs) if (b != null) b.Dispose();                       // (freed some frames later: the card may still read them)
                _bufs = new UploadBuffer[t.Length]; _counts = new int[t.Length];
            }
            if (_shown == null) { _hud.DrawCard(pipes, new[] { "Front man compared", "working out the first frame..." }, w * 0.04, h * 0.1, w, h); return; }
            float gap = w * 0.01f, tileW = (w - gap * (_shown.Length + 1)) / _shown.Length, s = tileW / _tw, tileH = _th * s, top = h * 0.12f;
            for (int k = 0; k < _shown.Length; k++)
            {
                float x0 = gap + k * (tileW + gap);
                bool keep = Universe.Platform.Settings.Get("frontman-keep-" + k, false);
                if (input.RightClicked && input.MouseX >= x0 && input.MouseX < x0 + tileW && input.MouseY >= top && input.MouseY < top + tileH) { keep = !keep; Universe.Platform.Settings.Set("frontman-keep-" + k, keep); }
                if (_bufs[k] == null)
                {
                    var l = new List<StarVertex>(_tw * _th * 6 + 48); var px = _shown[k].Rgb;
                    for (int y = 0; y < _th; y++) for (int x = 0; x < _tw; x++) { int o = (y * _tw + x) * 3; Quad(l, x0 + x * s, top + y * s, s + 0.5f, s + 0.5f, px[o] / 255f, px[o + 1] / 255f, px[o + 2] / 255f); }
                    _bufs[k] = new UploadBuffer(_device, (long)l.Count * System.Runtime.InteropServices.Marshal.SizeOf(typeof(StarVertex))); _bufs[k].Write(l.ToArray(), l.Count); _counts[k] = l.Count;
                }
                if (keep) { var fl = new List<StarVertex>(); float bw = Math.Max(2, s * 2); Quad(fl, x0 - bw, top - bw, tileW + 2 * bw, bw, 0.2f, 0.9f, 0.3f); Quad(fl, x0 - bw, top + tileH, tileW + 2 * bw, bw, 0.2f, 0.9f, 0.3f); Quad(fl, x0 - bw, top, bw, tileH, 0.2f, 0.9f, 0.3f); Quad(fl, x0 + tileW, top, bw, tileH, 0.2f, 0.9f, 0.3f); DrawList(pipes, fl, w, h); }
                DrawBuf(pipes, _bufs[k], _counts[k], w, h);
                var tl = _shown[k];
                _hud.DrawCard(pipes, new[] { tl.Name, k == 0 ? "the original, " + (tl.Bytes / 1024) + " KB of pixels" : (tl.Bytes / 1024) + " KB this frame, " + (tl.Db >= 98 ? "exact" : tl.Db.ToString("0.0") + " dB"), keep ? "kept (right-click to let go)" : "right-click to keep" }, x0, top + tileH + gap, w, h);
            }
        }
        static void Quad(List<StarVertex> l, float x, float y, float sw, float sh, float r, float g, float b)
        {
            var a = new StarVertex { X = x, Y = y, Z = 0, Size = 1, R = r, G = g, B = b, A = 1 }; var bq = a; bq.X = x + sw; var c = a; c.X = x + sw; c.Y = y + sh; var d = a; d.Y = y + sh;
            l.Add(a); l.Add(bq); l.Add(c); l.Add(a); l.Add(c); l.Add(d);
        }
        private readonly List<UploadBuffer> _temp = new List<UploadBuffer>();
        void DrawList(MeshPipes pipes, List<StarVertex> l, int w, int h)
        {
            var buf = new UploadBuffer(_device, (long)l.Count * System.Runtime.InteropServices.Marshal.SizeOf(typeof(StarVertex))); buf.Write(l.ToArray(), l.Count);
            DrawBuf(pipes, buf, l.Count, w, h); buf.Dispose();                             // (freed some frames later)
        }
        static void DrawBuf(MeshPipes pipes, UploadBuffer buf, int count, int w, int h)
        {
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = 2f / w; c2[5] = -2f / h; c2[10] = 0; c2[12] = -1; c2[13] = 1; c2[14] = 0.0001f; c2[15] = 1; c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(buf, count, c2, 0, 0, w, h);
        }
        public void Dispose() { foreach (var b in _bufs) if (b != null) b.Dispose(); _hud.Dispose(); }
    }
}
