// DNA-AND · installer · the programs' icons, drawn by our own code: Universe a spiral galaxy, DNA-AND a double helix. Writes
// installer\universe.ico and installer\dna-and.ico (each 16, 24, 32, 48, 64 and 256 pixels, as PNGs inside the icon), which
// the builds put in the programs (their windows, the taskbar, the Start menu and desktop shortcuts, the installers).
// Build and run (only what comes with Windows): csc /out:MakeIcons.exe MakeIcons.cs && MakeIcons.exe <folder>
using System;
using System.IO;
using System.IO.Compression;

internal static class MakeIcons
{
    private static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Write(Path.Combine(dir, "universe.ico"), Galaxy);
        Write(Path.Combine(dir, "dna-and.ico"), Helix);
        Console.WriteLine("Wrote universe.ico and dna-and.ico");
        return 0;
    }

    private delegate void Painter(double x, double y, out double r, out double g, out double b, out double a);

    // a tile of the dark sky, round, with a soft rim: both icons sit on it
    private static double Disc(double x, double y, out double rim)
    {
        double d = Math.Sqrt(x * x + y * y); rim = Math.Max(0, 1 - Math.Abs(d - 0.93) / 0.05);
        return Math.Max(0, Math.Min(1, (0.98 - d) / 0.03));
    }

    // Universe: a two-armed spiral galaxy, a bright yellow-white core and blue arms, with a few stars, in the dark
    private static void Galaxy(double x, double y, out double r, out double g, out double b, out double a)
    {
        double rim; a = Disc(x, y, out rim);
        r = 0.03 + 0.25 * rim; g = 0.03 + 0.2 * rim; b = 0.09 + 0.45 * rim;
        double tx = x * 0.97 - y * 0.26, ty = (x * 0.26 + y * 0.97) * 1.35;              // tilted a little, seen at a slant
        double d = Math.Sqrt(tx * tx + ty * ty), th = Math.Atan2(ty, tx);
        double arm = 0;
        for (int k = 0; k < 2; k++)
        {
            double want = Math.Log(Math.Max(1e-3, d) / 0.08) / 0.32 + k * Math.PI;           // a logarithmic spiral
            double off = Math.IEEERemainder(th - want, 2 * Math.PI);
            arm = Math.Max(arm, Math.Exp(-off * off / 0.22) * Math.Exp(-d / 0.55) * (d > 0.06 ? 1 : d / 0.06));
        }
        double core = Math.Exp(-d * d / 0.012), halo = Math.Exp(-d / 0.28) * 0.35;
        r += arm * 0.45 + core * 1.0 + halo * 0.5; g += arm * 0.62 + core * 0.95 + halo * 0.45; b += arm * 1.0 + core * 0.8 + halo * 0.55;
        double sx = Math.Floor((x + 1) * 9), sy = Math.Floor((y + 1) * 9), h = Math.Abs(Math.Sin(sx * 12.9898 + sy * 78.233) * 43758.5453) % 1;
        if (h > 0.93) { double fx = (x + 1) * 9 - sx - 0.5, fy = (y + 1) * 9 - sy - 0.5, st = Math.Exp(-(fx * fx + fy * fy) / 0.02); r += st; g += st; b += st; }
    }

    // DNA-AND: a double helix, its two strands (teal and green) crossing, joined by base pairs, on the dark sky
    private static void Helix(double x, double y, out double r, out double g, out double b, out double a)
    {
        double rim; a = Disc(x, y, out rim);
        r = 0.02 + 0.1 * rim; g = 0.05 + 0.4 * rim; b = 0.06 + 0.35 * rim;
        double t = y * 2.6 * Math.PI / 2;                                                    // along the helix, top to bottom
        double s1 = Math.Sin(t) * 0.42, s2 = -s1, z1 = Math.Cos(t), z2 = -z1, w = 0.085;
        if (Math.Abs(y) < 0.82)
        {
            // the base pairs: rungs between the strands, every so often
            double rung = Math.Abs(Math.IEEERemainder(y, 0.16)) < 0.025 && Math.Abs(x) < Math.Abs(s1) ? 1 : 0;
            r += rung * 0.6; g += rung * 0.55; b += rung * 0.35;
            // the strands, the nearer one drawn over
            for (int k = 0; k < 2; k++)
            {
                bool first = (z1 < z2) == (k == 0);
                double sx = first ? s1 : s2, z = first ? z1 : z2, d = Math.Abs(x - sx), on = Math.Max(0, Math.Min(1, (w - d) / 0.02));
                if (on <= 0) continue;
                double lit = 0.55 + 0.45 * z, shade = 1 - (d / w) * (d / w) * 0.5;
                double cr = first ? 0.25 : 0.35, cg = first ? 0.85 : 1.0, cb = first ? 0.95 : 0.45;
                r = r * (1 - on) + cr * lit * shade * on; g = g * (1 - on) + cg * lit * shade * on; b = b * (1 - on) + cb * lit * shade * on;
            }
        }
    }

    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 256 };

    private static void Write(string path, Painter paint)
    {
        var images = new byte[Sizes.Length][];
        for (int i = 0; i < Sizes.Length; i++) images[i] = Png(Sizes[i], paint);
        using (var f = new BinaryWriter(File.Create(path)))
        {
            f.Write((short)0); f.Write((short)1); f.Write((short)Sizes.Length);
            int offset = 6 + 16 * Sizes.Length;
            for (int i = 0; i < Sizes.Length; i++)
            {
                int n = Sizes[i];
                f.Write((byte)(n >= 256 ? 0 : n)); f.Write((byte)(n >= 256 ? 0 : n)); f.Write((byte)0); f.Write((byte)0);
                f.Write((short)1); f.Write((short)32); f.Write(images[i].Length); f.Write(offset); offset += images[i].Length;
            }
            foreach (var im in images) f.Write(im);
        }
    }

    // one size, drawn with four samples a pixel, as a PNG (RGBA, its rows deflated)
    private static byte[] Png(int n, Painter paint)
    {
        var raw = new byte[n * (n * 4 + 1)];
        for (int py = 0; py < n; py++)
        {
            raw[py * (n * 4 + 1)] = 0;
            for (int px = 0; px < n; px++)
            {
                double R = 0, G = 0, B = 0, A = 0;
                for (int sy = 0; sy < 2; sy++)
                    for (int sx = 0; sx < 2; sx++)
                    {
                        double x = ((px + 0.25 + sx * 0.5) / n) * 2 - 1, y = ((py + 0.25 + sy * 0.5) / n) * 2 - 1, r, g, b, a;
                        paint(x, y, out r, out g, out b, out a);
                        R += Math.Min(1, r) * a; G += Math.Min(1, g) * a; B += Math.Min(1, b) * a; A += a;
                    }
                int o = py * (n * 4 + 1) + 1 + px * 4;
                double al = A / 4;
                raw[o] = (byte)(al > 0 ? Math.Min(255, R / A * 255) : 0); raw[o + 1] = (byte)(al > 0 ? Math.Min(255, G / A * 255) : 0); raw[o + 2] = (byte)(al > 0 ? Math.Min(255, B / A * 255) : 0); raw[o + 3] = (byte)(al * 255);
            }
        }
        var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var hdr = new byte[13]; Big(hdr, 0, n); Big(hdr, 4, n); hdr[8] = 8; hdr[9] = 6; Chunk(ms, "IHDR", hdr);
        var z = new MemoryStream(); z.WriteByte(0x78); z.WriteByte(0x01);
        using (var d = new DeflateStream(z, CompressionLevel.Optimal, true)) d.Write(raw, 0, raw.Length);
        uint s1 = 1, s2 = 0; foreach (byte v in raw) { s1 = (s1 + v) % 65521; s2 = (s2 + s1) % 65521; }
        var ad = new byte[4]; Big(ad, 0, (int)((s2 << 16) | s1)); z.Write(ad, 0, 4);
        Chunk(ms, "IDAT", z.ToArray()); Chunk(ms, "IEND", new byte[0]);
        return ms.ToArray();
    }
    private static void Big(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    private static uint[] _crc;
    private static void Chunk(Stream s, string type, byte[] data)
    {
        if (_crc == null) { _crc = new uint[256]; for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; _crc[i] = c; } }
        var len = new byte[4]; Big(len, 0, data.Length); s.Write(len, 0, 4);
        var td = new byte[4 + data.Length]; for (int i = 0; i < 4; i++) td[i] = (byte)type[i]; Array.Copy(data, 0, td, 4, data.Length);
        s.Write(td, 0, td.Length);
        uint crc = 0xFFFFFFFF; foreach (byte v in td) crc = _crc[(crc ^ v) & 0xFF] ^ (crc >> 8); crc ^= 0xFFFFFFFF;
        var cb = new byte[4]; Big(cb, 0, (int)crc); s.Write(cb, 0, 4);
    }
}
