// DNA-AND · render · solid faceted ground: triangles lit by one star, every face flat-shaded (the Populous-like look).
// Uses the same root signature as the billboards: [0] 32 root constants at b0, [1] the vertex list at t0.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace DnaAnd.Render
{
    internal sealed class MeshPipeline : IDisposable
    {
        public IntPtr Pipeline;
        public readonly IntPtr RootSignature;

        [DllImport("d3dcompiler_47.dll", CharSet = CharSet.Ansi)]
        private static extern int D3DCompile(byte[] src, UIntPtr size, string name, IntPtr defines, IntPtr include,
            string entry, string target, uint flags1, uint flags2, out IntPtr code, out IntPtr errors);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate IntPtr BlobPtrFn(IntPtr self);   // 3
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate UIntPtr BlobSizeFn(IntPtr self); // 4
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateGraphicsPipelineStateFn(IntPtr self, IntPtr desc, ref Guid iid, out IntPtr pso); // 10

        // constants: c[0..15] viewProj, c[16..18] direction the starlight comes from, c[20..22] camera position,
        // c[24] time, c[25] fog distance (0 = no fog), c[26] kind of surface (0 ground, 1 air glow, 2 clouds, 3 water),
        // c[28..30] fog colour
        private const string Hlsl = @"
cbuffer C : register(b0)
{
    row_major float4x4 viewProj;
    float4 light;
    float4 cam;
    float4 misc;
    float4 fog;
    float4 sunCol;      // the starlight's colour on this ground (the star's colour through the air); a = 0: plain white
    float4 nightCol;    // the night side's light: half bright, in the colour of the air's glow and the starlight
    float4 detA;        // detail mode: grain-local position = view position * w + xyz (the grain's own frame, its radius 1)
    float4 detB;        // x: the grain's key, y: its radius in view units, z: steps on, w: 1 = work its detail out here per pixel
};
struct V { float3 pos; float glow; float4 col; };
StructuredBuffer<float> wave : register(t1);
// ── the waveform on the card: stored CDF 9/7 octaves (made once on the CPU, uploaded), mode-E past them ──
static const int WT = 128; static const int WSTORED = 12;
uint whash(uint a, uint b) { uint h = a * 747796405u + b * 2891336453u + 1u; h = ((h >> ((h >> 28u) + 4u)) ^ h) * 277803737u; return (h >> 22u) ^ h; }
float wh01(uint a, uint b) { return (whash(a, b) & 0xFFFFFF) / 16777216.0; }
float wmirror(float p) { float m = fmod(p, 2.0 * (WT - 1)); if (m < 0) m += 2.0 * (WT - 1); return m > WT - 1 ? 2.0 * (WT - 1) - m : m; }
float wtile(uint k, float u, float v)
{
    u = wmirror(u * WT); v = wmirror(v * WT);
    int x0 = (int)u, y0 = (int)v, x1 = min(WT - 1, x0 + 1), y1 = min(WT - 1, y0 + 1); float fx = u - x0, fy = v - y0;
    int b = (int)(k % 6) * WT * WT;
    float a0 = lerp(wave[b + y0 * WT + x0], wave[b + y0 * WT + x1], fx), a1 = lerp(wave[b + y1 * WT + x0], wave[b + y1 * WT + x1], fx);
    return lerp(a0, a1, fy);
}
// mode-E past the stored octaves: Newton on z^3 - K, then cellular octaves that subdivide as the zoom rises
float wcell(float x, float y, float freq, float seed)
{
    float sx = x * freq, sy = y * freq, gx = floor(sx), gy = floor(sy), d1 = 9;
    for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
    {
        float cx = gx + ox, cy = gy + oy;
        float fx = frac(sin((cx + seed) * 127.1 + cy * 311.7) * 43758.5453), fy = frac(sin(cx * 127.1 + (cy + seed + 19.3) * 311.7) * 43758.5453);
        float dx = cx + fx - sx, dy = cy + fy - sy; d1 = min(d1, dx * dx + dy * dy);
    }
    return min(1, sqrt(d1));
}
float wmodeE(float X0, float Y0, float zoom, float seed, float kb, float ang)
{
    float K = 3 + kb * 2.4, ca = cos(ang), sa = sin(ang), X = X0 * ca - Y0 * sa, Y = X0 * sa + Y0 * ca, wf = 1.3 + log2(max(1, zoom)) * 0.05, ph = seed * 6.2832;
    float zr = X + 0.22 * sin(Y * wf + ph), zi = Y + 0.22 * cos(X * wf - ph); int it = 0;
    for (; it < 12; it++)
    {
        float r2 = zr * zr - zi * zi, i2 = 2 * zr * zi, r3 = r2 * zr - i2 * zi, i3 = r2 * zi + i2 * zr;
        float fr = r3 - K, fi = i3, dr = 3 * r2, di = 3 * i2, den = dr * dr + di * di + 1e-9;
        float qr = (fr * dr + fi * di) / den, qi = (fi * dr - fr * di) / den; zr -= qr; zi -= qi;
        if (qr * qr + qi * qi < 1e-6) break;
    }
    float bi = atan2(zi, zr) > 0 ? 1 : 0, c = 0, amp = 1, f = max(1, zoom) * 0.5, tot = 0;
    for (int oc = 0; oc < 4; oc++) { c += amp * wcell(X0, Y0, f, bi); tot += amp; amp *= 0.5; f *= 3.16227766; }
    return (0.4 + 0.6 * (it / 24.0)) * (0.45 + 0.9 * pow(c / tot, 0.55));
}
float woctave(float3 p, uint key, uint k)
{
    float sum = 0;
    for (uint pl = 0; pl < 3; pl++)
    {
        float u = pl == 0 ? p.x : pl == 1 ? p.y : p.z, v = pl == 0 ? p.y : pl == 1 ? p.z : p.x;
        float a = wh01(key + pl * 31u, k) * 6.2832, c = cos(a), s = sin(a);
        float uu = u * c - v * s + wh01(key + pl * 31u, k + 97u) * 37, vv = u * s + v * c + wh01(key + pl * 31u, k + 191u) * 41;
        if (k < (uint)WSTORED) sum += wtile(k + pl, uu * 0.02, vv * 0.02);
        else sum += (wmodeE(fmod(uu * 0.02, 3) - 1.5, fmod(vv * 0.02, 3) - 1.5, exp2(k - WSTORED + 1), wh01(key, k + 5u), wh01(key, k + 7u), wh01(key, k + 9u) * 6.28) - 0.75) * 1.4;
    }
    return sum / 3;
}
// octaves from frequency f0, each twice as fine and gain as strong, as fine as maxF (the finest the pixel can show), the
// finest fading in, never switching
// ── the same detail with the place in double precision, so it stays sharp at any zoom: a float only resolves a grain to about
// a ten-millionth of its size (tens of nanometres on a sand grain), so below that every pixel would read the same place and the
// detail would freeze into flat colour; the place is kept in doubles and brought into each octave's own repeat before floats ──
float wreduce(double x, float P)
{
    float invP = 1.0 / P;
    double r = x - (double)floor((float)(x * (double)invP)) * (double)P;          // near: off by at most a few thousand repeats
    r = r - (double)floor((float)(r * (double)invP)) * (double)P;                  // exact
    float rf = (float)r; if (rf < 0) rf += P; if (rf >= P) rf -= P; return rf;
}
float wtileR(uint k, float mu, float mv)
{
    mu = mu > WT - 1 ? 2.0 * (WT - 1) - mu : mu; mv = mv > WT - 1 ? 2.0 * (WT - 1) - mv : mv;
    int x0 = (int)mu, y0 = (int)mv, x1 = min(WT - 1, x0 + 1), y1 = min(WT - 1, y0 + 1); float fx = mu - x0, fy = mv - y0;
    int b = (int)(k % 6) * WT * WT;
    float a0 = lerp(wave[b + y0 * WT + x0], wave[b + y0 * WT + x1], fx), a1 = lerp(wave[b + y1 * WT + x0], wave[b + y1 * WT + x1], fx);
    return lerp(a0, a1, fy);
}
float woctaveD(double3 p, uint key, uint k)
{
    float sum = 0;
    for (uint pl = 0; pl < 3; pl++)
    {
        double u = pl == 0 ? p.x : pl == 1 ? p.y : p.z, v = pl == 0 ? p.y : pl == 1 ? p.z : p.x;
        float a = wh01(key + pl * 31u, k) * 6.2832, c = cos(a), s = sin(a);
        double uu = u * (double)c - v * (double)s + (double)(wh01(key + pl * 31u, k + 97u) * 37), vv = u * (double)s + v * (double)c + (double)(wh01(key + pl * 31u, k + 191u) * 41);
        if (k < (uint)WSTORED) sum += wtileR(k + pl, wreduce(uu * (double)(0.02 * WT), 2.0 * (WT - 1)), wreduce(vv * (double)(0.02 * WT), 2.0 * (WT - 1)));
        else sum += (wmodeE(wreduce(uu * 0.02, 3) - 1.5, wreduce(vv * 0.02, 3) - 1.5, exp2(k - WSTORED + 1), wh01(key, k + 5u), wh01(key, k + 7u), wh01(key, k + 9u) * 6.28) - 0.75) * 1.4;
    }
    return sum / 3;
}
float woctavesD(double3 p, uint key, float f0, float amp0, float gain, float maxF)
{
    float sum = 0, amp = amp0, f = f0;
    for (uint k = 0; k < 40; k++)
    {
        float fine = maxF / f; if (fine < 0.5) break;
        sum += min(1, fine - 0.5) * amp * woctaveD(p * (double)f, key, k);
        amp *= gain; f *= 2;
    }
    return sum;
}
// the ground's grains on the card: the same 32-bit hash and cells as the processor's grain field, so the very same grains
static const int GPALB = 6 * WT * WT, GPALM = 16, GPALS = 2 + 16 + 48;
uint ghash(uint i, uint j, uint seedW) { uint h = i * 0x9E3779B1u ^ j * 0x85EBCA77u ^ seedW; h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16; return h; }
float g01(uint key, uint j, uint seedW) { return (ghash(key, j, seedW) & 0xFFFFFF) / 16777216.0; }
float woctaves(float3 p, uint key, float f0, float amp0, float gain, float maxF)
{
    float sum = 0, amp = amp0, f = f0;
    for (uint k = 0; k < 40; k++)
    {
        float fine = maxF / f; if (fine < 0.5) break;
        sum += min(1, fine - 0.5) * amp * woctave(p * f, key, k);
        amp *= gain; f *= 2;
    }
    return sum;
}
StructuredBuffer<V> verts : register(t0);
struct VO { float4 pos : SV_Position; float3 wp : TEXCOORD0; float4 col : COLOR0; float glow : TEXCOORD1; float4 sn : TEXCOORD2; };

VO VS(uint vid : SV_VertexID)
{
    V v = verts[vid];
    VO o;
    o.pos = mul(float4(v.pos, 1), viewProj);
    o.wp = v.pos; o.col = v.col; o.glow = v.glow; o.sn = float4(0, 0, 0, 0);
    if (v.glow < -1)
    {
        // a smooth surface: this corner's own direction (packed in two 12-bit halves), unpacked here so it blends across
        // the triangle as a direction (blending the packed number itself would be noise)
        uint code = (uint)(-v.glow - 2 + 0.5);
        float2 e = float2((code & 4095) / 4095.0, (code >> 12) / 4095.0) * 2 - 1;
        float3 n = float3(e.x, 1 - abs(e.x) - abs(e.y), e.y);
        if (n.y < 0) { float2 t = (1 - abs(n.zx)) * float2(n.x >= 0 ? 1 : -1, n.z >= 0 ? 1 : -1); n.x = t.x; n.z = t.y; }
        o.sn = float4(normalize(n), 1); o.glow = 0;
    }
    return o;
}

float4 PS(VO i) : SV_Target
{
    // flat facets: the face direction from how the position changes across the triangle
    float3 cr = cross(ddx(i.wp), ddy(i.wp));
    float cl = length(cr);
    float3 n = cl > 1e-30 ? cr / cl : light.xyz;      // a tiny or broken triangle: treat it as facing the light
    if (i.sn.w > 0.5 && dot(i.sn.xyz, i.sn.xyz) > 1e-6) n = normalize(i.sn.xyz);   // a smooth surface: its corners' directions, blended
    float3 toCam = cam.xyz - i.wp;
    if (dot(n, toCam) < 0) n = -n;
    float3 v = normalize(toCam);
    if (misc.w > 0.5)
    {
        // shells: the inner land shows only from inside, the hull only from outside, so the two never fight on screen
        float3 radial = float3(light.w, cam.w, fog.w);
        float outward = dot(v, radial);
        if (misc.w < 1.5 && outward > 0.02) discard;
        if (misc.w > 1.5 && outward < -0.02) discard;
    }
    if (misc.z > 0.5 && misc.z < 1.5)
    {
        // the air: a glowing shell, brightest where you look through the most of it (the edge), only on the lit side
        float3 up = normalize(i.wp);
        float edge = 1 - saturate(abs(dot(up, v)));
        float day = saturate(dot(up, light.xyz) * 1.6 + 0.35);
        float a = (pow(edge, 3) * 1.1 + 0.08) * day;
        return float4(i.col.rgb * a, 1);
    }
    float3 stain = float3(1, 1, 1);
    if (nightCol.a < 0.5 && detB.w > 0.5)
    {
        // the grain's face, worked out here per pixel (never on the processor): its roughness from the waveform, as fine as
        // this pixel can show, bending the light; its stains and cracks
        // where this pixel is on the grain, in its own frame (radius 1), in double precision: the part the camera's place gives
        // (high and low halves) and the pixel's own small offset
        double3 L = (double3)i.wp * (double)detA.w + (double3)detA.xyz + double3(light.w, cam.w, fog.w); uint key = asuint(detB.x);
        float fw = max(1e-15, length(fwidth(i.wp)) * detA.w), maxF = 0.5 / fw;        // this pixel's size on the grain (from the view, exact)
        float h = woctavesD(L, key, 2.5, 0.16, 0.5, maxF) * detB.y;                    // in view units
        float3 dpdx = ddx(i.wp), dpdy = ddy(i.wp); float dhx = ddx(h), dhy = ddy(h);
        float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx); float det = dot(dpdx, r1);
        if (abs(det) > 1e-20)
        {
            // the bump's tilt, never more than about 50 degrees: where a pixel block straddles two triangles its slopes are wild,
            // and an untamed tilt there throws a sharp glint along every triangle edge (lines crawling as you move)
            float3 gv = sign(det) * (dhx * r1 + dhy * r2) / abs(det); float gl = length(gv); if (gl > 1.2) gv *= 1.2 / gl;
            n = normalize(n - gv);
        }
        float lim = min(maxF * 0.8, 5e5);
        float iron = woctavesD(L, key * 7u + 300u, 2.0, 1, 0.6, lim), clay = woctavesD(L + double3(9, 0, -4), key * 7u + 400u, 2.0, 1, 0.6, lim);
        float crk = woctavesD(L * 1.7, key * 7u + 500u, 2.0, 1, 0.6, lim), dark = clamp(0.5 - abs(crk) * 3, 0, 0.6);
        float ir = clamp(iron / 2.4 * 2.4 - 0.15, 0, 0.6), cl = clamp(clay / 2.4 * 2.4 - 0.2, 0, 0.5);
        stain = float3(1 + ir * 0.25 + cl * 0.3, 1 - ir * 0.35 + cl * 0.3, 1 - ir * 0.55 + cl * 0.25) * (1 - dark);
    }
    float3 baseCol = i.col.rgb;
    if (nightCol.a > 0.5 && misc.z < 0.5 && misc.w < 0.5)
    {
        // the ground's sand, grain by grain, worked out here per pixel (finer than the ground's corners can carry): which grain
        // tops the bed under this pixel, by the same hash and cells as the processor's grain field; its mineral, its shade, its
        // rounded top bending the light; a gap between grains in shadow. Coming in as the grains grow big enough to see.
        float invG = detA.w, pix = length(fwidth(i.wp)) * invG;
        float gw = saturate((0.25 - pix) / 0.15);
        if (gw > 0)
        {
            float3 up = float3(light.w, cam.w, fog.w); uint seedW = asuint(detB.w); uint3 base = uint3(asuint(detB.x), asuint(detB.y), asuint(detB.z));
            float3 q = i.wp * invG + detA.xyz; int3 c0 = (int3)floor(q);
            int pb = GPALB + ((int)(nightCol.a + 0.5) - 1) * GPALS; int nm = (int)wave[pb];
            float best = -1e9; bool any = false; float3 bc = 0, bn = up;
            for (int dz = -1; dz <= 1; dz++) for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
            for (uint layer = 0; layer < 2; layer++)
            {
                int3 cc = c0 + int3(dx, dy, dz); uint3 u = base + (uint3)cc;
                uint key = (u.x * 73856093u ^ u.y * 19349663u ^ u.z * 83492791u) * 16u + layer;
                float3 p = float3(cc) + 0.2 + 0.6 * float3(g01(key, 1, seedW), g01(key, 2, seedW), g01(key, 3, seedW));
                if (abs(dot(p, up) + misc.x) > 0.5) continue;
                float rad = 0.3 + 0.2 * g01(key, 4, seedW), depth = layer == 0 ? rad * (0.55 + 0.3 * g01(key, 5, seedW)) : 0.75 + rad * 0.6;
                float3 o = q - p, ot = o - up * dot(o, up); float d2 = dot(ot, ot);
                if (d2 >= rad * rad) continue;
                float hz = sqrt(rad * rad - d2), ty = hz - depth;
                if (ty > best)
                {
                    best = ty; any = true;
                    float t = g01(key, 6, seedW); int mi = max(0, nm - 1);
                    for (int m = 0; m < GPALM; m++) if (m < nm && t <= wave[pb + 2 + m]) { mi = m; break; }
                    float shd = 0.75 + 0.35 * g01(key, 7, seedW);
                    bc = float3(wave[pb + 2 + GPALM + mi * 3], wave[pb + 3 + GPALM + mi * 3], wave[pb + 4 + GPALM + mi * 3]) * shd;
                    bn = normalize(ot + up * hz);
                }
            }
            baseCol = lerp(i.col.rgb, any ? bc : i.col.rgb * 0.25, gw);
            if (any) n = normalize(lerp(n, bn, gw));
        }
    }
    float sun = saturate(dot(n, light.xyz));
    float lit = 0.5 + 0.5 * sun;                                  // a god sees by night: the night side is about half as bright, never black
    float3 sc = sunCol.a > 0 ? sunCol.rgb : float3(1, 1, 1), nc = sunCol.a > 0 ? nightCol.rgb : float3(0.5, 0.5, 0.5);
    float3 lit3 = lerp(nc, sc, sun);                              // by day the star's own light through the air, by night the air's glow
    if (misc.z > 1.5 && misc.z < 2.5)
    {
        // clouds scatter light through themselves: lit from the star's side of the planet, glowing grey from below
        lit = 0.45 + 0.55 * saturate(abs(dot(n, light.xyz)));
    }
    float gl = max(i.glow, 0);
    float3 c = misc.z > 1.5 && misc.z < 2.5 ? i.col.rgb * max(lit, gl) * sc : baseCol * max(lit3, gl);
    c *= stain;
    float alpha = misc.z > 1.5 ? i.col.a : 1;
    if (misc.z < 0.5 && i.col.a < 0.995)
    {
        // a lustre (from the mineral's data): a = 0.9 vitreous, 0.8 metallic, 0.7 pearly, 0.6 resinous/waxy, 0.5 wet;
        // the clarity in the hundredths (0.0x clear, translucent, opaque by x = 2, 1, 0)
        float code = i.col.a; float lu = floor(code * 10 + 0.001) / 10; float clr = round((code - lu) * 100);
        float3 hv = normalize(light.xyz + v); float nh = saturate(dot(n, hv)), nv = saturate(dot(n, v));
        float fres = pow(1 - nv, 5);
        float shine = 0, power = 8; float3 spec = float3(1, 1, 1);
        if (lu > 0.85) { shine = 0.55; power = 90; }                                  // vitreous: a sharp glassy glint
        else if (lu > 0.75) { shine = 0.7; power = 40; spec = lerp(i.col.rgb, float3(1, 1, 1), 0.35); c *= 0.7; }   // metallic: the glint in its own colour, dark body
        else if (lu > 0.65) { shine = 0.35; power = 14; c += fres * 0.25 * sc; }      // pearly: a soft sheen across it
        else if (lu > 0.55) { shine = 0.3; power = 20; }                              // resinous, waxy: a soft greasy glint
        else if (lu > 0.45) { shine = 0.9; power = 140; }                             // wet: a mirror glint
        c += spec * sc * shine * pow(nh, power) * saturate(sun * 4) * 0.8;   // a glint, never blown out to white
        c += sc * fres * (lu > 0.75 && lu < 0.85 ? 0.15 : 0.35) * shine;               // the edge catches the light (glass and ice most)
        if (clr > 0.5)
        {
            // light through it: clear and translucent things glow softly from within on the side away from the light
            float through = saturate(-dot(n, light.xyz)) * (clr > 1.5 ? 0.45 : 0.25);
            c += i.col.rgb * sc * through + i.col.rgb * (clr > 1.5 ? 0.12 : 0.05);
        }
    }
    if (misc.z > 2.5)
    {
        // water: a sharp glint of starlight, and more see-through when looked at from straight above
        float3 r = reflect(-light.xyz, n);
        c += pow(saturate(dot(r, v)), 60) * 0.8 * saturate(dot(n, light.xyz) * 4);
        alpha = lerp(alpha, 0.97, pow(1 - saturate(abs(dot(n, v))), 4));
    }
    if (misc.y > 0)
    {
        float f = saturate(length(toCam) / misc.y);
        c = lerp(c, fog.rgb, f * f);
    }
    return float4(c, alpha);
}
";

        /// <summary><paramref name="blend"/>: 0 solid, 1 adds light (glowing air), 2 see-through (clouds, water).</summary>
        public MeshPipeline(IntPtr device, IntPtr rootSignature, int blend)
        {
            RootSignature = rootSignature;
            IntPtr vs = Compile("VS", "vs_5_0"), ps = Compile("PS", "ps_5_0");
            IntPtr p = Marshal.AllocHGlobal(656);
            try
            {
                for (int i = 0; i < 656; i++) Marshal.WriteByte(p, i, 0);
                Marshal.WriteIntPtr(p, 0, RootSignature);
                Marshal.WriteIntPtr(p, 8, BlobPtr(vs)); Marshal.WriteInt64(p, 16, (long)BlobSize(vs).ToUInt64());
                Marshal.WriteIntPtr(p, 24, BlobPtr(ps)); Marshal.WriteInt64(p, 32, (long)BlobSize(ps).ToUInt64());
                // blend: off (solid), every render target: no logic op, write all colours
                for (int k = 0; k < 8; k++)
                {
                    int rt = 128 + k * 40;
                    if (k == 0 && blend > 0)
                    {
                        Marshal.WriteInt32(p, rt, 1);
                        Marshal.WriteInt32(p, rt + 8, blend == 1 ? 2 : 5);     // ONE or SRC_ALPHA
                        Marshal.WriteInt32(p, rt + 12, blend == 1 ? 2 : 6);    // ONE or INV_SRC_ALPHA
                        Marshal.WriteInt32(p, rt + 16, 1);
                        Marshal.WriteInt32(p, rt + 20, 2); Marshal.WriteInt32(p, rt + 24, 2); Marshal.WriteInt32(p, rt + 28, 1);
                        Marshal.WriteInt32(p, rt + 32, 4); Marshal.WriteByte(p, rt + 36, 0xF);
                        continue;
                    }
                    Marshal.WriteInt32(p, rt + 8, 2); Marshal.WriteInt32(p, rt + 12, 1); Marshal.WriteInt32(p, rt + 16, 1);
                    Marshal.WriteInt32(p, rt + 20, 2); Marshal.WriteInt32(p, rt + 24, 1); Marshal.WriteInt32(p, rt + 28, 1);
                    Marshal.WriteInt32(p, rt + 32, 4); Marshal.WriteByte(p, rt + 36, 0xF);
                }
                Marshal.WriteInt32(p, 448, -1);     // sample mask
                Marshal.WriteInt32(p, 452, 3); Marshal.WriteInt32(p, 456, 1); Marshal.WriteInt32(p, 476, 1);   // solid, no culling, depth clip
                Marshal.WriteInt32(p, 496, 1); Marshal.WriteInt32(p, 500, blend == 0 ? 1 : 0); Marshal.WriteInt32(p, 504, blend == 0 ? 5 : 7);   // depth on (reversed: nearer is greater); only solid ground writes it
                for (int f = 0; f < 2; f++) { int b = 516 + f * 16; Marshal.WriteInt32(p, b, 1); Marshal.WriteInt32(p, b + 4, 1); Marshal.WriteInt32(p, b + 8, 1); Marshal.WriteInt32(p, b + 12, 8); }
                Marshal.WriteInt32(p, 572, 3);      // triangles
                Marshal.WriteInt32(p, 576, 1);
                Marshal.WriteInt32(p, 580, (int)D3D12.DXGI_FORMAT_R8G8B8A8_UNORM);
                Marshal.WriteInt32(p, 612, (int)D3D12.DXGI_FORMAT_D32_FLOAT);
                Marshal.WriteInt32(p, 616, 1);
                Guid iid = D3D12.IID_ID3D12PipelineState;
                int hr = Com.Method<CreateGraphicsPipelineStateFn>(device, 10)(device, p, ref iid, out Pipeline);
                if (hr < 0 && !NoDoubles)
                {
                    // a card without double precision in its shaders: the same detail in single precision (sharp down to tens
                    // of nanometres on a sand grain, rather than to the atoms), never a game that will not start
                    NoDoubles = true; Com.Release(ref ps); ps = Compile("PS", "ps_5_0");
                    Marshal.WriteIntPtr(p, 24, BlobPtr(ps)); Marshal.WriteInt64(p, 32, (long)BlobSize(ps).ToUInt64());
                    hr = Com.Method<CreateGraphicsPipelineStateFn>(device, 10)(device, p, ref iid, out Pipeline);
                }
                Com.Check(hr, "CreateGraphicsPipelineState(mesh)");
            }
            finally { Marshal.FreeHGlobal(p); Com.Release(ref vs); Com.Release(ref ps); }
        }

        private static bool NoDoubles;
        private static IntPtr Compile(string entry, string target)
        {
            byte[] src = Encoding.ASCII.GetBytes(NoDoubles ? Hlsl.Replace("double", "float") : Hlsl);
            IntPtr code, err;
            int hr = D3DCompile(src, (UIntPtr)src.Length, "mesh", IntPtr.Zero, IntPtr.Zero, entry, target, 1u << 15, 0, out code, out err);
            if (hr < 0)
            {
                string why = err == IntPtr.Zero ? "(no details)" : Marshal.PtrToStringAnsi(BlobPtr(err), (int)BlobSize(err).ToUInt64());
                throw new InvalidOperationException("Mesh shader " + entry + ": " + why);
            }
            Com.Release(ref err);
            return code;
        }

        private static IntPtr BlobPtr(IntPtr b) { return Com.Method<BlobPtrFn>(b, 3)(b); }
        private static UIntPtr BlobSize(IntPtr b) { return Com.Method<BlobSizeFn>(b, 4)(b); }

        public void Dispose() { Com.Release(ref Pipeline); }
    }

    /// <summary>The three kinds of triangle drawing, ready to record: solid ground, glowing air, see-through clouds and water.</summary>
    internal sealed class MeshPipes : IDisposable
    {
        public readonly MeshPipeline SolidPipe, GlowPipe, ClearPipe;
        public readonly MeshDrawer Solid, Glow, Clear;
        public MeshPipes(Gpu gpu, IntPtr rootSignature)
        {
            SolidPipe = new MeshPipeline(gpu.Device, rootSignature, 0);
            GlowPipe = new MeshPipeline(gpu.Device, rootSignature, 1);
            ClearPipe = new MeshPipeline(gpu.Device, rootSignature, 2);
            Solid = new MeshDrawer(gpu.CommandList, SolidPipe); Glow = new MeshDrawer(gpu.CommandList, GlowPipe); Clear = new MeshDrawer(gpu.CommandList, ClearPipe);
        }
        public void Dispose() { SolidPipe.Dispose(); GlowPipe.Dispose(); ClearPipe.Dispose(); }
    }

    /// <summary>Records solid triangle draws into the frame's command list.</summary>
    internal sealed class MeshDrawer
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
        private readonly IntPtr _list; private readonly MeshPipeline _pipe;
        /// <summary>The stored waveforms on the card (Waveform.Upload), bound for every mesh draw.</summary>
        public static ulong WaveAddress;

        public MeshDrawer(IntPtr commandList, MeshPipeline pipeline)
        {
            _list = commandList; _pipe = pipeline;
            _pso = Com.Method<SetPsoFn>(_list, 25); _rs = Com.Method<SetRootSigFn>(_list, 30);
            _consts = Com.Method<SetConstantsFn>(_list, 36); _srv = Com.Method<SetRootSrvFn>(_list, 40);
            _topo = Com.Method<SetTopologyFn>(_list, 20); _vp = Com.Method<SetViewportsFn>(_list, 21);
            _sc = Com.Method<SetScissorsFn>(_list, 22); _draw = Com.Method<DrawInstancedFn>(_list, 12);
        }

        /// <summary>Draws vertexCount vertices starting at firstVertex of the buffer (one stone of many, its own constants).</summary>
        public void Draw(UploadBuffer buffer, int firstVertex, int vertexCount, float[] constants, int x, int y, int w, int h)
        {
            if (vertexCount <= 0) return;
            var vp = new Viewport { X = x, Y = y, W = w, H = h, MinZ = 0, MaxZ = 1 };
            var r = new Rect { L = x, T = y, R = x + w, B = y + h };
            _pso(_list, _pipe.Pipeline); _rs(_list, _pipe.RootSignature);
            _consts(_list, 0, BillboardPipeline.ConstantCount, constants, 0);
            _srv(_list, 1, buffer.GpuAddress);
            if (WaveAddress != 0) _srv(_list, 2, WaveAddress);
            _topo(_list, 4);
            _vp(_list, 1, ref vp); _sc(_list, 1, ref r);
            _draw(_list, (uint)vertexCount, 1, (uint)firstVertex, 0);
        }
        public void Draw(UploadBuffer buffer, int vertexCount, float[] constants, int x, int y, int w, int h)
        {
            if (vertexCount <= 0) return;
            var vp = new Viewport { X = x, Y = y, W = w, H = h, MinZ = 0, MaxZ = 1 };
            var r = new Rect { L = x, T = y, R = x + w, B = y + h };
            _pso(_list, _pipe.Pipeline); _rs(_list, _pipe.RootSignature);
            _consts(_list, 0, BillboardPipeline.ConstantCount, constants, 0);
            _srv(_list, 1, buffer.GpuAddress);
            if (WaveAddress != 0) _srv(_list, 2, WaveAddress);                          // the waveform, for detail worked out per pixel
            _topo(_list, 4);
            _vp(_list, 1, ref vp); _sc(_list, 1, ref r);
            _draw(_list, (uint)vertexCount, 1, 0, 0);
        }
    }
}
