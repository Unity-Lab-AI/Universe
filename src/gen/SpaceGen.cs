// Universe · gen · live generators for the deep zoom: galaxy structures (clusters, nebulas), the core's
// supermassive black hole, and full star systems. All built from the seed and the content files.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;

namespace Universe.Gen
{
    /// <summary>Distance units. One galaxy unit is 50,000 light-years (about a galaxy's radius).</summary>
    internal static class Units
    {
        public const double LightYearsPerUnit = 50000.0;
        public const double KmPerLightYear = 9.4607e12;
        public const double KmPerAu = 1.496e8;
        public const double AuPerLightYear = 63241.0;
        public static double Km(double km) { return km / KmPerLightYear / LightYearsPerUnit; }
        public static double Au(double au) { return Km(au * KmPerAu); }

        public static string Describe(double units)
        {
            double ly = units * LightYearsPerUnit;
            if (ly >= 1) return ly.ToString(ly >= 100 ? "N0" : "0.0") + " light-years";
            double au = ly * AuPerLightYear;
            if (au >= 0.01) return au.ToString(au >= 100 ? "N0" : "0.00") + " au";
            return (au * KmPerAu).ToString("N0") + " km";
        }
    }

    internal static class SpaceGen
    {
        // ── galaxy structures: globular clusters, open clusters, nebulas ──
        public static StarVertex[] Structures(string galaxyType, ulong seed, List<StarKind> kinds, out int clusterCount, out int nebulaCount)
        { return Structures(galaxyType, seed, kinds, out clusterCount, out nebulaCount, null); }

        /// <summary>The same, and each cluster and nebula as one feature (galaxy units: x, y, z, radius, kind: 0 globular cluster,
        /// 1 open cluster, 2 emission nebula, 3 reflection nebula), for picking.</summary>
        /// <summary>What a nebula is made of, by its kind and its own mix u (0 to 1): each part's name, its element (for its gas),
        /// its share, and the colour it shines or shows in. Emission: hydrogen glowing deep red (H-alpha), oxygen teal-green ([O III]),
        /// sulfur and nitrogen deeper reds; planetary: oxygen green-blue strongest, hydrogen and nitrogen red rims; supernova
        /// remnants: hydrogen with the star's own oxygen, sulfur, silicon and iron; dark clouds: molecular hydrogen, carbon monoxide,
        /// water ice, ammonia, methanol and ethanol (alcohols), formaldehyde, hydrogen cyanide, and dust; reflection: dust scattering blue.</summary>
        public sealed class Part { public string Name, El; public double Share; public float R, G, B; public Part(string n, string e, double s, float r, float g, float b) { Name = n; El = e; Share = s; R = r; G = g; B = b; } }
        public static Part[] Composition(int kind, double u)
        {
            Part[] p;
            switch (kind)
            {
                case 2: p = new[] { new Part("hydrogen (glowing red)", "H", 0.55 + 0.25 * (1 - u), 1f, 0.18f, 0.25f), new Part("oxygen (teal-green)", "O", 0.05 + 0.3 * u, 0.15f, 1f, 0.8f), new Part("sulfur (deep red)", "S", 0.02 + 0.06 * (1 - u), 0.75f, 0.08f, 0.1f), new Part("nitrogen (red)", "N", 0.03 + 0.07 * u, 1f, 0.3f, 0.3f), new Part("helium", "He", 0.09, 0.9f, 0.85f, 1f) }; break;
                case 3: p = new[] { new Part("dust: silicate and carbon grains (scattering blue)", "Si", 0.6, 0.35f, 0.5f, 1f), new Part("hydrogen", "H", 0.3, 0.5f, 0.55f, 0.9f), new Part("helium", "He", 0.1, 0.6f, 0.65f, 1f) }; break;
                case 4: p = new[] { new Part("oxygen (green-blue)", "O", 0.3 + 0.25 * u, 0.2f, 1f, 0.75f), new Part("hydrogen (red)", "H", 0.3, 1f, 0.2f, 0.3f), new Part("nitrogen (red rim)", "N", 0.08 + 0.15 * (1 - u), 1f, 0.35f, 0.3f), new Part("helium", "He", 0.12, 0.6f, 0.7f, 1f) }; break;
                case 5: p = new[] { new Part("hydrogen (red)", "H", 0.35, 1f, 0.25f, 0.3f), new Part("oxygen (teal)", "O", 0.2 + 0.2 * u, 0.2f, 0.9f, 0.85f), new Part("sulfur (deep red)", "S", 0.1, 0.8f, 0.1f, 0.15f), new Part("silicon", "Si", 0.08, 0.7f, 0.6f, 0.5f), new Part("iron", "Fe", 0.05 + 0.1 * (1 - u), 0.9f, 0.5f, 0.3f), new Part("neon", "Ne", 0.05, 1f, 0.45f, 0.2f) }; break;
                case 6: p = new[] { new Part("molecular hydrogen (H2)", "H", 0.7, 0.3f, 0.2f, 0.15f), new Part("carbon monoxide (CO)", "C", 0.08, 0.35f, 0.22f, 0.16f), new Part("water ice (H2O)", "O", 0.06, 0.4f, 0.4f, 0.45f), new Part("ammonia (NH3)", "N", 0.02, 0.35f, 0.3f, 0.25f),
                    new Part("methanol (CH3OH, an alcohol)", "C", 0.015 + 0.02 * u, 0.4f, 0.28f, 0.2f), new Part("ethanol (C2H5OH, an alcohol)", "C", 0.002 + 0.008 * u, 0.4f, 0.3f, 0.2f), new Part("formaldehyde (H2CO)", "O", 0.006, 0.35f, 0.25f, 0.2f), new Part("hydrogen cyanide (HCN)", "N", 0.004, 0.3f, 0.25f, 0.2f), new Part("vinyl alcohol and propanol (alcohols)", "C", 0.001 + 0.002 * u, 0.4f, 0.3f, 0.2f), new Part("ethyl formate (the raspberry ester)", "C", 0.0005 + 0.001 * u, 0.4f, 0.3f, 0.22f), new Part("dust grains", "Si", 0.1, 0.25f, 0.16f, 0.1f) }; break;
                default: p = new[] { new Part("hydrogen", "H", 0.9, 1f, 1f, 1f), new Part("helium", "He", 0.1, 1f, 1f, 1f) }; break;
            }
            double sum = 0; foreach (var q in p) sum += q.Share; foreach (var q in p) q.Share /= sum;
            return p;
        }
        // a nebula's colour: its parts' colours by their shares
        public static void Colour(int kind, double u, out float r, out float g, out float b)
        {
            r = g = b = 0; foreach (var q in Composition(kind, u)) { r += q.R * (float)q.Share; g += q.G * (float)q.Share; b += q.B * (float)q.Share; }
            float m = Math.Max(0.01f, Math.Max(r, Math.Max(g, b))); r /= m; g /= m; b /= m;
        }

