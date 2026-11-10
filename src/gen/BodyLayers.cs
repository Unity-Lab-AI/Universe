// Universe · gen · every body's layers from its surface to its middle (content @layer, in any content file), read by one
// reader for stars, giants, rocky worlds, moons and small bodies; and the make-up by element that a world's layers add up to
// (each layer's volume, times its density, times its mix's minerals, times each mineral's atoms by their mass).
using System;
using System.Collections.Generic;
using Universe.Core;

namespace Universe.Gen
{
    /// <summary>One layer of a body (content @layer): where it lies as shares of the radius from the middle, what it is.</summary>
    internal sealed class BodyLayer
    {
        public string Id, Name, Matter, Says, MakeUp, Mix, File;
        public double From, To, TLo, THi, DLo, DHi, PLo, PHi;
        /// <summary>The kinds of body that have it (null: every body of its file).</summary>
        public List<string> In;
        public bool Has(string kind) { return In == null || In.Contains(kind ?? ""); }
        // how far down the layer a share of the radius is: 0 at its top, 1 at its bottom
        private double Down(double share) { return Math.Max(0, Math.Min(1, (To - share) / Math.Max(1e-12, To - From))); }
        /// <summary>Its temperature (K) at a share of the radius, from its top's to its bottom's (on a log scale).</summary>
        public double TempK(double share) { return TLo > 0 && THi > 0 ? TLo * Math.Pow(THi / TLo, Down(share)) : 0; }
        /// <summary>Its density (g/cc) at a share of the radius, from its top's to its bottom's (on a log scale).</summary>
        public double Density(double share) { return DLo > 0 && DHi > 0 ? DLo * Math.Pow(DHi / DLo, Down(share)) : 0; }
        /// <summary>Its pressure (GPa) at a share of the radius, from its top's to its bottom's.</summary>
        public double PressureGpa(double share) { return PLo + (PHi - PLo) * Down(share); }
        /// <summary>Its mean density (g/cc): the middle of its range on the log scale.</summary>
        public double MeanDensity { get { return DLo > 0 && DHi > 0 ? Math.Sqrt(DLo * DHi) : Math.Max(DLo, DHi); } }
    }

    /// <summary>Every @layer in the content, by its file; looked up by the kind of body.</summary>
    internal sealed class BodyLayers
    {
        public const string Stars = "stars.unt", Giants = "gas-giants.unt", Worlds = "planets.unt", Small = "small-bodies.unt";
        private static BodyLayers _one; private static ContentLibrary _of;
        /// <summary>The layers of this content (read once).</summary>
        public static BodyLayers Of(ContentLibrary c) { if (_one == null || _of != c) { _one = new BodyLayers(c); _of = c; } return _one; }

        private readonly ContentLibrary _c;
        private readonly List<BodyLayer> _all = new List<BodyLayer>();
        private readonly Dictionary<string, List<BodyLayer>> _for = new Dictionary<string, List<BodyLayer>>();
        private BodyLayers(ContentLibrary c)
        {
            _c = c;
            foreach (var e in c.Entries)
            {
                if (e.Kind != "layer") continue;
                double a, b; if (!e.TryRange("from-r", out a, out b)) continue; double f = a; if (!e.TryRange("to-r", out a, out b)) continue;
                var l = new BodyLayer { Id = e.Id, Name = e.Name, Says = e.Get("says", ""), Matter = e.Get("matter", "air"), From = f, To = a, File = System.IO.Path.GetFileName(e.File),
                    MakeUp = e.Props.ContainsKey("makeup") ? e.Get("makeup", "") : null, Mix = e.Props.ContainsKey("mix") ? e.Get("mix", "") : null };
                double lo, hi;
                if (e.TryRange("temp-k", out lo, out hi)) { l.TLo = lo; l.THi = hi; }
                if (e.TryRange("density-g-cc", out lo, out hi)) { l.DLo = lo; l.DHi = hi; }
                if (e.TryRange("pressure-gpa", out lo, out hi)) { l.PLo = lo; l.PHi = hi; }
                if (e.Props.ContainsKey("in")) { l.In = new List<string>(); foreach (var q in e.Get("in", "").Split(',')) if (q.Trim().Length > 0) l.In.Add(q.Trim()); }
                _all.Add(l);
            }
        }

