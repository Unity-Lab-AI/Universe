// Universe · simulation · life in the sea at every size, shown at its own scale as you zoom:
// from megalodon-sized sharks down through fish, minnows, krill and drifting animals to amoebas.
// (Bacteria and viruses below that come from MicrobeWorld.) Only the ones near your current size are kept,
// around the camera: the game never simulates every animal in the sea.
using System;
using System.Collections.Generic;
using Universe.Core;
using Universe.Render;
using Universe.Gen;

namespace Universe.Sim
{
    internal sealed class SeaLife
    {
        public sealed class Kind { public string Name; public double SizeKm; public float R, G, B; public int Shape; public int Stage; }   // shape 0 fish, 1 drifter, 2 amoeba; Stage: what life must have reached first
        public static readonly Kind[] Kinds =
        {
            new Kind { Name = "megalodon-sized shark", SizeKm = 0.016, R = 0.42f, G = 0.47f, B = 0.52f, Shape = 0, Stage = 5 },
            new Kind { Name = "shark", SizeKm = 0.004, R = 0.5f, G = 0.55f, B = 0.6f, Shape = 0, Stage = 5 },
            new Kind { Name = "big fish", SizeKm = 0.0012, R = 0.35f, G = 0.45f, B = 0.65f, Shape = 0, Stage = 4 },
            new Kind { Name = "fish", SizeKm = 0.0003, R = 0.75f, G = 0.72f, B = 0.4f, Shape = 0, Stage = 4 },
            new Kind { Name = "minnow", SizeKm = 0.00005, R = 0.8f, G = 0.82f, B = 0.85f, Shape = 0, Stage = 4 },
            new Kind { Name = "krill", SizeKm = 0.00001, R = 0.95f, G = 0.5f, B = 0.4f, Shape = 1, Stage = 3 },
            new Kind { Name = "copepod (tiny drifting animal)", SizeKm = 0.000001, R = 0.85f, G = 0.85f, B = 0.6f, Shape = 1, Stage = 3 },
            new Kind { Name = "amoeba", SizeKm = 0.0000003, R = 0.6f, G = 0.85f, B = 0.75f, Shape = 2, Stage = 1 },
        };

        public sealed class Animal { public int Kind; public double X, Y, Z, Dx, Dy, Dz, Phase; }
        public readonly List<Animal> Animals = new List<Animal>();
        private readonly Rng _rng = new Rng(99);

        /// <summary>Keeps animals near the camera's size around it: those far too big or small, or far away, go; new ones arrive.</summary>
        /// <summary>The highest stage life has reached in this water: only animals that far up the ladder can be here.</summary>
        public int StageHere = 99;
        /// <summary>When the planet's own sea animals swim here, the stock fish are left out.</summary>
        public bool NoFish;

        public void Update(double viewKm, double camX, double camY, double camZ, Vec3 up, Vec3 ahead, double dt)
        {
            Animals.RemoveAll(a => Kinds[a.Kind].Stage > StageHere || (NoFish && Kinds[a.Kind].Shape == 0));
            Animals.RemoveAll(a =>
            {
                var k = Kinds[a.Kind];
                if (k.SizeKm < viewKm * 0.02 || k.SizeKm > viewKm * 60) return true;
                double dx = a.X - camX, dy = a.Y - camY, dz = a.Z - camZ;
                if (dx * ahead.X + dy * ahead.Y + dz * ahead.Z < 0) return true;           // behind you: gone, so new ones come in ahead
                return dx * dx + dy * dy + dz * dz > (k.SizeKm * 30) * (k.SizeKm * 30);
            });
            for (int i = 0; i < Kinds.Length; i++)
            {
                var k = Kinds[i];
                if (k.SizeKm < viewKm * 0.02 || k.SizeKm > viewKm * 60 || k.Stage > StageHere || (NoFish && k.Shape == 0)) continue;
                int have = 0; foreach (var a in Animals) if (a.Kind == i) have++;
                int want = k.Shape == 0 ? (k.SizeKm > 0.003 ? 3 : 18) : 40;
                for (; have < want; have++)
                {
                    // new arrivals come into view ahead of you, at a distance where they fill a fair part of the picture
                    double r = k.SizeKm * _rng.Range(2.5f, 12f);
                    var side = Vec3.Cross(ahead, up).Normalized; var lift = Vec3.Cross(side, ahead).Normalized;
                    var at = ahead * (float)r + side * (float)(r * _rng.Range(-0.6f, 0.6f)) + lift * (float)(r * _rng.Range(-0.35f, 0.35f));
                    var d = new Vec3((float)_rng.Range(-1, 1), (float)_rng.Range(-0.2f, 0.2f), (float)_rng.Range(-1, 1)).Normalized;
                    Animals.Add(new Animal { Kind = i, X = camX + at.X, Y = camY + at.Y, Z = camZ + at.Z, Dx = d.X, Dy = d.Y, Dz = d.Z, Phase = _rng.NextDouble() * 6.28 });
                }
            }
            foreach (var a in Animals)
            {
                var k = Kinds[a.Kind];
                double speed = k.SizeKm * (k.Shape == 0 ? 0.8 : k.Shape == 1 ? 0.3 : 0.05);    // body lengths a second
                a.X += a.Dx * speed * dt; a.Y += a.Dy * speed * dt; a.Z += a.Dz * speed * dt; a.Phase += dt * (k.Shape == 0 ? 4 : 1.5);
                if (_rng.NextDouble() < dt * 0.2) { var d = new Vec3((float)(a.Dx + _rng.Range(-0.5f, 0.5f)), (float)(a.Dy * 0.5 + _rng.Range(-0.1f, 0.1f)), (float)(a.Dz + _rng.Range(-0.5f, 0.5f))).Normalized; a.Dx = d.X; a.Dy = d.Y; a.Dz = d.Z; }
            }
        }

