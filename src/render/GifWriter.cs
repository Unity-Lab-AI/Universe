// Universe · render · our own moving-picture writer: frames of the game saved as one looping GIF, which plays anywhere.
// Each frame is coloured from a fixed palette of 252 colours (6 reds × 7 greens × 6 blues) and packed with LZW,
// the way the GIF format asks. Nothing outside the game is used.
using System;
using System.Collections.Generic;
using System.IO;

namespace Universe.Render
{
    internal sealed class GifWriter
    {
        private readonly int _w, _h, _delayCs;
        private readonly List<byte[]> _frames = new List<byte[]>();

        /// <param name="delayCs">How long each frame shows, in hundredths of a second.</param>
        public GifWriter(int width, int height, int delayCs) { _w = width; _h = height; _delayCs = delayCs; }

        public int Count { get { return _frames.Count; } }

        /// <summary>Adds one frame of RGB bytes, top row first; it is turned into palette numbers straight away.</summary>
        public void Add(byte[] rgb)
        {
            var idx = new byte[_w * _h];
            for (int i = 0, p = 0; i < idx.Length; i++, p += 3)
            {
                // a small ordered pattern spreads the steps between palette colours, so smooth glows do not band
                int x = i % _w, y = i / _w, d = ((x & 1) * 2 + (y & 1) * 3) % 4 - 1;
                int r = Math.Max(0, Math.Min(5, (rgb[p] * 5 + 127 + d * 24) / 255));
                int g = Math.Max(0, Math.Min(6, (rgb[p + 1] * 6 + 127 + d * 24) / 255));
                int b = Math.Max(0, Math.Min(5, (rgb[p + 2] * 5 + 127 + d * 24) / 255));
                idx[i] = (byte)(r * 42 + g * 6 + b);
            }
            _frames.Add(idx);
        }

        public void Save(string path)
        {
            using (var f = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                var o = new BinaryWriter(f);
                o.Write(new[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a' });
                o.Write((ushort)_w); o.Write((ushort)_h);
                o.Write((byte)0xF7); o.Write((byte)0); o.Write((byte)0);                        // a 256-entry colour table follows
                for (int i = 0; i < 256; i++)
                {
                    if (i < 252) { o.Write((byte)(i / 42 * 51)); o.Write((byte)(i / 6 % 7 * 255 / 6)); o.Write((byte)(i % 6 * 51)); }
                    else { o.Write((byte)0); o.Write((byte)0); o.Write((byte)0); }
                }
                // loop forever
                o.Write(new byte[] { 0x21, 0xFF, 11 }); o.Write(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0")); o.Write(new byte[] { 3, 1, 0, 0, 0 });
                foreach (var fr in _frames)
                {
                    o.Write(new byte[] { 0x21, 0xF9, 4, 0 }); o.Write((ushort)_delayCs); o.Write((byte)0); o.Write((byte)0);
                    o.Write((byte)0x2C); o.Write((ushort)0); o.Write((ushort)0); o.Write((ushort)_w); o.Write((ushort)_h); o.Write((byte)0);
                    o.Write((byte)8);                                                             // smallest code size
                    var data = Lzw(fr);
                    for (int p = 0; p < data.Count; p += 255)
                    {
                        int n = Math.Min(255, data.Count - p);
                        o.Write((byte)n); for (int k = 0; k < n; k++) o.Write(data[p + k]);
                    }
                    o.Write((byte)0);
                }
                o.Write((byte)0x3B);
            }
        }

        // LZW packing: grows a table of repeated runs, writes codes 9 to 12 bits wide, starts over when the table is full
        private static List<byte> Lzw(byte[] px)
        {
            var outB = new List<byte>(px.Length / 2);
            int bitBuf = 0, bitCount = 0;
            Action<int, int> emit = (code, bits) =>
            {
                bitBuf |= code << bitCount; bitCount += bits;
                while (bitCount >= 8) { outB.Add((byte)(bitBuf & 0xFF)); bitBuf >>= 8; bitCount -= 8; }
            };
            const int Clear = 256, End = 257;
            var dict = new Dictionary<int, int>();
            int width = 9, maxCode = End;
            emit(Clear, width);
            int prefix = px[0];
            for (int i = 1; i < px.Length; i++)
            {
                int k = px[i], key = (prefix << 8) | k, code;
                if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix, width);
                dict[key] = ++maxCode;
                if (maxCode >= (1 << width)) width++;
                if (maxCode == 4095) { emit(Clear, 12); dict.Clear(); width = 9; maxCode = End; }
                prefix = k;
            }
            emit(prefix, width);
            emit(End, width);
            if (bitCount > 0) outB.Add((byte)(bitBuf & 0xFF));
            return outB;
        }
    }
}
