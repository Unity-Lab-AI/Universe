// Universe · platform · the player's settings, kept between runs in their own folder (%LOCALAPPDATA%\Universe\settings.txt),
// one "name = value" per line.
using System;
using System.Collections.Generic;
using System.IO;

namespace Universe.Platform
{
    internal static class Settings
    {
        private static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Universe.Shell.PreviewText.AppName, "settings.txt");
        private static Dictionary<string, string> _v;

        private static Dictionary<string, string> V
        {
            get
            {
                if (_v != null) return _v;
                _v = new Dictionary<string, string>();
                try { if (File.Exists(FilePath)) foreach (var l in File.ReadAllLines(FilePath)) { int i = l.IndexOf('='); if (i > 0) _v[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim(); } }
                catch (System.Exception ex) { Universe.Program.WriteLog("failed in reading the settings: " + ex.Message); }   // (never silent: written to the log)
                return _v;
            }
        }

        public static bool Get(string name, bool fallback) { string s; return V.TryGetValue(name, out s) ? s == "on" : fallback; }

        public static int Get(string name, int fallback) { string s; int v; return V.TryGetValue(name, out s) && int.TryParse(s, out v) ? v : fallback; }

        public static void Set(string name, int value) { V[name] = value.ToString(); Save(); }

        public static void Set(string name, bool value) { V[name] = value ? "on" : "off"; Save(); }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>(); foreach (var kv in V) lines.Add(kv.Key + " = " + kv.Value);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (System.Exception ex) { Universe.Program.WriteLog("failed in saving the settings: " + ex.Message); }   // (never silent: written to the log)
        }
    }
}