        /// <summary>Points for every animal, relative to the camera (km).</summary>
        public int Write(StarVertex[] outV, double camX, double camY, double camZ, Vec3 up, float light)
        {
            int n = 0;
            foreach (var a in Animals)
            {
                if (n > outV.Length - 12) break;
                var k = Kinds[a.Kind]; float s = (float)k.SizeKm;
                var c = new Vec3((float)(a.X - camX), (float)(a.Y - camY), (float)(a.Z - camZ));
                var dir = new Vec3((float)a.Dx, (float)a.Dy, (float)a.Dz);
                var side = Vec3.Cross(dir, up).Normalized;
                float r = k.R * light, g = k.G * light, b = k.B * light;
                if (k.Shape == 0)
                {
                    // a fish: a tapering body, a tail that sweeps side to side, and a fin on top
                    float sweep = (float)Math.Sin(a.Phase) * 0.12f;
                    for (int i = 0; i < 6; i++)
                    {
                        float t = i / 5f, w = (float)Math.Sin(Math.PI * (0.15 + 0.85 * (1 - t))) * 0.16f + 0.03f;
                        var p = c + dir * (s * (0.5f - t)) + side * (s * sweep * t * t);
                        outV[n++] = SpaceGen.Star(p, s * w, r, g, b, 0.9f);
                    }
                    var tail = c + dir * (-s * 0.55f) + side * (s * sweep * 1.4f);
                    outV[n++] = SpaceGen.Star(tail + up * (s * 0.08f), s * 0.09f, r, g, b, 0.9f);
                    outV[n++] = SpaceGen.Star(tail - up * (s * 0.08f), s * 0.09f, r, g, b, 0.9f);
                    outV[n++] = SpaceGen.Star(c + up * (s * 0.12f), s * 0.07f, r * 0.8f, g * 0.8f, b * 0.8f, 0.9f);
                }
                else if (k.Shape == 1)
                {
                    // a drifter: a small body with feelers
                    outV[n++] = SpaceGen.Star(c, s * 0.3f, r, g, b, 0.9f);
                    outV[n++] = SpaceGen.Star(c + dir * (s * 0.3f), s * 0.18f, r, g, b, 0.9f);
                    for (int i = -1; i <= 1; i += 2) outV[n++] = SpaceGen.Star(c + dir * (s * 0.55f) + side * (s * 0.3f * i), s * 0.05f, r, g, b, 0.7f);
                }
                else
                {
                    // an amoeba: a soft blob reaching out with changing arms
                    outV[n++] = SpaceGen.Star(c, s * 0.4f, r, g, b, 0.6f);
                    for (int i = 0; i < 4; i++)
                    {
                        double ang = i * 1.57 + Math.Sin(a.Phase + i) * 0.5;
                        var arm = side * (float)Math.Cos(ang) + up * (float)Math.Sin(ang);
                        outV[n++] = SpaceGen.Star(c + arm * (s * (0.35f + 0.15f * (float)Math.Sin(a.Phase * 1.3 + i))), s * 0.18f, r, g, b, 0.6f);
                    }
                    outV[n++] = SpaceGen.Star(c, s * 0.12f, r * 0.6f, g * 0.6f, b * 0.6f, 0.9f);     // its nucleus
                }
            }
            return n;
        }
    }
}
