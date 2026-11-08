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
        /// <summary>Reads a .cdf97u, the few lines older filmed tours had beside them (no longer written: a filmed tour is now the
        /// .cdf97w of the universe itself, its seed and tour in its own metadata): its seed and tour (false: not one).</summary>
        public static bool ReadUniverse(string path, out ulong seed, out string tour)
        {
            seed = 0; tour = null;
            try
            {
                var lines = System.IO.File.ReadAllLines(path); if (lines.Length == 0 || !lines[0].StartsWith("C97U")) return false;
                foreach (var l in lines) { if (l.StartsWith("seed: ")) ulong.TryParse(l.Substring(6).Trim(), out seed); if (l.StartsWith("tour: ")) tour = l.Substring(6).Trim(); }
                return tour != null && tour.Length > 0;
            }
            catch { return false; }
        }
        /// <summary>The clip format picked (View > Clip format, or the start page): 0 MP4, 1 .cdf97w, 2 an MP4 carrying the .cdf97w (the front man).</summary>
        public static int Format { get { int f = Universe.Platform.Settings.Get("clip-format", 0); return f == 1 || f == 2 ? f : 0; } }   // 0 MP4, 1 the .cdf97w of waveforms, 2 the .cdf97w of the universe itself
        public static string Extension { get { return Format >= 1 ? ".cdf97w" : ".mp4"; } }
        /// <summary>The graphics card the .cdf97w's wavelet runs on (set once the renderer is made; none: the processor).</summary>
        public static IntPtr Device;
        /// <summary>The universe's seed and the tour being filmed, for a clip's metadata (null: none).</summary>
        public static ulong? Seed; public static string Tour;
        /// <summary>Called before a clip's buffers on the card are let go: the card finishes its frames first, and stops recording.</summary>
        public static Action BeforeCardRelease;
        /// <summary>Opens a clip at <paramref name="path"/> (its extension set to the format picked).</summary>
        public static IClipWriter Open(string path, int w, int h)
        {
            path = System.IO.Path.ChangeExtension(path, Extension);
            if (Format == 1) return new Cdf97wWriter(path, w, h);                            // the .cdf97w: the waveforms and their metadata, nothing else
            if (Format == 2) return new UniverseClipWriter(path);                            // the .cdf97w of the universe itself: the seed and the camera's track, no pixels
            return new Mp4Writer(path, w, h);
        }
    }

    /// <summary>The .cdf97w's sums, shared by the writer and the player. Version 2: every wavelet term of a plane quantised by one
    /// step (a dead zone, so most become zero), a frame stored whole (a key, every 30th) or as its change from the last frame,
    /// and packed by the wavelet coder, strip by strip on every core (version 4; 3 was one stream, 2 used Deflate).</summary>
    internal static class Cdf97wCodec
    {
        public const uint Magic = 0x57373943;                // 'C97W'
        public const ushort Version = 8;
        /// <summary>The quantising step of each plane at the finest level: Y, Cb, Cr. Each coarser level's step is smaller by its own
        /// weight (<see cref="LevelWeights"/>). Measured on the filmed tour (FRONTMAN-RESEARCH.md, E10): at a quarter of 4K about
        /// 36 KB a frame at 34.5 dB, where one step for every level gave 29 dB at that size and a DCT picture 31.</summary>
        public static readonly float[] Step = { 0.032f, 0.064f, 0.064f };
        /// <summary>The steps for a clip this wide: half of these above 1920 (a 4K picture's single-pixel stars live in its finest terms;
        /// measured on the filmed tour with the dyadic layout, H9: 43 of 46 stars kept at half, 45.5 dB, 1.5 MB a whole frame).</summary>
        public static float[] StepFor(int width) { float k = width > 1920 ? 0.5f : 1f; return new[] { Step[0] * k, Step[1] * k, Step[2] * k }; }
        /// <summary>The clip's metadata (what made it, when, from which seed and tour), written into every .cdf97w after its header.</summary>
        public static string Meta(int w, int h, float[] steps)
        {
            // everything that turns this file's waveforms back into pixels, in readable lines: the layout, the lifting, the steps and
            // weights of every subband, the colour, the frames, the packing; then what made it
            int pw = Wavelet97.PadSide(w), ph = Wavelet97.PadSide(h); var lw = LevelWeights(pw, ph); var L = WaveletCoder.LevelMap(pw, ph);
            var seen = new System.Collections.Generic.SortedDictionary<int, float>(); for (int i = 0; i < L.Length; i++) if (!seen.ContainsKey(L[i])) seen[L[i]] = lw[i];
            var sb = new System.Text.StringBuilder();
            sb.Append("format: cdf97w " + Version + "\n");
            sb.Append("picture: " + w + " x " + h + ", stored padded to " + pw + " x " + ph + " by mirroring the last row and column\n");
            sb.Append("transform: CDF 9/7 lifting, dyadic, " + Wavelet97.DyadicLevels + " levels; each level splits the low corner, rows then columns; whole-sample mirror at the edges\n");
            sb.Append("lifting: alpha " + Wavelet97.A97.ToString("R") + ", beta " + Wavelet97.B97.ToString("R") + ", gamma " + Wavelet97.G97.ToString("R") + ", delta " + Wavelet97.D97.ToString("R") + ", K " + Wavelet97.K97.ToString("R") + "\n");
            sb.Append("forward, one line of n: odd += alpha(left+right), last odd += 2 alpha prev; even += beta(left+right), first += 2 beta next; odd += gamma(...), last += 2 gamma prev; even += delta(...), first += 2 delta next; low = even / K first half, high = odd x K second half\n");
            sb.Append("inverse, one line of n: even = K low, odd = high / K interleaved; even -= delta(...), first -= 2 delta next; odd -= gamma(...), last -= 2 gamma prev; even -= beta(...), first -= 2 beta next; odd -= alpha(...), last -= 2 alpha prev; from the coarsest level, columns then rows\n");
            sb.Append("subband: label = level x 4 + orientation (level 1 the finest; 0 the coarsest low corner, 1 high across, 2 high down, 3 high both)\n");
            sb.Append("steps: Y " + steps[0].ToString("R") + ", Cb " + steps[1].ToString("R") + ", Cr " + steps[2].ToString("R") + " (a term's step = its plane's step x its subband's weight)\n");
            sb.Append("weights:"); foreach (var kv in seen) sb.Append(" " + kv.Key + "=" + kv.Value.ToString("R")); sb.Append("\n");
            sb.Append("value: term q to coefficient (q + half its sign) x step; q = 0 is 0\n");
            sb.Append("colour: Y Cb Cr in 0..1, Cb and Cr centred on 0.5; R = Y + 1.402 (Cr - 0.5), G = Y - 0.344136 (Cb - 0.5) - 0.714136 (Cr - 0.5), B = Y + 1.772 (Cb - 0.5); x 255, rounded, held to 0..255\n");
            sb.Append("frames: each its time in milliseconds, whole (kind 0) or the change from the frame before (kind 1, added term by term), a whole one every " + KeyEvery + "; 30 a second at most\n");
            sb.Append("packing: per frame, the three planes each cut into " + WaveletCoder.Strips + " strips of rows, each strip an adaptive binary range coder (11-bit odds moved a 32nd each bit): nonzero or not by subband and how many of the left and upper terms are nonzero, sign, size as a unary count to 15 by subband then Exp-Golomb\n");
            sb.Append("program: " + Universe.Shell.PreviewText.AppName + " " + Universe.Shell.PreviewText.Version + "\nfilmed: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n");
            if (ClipWriters.Seed.HasValue) sb.Append("seed: " + ClipWriters.Seed.Value + "\n");
            if (ClipWriters.Tour != null) sb.Append("tour: " + ClipWriters.Tour + "\n");
            return sb.ToString();
        }
        private static float[] _lw; private static int _lwW, _lwH;
        /// <summary>Each position's share of its plane's step: one over the square root of how much picture energy a term at its level
        /// spreads (an impulse there, rebuilt), against the finest level's. The error then lands evenly over every level (the rule that
        /// gives the least picture error for the bytes), where one step for all had left the coarse levels, the ones the eye sees
        /// most, as rough as the finest.</summary>
        public static float[] LevelWeights(int pw, int ph)
        {
            lock (typeof(Cdf97wCodec))
            {
                if (_lw != null && _lwW == pw && _lwH == ph) return _lw;
                var L = WaveletCoder.LevelMap(pw, ph); int top = 0; foreach (byte b in L) top = Math.Max(top, b);
                var energy = new double[top + 1];
                for (int lv = 1; lv <= top; lv++)
                {
                    int at = -1, n = 0; for (int i = 0; i < L.Length; i++) if (L[i] == lv && n++ == 0) at = i;
                    int mid = -1, k = 0; for (int i = 0; i < L.Length && mid < 0; i++) if (L[i] == lv && k++ == n / 2) mid = i;
                    if (at < 0) { energy[lv] = -1; continue; }                     // (no position has this label)
                    var g = new double[pw * ph]; g[mid >= 0 ? mid : at] = 1; Wavelet97.InverseDyadic(g, pw, ph);
                    double e = 0; foreach (double v in g) e += v * v; energy[lv] = Math.Max(1e-12, e);
                }
                double e1 = double.MaxValue; for (int lv = 1; lv <= top; lv++) if (energy[lv] > 0) e1 = Math.Min(e1, energy[lv]);   // (against the subband whose terms spread least: the finest)
                var w = new float[pw * ph]; for (int i = 0; i < w.Length; i++) w[i] = (float)(1 / Math.Sqrt(energy[L[i]] / e1));
                _lw = w; _lwW = pw; _lwH = ph; return w;
            }
        }
        public const int KeyEvery = 30;

        public static short Quantise(double v, float step) { double q = v / step; q = q < 0 ? Math.Ceiling(q) : Math.Floor(q); return (short)Math.Max(-32767, Math.Min(32767, q)); }
        public static double Value(short q, float step) { return q == 0 ? 0 : (q + (q > 0 ? 0.5 : -0.5)) * step; }

        // version 3: the terms packed by the wavelet coder (src/render/WaveletCoder.cs), 1.4 to 1.75 times smaller than Deflate was
        public static byte[] Pack(short[][] planes, int pw, int ph) { return WaveletCoder.Pack(planes, pw, ph); }
        public static void Unpack(byte[] data, short[][] into, int pw, int ph) { WaveletCoder.Unpack(data, into, pw, ph); }
    }

    /// <summary>The clip as the universe itself (FRONTMAN-RESEARCH.md section 11): no pixels. The seed, then the camera's track through
    /// the universe's own coordinates (the "dark energy" mesh, 0,0,0 and the clip's time stamp its origin), a point a frame, and every
    /// call that set the matter's scene with its exact arguments; the program makes the universe again from the seed and draws each
    /// point, at any size. Version 9, kind 1; the records packed with Deflate.</summary>
    internal sealed class UniverseClipWriter : IClipWriter
    {
        public const ushort Version = 9; public const byte Kind = 1;
        private readonly System.IO.FileStream _file; private readonly System.IO.Compression.DeflateStream _z; private readonly System.IO.BinaryWriter _o;
        private readonly long _t0 = System.Diagnostics.Stopwatch.GetTimestamp(); private long _last = -1; private int _points;
        public int Down { get { return 1; } }
        public string Path { get; private set; }
        public double Seconds { get { return (System.Diagnostics.Stopwatch.GetTimestamp() - _t0) / (double)System.Diagnostics.Stopwatch.Frequency; } }
        public UniverseClipWriter(string path)
        {
            Path = path;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            _file = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write);
            var h = new System.IO.BinaryWriter(_file);
            h.Write(Cdf97wCodec.Magic); h.Write(Version); h.Write(Kind);
            var meta = System.Text.Encoding.UTF8.GetBytes(Meta()); h.Write((ushort)meta.Length); h.Write(meta); h.Flush();
            _z = new System.IO.Compression.DeflateStream(_file, System.IO.Compression.CompressionMode.Compress, true); _o = new System.IO.BinaryWriter(_z);
            // the scene already built when the clip begins (a clip begun mid-dive), then every call from here on
            foreach (var c in Universe.Shell.MatterView.Scene) Event(c.Key, c.Value, 0);
            Universe.Shell.MatterView.Recorded = (n, a) => Event(n, a, Ms());
        }
        private long Ms() { return (System.Diagnostics.Stopwatch.GetTimestamp() - _t0) * 1000L / System.Diagnostics.Stopwatch.Frequency; }
        private static string Meta()
        {
            return "format: cdf97w " + Version + " universe\n" +
                "what: the clip as the universe itself; no pixels. The universe is made again from the seed and each camera point drawn, at any size\n" +
                "mesh: the universe's own coordinates (the dark energy mesh); a point's plot location is its anchor plus its offset, in the universe's units; time from the clip's own 0\n" +
                "point: record 1: milliseconds (u32), anchor x y z, offset x y z (doubles), forward x y z, up x y z (floats), far scale, space scale, space top, matter scale (doubles), the universe's clock in years (double), the matter view's focus x y z and its target x y z (doubles), then as a check the galaxy, the star system and the ground made round it (int32 each)\n" +
                "event: record 2: milliseconds (u32), the call's name (string), its argument count (byte), each argument as a type letter (d double, f float, u ulong, i int, b bool, s string, n none, p parts: a count, then name, element, share, r, g, b) and its value\n" +
                "packing: the records after this block, Deflate\n" +
                (ClipWriters.Seed.HasValue ? "seed: " + ClipWriters.Seed.Value + "\n" : "") + (ClipWriters.Tour != null ? "tour: " + ClipWriters.Tour + "\n" : "") +
                "program: " + Universe.Shell.PreviewText.AppName + " " + Universe.Shell.PreviewText.Version + "\nfilmed: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n";
        }
        private void Event(string name, object[] args, long ms)
        {
            if (_o == null) return;
            _o.Write((byte)2); _o.Write((uint)ms); _o.Write(name); _o.Write((byte)args.Length);
            foreach (var a in args)
            {
                if (a == null) _o.Write('n');
                else if (a is double) { _o.Write('d'); _o.Write((double)a); }
                else if (a is float) { _o.Write('f'); _o.Write((float)a); }
                else if (a is ulong) { _o.Write('u'); _o.Write((ulong)a); }
                else if (a is int) { _o.Write('i'); _o.Write((int)a); }
                else if (a is bool) { _o.Write('b'); _o.Write((bool)a); }
                else if (a is string) { _o.Write('s'); _o.Write((string)a); }
                else if (a is Universe.Gen.SpaceGen.Part[])
                {
                    var ps = (Universe.Gen.SpaceGen.Part[])a; _o.Write('p'); _o.Write(ps.Length);
                    foreach (var p in ps) { _o.Write(p.Name ?? ""); _o.Write(p.El ?? ""); _o.Write(p.Share); _o.Write(p.R); _o.Write(p.G); _o.Write(p.B); }
                }
                else { _o.Write('s'); _o.Write(a.ToString()); }
            }
        }
        /// <summary>The camera's point for this frame (once each frame the universe has drawn; thirty a second at most).</summary>
        public void Point(Universe.Shell.UniverseView world)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_last >= 0 && (now - _last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency < 28) return;
            _last = now; _o.Write((byte)1); _o.Write((uint)Ms()); world.WritePoint(_o); _points++;
        }
        public void Add(byte[] rgb, int srcW, int srcH) { }                              // (no pixels: the universe draws them again)
        public void Dispose()
        {
            if (Universe.Shell.MatterView.Recorded != null) Universe.Shell.MatterView.Recorded = null;
            try { _o.Flush(); _z.Dispose(); _file.Dispose(); } catch { }
        }
    }

    /// <summary>The front man: an MP4 every player plays, carrying the .cdf97w of the same frames in a private top-level box
    /// (a 'uuid' box, which every MP4 reader skips by its size), so this program plays the equations from the same file.</summary>
    internal sealed class FrontmanWriter : IClipWriter, ICardRecorder
    {
        /// <summary>The box's own id: the .cdf97w inside an MP4.</summary>
        public static readonly byte[] BoxId = { 0x43, 0x39, 0x37, 0x57, 0x2D, 0x55, 0x6E, 0x69, 0x56, 0x73, 0x4D, 0x61, 0x74, 0x69, 0x63, 0x73 };   // "C97W-UniVsMatics"
        private readonly Mp4Writer _mp4; private readonly Cdf97wWriter _eq; private readonly string _eqPath; private bool _done;
        public int Down { get { return _eq.Down; } }
        public double Seconds { get { return _eq.Seconds; } }
        public bool OnCard { get { return _eq.OnCard; } }
        public bool WantsFrame() { return _eq.WantsFrame(); }
        public void Record(IntPtr list, IntPtr backBuffer, int width, int height) { _eq.Record(list, backBuffer, width, height); }
        public void Collect() { _eq.Collect(); }
        private readonly string _final;
        public string Path { get { return _final; } }
        public FrontmanWriter(string path, int w, int h)
        {
            // the .cdf97w: the equations (made on the card from each frame), and the video media players play, made of those same
            // equations solved back into pixels on the card in the same frame (never the frame drawn): an MP4 inside, the equations in
            // its private box. Media Foundation picks its container by the name, so the MP4 is written under a name of its own first
            _final = path; _eqPath = path + ".equations.part";
            _eq = new Cdf97wWriter(_eqPath, w, h, true);
            _mp4 = new Mp4Writer(path + ".video.mp4", _eq.ClipW, _eq.ClipH, 1);
            // the front man: the first solved picture, then one every ten seconds (a player shows the clip's length and a picture; the
            // clip itself is the .cdf97w in the box, played here); the last one shown is held to the end
            _eq.Solved = (rgb, sw, sh) => _mp4.Add(rgb, sw, sh);                             // (every frame: the video is the equations solved)
        }
        // the front man's frames are the equations solved into pixels: each frame goes to the .cdf97w writer only, and the picture
        // its terms rebuild to (the inverse CDF 9/7, the same sums the player runs) is what the MP4 is made of
        public void Add(byte[] rgb, int srcW, int srcH) { _eq.Add(rgb, srcW, srcH); }
        /// <summary>Finishes both, then puts the equations into the MP4 as its last box.</summary>
        public void Dispose()
        {
            if (_done) return; _done = true;
            _eq.Dispose(); _mp4.Dispose();                                                 // (the equations first: their last frames are still being solved into the MP4)
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
            try { if (System.IO.File.Exists(_final)) System.IO.File.Delete(_final); System.IO.File.Move(_mp4.Path, _final); } catch { }   // (the MP4 with its equations, under the .cdf97w's name)
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
    internal sealed class Cdf97wWriter : IClipWriter, ICardRecorder
    {
        public const uint Magic = Cdf97wCodec.Magic;
        private readonly int _w, _h, _pw, _ph; private readonly System.IO.FileStream _file; private readonly System.IO.BinaryWriter _o;
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>> _queue = new System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, long>>(2);
        private readonly System.Threading.Thread _encoder; private long _t0 = -1, _last = -1; private int _frames; private readonly object _lock = new object();
        private short[][] _prev;
        /// <summary>When set, each frame's terms are also solved back into pixels (RGB, top row first, the clip's own size) and handed here: the front man's frames.</summary>
        public Action<byte[], int, int> Solved;
        private CardClip _card;                                                         // the whole frame worked out on the card, inside the frame (null: the frames come here as pictures)
        private readonly System.Collections.Concurrent.BlockingCollection<int> _terms = new System.Collections.Concurrent.BlockingCollection<int>(2);
        /// <summary>How often (seconds) the terms are solved back into a picture for <see cref="Solved"/> (0: every frame).</summary>
        public double SolveEvery = 0; private long _solvedAt = -1;
        /// <summary>True when the frames are worked out on the card inside each frame (the renderer then never copies the picture back).</summary>
        public bool OnCard { get { return _card != null; } }
        public int Down { get; private set; }
        public string Path { get; private set; }
        public double Seconds { get { return _last < 0 ? 0 : (_last - _t0) / (double)System.Diagnostics.Stopwatch.Frequency; } }

        private float[] _steps;
        public int ClipW { get { return _w; } }
        public int ClipH { get { return _h; } }
        private readonly bool _solveOnCard;
        public Cdf97wWriter(string path, int w, int h, bool solveOnCard = false)
        {
            _solveOnCard = solveOnCard;
            int want = Universe.Platform.Settings.Get("clip-down", 0);
            Path = path; Down = want > 0 ? want : (w > 1920 ? 2 : 1);
            _w = Math.Max(2, w / Down); _h = Math.Max(2, h / Down); _pw = Wavelet97.PadSide(_w); _ph = Wavelet97.PadSide(_h);
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            _file = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write); _o = new System.IO.BinaryWriter(_file);
            _o.Write(Magic); _o.Write(Cdf97wCodec.Version); _o.Write((ushort)_w); _o.Write((ushort)_h); _o.Write((ushort)_pw); _o.Write((ushort)_ph);
            _o.Write(0);                                                                   // the frame count (byte 14), filled in when the clip ends
            _steps = Cdf97wCodec.StepFor(_w);
            foreach (float st in _steps) _o.Write(st);
            { var meta = System.Text.Encoding.UTF8.GetBytes(Cdf97wCodec.Meta(_w, _h, _steps)); _o.Write((ushort)meta.Length); _o.Write(meta); }   // (version 7: the metadata after the steps)
            if (ClipWriters.Device != IntPtr.Zero)
            {
                try { _card = new CardClip(ClipWriters.Device, _w, _h, Down, _steps, _solveOnCard); }
                catch (Exception ex) { _card = null; Universe.Program.WriteLog("cdf97w: the frame could not be worked out on the card (" + ex.Message + "); its picture comes to the processor"); }
            }
            // the packing, the one part on the processor: the card's terms (or, without the card, the pictures) in the order they came
            _encoder = new System.Threading.Thread(() =>
            {
                if (_card != null) { foreach (int slot in _terms.GetConsumingEnumerable()) { try { short[][] q; byte[] rgb; long ms; _card.Read(slot, out q, out rgb, out ms); Store(q, ms, rgb); } catch { } } }   // (read from the card's memory here, never on the game's thread)
                else foreach (var f in _queue.GetConsumingEnumerable()) { try { Encode(f.Item1, f.Item2, f.Item3, f.Item4); } catch { } }
            }) { IsBackground = true, Name = "cdf97w packer" };
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

        // ── on the card: asked each frame, recorded into the frame's list, collected when the card is done ──
        public bool WantsFrame()
        {
            if (_card == null) return false;
            long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (_t0 < 0) _t0 = now;
            return _last < 0 || (now - _last) * 1000.0 / System.Diagnostics.Stopwatch.Frequency >= 28;   // (thirty frames a second)
        }
        public void Record(IntPtr list, IntPtr backBuffer, int width, int height)
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_card.Record(list, backBuffer, width, height, (now - _t0) * 1000L / System.Diagnostics.Stopwatch.Frequency)) _last = now;
        }
        public void Collect() { if (_card != null) { var c = _card; c.Collect(s => { if (_terms.IsAddingCompleted || !_terms.TryAdd(s)) c.Release(s); }); } }   // (the packer behind: that frame let go, the clip keeps its timing)

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
                var qc = q[c] = new short[planes[c].Length]; float st = _steps[c]; int pw = _pw; var lw = Cdf97wCodec.LevelWeights(_pw, _ph);
                { Wavelet97.ForwardDyadic(planes[c], _pw, _ph); var co = planes[c]; Jobs.For(_ph, y => { for (int i = y * pw, e = i + pw; i < e; i++) qc[i] = Cdf97wCodec.Quantise(co[i], st * lw[i]); }); }
            }
            Store(q, ms, null);
        }

        // a frame's terms into the clip: whole, or as the change from the last, packed; solved into a picture when one is wanted
        private void Store(short[][] q, long ms, byte[] solvedOnCard)
        {
            bool key = _prev == null || _frames % Cdf97wCodec.KeyEvery == 0;
            short[][] stored = q;
            if (!key)
            {
                // the change from the last frame: a slow camera over a dark sky leaves most terms as they were, and zeros pack to nothing
                stored = new short[3][];
                for (int c = 0; c < 3; c++) { var d = stored[c] = new short[q[c].Length]; var a = q[c]; var b = _prev[c]; for (int i = 0; i < d.Length; i++) d[i] = unchecked((short)(a[i] - b[i])); }
            }
            var packed = Cdf97wCodec.Pack(stored, _pw, _ph); _prev = q;
            if (Solved != null)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (SolveEvery <= 0 || _solvedAt < 0 || (now - _solvedAt) >= System.Diagnostics.Stopwatch.Frequency * SolveEvery) { _solvedAt = now; Solved(solvedOnCard ?? Solve(q), _w, _h); }   // (solved on the card in the frame, or here without a card)
            }
            lock (_lock) { _o.Write((uint)ms); _o.Write((byte)(key ? 0 : 1)); _o.Write(packed.Length); _o.Write(packed); _frames++; }
        }

        /// <summary>A frame's quantised terms solved into its picture: each plane's terms back to values, the inverse CDF 9/7,
        /// YCbCr to RGB (the same sums the .cdf97w player runs, so the front man shows what the equations hold).</summary>
        private byte[] Solve(short[][] q)
        {
            var pl = new double[3][];
            for (int c = 0; c < 3; c++)
            {
                var g = pl[c] = new double[_pw * _ph]; var a = q[c]; float st = _steps[c]; var lw = Cdf97wCodec.LevelWeights(_pw, _ph); for (int k = 0; k < g.Length; k++) g[k] = Cdf97wCodec.Value(a[k], st * lw[k]);
                Wavelet97.InverseDyadic(g, _pw, _ph);
            }
            var rgb = new byte[_w * _h * 3]; int w = _w, pw = _pw;
            Jobs.For(_h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int g = y * pw + x, o = (y * w + x) * 3; double yy = pl[0][g], cb = pl[1][g] - 0.5, cr = pl[2][g] - 0.5;
                    rgb[o] = B(yy + 1.402 * cr); rgb[o + 1] = B(yy - 0.344136 * cb - 0.714136 * cr); rgb[o + 2] = B(yy + 1.772 * cb);
                }
            });
            return rgb;
        }
        private static byte B(double v) { int i = (int)(v * 255 + 0.5); return (byte)(i < 0 ? 0 : i > 255 ? 255 : i); }

        /// <summary>Finishes the clip: the frames still waiting go in, then the frame count.</summary>
        public void Dispose()
        {
            if (_queue.IsAddingCompleted) return;
            if (_card != null && ClipWriters.BeforeCardRelease != null) { try { ClipWriters.BeforeCardRelease(); } catch { } try { Collect(); } catch { } }   // (the card finishes and stops first; its last frames are read)
            _queue.CompleteAdding(); _terms.CompleteAdding(); _encoder.Join(30000);
            if (_card != null) { try { _card.Dispose(); } catch { } _card = null; }
            lock (_lock) { try { _o.Flush(); _file.Seek(14, System.IO.SeekOrigin.Begin); _o.Write(_frames); _o.Flush(); } catch { } try { _o.Close(); } catch { } }
        }
    }
}
