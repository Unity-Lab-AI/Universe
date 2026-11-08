// Universe · render · the clip writers: one call for every clip format, and the .cdf97w clip, pictures as CDF 9/7 wavelet
// equations (the lab's Uni Vs Matics maths, src/core/Wavelet97.cs). No sound, no wrapper: a header, then each frame's time and
// its three planes (Y, Cb, Cr) as kept terms. The program plays it itself (src/platform/ClipPlayer.cs).
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
        /// <summary>The clip format picked (View > Clip format, or the start page): 0 MP4, 1 .cdf97w.</summary>
        public static int Format { get { return Universe.Platform.Settings.Get("clip-format", 0) == 1 ? 1 : 0; } }
        public static string Extension { get { return Format == 1 ? ".cdf97w" : ".mp4"; } }
        /// <summary>Opens a clip at <paramref name="path"/> (its extension set to the format picked).</summary>
        public static IClipWriter Open(string path, int w, int h)
        {
            path = System.IO.Path.ChangeExtension(path, Extension);
            if (Format == 1) return new Cdf97wWriter(path, w, h);
            return new Mp4Writer(path, w, h);
        }
    }

    /// <summary>A .cdf97w clip: every frame turned into its wavelet equations, on a thread of its own.</summary>
    internal sealed class Cdf97wWriter : IClipWriter
    {
        public const uint Magic = 0x57373943;                // 'C97W'
        /// <summary>How much of each plane's energy may be left out: Y, Cb, Cr (tighter than the reference engine's default, so a star field keeps its stars).</summary>
        public static readonly double[] Tol = { 0.006, 0.012, 0.012 };
        private readonly int _w, _h, _pw, _ph; private readonly System.IO.FileStream _file; private readonly System.IO.BinaryWriter _o;
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>> _queue = new System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>>(2);
        private readonly System.Threading.Thread _encoder; private long _t0 = -1, _last = -1; private int _frames; private readonly object _lock = new object();
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
            _o.Write(Magic); _o.Write((ushort)1); _o.Write((ushort)_w); _o.Write((ushort)_h); _o.Write((ushort)_pw); _o.Write((ushort)_ph);
            _o.Write(0);                                                                   // the frame count, filled in when the clip ends
            foreach (double t in Tol) _o.Write((float)t);
            _encoder = new System.Threading.Thread(() => { foreach (var f in _queue.GetConsumingEnumerable()) { try { Encode(f.Item1, f.Item2, f.Item3, f.Item4); } catch { } } }) { IsBackground = true, Name = "cdf97w encoder" };
            _encoder.Start();
        }

        /// <summary>Adds one frame (RGB, top row first) at the moment it was shown; a frame comes while the last is still being worked out is let go (each frame keeps its own time, so the clip keeps its timing).</summary>
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
            var fields = new Wavelet97.Field[3];
            for (int c = 0; c < 3; c++) { Wavelet97.Forward(planes[c], _pw, _ph); fields[c] = Wavelet97.Keep(planes[c], _pw, _ph, Tol[c], c == 0 ? 800 : 400); }
            lock (_lock) { _o.Write((uint)ms); for (int c = 0; c < 3; c++) Wavelet97.Write(_o, fields[c]); _frames++; }
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
