// DNA-AND · render · recording clips as MP4 video (H.264), through Windows' own Media Foundation (part of Windows, as
// Direct3D is; no outside code): each frame of the game handed to its sink writer, timed as it was shown.
using System;
using System.Runtime.InteropServices;

namespace DnaAnd.Render
{
    internal sealed class Mp4Writer : IDisposable
    {
        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint flags);
        [DllImport("mfplat.dll")] private static extern int MFStartup(uint version, uint flags);
        [DllImport("mfplat.dll")] private static extern int MFShutdown();
        [DllImport("mfplat.dll")] private static extern int MFCreateMediaType(out IntPtr type);
        [DllImport("mfplat.dll")] private static extern int MFCreateMemoryBuffer(uint max, out IntPtr buffer);
        [DllImport("mfplat.dll")] private static extern int MFCreateSample(out IntPtr sample);
        [DllImport("mfplat.dll")] private static extern int MFCreateAttributes(out IntPtr attrs, uint size);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)] private static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attrs, out IntPtr writer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetU32Fn(IntPtr self, ref Guid key, uint v);          // IMFAttributes 21
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetU64Fn(IntPtr self, ref Guid key, ulong v);         // 22
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid v);     // 24
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddStreamFn(IntPtr self, IntPtr type, out uint index); // IMFSinkWriter 3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetInputFn(IntPtr self, uint index, IntPtr type, IntPtr parms); // 4
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgFn(IntPtr self);                                  // BeginWriting 5, Finalize 11
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int WriteSampleFn(IntPtr self, uint index, IntPtr sample); // 6
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int LockFn(IntPtr self, out IntPtr data, out uint max, out uint cur); // IMFMediaBuffer 3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetLengthFn(IntPtr self, uint len);                    // 6
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetTimeFn(IntPtr self, long t);                        // IMFSample 36, 38
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int AddBufferFn(IntPtr self, IntPtr buffer);               // 42

        private static Guid MajorType = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f"), Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        private static Guid SubType = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5"), H264 = new Guid("34363248-0000-0010-8000-00AA00389B71"), Rgb32 = new Guid("00000016-0000-0010-8000-00AA00389B71");
        private static Guid Bitrate = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e"), Interlace = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        private static Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d"), FrameRate = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        private static Guid Aspect = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), Stride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private static Guid HardwareOk = new Guid("a634a91c-822b-41b9-a494-4de4643612b0");

        private IntPtr _writer; private uint _stream; private readonly int _w, _h; private long _lastTicks = -1, _t0; private bool _started;
        public readonly string Path;

        /// <summary>Starts a clip at <paramref name="path"/>, <paramref name="w"/> by <paramref name="h"/> pixels (made even), about 60 frames a second.</summary>
        public Mp4Writer(string path, int w, int h)
        {
            Path = path; _w = w & ~1; _h = h & ~1;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            CoInitializeEx(IntPtr.Zero, 0);                                             // (already started on this thread: fine)
            Check(MFStartup(0x00020070, 0), "MFStartup"); _started = true;
            IntPtr attrs; Check(MFCreateAttributes(out attrs, 1), "MFCreateAttributes");
            try
            {
                Com.Method<SetU32Fn>(attrs, 21)(attrs, ref HardwareOk, 1);                     // the card's own encoder when there is one
                Check(MFCreateSinkWriterFromURL(System.IO.Path.GetFullPath(path), IntPtr.Zero, attrs, out _writer), "MFCreateSinkWriterFromURL");
            }
            finally { Com.Release(ref attrs); }
            ulong size = ((ulong)_w << 32) | (uint)_h, rate = (60UL << 32) | 1, square = (1UL << 32) | 1;
            IntPtr outType; Check(MFCreateMediaType(out outType), "MFCreateMediaType");
            try
            {
                Com.Method<SetGuidFn>(outType, 24)(outType, ref MajorType, ref Video);
                Com.Method<SetGuidFn>(outType, 24)(outType, ref SubType, ref H264);
                Com.Method<SetU32Fn>(outType, 21)(outType, ref Bitrate, (uint)Math.Min(40000000L, Math.Max(4000000L, (long)_w * _h * 6)));
                Com.Method<SetU32Fn>(outType, 21)(outType, ref Interlace, 2);           // progressive
                Com.Method<SetU64Fn>(outType, 22)(outType, ref FrameSize, size);
                Com.Method<SetU64Fn>(outType, 22)(outType, ref FrameRate, rate);
                Com.Method<SetU64Fn>(outType, 22)(outType, ref Aspect, square);
                Check(Com.Method<AddStreamFn>(_writer, 3)(_writer, outType, out _stream), "AddStream");
            }
            finally { Com.Release(ref outType); }
            IntPtr inType; Check(MFCreateMediaType(out inType), "MFCreateMediaType");
            try
            {
                Com.Method<SetGuidFn>(inType, 24)(inType, ref MajorType, ref Video);
                Com.Method<SetGuidFn>(inType, 24)(inType, ref SubType, ref Rgb32);
                Com.Method<SetU32Fn>(inType, 21)(inType, ref Interlace, 2);
                Com.Method<SetU64Fn>(inType, 22)(inType, ref FrameSize, size);
                Com.Method<SetU64Fn>(inType, 22)(inType, ref FrameRate, rate);
                Com.Method<SetU64Fn>(inType, 22)(inType, ref Aspect, square);
                Com.Method<SetU32Fn>(inType, 21)(inType, ref Stride, (uint)(_w * 4));   // rows top first
                Check(Com.Method<SetInputFn>(_writer, 4)(_writer, _stream, inType, IntPtr.Zero), "SetInputMediaType");
            }
            finally { Com.Release(ref inType); }
            Check(Com.Method<NoArgFn>(_writer, 5)(_writer), "BeginWriting");
        }

        /// <summary>Adds one frame (RGB, 3 bytes a pixel, top row first, <paramref name="srcW"/> wide), at the moment it was shown.</summary>
        public void Add(byte[] rgb, int srcW, int srcH)
        {
            if (_writer == IntPtr.Zero || rgb == null || rgb.Length < srcW * srcH * 3) return;
            long now = System.Diagnostics.Stopwatch.GetTimestamp(); if (_lastTicks < 0) _t0 = now;
            long t = (now - _t0) * 10000000L / System.Diagnostics.Stopwatch.Frequency;      // in hundreds of nanoseconds
            long prev = _lastTicks < 0 ? 0 : (_lastTicks - _t0) * 10000000L / System.Diagnostics.Stopwatch.Frequency;
            long dur = _lastTicks < 0 ? 166667 : Math.Max(1, t - prev); _lastTicks = now;
            int len = _w * _h * 4;
            IntPtr buf; Check(MFCreateMemoryBuffer((uint)len, out buf), "MFCreateMemoryBuffer");
            IntPtr sample = IntPtr.Zero;
            try
            {
                IntPtr data; uint max, cur; Check(Com.Method<LockFn>(buf, 3)(buf, out data, out max, out cur), "Lock");
                var row = new byte[_w * 4];
                for (int y = 0; y < _h; y++)
                {
                    int s = y * srcW * 3;
                    for (int x = 0; x < _w; x++) { row[x * 4] = rgb[s + 2]; row[x * 4 + 1] = rgb[s + 1]; row[x * 4 + 2] = rgb[s]; row[x * 4 + 3] = 255; s += 3; }   // to blue, green, red
                    Marshal.Copy(row, 0, data + y * _w * 4, row.Length);
                }
                Com.Method<NoArgFn>(buf, 4)(buf);                                         // Unlock
                Com.Method<SetLengthFn>(buf, 6)(buf, (uint)len);
                Check(MFCreateSample(out sample), "MFCreateSample");
                Com.Method<AddBufferFn>(sample, 42)(sample, buf);
                Com.Method<SetTimeFn>(sample, 36)(sample, t - dur < 0 ? 0 : t);
                Com.Method<SetTimeFn>(sample, 38)(sample, dur);
                Check(Com.Method<WriteSampleFn>(_writer, 6)(_writer, _stream, sample), "WriteSample");
            }
            finally { Com.Release(ref sample); Com.Release(ref buf); }
        }

        /// <summary>Finishes the file (it plays from here on) and lets go of the encoder.</summary>
        public void Dispose()
        {
            if (_writer != IntPtr.Zero) { try { Com.Method<NoArgFn>(_writer, 11)(_writer); } catch { } Com.Release(ref _writer); }
            if (_started) { MFShutdown(); _started = false; }
        }

        private static void Check(int hr, string what) { if (hr < 0) throw new InvalidOperationException(what + " failed: 0x" + hr.ToString("X8")); }
    }
}
