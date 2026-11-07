// DNA-AND · shell · the words of the universe preview's Help menu: About, Readme and Credits.
namespace DnaAnd.Shell
{
    internal static class PreviewText
    {
        /// <summary>The program's name: Universe for the Universe project's own release, DNA-AND for DNA-AND (its universe
        /// preview included), never one in the other's place.</summary>
#if UNIVERSE
        public const string AppName = "Universe";
        public const string Version = "0.1.0";
#else
        public const string AppName = "DNA-AND";
        public const string Version = "0.1 (universe preview)";
#endif

        public const string About =
            AppName + " " + Version + "\n\n" +
            "The universe of DNA-AND (a 3D god game of life, in the making), on its own: one continuous zoom from a sky full of galaxies, into a galaxy and its stars, into a star's solar system, down to a planet's ground and on into its matter, to atoms.\n\n" +
            "This preview is the universe only: a dead universe, fully made, with no life, gameplay or interface yet.\n\n" +
            "Everything is made live from a seed by our own code: no outside code, no stored pictures. It is one camera with true infinite zoom, no separate views and no loading screens; detail comes from our own coefficient waveform engine (the CDF 9/7 wavelet) and endless self-similar detail (mode-E).\n\n" +
            "Help > How it works explains the waveform, the infinite zoom, how precision holds from galaxies to atoms, and the living planet.\n\n" +
            "Made by the Unity AI Lab team.\nhttps://unityailab.com\nThe Universe project: https://git.unityailab.com/GFourteen/Universe";

        public const string Readme =
            "How to use the preview\n\n" +
            "Click a galaxy, star, planet or a spot on the ground: the camera turns to it and the wheel flies straight to it.\n" +
            "B, among atoms: glide across the bond to the next atom, through the electrons they share.\n" +
            "Mouse wheel: fly in or out toward what the mouse points at. A star or planet near the pointer is flown at straight.\n" +
            "Hold Ctrl with the wheel: eight times as fast.\n" +
            "T: the panoramic tour, galaxies down to the quarks, told as a story (any key or click takes the controls back).\n" +
            "F12: a screenshot (PNG). F9: start or stop recording a clip (MP4 video). Both go in the captures folder.\n" +
            "Right click: let go of what you picked.\n" +
            "Space: pause time, or carry on. Comma and full stop: slower and faster, from real time to ten years a second (the Time menu too). Each planet turns on its own day, so its days and nights pass over the ground; the title bar shows the time where you are.\n" +
            "W A S D: fly forward, back, left and right; Q and E: down and up. The speed scales to the nearest thing, so it feels the same among galaxies as above the ground. Hold Shift: four times as fast. Flying yourself lets go of what you picked.\n" +
            "Drag with the left button: with something picked, circle round it at the distance you are at (see a solar system from above, a galaxy edge-on); with nothing picked, look around. The View menu can invert left-right and up-down.\n" +
            "Whatever you pick gets an information card beside it: what it is, its id and its facts (a star's temperature, planets and habitable zone; a planet's orbit, year and what it is made of; the ground's height, and whether it is land, sea or ice).\n" +
            "H: the scale and the information card, on or off.\n" +
            "View menu: the scale, and the render scale (lower is faster, higher is sharper).\n" +
            "Esc or Exit: close.\n\n" +
            "What you will see\n\n" +
            "The galaxies, each of its own kind. Close to one, its own stars fade in, about a hundred billion of them, made around you as you go.\n" +
            "Near a star, its solar system: its planets, moons, belts and the star itself.\n" +
            "Near a planet, its real ground, its seas, clouds and air. The planet is alive even with no life on it: plates, volcanoes, quakes, weather, ice ages and impacts run on its own clock.\n" +
            "Below the finest ground, its matter: grains, the crystal of its mineral, then its atoms.\n\n" +
            "How it is done (the waveform, the infinite zoom and the rest): Help > How it works.";

