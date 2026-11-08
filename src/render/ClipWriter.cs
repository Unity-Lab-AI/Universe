// Universe · render · the clip writers: one call for every clip format, and the .cdf97w clip, pictures as CDF 9/7 wavelet
// equations (the lab's Uni Vs Matics maths, src/core/Wavelet97.cs). No sound, no wrapper: a header, then each frame's time and
// its three planes (Y, Cb, Cr) of quantised terms, whole or as the change from the last frame, compressed. The program plays
// it itself (src/platform/ClipPlayer.cs).
using System;
using Universe.Core;

namespace Universe.Render
{
    /// <summary>A clip being filmed, whatever its format: frames in, finished on Dispose.</summary>
    internal interface IClipWriter : IDisposable
    {
        void Add(byte[] rgb, int srcW, int srcH);
        int Down { get; }
        double Seconds { get; }
        string Path { get; }
    }

    internal static class ClipWriters
    {
        /// <summary>The clip format picked (View > Clip format, or the start page): 0 MP4, 1 .cdf97w, 2 an MP4 carrying the .cdf97w (the front man).</summary>
        public static int Format { get { int f = Universe.Platform.Settings.Get("clip-format", 0); return f == 1 || f == 2 ? f : 0; } }
        public static string Extension { get { return Format == 1 ? ".cdf97w" : ".mp4"; } }
        /// <summary>Opens a clip at <paramref name="path"/> (its extension set to the format picked).</summary>
        public static IClipWriter Open(string path, int w, int h)
        {
            path = System.IO.Path.ChangeExtension(path, Extension);
            if (Format == 1) return new Cdf97wWriter(path, w, h);
            if (Format == 2) return new FrontmanWriter(path, w, h);
            return new Mp4Writer(path, w, h);
        }
    }

    /// <summary>The .cdf97w's sums, shared by the writer and the player. Version 2: every wavelet term of a plane quantised by one
    /// step (a dead zone, so most become zero), a frame stored whole (a key, every 30th) or as its change from the last frame,
    /// and compressed. Measured on the tour (reference/univsmatics/FRONTMAN-RESEARCH.md, E1c): five to ten times smaller than
    /// the per-term packing of version 1, the size of a DCT picture.</summary>
    internal static class Cdf97wCodec
    {
        public const uint Magic = 0x57373943;                // 'C97W'
        public const ushort Version = 2;
        /// <summary>The quantising step of each plane: Y, Cb, Cr (about 40 dB on the tour's frames).</summary>
        public static readonly float[] Step = { 0.004f, 0.008f, 0.008f };
        public const int KeyEvery = 30;

        public static short Quantise(double v, float step) { double q = v / step; q = q < 0 ? Math.Ceiling(q) : Math.Floor(q); return (short)Math.Max(-32767, Math.Min(32767, q)); }
        public static double Value(short q, float step) { return q == 0 ? 0 : (q + (q > 0 ? 0.5 : -0.5)) * step; }

