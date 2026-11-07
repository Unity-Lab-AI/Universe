// Universe · simulation · the inside of a rocky planet, running on the planet's own clock whether or not anything lives on it.
// Built from content/physics.dnat: core, mantle and crust; heat from forming and from radioactive rock that slowly runs out;
// mantle convection; a magma ocean when young; plate tectonics or a stagnant lid; a geodynamo making the magnetic field
// (it needs a liquid outer core and spin), which wanders and flips; and the shield that field gives the air and life.
// Numbers are relative to Earth today (Earth: 6,371 km, 4.5 billion years old, heat flow 1, field 1).
using System;
using Universe.Core;

namespace Universe.Sim
{
    internal sealed class PlanetCore
    {
        public readonly double RadiusKm, Size;          // Size: radius in Earth radii
        public readonly bool Wet;                        // water in the rock lets plates break and slide
        public readonly double DayHours;
        /// <summary>How old the planet is, in years since it formed.</summary>
        public double Age;

        // results, refreshed by Update
        public double Heat;              // heat flowing out from inside (Earth today = 1)
        public bool MagmaOcean;          // the surface is still molten rock
        public bool Plates;              // plate tectonics (false: a stagnant lid)
        public bool LiquidOuterCore;
        public double InnerCore;         // the solid inner core's share of the core's radius (0 to 0.9)
        public double Field;             // magnetic field strength (Earth today = 1; 0 = none)
        public bool FieldNormal = true;  // which way the field points; it flips now and then
        public int Flips;                // how many times it has flipped
        public double Volcanism;         // how active volcanoes are (Earth today = 1)
        public string Events = "";       // what changed in the last update, for the title

        private readonly Rng _rng;
        private double _nextFlip;

        /// <param name="ageYears">How old the planet is when the game starts.</param>
        public PlanetCore(double radiusKm, bool wet, double dayHours, double ageYears, ulong seed)
        {
            RadiusKm = radiusKm; Size = radiusKm / 6371.0; Wet = wet; DayHours = dayHours; Age = ageYears;
            _rng = new Rng(seed ^ 0xC0DEUL);
            _nextFlip = ageYears + FlipGap();
            Update();
            Events = "";
        }

        // field flips come at random, on average about every 450,000 years (Earth's record)
        private double FlipGap() { return 450000 * -Math.Log(Math.Max(1e-9, _rng.NextDouble())); }

        /// <summary>Works out the inside from the planet's age. Call after changing <see cref="Age"/>.</summary>
        public void Update()
        {
            bool wasPlates = Plates, wasField = Field > 0.05, wasMagma = MagmaOcean;
            // heat runs out as it leaks away; a bigger planet holds it longer (it has more inside for its surface)
            double keep = 3.0e9 * Math.Max(0.2, Size);
            Heat = Math.Exp(4.5e9 / 3.0e9) * Math.Exp(-Age / keep);
            MagmaOcean = Heat > 4.2;                                                   // the first couple of hundred million years
            // plates need enough heat to keep the mantle churning, enough size, and water to weaken the rock
            Plates = !MagmaOcean && Heat > 0.35 && Size > 0.5 && Wet;
            // the core cools from the middle out: the solid inner core grows, and once all is solid the dynamo stops
            InnerCore = Math.Max(0, Math.Min(0.9, (2.2 - Heat) / 2.2 * 0.65));
            LiquidOuterCore = InnerCore < 0.9 && Heat > 0.25;
            // the geodynamo: a churning liquid outer core and spin; plates help by carrying heat out of the core faster
            double spin = Math.Max(0, Math.Min(1, 100.0 / Math.Max(1, DayHours) / 4.2));
            Field = LiquidOuterCore && !MagmaOcean ? Math.Min(3, Math.Sqrt(Heat) * spin * (Plates ? 1 : 0.4) * Math.Pow(Size, 1.5)) : 0;
            Volcanism = MagmaOcean ? 10 : Heat * (Plates ? 1 : 0.5);
            var ev = new System.Text.StringBuilder();
            while (Field > 0.05 && Age >= _nextFlip) { FieldNormal = !FieldNormal; Flips++; _nextFlip += FlipGap(); if (ev.Length == 0) ev.Append("the magnetic field flipped"); }
            if (Age >= _nextFlip) _nextFlip = Age + FlipGap();
            if (wasMagma && !MagmaOcean) Add(ev, "the magma ocean cooled into a crust");
            if (wasPlates && !Plates) Add(ev, "the plates have stopped: a stagnant lid");
            if (wasField && Field <= 0.05) Add(ev, "the magnetic field has died; the star's wind now strips the air");
            Events = ev.ToString();
        }

        private static void Add(System.Text.StringBuilder b, string s) { if (b.Length > 0) b.Append("; "); b.Append(s); }

        /// <summary>How much of the star's harmful radiation reaches the surface (0 none, 1 all): the magnetic shield turns it away.</summary>
        public double Radiation { get { return 1 / (1 + Field * 3); } }

        /// <summary>A short line for the title: the planet's inside right now.</summary>
        public string Describe()
        {
            string s = (Age / 1e9).ToString("0.00") + " billion years old · inside: " + (MagmaOcean ? "a magma ocean" : (Plates ? "moving plates" : "a stagnant lid")) +
                       ", heat " + Heat.ToString("0.00") + "× Earth's, core " + (LiquidOuterCore ? "molten outside, " + (InnerCore * 100).ToString("0") + "% solid inside" : "frozen solid") +
                       ", magnetic field " + (Field > 0.05 ? Field.ToString("0.00") + "× Earth's" + (Flips > 0 ? " (flipped " + Flips + " times)" : "") : "none");
            return s;
        }

        public Chunk Save()
        {
            var ms = new System.IO.MemoryStream(); var o = new System.IO.BinaryWriter(ms);
            o.Write(Age); o.Write(FieldNormal); o.Write(Flips); o.Write(_nextFlip); foreach (var u in _rng.State) o.Write(u);
            o.Flush(); return new Chunk { Name = "CORE", Version = 1, Data = ms.ToArray() };
        }

        public void Load(Chunk c)
        {
            if (c.Name != "CORE" || c.Version > 1) throw new System.IO.InvalidDataException("not a planet-inside chunk this game can read");
            var r = new System.IO.BinaryReader(new System.IO.MemoryStream(c.Data));
            Age = r.ReadDouble(); FieldNormal = r.ReadBoolean(); Flips = r.ReadInt32(); _nextFlip = r.ReadDouble();
            _rng.SetState(new[] { r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64() });
            Update(); Events = "";
        }
    }
}