        /// <summary>Every layer in this file, whatever kind of body has it (cached).</summary>
        public List<BodyLayer> InFile(string file)
        {
            string key = "file|" + file; List<BodyLayer> l;
            if (_for.TryGetValue(key, out l)) return l;
            l = new List<BodyLayer>(); foreach (var q in _all) if (q.File.Equals(file, StringComparison.OrdinalIgnoreCase)) l.Add(q);
            _for[key] = l; return l;
        }
        /// <summary>The layers a body of this kind has in this file, from the top down (cached).</summary>
        public List<BodyLayer> For(string file, string kind)
        {
            string key = file + "|" + (kind ?? ""); List<BodyLayer> l;
            if (_for.TryGetValue(key, out l)) return l;
            l = new List<BodyLayer>();
            foreach (var q in _all) if (q.File.Equals(file, StringComparison.OrdinalIgnoreCase) && q.Has(kind)) l.Add(q);
            l.Sort((p, q) => q.To.CompareTo(p.To)); _for[key] = l; return l;
        }
        /// <summary>A solid world's or small body's layers (rocky worlds, moons, asteroids, comets), by its ground's kind
        /// (content/terrain.unt @terrain id): only layers that name the kind, so a world never takes another's.</summary>
        public List<BodyLayer> ForWorld(string terrainKind)
        {
            string key = "world|" + (terrainKind ?? ""); List<BodyLayer> l;
            if (_for.TryGetValue(key, out l)) return l;
            l = new List<BodyLayer>();
            foreach (var q in _all)
                if ((q.File.Equals(Worlds, StringComparison.OrdinalIgnoreCase) || q.File.Equals(Small, StringComparison.OrdinalIgnoreCase)) && q.In != null && q.In.Contains(terrainKind ?? "")) l.Add(q);
            if (l.Count == 0 && terrainKind != "default") l = ForWorld("default");
            else l.Sort((p, q) => q.To.CompareTo(p.To));
            _for[key] = l; return l;
        }
        /// <summary>The layer at a share of the radius among these (the thinnest that holds it); null outside them all.</summary>
        public static BodyLayer At(List<BodyLayer> layers, double share)
        {
            BodyLayer best = null;
            foreach (var l in layers) { if (share < l.From || share > l.To) continue; if (best == null || l.To - l.From < best.To - best.From) best = l; }
            return best;
        }

