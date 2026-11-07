// Universe · shell · the scale on screen: a bar of a round length (1, 2 or 5 of a unit) and its label, showing how big things are
// where you are looking, from billions of light-years down past the Planck length. Drawn in screen pixels on top of the picture, in the game's own
// blocky letters. It can be turned on and off.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Universe.Core;
using Universe.Gen;
using Universe.Render;

namespace Universe.Shell
{
    internal sealed class ScaleHud : IDisposable
    {
        private readonly IntPtr _device; private UploadBuffer _buf; private StarVertex[] _v = new StarVertex[0];
        public bool Visible = true;
        /// <summary>How long a card's line may run before it wraps (the corner cards keep short lines; a full screen of words, longer).</summary>
        public int WrapAt = 38;

        public ScaleHud(IntPtr device) { _device = device; }

        // a round length near the wanted one: 1, 2 or 5 times a power of ten
        private static double Round(double want)
        {
            double p = Math.Pow(10, Math.Floor(Math.Log10(want))), m = want / p;
            return (m < 2 ? 1 : m < 5 ? 2 : 5) * p;
        }

        // a length in km written plainly, from the whole universe down past the Planck length (the zoom in has no floor)
        public static string Words(double km)
        {
            const double ly = 9.4607e12;
            if (km >= ly * 1e15) return (km / ly).ToString("0.#e+0") + " light-years";
            if (km >= ly * 1e12 * 0.5) return Num(km / ly / 1e12) + " trillion light-years";
            if (km >= ly * 1e9 * 0.5) return Num(km / ly / 1e9) + " billion light-years";
            if (km >= ly * 1e6 * 0.5) return Num(km / ly / 1e6) + " million light-years";
            if (km >= ly * 0.5) return Num(km / ly) + " light-years";
            if (km >= 1.496e8 * 0.5) return Num(km / 1.496e8) + " AU";
            if (km >= 1) return Num(km) + " km";
            if (km >= 1e-3) return Num(km * 1e3) + " m";
            if (km >= 1e-6) return Num(km * 1e6) + " mm";
            if (km >= 1e-9) return Num(km * 1e9) + " micrometres";
            if (km >= 1e-12) return Num(km * 1e12) + " nanometres";
            if (km >= 1e-15) return Num(km * 1e15) + " picometres";
            if (km >= 1e-18) return Num(km * 1e18) + " femtometres";
            if (km >= 1e-21) return Num(km * 1e21) + " attometres";
            if (km >= 1e-24) return Num(km * 1e24) + " zeptometres";
            if (km >= 1e-27) return Num(km * 1e27) + " yoctometres";
            if (km >= 1e-30) return Num(km * 1e30) + " rontometres";
            if (km >= 1e-33) return Num(km * 1e33) + " quectometres";
            return (km * 1e3).ToString("0.#e-0") + " m";                                  // (past the last named unit, and past the Planck length: the number itself)
        }

        private static string Num(double v) { return v >= 1000 ? v.ToString("N0") : v >= 10 ? v.ToString("0") : v.ToString("0.#"); }

        /// <summary>Draws the scale for <paramref name="kmPerPixel"/> at the middle of the view.</summary>
        public void Draw(MeshPipes pipes, double kmPerPixel, int w, int h)
        {
            if (!Visible || kmPerPixel <= 0 || double.IsInfinity(kmPerPixel) || double.IsNaN(kmPerPixel)) return;
            float px = Math.Max(1, h / 540f);                                   // one letter cell, bigger on bigger screens
            double km = Round(kmPerPixel * 220 * px);
            float barLen = (float)(km / kmPerPixel);
            var list = new List<StarVertex>();
            float x0 = 30 * px, y0 = h - 40 * px;
            // the bar with its two end ticks
            Quad(list, x0, y0, barLen, 2 * px, 0.92f, 0.95f, 1f);
            Quad(list, x0, y0 - 6 * px, 2 * px, 8 * px, 0.92f, 0.95f, 1f);
            Quad(list, x0 + barLen - 2 * px, y0 - 6 * px, 2 * px, 8 * px, 0.92f, 0.95f, 1f);
            // its length, in the game's letters, above the bar
            string label = Words(km).ToUpperInvariant();
            float cx = x0, cy = y0 - 18 * px, cell = 1.6f * px;
            foreach (char ch in label)
            {
                if (ch != ' ') foreach (var c in IconFont.Cells(ch)) Quad(list, cx + c[0] * cell, cy + c[1] * cell, cell * 0.9f, cell * 0.9f, 0.92f, 0.95f, 1f);
                cx += 6 * cell;
            }
            int n = list.Count;
            if (_v.Length < n) _v = new StarVertex[n + 600];
            list.CopyTo(_v);
            if (_buf == null || _buf.Bytes < (long)n * Marshal.SizeOf(typeof(StarVertex))) { if (_buf != null) _buf.Dispose(); _buf = new UploadBuffer(_device, (long)(n + 600) * Marshal.SizeOf(typeof(StarVertex))); }
            _buf.Write(_v, n);
            // screen pixels straight to the picture: x right, y down, nothing in front of it
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[1] = m.M12; c2[2] = m.M13; c2[3] = m.M14; c2[4] = m.M21; c2[5] = m.M22; c2[6] = m.M23; c2[7] = m.M24;
            c2[8] = m.M31; c2[9] = m.M32; c2[10] = m.M33; c2[11] = m.M34; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44;
            c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(_buf, n, c2, 0, 0, w, h);
        }

