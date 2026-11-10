// Universe · core · reads the game's content files (docs/CONTENT-FORMAT.md). Our own parser.
using System;
using System.Collections.Generic;
using System.IO;

namespace Universe.Core
{
    internal sealed class ContentEntry
    {
        public string Kind, Id, File, Section;
        public readonly Dictionary<string, string> Props = new Dictionary<string, string>();
        // every value of a key written more than once (a crystal's sites, one line each), in order
        public readonly Dictionary<string, List<string>> Lists = new Dictionary<string, List<string>>();
        public List<string> All(string key) { List<string> l; return Lists.TryGetValue(key, out l) ? l : new List<string>(); }
        public string Get(string key, string fallback) { string v; return Props.TryGetValue(key, out v) ? v : fallback; }
        public string Name { get { return Get("name", Id); } }

        /// <summary>Reads a range like "30000..50000" (or a single number). Returns false if missing.</summary>
        public bool TryRange(string key, out double lo, out double hi)
        {
            lo = hi = 0; string v;
            if (!Props.TryGetValue(key, out v)) return false;
            int dots = v.IndexOf("..", StringComparison.Ordinal);
            string a = dots >= 0 ? v.Substring(0, dots) : v, b = dots >= 0 ? v.Substring(dots + 2) : v;
            if (!ParseNumber(a, out lo)) return false;
            if (b.Length == 0 || !ParseNumber(b, out hi)) hi = lo;
            return true;
        }
        private static bool ParseNumber(string s, out double d)
        {
            return double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d);
        }
    }

    internal sealed class ContentLibrary
    {
        public readonly List<ContentEntry> Entries = new List<ContentEntry>();
        public readonly List<string> Files = new List<string>();

        public static ContentLibrary Load(string folder)
        {
            var lib = new ContentLibrary();
            if (!Directory.Exists(folder)) return lib;
            foreach (string path in Directory.GetFiles(folder, "*.unt", SearchOption.AllDirectories))
            {
                lib.Files.Add(path);
                lib.ParseFile(path);
            }
            return lib;
        }

        // the entries by kind, built once (rebuilt if entries were added since): a lookup walks only its own kind, never the
        // whole content (thousands of entries) as it did on every card line
        private Dictionary<string, List<ContentEntry>> _byKind; private int _indexed = -1;
        private static readonly List<ContentEntry> None = new List<ContentEntry>();
        public IEnumerable<ContentEntry> OfKind(string file, string kind)
        {
            if (_byKind == null || _indexed != Entries.Count)
            {
                _byKind = new Dictionary<string, List<ContentEntry>>();
                foreach (var x in Entries) { List<ContentEntry> l; if (!_byKind.TryGetValue(x.Kind ?? "", out l)) { l = new List<ContentEntry>(); _byKind[x.Kind ?? ""] = l; } l.Add(x); }
                _indexed = Entries.Count;
            }
            List<ContentEntry> of; if (!_byKind.TryGetValue(kind ?? "", out of)) of = None;
            foreach (var e in of)
                if (file == null || e.File.EndsWith(file, StringComparison.OrdinalIgnoreCase)) yield return e;
        }

        private void ParseFile(string path)
        {
            string section = "";
            ContentEntry cur = null;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.TrimEnd();
                if (line.StartsWith("# ──")) { section = line.Trim('#', ' ', '─'); cur = null; continue; }
                if (line.StartsWith("#")) continue;                                    // a comment inside an entry is only a comment: it never ends the entry (it did, and the two black holes of bodies.unt, with a comment under their headers, lost every line: no black hole was ever made)
                if (line.Length == 0) { cur = null; continue; }
                if (line.StartsWith("@"))
                {
                    string[] head = line.Substring(1).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    cur = new ContentEntry { Kind = head[0], Id = head.Length > 1 ? head[1] : "", File = path.Replace('\\', '/'), Section = section };
                    Entries.Add(cur);
                    continue;
                }
                int colon = line.IndexOf(':');
                if (cur != null && colon > 0)
                {
                    string k = line.Substring(0, colon).Trim(), v = line.Substring(colon + 1).Trim();
                    cur.Props[k] = v; List<string> l; if (!cur.Lists.TryGetValue(k, out l)) cur.Lists[k] = l = new List<string>(); l.Add(v);
                }
            }
        }
    }
}