        public static StarVertex[] Structures(string galaxyType, ulong seed, List<StarKind> kinds, out int clusterCount, out int nebulaCount, List<double[]> features)
        {
            // every kind of body a galaxy holds, in its real numbers for its kind (sampled, so the sky stays light), each a feature
            // (x, y, z, radius, kind): 0 globular cluster, 1 open cluster, 2 emission nebula, 3 reflection nebula, 4 planetary nebula,
            // 5 supernova remnant, 6 dark nebula (a cold cloud of dust and molecules). Real counts for a galaxy like ours: about 150
            // globular clusters, thousands of open clusters, nebulae and dark clouds, hundreds of planetary nebulae seen (thousands
            // there), a few hundred supernova remnants; a giant elliptical thousands of globulars, few young things, but planetary
            // nebulae and remnants from its old stars.
            var rng = new Rng(seed ^ 0xC1u);
            var list = new List<StarVertex>();
            // all from its kind's template (content/galaxies.unt), never its name: whether its young things lie on arms, whether
            // its bodies swarm in a ball or lie in a disc, how much it still makes stars, how much it holds, its old clusters
            bool spiral = GalaxyGen.Yes(galaxyType, "bodies-on-arms", false);
            bool elliptical = GalaxyGen.Word(galaxyType, "bodies-spread", "disc") == "ball";
            double young = GalaxyGen.Trait(galaxyType, "star-forming-rate", 0.2, seed);   // how much it is still making stars
            double size = GalaxyGen.Trait(galaxyType, "structure-size", 1, seed);
            // researched counts: the Milky Way about 150-180 globular clusters, a few tens of thousands of open clusters (about 10,000
            // known), about 10,000-30,000 planetary nebulae (about 3,500 known), about 1,000 supernova remnants (about 300 known);
            // a giant elliptical like M87 about 12,000-15,000 globular clusters
            int globulars = (int)(GalaxyGen.Trait(galaxyType, "globular-clusters", 160, seed) * (0.8 + 0.4 * rng.NextDouble()));
            int opens = (int)(3000 * young * size), emis = (int)(2000 * young * size), refl = (int)(500 * young * size), dark = (int)(3000 * young * size);
            int planetary = (int)(3000 * size), remnants = (int)((300 + 700 * young) * size);
            clusterCount = 0; nebulaCount = 0;
            Func<Vec3> disc = () =>
            {
                // in the disc: on the arms for a spiral, anywhere for the rest
                float rr = (float)Math.Pow(rng.NextDouble(), 0.7) * 0.85f + 0.1f;
                float ang = spiral ? rng.Int(2) * (float)Math.PI + (float)Math.Log(rr / 0.05f + 1) * 3.2f + rng.Gaussian() * 0.25f : (float)(rng.NextDouble() * Math.PI * 2);
                return new Vec3((float)Math.Cos(ang) * rr, rng.Gaussian() * (elliptical ? 0.3f : 0.01f), (float)Math.Sin(ang) * rr);
            };
            Func<Vec3> old = () => elliptical ? new Vec3(rng.Gaussian() * 0.35f, rng.Gaussian() * 0.3f, rng.Gaussian() * 0.35f) : disc();   // among the old stars
            float ly = (float)(1 / Units.LightYearsPerUnit);
            double mix = 0;                                                               // the next body's own mix (its make-up)
            Action<Vec3, float, int> add = (c, r, kind) => { if (features != null) features.Add(new double[] { c.X, c.Y, c.Z, r, kind, mix }); };
            for (int g = 0; g < globulars; g++)
            {
                // halo: tight balls of ancient stars
                var c = new Vec3(rng.Gaussian() * 0.5f, rng.Gaussian() * 0.35f, rng.Gaussian() * 0.5f); float r = 60 * ly;
                int pts = globulars > 3000 ? 6 : globulars > 300 ? 20 : 40;   // drawn lightly, so even thousands stay smooth
                for (int i = 0; i < pts; i++) list.Add(Star(c + new Vec3(rng.Gaussian(), rng.Gaussian(), rng.Gaussian()) * (r * (float)Math.Abs(rng.Gaussian()) * 0.6f), 0f, 1f, 0.75f, 0.45f, rng.Range(0.05f, 0.2f)));
                clusterCount++; add(c, r, 0);
            }
            for (int k = 0; k < opens; k++)
            {
                var c = disc(); float r = 15 * ly;
                for (int i = 0; i < 6; i++) list.Add(Star(c + new Vec3(rng.Gaussian(), rng.Gaussian() * 0.4f, rng.Gaussian()) * r, 0f, 0.7f, 0.8f, 1f, rng.Range(0.1f, 0.35f)));
                clusterCount++; add(c, r, 1);
            }
            for (int k = 0; k < emis + refl; k++)
            {
                // emission nebulae: hydrogen glowing red round hot young stars; reflection nebulae: dust lit blue
                bool emission = k < emis; var c = disc(); float r = rng.Range(40, 300) * ly;
                mix = rng.NextDouble(); float cr, cg, cb; Colour(emission ? 2 : 3, mix, out cr, out cg, out cb);   // its colours from what it is made of
                for (int i = 0; i < 8; i++)
                {
                    // each part of it glows in its own part's colour (oxygen's teal toward its middle, hydrogen's red throughout)
                    var parts = Composition(emission ? 2 : 3, mix); var pp = parts[(int)(rng.NextDouble() * parts.Length) % parts.Length];
                    float wr = (cr + pp.R) * 0.5f, wg = (cg + pp.G) * 0.5f, wb = (cb + pp.B) * 0.5f;
                    list.Add(Star(c + new Vec3(rng.Gaussian(), rng.Gaussian() * 0.5f, rng.Gaussian()) * r * (pp.El == "O" ? 0.6f : 1f), r * 2.5f, wr, wg, wb, 0.03f));
                }
                nebulaCount++; add(c, r * 1.5f, emission ? 2 : 3);
            }
            for (int k = 0; k < dark; k++)
            {
                // dark nebulae: cold clouds of dust and molecules, shown by the faint dusky glow at their edges (stars are born inside)
                var c = disc(); float r = rng.Range(5, 60) * ly;
                mix = rng.NextDouble(); float dr, dg, db; Colour(6, mix, out dr, out dg, out db);
                for (int i = 0; i < 3; i++) list.Add(Star(c + new Vec3(rng.Gaussian(), rng.Gaussian() * 0.4f, rng.Gaussian()) * r, r * 1.8f, dr * 0.4f, dg * 0.4f, db * 0.4f, 0.02f));
                nebulaCount++; add(c, r * 1.3f, 6);
            }
            for (int k = 0; k < planetary; k++)
            {
                // planetary nebulae: glowing shells a light-year or so across, puffed off by dying stars of about one solar mass (in every galaxy)
                var c = old(); float r = rng.Range(0.3f, 1.5f) * ly;
                mix = rng.NextDouble(); float pr, pg, pb; Colour(4, mix, out pr, out pg, out pb);
                for (int i = 0; i < 4; i++) { var d = new Vec3(rng.Gaussian(), rng.Gaussian(), rng.Gaussian()).Normalized; list.Add(Star(c + d * r, r * 0.8f, pr, pg, pb, 0.06f)); }
                nebulaCount++; add(c, r * 1.2f, 4);
            }
            for (int k = 0; k < remnants; k++)
            {
                // supernova remnants: expanding shells tens of light-years across, from stars that exploded
                var c = rng.NextDouble() < young / (young + 0.3) ? disc() : old(); float r = rng.Range(10, 80) * ly;
                mix = rng.NextDouble(); float sr, sg, sb; Colour(5, mix, out sr, out sg, out sb);
                for (int i = 0; i < 6; i++) { var d = new Vec3(rng.Gaussian(), rng.Gaussian(), rng.Gaussian()).Normalized; list.Add(Star(c + d * r, r * 0.5f, sr, sg, sb, 0.03f)); }
                nebulaCount++; add(c, r * 1.1f, 5);
            }
            return list.ToArray();
        }