        private StarVertex[] _ringV = new StarVertex[0];
        /// <summary>A ring of small marks round a point on screen (the thing you picked), in pixels.</summary>
        public void DrawRing(MeshPipes pipes, double x, double y, double radius, int w, int h)
        {
            var list = new List<StarVertex>(); float px = Math.Max(1, h / 540f), s2 = 2.2f * px;
            int n = 32;
            for (int i = 0; i < n; i++)
            {
                if (i % 4 == 3) continue;                                            // a ring broken into dashes
                double a = i * Math.PI * 2 / n;
                Quad(list, (float)(x + Math.Cos(a) * radius) - s2 / 2, (float)(y + Math.Sin(a) * radius) - s2 / 2, s2, s2, 0.55f, 0.95f, 1f);
            }
            int cnt = list.Count;
            if (_ringV.Length < cnt) _ringV = new StarVertex[cnt + 64];
            list.CopyTo(_ringV);
            var ringBuf = Slot(_ringV, cnt);
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[5] = m.M22; c2[10] = m.M33; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44;
            c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(ringBuf, cnt, c2, 0, 0, w, h);
        }

        private StarVertex[] _cardV = new StarVertex[0];
        // the GPU draws a frame after it is recorded, and two or three frames are in flight: so each card or ring drawn gets its
        // own buffer from a ring of them (never one another card wrote over before the GPU read it), and a buffer outgrown is
        // freed only after the ring has gone round many times (never while the GPU may still read it)
        private const int Slots = 24;
        private readonly UploadBuffer[] _slot = new UploadBuffer[Slots]; private int _slotNext;
        private readonly List<KeyValuePair<UploadBuffer, int>> _retired = new List<KeyValuePair<UploadBuffer, int>>(); private int _uses;
        private UploadBuffer Slot(StarVertex[] v, int cnt)
        {
            int i = _slotNext; _slotNext = (_slotNext + 1) % Slots; _uses++;
            long need = (long)Math.Max(1, cnt) * Marshal.SizeOf(typeof(StarVertex));
            if (_slot[i] == null || _slot[i].Bytes < need)
            {
                if (_slot[i] != null) _retired.Add(new KeyValuePair<UploadBuffer, int>(_slot[i], _uses));
                _slot[i] = new UploadBuffer(_device, (long)(cnt + 1200) * Marshal.SizeOf(typeof(StarVertex)));
            }
            for (int k = _retired.Count - 1; k >= 0; k--) if (_uses - _retired[k].Value > Slots * 8) { _retired[k].Key.Dispose(); _retired.RemoveAt(k); }
            _slot[i].Write(v, cnt);
            return _slot[i];
        }
        /// <summary>An information card on screen beside the thing you picked: just its lines of text, no box; the first line its name
        /// (brighter), then its facts. Placed at (x, y) in pixels, kept inside the screen.</summary>
        /// <summary>Letterbox bars across the top and bottom of the screen (each <paramref name="frac"/> of its height).</summary>
        /// <summary>Fills the whole view with one colour (inside a giant's gas: nothing past it can be seen).</summary>
        public void DrawFill(MeshPipes pipes, int w, int h, float r, float g, float b)
        {
            var l = new List<StarVertex>(); Quad(l, 0, 0, w, h, r, g, b);
            var arr = l.ToArray(); var buf = Slot(arr, arr.Length);
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[5] = m.M22; c2[10] = m.M33; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44; c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(buf, arr.Length, c2, 0, 0, w, h);
        }
        /// <summary>The start's loading screen: the dark of space, a bar filling as the universe is made, and what is being made.</summary>
        public void DrawLoading(MeshPipes pipes, int w, int h, double share, IList<string> lines)
        {
            DrawFill(pipes, w, h, 0.012f, 0.014f, 0.03f);
            var l = new List<StarVertex>(); float bw = w * 0.4f, bh = Math.Max(6, h * 0.008f), x0 = (w - bw) / 2, y0 = h * 0.55f;
            Quad(l, x0 - 2, y0 - 2, bw + 4, bh + 4, 0.25f, 0.3f, 0.45f);                    // its frame
            Quad(l, x0, y0, bw, bh, 0.03f, 0.035f, 0.07f);
            Quad(l, x0, y0, (float)(bw * Math.Max(0, Math.Min(1, share))), bh, 0.55f, 0.7f, 1f);   // how far along
            var arr = l.ToArray(); var buf = Slot(arr, arr.Length);
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[5] = m.M22; c2[10] = m.M33; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44; c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(buf, arr.Length, c2, 0, 0, w, h);
            DrawCard(pipes, lines, x0, h * 0.40, w, h);
        }
        public void DrawBars(MeshPipes pipes, int w, int h, float frac)
        {
            var l = new List<StarVertex>(); float bh = h * frac;
            Quad(l, 0, 0, w, bh, 0, 0, 0); Quad(l, 0, h - bh, w, bh, 0, 0, 0);
            var arr = l.ToArray(); var buf = Slot(arr, arr.Length);
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[5] = m.M22; c2[10] = m.M33; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44; c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(buf, arr.Length, c2, 0, 0, w, h);
        }
        public void DrawCard(MeshPipes pipes, IList<string> lines, double x, double y, int w, int h, float fade) { _fade = fade; try { DrawCard(pipes, lines, x, y, w, h); } finally { _fade = 1; } }
        private float _fade = 1;
        public void DrawCard(MeshPipes pipes, IList<string> lines, double x, double y, int w, int h)
        {
            if (lines == null || lines.Count == 0) return;
            // every card short and to one side: long lines wrap at a reading width, the text small enough for a corner, and a card
            // that will not fit beside what you picked goes on its other side, never over it
            int Wrap = WrapAt;
            var wrapped = new List<string>();
            foreach (var l0 in lines)
            {
                string l = l0;
                while (l.Length > Wrap) { int cut = l.LastIndexOf(' ', Wrap); if (cut < Wrap / 2) cut = Wrap; wrapped.Add(l.Substring(0, cut).TrimEnd()); l = "  " + l.Substring(cut).TrimStart(); }
                wrapped.Add(l);
            }
            lines = wrapped;
            float px = Math.Max(1, h / 540f), cell = 1.1f * px, adv = 6 * cell, lh = 10 * cell, pad = 6 * px;
            int longest = 0; foreach (var l in lines) longest = Math.Max(longest, l.Length);
            float cw = longest * adv + pad * 2, ch = lines.Count * lh + pad * 2 - 3 * cell;
            float x0 = (float)x, y0 = (float)Math.Min(Math.Max(4, y), h - ch - 4);
            if (x0 + cw > w - 4) x0 = (float)(x - 70 * px - cw);                         // no room to its right: on its left
            x0 = Math.Max(4, Math.Min(x0, w - cw - 4));
            var list = new List<StarVertex>();
            for (int i = 0; i < lines.Count; i++)
            {
                float cx = x0 + pad, cy = y0 + pad + i * lh, r = (i == 0 ? 0.55f : 0.85f) * _fade, g = (i == 0 ? 0.95f : 0.88f) * _fade, b = 1f * _fade;
                foreach (char c0 in lines[i].ToUpperInvariant())
                {
                    if (c0 != ' ') foreach (var c in IconFont.Cells(c0)) Quad(list, cx + c[0] * cell, cy + c[1] * cell, cell * 0.9f, cell * 0.9f, r, g, b);
                    cx += adv;
                }
            }
            int cnt = list.Count;
            if (_cardV.Length < cnt) _cardV = new StarVertex[cnt + 1200];
            list.CopyTo(_cardV);
            var cardBuf = Slot(_cardV, cnt);
            var m = Mat4.Identity; m.M11 = 2f / w; m.M22 = -2f / h; m.M33 = 0; m.M41 = -1; m.M42 = 1; m.M43 = 0.0001f; m.M44 = 1;
            var c2 = new float[BillboardPipeline.ConstantCount];
            c2[0] = m.M11; c2[5] = m.M22; c2[10] = m.M33; c2[12] = m.M41; c2[13] = m.M42; c2[14] = m.M43; c2[15] = m.M44;
            c2[18] = 1; c2[22] = 1;
            pipes.Solid.Draw(cardBuf, cnt, c2, 0, 0, w, h);
        }

        private static void Quad(List<StarVertex> l, float x, float y, float sw, float sh, float r, float g, float b)
        {
            var a = new StarVertex { X = x, Y = y, Z = 0, Size = 1, R = r, G = g, B = b, A = 1 };
            var bq = a; bq.X = x + sw; var c = a; c.X = x + sw; c.Y = y + sh; var d = a; d.Y = y + sh;
            l.Add(a); l.Add(bq); l.Add(c); l.Add(a); l.Add(c); l.Add(d);
        }

        public void Dispose() { if (_buf != null) _buf.Dispose(); foreach (var b in _slot) if (b != null) b.Dispose(); foreach (var r in _retired) r.Key.Dispose(); }
    }
}