        public const string HowItWorks =
            "How it works\n\n" +
            "ONE CAMERA, INFINITE ZOOM\n" +
            "There are no separate views and no loading screens between scales. One camera flies through one world that holds every scale: the universe of galaxies, each galaxy's stars, each star's system, each planet's ground, and the matter it is made of. Each roll of the wheel moves the camera a share of the way to the nearest thing, so the zoom slows by itself as things come close and never runs out.\n\n" +
            "PRECISION AT EVERY SCALE\n" +
            "A single number cannot hold both the width of the universe and a millimetre. The camera keeps its place relative to an anchor (a galaxy's centre, then the star of the system you are in, then the planet you are at), so everything near you is worked out exactly, whether you are light-years or atoms away. The screen's depth is kept in reversed floating point, so near and far never fight, from a micrometre in front of you to across a galaxy.\n\n" +
            "THE COEFFICIENT WAVEFORM (CDF 9/7)\n" +
            "The lab's own wavelet engine, from Uni Vs Matics, the same wavelet JPEG 2000 uses. A field of values (heights, colours, densities) becomes a short list of wavelet terms: the strongest are kept until almost all of the field's energy is captured, and packed small. Coarse terms are the big shapes and fine terms the detail, so a field can be rebuilt at any level of detail, edited by changing a few terms, and saved small. In this preview it carries the planet's changes: its mountains wearing down and rising, and its craters, over its six cube faces.\n\n" +
            "MODE-E: DETAIL THAT NEVER RUNS OUT\n" +
            "Where stored detail ends, mode-E grows new detail of the same character, forever: Newton's method on z cubed minus K, carried on into a 3D cubic surface, textured by cellular noise whose cells divide by ten-fold steps as you zoom. It is part of the engine; carrying it (and the waveform) into the matter below the ground is still to come.\n\n" +
            "THE MATTER, AT ITS REAL SIZES\n" +
            "Below the finest ground the camera goes on into what the ground is made of, every piece at its real size and fixed in place, so zooming truly scales it into view. Grains about 0.2 mm across, each one mineral picked from its ground's own mix (quartz, feldspars, clays, olivine, pyroxenes, iron oxides, calcite, salt, ices and more). The grain's surface, its detail at every size. Then its atoms: rock salt as sodium and chlorine alternating on a cube grid 0.282 nm apart, quartz as the silica network (each silicon bonded to four oxygens), ice and sea water as real water molecules bent at 104.5 degrees, carbon dioxide straight, methane a tetrahedron. Grains are their ground's real sizes (fine sand, dust, silt), and a crystal's face steps down in single atomic layers 0.3 nm high. Every atom near you has its electrons in their real shells (worked out from its atomic number), each a point, round a nucleus about a hundred thousand times smaller than the atom, in mostly empty space; the nucleus is that element's real protons and neutrons, and inside the one at its middle, its three quarks, held by gluons, down to a hundredth of a femtometre. Each layer grows out of the one before. Click any grain, atom, electron, proton, neutron or quark (or any spot) for its card; grains and atoms are flown to.\n\n" +
            "A GALAXY THAT FILLS IN AROUND YOU\n" +
            "A galaxy holds about a hundred billion stars; it keeps a sample of 150,000. Around the camera, the real ones are made at the galaxy's true density, in cubes about 25 light-years wide, each made from its own position, so they are the same stars whenever you come back.\n\n" +
            "A LIVING PLANET, EVEN DEAD\n" +
            "Every rocky planet runs on its own clock: its heat inside runs down, plates move, volcanoes erupt and quakes shake at real rates, the magnetic field flips, the air and climate follow real chemistry, ice ages come with the orbit's wobbles, mountains wear down, and rocks from space strike. Its ground changes all at once at big moments, never piece by piece in front of you.\n\n" +
            "TIME AND DAYS\n" +
            "Time runs at the speed you choose, from paused and real time up to ten years a second (Space, comma, full stop, or the Time menu). Every planet turns on its own day, so its star rises and sets over the ground, and the light on the ground is its star's own colour through its air (half bright by night, never black).\n\n" +
            "YOUR OWN COMPUTER\n" +
            "The game picks the strongest graphics card in the computer and builds the ground in the background on every processor core, only what is in view in fine detail and what you need most first. GPU use in the View menu sets how much of the card's time it may use, 10 to 90 per cent: the card's own working time is measured every frame, and the detail is turned up or down to hold it there. The render scale trades sharpness for speed.\n\n" +
            "STILL TO COME\n" +
            "The waveform carrying every field from galaxies to microbes (gas and star fields, the ground itself, clouds, stars' surfaces); life; gameplay; and the interface.";

        // a Help text as a document: its first line the title, its lines in capitals its headings
        private static string AsDoc(string text)
        {
            var lines = text.Split('\n'); var b = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                bool caps = l.Length > 3 && l.ToUpperInvariant() == l && l.IndexOf("://") < 0 && System.Linq.Enumerable.Any(l, char.IsLetter);
                if (i == 0) b.Append("# ").Append(l).Append("\n");
                else if (caps) b.Append("## ").Append(char.ToUpperInvariant(l[0])).Append(l.Substring(1).ToLowerInvariant()).Append("\n");
                else b.Append(l).Append("\n");
            }
            return b.ToString();
        }

        // a document built into the program (from docs/release, so the program and the repo say the same)
        private static string Built(string name)
        {
            try
            {
                using (var s = typeof(PreviewText).Assembly.GetManifestResourceStream(name))
                    if (s != null) using (var r = new System.IO.StreamReader(s)) return r.ReadToEnd();
            }
            catch { }
            return "# " + name + "\n\nThis document is not in this copy of the program.";
        }

        /// <summary>Every document for the viewer: About, Readme, How it works, the universe in plain words, the build guide, Credits.</summary>
        public static System.Collections.Generic.List<DnaAnd.Platform.DocViewer.Doc> Docs()
        {
            return new System.Collections.Generic.List<DnaAnd.Platform.DocViewer.Doc>
            {
                new DnaAnd.Platform.DocViewer.Doc { Title = "About", Text = AsDoc(About) },
                new DnaAnd.Platform.DocViewer.Doc { Title = "Readme: how to use it", Text = AsDoc(Readme) },
                new DnaAnd.Platform.DocViewer.Doc { Title = "How it works", Text = AsDoc(HowItWorks) },
                new DnaAnd.Platform.DocViewer.Doc { Title = "The universe, in plain words", Text = Built("universe-readme.md") },
                new DnaAnd.Platform.DocViewer.Doc { Title = "Build your own (technical)", Text = Built("how-to-build.md") },
                new DnaAnd.Platform.DocViewer.Doc { Title = "Credits", Text = AsDoc(Credits) },
            };
        }

        public const string Credits =
            "Made by the Unity AI Lab team\nhttps://unityailab.com\n\n" +
            "GFourteen (Gee): co-founder, engineer, financial advisor\n" +
            "SpongeBong (Sponge / hackall360): co-founder, engineer, ethical hacker, sys admin\n" +
            "Alfreddo: engineer, agentic systems, researcher\n" +
            "Red: engineer, security, sys admin\n\n" +
            "The coefficient waveform engine and mode-E come from the lab's own Uni Vs Matics engine.\n\n" +
            "The Universe project, with everything to build your own: https://git.unityailab.com/GFourteen/Universe\n\n" +
            "Unity AI Lab Git: https://git.unityailab.com\nUnity AI Lab on GitHub: https://github.com/Unity-Lab-AI";
    }
}