        // ── the core's supermassive black hole ──
        public sealed class BlackHole
        {
            public double MassSol, HorizonUnits; public bool Active;
            public StarVertex[] Disk, Jets, Glow;
        }

        public static BlackHole CoreBlackHole(ContentLibrary content, ulong seed)
        {
            var rng = new Rng(seed ^ 0xB1ACu);
            double lo = 1e6, hi = 1e10;
            foreach (var e in content.OfKind("black-holes.unt", "blackhole"))
                if (e.Id == "supermassive") e.TryRange("mass-sol", out lo, out hi);
            double mass = Math.Exp(Math.Log(lo) + rng.NextDouble() * (Math.Log(Math.Min(hi, 1e9)) - Math.Log(lo)));
            Physics.Load(content); double rs = Units.Km(Physics.HorizonKm(mass));   // event horizon radius (2GM/c², content/physics.unt)
            var bh = new BlackHole { MassSol = mass, HorizonUnits = rs, Active = rng.NextDouble() < 0.5 };

            var disk = new List<StarVertex>();
            for (int i = 0; i < 9000; i++)
            {
                // accretion disk from just outside the photon sphere outward; hotter (whiter) toward the inside
                double r = rs * (2.6 + Math.Pow(rng.NextDouble(), 1.6) * 30);
                double a = rng.NextDouble() * Math.PI * 2;
                float heat = (float)Math.Max(0, 1 - (r / rs - 2.6) / 30);
                disk.Add(Star(new Vec3((float)(Math.Cos(a) * r), (float)(rng.Gaussian() * rs * 0.08), (float)(Math.Sin(a) * r)),
                    (float)(rs * 0.5), 1f, 0.45f + heat * 0.5f, 0.15f + heat * 0.8f, 0.05f + heat * 0.12f));
            }
            for (int i = 0; i < 1200; i++)
            {
                // photon ring: light bent all the way around the hole
                double a = rng.NextDouble() * Math.PI * 2, r = rs * 1.5;
                disk.Add(Star(new Vec3((float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r * 0.9), (float)(rng.Gaussian() * rs * 0.05)), (float)(rs * 0.2), 1f, 0.9f, 0.7f, 0.18f));
            }
            bh.Disk = disk.ToArray();

            var jets = new List<StarVertex>();
            if (bh.Active)
                for (int i = 0; i < 5000; i++)
                {
                    // jets fired from the poles (content: jets, relativistic jet)
                    double h = rs * (3 + Math.Pow(rng.NextDouble(), 2.2) * 3000) * (rng.NextDouble() < 0.5 ? 1 : -1);
                    double w = Math.Abs(h) * 0.04 * Math.Abs(rng.Gaussian());
                    double a = rng.NextDouble() * Math.PI * 2;
                    jets.Add(Star(new Vec3((float)(Math.Cos(a) * w), (float)h, (float)(Math.Sin(a) * w)), (float)(Math.Abs(h) * 0.02 + rs * 0.3), 0.45f, 0.65f, 1f, 0.06f));
                }
            bh.Jets = jets.ToArray();

            var glow = new List<StarVertex>();
            for (int i = 0; i < 2500; i++)
            {
                // dense star crowd around the core, spread over the inner light-years
                Vec3 d = new Vec3(rng.Gaussian(), rng.Gaussian() * 0.7f, rng.Gaussian()).Normalized;
                double r = Units.Au(500) * Math.Pow(rng.NextDouble(), 2.5) * 4000;
                glow.Add(Star(d * (float)r, 0, 1f, 0.8f, 0.55f, rng.Range(0.1f, 0.5f)));
            }
            bh.Glow = glow.ToArray();
            return bh;
        }

