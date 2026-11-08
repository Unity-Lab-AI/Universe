// Universe · render · our own WebM container (EBML, the Matroska layout), every line ours: one video track, a cluster from each
// whole frame onwards, the frames as simple blocks (the whole ones marked, where a player can start), cues to the clusters. Used
// by our own VP9 and AV1 front men.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    internal static class WebmFile
    {
        static void Id(List<byte> o, uint id) { if (id > 0xFFFFFF) o.Add((byte)(id >> 24)); if (id > 0xFFFF) o.Add((byte)(id >> 16)); if (id > 0xFF) o.Add((byte)(id >> 8)); o.Add((byte)id); }
        static void Size8(List<byte> o, long n) { o.Add(0x01); for (int i = 6; i >= 0; i--) o.Add((byte)(n >> (8 * i))); }
        static void El(List<byte> o, uint id, List<byte> body) { Id(o, id); Size8(o, body.Count); o.AddRange(body); }
        static void UInt(List<byte> o, uint id, ulong v) { var b = new List<byte>(); int n = 1; while (n < 8 && (v >> (8 * n)) != 0) n++; for (int i = n - 1; i >= 0; i--) b.Add((byte)(v >> (8 * i))); El(o, id, b); }
        static void Str(List<byte> o, uint id, string s) { El(o, id, new List<byte>(System.Text.Encoding.ASCII.GetBytes(s))); }
        static void F64(List<byte> o, uint id, double v) { var b = BitConverter.GetBytes(v); Array.Reverse(b); El(o, id, new List<byte>(b)); }

        /// <summary>Writes the file: <paramref name="codec"/> (V_VP9, V_AV1), its private data (null: none), the frames with their
        /// times in milliseconds and which are whole, and the clip's length; <paramref name="attachment"/> (null: none) carried as an
        /// attached file named <paramref name="attachName"/> (players pass it by; our own program reads it). The file is written as
        /// it goes, cluster by cluster, the segment's size set at the end.</summary>
        public static void Write(string path, string codec, byte[] codecPrivate, int w, int h, IList<byte[]> frames, IList<uint> times, IList<bool> keys, uint lengthMs, byte[] attachment = null, string attachName = null, string attachMime = null, string attachPath = null)
        {
            var ebml = new List<byte>(); UInt(ebml, 0x4286, 1); UInt(ebml, 0x42F7, 1); UInt(ebml, 0x42F2, 4); UInt(ebml, 0x42F3, 8); Str(ebml, 0x4282, "webm"); UInt(ebml, 0x4287, 2); UInt(ebml, 0x4285, 2);
            var head = new List<byte>(); El(head, 0x1A45DFA3, ebml);
            var info = new List<byte>(); UInt(info, 0x2AD7B1, 1000000); F64(info, 0x4489, lengthMs); Str(info, 0x4D80, "Universe front man"); Str(info, 0x5741, "Universe front man");
            var video = new List<byte>(); UInt(video, 0xB0, (ulong)w); UInt(video, 0xBA, (ulong)h);
            var entry = new List<byte>(); UInt(entry, 0xD7, 1); UInt(entry, 0x73C5, 1); UInt(entry, 0x83, 1); UInt(entry, 0x9C, 0); Str(entry, 0x86, codec);
            if (codecPrivate != null) El(entry, 0x63A2, new List<byte>(codecPrivate));
            El(entry, 0xE0, video);
            var tracks = new List<byte>(); El(tracks, 0xAE, entry);
            using (var f = System.IO.File.Create(path))
            {
                Action<List<byte>> put = l => f.Write(l.ToArray(), 0, l.Count);
                put(head);
                var sh = new List<byte>(); Id(sh, 0x18538067); put(sh); long sizeAt = f.Position; put(new List<byte>(new byte[8])); long body = f.Position;
                var top = new List<byte>(); El(top, 0x1549A966, info); El(top, 0x1654AE6B, tracks); put(top);
                if (attachment != null || attachPath != null)
                {
                    long attLen = attachment != null ? attachment.Length : new System.IO.FileInfo(attachPath).Length;
                    // the attached file's header, then its bytes straight from the array (a clip's records can be megabytes)
                    var fh = new List<byte>(); Str(fh, 0x466E, attachName ?? "clip"); Str(fh, 0x4660, attachMime ?? "application/octet-stream"); UInt(fh, 0x46AE, 1);
                    var dataHead = new List<byte>(); Id(dataHead, 0x465C); Size8(dataHead, attLen);
                    long fileLen = fh.Count + dataHead.Count + attLen;
                    var ah = new List<byte>(); Id(ah, 0x1941A469); Size8(ah, 2 + 8 + fileLen); Id(ah, 0x61A7); Size8(ah, fileLen); ah.AddRange(fh); ah.AddRange(dataHead);
                    put(ah);
                    if (attachment != null) f.Write(attachment, 0, attachment.Length);
                    else using (var a = System.IO.File.OpenRead(attachPath)) a.CopyTo(f, 1 << 20);
                }
                var cues = new List<byte>();
                int i0 = 0;
                while (i0 < frames.Count)
                {
                    int i1 = i0 + 1; while (i1 < frames.Count && !keys[i1] && times[i1] - times[i0] < 30000) i1++;
                    var cl = new List<byte>(); UInt(cl, 0xE7, times[i0]);
                    for (int k = i0; k < i1; k++) { var sb = new List<byte>(); sb.Add(0x81); int rel = (int)(times[k] - times[i0]); sb.Add((byte)(rel >> 8)); sb.Add((byte)rel); sb.Add((byte)(keys[k] ? 0x80 : 0)); sb.AddRange(frames[k]); El(cl, 0xA3, sb); }
                    var tp = new List<byte>(); UInt(tp, 0xF7, 1); UInt(tp, 0xF1, (ulong)(f.Position - body));
                    var cp = new List<byte>(); UInt(cp, 0xB3, times[i0]); El(cp, 0xB7, tp); El(cues, 0xBB, cp);
                    var c = new List<byte>(); El(c, 0x1F43B675, cl); put(c); i0 = i1;
                }
                var cu = new List<byte>(); El(cu, 0x1C53BB6B, cues); put(cu);
                long len = f.Position - body; f.Position = sizeAt; var sz = new List<byte>(); Size8(sz, len); put(sz);
            }
        }

        /// <summary>Where the attached file named <paramref name="name"/> starts in a WebM written by <see cref="Write"/>, and its length
        /// (-1: none); read from the elements before the first cluster.</summary>
        public static long FindAttachment(System.IO.Stream s, string name, out long length)
        {
            length = 0;
            try
            {
                var r = new System.IO.BinaryReader(s); s.Position = 0;
                Func<long> vint = () => { int b = r.ReadByte(), n = 1, m = 0x80; while (n <= 8 && (b & m) == 0) { n++; m >>= 1; } long v = b & (m - 1); for (int k = 1; k < n; k++) v = (v << 8) | r.ReadByte(); return v; };
                Func<uint> id = () => { int b = r.ReadByte(); int n = (b & 0x80) != 0 ? 1 : (b & 0x40) != 0 ? 2 : (b & 0x20) != 0 ? 3 : 4; uint v = (uint)b; for (int k = 1; k < n; k++) v = (v << 8) | r.ReadByte(); return v; };
                if (id() != 0x1A45DFA3) return -1; { long hl = vint(); s.Position += hl; }   // (the size read first: "Position += vint()" would read the position before the size)
                if (id() != 0x18538067) return -1; vint();
                while (s.Position < s.Length)
                {
                    uint e = id(); long n = vint();
                    if (e == 0x1F43B675) return -1;                                         // (a cluster: no attachment before the frames)
                    if (e != 0x1941A469) { s.Position += n; continue; }
                    long end = s.Position + n;
                    while (s.Position < end)
                    {
                        uint fe = id(); long fl = vint(); long fend = s.Position + fl; string nm = null;
                        while (s.Position < fend)
                        {
                            uint c = id(); long cl = vint();
                            if (c == 0x466E) nm = System.Text.Encoding.UTF8.GetString(r.ReadBytes((int)cl));
                            else if (c == 0x465C && nm == name) { length = cl; return s.Position; }
                            else s.Position += cl;
                        }
                    }
                    return -1;
                }
            }
            catch { }
            return -1;
        }
    }
}
