// Universe · render · the .cdf97w made where the frame is drawn: in the frame's own command list on the graphics card, the finished
// picture is copied into a buffer beside it, split into its Y, Cb and Cr planes (sized for the clip and reflect-padded to 64), each
// plane put through the CDF 9/7 (the same lifting as src/core/Wavelet97.cs), and every term quantised by its plane's step weighted
// for its level. Only those whole-number terms come back, a frame later, when the card has finished that frame anyway: the picture
// itself is never copied back to the processor and the game never waits for the clip. The processor's part is the packing alone.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Render
{
    internal sealed class CardClip : IDisposable
    {
        [DllImport("d3d12.dll")] private static extern int D3D12SerializeRootSignature(IntPtr desc, int version, out IntPtr blob, out IntPtr errors);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] src, UIntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
        [StructLayout(LayoutKind.Sequential)] private struct HeapProps { public int Type, CpuPage, Pool; public uint CreateMask, VisibleMask; }
        [StructLayout(LayoutKind.Explicit, Size = 56)]
        private struct ResDesc
        {
            [FieldOffset(0)] public int Dimension; [FieldOffset(8)] public ulong Alignment; [FieldOffset(16)] public ulong Width;
            [FieldOffset(24)] public uint Height; [FieldOffset(28)] public ushort Depth; [FieldOffset(30)] public ushort Mips;
            [FieldOffset(32)] public uint Format; [FieldOffset(36)] public uint SampleCount; [FieldOffset(40)] public uint SampleQuality;
            [FieldOffset(44)] public int Layout; [FieldOffset(48)] public int Flags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCommittedResourceFn(IntPtr self, ref HeapProps heap, int heapFlags, ref ResDesc desc, int state, IntPtr clear, ref Guid iid, out IntPtr res); // device 27
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateRootSignatureFn(IntPtr self, uint nodeMask, IntPtr blob, UIntPtr size, ref Guid iid, out IntPtr rs); // device 16
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateComputePipelineStateFn(IntPtr self, IntPtr desc, ref Guid iid, out IntPtr pso); // device 11
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapFn(IntPtr self, uint sub, IntPtr range, out IntPtr data); // resource 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapFn(IntPtr self, uint sub, IntPtr range); // resource 9
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate ulong GpuAddressFn(IntPtr self); // resource 11
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchFn(IntPtr self, uint x, uint y, uint z); // list 14
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyBufferRegionFn(IntPtr self, IntPtr dst, ulong dstOff, IntPtr src, ulong srcOff, ulong bytes); // list 15
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetPsoFn(IntPtr self, IntPtr pso); // list 25
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootSigFn(IntPtr self, IntPtr rs); // list 29
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetConstantsFn(IntPtr self, uint index, uint count, uint[] data, uint offset); // list 35
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootUavFn(IntPtr self, uint index, ulong address); // list 41
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrFn(IntPtr self); // 3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSizeFn(IntPtr self); // 4

        // constants: c0..c3 the pass (n, lines, along, across), c4 the plane's start, c5 its step (as bits), c6 w, c7 h, c8 pw, c9 ph,
        // c10 the source's width, c11 its height, c12 its row in words, c13 the size down
        private const int NC = 14;
        private const string Hlsl = @"
cbuffer C : register(b0) { uint n; uint lines; uint along; uint across; uint base0; uint stepBits; uint w; uint h; uint pw; uint ph; uint sw; uint sh; uint srow; uint down; };
RWStructuredBuffer<float> a : register(u0);
RWStructuredBuffer<float> t : register(u1);
RWStructuredBuffer<uint> x : register(u2);
RWStructuredBuffer<float> lw : register(u3);
static const float A = -1.586134342059924, B = -0.052980118572961, G = 0.882911075530934, D = 0.443506852043971, K = 1.230174104914001;
[numthreads(8, 8, 1)]
void PREP(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= pw || id.y >= ph) return;
    int rx = id.x < w ? (int)id.x : max(0, 2 * ((int)w - 1) - (int)id.x), ry = id.y < h ? (int)id.y : max(0, 2 * ((int)h - 1) - (int)id.y);
    uint sx = min(sw - 1, (uint)rx * down), sy = min(sh - 1, (uint)ry * down);
    uint p = x[sy * srow + sx];
    float r = (p & 255) / 255.0, g = ((p >> 8) & 255) / 255.0, b = ((p >> 16) & 255) / 255.0;
    uint i = id.y * pw + id.x, N = pw * ph;
    a[i] = 0.299 * r + 0.587 * g + 0.114 * b;
    a[N + i] = 0.5 - 0.168736 * r - 0.331264 * g + 0.5 * b;
    a[2 * N + i] = 0.5 + 0.5 * r - 0.418688 * g - 0.081312 * b;
}
[numthreads(64, 1, 1)]
void FWD(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= lines) return;
    uint b = base0 + id.x * across;
    uint size = n;
    [loop] while (size >= 4 && (size & 1) == 0)
    {
        uint i;
        [loop] for (i = 1; i < size - 1; i += 2) a[b + i * along] += A * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        a[b + (size - 1) * along] += 2 * A * a[b + (size - 2) * along];
        a[b] += 2 * B * a[b + along];
        [loop] for (i = 2; i < size; i += 2) a[b + i * along] += B * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        [loop] for (i = 1; i < size - 1; i += 2) a[b + i * along] += G * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        a[b + (size - 1) * along] += 2 * G * a[b + (size - 2) * along];
        a[b] += 2 * D * a[b + along];
        [loop] for (i = 2; i < size; i += 2) a[b + i * along] += D * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        [loop] for (i = 0; i < size; i += 2) a[b + i * along] /= K;
        [loop] for (i = 1; i < size; i += 2) a[b + i * along] *= K;
        uint hs = size / 2;
        [loop] for (i = 0; i < hs; i++) { t[b + i * along] = a[b + 2 * i * along]; t[b + (hs + i) * along] = a[b + (2 * i + 1) * along]; }
        [loop] for (i = 0; i < size; i++) a[b + i * along] = t[b + i * along];
        size = hs;
    }
}
[numthreads(256, 1, 1)]
void QUANT(uint3 id : SV_DispatchThreadID)
{
    uint N = pw * ph; if (id.x >= N) return;
    float q = a[base0 + id.x] / (asfloat(stepBits) * lw[id.x]);
    x[base0 + id.x] = (uint)(int)clamp(trunc(q), -32767.0, 32767.0);
}
";
        private readonly IntPtr _device; private IntPtr _rs, _prep, _fwd, _quant, _src, _a, _t, _q, _lw, _lwUp;
        private readonly IntPtr[] _down = new IntPtr[2]; private readonly long[] _downMs = { -1, -1 }; private int _slot;
        private readonly int _w, _h, _pw, _ph, _down0; private readonly float[] _step; private int _srcW, _srcH; private uint _srow;
        public readonly int Width, Height;

        public CardClip(IntPtr device, int clipW, int clipH, int down, float[] step)
        {
            _device = device; _w = clipW; _h = clipH; _pw = Universe.Core.Wavelet97.PadSide(clipW); _ph = Universe.Core.Wavelet97.PadSide(clipH); _down0 = down; _step = step;
            Width = clipW; Height = clipH;
            IntPtr parms = Marshal.AllocHGlobal(32 * 5), desc = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 32 * 5; i++) Marshal.WriteByte(parms, i, 0);
                Marshal.WriteInt32(parms, 0, 1); Marshal.WriteInt32(parms, 16, NC);                  // constants b0
                for (int u = 0; u < 4; u++) { Marshal.WriteInt32(parms, 32 * (u + 1), 4); Marshal.WriteInt32(parms, 32 * (u + 1) + 8, u); }   // UAVs u0..u3
                for (int i = 0; i < 40; i++) Marshal.WriteByte(desc, i, 0);
                Marshal.WriteInt32(desc, 0, 5); Marshal.WriteIntPtr(desc, 8, parms);
                IntPtr blob, err;
                if (D3D12SerializeRootSignature(desc, 1, out blob, out err) < 0) throw new InvalidOperationException("clip root signature: " + Text(err));
                Guid iid = D3D12.IID_ID3D12RootSignature;
                Com.Check(Com.Method<CreateRootSignatureFn>(device, 16)(device, 0, BlobPtr(blob), BlobSize(blob), ref iid, out _rs), "CreateRootSignature(clip)");
                Com.Release(ref blob);
            }
            finally { Marshal.FreeHGlobal(parms); Marshal.FreeHGlobal(desc); }
            _prep = Pso("PREP"); _fwd = Pso("FWD"); _quant = Pso("QUANT");
            long N = (long)_pw * _ph;
            _a = Buffer(1, N * 3 * 4, 4, 0x8); _t = Buffer(1, N * 3 * 4, 4, 0x8); _q = Buffer(1, N * 3 * 4, 4, 0x8);
            _lw = Buffer(1, N * 4, 4, 0x400); _lwUp = Buffer(2, N * 4, 0, 0xAC3);
            for (int i = 0; i < 2; i++) _down[i] = Buffer(3, N * 3 * 4, 0, 0x400);
            var lw = Cdf97wCodec.LevelWeights(_pw, _ph); IntPtr p;
            Com.Check(Com.Method<MapFn>(_lwUp, 8)(_lwUp, 0, IntPtr.Zero, out p), "Map(weights)"); Marshal.Copy(lw, 0, p, lw.Length); Com.Method<UnmapFn>(_lwUp, 9)(_lwUp, 0, IntPtr.Zero);
            _lwFresh = true;
        }
        private bool _lwFresh;

        private IntPtr Pso(string entry)
        {
            byte[] src = Encoding.ASCII.GetBytes(Hlsl); IntPtr code, err;
            if (D3DCompile(src, (UIntPtr)src.Length, "clip", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 1u << 15, 0, out code, out err) < 0) throw new InvalidOperationException("clip shader " + entry + ": " + Text(err));
            Com.Release(ref err);
            IntPtr d = Marshal.AllocHGlobal(56), pso;
            try
            {
                for (int i = 0; i < 56; i++) Marshal.WriteByte(d, i, 0);
                Marshal.WriteIntPtr(d, 0, _rs); Marshal.WriteIntPtr(d, 8, BlobPtr(code)); Marshal.WriteInt64(d, 16, (long)BlobSize(code).ToUInt64());
                Guid iid = D3D12.IID_ID3D12PipelineState;
                Com.Check(Com.Method<CreateComputePipelineStateFn>(_device, 11)(_device, d, ref iid, out pso), "CreateComputePipelineState(" + entry + ")");
            }
            finally { Marshal.FreeHGlobal(d); Com.Release(ref code); }
            return pso;
        }
        private IntPtr Buffer(int heapType, long bytes, int flags, int state)
        {
            var heap = new HeapProps { Type = heapType, CreateMask = 1, VisibleMask = 1 };
            var desc = new ResDesc { Dimension = 1, Width = (ulong)Math.Max(256, bytes), Height = 1, Depth = 1, Mips = 1, SampleCount = 1, Layout = 1, Flags = flags };
            Guid iid = D3D12.IID_ID3D12Resource; IntPtr r;
            Com.Check(Com.Method<CreateCommittedResourceFn>(_device, 27)(_device, ref heap, 0, ref desc, state, IntPtr.Zero, ref iid, out r), "CreateCommittedResource(clip)");
            return r;
        }
        private static D3D12.TransitionBarrier Turn(IntPtr r, int from, int to) { return new D3D12.TransitionBarrier { Type = 0, Resource = r, Subresource = 0xFFFFFFFF, StateBefore = from, StateAfter = to }; }

        /// <summary>Records this frame's equations into the frame's own command list, from the back buffer (in the render-target state,
        /// and left in it). <paramref name="ms"/> is the frame's time in the clip; the terms come back from <see cref="Collect"/> once
        /// the card has finished the frame.</summary>
        public bool Record(IntPtr list, IntPtr backBuffer, int srcW, int srcH, long ms)
        {
            if (srcW / _down0 < _w || srcH / _down0 < _h) return false;                      // (the window was made smaller mid-clip: frames at the old size only)
            if (_downMs[_slot] >= 0) return false;                                           // (that slot not yet collected)
            uint srow = (uint)((srcW * 4 + 255) & ~255) / 4;
            if (_src == IntPtr.Zero || srcW != _srcW || srcH != _srcH) { Com.Release(ref _src); _src = Buffer(1, (long)srow * 4 * srcH, 4, 0x400); _srcW = srcW; _srcH = srcH; _srow = srow; }
            var barrier = Com.Method<D3D12.ResourceBarrierFn>(list, 26);
            var b = Turn(backBuffer, D3D12.RESOURCE_STATE_RENDER_TARGET, D3D12.RESOURCE_STATE_COPY_SOURCE); barrier(list, 1, ref b);
            var src = new D3D12.TextureCopyLocation { Resource = backBuffer, Type = 0, SubresourceIndex = 0 };
            var dst = new D3D12.TextureCopyLocation { Resource = _src, Type = 1, Offset = 0, Format = D3D12.DXGI_FORMAT_R8G8B8A8_UNORM, Width = (uint)srcW, Height = (uint)srcH, Depth = 1, RowPitch = _srow * 4 };
            Com.Method<D3D12.CopyTextureRegionFn>(list, 16)(list, ref dst, 0, 0, 0, ref src, IntPtr.Zero);
            b = Turn(backBuffer, D3D12.RESOURCE_STATE_COPY_SOURCE, D3D12.RESOURCE_STATE_RENDER_TARGET); barrier(list, 1, ref b);
            b = Turn(_src, 0x400, 0x8); barrier(list, 1, ref b);
            if (_lwFresh) { Com.Method<CopyBufferRegionFn>(list, 15)(list, _lw, 0, _lwUp, 0, (ulong)_pw * (ulong)_ph * 4); _lwFresh = false; }
            b = Turn(_lw, 0x400, 0x8); barrier(list, 1, ref b);

            var setC = Com.Method<SetConstantsFn>(list, 35); var setUav = Com.Method<SetRootUavFn>(list, 41); var dispatch = Com.Method<DispatchFn>(list, 14); var pso = Com.Method<SetPsoFn>(list, 25);
            Com.Method<SetRootSigFn>(list, 29)(list, _rs);
            setUav(list, 1, Addr(_a)); setUav(list, 2, Addr(_t)); setUav(list, 3, Addr(_src)); setUav(list, 4, Addr(_lw));
            var c = new uint[NC]; c[6] = (uint)_w; c[7] = (uint)_h; c[8] = (uint)_pw; c[9] = (uint)_ph; c[10] = (uint)srcW; c[11] = (uint)srcH; c[12] = _srow; c[13] = (uint)_down0;
            pso(list, _prep); setC(list, 0, NC, c, 0); dispatch(list, (uint)(_pw + 7) / 8, (uint)(_ph + 7) / 8, 1); Uav(barrier, list, _a);
            pso(list, _fwd);
            uint N = (uint)(_pw * _ph);
            for (int pl = 0; pl < 3; pl++)
            {
                c[4] = N * (uint)pl;
                c[0] = (uint)_pw; c[1] = (uint)_ph; c[2] = 1; c[3] = (uint)_pw; setC(list, 0, NC, c, 0); dispatch(list, (uint)(_ph + 63) / 64, 1, 1); Uav(barrier, list, _a);   // rows
                c[0] = (uint)_ph; c[1] = (uint)_pw; c[2] = (uint)_pw; c[3] = 1; setC(list, 0, NC, c, 0); dispatch(list, (uint)(_pw + 63) / 64, 1, 1); Uav(barrier, list, _a);   // columns
            }
            pso(list, _quant); setUav(list, 3, Addr(_q));
            for (int pl = 0; pl < 3; pl++) { c[4] = N * (uint)pl; c[5] = BitConverter.ToUInt32(BitConverter.GetBytes(_step[pl]), 0); setC(list, 0, NC, c, 0); dispatch(list, (N + 255) / 256, 1, 1); }
            b = Turn(_q, 0x8, 0x800); barrier(list, 1, ref b);
            Com.Method<CopyBufferRegionFn>(list, 15)(list, _down[_slot], 0, _q, 0, (ulong)N * 3 * 4);
            b = Turn(_q, 0x800, 0x8); barrier(list, 1, ref b);
            b = Turn(_src, 0x8, 0x400); barrier(list, 1, ref b);
            b = Turn(_lw, 0x8, 0x400); barrier(list, 1, ref b);
            _downMs[_slot] = ms; _slot ^= 1;
            return true;
        }
        private static void Uav(D3D12.ResourceBarrierFn barrier, IntPtr list, IntPtr r) { var u = new D3D12.TransitionBarrier { Type = 2, Resource = r }; barrier(list, 1, ref u); }
        private static ulong Addr(IntPtr r) { return Com.Method<GpuAddressFn>(r, 11)(r); }

        /// <summary>The terms of every frame the card has finished (call after its fence: the frame before the one being recorded),
        /// handed on with their times.</summary>
        public void Collect(Action<short[][], long> take)
        {
            for (int k = 0; k < 2; k++)
            {
                int s = _slot ^ 1 ^ k;                                                       // (the older slot first)
                if (_downMs[s] < 0) continue;
                int N = _pw * _ph; var q = new short[3][]; IntPtr p;
                Com.Check(Com.Method<MapFn>(_down[s], 8)(_down[s], 0, IntPtr.Zero, out p), "Map(clip terms)");
                try
                {
                    var raw = new int[N * 3]; Marshal.Copy(p, raw, 0, raw.Length);
                    for (int pl = 0; pl < 3; pl++) { var a = q[pl] = new short[N]; int o = pl * N; for (int i = 0; i < N; i++) a[i] = (short)raw[o + i]; }
                }
                finally { Com.Method<UnmapFn>(_down[s], 9)(_down[s], 0, IntPtr.Zero); }
                long ms = _downMs[s]; _downMs[s] = -1; take(q, ms);
            }
        }

        private static IntPtr BlobPtr(IntPtr b) { return Com.Method<BlobPtrFn>(b, 3)(b); }
        private static UIntPtr BlobSize(IntPtr b) { return Com.Method<BlobSizeFn>(b, 4)(b); }
        private static string Text(IntPtr b) { if (b == IntPtr.Zero) return "(no details)"; string s = Marshal.PtrToStringAnsi(BlobPtr(b), (int)BlobSize(b).ToUInt64()); Com.Release(ref b); return s; }

        public void Dispose()
        {
            Com.Release(ref _src); Com.Release(ref _a); Com.Release(ref _t); Com.Release(ref _q); Com.Release(ref _lw); Com.Release(ref _lwUp);
            for (int i = 0; i < 2; i++) Com.Release(ref _down[i]);
            Com.Release(ref _prep); Com.Release(ref _fwd); Com.Release(ref _quant); Com.Release(ref _rs);
        }
    }
}
