// Universe · render · the front man's encoder on the graphics card (its shaders: CardVp9.hlsl.cs). Recorded into the frame's own
// command list from the picture just drawn: the frame's planes, the motion from the camera's equations, every block and transform
// coded and rebuilt as a player rebuilds it, the loop filter; then only the blocks' decisions and their coefficients are copied to a
// slot the processor reads a frame later (the picture itself never comes back). Vp9Writer.AddCard writes them into the file.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Render
{
    /// <summary>One frame as the card coded it: each 8 x 8's decisions, each superblock's coefficients and where they start.</summary>
    internal sealed class CardFrame
    {
        public uint[] Grid, Words; public int[] Start; public bool Key, Overflow; public int Q, Level; public uint Ms;
    }

    internal sealed class CardVp9 : IDisposable
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
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateCommittedResourceFn(IntPtr self, ref HeapProps heap, int heapFlags, ref ResDesc desc, int state, IntPtr clear, ref Guid iid, out IntPtr res);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateRootSignatureFn(IntPtr self, uint nodeMask, IntPtr blob, UIntPtr size, ref Guid iid, out IntPtr rs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateComputePipelineStateFn(IntPtr self, IntPtr desc, ref Guid iid, out IntPtr pso);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int MapFn(IntPtr self, uint sub, IntPtr range, out IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void UnmapFn(IntPtr self, uint sub, IntPtr range);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate ulong GpuAddressFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void DispatchFn(IntPtr self, uint x, uint y, uint z);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void CopyBufferRegionFn(IntPtr self, IntPtr dst, ulong dstOff, IntPtr src, ulong srcOff, ulong bytes);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetPsoFn(IntPtr self, IntPtr pso);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootSigFn(IntPtr self, IntPtr rs);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetConstantsFn(IntPtr self, uint index, uint count, uint[] data, uint offset);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void SetRootUavFn(IntPtr self, uint index, ulong address);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSizeFn(IntPtr self);

        public const int Slot = 6144, NP = 64;
        /// <summary>The most coefficient words a frame may bring back (16 MB): past it the frame is marked over, and repeated.</summary>
        public const int OutCap = 4 << 20;
        private readonly IntPtr _device; private IntPtr _rs; private IntPtr _prep, _busy, _m32, _m64, _sbAll, _sbDiag, _loopf, _place, _pack;
        private IntPtr _src, _recA, _recB, _grid, _mot, _str, _len, _out, _tab, _par, _tabUp;
        private readonly IntPtr[] _parUp = new IntPtr[2], _dGrid = new IntPtr[2], _dLen = new IntPtr[2], _dOut = new IntPtr[2];
        private readonly CardFrame[] _pending = new CardFrame[2]; private readonly bool[] _ready = new bool[2]; private int _slot; private bool _flip;
        public readonly int W, H, MiCols, MiRows, SbCols, SbRows, Fw, Fh, S, SH, TileLog2; private readonly int _nMi, _nSb;
        private int _srcW, _srcH; private uint _srow;

        public CardVp9(IntPtr device, int w, int h)
        {
            _device = device; W = w & ~1; H = h & ~1; Fw = (W + 7) & ~7; Fh = (H + 7) & ~7; MiCols = Fw / 8; MiRows = Fh / 8; SbCols = (MiCols + 7) / 8; SbRows = (MiRows + 7) / 8;
            S = SbCols * 64; SH = SbRows * 64; _nMi = MiCols * MiRows; _nSb = SbCols * SbRows;
            int maxLog2 = 1; while ((SbCols >> maxLog2) >= 4) maxLog2++; maxLog2--; int minLog2 = 0; while ((64 << minLog2) < SbCols) minLog2++; TileLog2 = Math.Max(minLog2, maxLog2);
            RootSignature();
            _prep = Pso("PREP"); _busy = Pso("BUSY"); _m32 = Pso("MOTION32"); _m64 = Pso("MOTION64"); _sbAll = Pso("SBALL"); _sbDiag = Pso("SBDIAG"); _loopf = Pso("LOOPF"); _place = Pso("PLACE"); _pack = Pso("PACK");
            long planes = (long)S * SH * 3 / 2;
            _src = Buffer(1, (planes + (long)S * SH * 4) * 4, 4, 0x8);                       // (the planes, then room for the drawn picture beside them)
            _recA = Buffer(1, planes * 4, 4, 0x8); _recB = Buffer(1, planes * 4, 4, 0x8);
            _grid = Buffer(1, (long)_nMi * 2 * 4, 4, 0x8);
            int bc = (MiCols + 3) / 4, br = (MiRows + 3) / 4; _mot = Buffer(1, ((long)_nMi + bc * br * 2 + _nSb * 3) * 4, 4, 0x8);
            _str = Buffer(1, (long)_nSb * Slot * 4, 4, 0x8); _len = Buffer(1, ((long)_nSb * 2 + 2) * 4, 4, 0x8); _out = Buffer(1, (long)OutCap * 4, 4, 0x8);
            _tab = Buffer(1, 4096 * 4, 4, 0x400); _tabUp = Buffer(2, 4096 * 4, 0, 0xAC3); _par = Buffer(1, NP * 4, 4, 0x400);
            for (int i = 0; i < 2; i++) { _parUp[i] = Buffer(2, NP * 4, 0, 0xAC3); _dGrid[i] = Buffer(3, (long)_nMi * 2 * 4, 0, 0x400); _dLen[i] = Buffer(3, ((long)_nSb * 2 + 2) * 4, 0, 0x400); _dOut[i] = Buffer(3, (long)OutCap * 4, 0, 0x400); }
            // the tables: the four scans, their inverses (where each place falls in its scan), cos64
            var tab = new uint[4096]; var scans = new[] { Vp9Tables.DefaultScan4x4, Vp9Tables.DefaultScan8x8, Vp9Tables.DefaultScan16x16, Vp9Tables.DefaultScan32x32 }; int[] at = { 0, 16, 80, 336 };
            for (int t = 0; t < 4; t++) for (int k = 0; k < scans[t].Length; k++) { tab[at[t] + k] = (uint)scans[t][k]; tab[1360 + at[t] + scans[t][k]] = (uint)k; }
            for (int a = 0; a <= 32; a++) tab[2720 + a] = (uint)Vp9Tables.Cos64Lookup[a];
            Put(_tabUp, tab); _tabFresh = true;
            // the grid starts empty: no motion of a frame before
        }
        private bool _tabFresh, _first = true;

        void RootSignature()
        {
            IntPtr parms = Marshal.AllocHGlobal(32 * 11), desc = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 32 * 11; i++) Marshal.WriteByte(parms, i, 0);
                Marshal.WriteInt32(parms, 0, 1); Marshal.WriteInt32(parms, 16, 32);                   // the frame's numbers and the dispatch's, b0
                for (int u = 0; u < 10; u++) { Marshal.WriteInt32(parms, 32 * (u + 1), 4); Marshal.WriteInt32(parms, 32 * (u + 1) + 8, u); }   // u0..u9, root views
                for (int i = 0; i < 40; i++) Marshal.WriteByte(desc, i, 0);
                Marshal.WriteInt32(desc, 0, 11); Marshal.WriteIntPtr(desc, 8, parms);
                IntPtr blob, err;
                if (D3D12SerializeRootSignature(desc, 1, out blob, out err) < 0) throw new InvalidOperationException("front man root signature: " + Text(err));
                Guid iid = D3D12.IID_ID3D12RootSignature;
                Com.Check(Com.Method<CreateRootSignatureFn>(_device, 16)(_device, 0, BlobPtr(blob), BlobSize(blob), ref iid, out _rs), "CreateRootSignature(front man)");
                Com.Release(ref blob);
            }
            finally { Marshal.FreeHGlobal(parms); Marshal.FreeHGlobal(desc); }
        }
        private IntPtr Pso(string entry)
        {
            byte[] src = Encoding.ASCII.GetBytes(CardVp9Shaders.Hlsl); IntPtr code, err;
            if (D3DCompile(src, (UIntPtr)src.Length, "frontman", IntPtr.Zero, IntPtr.Zero, entry, "cs_5_0", 1u << 15, 0, out code, out err) < 0) throw new InvalidOperationException("front man shader " + entry + ": " + Text(err));
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
            Com.Check(Com.Method<CreateCommittedResourceFn>(_device, 27)(_device, ref heap, 0, ref desc, state, IntPtr.Zero, ref iid, out r), "CreateCommittedResource(front man)");
            return r;
        }
        private static void Put(IntPtr up, uint[] data) { IntPtr p; Com.Check(Com.Method<MapFn>(up, 8)(up, 0, IntPtr.Zero, out p), "Map(front man)"); Marshal.Copy((int[])(object)data, 0, p, data.Length); Com.Method<UnmapFn>(up, 9)(up, 0, IntPtr.Zero); }
        private static D3D12.TransitionBarrier Turn(IntPtr r, int from, int to) { return new D3D12.TransitionBarrier { Type = 0, Resource = r, Subresource = 0xFFFFFFFF, StateBefore = from, StateAfter = to }; }
        private static void Uav(D3D12.ResourceBarrierFn barrier, IntPtr list) { var u = new D3D12.TransitionBarrier { Type = 2, Resource = IntPtr.Zero }; barrier(list, 1, ref u); }
        private static ulong Addr(IntPtr r) { return Com.Method<GpuAddressFn>(r, 11)(r); }

        /// <summary>True when a slot is free for the next frame (the one two frames back has been read).</summary>
        public bool SlotFree { get { lock (_ready) return _pending[_slot] == null; } }

        /// <summary>Records one frame's coding into <paramref name="list"/>, from the drawn picture <paramref name="backBuffer"/> (in the
        /// render-target state, and left in it), or from an upload buffer of RGBA words (<paramref name="upload"/>, for a check). The
        /// frame's numbers: its quantiser, whether whole, its loop filter level, the two cameras (null: no motion from them).</summary>
        public bool Record(IntPtr list, IntPtr backBuffer, IntPtr upload, int srcW, int srcH, int q, bool key, int lvl, double[] cam, double[] camPrev, uint ms)
        {
            lock (_ready) if (_pending[_slot] != null) return false;
            var barrier = Com.Method<D3D12.ResourceBarrierFn>(list, 26); var copy = Com.Method<CopyBufferRegionFn>(list, 15);
            var setC = Com.Method<SetConstantsFn>(list, 35); var setUav = Com.Method<SetRootUavFn>(list, 41); var dispatch = Com.Method<DispatchFn>(list, 14); var pso = Com.Method<SetPsoFn>(list, 25);
            uint srow = (uint)((srcW * 4 + 255) & ~255) / 4; _srcW = srcW; _srcH = srcH; _srow = srow;
            if ((long)srow * srcH > (long)S * SH * 4) return false;
            ulong srcOff = (ulong)((long)S * SH * 3 / 2 * 4);
            var b = Turn(_src, 0x8, 0x400); barrier(list, 1, ref b);
            if (upload != IntPtr.Zero) copy(list, _src, srcOff, upload, 0, (ulong)srow * (ulong)srcH * 4);
            else
            {
                b = Turn(backBuffer, D3D12.RESOURCE_STATE_RENDER_TARGET, D3D12.RESOURCE_STATE_COPY_SOURCE); barrier(list, 1, ref b);
                var s = new D3D12.TextureCopyLocation { Resource = backBuffer, Type = 0, SubresourceIndex = 0 };
                var dst = new D3D12.TextureCopyLocation { Resource = _src, Type = 1, Offset = srcOff, Format = D3D12.DXGI_FORMAT_R8G8B8A8_UNORM, Width = (uint)srcW, Height = (uint)srcH, Depth = 1, RowPitch = srow * 4 };
                Com.Method<D3D12.CopyTextureRegionFn>(list, 16)(list, ref dst, 0, 0, 0, ref s, IntPtr.Zero);
                b = Turn(backBuffer, D3D12.RESOURCE_STATE_COPY_SOURCE, D3D12.RESOURCE_STATE_RENDER_TARGET); barrier(list, 1, ref b);
            }
            b = Turn(_src, 0x400, 0x8); barrier(list, 1, ref b);
            // the frame's numbers
            var P = new uint[NP]; int dcq = Vp9Tables.DcQLookup[q], acq = Vp9Tables.AcQLookup[q];
            P[0] = (uint)MiCols; P[1] = (uint)MiRows; P[2] = (uint)SbCols; P[3] = (uint)SbRows; P[4] = (uint)Fw; P[5] = (uint)Fh; P[6] = (uint)S; P[7] = key ? 1u : 0;
            P[8] = (uint)dcq; P[9] = (uint)acq; float step = (float)(acq / 8.0 * Vp9Writer.BusySteps); P[10] = F(step * step); P[11] = (uint)Math.Max(1, acq / 8);
            bool flow = !key && cam != null && camPrev != null && cam.Length >= 10 && camPrev.Length >= 10 && cam[9] > 0 && camPrev[9] > 0;
            P[12] = flow ? 1u : 0; P[13] = F((float)(flow ? Math.Max(0.25, Math.Min(4, camPrev[9] / cam[9])) : 1));
            if (flow) for (int i = 0; i < 9; i++) { P[14 + i] = F((float)cam[i]); P[23 + i] = F((float)camPrev[i]); }
            P[32] = (uint)W; P[33] = (uint)H; P[34] = (uint)srcW; P[35] = (uint)srcH; P[36] = srow;
            int limit = Math.Max(1, lvl), blimit = 2 * (lvl + 2) + limit; P[37] = (uint)lvl; P[38] = (uint)limit; P[39] = (uint)blimit; P[40] = (uint)(lvl >> 4);
            P[41] = (uint)TileLog2; P[42] = (uint)SH; P[43] = OutCap; P[44] = (uint)Vp9Writer.SplitFloor;
            Put(_parUp[_slot], P);
            b = Turn(_par, 0x8 | 0x400, 0x400); if (!_first) { b = Turn(_par, 0x8, 0x400); barrier(list, 1, ref b); }
            copy(list, _par, 0, _parUp[_slot], 0, NP * 4);
            b = Turn(_par, 0x400, 0x8); barrier(list, 1, ref b);
            if (_tabFresh) { copy(list, _tab, 0, _tabUp, 0, 4096 * 4); _tabFresh = false; }
            if (_first) { b = Turn(_tab, 0x400, 0x8); barrier(list, 1, ref b); }
            _first = false;

            IntPtr cur = _flip ? _recB : _recA, refb = _flip ? _recA : _recB;
            Com.Method<SetRootSigFn>(list, 29)(list, _rs);
            IntPtr[] views = { _src, cur, refb, _grid, _mot, _str, _len, _out, _tab, _par };
            for (int u = 0; u < 10; u++) setUav(list, (uint)(u + 1), Addr(views[u]));
            // the frame's numbers as constants (every thread the same): k[0] and k[1] each dispatch's own
            var k = new uint[32]; int[] map = { 0, 1, 2, 3, 4, 5, 6, 42, 7, 8, 9, 10, 11, 12, 13, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 43, 44 };
            for (int i = 0; i < map.Length; i++) k[2 + i] = P[map[i]];
            pso(list, _prep); setC(list, 0, 32, k, 0); dispatch(list, (uint)(Fw + 7) / 8, (uint)(Fh + 7) / 8, 1); Uav(barrier, list);
            pso(list, _busy); dispatch(list, (uint)(_nMi + 63) / 64, 1, 1); Uav(barrier, list);
            if (!key)
            {
                int bc = (MiCols + 3) / 4, br = (MiRows + 3) / 4;
                pso(list, _m32); dispatch(list, (uint)(bc * br), 1, 1); Uav(barrier, list);
                pso(list, _m64); dispatch(list, (uint)_nSb, 1, 1); Uav(barrier, list);
                pso(list, _sbAll); dispatch(list, (uint)_nSb, 1, 1); Uav(barrier, list);
            }
            else
            {
                pso(list, _sbDiag);
                for (int d = 0; d < SbRows + SbCols - 1; d++) { int r0 = Math.Max(0, d - SbCols + 1), r1 = Math.Min(d, SbRows - 1); k[0] = (uint)d; k[1] = (uint)r0; setC(list, 0, 32, k, 0); dispatch(list, (uint)(r1 - r0 + 1), 1, 1); Uav(barrier, list); }   // (each diagonal's superblocks, the rows on it)
            }
            if (lvl > 0)
            {
                pso(list, _loopf);
                for (int wv = 0; wv < 2 * (SbRows - 1) + SbCols; wv++) { int r0 = Math.Max(0, (wv - SbCols + 2) / 2), r1 = Math.Min(SbRows - 1, wv / 2); if (r1 < r0) continue; k[0] = (uint)wv; k[1] = (uint)r0; setC(list, 0, 32, k, 0); dispatch(list, (uint)(r1 - r0 + 1), 1, 1); Uav(barrier, list); }   // (each wave: 2 x row + column; the rows on it)
            }
            pso(list, _place); dispatch(list, 1, 1, 1); Uav(barrier, list);
            pso(list, _pack); dispatch(list, (uint)_nSb, 1, 1); Uav(barrier, list);
            // what comes back: the decisions, where each superblock's coefficients start, the coefficients
            foreach (var r in new[] { _grid, _len, _out }) { b = Turn(r, 0x8, 0x800); barrier(list, 1, ref b); }
            copy(list, _dGrid[_slot], 0, _grid, 0, (ulong)_nMi * 2 * 4);
            copy(list, _dLen[_slot], 0, _len, 0, ((ulong)_nSb * 2 + 2) * 4);
            copy(list, _dOut[_slot], 0, _out, 0, (ulong)OutCap * 4);
            foreach (var r in new[] { _grid, _len, _out }) { b = Turn(r, 0x800, 0x8); barrier(list, 1, ref b); }
            _flip = !_flip;                                                             // (this frame's picture, the next one's reference)
            lock (_ready) { _pending[_slot] = new CardFrame { Key = key, Q = q, Level = lvl, Ms = ms }; _ready[_slot] = false; }
            _slot ^= 1;
            return true;
        }
        static uint F(float f) { return BitConverter.ToUInt32(BitConverter.GetBytes(f), 0); }

        /// <summary>Marks the frames the card has finished (call after its fence) ready to read.</summary>
        public void Finished() { lock (_ready) for (int s = 0; s < 2; s++) if (_pending[s] != null) _ready[s] = true; }
        /// <summary>The oldest finished frame, read from its slot (null: none ready); the slot is then free.</summary>
        public CardFrame Take()
        {
            int s = -1;
            lock (_ready) { for (int k = 0; k < 2; k++) { int c = _slot ^ k; if (_pending[c] != null && _ready[c]) { s = c; break; } } }
            if (s < 0) return null;
            var f = _pending[s]; IntPtr p;
            f.Grid = new uint[_nMi * 2];
            Com.Check(Com.Method<MapFn>(_dGrid[s], 8)(_dGrid[s], 0, IntPtr.Zero, out p), "Map(grid)"); Marshal.Copy(p, (int[])(object)f.Grid, 0, f.Grid.Length); Com.Method<UnmapFn>(_dGrid[s], 9)(_dGrid[s], 0, IntPtr.Zero);
            var len = new uint[_nSb * 2 + 2];
            Com.Check(Com.Method<MapFn>(_dLen[s], 8)(_dLen[s], 0, IntPtr.Zero, out p), "Map(lengths)"); Marshal.Copy(p, (int[])(object)len, 0, len.Length); Com.Method<UnmapFn>(_dLen[s], 9)(_dLen[s], 0, IntPtr.Zero);
            uint total = len[_nSb * 2]; f.Overflow = len[_nSb * 2 + 1] != 0;
            f.Start = new int[_nSb]; for (int i = 0; i < _nSb; i++) f.Start[i] = (int)len[_nSb + i];
            f.Words = new uint[f.Overflow ? 0 : total];
            if (!f.Overflow && total > 0) { Com.Check(Com.Method<MapFn>(_dOut[s], 8)(_dOut[s], 0, IntPtr.Zero, out p), "Map(coefficients)"); Marshal.Copy(p, (int[])(object)f.Words, 0, (int)total); Com.Method<UnmapFn>(_dOut[s], 9)(_dOut[s], 0, IntPtr.Zero); }
            lock (_ready) { _pending[s] = null; _ready[s] = false; }
            return f;
        }
        /// <summary>For a check: the picture the card rebuilt for the last frame recorded (Y, Cb, Cr at the writer's strides), read
        /// from a readback buffer the caller copies it to.</summary>
        public IntPtr LastPicture { get { return _flip ? _recA : _recB; } }
        public long PlaneWords { get { return (long)S * SH * 3 / 2; } }

        private static IntPtr BlobPtr(IntPtr b) { return Com.Method<BlobPtrFn>(b, 3)(b); }
        private static UIntPtr BlobSize(IntPtr b) { return Com.Method<BlobSizeFn>(b, 4)(b); }
        private static string Text(IntPtr b) { if (b == IntPtr.Zero) return "(no details)"; string s = Marshal.PtrToStringAnsi(BlobPtr(b), (int)BlobSize(b).ToUInt64()); Com.Release(ref b); return s; }

        public void Dispose()
        {
            foreach (var r in new[] { _src, _recA, _recB, _grid, _mot, _str, _len, _out, _tab, _par, _tabUp }) { var x = r; Com.Release(ref x); }
            for (int i = 0; i < 2; i++) { Com.Release(ref _parUp[i]); Com.Release(ref _dGrid[i]); Com.Release(ref _dLen[i]); Com.Release(ref _dOut[i]); }
            foreach (var r in new[] { _prep, _busy, _m32, _m64, _sbAll, _sbDiag, _loopf, _place, _pack, _rs }) { var x = r; Com.Release(ref x); }
        }
    }
}
