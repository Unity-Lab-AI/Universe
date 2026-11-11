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
    float4 right;   // xyz camera right; w: a hole's parts, how long they flew with gravity off (0: still flying, webA.w)
    float4 up;      // xyz camera up; w: a disc galaxy's turn so far, the distance at its rim speed (its own units; 0 none)
    float4 misc;    // x time, y size scale, z style (0 glow, otherwise faceted)
    float4 screen;  // x width, y height (of this tile, in pixels), z minimum size per unit of distance, w maximum (0: none)
    float4 extra;   // x: how much dimmer (0 full bright, 1 gone); y: a hole's inner orbit pace; z: gravity off, how far each galaxy star has flown (0: never off); w: which part of a hole
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
// a body carried on under one mass's pull mu from its place r and velocity v for a time dt (the universal variable, Stumpff's C and S,
// Newton on the universal Kepler equation, the Lagrange f and g: as src/core/Kepler.cs), its new place
float kstC(float z) { return z > 1e-4 ? (1 - cos(sqrt(z))) / z : z < -1e-4 ? (cosh(sqrt(-z)) - 1) / -z : 0.5 - z / 24; }
float kstS(float z) { if (z > 1e-4) { float q = sqrt(z); return (q - sin(q)) / (q * q * q); } if (z < -1e-4) { float q = sqrt(-z); return (sinh(q) - q) / (q * q * q); } return 1.0 / 6 - z / 120; }
float3 keplerOn(float mu, float3 r, float3 v, float dt)
{
    float r0 = max(1e-6, length(r)), vr0 = dot(r, v) / r0, alpha = 2 / r0 - dot(v, v) / mu, smu = sqrt(mu);
    float chi = alpha > 0 ? smu * alpha * dt : smu * dt / r0;
    for (int it = 0; it < 24; it++)
    {
        float z = alpha * chi * chi, c = kstC(z), sv = kstS(z);
        float F = r0 * vr0 / smu * chi * chi * c + (1 - alpha * r0) * chi * chi * chi * sv + r0 * chi - smu * dt;
        float dF = r0 * vr0 / smu * chi * (1 - alpha * chi * chi * sv) + (1 - alpha * r0) * chi * chi * c + r0;
        chi -= F / max(1e-6, dF);
    }
    float zz = alpha * chi * chi; float f = 1 - chi * chi / r0 * kstC(zz), g = dt - chi * chi * chi / smu * kstS(zz);
    return f * r + g * v;
}
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
    if (up.w != 0 && extra.y == 0)
    {
        // a disc galaxy turning (the flat rotation curve its dark matter gives): each star round its middle at the rim speed, so by an
        // angle of the distance travelled over its radius (up.w that distance, in the galaxy's own units), the inner ones round
        // faster and the arms winding; less the turn of where you are (right.w your radius), as you ride round with your own stars
        float rr = max(1e-4, length(s.pos.xz)), an = up.w / rr - (right.w > 0 ? up.w / right.w : 0), ca = cos(an), sa = sin(an);
        s.pos.xz = float2(s.pos.x * ca - s.pos.z * sa, s.pos.x * sa + s.pos.z * ca);
    }
    if (extra.z != 0)
    {
        // gravity switched off: a galaxy's stars fly off on their tangents at the speed they circled at (a disc), or every way
        // (a round galaxy): each point's own straight line, so far along it by now (extra.z the distance in the galaxy's own
        // frame, negative for a round galaxy)
        float dd = abs(extra.z); float3 tv;
        if (extra.z > 0) { float rr = length(s.pos.xz); tv = rr > 1e-5 ? float3(-s.pos.z, 0, s.pos.x) / rr : float3(0, 0, 0); }
        else { uint kk = asuint(s.pos.x) ^ (asuint(s.pos.y) * 7919u) ^ (asuint(s.pos.z) * 104729u); tv = normalize(float3(oh01(kk, 1u), oh01(kk, 2u), oh01(kk, 3u)) * 2 - 1); }
        s.pos += tv * dd;
    }
    bool webOut = false; float orbR = -1, nebSprite = -1;   // (orbR: an orbital's or an atom's reach, for keeping its light by its size on screen)
    if (lensB.w > 0)
    {
        // a point of the web's light: left out near you (the galaxies made in full stand there) and past three fade distances;
        // fading with distance otherwise (seen through the whole web at once it would stack into an even dust)
        float wl = length(s.pos - webA.xyz);
        webOut = wl < webA.w || wl > lensB.w * 7;   // (out where its light has fallen to 4 e^-7, under half a percent: no edge, where at three fade distances it had been cut at a fifth of full)
        s.color.rgb *= 4.0 * exp(-wl / lensB.w) * (wl >= webA.w ? 1.0 : 0.0);   // (from where the galaxies made in full end, at one distance: no fade; its light matched to theirs there, UniverseView.WebMatch)
        s.color.rgb *= lerp(float3(1, 1, 1), float3(1.0, 0.45, 0.25), saturate(wl / (lensB.w * 3)));   // the farthest light comes reddened: the space it crossed was stretching as it came (the expansion of space), redder the farther, out to your horizon   // faded in over half again past where it starts: just where the galaxies made in full fade out, so the two never show at once                        // each point a galaxy's whole light, bright enough that the web's strands read across hundreds of millions of light-years
    }
    if (lensB.y > 0.5)
    {
        // an electron's orbital, worked out here on the card: one of its points, where the electron may be found, in the
        // orbital's real shape about its shell's distance, sampled afresh a few times a second (it shimmers: a wave, not a dot)
        uint code = (uint)(s.color.a * 64 + 0.5), l = code / 8u, m = code % 8u;
        uint key = asuint(s.pos.x) ^ (asuint(s.pos.y) * 7919u) ^ (asuint(s.pos.z) * 104729u) ^ (code * 31u);
        uint tick = 0;                                                    // (a steady cloud: sampled once, never flickering like static)
        float3 dir = float3(0, 1, 0); float best = -1;
        if (l == 6u)
        {
            // a nebula, grown here from its own seed (pos.x's bits; its record at the cloud's middle, its radius 1): a planetary
            // nebula's thin shell (m 4), a supernova remnant's filaments (5), an emission, reflection or dark cloud's knots (2, 3, 6);
            // each point one of its many, the same every frame. Records of one cloud share its seed, one for each part of its gas
            // each part (one ion of one element) its own points (its salt in pos.z: every part had sat on the very same points, one
            // even mix), its own knots for part of its gas besides the cloud's shared ones, and its zone (pos.y: 1 round the hot
            // middle, 2 the edges, 0 throughout)
            uint salt = asuint(s.pos.z) * 2654435761u; float zone = s.pos.y;
            uint ns0 = asuint(s.pos.x), ns = ns0, pt = sub + salt; float br = 1; float3 q = float3(0, 0, 0);
            float g1 = sqrt(-2 * log(max(1e-6, oh01(ns + pt, 21u)))) * cos(6.2832 * oh01(ns + pt, 22u));
            float g2 = sqrt(-2 * log(max(1e-6, oh01(ns + pt, 23u)))) * cos(6.2832 * oh01(ns + pt, 24u));
            float g3 = sqrt(-2 * log(max(1e-6, oh01(ns + pt, 25u)))) * cos(6.2832 * oh01(ns + pt, 26u));
            float uu = oh01(ns + pt, 27u) * 2 - 1, aa = oh01(ns + pt, 28u) * 6.2832, ss2 = sqrt(1 - uu * uu); float3 dv = float3(ss2 * cos(aa), uu, ss2 * sin(aa));
            if (m == 4u)
            {
                float squash = 0.6 + 0.4 * oh01(ns, 1u), rr = 0.75 + 0.22 * oh01(ns + pt, 29u) + 0.05 * g1;
                if (oh01(ns + pt, 30u) < 0.19) { rr = sqrt(oh01(ns + pt, 31u)) * 0.7; br = 0.4; } else br = rr > 0.9 ? 1 : 0.7;
                rr *= zone > 0.5 && zone < 1.5 ? 0.6 : zone > 1.5 ? 1.08 : 1;          // (the high ions inside the shell, the low ones at its rim)
                q = float3(dv.x * rr, dv.y * rr * squash, dv.z * rr);
            }
            else if (m == 5u)
            {
                uint fi = (uint)(oh01(ns + pt, 32u) * 40); float fu = oh01(ns + fi, 33u) * 2 - 1, fa = oh01(ns + fi, 34u) * 6.2832, fs = sqrt(1 - fu * fu);
                float3 f0 = float3(fs * cos(fa), fu, fs * sin(fa)); float rr = 0.85 + 0.15 * oh01(ns + pt, 35u);
                if (oh01(ns + pt, 36u) < 0.14) { q = dv * pow(oh01(ns + pt, 37u), 0.4); br = 0.35; } else q = normalize(f0 * 0.75 + dv * 0.35) * rr;
                if (zone > 0.5 && zone < 1.5) q *= 0.6;                                    // (the high ions inside, the low ones in the filaments)
            }
            else
            {
                uint kq = (uint)(oh01(ns + pt, 38u) * 28); float3 kc = float3(0, 0, 0);
                if (oh01(ns + pt, 49u) < 0.4) ns = ns0 ^ salt;                            // (two in five of its points in its own knots)
                float k1 = sqrt(-2 * log(max(1e-6, oh01(ns + kq, 41u)))) * cos(6.2832 * oh01(ns + kq, 42u)), k2 = sqrt(-2 * log(max(1e-6, oh01(ns + kq, 43u)))) * cos(6.2832 * oh01(ns + kq, 44u)), k3 = sqrt(-2 * log(max(1e-6, oh01(ns + kq, 45u)))) * cos(6.2832 * oh01(ns + kq, 46u));
                kc = float3(k1 * 0.45, k2 * 0.3, k3 * 0.45); float ksz = 0.12 + 0.3 * oh01(ns + kq, 47u);
                q = kc + float3(g1, g2, g3) * ksz; float cl = length(q);
                ns = ns0;
                if (cl > 1 || (zone > 0.5 && zone < 1.5 && cl > 0.5) || (zone > 1.5 && cl < 0.4)) webOut = true;   // (outside the cloud; the high ions kept to its middle, the low ones to its edges)
                br = (1 - cl * 0.5) * (m == 6u ? 0.45 : 1);
            }
            br *= 0.35 + 0.45 * oh01(ns + pt, 48u);
            orbR = abs(s.size); nebSprite = abs(s.size);                                  // (its points' own size, a share of its radius)
            s.pos = q; s.size = abs(s.size); s.color.rgb *= br; s.color.a = 1; best = 3;
        }
        if (l == 7u)
        {
            // an atom's electron density (the one model at every size): its electrons where they may be found, every way alike,
            // densest a little out from the nucleus and thinning outward (r^2 e^(-2r/a), a gamma of three), out past the atom's
            // radius, so in a solid each atom's outer electrons overlap its neighbours' into one sea of them
            float u1 = max(1e-6, oh01(key + sub * 13u, 11u)), u2 = max(1e-6, oh01(key + sub * 13u, 12u)), u3 = max(1e-6, oh01(key + sub * 13u, 13u));
            float th = acos(1 - 2 * oh01(key + sub * 13u, 14u)), ph = oh01(key + sub * 13u, 15u) * 6.2832;
            float rr = min(1.8, -log(u1 * u2 * u3) * 0.3);                   // (in shares of its reach: mean 0.9, a tail past it)
            orbR = s.size; s.pos += float3(sin(th) * cos(ph), cos(th), sin(th) * sin(ph)) * s.size * rr;
            s.size = 0; s.color.a = 1; best = 2;
        }
        for (uint t = 0; t < (l >= 6u ? 0u : 6u); t++)
        {
            float u = oh01(key + sub * 13u, tick * 7u + t), v = oh01(key + sub * 13u + 5u, tick * 7u + t);
            float th = acos(1 - 2 * u), ph = v * 6.2832; float3 d = float3(sin(th) * cos(ph), cos(th), sin(th) * sin(ph));
            float sc = olobe((int)l, (int)m, d) - oh01(key + sub, tick + t * 3u) * 3.2;
            if (sc > best) { best = sc; dir = d; }
            if (sc > 0) break;
        }
        if (l < 6u)   // (every orbital's point placed: a p or d lobe scores past 1.5 by itself, and the test on the score had left those points unplaced, each a disc the whole shell wide on the nucleus)
        {
            float w = oh01(key + sub * 3u + 1u, tick);
            orbR = s.size; s.pos += dir * s.size * (0.45 + 1.1 * pow(w, 0.7));
            s.size = 0;
            s.color.a = 1;
        }
    }
    bool holeHidden = false;
    if (extra.y > 0)
    {
        // a feeding black hole's own things, in its own frame (its disc level, its horizon's radius 1), each worked out here
        // every frame: extra.y the pace of the inner orbit, extra.w which record this is (1 the disc's gas, 2 a star on its
        // orbit round the hole, 3 a star torn into a stream); webA.xyz where the camera is in that frame
        // with gravity switched off (webA.w: the watched seconds since), every point goes on from where it was at that moment at the
        // velocity it had there, straight (worked out from its own motion over a hundredth of a second): the disc's gas off its
        // circles, the swarm's stars off their ellipses, the torn star's stream flying apart
        float offS = webA.w, K = extra.y; Star sKeep = s, sAt = s; float3 pA = s.pos, pB = s.pos;
        for (int hp = 0; hp < (offS > 0 ? 2 : 1); hp++)
        {
        s = sKeep; float t = offS > 0 ? misc.x - offS + hp * 0.01 : misc.x;
            if (extra.w < 1.5)
            {
                // the disc's gas: each point on its own circular orbit, the inner ones round many times faster (Kepler: the rate
                // falls as r^1.5), so the disc shears into spiral streams and never turns as one plate
                float r = max(1.5, length(s.pos.xz)), a0 = atan2(s.pos.z, s.pos.x), ang = a0 + K * t / pow(r, 1.5);
                float3 p = float3(cos(ang) * r, s.pos.y, sin(ang) * r);
                // the gas moves at a good share of light speed (v/c = sqrt(1/(2r)) on a circular orbit, 0.4c at the inner edge), so
                // the side coming toward you is brightened and the side going away dimmed (Doppler beaming, as the Event Horizon
                // Telescope saw): the disc is always lopsided
                float3 tang = float3(-sin(ang), 0, cos(ang)); float beta = sqrt(0.5 / r);
                float3 toCam = normalize(webA.xyz - p); float gam = 1 / sqrt(1 - beta * beta), dop = 1 / (gam * (1 - beta * dot(tang, toCam)));
                s.color.rgb *= clamp(dop * dop * dop, 0.25, 4.0);     // (lopsided, as the Event Horizon Telescope saw it, but never a missing half)
                s.pos = p;
            }
            else if (extra.w < 2.5)
            {
                // a star on its own orbit round the hole (as the S stars round the Milky Way's heart, swept round at thousands of km a
                // second): its ellipse in its record (x the semi-major axis, y the eccentricity, z its phase, size its orientation),
                // Kepler's equation solved here, so it rushes through the close end and crawls through the far
                float a = s.pos.x, e = s.pos.y, M = s.pos.z + K * t * 3.0 / pow(a, 1.5);
                float E = M; for (int i = 0; i < 6; i++) E -= (E - e * sin(E) - M) / (1 - e * cos(E));
                float x = a * (cos(E) - e), z = a * sqrt(1 - e * e) * sin(E);
                uint seed = asuint(s.size); float inc = oh01(seed, 1u) * 1.4 - 0.7, node = oh01(seed, 2u) * 6.2832, peri = oh01(seed, 3u) * 6.2832;
                float cp = cos(peri), sp = sin(peri); float x1 = x * cp - z * sp, z1 = x * sp + z * cp;
                float ci = cos(inc), si = sin(inc); float y2 = z1 * si, z2 = z1 * ci;
                float cn = cos(node), sn = sin(node);
                s.pos = float3(x1 * cn - z2 * sn, y2, x1 * sn + z2 * cn); s.size = 0.9;
            }
            else
            {
                // a star torn apart: it came too close, and the hole's pull on its near side so outdid the pull on its far side that it was
                // stretched along its orbit into a stream of gas (a tidal disruption), winding in round the hole and feeding the disc,
                // hotter and bluer as it falls. Each point is a share of the way along the stream, and the stream flows: the star's
                // remains at its head, its gas drawn out behind and down
                float s0 = frac(s.pos.x - K * t * 0.0035), r = 3.2 + 90 * exp(-s0 * 3.4), ang = -s0 * 6.2832 * 2.4 + 0.9;   // (winding in the way the disc turns: its angle grows as it falls)
                float wide = 0.25 + 4 * s0 * s0;                                                   // (thin at the star, spreading as it winds in)
                float3 p = float3(cos(ang) * r, s.pos.y * wide, sin(ang) * r) + float3(-sin(ang), 0, cos(ang)) * s.pos.z * wide * 2;
                float head = exp(-s0 * 14);                                                        // (the star's remains: a bright knot at the head)
                s.color.rgb = lerp(float3(1, 0.85, 0.55), float3(0.7, 0.8, 1), s0) * s.color.r * (0.35 + 1.4 * s0 + 2.5 * head);
                s.size = s.size * (0.6 + 2 * s0) + head * 1.5; s.pos = p;
            }
        if (hp == 0) { sAt = s; pA = s.pos; } else pB = s.pos;
        }
        if (offS > 0)
        {
            // flown straight on while it was off; gravity back on (right.w how long it flew), from there each falls back under the hole's
            // pull on its new path from where it is at the speed it had (its pull: the disc's circles run at K / r^1.5, mu K squared; the
            // swarm's ellipses at 3K / a^1.5, mu nine K squared), and what falls inside the horizon is gone
            float flightS = right.w > 0 ? right.w : offS; s = sAt; float3 vel = (pB - pA) / 0.01; s.pos = pA + vel * flightS;
            if (right.w > 0 && offS > right.w && K > 0)
            {
                float mu = (extra.w > 1.5 && extra.w < 2.5 ? 9.0 : 1.0) * K * K; s.pos = keplerOn(mu, s.pos, vel, offS - right.w);
                if (length(s.pos) < 1) s.color.rgb = 0;
            }
        }
        {
            // the hole's own things seen past it: what lies within its shadow's cone is hidden by the shadow (the light from the far side
            // that reaches you round the hole is the background lens's work: its ring for light from well behind)
            float3 cam = webA.xyz; float rc = max(1.001, length(cam)); float3 toHole = -cam / rc;
            float3 rel = s.pos - cam; float along = dot(rel, toHole); float dz = along - rc;
            if (dz > 0)
            {
                // behind the hole: what lies within its shadow's cone as seen from here (the shadow's half-angle from where the camera
                // stands: sin^2 = (27/4)(rs/r)^2 (1 - rs/r), 2.6 radii across from afar, half the sky at the photon sphere) is not lost:
                // its light comes round the hole, and it shows bent over the shadow's rim, the deeper inside the farther over (the
                // disc's far side as an arc above the shadow and another below it, the look of a hole in its disc from the side)
                float s2 = 6.75 / (rc * rc) * max(0.0, 1.0 - 1.0 / rc); float th = rc >= 1.5 ? asin(sqrt(saturate(s2))) : 3.14159265 - asin(sqrt(saturate(s2)));
                float3 rd = normalize(rel); float cang = dot(rd, toHole);
                if (cang > cos(th))
                {
                    float ang = acos(saturate(cang)), ang2 = min(3.0, th * 1.03 + (th - ang) * 0.55);
                    float3 perp = rd - toHole * cang; float pl = length(perp);
                    if (pl < 1e-5) perp = normalize(cross(toHole, float3(0, 1, 0.001))); else perp /= pl;
                    float3 nd = toHole * cos(ang2) + perp * sin(ang2);
                    s.pos = cam + nd * length(rel); s.color.rgb *= 0.85;
                }
            }
        }
    }
    bool o0 = holeHidden;
    float2 c = corners[vid];
    float4 cc0 = mul(float4(s.pos, 1), viewProj);
    float size = max(s.size * misc.y, cc0.w * screen.z); // never smaller than screen.z of the view distance
    if (nebSprite > 0)
    {
        // a nebula's light kept by its size on screen: each point's own glow is a share of the cloud's radius; once that is smaller
        // than the smallest dot, the dot carries only the light of the glow it stands for (their sizes squared), so a far cloud
        // shrinks and dims together and is never a white ball
        float sp = nebSprite * misc.y, dotR2 = cc0.w * screen.z; s.color.rgb *= saturate((sp * sp) / max(1e-30, dotR2 * dotR2));
    }
    else if (orbR >= 0)
    {
        // a cloud's light kept by its size on screen: its points (lensB.z of them) share the light of its area, so a cloud smaller
        // than its points' own dots dims as its area does and never adds up to white, and close in it is as bright as ever
        float dotR = cc0.w * screen.z; s.color.rgb *= saturate(orbR * orbR / max(1e-30, lensB.z * dotR * dotR));
    }
    if (screen.w > 0) size = min(size, cc0.w * screen.w);   // and, when asked, never bigger than screen.w of it (points of light stay points up close)
    if (cc0.w <= 0) o0 = true;
    // never more than about a third of the screen out from its middle (thousands each covering the whole screen are more work than
    // the card may take in a frame): a glow that would reach past that is shrunk whole and dimmed with it, so a star you pass
    // close fades as it goes by. Pulling its corners in instead drew every near star as a sliver pointing at the middle of the
    // screen, a warp round one point as the view pulled out
    float dimBig = 1;
    if (!o0)
    {
        float4 cu = mul(float4(s.pos + up.xyz * size, 1), viewProj);
        float ext = abs(cu.y / max(1e-6, abs(cu.w)) - cc0.y / cc0.w);
        if (ext > 0.7) { float k = 0.7 / ext; size *= k; dimBig = k * k; }
    }
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
            float tE = lensA.z * saturate((cc0.w - lensA.w) / lensA.w);          // (the ring is for light from well behind the hole: what sits at the hole itself is bent next to nothing by this; its own bending is worked above)
            float r2 = (r + sqrt(r * r + 4 * tE * tE)) * 0.5;
            float2 nd = d / r * r2; nd.x /= lensB.x;
            o.pos.xy += (lensA.xy + nd - ndc) * o.pos.w;
        }
    }
    o.uv = c;
    o.col = s.color * (1 - extra.x) * dimBig;
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
