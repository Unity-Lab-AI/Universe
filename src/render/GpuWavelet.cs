// Universe · render · the CDF 9/7 wavelet on the graphics card (the lab's GPU-first rule): the same lifting as src/core/Wavelet97.cs,
// one line of a plane to a thread, rows then columns forward, columns then rows back, in a compute shader on a queue of its own
// (the game's drawing never waits on it). Used by the .cdf97w writer for the forward transform of every frame and, for the front
// man, the inverse. If the card refuses any of it, the writer goes on with the processor's transform.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Render
{
    internal sealed class GpuWavelet : IDisposable
    {
        [DllImport("d3d12.dll")] private static extern int D3D12SerializeRootSignature(IntPtr desc, int version, out IntPtr blob, out IntPtr errors);
        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] src, UIntPtr size, string name, IntPtr defines, IntPtr include, string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
        [DllImport("kernel32.dll")] private static extern IntPtr CreateEvent(IntPtr attrs, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);

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
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void Copy(IntPtr dst, float[] src, UIntPtr n);
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void Copy(float[] dst, IntPtr src, UIntPtr n);

        private const string Hlsl = @"
cbuffer C : register(b0) { uint n; uint lines; uint along; uint across; };
RWStructuredBuffer<float> a : register(u0);
RWStructuredBuffer<float> t : register(u1);
static const float A = -1.586134342059924, B = -0.052980118572961, G = 0.882911075530934, D = 0.443506852043971, K = 1.230174104914001;
[numthreads(64, 1, 1)]
void FWD(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= lines) return;
    uint b = id.x * across;
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
[numthreads(64, 1, 1)]
void INV(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= lines) return;
    uint b = id.x * across;
    uint smallest = n;
    [loop] while (smallest >= 4 && (smallest & 1) == 0) smallest /= 2;
    uint size = smallest * 2;
    [loop] while (size <= n && size >= 4)
    {
        uint i, hs = size / 2;
        [loop] for (i = 0; i < hs; i++) { t[b + 2 * i * along] = a[b + i * along]; t[b + (2 * i + 1) * along] = a[b + (hs + i) * along]; }
        [loop] for (i = 0; i < size; i++) a[b + i * along] = t[b + i * along];
        [loop] for (i = 0; i < size; i += 2) a[b + i * along] *= K;
        [loop] for (i = 1; i < size; i += 2) a[b + i * along] /= K;
        a[b] -= 2 * D * a[b + along];
        [loop] for (i = 2; i < size; i += 2) a[b + i * along] -= D * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        [loop] for (i = 1; i < size - 1; i += 2) a[b + i * along] -= G * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        a[b + (size - 1) * along] -= 2 * G * a[b + (size - 2) * along];
        a[b] -= 2 * B * a[b + along];
        [loop] for (i = 2; i < size; i += 2) a[b + i * along] -= B * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        [loop] for (i = 1; i < size - 1; i += 2) a[b + i * along] -= A * (a[b + (i - 1) * along] + a[b + (i + 1) * along]);
        a[b + (size - 1) * along] -= 2 * A * a[b + (size - 2) * along];
        size *= 2;
    }
}
";
        private IntPtr _device, _queue, _alloc, _list, _fence, _event, _rs, _fwd, _inv, _a, _t, _up, _down;
        private ulong _fenceValue; private long _bytes;

        public GpuWavelet(IntPtr device)
        {
            _device = device;
            var qd = new D3D12.CommandQueueDesc { Type = 2 };                          // a compute queue of its own
            Guid iid = D3D12.IID_ID3D12CommandQueue;
            Com.Check(Com.Method<D3D12.CreateCommandQueueFn>(device, 8)(device, ref qd, ref iid, out _queue), "CreateCommandQueue(compute)");
            iid = D3D12.IID_ID3D12CommandAllocator;
            Com.Check(Com.Method<D3D12.CreateCommandAllocatorFn>(device, 9)(device, 2, ref iid, out _alloc), "CreateCommandAllocator(compute)");
            iid = D3D12.IID_ID3D12GraphicsCommandList;
            Com.Check(Com.Method<D3D12.CreateCommandListFn>(device, 12)(device, 0, 2, _alloc, IntPtr.Zero, ref iid, out _list), "CreateCommandList(compute)");
            Com.Method<D3D12.CloseFn>(_list, 9)(_list);
            iid = D3D12.IID_ID3D12Fence;
            Com.Check(Com.Method<D3D12.CreateFenceFn>(device, 36)(device, 0, 0, ref iid, out _fence), "CreateFence(compute)");
            _event = CreateEvent(IntPtr.Zero, false, false, null);

            // root signature: [0] four constants b0, [1] UAV u0 (the plane), [2] UAV u1 (a scratch plane)
            IntPtr parms = Marshal.AllocHGlobal(96), desc = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 96; i++) Marshal.WriteByte(parms, i, 0);
                Marshal.WriteInt32(parms, 0, 1); Marshal.WriteInt32(parms, 8, 0); Marshal.WriteInt32(parms, 12, 0); Marshal.WriteInt32(parms, 16, 4);
                Marshal.WriteInt32(parms, 32, 4); Marshal.WriteInt32(parms, 40, 0);
                Marshal.WriteInt32(parms, 64, 4); Marshal.WriteInt32(parms, 72, 1);
                for (int i = 0; i < 40; i++) Marshal.WriteByte(desc, i, 0);
                Marshal.WriteInt32(desc, 0, 3); Marshal.WriteIntPtr(desc, 8, parms);
                IntPtr blob, err;
                if (D3D12SerializeRootSignature(desc, 1, out blob, out err) < 0) throw new InvalidOperationException("compute root signature: " + Text(err));
                iid = D3D12.IID_ID3D12RootSignature;
                Com.Check(Com.Method<CreateRootSignatureFn>(device, 16)(device, 0, BlobPtr(blob), BlobSize(blob), ref iid, out _rs), "CreateRootSignature(compute)");
                Com.Release(ref blob);
            }
            finally { Marshal.FreeHGlobal(parms); Marshal.FreeHGlobal(desc); }
            _fwd = Pso("FWD"); _inv = Pso("INV");
        }

        private IntPtr Pso(string entry)
        {
            byte[] src = Encoding.ASCII.GetBytes(Hlsl); IntPtr code, err;
            if (D3DCompile(src, (UIntPtr)src.Length, "wavelet", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 1u << 15, 0, out code, out err) < 0) throw new InvalidOperationException("wavelet shader " + entry + ": " + Text(err));
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
            var desc = new ResDesc { Dimension = 1, Width = (ulong)bytes, Height = 1, Depth = 1, Mips = 1, SampleCount = 1, Layout = 1, Flags = flags };
            Guid iid = D3D12.IID_ID3D12Resource; IntPtr r;
            Com.Check(Com.Method<CreateCommittedResourceFn>(_device, 27)(_device, ref heap, 0, ref desc, state, IntPtr.Zero, ref iid, out r), "CreateCommittedResource(wavelet)");
            return r;
        }
        private void Size(long bytes)
        {
            if (bytes <= _bytes) return;
            Com.Release(ref _a); Com.Release(ref _t); Com.Release(ref _up); Com.Release(ref _down);
            _a = Buffer(1, bytes, 4, 0x400); _t = Buffer(1, bytes, 4, 0x8);           // on the card: the plane (starts as a copy's target) and its scratch (unordered access)
            _up = Buffer(2, bytes, 0, 0xAC3); _down = Buffer(3, bytes, 0, 0x400);       // to the card, and back
            _bytes = bytes;
        }

        private static D3D12.TransitionBarrier Turn(IntPtr r, int from, int to) { return new D3D12.TransitionBarrier { Type = 0, Resource = r, Subresource = 0xFFFFFFFF, StateBefore = from, StateAfter = to }; }

        /// <summary>The forward (or inverse) 2D transform of a <paramref name="w"/> × <paramref name="h"/> plane, in place, on the card.</summary>
        public void Run(float[] plane, int w, int h, bool forward)
        {
            long bytes = (long)w * h * 4; Size(bytes);
            IntPtr p; Com.Check(Com.Method<MapFn>(_up, 8)(_up, 0, IntPtr.Zero, out p), "Map(up)"); Copy(p, plane, (UIntPtr)bytes); Com.Method<UnmapFn>(_up, 9)(_up, 0, IntPtr.Zero);
            Com.Check(Com.Method<D3D12.AllocatorResetFn>(_alloc, 8)(_alloc), "Reset(allocator)");
            Com.Check(Com.Method<D3D12.ListResetFn>(_list, 10)(_list, _alloc, IntPtr.Zero), "Reset(list)");
            var barrier = Com.Method<D3D12.ResourceBarrierFn>(_list, 26);
            Com.Method<CopyBufferRegionFn>(_list, 15)(_list, _a, 0, _up, 0, (ulong)bytes);
            var b = Turn(_a, 0x400, 0x8); barrier(_list, 1, ref b);
            Com.Method<SetRootSigFn>(_list, 29)(_list, _rs);
            Com.Method<SetPsoFn>(_list, 25)(_list, forward ? _fwd : _inv);
            var setUav = Com.Method<SetRootUavFn>(_list, 41); var setC = Com.Method<SetConstantsFn>(_list, 35); var dispatch = Com.Method<DispatchFn>(_list, 14);
            setUav(_list, 1, Com.Method<GpuAddressFn>(_a, 11)(_a)); setUav(_list, 2, Com.Method<GpuAddressFn>(_t, 11)(_t));
            // rows: each a line of w along x; columns: each a line of h down y (forward rows then columns; back the other way round)
            uint[] rows = { (uint)w, (uint)h, 1, (uint)w }, cols = { (uint)h, (uint)w, (uint)w, 1 };
            for (int pass = 0; pass < 2; pass++)
            {
                var c = (pass == 0) == forward ? rows : cols;
                setC(_list, 0, 4, c, 0); dispatch(_list, (c[1] + 63) / 64, 1, 1);
                var u = new D3D12.TransitionBarrier { Type = 2, Resource = _a }; barrier(_list, 1, ref u);   // every line of this pass done before the next reads it
            }
            b = Turn(_a, 0x8, 0x800); barrier(_list, 1, ref b);
            Com.Method<CopyBufferRegionFn>(_list, 15)(_list, _down, 0, _a, 0, (ulong)bytes);
            b = Turn(_a, 0x800, 0x400); barrier(_list, 1, ref b);
            Com.Check(Com.Method<D3D12.CloseFn>(_list, 9)(_list), "Close(list)");
            Com.Method<D3D12.ExecuteCommandListsFn>(_queue, 10)(_queue, 1, new[] { _list });
            _fenceValue++; Com.Method<D3D12.SignalFn>(_queue, 14)(_queue, _fence, _fenceValue);
            if (Com.Method<D3D12.GetCompletedValueFn>(_fence, 8)(_fence) < _fenceValue)
            {
                Com.Method<D3D12.SetEventOnCompletionFn>(_fence, 9)(_fence, _fenceValue, _event);
                if (WaitForSingleObject(_event, 5000) != 0) throw new InvalidOperationException("the card did not finish the wavelet in five seconds");
            }
            Com.Check(Com.Method<MapFn>(_down, 8)(_down, 0, IntPtr.Zero, out p), "Map(down)"); Copy(plane, p, (UIntPtr)bytes); Com.Method<UnmapFn>(_down, 9)(_down, 0, IntPtr.Zero);
        }

        private static IntPtr BlobPtr(IntPtr b) { return Com.Method<BlobPtrFn>(b, 3)(b); }
        private static UIntPtr BlobSize(IntPtr b) { return Com.Method<BlobSizeFn>(b, 4)(b); }
        private static string Text(IntPtr b) { if (b == IntPtr.Zero) return "(no details)"; string s = Marshal.PtrToStringAnsi(BlobPtr(b), (int)BlobSize(b).ToUInt64()); Com.Release(ref b); return s; }

        public void Dispose()
        {
            // (the queue is idle here: every Run waits for its own work)
            Com.Release(ref _a); Com.Release(ref _t); Com.Release(ref _up); Com.Release(ref _down);
            Com.Release(ref _fwd); Com.Release(ref _inv); Com.Release(ref _rs);
            Com.Release(ref _list); Com.Release(ref _alloc); Com.Release(ref _fence); Com.Release(ref _queue);
            if (_event != IntPtr.Zero) { CloseHandle(_event); _event = IntPtr.Zero; }
        }
    }
}