        // ── the make-up by element: each layer's mass share, its mix's minerals, each mineral's atoms by their mass ──
        private Dictionary<string, double> _elMass;
        private readonly Dictionary<string, Dictionary<string, double>> _mineralEls = new Dictionary<string, Dictionary<string, double>>();
        private readonly Dictionary<string, List<KeyValuePair<string, double>>> _mixes = new Dictionary<string, List<KeyValuePair<string, double>>>();
        private readonly Dictionary<string, List<KeyValuePair<string, double>>> _bulk = new Dictionary<string, List<KeyValuePair<string, double>>>();
        private double ElMass(string sym)
        {
            if (_elMass == null)
            {
                _elMass = new Dictionary<string, double>();
                foreach (var e in _c.OfKind("elements.unt", "element")) { double a, b; if (e.TryRange("mass-number", out a, out b) && a > 0) _elMass[e.Get("symbol", e.Id)] = a; }
            }
            double m; return _elMass.TryGetValue(sym, out m) ? m : 0;
        }
        // a mineral's elements by their share of its mass (from its atoms written out one by one, each by its mass number)
        private Dictionary<string, double> MineralEls(string id)
        {
            Dictionary<string, double> d; if (_mineralEls.TryGetValue(id, out d)) return d;
            d = new Dictionary<string, double>(); double tot = 0;
            foreach (var e in _c.OfKind("minerals.unt", "mineral"))
            {
                if (e.Id != id) continue;
                foreach (var a in e.Get("atoms", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) { double m = ElMass(a); if (m <= 0) continue; double v; d.TryGetValue(a, out v); d[a] = v + m; tot += m; }
                break;
            }
            if (tot > 0) { var ks = new List<string>(d.Keys); foreach (var k in ks) d[k] /= tot; }
            _mineralEls[id] = d; return d;
        }
        // a mix's minerals and their shares (content/minerals.unt @mix minerals:)
        private List<KeyValuePair<string, double>> MixMinerals(string id)
        {
            List<KeyValuePair<string, double>> l; if (_mixes.TryGetValue(id ?? "", out l)) return l;
            l = new List<KeyValuePair<string, double>>();
            foreach (var e in _c.OfKind("minerals.unt", "mix"))
            {
                if (e.Id != id) continue;
                foreach (var part in e.Get("minerals", "").Split(','))
                {
                    var bits = part.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); double s;
                    if (bits.Length == 2 && double.TryParse(bits[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s)) l.Add(new KeyValuePair<string, double>(bits[0], s));
                }
                break;
            }
            _mixes[id ?? ""] = l; return l;
        }
        /// <summary>A solid world's whole make-up by element, as shares of its mass, largest first: its layers (by its ground's
        /// kind) each weighed by its volume and density, each made of its mix (ground: the world's own surface mix).</summary>
        public List<KeyValuePair<string, double>> Bulk(string terrainKind, string groundMix) { return Bulk(terrainKind, groundMix, 0); }
        /// <summary>The same for one body: each layer's minerals scattered from its kind's by the body's seed, by its mix's own
        /// spread (content/minerals.unt vary: the logarithm of each share's factor at one sigma), so no two worlds alike.</summary>
        public List<KeyValuePair<string, double>> Bulk(string terrainKind, string groundMix, ulong seed)
        {
            string key = (terrainKind ?? "") + "|" + (groundMix ?? "") + "|" + seed; List<KeyValuePair<string, double>> r;
            if (_bulk.TryGetValue(key, out r)) return r; if (_bulk.Count > 512) _bulk.Clear();   // (one entry a body seen: kept to the last few hundred)
            var tot = new Dictionary<string, double>(); double all = 0;
            foreach (var l in ForWorld(terrainKind))
            {
                string mix = l.Mix == "ground" ? groundMix : l.Mix; var mins = MixMinerals(mix); if (mins.Count == 0) continue;
                double mass = (l.To * l.To * l.To - l.From * l.From * l.From) * l.MeanDensity, sum = 0;
                double vary = seed != 0 ? MixVary(mix) : 0; var shares = new double[mins.Count];
                for (int i = 0; i < mins.Count; i++) { shares[i] = mins[i].Value * (vary > 0 ? Math.Exp(vary * Gauss(seed, l.Id, mins[i].Key)) : 1); sum += shares[i]; }
                if (sum <= 0 || mass <= 0) continue;
                for (int i = 0; i < mins.Count; i++)
                    foreach (var el in MineralEls(mins[i].Key)) { double v; tot.TryGetValue(el.Key, out v); double add = mass * shares[i] / sum * el.Value; tot[el.Key] = v + add; all += add; }
            }
            r = new List<KeyValuePair<string, double>>();
            if (all > 0) foreach (var kv in tot) r.Add(new KeyValuePair<string, double>(kv.Key, kv.Value / all));
            r.Sort((p, q) => q.Value.CompareTo(p.Value)); _bulk[key] = r; return r;
        }
        private readonly Dictionary<string, double> _vary = new Dictionary<string, double>();
        private double MixVary(string id)
        {
            double v; if (_vary.TryGetValue(id ?? "", out v)) return v; v = 0;
            foreach (var e in _c.OfKind("minerals.unt", "mix")) if (e.Id == id) { double lo, hi; if (e.TryRange("vary", out lo, out hi)) v = lo; break; }
            _vary[id ?? ""] = v; return v;
        }
        // a standard normal number fixed by the body's seed, the layer and the mineral (Box-Muller from two hashed uniforms)
        private static double Gauss(ulong seed, string layer, string mineral)
        {
            ulong h = seed * 0x9E3779B97F4A7C15UL; foreach (char ch in layer + "/" + mineral) h = (h ^ ch) * 0x100000001B3UL;
            h ^= h >> 31; h *= 0xBF58476D1CE4E5B9UL; h ^= h >> 29;
            double u1 = ((h >> 11) + 1) / 9007199254740993.0, u2 = (((h * 0x94D049BB133111EBUL) >> 11)) / 9007199254740992.0;
            return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        }
        /// <summary>The make-up written short: the first few elements with their shares of the mass.</summary>
        public static string Words(List<KeyValuePair<string, double>> bulk, int most)
        {
            var parts = new List<string>();
            for (int i = 0; i < bulk.Count && i < most; i++) { double p = bulk[i].Value * 100; parts.Add(bulk[i].Key + " " + (p >= 10 ? p.ToString("0") : p.ToString("0.#")) + "%"); }
            return string.Join(", ", parts);
        }
    }
}
