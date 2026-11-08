// Universe · render · the universe clip played back (FRONTMAN-RESEARCH.md section 11, step 2): the .cdf97w of the universe itself
// read back, the universe made again from its seed, and each frame its recorded scene calls applied with their exact arguments,
// its camera point set (before and after the universe's own update, so nothing of the update moves it), the clock held while the
// universe is still being made round the camera, and the galaxy, system and ground made checked against what was recorded.
using System;
using System.Collections.Generic;

namespace Universe.Render
{
    internal sealed class UniverseClipReader
    {
        public sealed class Point { public uint Ms; public byte[] Data; public int Near, Sys, Planet; }
        public sealed class Event { public uint Ms; public string Name; public object[] Args; }
        public readonly List<Point> Points = new List<Point>(); public readonly List<Event> Events = new List<Event>();
        public ulong? Seed; public string Tour, Meta;
        public const int PointBytes = 6 * 8 + 6 * 4 + 5 * 8 + 6 * 8 + 3 * 4;

        /// <summary>True for a .cdf97w of the universe itself (version 9, kind 1).</summary>
        public static bool Is(string path)
        {
            try { using (var r = new System.IO.BinaryReader(System.IO.File.OpenRead(path))) return r.BaseStream.Length > 7 && r.ReadUInt32() == Cdf97wCodec.Magic && r.ReadUInt16() == UniverseClipWriter.Version && r.ReadByte() == UniverseClipWriter.Kind; }
            catch { return false; }
        }
        public UniverseClipReader(string path)
        {
            using (var f = System.IO.File.OpenRead(path))
            {
                var h = new System.IO.BinaryReader(f);
                if (h.ReadUInt32() != Cdf97wCodec.Magic || h.ReadUInt16() != UniverseClipWriter.Version || h.ReadByte() != UniverseClipWriter.Kind) throw new InvalidOperationException("not a clip of the universe itself");
                int ml = h.ReadUInt16(); Meta = System.Text.Encoding.UTF8.GetString(h.ReadBytes(ml));
                foreach (var line in Meta.Split('\n')) { ulong s; if (line.StartsWith("seed: ") && ulong.TryParse(line.Substring(6).Trim(), out s)) Seed = s; if (line.StartsWith("tour: ")) Tour = line.Substring(6).Trim(); }
                using (var z = new System.IO.Compression.DeflateStream(f, System.IO.Compression.CompressionMode.Decompress))
                {
                    var r = new System.IO.BinaryReader(z);
                    while (true)
                    {
                        int type; try { type = r.ReadByte(); } catch (System.IO.EndOfStreamException) { break; }
                        uint ms = r.ReadUInt32();
                        if (type == 1)
                        {
                            var d = r.ReadBytes(PointBytes); if (d.Length < PointBytes) break;
                            Points.Add(new Point { Ms = ms, Data = d, Near = BitConverter.ToInt32(d, PointBytes - 12), Sys = BitConverter.ToInt32(d, PointBytes - 8), Planet = BitConverter.ToInt32(d, PointBytes - 4) });
                        }
                        else if (type == 2)
                        {
                            string name = r.ReadString(); int n = r.ReadByte(); var args = new object[n];
                            for (int i = 0; i < n; i++)
                            {
                                char t = r.ReadChar();
                                switch (t)
                                {
                                    case 'n': args[i] = null; break;
                                    case 'd': args[i] = r.ReadDouble(); break;
                                    case 'f': args[i] = r.ReadSingle(); break;
                                    case 'u': args[i] = r.ReadUInt64(); break;
                                    case 'i': args[i] = r.ReadInt32(); break;
                                    case 'b': args[i] = r.ReadBoolean(); break;
                                    case 's': args[i] = r.ReadString(); break;
                                    case 'p':
                                        int pc = r.ReadInt32(); var ps = new Universe.Gen.SpaceGen.Part[pc];
                                        for (int k = 0; k < pc; k++) { string pn = r.ReadString(), pe = r.ReadString(); double sh = r.ReadDouble(); float pr = r.ReadSingle(), pg = r.ReadSingle(), pb = r.ReadSingle(); ps[k] = new Universe.Gen.SpaceGen.Part(pn, pe, sh, pr, pg, pb); }
                                        args[i] = ps; break;
                                    default: throw new InvalidOperationException("an argument of an unknown kind");
                                }
                            }
                            Events.Add(new Event { Ms = ms, Name = name, Args = args });
                        }
                        else break;
                    }
                }
            }
        }
    }

    /// <summary>Plays a universe clip back through the universe made again from its seed.</summary>
    internal sealed class UniverseClipPlayer
    {
        private readonly UniverseClipReader _c; private double _t; private int _ev, _pt; private bool _started;
        public bool Finished { get; private set; }
        public string Note { get; private set; }
        public int Mismatches { get; private set; }
        public UniverseClipPlayer(UniverseClipReader clip) { _c = clip; Note = "playing a clip of the universe itself"; }
        /// <summary>Before the universe's update: the clock moved on (held while the universe is still being made), the scene calls up
        /// to now applied, the camera set to the point for now.</summary>
        public void Before(Universe.Shell.UniverseView w, double dt)
        {
            if (Finished || _c.Points.Count == 0) { Finished = true; return; }
            bool loading = w.Loading || w.GroundBacklog > 256;
            if (!loading || !_started) { if (_started) _t += dt * 1000; _started = true; }
            while (_ev < _c.Events.Count && _c.Events[_ev].Ms <= _t) { w.ApplyMatterCall(_c.Events[_ev].Name, _c.Events[_ev].Args); _ev++; }
            while (_pt + 1 < _c.Points.Count && _c.Points[_pt + 1].Ms <= _t) _pt++;
            w.ApplyPoint(_c.Points[_pt].Data);
            if (_pt + 1 >= _c.Points.Count && _t > _c.Points[_pt].Ms + 1000) Finished = true;
            Note = loading ? "the clip waits while the universe is made round it" : "playing the clip: " + (_t / 1000).ToString("0") + " of " + (_c.Points[_c.Points.Count - 1].Ms / 1000) + " s";
        }
        /// <summary>After the update: the camera set to the point again (nothing of the update moves it), and the scene checked.</summary>
        public void After(Universe.Shell.UniverseView w)
        {
            if (Finished || _c.Points.Count == 0) return;
            var p = _c.Points[_pt]; w.ApplyPoint(p.Data);
            if (!w.Loading && (w.NearIndex != p.Near || w.SystemIndex != p.Sys || w.GroundIndex != p.Planet))
            {
                Mismatches++;
                if (Mismatches == 1 || Mismatches % 300 == 0) Universe.Program.WriteLog("universe clip: at " + p.Ms + " ms the scene made (galaxy " + w.NearIndex + ", system " + w.SystemIndex + ", ground " + w.GroundIndex + ") is not the one recorded (" + p.Near + ", " + p.Sys + ", " + p.Planet + ")");
                Note = "the scene made here is not the one recorded (galaxy, system or ground)";
            }
        }
    }
}
