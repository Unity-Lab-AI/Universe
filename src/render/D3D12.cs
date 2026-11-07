// DNA-AND · render · our own declarations for Direct3D 12 and DXGI (Windows system files d3d12.dll, dxgi.dll).
using System;
using System.Runtime.InteropServices;

namespace DnaAnd.Render
{
    internal static class D3D12
    {
        // ── Interface IDs ──
        public static readonly Guid IID_IDXGIFactory2 = new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0");
        public static readonly Guid IID_IDXGISwapChain3 = new Guid("94d99bdb-f1f8-4ab0-b236-7da0170edab1");
        public static readonly Guid IID_ID3D12Device = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
        public static readonly Guid IID_ID3D12CommandQueue = new Guid("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
        public static readonly Guid IID_ID3D12CommandAllocator = new Guid("6102dee4-af59-4b09-b999-b44d73f09b24");
        public static readonly Guid IID_ID3D12GraphicsCommandList = new Guid("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
        public static readonly Guid IID_ID3D12DescriptorHeap = new Guid("8efb471d-616c-4f49-90f7-127bb763fa51");
        public static readonly Guid IID_ID3D12Resource = new Guid("696442be-a72e-4059-bc79-5b5c98040fad");
        public static readonly Guid IID_ID3D12Fence = new Guid("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
        public static readonly Guid IID_ID3D12RootSignature = new Guid("c54a6b66-72df-4ee8-8be5-a946a1429214");
        public static readonly Guid IID_ID3D12PipelineState = new Guid("765a30f3-f624-4c6f-a828-ace948622445");

        // ── Constants ──
        public const int D3D_FEATURE_LEVEL_11_0 = 0xb000;
        public const int COMMAND_LIST_TYPE_DIRECT = 0;
        public const int DESCRIPTOR_HEAP_TYPE_RTV = 2, DESCRIPTOR_HEAP_TYPE_DSV = 3;
        public const uint DXGI_FORMAT_D32_FLOAT = 40;
        public const int RESOURCE_STATE_DEPTH_WRITE = 0x10;
        public const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
        public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
        public const uint DXGI_SWAP_EFFECT_FLIP_DISCARD = 4;
        public const uint DXGI_MWA_NO_ALT_ENTER = 2;
        public const int RESOURCE_STATE_PRESENT = 0;
        public const int RESOURCE_STATE_RENDER_TARGET = 0x4;
        public const int RESOURCE_STATE_COPY_DEST = 0x400, RESOURCE_STATE_COPY_SOURCE = 0x800;

        // where a texture copy reads from or writes to: a texture's sub-picture (type 0) or rows in a buffer (type 1)
        [StructLayout(LayoutKind.Explicit, Size = 48)]
        public struct TextureCopyLocation
        {
            [FieldOffset(0)] public IntPtr Resource;
            [FieldOffset(8)] public int Type;
            [FieldOffset(16)] public uint SubresourceIndex;      // type 0
            [FieldOffset(16)] public ulong Offset;               // type 1 ...
            [FieldOffset(24)] public uint Format;
            [FieldOffset(28)] public uint Width;
            [FieldOffset(32)] public uint Height;
            [FieldOffset(36)] public uint Depth;
            [FieldOffset(40)] public uint RowPitch;
        }
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void CopyTextureRegionFn(IntPtr self, ref TextureCopyLocation dst, uint x, uint y, uint z, ref TextureCopyLocation src, IntPtr box); // 16

        // ── Structs ──
        [StructLayout(LayoutKind.Sequential)]
        public struct CommandQueueDesc { public int Type; public int Priority; public int Flags; public uint NodeMask; }

        [StructLayout(LayoutKind.Sequential)]
        public struct SwapChainDesc1
        {
            public uint Width, Height, Format; public int Stereo;
            public uint SampleCount, SampleQuality, BufferUsage, BufferCount, Scaling, SwapEffect, AlphaMode, Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DescriptorHeapDesc { public int Type; public uint NumDescriptors; public int Flags; public uint NodeMask; }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        public struct TransitionBarrier
        {
            [FieldOffset(0)] public int Type;          // 0 = transition
            [FieldOffset(4)] public int Flags;
            [FieldOffset(8)] public IntPtr Resource;
            [FieldOffset(16)] public uint Subresource;  // 0xFFFFFFFF = all
            [FieldOffset(20)] public int StateBefore;
            [FieldOffset(24)] public int StateAfter;
        }

        // ── Entry points from Windows' own system files ──
        [DllImport("dxgi.dll")]
        public static extern int CreateDXGIFactory2(uint flags, ref Guid iid, out IntPtr factory);

        [DllImport("d3d12.dll")]
        public static extern int D3D12CreateDevice(IntPtr adapter, int minFeatureLevel, ref Guid iid, out IntPtr device);

        // ── Method signatures (slot numbers next to each) ──
        // IDXGIFactory2
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int MakeWindowAssociationFn(IntPtr self, IntPtr hwnd, uint flags);             // 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateSwapChainForHwndFn(IntPtr self, IntPtr device, IntPtr hwnd,
            ref SwapChainDesc1 desc, IntPtr fullscreenDesc, IntPtr restrictOutput, out IntPtr swapChain);                                                   // 15
        // IDXGISwapChain3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int PresentFn(IntPtr self, uint syncInterval, uint flags);                       // 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int GetBufferFn(IntPtr self, uint index, ref Guid iid, out IntPtr surface);       // 9
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ResizeBuffersFn(IntPtr self, uint count, uint w, uint h, uint fmt, uint flags); // 13
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint GetCurrentBackBufferIndexFn(IntPtr self);                                  // 36
        // ID3D12Device
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateCommandQueueFn(IntPtr self, ref CommandQueueDesc desc, ref Guid iid, out IntPtr q); // 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateCommandAllocatorFn(IntPtr self, int type, ref Guid iid, out IntPtr a);        // 9
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateCommandListFn(IntPtr self, uint nodeMask, int type, IntPtr alloc, IntPtr pso, ref Guid iid, out IntPtr list); // 12
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateDescriptorHeapFn(IntPtr self, ref DescriptorHeapDesc desc, ref Guid iid, out IntPtr heap); // 14
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint GetDescriptorHandleIncrementSizeFn(IntPtr self, int type);                     // 15
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void CreateRenderTargetViewFn(IntPtr self, IntPtr resource, IntPtr desc, IntPtr handle); // 20
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CreateFenceFn(IntPtr self, ulong initial, int flags, ref Guid iid, out IntPtr fence); // 36
        // ID3D12DescriptorHeap (struct return goes through a hidden pointer)
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate IntPtr GetCpuHandleStartFn(IntPtr self, out IntPtr handle);                       // 9
        // ID3D12CommandQueue
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ExecuteCommandListsFn(IntPtr self, uint count, IntPtr[] lists);               // 10
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SignalFn(IntPtr self, IntPtr fence, ulong value);                               // 14
        // ID3D12Fence
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate ulong GetCompletedValueFn(IntPtr self);                                           // 8
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int SetEventOnCompletionFn(IntPtr self, ulong value, IntPtr evt);                  // 9
        // ID3D12CommandAllocator
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int AllocatorResetFn(IntPtr self);                                                // 8
        // ID3D12GraphicsCommandList
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int CloseFn(IntPtr self);                                                        // 9
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int ListResetFn(IntPtr self, IntPtr alloc, IntPtr pso);                           // 10
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ResourceBarrierFn(IntPtr self, uint count, ref TransitionBarrier barrier);     // 26
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void OMSetRenderTargetsFn(IntPtr self, uint count, ref IntPtr handles, int single, ref IntPtr depth); // 46
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearDepthStencilViewFn(IntPtr self, IntPtr handle, int flags, float depth, byte stencil, uint rects, IntPtr rectList); // 47
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void CreateDepthStencilViewFn(IntPtr self, IntPtr resource, IntPtr desc, IntPtr handle); // 21
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void ClearRenderTargetViewFn(IntPtr self, IntPtr handle, float[] color, uint rects, IntPtr rectList); // 48
    }
}
