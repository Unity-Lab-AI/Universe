// DNA-AND · render · screenshots as PNG files, written by the game's own code (the picture's rows packed with the
// framework's own Deflate, the PNG chunks, checksums and header written here).
using System;
using System.IO;
using System.IO.Compression;

namespace DnaAnd.Render
{
    internal static class PngWriter
    {
        private static uint[] _crc;

        /// <summary>Saves an RGB picture (3 bytes a pixel, top row first) as a PNG file.</summary>
        public static void Save(string path, byte[] rgb, int w, int h)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            byte[] packed;
            using (var raw = new MemoryStream())
            {
                // zlib: its two-byte header, the Deflate data, then the Adler-32 of the rows
                raw.WriteByte(0x78); raw.WriteByte(0x01);
                uint a = 1, b = 0;
                using (var z = new DeflateStream(raw, CompressionLevel.Optimal, true))
                {
                    var row = new byte[w * 3 + 1];
                    for (int y = 0; y < h; y++)
                    {
                        row[0] = 0;                                                        // no filter on the row
                        Buffer.BlockCopy(rgb, y * w * 3, row, 1, w * 3);
                        z.Write(row, 0, row.Length);
                        foreach (byte v in row) { a = (a + v) % 65521; b = (b + a) % 65521; }
                    }
                }
                uint ad = (b << 16) | a;
                raw.WriteByte((byte)(ad >> 24)); raw.WriteByte((byte)(ad >> 16)); raw.WriteByte((byte)(ad >> 8)); raw.WriteByte((byte)ad);
                packed = raw.ToArray();
            }
            using (var f = new FileStream(path, FileMode.Create))
            {
                f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
                var hdr = new byte[13]; Be(hdr, 0, (uint)w); Be(hdr, 4, (uint)h); hdr[8] = 8; hdr[9] = 2;   // 8 bits, RGB
                Chunk(f, "IHDR", hdr); Chunk(f, "IDAT", packed); Chunk(f, "IEND", new byte[0]);
            }
        }

        private static void Be(byte[] a, int at, uint v) { a[at] = (byte)(v >> 24); a[at + 1] = (byte)(v >> 16); a[at + 2] = (byte)(v >> 8); a[at + 3] = (byte)v; }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var len = new byte[4]; Be(len, 0, (uint)data.Length); s.Write(len, 0, 4);
            var t = new byte[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] }; s.Write(t, 0, 4); s.Write(data, 0, data.Length);
            if (_crc == null)
            {
                _crc = new uint[256];
                for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crc[n] = c; }
            }
            uint crc = 0xFFFFFFFFu;
            foreach (byte v in t) crc = _crc[(crc ^ v) & 0xFF] ^ (crc >> 8);
            foreach (byte v in data) crc = _crc[(crc ^ v) & 0xFF] ^ (crc >> 8);
            var cb = new byte[4]; Be(cb, 0, crc ^ 0xFFFFFFFFu); s.Write(cb, 0, 4);
        }
    }
}
