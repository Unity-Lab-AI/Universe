// Universe · render · the front man's encoder on the graphics card: its compute shaders (our own, written from the VP9 format's rule
// text, the same rules as src/render/Vp9Writer.cs). The drawn frame is turned into its Y, Cb and Cr planes; each block's motion
// worked out from the camera's equations and refined; each superblock's blocks and transform sizes chosen; every transform block
// predicted, moved to frequencies, quantised and rebuilt with the format's own whole-number inverse, exactly as a player rebuilds it;
// the loop filter run as the player runs it. Only each block's decisions and its coefficients go back to the processor, which writes
// them into the file in order.
namespace Universe.Render
{
    internal static partial class CardVp9Shaders
    {
        public const string Hlsl = @"
cbuffer K : register(b0) { uint kWave; uint kA; uint cMiCols; uint cMiRows; uint cSbCols; uint cSbRows; uint cFw; uint cFh; uint cS; uint cSH; uint cKey; uint cDcq; uint cAcq; uint cBusy; uint cLambda; uint cFlow; uint cZoom; uint cVw; uint cVh; uint cSrcW; uint cSrcH; uint cSrow; uint cLvl; uint cLimit; uint cBlimit; uint cThresh; uint cTile; uint cOutCap; uint cFloor; uint cPad0; uint cPad1; uint cPad2; };   // (the frame's numbers: the same for every thread)
RWStructuredBuffer<uint> SRC : register(u0);   // source planes: Y (S x SH), Cb, Cr (S/2 x SH/2)
RWStructuredBuffer<uint> CUR : register(u1);   // this frame's picture as the player rebuilds it
RWStructuredBuffer<uint> REF : register(u2);   // the frame before's (its reference)
RWStructuredBuffer<uint> GRID : register(u3);  // each 8 x 8: info (bsl | tx << 2 | skip << 4 | coded tx << 5), motion (row << 16 | col, eighths)
RWStructuredBuffer<uint> MOT : register(u4);   // each 8 x 8's busyness; each 32 x 32's motion and difference; each 64's
RWStructuredBuffer<uint> STR : register(u5);   // each superblock's coefficients, in its own slot
RWStructuredBuffer<uint> LEN : register(u6);   // each superblock's length, its place in OUT, and the total
RWStructuredBuffer<uint> OUT : register(u7);   // every superblock's coefficients, one after another
RWStructuredBuffer<uint> TAB : register(u8);   // the scans, their inverses, cos64
RWStructuredBuffer<uint> P : register(u9);     // the frame's numbers

#define MICOLS cMiCols
#define MIROWS cMiRows
#define SBCOLS cSbCols
#define SBROWS cSbRows
#define FW cFw
#define FH cFh
#define S cS
#define KEY cKey
#define DCQ (int)cDcq
#define ACQ (int)cAcq
#define BUSYLIM asfloat(cBusy)
#define LAMBDA cLambda
#define FLOWON cFlow
#define ZOOM asfloat(cZoom)
#define VW cVw
#define VH cVh
#define SRCW cSrcW
#define SRCH cSrcH
#define SROW cSrow
#define LVL cLvl
#define LIMIT (int)cLimit
#define BLIMIT (int)cBlimit
#define THRESH (int)cThresh
#define TILELOG2 cTile
#define SH cSH
#define OUTCAP cOutCap
#define FLOOR cFloor
#define SLOT 6144

uint PlaneOff(uint p) { return p == 0 ? 0 : p == 1 ? S * SH : S * SH + (S / 2) * (SH / 2); }
uint PStride(uint p) { return p == 0 ? S : S / 2; }

// ── the frame's planes from the drawn picture (the same whole-number colour as Vp9Writer.Prepare) ──
[numthreads(8, 8, 1)]
void PREP(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= FW || id.y >= FH) return;
    uint oy = min(SRCH - 1, min(id.y, VH - 1) * SRCH / VH), ox = min(SRCW - 1, min(id.x, VW - 1) * SRCW / VW);
    uint px = SRC[S * SH + (S / 2) * (SH / 2) * 2 + oy * SROW + ox];
    int r = px & 255, g = (px >> 8) & 255, b = (px >> 16) & 255;
    SRC[id.y * S + id.x] = (uint)(16 + ((16829 * r + 33039 * g + 6416 * b + 32768) >> 16));
    if ((id.x & 1) == 0 && (id.y & 1) == 0)
    {
        uint co = (id.y / 2) * (S / 2) + id.x / 2;
        SRC[PlaneOff(1) + co] = (uint)(128 + ((-9714 * r - 19070 * g + 28784 * b + 32768) >> 16));
        SRC[PlaneOff(2) + co] = (uint)(128 + ((28784 * r - 24103 * g - 4681 * b + 32768) >> 16));
    }
}

// ── each 8 x 8's busyness: its luma's variance ──
[numthreads(64, 1, 1)]
void BUSY(uint3 id : SV_DispatchThreadID)
{
    uint n = MICOLS * MIROWS; if (id.x >= n) return;
    uint r = id.x / MICOLS, c = id.x - r * MICOLS; uint o = r * 8 * S + c * 8; int sum = 0, sq = 0;
    [loop] for (uint i = 0; i < 8; i++) [loop] for (uint j = 0; j < 8; j++) { int a = SRC[o + i * S + j]; sum += a; sq += a * a; }
    MOT[id.x] = asuint((sq - sum * sum / 64.0) / 64.0);
}
bool Busy(uint r, uint c, uint bsl)
{
    uint n8 = 1u << bsl;
    [loop] for (uint y = 0; y < n8 && r + y < MIROWS; y++) [loop] for (uint x = 0; x < n8 && c + x < MICOLS; x++) if (asfloat(MOT[(r + y) * MICOLS + c + x]) > BUSYLIM) return true;
    return false;
}

// ── motion ──
// where the picture at (px, py) was in the frame before, from the two cameras (Vp9Writer.FlowAt, the same)
int2 Flow(float px, float py)
{
    if (FLOWON == 0) return int2(0, 0);
    float TanHalf = 0.4830550656165784, asp = VW / (float)VH, z = ZOOM;
    float nx = (px / VW * 2 - 1) / z, ny = (1 - py / VH * 2) / z, rx = nx * TanHalf * asp, ry = ny * TanHalf;
    float3 cx = float3(asfloat(P[14]), asfloat(P[15]), asfloat(P[16])), cy = float3(asfloat(P[17]), asfloat(P[18]), asfloat(P[19])), cz = float3(asfloat(P[20]), asfloat(P[21]), asfloat(P[22]));
    float3 qx = float3(asfloat(P[23]), asfloat(P[24]), asfloat(P[25])), qy = float3(asfloat(P[26]), asfloat(P[27]), asfloat(P[28])), qz = float3(asfloat(P[29]), asfloat(P[30]), asfloat(P[31]));
    float3 w = cx * rx + cy * ry + cz; float a = dot(w, qx), b = dot(w, qy), f = dot(w, qz);
    if (f <= 1e-6) return int2(0, 0);
    float ox = (a / f / (TanHalf * asp) * 0.5 + 0.5) * VW, oy = (0.5 - b / f / TanHalf * 0.5) * VH, dx = ox - px, dy = oy - py;
    if (abs(dx) > 255 || abs(dy) > 255) return int2(0, 0);
    return int2((int)round(dy / 2) * 2, (int)round(dx / 2) * 2);
}
// the luma's absolute difference, every other row, of the block at (x, y), n wide (its part in the frame), against the frame before
// moved by (dy, dx); the moved block always inside the frame (the search keeps it there)
uint Sad(uint x, uint y, uint n, int dy, int dx)
{
    uint w = min(n, FW - x), h = min(n, FH - y), s = 0;
    [loop] for (uint i = 0; i < h; i += 2)
    {
        uint so = (y + i) * S + x, ro = (uint)((int)(y + i) + dy) * S + (uint)((int)x + dx);
        [loop] for (uint j = 0; j < w; j++) s += (uint)abs((int)SRC[so + j] - (int)REF[ro + j]);
    }
    return s * 2;
}
bool Fits(uint x, uint y, uint n, int dy, int dx)
{
    int w = (int)min(n, FW - x), h = (int)min(n, FH - y);
    return (int)x + dx >= 0 && (int)y + dy >= 0 && (int)x + dx + w <= (int)FW && (int)y + dy + h <= (int)FH && abs(dy) <= 255 && abs(dx) <= 255;
}
groupshared uint gBest[64]; groupshared int gBy[64], gBx[64];
// a block's motion: the best of its guesses, then every whole pair of pixels within reach of it; a thread a share of the places
void SearchBlock(uint t, uint x, uint y, uint n, int2 guesses[4], int reach, out int2 best, out uint bestSad)
{
    uint bs = 0xFFFFFFFF; int2 b = int2(0, 0);
    if (t < 4) { int2 g = guesses[t]; g.x &= ~1; g.y &= ~1; if (Fits(x, y, n, g.x, g.y)) { uint s = Sad(x, y, n, g.x, g.y); bs = s; b = g; } }
    gBest[t] = bs; gBy[t] = b.x; gBx[t] = b.y; GroupMemoryBarrierWithGroupSync();
    if (t == 0) { [loop] for (uint k = 1; k < 4; k++) if (gBest[k] < gBest[0]) { gBest[0] = gBest[k]; gBy[0] = gBy[k]; gBx[0] = gBx[k]; } }
    GroupMemoryBarrierWithGroupSync();
    int cy = gBy[0], cx = gBx[0]; uint side = (uint)reach + 1, count = side * side;
    GroupMemoryBarrierWithGroupSync();
    bs = gBest[0]; b = int2(cy, cx);
    [loop] for (uint k = t; k < count; k += 64)
    {
        int dy = cy + ((int)(k / side) - reach / 2) * 2, dx = cx + ((int)(k % side) - reach / 2) * 2;
        if (!Fits(x, y, n, dy, dx)) continue;
        uint s = Sad(x, y, n, dy, dx); if (s < bs) { bs = s; b = int2(dy, dx); }
    }
    GroupMemoryBarrierWithGroupSync();
    gBest[t] = bs; gBy[t] = b.x; gBx[t] = b.y; GroupMemoryBarrierWithGroupSync();
    [unroll] for (uint st = 32; st > 0; st >>= 1) { if (t < st && gBest[t + st] < gBest[t]) { gBest[t] = gBest[t + st]; gBy[t] = gBy[t + st]; gBx[t] = gBx[t + st]; } GroupMemoryBarrierWithGroupSync(); }
    best = int2(gBy[0], gBx[0]); bestSad = gBest[0];
}
uint PackMv(int2 m) { return ((uint)(m.x * 8) << 16) | ((uint)(m.y * 8) & 0xFFFF); }   // (row, column) in eighths
int2 UnpackMv(uint v) { return int2(((int)v >> 16) / 8, ((int)(v << 16) >> 16) / 8); }
uint M32Base() { return MICOLS * MIROWS; }
uint M64Base() { return M32Base() + ((MICOLS + 3) / 4) * ((MIROWS + 3) / 4) * 2; }

// each 32 x 32's motion (a group each)
[numthreads(64, 1, 1)]
void MOTION32(uint3 gid : SV_GroupID, uint t : SV_GroupIndex)
{
    uint bc = (MICOLS + 3) / 4, br = gid.x / bc, bcol = gid.x - br * bc; uint x = bcol * 32, y = br * 32;
    bool live = y < FH && x < FW; GroupMemoryBarrierWithGroupSync();
    if (!live) { x = 0; y = 0; }
    uint g = (br * 4) * MICOLS + bcol * 4;
    int2 gs[4]; gs[0] = Flow(x + 16.0, y + 16.0); gs[1] = int2(0, 0); gs[2] = UnpackMv(GRID[g * 2 + 1]); gs[3] = gs[0];
    int2 b; uint s; SearchBlock(t, x, y, 32, gs, 16, b, s);
    if (t == 0 && live) { MOT[M32Base() + gid.x * 2] = PackMv(b); MOT[M32Base() + gid.x * 2 + 1] = s; }
}
// each 64's motion and whether its four quarters moving apart pays (a group each)
[numthreads(64, 1, 1)]
void MOTION64(uint3 gid : SV_GroupID, uint t : SV_GroupIndex)
{
    uint sr = gid.x / SBCOLS, sc = gid.x - sr * SBCOLS; uint x = sc * 64, y = sr * 64;
    uint bc = (MICOLS + 3) / 4, q0 = (sr * 2) * bc + sc * 2;
    int2 gs[4]; gs[0] = Flow(x + 32.0, y + 32.0); gs[1] = int2(0, 0); gs[2] = UnpackMv(MOT[M32Base() + q0 * 2]); gs[3] = UnpackMv(GRID[((sr * 8) * MICOLS + sc * 8) * 2 + 1]);
    int2 b; uint s; SearchBlock(t, x, y, 64, gs, 4, b, s);
    if (t == 0)
    {
        uint parts = 0; [loop] for (uint q = 0; q < 4; q++) { uint qr = sr * 2 + q / 2, qc = sc * 2 + (q & 1); if (qr * 32 < FH && qc * 32 < FW) parts += MOT[M32Base() + (qr * bc + qc) * 2 + 1]; }
        MOT[M64Base() + gid.x * 3] = PackMv(b); MOT[M64Base() + gid.x * 3 + 1] = s; MOT[M64Base() + gid.x * 3 + 2] = parts + LAMBDA * 40 < s ? 1 : 0;
    }
}

// ── the format's whole-number inverse transform (section 8.7), a row or a column to a thread ──
int Cos64(int angle) { int a = angle & 127; if (a <= 32) return (int)TAB[2720 + a]; if (a <= 64) return -(int)TAB[2720 + 64 - a]; if (a <= 96) return -(int)TAB[2720 + a - 64]; return (int)TAB[2720 + 128 - a]; }
int Sin64(int angle) { return Cos64(angle - 32); }
void Bf(inout int T[32], int a, int b, int angle, bool flip)
{
    int x = T[a] * Cos64(angle) - T[b] * Sin64(angle), y = T[a] * Sin64(angle) + T[b] * Cos64(angle);
    int ra = (x + 8192) >> 14, rb = (y + 8192) >> 14; if (flip) { T[a] = rb; T[b] = ra; } else { T[a] = ra; T[b] = rb; }
}
void Hf(inout int T[32], int a, int b, bool flip) { if (flip) { int s = a; a = b; b = s; } int x = T[a], y = T[b]; T[a] = x + y; T[b] = x - y; }
int Brev(int bits, int x) { int t = 0; for (int i = 0; i < bits; i++) t |= ((x >> i) & 1) << (bits - 1 - i); return t; }
void Idct(inout int T[32], int n)
{
    Bf(T, 0, 1, 16, true);
    [loop] for (int m = 2; m <= n; m++)
    {
        int n0 = 1 << m, n1 = n0 >> 1, n2 = n0 >> 2, n3 = n0 >> 3, i, j;
        if (m > 2) { }   // (step 1 for m is the whole transform of m - 1, done by the turns before)
        [loop] for (i = 0; i < n2; i++) Bf(T, n1 + i, n0 - 1 - i, 32 - Brev(5, n1 + i), false);
        if (m >= 3) [loop] for (i = 0; i < n3; i++) [loop] for (j = 0; j < 2; j++) Hf(T, n1 + 4 * i + 2 * j, n1 + 1 + 4 * i + 2 * j, j == 1);
        if (m == 5)
        {
            [loop] for (i = 0; i < 2; i++) [loop] for (j = 0; j < 2; j++) Bf(T, n0 - m + 3 - n2 * j - 4 * i, n1 + m - 4 + n2 * j + 4 * i, 28 - 16 * i + 56 * j, true);
            [loop] for (i = 0; i < 2; i++) [loop] for (j = 0; j < 4; j++) Hf(T, n1 + n3 * j + i, n1 + n2 - 5 + n3 * j - i, (j & 1) == 1);
        }
        if (m >= 4)
        {
            [loop] for (i = 0; i <= (m == 5 ? 1 : 0); i++) [loop] for (j = 0; j < 2; j++) Bf(T, n0 - m + 2 - i - n2 * j, n1 + m - 3 + i + n2 * j, 24 + 48 * j, true);
            [loop] for (i = 0; i <= 2 * m - 7; i++) [loop] for (j = 0; j < 2; j++) Hf(T, n1 + n2 * j + i, n1 + n2 - 1 + n2 * j - i, (j & 1) == 1);
        }
        if (m >= 3) [loop] for (i = 0; i < n3; i++) Bf(T, n0 - n3 - 1 - i, n1 + n3 + i, 16, true);
        [loop] for (i = 0; i < n1; i++) Hf(T, i, n0 - 1 - i, false);
    }
}

// ── one superblock: its blocks, every transform block of them, as the player rebuilds them ──
groupshared int sPred[1024], sQ[1024], sDq[1024]; groupshared float sRes[1024], sTmp[1024];
groupshared uint sList[64], sCount, sAt, sEob, sAny, sEobTotal, sDc;
groupshared uint sStack[64], sYtx;
uint ScanAt(uint tsz, uint k) { uint b = tsz == 0 ? 0 : tsz == 1 ? 16 : tsz == 2 ? 80 : 336; return TAB[b + k]; }
uint InvScan(uint tsz, uint pos) { uint b = tsz == 0 ? 0 : tsz == 1 ? 16 : tsz == 2 ? 80 : 336; return TAB[1360 + b + pos]; }
uint TileStart(uint c)
{
    uint sc = c >> 3, k = 0; [loop] for (uint t = 1; t < (1u << TILELOG2); t++) if (((t * SBCOLS) >> TILELOG2) <= sc) k = t;
    return ((k * SBCOLS) >> TILELOG2) << 3;
}
void Superblock(uint sr, uint sc, uint t)
{
    uint r0 = sr * 8, c0 = sc * 8, slot = (sr * SBCOLS + sc) * SLOT, keyF = KEY, micols = MICOLS, mirows = MIROWS; GroupMemoryBarrierWithGroupSync();
    uint m64 = M64Base() + (sr * SBCOLS + sc) * 3, bc = (micols + 3) / 4;
    // the blocks, in the order the format codes them (decode_partition: split at the frame's edges, where busy, where the motion parts)
    if (t == 0)
    {
        sCount = 0; uint top = 0; sStack[top++] = (r0 << 18) | (c0 << 4) | 3;
        [loop] while (top > 0)
        {
            uint e = sStack[--top]; uint r = e >> 18, c = (e >> 4) & 0x3FFF, bsl = e & 15;
            if (r >= mirows || c >= micols) continue;
            uint half = (1u << bsl) >> 1; bool hasRows = r + half < mirows, hasCols = c + half < micols, split;
            if (hasRows && hasCols) split = bsl > 0 && ((bsl > FLOOR && Busy(r, c, bsl)) || (bsl == 3 && keyF == 0 && MOT[m64 + 2] != 0));
            else split = bsl > 0;
            if (split) { uint b1 = bsl - 1; sStack[top++] = ((r + half) << 18) | ((c + half) << 4) | b1; sStack[top++] = ((r + half) << 18) | (c << 4) | b1; sStack[top++] = (r << 18) | ((c + half) << 4) | b1; sStack[top++] = (r << 18) | (c << 4) | b1; }
            else sList[sCount++] = e;
        }
        sAt = 0;
    }
    GroupMemoryBarrierWithGroupSync();
    uint count = sCount;
    [loop] for (uint bi = 0; bi < count; bi++)
    {
        uint e = sList[bi]; uint r = e >> 18, c = (e >> 4) & 0x3FFF, bsl = e & 15;
        int2 mv = int2(0, 0);
        if (keyF == 0) mv = bsl == 3 ? UnpackMv(MOT[m64]) : UnpackMv(MOT[M32Base() + ((r >> 2) * bc + (c >> 2)) * 2]);
        uint maxT = min(bsl + 1, 3u);
        if (t == 0) sYtx = Busy(r, c, bsl) ? 0 : maxT;                                  // (worked out once, then the same for every thread)
        GroupMemoryBarrierWithGroupSync();
        uint ytx = sYtx;
        bool availU = r > 0, availL = c > TileStart(c);
        if (t == 0) sEobTotal = 0;
        GroupMemoryBarrierWithGroupSync();
        [loop] for (uint p = 0; p < 3; p++)
        {
            uint sub = p > 0 ? 1 : 0, tsz = p == 0 ? ytx : min(ytx, bsl), step = 1u << tsz, N = 4u << tsz, NN = N * N;
            uint n4 = (2u << bsl) >> sub, baseX = (c * 8) >> sub, baseY = (r * 8) >> sub, maxx = (micols * 8) >> sub, maxy = (mirows * 8) >> sub;
            uint po = PlaneOff(p), ps = PStride(p);
            int lastX = p == 0 ? (int)FW - 1 : (int)((FW + 1) >> 1) - 1, lastY = p == 0 ? (int)FH - 1 : (int)((FH + 1) >> 1) - 1;
            [loop] for (uint ty = 0; ty < n4; ty += step) [loop] for (uint tx = 0; tx < n4; tx += step)
            {
                uint x0 = baseX + 4 * tx, y0 = baseY + 4 * ty; bool inF = x0 < maxx && y0 < maxy;
                if (!inF) { if (t == 0) { STR[slot + sAt] = 0; sAt++; } GroupMemoryBarrierWithGroupSync(); continue; }
                // the prediction
                if (keyF != 0)
                {
                    DeviceMemoryBarrierWithGroupSync();
                    if (t == 0)
                    {
                        bool hL = availL || tx > 0, hA = availU || ty > 0; int sa = 0, sl = 0, lg = (int)tsz + 2, dc;
                        uint mX = maxx - 1, mY = maxy - 1;
                        if (hA) [loop] for (uint i = 0; i < N; i++) sa += (int)CUR[po + (y0 - 1) * ps + min(mX, x0 + i)];
                        if (hL) [loop] for (uint i = 0; i < N; i++) sl += (int)CUR[po + min(mY, y0 + i) * ps + x0 - 1];
                        if (hA && hL) dc = (sa + sl + (int)N) >> (lg + 1); else if (hL) dc = (sl + (int)(N >> 1)) >> lg; else if (hA) dc = (sa + (int)(N >> 1)) >> lg; else dc = 128;
                        sDc = (uint)dc;
                    }
                    GroupMemoryBarrierWithGroupSync();
                    [loop] for (uint k = t; k < NN; k += 256) sPred[k] = (int)sDc;
                }
                else
                {
                    int my = p == 0 ? mv.x : mv.x / 2, mx = p == 0 ? mv.y : mv.y / 2;
                    [loop] for (uint k = t; k < NN; k += 256) { int yy = clamp((int)(y0 + k / N) + my, 0, lastY), xx = clamp((int)(x0 + k % N) + mx, 0, lastX); sPred[k] = (int)REF[po + yy * ps + xx]; }
                }
                if (t == 0) { sAny = 0; sEob = 0; }
                GroupMemoryBarrierWithGroupSync();
                // the residual (nothing outside the frame)
                [loop] for (uint k = t; k < NN; k += 256)
                {
                    uint yy = y0 + k / N, xx = x0 + k % N; float d = yy < maxy && xx < maxx ? (float)((int)SRC[po + yy * ps + xx] - sPred[k]) : 0;
                    sRes[k] = d; if (d != 0) InterlockedOr(sAny, 1u);
                }
                GroupMemoryBarrierWithGroupSync();
                if (sAny != 0)
                {
                    // the forward transform: 8 x the orthonormal DCT, rows then columns; quantised; the last coefficient in the scan found
                    float pi = 3.14159265358979, sc8 = sqrt(8.0) * sqrt(2.0 / N);
                    [loop] for (uint k = t; k < NN; k += 256)
                    {
                        uint i = k / N, u = k % N; float a = 0, cu = (u == 0 ? sqrt(0.5) : 1.0) * sc8;
                        [loop] for (uint j = 0; j < N; j++) a += cos((2 * j + 1) * u * pi / (2 * N)) * sRes[i * N + j];
                        sTmp[k] = a * cu;
                    }
                    GroupMemoryBarrierWithGroupSync();
                    [loop] for (uint k = t; k < NN; k += 256)
                    {
                        uint u = k / N, w = k % N; float a = 0, cu = (u == 0 ? sqrt(0.5) : 1.0) * sc8;
                        [loop] for (uint i = 0; i < N; i++) a += cos((2 * i + 1) * u * pi / (2 * N)) * sTmp[i * N + w];
                        a *= cu; bool dcp = k == 0; float m = abs(a) / (float)(dcp ? DCQ : ACQ);
                        int qv = (int)(m + (dcp ? 0.5 : 0.36)); qv = min(qv, 16000); sQ[k] = a < 0 ? -qv : qv;
                        if (qv != 0) InterlockedMax(sEob, InvScan(tsz, k) + 1);
                    }
                }
                else [loop] for (uint k = t; k < NN; k += 256) sQ[k] = 0;
                GroupMemoryBarrierWithGroupSync();
                uint eob = sEob, at = sAt;
                // its coefficients into the superblock's slot: the count, then two to a word in the scan's order
                if (t == 0) { STR[slot + at] = eob; sEobTotal += eob; }
                [loop] for (uint k = t; k < (eob + 1) / 2; k += 256) { int lo = sQ[ScanAt(tsz, 2 * k)], hi = 2 * k + 1 < eob ? sQ[ScanAt(tsz, 2 * k + 1)] : 0; STR[slot + at + 1 + k] = ((uint)lo & 0xFFFF) | ((uint)hi << 16); }
                GroupMemoryBarrierWithGroupSync();
                if (t == 0) sAt = at + 1 + (eob + 1) / 2;
                // rebuilt as the player rebuilds it: dequantised, the whole-number inverse (rows, then columns), added to the prediction
                int den = tsz == 3 ? 2 : 1;
                [loop] for (uint k = t; k < NN; k += 256) sDq[k] = eob > 0 ? sQ[k] * (k == 0 ? DCQ : ACQ) / den : 0;
                GroupMemoryBarrierWithGroupSync();
                if (eob > 0)
                {
                    int n = (int)tsz + 2, sh = min(6, n + 2);
                    if (t < N) { int T[32], cc[32]; uint o = t * N; [loop] for (uint j = 0; j < N; j++) cc[j] = sDq[o + j]; [loop] for (uint j2 = 0; j2 < N; j2++) T[j2] = cc[Brev(n, j2)]; Idct(T, n); [loop] for (uint j3 = 0; j3 < N; j3++) sDq[o + j3] = T[j3]; }
                    GroupMemoryBarrierWithGroupSync();
                    if (t < N) { int T[32], cc[32]; [loop] for (uint i = 0; i < N; i++) cc[i] = sDq[i * N + t]; [loop] for (uint i2 = 0; i2 < N; i2++) T[i2] = cc[Brev(n, i2)]; Idct(T, n); [loop] for (uint i3 = 0; i3 < N; i3++) sDq[i3 * N + t] = (T[i3] + (1 << (sh - 1))) >> sh; }
                    GroupMemoryBarrierWithGroupSync();
                }
                [loop] for (uint k = t; k < NN; k += 256) { uint yy = y0 + k / N, xx = x0 + k % N; CUR[po + yy * ps + xx] = (uint)clamp(sPred[k] + sDq[k], 0, 255); }
                DeviceMemoryBarrierWithGroupSync();
            }
        }
        // the block's decisions for every 8 x 8 of it in the frame (a skipped change block's transform the largest, as the player takes it)
        uint skip = sEobTotal == 0 ? 1 : 0, txs = keyF == 0 && skip == 1 ? maxT : ytx, n8 = 1u << bsl;
        [loop] for (uint k = t; k < n8 * n8; k += 256) { uint yy = r + k / n8, xx = c + k % n8; if (yy < mirows && xx < micols) { GRID[(yy * micols + xx) * 2] = bsl | (txs << 2) | (skip << 4) | (ytx << 5); GRID[(yy * micols + xx) * 2 + 1] = PackMv(mv); } }
        GroupMemoryBarrierWithGroupSync();
    }
    if (t == 0) LEN[sr * SBCOLS + sc] = sAt;
}
// change frames: every superblock at once; whole frames: superblock diagonals in turn (each predicts from the ones above and left)
[numthreads(256, 1, 1)]
void SBALL(uint3 gid : SV_GroupID, uint t : SV_GroupIndex) { uint sr = gid.x / SBCOLS, sc = gid.x - sr * SBCOLS; Superblock(sr, sc, t); }
[numthreads(256, 1, 1)]
void SBDIAG(uint3 gid : SV_GroupID, uint t : SV_GroupIndex)
{
    uint r = kA + gid.x, c = kWave - r;
    Superblock(r, c, t);
}

// ── the loop filter (section 8.8), superblocks in waves (each after the ones it overlaps), within one the format's own order ──
int C4(int v) { return clamp(v, -128, 127); }
void FilterSample(uint po, uint ps, int x, int y, int dx, int dy, int fs)
{
    int st = dy * (int)ps + dx, o = (int)po + y * (int)ps + x, s[16], k;
    int reach = fs == 2 ? 8 : 4;
    [loop] for (k = -reach; k < reach; k++) s[8 + k] = (int)CUR[o + k * st];
    int p0 = s[7], p1 = s[6], p2 = s[5], p3 = s[4], q0 = s[8], q1 = s[9], q2 = s[10], q3 = s[11];
    bool hev = abs(p1 - p0) > THRESH || abs(q1 - q0) > THRESH;
    bool mask = abs(p3 - p2) > LIMIT || abs(p2 - p1) > LIMIT || abs(p1 - p0) > LIMIT || abs(q1 - q0) > LIMIT || abs(q2 - q1) > LIMIT || abs(q3 - q2) > LIMIT || abs(p0 - q0) * 2 + abs(p1 - q1) / 2 > BLIMIT;
    if (mask) return;
    bool flat = false, flat2 = false;
    if (fs >= 1) flat = !(abs(p1 - p0) > 1 || abs(q1 - q0) > 1 || abs(p2 - p0) > 1 || abs(q2 - q0) > 1 || abs(p3 - p0) > 1 || abs(q3 - q0) > 1);
    if (fs >= 2) flat2 = !(abs(s[0] - p0) > 1 || abs(s[15] - q0) > 1 || abs(s[1] - p0) > 1 || abs(s[14] - q0) > 1 || abs(s[2] - p0) > 1 || abs(s[13] - q0) > 1 || abs(s[3] - p0) > 1 || abs(s[12] - q0) > 1);
    if (fs == 0 || !flat)
    {
        int ps1 = p1 - 128, ps0 = p0 - 128, qs0 = q0 - 128, qs1 = q1 - 128;
        int f = hev ? C4(ps1 - qs1) : 0; f = C4(f + 3 * (qs0 - ps0));
        int f1 = C4(f + 4) >> 3, f2 = C4(f + 3) >> 3;
        CUR[o] = (uint)(C4(qs0 - f1) + 128); CUR[o - st] = (uint)(C4(ps0 + f2) + 128);
        if (!hev) { f = (f1 + 1) >> 1; CUR[o + st] = (uint)(C4(qs1 - f) + 128); CUR[o - 2 * st] = (uint)(C4(ps1 + f) + 128); }
        return;
    }
    int log2 = fs == 1 || !flat2 ? 3 : 4, n = (1 << (log2 - 1)) - 1, F[16], i, j;
    [loop] for (i = -n; i < n; i++) { int tt = s[8 + i]; [loop] for (j = -n; j <= n; j++) tt += s[8 + clamp(i + j, -(n + 1), n)]; F[8 + i] = (tt + (1 << (log2 - 1))) >> log2; }
    [loop] for (i = -n; i < n; i++) CUR[o + i * st] = (uint)F[8 + i];
}
[numthreads(64, 1, 1)]
void LOOPF(uint3 gid : SV_GroupID, uint i : SV_GroupIndex)
{
    uint sr = kA + gid.x, sc = kWave - 2 * sr;
    uint row = sr * 8, col = sc * 8, mic = MICOLS, mir = MIROWS, key = KEY; GroupMemoryBarrierWithGroupSync();
    [loop] for (uint plane = 0; plane < 3; plane++)
    [loop] for (uint dir = 0; dir < 2; dir++)
    {
        uint subX = plane > 0 ? 1 : 0, subY = subX, dx = dir == 0 ? 1 : 0, dy = 1 - dx, sub = dir == 0 ? subX : subY, edgeLen = 64u >> (dir == 0 ? subY : subX);
        uint po = PlaneOff(plane), ps = PStride(plane);
        [loop] for (uint edge = 0; edge < (16u >> sub); edge++)
        {
            if (i < edgeLen)
            {
                uint x, y;
                if (dir == 0) { x = col * 8 + edge * (4u << subX); y = row * 8 + (i << subY); } else { x = col * 8 + (i << subX); y = row * 8 + edge * (4u << subY); }
                bool on = !(x >= 8 * MICOLS || y >= 8 * MIROWS || (dir == 0 && x == 0) || (dir == 1 && y == 0));
                if (on)
                {
                    uint loopCol = ((x >> 3) >> subX) << subX, loopRow = ((y >> 3) >> subY) << subY, info = GRID[(loopRow * MICOLS + loopCol) * 2];
                    uint bsl = info & 3, txs = (info >> 2) & 3, txSz = plane > 0 ? min(txs, bsl) : txs, sbBsl = sub == 0 ? bsl : max(1u, bsl), span = 8u << sbBsl;
                    bool skip = ((info >> 4) & 1) != 0, isIntra = KEY != 0;
                    bool isBlockEdge = dir == 0 ? x % span == 0 : y % span == 0, isTxEdge;
                    if (dir == 1 && subX == 1 && (MICOLS & 1) == 1 && (edge & 1) == 1 && x + 8 >= MICOLS * 8) isTxEdge = false; else isTxEdge = edge % (1u << txSz) == 0;
                    bool is32 = edge % 8 == 0;
                    if (isBlockEdge || (isTxEdge && isIntra) || (isTxEdge && !skip))
                    {
                        int baseSize = txSz == 0 && is32 ? 1 : (int)min(2u, txSz), fs = baseSize;
                        if (dir == 0 && subX == 1 && baseSize == 2 && (x >> 3) == MICOLS - 1) fs = 1;
                        else if (dir == 1 && subY == 1 && baseSize == 2 && (y >> 3) == MIROWS - 1) fs = 1;
                        FilterSample(po, ps, (int)(x >> subX), (int)(y >> subY), (int)dx, (int)dy, fs);
                    }
                }
            }
            DeviceMemoryBarrierWithGroupSync();
        }
    }
}

// ── the coefficients packed one superblock after another: where each starts, then the copy ──
[numthreads(1, 1, 1)]
void PLACE(uint3 id : SV_DispatchThreadID)
{
    uint n = SBCOLS * SBROWS, at = 0;
    [loop] for (uint k = 0; k < n; k++) { LEN[n + k] = at; at += LEN[k]; }
    LEN[2 * n] = at; LEN[2 * n + 1] = at > OUTCAP ? 1 : 0;
}
[numthreads(256, 1, 1)]
void PACK(uint3 gid : SV_GroupID, uint t : SV_GroupIndex)
{
    uint n = SBCOLS * SBROWS, sb = gid.x, len = LEN[sb], at = LEN[n + sb];
    if (at + len > OUTCAP) return;
    [loop] for (uint k = t; k < len; k += 256) OUT[at + k] = STR[sb * SLOT + k];
}
";
    }
}
