// Universe · render · root signature, shaders and pipeline state for drawing glowing billboards
// (stars, galaxies, particles). Shaders are compiled by Windows' own d3dcompiler_47.dll.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Universe.Render
{
    internal sealed class BillboardPipeline : IDisposable
    {
        public const int ConstantCount = 48; // root constants (32-bit values) per draw
        public IntPtr RootSignature, Pipeline;

        [DllImport("d3d12.dll")]
        private static extern int D3D12SerializeRootSignature(IntPtr desc, int version, out IntPtr blob, out IntPtr errors);

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] src, UIntPtr size, string name, IntPtr defines, IntPtr include,
            string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrFn(IntPtr self);   // 3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSizeFn(IntPtr self); // 4
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateRootSignatureFn(IntPtr self, uint nodeMask, IntPtr blob, UIntPtr size, ref Guid iid, out IntPtr rs); // 16
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateGraphicsPipelineStateFn(IntPtr self, IntPtr desc, ref Guid iid, out IntPtr pso); // 10

        private const string Hlsl = @"
cbuffer C : register(b0)
{
    row_major float4x4 viewProj;
    float4 right;   // xyz camera right
    float4 up;      // xyz camera up
    float4 misc;    // x time, y size scale, z style (0 glow, otherwise faceted)
    float4 screen;  // x width, y height (of this tile, in pixels), z minimum size per unit of distance, w maximum (0: none)
    float4 extra;   // x: how much dimmer (0 full bright, 1 gone), so things fade in and out, never switch
    float4 lensA;   // a black hole's light bending: xy where it is on screen (-1..1), z its Einstein ring (same units, up the screen), w its distance
    float4 lensB;   // x: width over height of the screen; y: 1 = orbital clouds (each record an orbital, its points placed here); z: points per orbital; w: the web's fade distance (0: not the web)
    float4 webA;    // the cosmic web's light, sifted here on the card: xyz where you are (in the points' own frame), w how near round you is left out (the galaxies made in full stand there; it fades in over half again past that, as they fade out)
};
struct Star { float3 pos; float size; float4 color; };
StructuredBuffer<Star> stars : register(t0);
struct VO { float4 pos : SV_Position; float2 uv : TEXCOORD0; float4 col : COLOR0; };

static const float2 corners[6] = { float2(-1,-1), float2(-1,1), float2(1,1), float2(-1,-1), float2(1,1), float2(1,-1) };

uint ohash(uint a, uint b) { uint h = a * 747796405u + b * 2891336453u + 1u; h = ((h >> ((h >> 28u) + 4u)) ^ h) * 277803737u; return (h >> 22u) ^ h; }
float oh01(uint a, uint b) { return (ohash(a, b) & 0xFFFFFF) / 16777216.0; }
// how strongly an orbital (l, m) leans along a direction: its real |Y|^2 shape (s round, p dumbbells, d clovers, f more lobed)
float olobe(int l, int m, float3 d)
{
    if (l == 0) return 1;
    if (l == 1) return m == 0 ? d.x * d.x * 3 : m == 1 ? d.y * d.y * 3 : d.z * d.z * 3;
    if (l == 2) return m == 0 ? 15 * d.x * d.x * d.y * d.y : m == 1 ? 15 * d.y * d.y * d.z * d.z : m == 2 ? 15 * d.x * d.x * d.z * d.z : m == 3 ? 3.75 * (d.x * d.x - d.z * d.z) * (d.x * d.x - d.z * d.z) : 1.25 * (3 * d.y * d.y - 1) * (3 * d.y * d.y - 1);
    float c = cos(m * 1.3) * d.x + sin(m * 1.3) * d.z; return 7 * pow(abs(c * d.y), 1.5) + 0.2;
}
VO VS(uint vid : SV_VertexID, uint iid : SV_InstanceID)
{
    uint rec = iid, sub = 0;
    if (lensB.y > 0.5) { uint per = max(1u, (uint)lensB.z); rec = iid / per; sub = iid % per; }
    Star s = stars[rec];
    bool webOut = false;
    if (lensB.w > 0)
    {
        // a point of the web's light: left out near you (the galaxies made in full stand there) and past three fade distances;
        // fading with distance otherwise (seen through the whole web at once it would stack into an even dust)
        float wl = length(s.pos - webA.xyz);
        webOut = wl < webA.w || wl > lensB.w * 3;
        s.color.rgb *= 4.0 * exp(-wl / lensB.w) * saturate((wl - webA.w) / (webA.w * 0.5));   // faded in over half again past where it starts: just where the galaxies made in full fade out, so the two never show at once                        // each point a galaxy's whole light, bright enough that the web's strands read across hundreds of millions of light-years
    }
    if (lensB.y > 0.5)
    {
        // an electron's orbital, worked out here on the card: one of its points, where the electron may be found, in the
        // orbital's real shape about its shell's distance, sampled afresh a few times a second (it shimmers: a wave, not a dot)
        uint code = (uint)(s.color.a * 64 + 0.5), l = code / 8u, m = code % 8u;
        uint key = asuint(s.pos.x) ^ (asuint(s.pos.y) * 7919u) ^ (asuint(s.pos.z) * 104729u) ^ (code * 31u);
        uint tick = 0;                                                    // (a steady cloud: sampled once, never flickering like static)
        float3 dir = float3(0, 1, 0); float best = -1;
        for (uint t = 0; t < 6u; t++)
        {
            float u = oh01(key + sub * 13u, tick * 7u + t), v = oh01(key + sub * 13u + 5u, tick * 7u + t);
            float th = acos(1 - 2 * u), ph = v * 6.2832; float3 d = float3(sin(th) * cos(ph), cos(th), sin(th) * sin(ph));
            float sc = olobe((int)l, (int)m, d) - oh01(key + sub, tick + t * 3u) * 3.2;
            if (sc > best) { best = sc; dir = d; }
            if (sc > 0) break;
        }
        float w = oh01(key + sub * 3u + 1u, tick);
        s.pos += dir * s.size * (0.45 + 1.1 * pow(w, 0.7));
        s.size = 0;
        s.color.a = 1;
    }
    bool o0 = false;
    float2 c = corners[vid];
    float4 cc0 = mul(float4(s.pos, 1), viewProj);
    float size = max(s.size * misc.y, cc0.w * screen.z); // never smaller than screen.z of the view distance
    if (screen.w > 0) size = min(size, cc0.w * screen.w);   // and, when asked, never bigger than screen.w of it (points of light stay points up close)
    size = min(size, cc0.w * 0.35);                               // and never more than most of the view, for every point (a soft glow right up close
                                                                  // looks the same, but thousands of them each covering the whole screen are more work
                                                                  // than the card may take in a frame, and Windows resets it)
    if (cc0.w <= 0) o0 = true;
    float3 world = s.pos + (right.xyz * c.x + up.xyz * c.y) * size;
    VO o;
    o.pos = mul(float4(world, 1), viewProj);
    if (webOut || o0) o.pos = float4(2, 2, 2, 1);                 // (outside the view, or behind you: not drawn)
    if (lensA.z > 0 && cc0.w > lensA.w)
    {
        // light passing a black hole is bent toward it by its mass (general relativity): what is behind it shows moved outward,
        // by the lens equation theta' = (theta + sqrt(theta^2 + 4 thetaE^2)) / 2, its Einstein ring thetaE from the mass and distance
        float2 ndc = cc0.xy / cc0.w, d = ndc - lensA.xy; d.x *= lensB.x;
        float r = length(d);
        if (r > 1e-6)
        {
            float r2 = (r + sqrt(r * r + 4 * lensA.z * lensA.z)) * 0.5;
            float2 nd = d / r * r2; nd.x /= lensB.x;
            o.pos.xy += (lensA.xy + nd - ndc) * o.pos.w;
        }
    }
    o.uv = c;
    o.col = s.color * (1 - extra.x);
    return o;
}

float4 PS(VO i) : SV_Target
{
    float style = misc.z;
    float d = length(i.uv);
    float a;
    if (style < 0.5) { a = saturate(1 - d); a = a * a; }                            // smooth glow: gas, clouds of stars, cores
    else                                                                              // a round point of light: a bright disc with a soft edge
    {
        if (d > 1) discard;
        a = saturate((0.75 - d) / 0.25);
        float core = 1 - 0.35 * d * d;                                                // brightest at the middle, like a lit sphere seen from afar
        return float4(i.col.rgb * core * a, a);
    }
    return float4(i.col.rgb * a, a);
}
";

        public BillboardPipeline(IntPtr device)
        {
            // ── root signature: [0] root constants b0, [1] root SRV t0 (the vertex list), [2] root SRV t1 (the waveform) ──
            IntPtr parms = Marshal.AllocHGlobal(96);
            IntPtr desc = Marshal.AllocHGlobal(40);
            try
            {
                for (int i = 0; i < 96; i++) Marshal.WriteByte(parms, i, 0);
                Marshal.WriteInt32(parms, 0, 1);                 // 32-bit constants
                Marshal.WriteInt32(parms, 8, 0);                 // register b0
                Marshal.WriteInt32(parms, 12, 0);                // space 0
                Marshal.WriteInt32(parms, 16, ConstantCount);    // count
                Marshal.WriteInt32(parms, 24, 0);                // visible to all stages
                Marshal.WriteInt32(parms, 32, 3);                // root SRV
                Marshal.WriteInt32(parms, 40, 0);                // register t0
                Marshal.WriteInt32(parms, 44, 0);
                Marshal.WriteInt32(parms, 56, 0);
                Marshal.WriteInt32(parms, 64, 3);                // root SRV
                Marshal.WriteInt32(parms, 72, 1);                // register t1: the waveform, for detail worked out on the card
                Marshal.WriteInt32(parms, 76, 0);
                Marshal.WriteInt32(parms, 88, 0);
                for (int i = 0; i < 40; i++) Marshal.WriteByte(desc, i, 0);
                Marshal.WriteInt32(desc, 0, 3);
                Marshal.WriteIntPtr(desc, 8, parms);

                IntPtr blob, err;
                int hr = D3D12SerializeRootSignature(desc, 1, out blob, out err);
                if (hr < 0) throw new InvalidOperationException("Root signature: " + BlobText(err));
                Guid iid = D3D12.IID_ID3D12RootSignature;
                Com.Check(Com.Method<CreateRootSignatureFn>(device, 16)(device, 0, BlobPtr(blob), BlobSize(blob), ref iid, out RootSignature), "CreateRootSignature");
                Com.Release(ref blob);
            }
            finally { Marshal.FreeHGlobal(parms); Marshal.FreeHGlobal(desc); }

            IntPtr vs = Compile("VS", "vs_5_0"), ps = Compile("PS", "ps_5_0");

            // ── pipeline state (laid out field by field, 656 bytes on 64-bit Windows) ──
            IntPtr p = Marshal.AllocHGlobal(656);
            try
            {
                for (int i = 0; i < 656; i++) Marshal.WriteByte(p, i, 0);
                Marshal.WriteIntPtr(p, 0, RootSignature);
                Marshal.WriteIntPtr(p, 8, BlobPtr(vs)); Marshal.WriteInt64(p, 16, (long)BlobSize(vs).ToUInt64());
                Marshal.WriteIntPtr(p, 24, BlobPtr(ps)); Marshal.WriteInt64(p, 32, (long)BlobSize(ps).ToUInt64());
                // blend state at 120: render target 0 at 128, additive
                int rt = 128;
                Marshal.WriteInt32(p, rt + 0, 1);   // blend on
                Marshal.WriteInt32(p, rt + 8, 2);   // src ONE
                Marshal.WriteInt32(p, rt + 12, 2);  // dest ONE
                Marshal.WriteInt32(p, rt + 16, 1);  // add
                Marshal.WriteInt32(p, rt + 20, 2);  // src alpha ONE
                Marshal.WriteInt32(p, rt + 24, 2);  // dest alpha ONE
                Marshal.WriteInt32(p, rt + 28, 1);  // add
                Marshal.WriteInt32(p, rt + 32, 4);  // logic op: no-op
                Marshal.WriteByte(p, rt + 36, 0xF); // write all colours
                for (int k = 1; k < 8; k++) { Marshal.WriteInt32(p, 128 + k * 40 + 32, 4); Marshal.WriteByte(p, 128 + k * 40 + 36, 0xF); }
                Marshal.WriteInt32(p, 448, -1);     // sample mask
                // rasterizer at 452: solid, no culling, depth clip on
                Marshal.WriteInt32(p, 452, 3); Marshal.WriteInt32(p, 456, 1); Marshal.WriteInt32(p, 476, 1);
                // depth/stencil at 496: test against depth (glows hide behind solid ground) but never write it
                Marshal.WriteInt32(p, 496, 1); Marshal.WriteInt32(p, 500, 0); Marshal.WriteInt32(p, 504, 7);   // reversed depth: nearer or equal passes
                for (int f = 0; f < 2; f++) { int b = 516 + f * 16; Marshal.WriteInt32(p, b, 1); Marshal.WriteInt32(p, b + 4, 1); Marshal.WriteInt32(p, b + 8, 1); Marshal.WriteInt32(p, b + 12, 8); }
                Marshal.WriteInt32(p, 572, 3);      // triangles
                Marshal.WriteInt32(p, 576, 1);      // one render target
                Marshal.WriteInt32(p, 580, (int)D3D12.DXGI_FORMAT_R8G8B8A8_UNORM);
                Marshal.WriteInt32(p, 612, (int)D3D12.DXGI_FORMAT_D32_FLOAT);   // depth format
                Marshal.WriteInt32(p, 616, 1);      // sample count
                Guid iid = D3D12.IID_ID3D12PipelineState;
                Com.Check(Com.Method<CreateGraphicsPipelineStateFn>(device, 10)(device, p, ref iid, out Pipeline), "CreateGraphicsPipelineState");
            }
            finally { Marshal.FreeHGlobal(p); Com.Release(ref vs); Com.Release(ref ps); }
        }

        private static IntPtr Compile(string entry, string target)
        {
            byte[] src = Encoding.ASCII.GetBytes(Hlsl);
            IntPtr code, err;
            int hr = D3DCompile(src, (UIntPtr)src.Length, "billboard", IntPtr.Zero, IntPtr.Zero, entry, target, 1u << 15, 0, out code, out err);
            if (hr < 0) throw new InvalidOperationException("Shader " + entry + ": " + BlobText(err));
            Com.Release(ref err);
            return code;
        }

        private static IntPtr BlobPtr(IntPtr b) { return Com.Method<BlobPtrFn>(b, 3)(b); }
        private static UIntPtr BlobSize(IntPtr b) { return Com.Method<BlobSizeFn>(b, 4)(b); }
        private static string BlobText(IntPtr b)
        {
            if (b == IntPtr.Zero) return "(no details)";
            string s = Marshal.PtrToStringAnsi(BlobPtr(b), (int)BlobSize(b).ToUInt64());
            Com.Release(ref b); return s;
        }

        public void Dispose() { Com.Release(ref Pipeline); Com.Release(ref RootSignature); }
    }
}
