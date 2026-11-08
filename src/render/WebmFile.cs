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
        /// times in milliseconds and which are whole, and the clip's length.</summary>
        public static void Write(string path, string codec, byte[] codecPrivate, int w, int h, IList<byte[]> frames, IList<uint> times, IList<bool> keys, uint lengthMs)
        {
            var ebml = new List<byte>(); UInt(ebml, 0x4286, 1); UInt(ebml, 0x42F7, 1); UInt(ebml, 0x42F2, 4); UInt(ebml, 0x42F3, 8); Str(ebml, 0x4282, "webm"); UInt(ebml, 0x4287, 2); UInt(ebml, 0x4285, 2);
            var head = new List<byte>(); El(head, 0x1A45DFA3, ebml);
            var info = new List<byte>(); UInt(info, 0x2AD7B1, 1000000); F64(info, 0x4489, lengthMs); Str(info, 0x4D80, "Universe front man"); Str(info, 0x5741, "Universe front man");
            var video = new List<byte>(); UInt(video, 0xB0, (ulong)w); UInt(video, 0xBA, (ulong)h);
            var entry = new List<byte>(); UInt(entry, 0xD7, 1); UInt(entry, 0x73C5, 1); UInt(entry, 0x83, 1); UInt(entry, 0x9C, 0); Str(entry, 0x86, codec);
            if (codecPrivate != null) El(entry, 0x63A2, new List<byte>(codecPrivate));
            El(entry, 0xE0, video);
            var tracks = new List<byte>(); El(tracks, 0xAE, entry);
            var seg = new List<byte>(); El(seg, 0x1549A966, info); El(seg, 0x1654AE6B, tracks);
            var cues = new List<byte>();
            int i0 = 0;
            while (i0 < frames.Count)
            {
                int i1 = i0 + 1; while (i1 < frames.Count && !keys[i1] && times[i1] - times[i0] < 30000) i1++;
                var cl = new List<byte>(); UInt(cl, 0xE7, times[i0]);
                for (int k = i0; k < i1; k++) { var sb = new List<byte>(); sb.Add(0x81); int rel = (int)(times[k] - times[i0]); sb.Add((byte)(rel >> 8)); sb.Add((byte)rel); sb.Add((byte)(keys[k] ? 0x80 : 0)); sb.AddRange(frames[k]); El(cl, 0xA3, sb); }
                var tp = new List<byte>(); UInt(tp, 0xF7, 1); UInt(tp, 0xF1, (ulong)seg.Count);
                var cp = new List<byte>(); UInt(cp, 0xB3, times[i0]); El(cp, 0xB7, tp); El(cues, 0xBB, cp);
                El(seg, 0x1F43B675, cl); i0 = i1;
            }
            El(seg, 0x1C53BB6B, cues);
            using (var f = System.IO.File.Create(path)) { f.Write(head.ToArray(), 0, head.Count); var s = new List<byte>(); El(s, 0x18538067, seg); f.Write(s.ToArray(), 0, s.Count); }
        }
    }
}
