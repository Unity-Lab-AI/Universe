// DNA-AND · render · GPU buffers and the draw calls for billboards (stars, glow, points).
using System;
using System.Runtime.InteropServices;

namespace DnaAnd.Render
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct StarVertex
    {
        public float X, Y, Z, Size;
        public float R, G, B, A;
    }

    /// <summary>A buffer the CPU writes and the GPU reads (upload heap).</summary>
    internal sealed class UploadBuffer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)] private struct HeapProps { public int Type, CpuPage, Pool; public uint CreateMask, VisibleMask; }
        [StructLayout(LayoutKind.Explicit, Size = 56)]
        private struct ResDesc
        {
            [FieldOffset(0)] public int Dimension; [FieldOffset(8)] public ulong Alignment; [FieldOffset(16)] public ulong Width;
            [FieldOffset(24)] public uint Height; [FieldOffset(28)] public ushort Depth; [FieldOffset(30)] public ushort Mips;
            [FieldOffset(32)] public uint Format; [FieldOffset(36)] public uint SampleCount; [FieldOffset(40)] public uint SampleQuality;
            [FieldOffset(44)] public int Layout; [FieldOffset(48)] public int Flags;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateCommittedResourceFn(IntPtr self, ref HeapProps heap, int heapFlags, ref ResDesc desc, int state, IntPtr clear, ref Guid iid, out IntPtr res); // 27
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapFn(IntPtr self, uint sub, IntPtr range, out IntPtr data); // 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate ulong GpuAddressFn(IntPtr self);                               // 11

        private IntPtr _resource, _mapped;
        public readonly long Bytes;
        public ulong GpuAddress { get; private set; }

        public UploadBuffer(IntPtr device, long bytes)
        {
            Bytes = Math.Max(256, bytes);
            var heap = new HeapProps { Type = 2, CreateMask = 1, VisibleMask = 1 };   // upload heap
            var desc = new ResDesc { Dimension = 1, Width = (ulong)Bytes, Height = 1, Depth = 1, Mips = 1, SampleCount = 1, Layout = 1 };
            Guid iid = D3D12.IID_ID3D12Resource;
            Com.Check(Com.Method<CreateCommittedResourceFn>(device, 27)(device, ref heap, 0, ref desc, 0xAC3, IntPtr.Zero, ref iid, out _resource), "CreateCommittedResource");
            Com.Check(Com.Method<MapFn>(_resource, 8)(_resource, 0, IntPtr.Zero, out _mapped), "Map");
            GpuAddress = Com.Method<GpuAddressFn>(_resource, 11)(_resource);
        }

        public void Write(StarVertex[] data, int count)
        {
            int size = Marshal.SizeOf(typeof(StarVertex));
            if ((long)count * size > Bytes) count = (int)(Bytes / size);
            GCHandle h = GCHandle.Alloc(data, GCHandleType.Pinned);
            try { CopyMemory(_mapped, h.AddrOfPinnedObject(), (UIntPtr)((long)count * size)); }
            finally { h.Free(); }
        }

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")] private static extern void CopyMemory(IntPtr dst, IntPtr src, UIntPtr size);

        // a buffer let go is never freed at once: frames still on the card may read it, and freeing it under them removes the
        // device; it is freed some frames later (Collect, once a frame), for every buffer in the game
        private static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, long>> Retired = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<IntPtr, long>>();
        private static long _frame;
        public void Dispose() { if (_resource != IntPtr.Zero) lock (Retired) Retired.Add(new System.Collections.Generic.KeyValuePair<IntPtr, long>(_resource, _frame)); _resource = IntPtr.Zero; _mapped = IntPtr.Zero; }
        /// <summary>Once a frame: frees the buffers let go more than eight frames ago.</summary>
        public static void Collect()
        {
            _frame++;
            lock (Retired) for (int k = Retired.Count - 1; k >= 0; k--) if (_frame - Retired[k].Value > 8) { var r = Retired[k].Key; Com.Release(ref r); Retired.RemoveAt(k); }
        }
    }

    /// <summary>Records billboard draws into the frame's command list.</summary>
    internal sealed class BillboardDrawer
    {
        [StructLayout(LayoutKind.Sequential)] private struct Viewport { public float X, Y, W, H, MinZ, MaxZ; }
        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int L, T, R, B; }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetPsoFn(IntPtr self, IntPtr pso);                    // 25
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootSigFn(IntPtr self, IntPtr rs);                 // 30
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetConstantsFn(IntPtr self, uint index, uint count, float[] data, uint offset); // 36
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootSrvFn(IntPtr self, uint index, ulong address); // 40
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetTopologyFn(IntPtr self, int topology);             // 20
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetViewportsFn(IntPtr self, uint count, ref Viewport vp); // 21
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetScissorsFn(IntPtr self, uint count, ref Rect r);   // 22
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DrawInstancedFn(IntPtr self, uint verts, uint instances, uint firstVert, uint firstInst); // 12

        private readonly SetPsoFn _pso; private readonly SetRootSigFn _rs; private readonly SetConstantsFn _consts; private readonly SetRootSrvFn _srv;
        private readonly SetTopologyFn _topo; private readonly SetViewportsFn _vp; private readonly SetScissorsFn _sc; private readonly DrawInstancedFn _draw;
        private readonly IntPtr _list; private readonly BillboardPipeline _pipe;

        public BillboardDrawer(IntPtr commandList, BillboardPipeline pipeline)
        {
            _list = commandList; _pipe = pipeline;
            _pso = Com.Method<SetPsoFn>(_list, 25); _rs = Com.Method<SetRootSigFn>(_list, 30);
            _consts = Com.Method<SetConstantsFn>(_list, 36); _srv = Com.Method<SetRootSrvFn>(_list, 40);
            _topo = Com.Method<SetTopologyFn>(_list, 20); _vp = Com.Method<SetViewportsFn>(_list, 21);
            _sc = Com.Method<SetScissorsFn>(_list, 22); _draw = Com.Method<DrawInstancedFn>(_list, 12);
        }

        /// <summary>Draws <paramref name="count"/> billboards from <paramref name="buffer"/> into a screen tile.</summary>
        public void Draw(UploadBuffer buffer, int count, float[] constants, int x, int y, int w, int h)
        {
            var vp = new Viewport { X = x, Y = y, W = w, H = h, MinZ = 0, MaxZ = 1 };
            var r = new Rect { L = x, T = y, R = x + w, B = y + h };
            _pso(_list, _pipe.Pipeline); _rs(_list, _pipe.RootSignature);
            _consts(_list, 0, BillboardPipeline.ConstantCount, constants, 0);
            _srv(_list, 1, buffer.GpuAddress);
            _topo(_list, 4); // triangle list
            _vp(_list, 1, ref vp); _sc(_list, 1, ref r);
            _draw(_list, 6, (uint)count, 0, 0);
        }
    }
}
