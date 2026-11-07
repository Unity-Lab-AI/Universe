// DNA-AND · core · vectors, matrices, seeded random numbers and noise. All our own.
using System;
using System.Runtime.InteropServices;

namespace DnaAnd.Core
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Vec3
    {
        public float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public Vec3 Normalized { get { float l = Length; return l > 1e-12f ? this * (1f / l) : this; } }
        public static float Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static Vec3 Cross(Vec3 a, Vec3 b) { return new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X); }
    }

    /// <summary>Row-major 4x4 matrix, row vectors (v * M), as uploaded to shaders.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Mat4
    {
        public float M11, M12, M13, M14, M21, M22, M23, M24, M31, M32, M33, M34, M41, M42, M43, M44;

        public static Mat4 Identity { get { var m = new Mat4(); m.M11 = m.M22 = m.M33 = m.M44 = 1; return m; } }

        public static Mat4 operator *(Mat4 a, Mat4 b)
        {
            var r = new Mat4();
            r.M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41;
            r.M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42;
            r.M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43;
            r.M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44;
            r.M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41;
            r.M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42;
            r.M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43;
            r.M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44;
            r.M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41;
            r.M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42;
            r.M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43;
            r.M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44;
            r.M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41;
            r.M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42;
            r.M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43;
            r.M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44;
            return r;
        }

        public static Mat4 LookAt(Vec3 eye, Vec3 target, Vec3 up)
        {
            Vec3 z = (target - eye).Normalized, x = Vec3.Cross(up, z).Normalized, y = Vec3.Cross(z, x);
            var m = Identity;
            m.M11 = x.X; m.M12 = y.X; m.M13 = z.X;
            m.M21 = x.Y; m.M22 = y.Y; m.M23 = z.Y;
            m.M31 = x.Z; m.M32 = y.Z; m.M33 = z.Z;
            m.M41 = -Vec3.Dot(x, eye); m.M42 = -Vec3.Dot(y, eye); m.M43 = -Vec3.Dot(z, eye);
            return m;
        }

        public static Mat4 RotationY(float a)
        {
            var m = Identity; float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            m.M11 = c; m.M13 = -s; m.M31 = s; m.M33 = c; return m;
        }

        public static Mat4 Translation(float x, float y, float z) { var m = Identity; m.M41 = x; m.M42 = y; m.M43 = z; return m; }

        /// <summary>Left-handed perspective, depth 0..1.</summary>
        public static Mat4 Perspective(float fovY, float aspect, float near, float far)
        {
            float h = 1f / (float)Math.Tan(fovY * 0.5f), w = h / aspect;
            var m = new Mat4();
            // reversed depth: the near plane lands at 1 and the far at 0, so a float depth buffer keeps fine precision at every
            // distance (from a micrometre in front of you to the far side of a galaxy)
            m.M11 = w; m.M22 = h; m.M33 = -near / (far - near); m.M34 = 1; m.M43 = near * far / (far - near);
            return m;
        }
    }

    /// <summary>Our own seeded random numbers (SplitMix64 seeding + xoshiro256**). Same seed, same world.</summary>
    internal sealed class Rng
    {
        private ulong _a, _b, _c, _d;
        /// <summary>The generator's whole state, for saving; <see cref="SetState"/> puts it back exactly.</summary>
        public ulong[] State { get { return new[] { _a, _b, _c, _d }; } }
        public void SetState(ulong[] s) { _a = s[0]; _b = s[1]; _c = s[2]; _d = s[3]; }
        public Rng(ulong seed)
        {
            ulong s = seed;
            _a = Mix(ref s); _b = Mix(ref s); _c = Mix(ref s); _d = Mix(ref s);
        }
        private static ulong Mix(ref ulong s)
        {
            ulong z = (s += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        private static ulong Rotl(ulong x, int k) { return (x << k) | (x >> (64 - k)); }
        public int Below(int n) { return (int)(Next() % (ulong)Math.Max(1, n)); }
        public ulong Next()
        {
            ulong result = Rotl(_b * 5, 7) * 9, t = _b << 17;
            _c ^= _a; _d ^= _b; _b ^= _c; _a ^= _d; _c ^= t; _d = Rotl(_d, 45);
            return result;
        }
        /// <summary>0 (inclusive) to 1 (exclusive).</summary>
        public double NextDouble() { return (Next() >> 11) * (1.0 / 9007199254740992.0); }
        public float Range(float lo, float hi) { return lo + (float)NextDouble() * (hi - lo); }
        public int Int(int n) { return (int)(NextDouble() * n); }
        /// <summary>Normally distributed (mean 0, spread 1).</summary>
        public float Gaussian()
        {
            double u1 = 1.0 - NextDouble(), u2 = NextDouble();
            return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
        }
    }

    /// <summary>Our own smooth 3D value noise with octaves.</summary>
    internal static class Noise
    {
        private static float Hash(int x, int y, int z, uint seed)
        {
            uint h = seed ^ (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu;
            h ^= h >> 13; h *= 0x5BD1E995u; h ^= h >> 15;
            return (h & 0xFFFFFF) / 16777215f;
        }
        private static float Smooth(float t) { return t * t * (3 - 2 * t); }
        public static float Value(float x, float y, float z, uint seed)
        {
            int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
            float fx = Smooth(x - ix), fy = Smooth(y - iy), fz = Smooth(z - iz);
            float c000 = Hash(ix, iy, iz, seed), c100 = Hash(ix + 1, iy, iz, seed), c010 = Hash(ix, iy + 1, iz, seed), c110 = Hash(ix + 1, iy + 1, iz, seed);
            float c001 = Hash(ix, iy, iz + 1, seed), c101 = Hash(ix + 1, iy, iz + 1, seed), c011 = Hash(ix, iy + 1, iz + 1, seed), c111 = Hash(ix + 1, iy + 1, iz + 1, seed);
            float x00 = c000 + (c100 - c000) * fx, x10 = c010 + (c110 - c010) * fx, x01 = c001 + (c101 - c001) * fx, x11 = c011 + (c111 - c011) * fx;
            float y0 = x00 + (x10 - x00) * fy, y1 = x01 + (x11 - x01) * fy;
            return y0 + (y1 - y0) * fz;
        }
        public static float Fractal(float x, float y, float z, uint seed, int octaves)
        {
            float sum = 0, amp = 0.5f, freq = 1, norm = 0;
            for (int i = 0; i < octaves; i++) { sum += Value(x * freq, y * freq, z * freq, seed + (uint)i) * amp; norm += amp; amp *= 0.5f; freq *= 2; }
            return sum / norm;
        }
    }

    /// <summary>A growable array of plain structs, for the big simulation lists.</summary>
    internal sealed class BigArray<T> where T : struct
    {
        private T[] _items;
        public int Count { get; private set; }
        public BigArray(int capacity) { _items = new T[Math.Max(16, capacity)]; }
        public T[] Raw { get { return _items; } }
        public void Add(T item)
        {
            if (Count == _items.Length) Array.Resize(ref _items, _items.Length * 2);
            _items[Count++] = item;
        }
        public void Clear() { Count = 0; }
        public T this[int i] { get { return _items[i]; } set { _items[i] = value; } }
    }
}