        public static byte[] Pack(short[][] planes)
        {
            var ms = new System.IO.MemoryStream();
            using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Compress, true))
                foreach (var p in planes) { var b = new byte[p.Length * 2]; Buffer.BlockCopy(p, 0, b, 0, b.Length); z.Write(b, 0, b.Length); }
            return ms.ToArray();
        }
        public static void Unpack(byte[] data, short[][] into)
        {
            using (var z = new System.IO.Compression.DeflateStream(new System.IO.MemoryStream(data), System.IO.Compression.CompressionMode.Decompress))
                foreach (var p in into)
                {
                    var b = new byte[p.Length * 2]; int got = 0;
                    while (got < b.Length) { int n = z.Read(b, got, b.Length - got); if (n <= 0) break; got += n; }
                    Buffer.BlockCopy(b, 0, p, 0, b.Length);
                }
        }
    }

    /// <summary>The front man: an MP4 every player plays, carrying the .cdf97w of the same frames in a private top-level box
    /// (a 'uuid' box, which every MP4 reader skips by its size), so this program plays the equations from the same file.</summary>
    internal sealed class FrontmanWriter : IClipWriter
    {
        /// <summary>The box's own id: the .cdf97w inside an MP4.</summary>
        public static readonly byte[] BoxId = { 0x43, 0x39, 0x37, 0x57, 0x2D, 0x55, 0x6E, 0x69, 0x56, 0x73, 0x4D, 0x61, 0x74, 0x69, 0x63, 0x73 };   // "C97W-UniVsMatics"
        private readonly Mp4Writer _mp4; private readonly Cdf97wWriter _eq; private readonly string _eqPath; private bool _done;
        public int Down { get { return _mp4.Down; } }
        public double Seconds { get { return _mp4.Seconds; } }
        public string Path { get { return _mp4.Path; } }
        public FrontmanWriter(string path, int w, int h)
        {
            _mp4 = new Mp4Writer(path, w, h);
            _eqPath = path + ".cdf97w.part"; _eq = new Cdf97wWriter(_eqPath, w, h);
        }
        public void Add(byte[] rgb, int srcW, int srcH) { _mp4.Add(rgb, srcW, srcH); _eq.Add(rgb, srcW, srcH); }
        /// <summary>Finishes both, then puts the equations into the MP4 as its last box.</summary>
        public void Dispose()
        {
            if (_done) return; _done = true;
            _mp4.Dispose(); _eq.Dispose();
            try
            {
                long n = new System.IO.FileInfo(_eqPath).Length;
                using (var o = new System.IO.FileStream(_mp4.Path, System.IO.FileMode.Append, System.IO.FileAccess.Write))
                using (var src = System.IO.File.OpenRead(_eqPath))
                {
                    // size 1 then a 64-bit size: a box of any length (big-endian, as every MP4 box)
                    long size = 8 + 8 + 16 + n; var head = new byte[32];
                    head[3] = 1; head[4] = (byte)'u'; head[5] = (byte)'u'; head[6] = (byte)'i'; head[7] = (byte)'d';
                    for (int i = 0; i < 8; i++) head[8 + i] = (byte)(size >> (56 - 8 * i));
                    Array.Copy(BoxId, 0, head, 16, 16);
                    o.Write(head, 0, head.Length); src.CopyTo(o);
                }
            }
            catch { }
            try { System.IO.File.Delete(_eqPath); } catch { }
        }
        /// <summary>Where the .cdf97w starts and ends inside an MP4 (-1: it carries none).</summary>
        public static long Find(System.IO.Stream s, out long end)
        {
            end = -1; long p = 0, len = s.Length; var b = new byte[32];
            while (p + 8 <= len)
            {
                s.Position = p; int got = s.Read(b, 0, 32); if (got < 8) break;
                long size = ((long)b[0] << 24) | ((long)b[1] << 16) | ((long)b[2] << 8) | b[3]; int head = 8;
                if (size == 1 && got >= 16) { size = 0; for (int i = 0; i < 8; i++) size = (size << 8) | b[8 + i]; head = 16; }
                else if (size == 0) size = len - p;
                if (size < head) break;
                if (b[4] == 'u' && b[5] == 'u' && b[6] == 'i' && b[7] == 'd' && got >= head + 16)
                {
                    bool ours = true; for (int i = 0; i < 16; i++) if (b[head + i] != BoxId[i]) ours = false;
                    if (ours) { end = p + size; return p + head + 16; }
                }
                p += size;
            }
            return -1;
        }
    }

    /// <summary>A .cdf97w clip: every frame turned into its wavelet equations, on a thread of its own.</summary>
    internal sealed class Cdf97wWriter : IClipWriter
    {
        public const uint Magic = Cdf97wCodec.Magic;
        private readonly int _w, _h, _pw, _ph; private readonly System.IO.FileStream _file; private readonly System.IO.BinaryWriter _o;
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>> _queue = new System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>>(2);
        private readonly System.Threading.Thread _encoder; private long _t0 = -1, _last = -1; private int _frames; private readonly object _lock = new object();
        private short[][] _prev;
        public int Down { get; private set; }
        public string Path { get; private set; }
        public double Seconds { get { return _last < 0 ? 0 : (_last - _t0) / (double)System.Diagnostics.Stopwatch.Frequency; } }

        public Cdf97wWriter(string path, int w, int h)
        {
            int want = Universe.Platform.Settings.Get("clip-down", 0);
            Path = path; Down = want > 0 ? want : (w > 1920 ? 2 : 1);
            _w = Math.Max(2, w / Down); _h = Math.Max(2, h / Down); _pw = Wavelet97.PadSide(_w); _ph = Wavelet97.PadSide(_h);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            _file = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write); _o = new System.IO.BinaryWriter(_file);
            _o.Write(Magic); _o.Write(Cdf97wCodec.Version); _o.Write((ushort)_w); _o.Write((ushort)_h); _o.Write((ushort)_pw); _o.Write((ushort)_ph);
            _o.Write(0);                                                                   // the frame count (byte 14), filled in when the clip ends
            foreach (float st in Cdf97wCodec.Step) _o.Write(st);
            _encoder = new System.Threading.Thread(() => { foreach (var f in _queue.GetConsumingEnumerable()) { try { Encode(f.Item1, f.Item2, f.Item3, f.Item4); } catch { } } }) { IsBackground = true, Name = "cdf97w encoder" };
            _encoder.Start();
        }

        /// <summary>Adds one frame (RGB, top row first) at the moment it was shown; one that comes while the last is still being worked out is let go (each frame keeps its own time, so the clip keeps its timing).</summary>
        public void Add(byte[] rgb, int srcW, int srcH)
        {
            if (rgb == null || rgb.Length < srcW * srcH * 3) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (_t0 < 0) _t0 = now;
            if (_last >= 0 && (now - _last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency < 28) return;
            if (!_queue.TryAdd(Tuple.Create(rgb, srcW, srcH, (now - _t0) * 1000L / System.Diagnostics.Stopwatch.Frequency))) return;
            _last = now;
        }

        private void Encode(byte[] rgb, int srcW, int srcH, long ms)
        {
            // the three planes, reflect-padded to multiples of 64 (as the reference engine pads)
            var planes = new[] { new double[_pw * _ph], new double[_pw * _ph], new double[_pw * _ph] };
            Jobs.For(_ph, y =>
            {
                int ry = y < _h ? y : Math.Max(0, 2 * (_h - 1) - y), sy = Math.Min(srcH - 1, ry * srcH / _h);
                for (int x = 0; x < _pw; x++)
                {
                    int rx = x < _w ? x : Math.Max(0, 2 * (_w - 1) - x), s = (sy * srcW + Math.Min(srcW - 1, rx * srcW / _w)) * 3;
                    double r = rgb[s] / 255.0, g = rgb[s + 1] / 255.0, b = rgb[s + 2] / 255.0; int p = y * _pw + x;
                    planes[0][p] = 0.299 * r + 0.587 * g + 0.114 * b;
                    planes[1][p] = 0.5 - 0.168736 * r - 0.331264 * g + 0.5 * b;
                    planes[2][p] = 0.5 + 0.5 * r - 0.418688 * g - 0.081312 * b;
                }
            });
            var q = new short[3][];
            for (int c = 0; c < 3; c++)
            {
                Wavelet97.Forward(planes[c], _pw, _ph); var co = planes[c]; var qc = q[c] = new short[co.Length]; float st = Cdf97wCodec.Step[c]; int pw = _pw;
                Jobs.For(_ph, y => { for (int i = y * pw, e = i + pw; i < e; i++) qc[i] = Cdf97wCodec.Quantise(co[i], st); });
            }
            bool key = _prev == null || _frames % Cdf97wCodec.KeyEvery == 0;
            short[][] stored = q;
            if (!key)
            {
                // the change from the last frame: a slow camera over a dark sky leaves most terms as they were, and zeros pack to nothing
                stored = new short[3][];
                for (int c = 0; c < 3; c++) { var d = stored[c] = new short[q[c].Length]; var a = q[c]; var b = _prev[c]; for (int i = 0; i < d.Length; i++) d[i] = unchecked((short)(a[i] - b[i])); }
            }
            var packed = Cdf97wCodec.Pack(stored); _prev = q;
            lock (_lock) { _o.Write((uint)ms); _o.Write((byte)(key ? 0 : 1)); _o.Write(packed.Length); _o.Write(packed); _frames++; }
        }

        /// <summary>Finishes the clip: the frames still waiting go in, then the frame count.</summary>
        public void Dispose()
        {
            if (_queue.IsAddingCompleted) return;
            _queue.CompleteAdding(); _encoder.Join(30000);
            lock (_lock) { try { _o.Flush(); _file.Seek(14, System.IO.SeekOrigin.Begin); _o.Write(_frames); _o.Flush(); } catch { } try { _o.Close(); } catch { } }
        }
    }
}