        // ── a full star system ──
        public sealed class Body
        {
            public string Kind; public double OrbitUnits, RadiusUnits, Period, Phase; public float R, G, B;
            /// <summary>Its kind's template in content/terrain.unt (null for a moon), and whether it is a giant (no ground).</summary>
            public string Terrain; public bool Giant, Rings, Locked;   // Locked: one face always to its star (by its orbit: physics.unt tidal-lock)
            /// <summary>What it is made of, for its card; and how much the tour's director wants to visit it.</summary>
            public string[] MakeUp, MakeUpDry; public double Interest = 1; public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc) public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc) public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc) public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc) public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc) public double MassKg;   // its mass (kg) from its size and its kind's density (content/terrain.unt density-g-cc)
            // its orbit's tilt to the system's plane and where it crosses it (radians): planets lie within a few degrees of one
            // plane (ours: Mercury 7, Pluto 17; around other stars mostly 1 to 3 degrees between planets, some far out of line);
            // close moons in their planet's plane, far captured ones at any tilt, even going round backwards
            public double Incl, Node;
            /// <summary>Its orbit's eccentricity (0 a circle) and where its closest point lies along it, from where it crosses the plane (radians).</summary>
            public double Ecc, Peri;
            /// <summary>How far its closest point turns round what it circles each orbit (radians): relativity's advance of the perihelion.</summary>
            public double PeriRate;
            /// <summary>Where on its orbit it is at angle <paramref name="ang"/> from where it crosses the plane, from what it circles.</summary>
            public void At(double ang, out double x, out double y, out double z)
            {
                // ang runs evenly with time (the mean anomaly): Kepler's equation gives where on its ellipse that puts it, fast
                // at its closest point, slow at its farthest; its distance from what it circles follows
                double e = Math.Min(0.95, Math.Max(0, Ecc)), M = ang % (2 * Math.PI), E = M + e * Math.Sin(M);
                for (int it = 0; it < 12; it++) { double dE = (E - e * Math.Sin(E) - M) / (1 - e * Math.Cos(E)); E -= dE; if (Math.Abs(dE) < 1e-12) break; }
                double nu = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2)), r = OrbitUnits * (1 - e * Math.Cos(E)), u = nu + Peri + PeriRate * (ang - Phase) / (2 * Math.PI);   // (its closest point turning on, by relativity, with each orbit)
                double cu = Math.Cos(u), su = Math.Sin(u), cn = Math.Cos(Node), sn = Math.Sin(Node), ci = Math.Cos(Incl), si = Math.Sin(Incl);
                x = r * (cn * cu - sn * su * ci); y = r * su * si; z = r * (sn * cu + cn * su * ci);
            }
            public int Parent = -1; // index of the planet a moon circles
        }
        private static double Frac(double v) { return v - Math.Floor(v); }
        public sealed class StarSystem
        {
            public string StarName; public double StarRadius, Luminosity, FrostLineAu, HabitableAu, MassSol, TempK, RadiusSol;
            public double BeltInAu, BeltOutAu, KuiperInAu, KuiperOutAu, OortInAu, OortOutAu;   // the asteroid belt (0 if none), Kuiper belt and Oort cloud
            public float SR, SG, SB;
            public readonly List<Body> Bodies = new List<Body>();
            public StarVertex[] Static; // belts, Kuiper belt, Oort cloud, orbit lines
            public float[] StaticRate;  // each point's turn a year (radians) about the star: the belts' rubble on its orbits (Kepler's rate at its distance); 0 for the orbit lines and the Oort cloud
            public int Planets;
        }

        public static StarSystem System(ContentLibrary content, StarKind kind, ulong seed, string layout = null)
        {
            var rng = new Rng(seed);
            var sys = new StarSystem { StarName = kind.Name, SR = kind.R, SG = kind.G, SB = kind.B };
            double massLo = 1, massHi = 1;
            foreach (var e in content.OfKind("stars.unt", "star"))
                if (e.Id == kind.Id) e.TryRange("mass-sol", out massLo, out massHi);
            double mass = Math.Max(0.08, massLo + rng.NextDouble() * (massHi - massLo));
            sys.Luminosity = Math.Pow(mass, 3.5); sys.MassSol = mass;
            // its real size, from content (giants and supergiants are far wider than their mass alone would make them)
            double rLo = -1, rHi = -1;
            foreach (var e in content.OfKind("stars.unt", "star")) if (e.Id == kind.Id) e.TryRange("radius-sol", out rLo, out rHi);
            double radiusSol = rLo > 0 ? rLo + rng.NextDouble() * (rHi - rLo) : Math.Pow(mass, 0.8);
            sys.StarRadius = Units.Km(696000 * radiusSol);
            // how bright it shines, from its size and surface temperature (one solar radius at 5772 K: one solar luminosity), when both are known
            double tLo = -1, tHi = -1;
            foreach (var e in content.OfKind("stars.unt", "star")) if (e.Id == kind.Id) e.TryRange("temp-k", out tLo, out tHi);
            if (tLo > 0 && rLo > 0) { double tk = tLo + rng.NextDouble() * (tHi - tLo); sys.Luminosity = radiusSol * radiusSol * Math.Pow(tk / 5772, 4); }
            sys.RadiusSol = radiusSol; sys.TempK = 5772 * Math.Pow(sys.Luminosity / (radiusSol * radiusSol), 0.25);   // its surface temperature, from its brightness and size
            double frostSun = ZoneAu(content, "frost-line", 2.7), hzSun = ZoneAu(content, "habitable", 1.0);
            double scale = Math.Sqrt(sys.Luminosity);
            sys.FrostLineAu = frostSun * scale; sys.HabitableAu = hzSun * scale;

            // its layout from the system kinds in content/solar-systems.unt (none named here): the usual one (usual) first,
            // then the kind asked for over it, each trait it gives taking the place of the usual one's; planets: how many,
            // first-orbit-au and first-orbit-spread: the first world's orbit, scales-with-star: moved out by the star's
            // brightness, spacing: each orbit over the one before; share-in-universe: how often a system is of that kind
            int count; double a, step; double lo, hi;
            Func<string, string, double, double, double[]> sysT = (id, key, l0, h0) => { foreach (var e in content.OfKind("solar-systems.unt", "system")) if (e.Id == id && e.TryRange(key, out lo, out hi)) return new[] { lo, hi }; return null; };
            Func<string, bool> scales = id => { foreach (var e in content.OfKind("solar-systems.unt", "system")) if (e.Id == id && e.Props.ContainsKey("scales-with-star")) return e.Get("scales-with-star", "yes") == "yes"; return true; };
            Action<string, bool> layoutOf = (id, first) =>
            {
                var pl = sysT(id, "planets", 0, 0); var fo = sysT(id, "first-orbit-au", 0, 0); var fs2 = sysT(id, "first-orbit-spread", 0, 0); var sp = sysT(id, "spacing", 0, 0);
                if (pl != null || first) count = (int)(pl != null ? pl[0] : 3) + rng.Int((int)(pl != null ? pl[1] - pl[0] : 7) + 1);
                if (fo != null || first) a = (fo != null ? fo[0] : 0.3) * (scales(id) ? scale : 1) * rng.Range((float)(fs2 != null ? fs2[0] : 0.6), (float)(fs2 != null ? fs2[1] : 1.4));
                if (sp != null || first) step = rng.Range((float)(sp != null ? sp[0] : 1.45), (float)(sp != null ? sp[1] : 1.9));
            };
            count = 0; a = 0; step = 1;
            layoutOf("usual", true);
            if (layout != null && layout != "usual") layoutOf(layout, false);
            var hj = sysT("hot-jupiter", "share-in-universe", 0, 0);
            bool hotJupiter = layout == "hot-jupiter" || (layout == null && rng.NextDouble() < (hj != null ? hj[0] : 0.1));
            // how far out of the system's plane its worlds lie: born in one flat disc they stay within a few degrees, but a
            // share of systems are stirred (a star passing close, a second star) and their worlds scattered tens of degrees
            // apart (content/solar-systems.unt tilt-spread, wild-tilt-share, wild-tilt; the stirred kind by its share, from the
            // seed itself so every other system stays as it was)
            var stirredShare = sysT("stirred", "share-in-universe", 0, 0);
            ulong sh = (seed ^ 0x5A17UL) * 0x9E3779B97F4A7C15UL; sh ^= sh >> 29;
            bool stirred = layout == "stirred" || (layout == null && stirredShare != null && (sh >> 11) * (1.0 / 9007199254740992.0) < stirredShare[0]);
            string tiltKind = stirred ? "stirred" : (layout ?? "usual");
            double[] ts = sysT(tiltKind, "tilt-spread", 0, 0) ?? sysT("usual", "tilt-spread", 0, 0), ws = sysT(tiltKind, "wild-tilt-share", 0, 0) ?? sysT("usual", "wild-tilt-share", 0, 0), wt = sysT(tiltKind, "wild-tilt", 0, 0) ?? sysT("usual", "wild-tilt", 0, 0);
            double tiltSpread = ts != null ? ts[0] : 1.5, wildShare = ws != null ? ws[0] : 0.06, wildLo = wt != null ? wt[0] : 8, wildHi = wt != null ? wt[1] : 33;
            double lastRocky = 0, firstGiant = 0, outer = 0;
            for (int i = 0; i < count; i++, a *= step)
            {
                var b = new Body { OrbitUnits = Units.Au(a), Phase = rng.NextDouble() * Math.PI * 2, Period = Math.Sqrt(a * a * a / mass) };
                bool giant = a > sys.FrostLineAu || (hotJupiter && i == 0);
                // which kind of world forms here: every kind in content/terrain.unt whose rules fit this orbit (where it lies
                // against the habitable zone and the frost line, how bright its star is), one chosen by their weights; its
                // size, moons, colour and make-up from its template
                var k = WorldKind(content, giant ? (hotJupiter && i == 0 ? "hot-jupiter" : "giant") : "rocky", a / sys.HabitableAu, a / sys.FrostLineAu, sys.Luminosity, rng);
                b.Terrain = k.Id; b.Giant = giant; b.Kind = k.Label; b.MakeUp = k.MakeUp; b.MakeUpDry = k.MakeUpDry; b.Interest = k.Interest;
                b.RadiusUnits = Units.Km(k.SpreadLo != k.SpreadHi ? k.SizeKm * rng.Range((float)k.SpreadLo, (float)k.SpreadHi) : k.SizeKm * k.SpreadLo);
                b.R = k.Dot[0]; b.G = k.Dot[1]; b.B = k.Dot[2]; b.MassKg = MassOf(b.RadiusUnits, k.Density);
                b.Locked = a < LockAu(content) * Math.Pow(mass, 1 / 3.0);                      // close enough to its star to be tidally locked: its day is its year
                // rings by its template's chance (rings-chance), from its own place, so the seed's other worlds stay as they were
                { ulong rh = (seed ^ (ulong)(i + 1) * 0x9E3779B97F4A7C15UL) * 0xBF58476D1CE4E5B9UL; rh ^= rh >> 31; b.Rings = (rh >> 11) * (1.0 / 9007199254740992.0) < k.RingChance; }
                if (giant) { if (firstGiant == 0) firstGiant = a; } else lastRocky = a;
                sys.Bodies.Add(b); outer = a;
                int planetIndex = sys.Bodies.Count - 1;
                var mk = WorldKind(content, null, 0, 0, 0, null, b.Terrain); int moons = mk.MoonsLo + rng.Int(mk.MoonsHi - mk.MoonsLo + 1);
                for (int m = 0; m < moons; m++)
                {
                    var mo = new Body
                    {
                        Parent = planetIndex, OrbitUnits = b.RadiusUnits * rng.Range(3f, 40f),
                        RadiusUnits = b.RadiusUnits * rng.Range(0.03f, 0.12f), Period = rng.Range(0.002f, 0.08f), Phase = rng.NextDouble() * Math.PI * 2
                    };
                    // a moon is a world too: its kind from the same templates as the planets (moon-weight), by where its planet
                    // circles the star; most airless rock or ice, now and then an ocean, a lava world or one with air (chosen from
                    // its own place, so the system's other worlds stay as they were)
                    var mr = new Rng(seed ^ ((ulong)(planetIndex + 1) * 0x9E3779B97F4A7C15UL) ^ ((ulong)(m + 1) * 0xBF58476D1CE4E5B9UL));
                    var mkd = WorldKind(content, "moon", a / sys.HabitableAu, a / sys.FrostLineAu, sys.Luminosity, mr);
                    mo.Kind = mkd.Label; mo.Terrain = mkd.Id; mo.MakeUp = mkd.MakeUp; mo.MakeUpDry = mkd.MakeUpDry; mo.Interest = mkd.Interest * 0.5;
                    mo.R = mkd.Dot[0]; mo.G = mkd.Dot[1]; mo.B = mkd.Dot[2]; mo.MassKg = MassOf(mo.RadiusUnits, mkd.Density);
                    sys.Bodies.Add(mo);
                }
            }
            sys.Planets = count;
            foreach (var b in sys.Bodies)
            {
                double u1 = Frac(Math.Sin(b.Phase * 91.7 + b.OrbitUnits * 1e-3) * 43758.5453), u2 = Frac(Math.Sin(b.Phase * 37.3 + 4.1) * 24634.6345), u3 = Frac(Math.Sin(b.Phase * 13.1 + 9.7) * 15731.743);
                b.Node = u2 * Math.PI * 2;
                if (b.Parent < 0)
                    b.Incl = (u3 < wildShare ? wildLo + u1 * (wildHi - wildLo) : Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, u1))) * tiltSpread) * Math.PI / 180;   // most within the kind's spread; a share far out of line
                else
                {
                    double far = b.OrbitUnits / Math.Max(1e-30, sys.Bodies[b.Parent].RadiusUnits);
                    b.Incl = far > 25 ? Math.Acos(1 - 2 * u1) : u1 * 2 * Math.PI / 180;    // close moons in the planet's plane; far ones captured, any tilt (past 90: backwards)
                }
                // its orbit from its speed, as the laws of motion have it (content/physics.unt orbits): where it was placed it
                // moves at some share of the circular speed there (f); exactly 1 is a circle, a little off an oval, near escape
                // speed (root 2) a long thin loop flown through its closest point ultra-fast and crawled through its farthest.
                // Faster than circular, the place is its closest point: a = r / (2 - f^2), e = f^2 - 1; slower, its farthest:
                // e = 1 - f^2. Everything else (its size, its period, its speed everywhere) follows
                double u4 = Frac(Math.Sin(b.Phase * 7.7 + 2.3) * 31415.9265), u5 = Frac(Math.Sin(b.Phase * 3.1 + 7.9) * 27182.818), u6 = Frac(Math.Sin(b.Phase * 5.3 + 1.1) * 16180.339);
                b.Peri = u5 * Math.PI * 2;
                double f;
                if (b.Parent < 0)
                    f = u6 < Orbit(content, "wild-share", 0.08) ? Orbit(content, "wild-speed", 1.15) + (Orbit(content, "wild-speed", 1.38, true) - Orbit(content, "wild-speed", 1.15)) * u4
                      : 1 + Orbit(content, "planet-speed-spread", 0.06) * Math.Sqrt(-2 * Math.Log(Math.Max(1e-9, u4))) * Math.Cos(u5 * Math.PI * 2);   // most near circular, a share wild
                else
                {
                    double far = b.OrbitUnits / Math.Max(1e-30, sys.Bodies[b.Parent].RadiusUnits);
                    f = far > Orbit(content, "moon-far-radii", 25) ? Orbit(content, "moon-far-speed", 1.05) + (Orbit(content, "moon-far-speed", 1.3, true) - Orbit(content, "moon-far-speed", 1.05)) * u4 : 1 + Orbit(content, "moon-close-speed", 0.02) * (u4 * 2 - 1);
                }
                f = Math.Max(0.3, Math.Min(1.39, f)); double f2 = f * f, r0 = b.OrbitUnits;
                b.OrbitUnits = r0 / (2 - f2); b.Ecc = Math.Abs(f2 - 1);
                b.Period *= Math.Pow(b.OrbitUnits / r0, 1.5);                                   // Kepler's third law, from the orbit's true size
                if (f < 1) b.Peri += Math.PI;                                                     // (placed at its farthest: its closest point lies opposite)
                // relativity: the orbit's closest point turns round the star, by 6 pi G M / (c^2 a (1 - e^2)) each orbit (Mercury's
                // 43 arcseconds a century; whole turns each orbit close to a black hole)
                if (b.Parent < 0)
                {
                    double aM = b.OrbitUnits / Units.Km(1) * 1000, gm = Physics.GmSol * sys.MassSol;
                    b.PeriRate = 6 * Math.PI * gm / (Physics.C * Physics.C * aM * Math.Max(1e-9, 1 - b.Ecc * b.Ecc));
                }
            }

            var stat = new List<StarVertex>(); var rates = new List<float>();
            // orbit lines
            foreach (var b in sys.Bodies)
                if (b.Parent < 0)
                    for (int k = 0; k < 1200; k++)
                    {
                        // a faint continuous line, its points at random along the orbit, never evenly spaced dots: two hundred and forty
                        // evenly spaced dots on every orbit had read, from the system's edge, as rows of bodies set out in straight lines
                        double t = rng.NextDouble() * Math.PI * 2;
                        { double ox, oy, oz; b.At(t, out ox, out oy, out oz); stat.Add(Star(new Vec3((float)ox, (float)oy, (float)oz), 0, 0.35f, 0.45f, 0.6f, 0.025f)); rates.Add(0); }   // (faint: a guide to the orbit, never a drawn ring of bodies)
                    }
            // asteroid belt in the gap between the rocky planets and the giants
            if (firstGiant > 0 && lastRocky > 0)
            {
                Ring(stat, rates, rng, Units.Au(lastRocky * 1.15), Units.Au(firstGiant * 0.85), 3000, 0.02, 0.7f, 0.6f, 0.5f, 0.16f, sys.MassSol);   // (faint: a real belt is rubble spread thin across a huge ring, a haze from afar, never a white annulus)
                sys.BeltInAu = lastRocky * 1.15; sys.BeltOutAu = firstGiant * 0.85;
            }
            // Kuiper belt beyond the last planet, and the Oort cloud far out
            Ring(stat, rates, rng, Units.Au(outer * 1.3), Units.Au(outer * 2.0), 4000, 0.05, 0.6f, 0.75f, 0.9f, 0.12f, sys.MassSol);
            sys.KuiperInAu = outer * 1.3; sys.KuiperOutAu = outer * 2.0; sys.OortInAu = 2000 * scale; sys.OortOutAu = 50000 * scale;
            for (int i = 0; i < 6000; i++)
            {
                Vec3 d = new Vec3(rng.Gaussian(), rng.Gaussian(), rng.Gaussian()).Normalized;
                double r = Units.Au(rng.Range(2000, 50000) * scale);
                stat.Add(Star(d * (float)r, 0, 0.55f, 0.7f, 0.85f, 0.06f)); rates.Add(0);
            }
            sys.Static = stat.ToArray(); sys.StaticRate = rates.ToArray();
            return sys;
        }

        // ── the kinds of world a system can make: templates in content/terrain.unt, each with the rules for where it forms ──
        public sealed class WorldKindRule
        {
            public string Id, Label, Forms; public double HzLo = -1, HzHi = -1, FrostLo = -1, FrostHi = -1, LumLo = -1, LumHi = -1, Weight = 1, Interest = 1;
            public double SizeKm = 6371, SpreadLo = 1, SpreadHi = 1, RingChance, MoonWeight; public int MoonsLo, MoonsHi; public float[] Dot = { 0.75f, 0.65f, 0.55f };
            public string[] MakeUp, MakeUpDry; public double Density = 5.5;   // g/cc (content/terrain.unt density-g-cc)
        }
        private static List<WorldKindRule> _kinds;
        private static List<WorldKindRule> Kinds(ContentLibrary content)
        {
            if (_kinds != null) return _kinds;
            var list = new List<WorldKindRule>(); var ci = global::System.Globalization.CultureInfo.InvariantCulture; var nf = global::System.Globalization.NumberStyles.Float;
            foreach (var e in content.OfKind("terrain.unt", "terrain"))
            {
                if (!e.Props.ContainsKey("forms") && !e.Props.ContainsKey("moon-weight")) continue;
                var k = new WorldKindRule { Id = e.Id, Label = e.Get("label", e.Name), Forms = e.Get("forms", "rocky") };
                double lo, hi;
                if (e.TryRange("orbit-hz", out lo, out hi)) { k.HzLo = lo; k.HzHi = hi; }
                if (e.TryRange("orbit-frost", out lo, out hi)) { k.FrostLo = lo; k.FrostHi = hi; }
                if (e.TryRange("star-brightness", out lo, out hi)) { k.LumLo = lo; k.LumHi = hi; }
                if (e.TryRange("weight", out lo, out hi)) k.Weight = lo;
                if (e.TryRange("interest", out lo, out hi)) k.Interest = lo;
                if (e.TryRange("moon-weight", out lo, out hi)) k.MoonWeight = lo;
                if (e.TryRange("rings-chance", out lo, out hi)) k.RingChance = lo;
                if (e.TryRange("size-km", out lo, out hi)) k.SizeKm = lo;
                if (e.TryRange("density-g-cc", out lo, out hi)) k.Density = lo;
                if (e.TryRange("size-spread", out lo, out hi)) { k.SpreadLo = lo; k.SpreadHi = hi; }
                if (e.TryRange("moons", out lo, out hi)) { k.MoonsLo = (int)lo; k.MoonsHi = (int)hi; }
                var w = e.Get("dot-colour", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries); float x, y, z;
                if (w.Length == 3 && float.TryParse(w[0], nf, ci, out x) && float.TryParse(w[1], nf, ci, out y) && float.TryParse(w[2], nf, ci, out z)) k.Dot = new[] { x, y, z };
                Func<string, string[]> lines = key => { var v = e.Get(key, ""); if (v.Length == 0) return null; var parts = v.Split(';'); for (int q = 0; q < parts.Length; q++) parts[q] = parts[q].Trim(); return parts; };
                k.MakeUp = lines("makeup"); k.MakeUpDry = lines("makeup-dry");
                list.Add(k);
            }
            _kinds = list; return list;
        }
        /// <summary>The kind of world that forms in a place: <paramref name="forms"/> rocky, giant or hot-jupiter; x the orbit over
        /// the habitable zone's distance, f over the frost line's, lum the star's brightness (one solar luminosity 1). With
        /// <paramref name="byId"/>, that kind itself. Only one kind fitting: no chance spent, so a seed's worlds stay its own.</summary>
        public static WorldKindRule WorldKind(ContentLibrary content, string forms, double x, double f, double lum, Rng rng, string byId = null)
        {
            var all = Kinds(content);
            if (byId != null) { foreach (var k in all) if (k.Id == byId) return k; return new WorldKindRule { Id = byId, Label = byId }; }
            var fit = new List<WorldKindRule>(); double total = 0;
            bool moon = forms == "moon";
            foreach (var k in all)
            {
                if (moon ? k.MoonWeight <= 0 : k.Forms != forms) continue;      // (a moon: any kind that can be one, by its moon-weight)
                if (k.HzLo >= 0 && (x < k.HzLo || x >= k.HzHi)) continue;
                if (k.FrostLo >= 0 && (f < k.FrostLo || f >= k.FrostHi)) continue;
                if (k.LumLo >= 0 && (lum < k.LumLo || lum >= k.LumHi)) continue;
                fit.Add(k); total += moon ? k.MoonWeight : k.Weight;
            }
            if (fit.Count == 0) { foreach (var k in all) if (moon ? k.MoonWeight > 0 : k.Forms == forms) return k; return new WorldKindRule { Id = "default", Label = "World" }; }
            if (fit.Count == 1) return fit[0];
            double pick = rng.NextDouble() * total;
            foreach (var k in fit) { pick -= moon ? k.MoonWeight : k.Weight; if (pick <= 0) return k; }
            return fit[fit.Count - 1];
        }

        // a number of the law of orbit shapes (content/physics.unt orbits): a range's low end, or its high end
        private static double Orbit(ContentLibrary content, string key, double fallback, bool high = false)
        {
            foreach (var e in content.OfKind("physics.unt", "law")) { double lo, hi; if (e.Id == "orbits" && e.TryRange(key, out lo, out hi)) return high ? hi : lo; }
            return fallback;
        }

        // how close to a star of one solar mass a world is tidally locked (content/physics.unt tidal-lock)
        private static double LockAu(ContentLibrary content)
        {
            foreach (var e in content.OfKind("physics.unt", "law")) { double lo, hi; if (e.Id == "tidal-lock" && e.TryRange("lock-au-sol", out lo, out hi)) return lo; }
            return 0.5;
        }

        private static double ZoneAu(ContentLibrary content, string id, double fallback)
        {
            foreach (var e in content.OfKind("solar-systems.unt", "zone"))
                if (e.Id == id)
                {
                    string v = e.Get("solar-system-au", "").Replace("about", "").Trim();
                    var tmp = new ContentEntry(); tmp.Props["v"] = v;
                    double lo, hi; if (tmp.TryRange("v", out lo, out hi)) return (lo + hi) * 0.5;
                }
            return fallback;
        }

        private static void Ring(List<StarVertex> list, List<float> rates, Rng rng, double inner, double outer, int n, double thick, float r, float g, float b, float a, double massSol)
        {
            for (int i = 0; i < n; i++)
            {
                double rad = inner + rng.NextDouble() * (outer - inner), t = rng.NextDouble() * Math.PI * 2;
                rates.Add((float)(2 * Math.PI / Math.Sqrt(Math.Pow(rad / Units.Au(1), 3) / Math.Max(0.05, massSol))));   // (its turn a year: Kepler's third law at its distance)
                list.Add(Star(new Vec3((float)(Math.Cos(t) * rad), (float)(rng.Gaussian() * rad * thick), (float)(Math.Sin(t) * rad)), 0, r, g, b, a));
            }
        }

        /// <summary>A world's mass (kg) from its radius (units) and its density (g/cc): four thirds pi r cubed rho.</summary>
        public static double MassOf(double radiusUnits, double densityGcc) { double r = radiusUnits / Units.Km(1) * 1000; return 4.0 / 3 * Math.PI * r * r * r * densityGcc * 1000; }
        /// <summary>Gravity switched off (content/physics.unt @law gravity-off): the year it went off (NaN: on). From then on every
        /// body carries straight on at the speed it had on its orbit, and the star swells by <see cref="StarSwell"/>.</summary>
        public static double GravityOffYear = double.NaN;
        /// <summary>How much the star has swollen since gravity went off (1: not at all), set by the view each frame.</summary>
        public static double StarSwell = 1;

        /// <summary>
        /// Builds this frame's star, planet and moon billboards. Calm motion: a body whose on-screen turn would be faster
        /// than <paramref name="maxTurnPerSecond"/> is drawn as a soft orbit trail instead of a moving dot.
        /// </summary>
        public static int BodyVerts(StarSystem sys, double simYears, double yearsPerSecond, double maxTurnPerSecond, StarVertex[] outV, int trailPoints)
        {
            double fx, fy, fz;
            return BodyVerts(sys, simYears, yearsPerSecond, maxTurnPerSecond, outV, -1, 1.0, out fx, out fy, out fz);
        }

        /// <summary>Star and bodies, worked out in double precision, written relative to the focus body
        /// (-1 = the star) and multiplied by <paramref name="scale"/>, so a single planet can fill the screen
        /// without shaking. The focus position (unscaled, star-centred) is given back for anything drawn with it.</summary>
        public static int BodyVerts(StarSystem sys, double simYears, double yearsPerSecond, double maxTurnPerSecond, StarVertex[] outV,
            int focus, double scale, out double fx, out double fy, out double fz)
        {
            int count = sys.Bodies.Count;
            var px = new double[count]; var py = new double[count]; var pz = new double[count];
            double seconds = yearsPerSecond > 0 ? simYears / yearsPerSecond : 0;
            Action<double, double[], double[], double[], bool> place = (t, qx, qy, qz, calm) =>
            {
                for (int i = 0; i < count; i++)
                {
                    var b = sys.Bodies[i];
                    double period = Math.Max(1e-4, b.Period);
                    double turn = yearsPerSecond / period * Math.PI * 2;
                    // calm motion: a body that would turn faster than the limit is shown turning at the limit
                    double ang = !calm || turn <= maxTurnPerSecond || yearsPerSecond <= 0
                        ? b.Phase + t / period * Math.PI * 2
                        : b.Phase + seconds * maxTurnPerSecond;
                    double cx = b.Parent >= 0 ? qx[b.Parent] : 0, cy = b.Parent >= 0 ? qy[b.Parent] : 0, cz = b.Parent >= 0 ? qz[b.Parent] : 0, ox, oy, oz;
                    b.At(ang, out ox, out oy, out oz); qx[i] = cx + ox; qy[i] = cy + oy; qz[i] = cz + oz;
                }
            };
            if (double.IsNaN(GravityOffYear) || simYears <= GravityOffYear) place(simYears, px, py, pz, true);
            else
            {
                // gravity off: from the moment it went, every body carries straight on at the speed it had then (its place a
                // moment later less its place then, over that moment), a moon with its planet's motion and its own
                double t0 = GravityOffYear, hY = 1e-5; var ax = new double[count]; var ay = new double[count]; var az = new double[count];
                place(t0, px, py, pz, false); place(t0 + hY, ax, ay, az, false); double k = (simYears - t0) / hY;
                for (int i = 0; i < count; i++) { px[i] += (ax[i] - px[i]) * k; py[i] += (ay[i] - py[i]) * k; pz[i] += (az[i] - pz[i]) * k; }
            }
            fx = focus >= 0 && focus < count ? px[focus] : 0; fy = focus >= 0 && focus < count ? py[focus] : 0; fz = focus >= 0 && focus < count ? pz[focus] : 0;
            int n = 0;
            outV[n++] = Star(new Vec3((float)(-fx * scale), (float)(-fy * scale), (float)(-fz * scale)), (float)(sys.StarRadius * StarSwell * scale), sys.SR, sys.SG, sys.SB, 1f);
            for (int i = 0; i < count; i++)
            {
                var b = sys.Bodies[i];
                outV[n++] = Star(new Vec3((float)((px[i] - fx) * scale), (float)((py[i] - fy) * scale), (float)((pz[i] - fz) * scale)), (float)(b.RadiusUnits * scale),
                    b.R, b.G, b.B, b.Parent >= 0 ? 0.6f : 1f);
            }
            return n;
        }

        public static StarVertex Star(Vec3 p, float size, float r, float g, float b, float bright)
        {
            return new StarVertex { X = p.X, Y = p.Y, Z = p.Z, Size = size, R = r * bright, G = g * bright, B = b * bright, A = 1 };
        }
    }
}
