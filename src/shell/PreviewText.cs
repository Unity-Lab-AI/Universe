// Universe · shell · the universe's Help menu: its name and version, and its documents (written in docs/release and built into
// the program, so the program and the project say the same).
namespace Universe.Shell
{
    internal static class PreviewText
    {
        /// <summary>The program's name: Universe for the Universe project's own release, Universe for Universe (its universe
        /// preview included), never one in the other's place.</summary>
#if UNIVERSE
        public const string AppName = "Universe";
        public const string Version = "0.1.3";
#else
        public const string AppName = "Universe";
        public const string Version = "0.1 (universe preview)";
#endif

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

        /// <summary>Every document for the viewer: the directory of them all first (what each is made of and when to read it),
        /// then About, Readme, How it works, the universe in plain words, against the field, the builders' guide, the technical guide, Credits.</summary>
        public static System.Collections.Generic.List<Universe.Platform.DocViewer.Doc> Docs()
        {
            return new System.Collections.Generic.List<Universe.Platform.DocViewer.Doc>
            {
                new Universe.Platform.DocViewer.Doc { Title = "Documents: what's here", Text = Built("docs.md") },
                new Universe.Platform.DocViewer.Doc { Title = "About", Text = "# " + AppName + "\n\n*Version " + Version + "*\n\n" + Built("about.md") },
                new Universe.Platform.DocViewer.Doc { Title = "Readme: how to use it", Text = Built("readme.md") },
                new Universe.Platform.DocViewer.Doc { Title = "How it works", Text = Built("how-it-works.md") },
                new Universe.Platform.DocViewer.Doc { Title = "The universe, in plain words", Text = Built("universe-readme.md") },
                new Universe.Platform.DocViewer.Doc { Title = "Against the field: size, scope, performance", Text = Built("compared.md") },
                new Universe.Platform.DocViewer.Doc { Title = "Build your own universe", Text = Built("guide.md") },
                new Universe.Platform.DocViewer.Doc { Title = "Build your own (technical)", Text = Built("how-to-build.md") },
                new Universe.Platform.DocViewer.Doc { Title = "Credits", Text = Built("credits.md") },
            };
        }
    }
}
