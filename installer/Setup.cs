// DNA-AND · installer · our own Windows installer, built with the C# compiler that comes with Windows (no outside tools).
// It carries the game (DNA-AND.exe and its content folder) packed inside itself. Installing puts the game in the user's own
// Programs folder (no administrator rights needed), adds Start menu and desktop shortcuts and an entry in Windows' Apps list,
// and leaves an uninstaller beside the game. Run with /uninstall (as the Apps list does) to remove it all again.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
#if UNIVERSE
    private const string Name = "Universe";                                // the Universe release
#else
    private const string Name = "DNA-AND";
#endif
    private const string Version = "0.1.0";
    private const string Publisher = "Unity AI Lab";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Name;

    private static string InstallDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name); } }
    private static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); } }
    private static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"); } }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (Array.IndexOf(args, "/uninstall") >= 0) return Uninstall(Array.IndexOf(args, "/quiet") >= 0);
            return Install(Array.IndexOf(args, "/quiet") >= 0);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Something went wrong:\n\n" + ex.Message, Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Install(bool quiet)
    {
        if (!quiet && MessageBox.Show("Install " + Name + " " + Version + " (universe preview)?\n\nIt goes in:\n" + InstallDir +
            "\n\nwith shortcuts on the Start menu and the desktop. No administrator rights are needed.",
            Name + " Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 2;

        // an earlier copy is replaced
        if (Directory.Exists(InstallDir)) Directory.Delete(InstallDir, true);
        Directory.CreateDirectory(InstallDir);
        // the game, unpacked from inside this installer
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        {
            if (s == null) throw new InvalidOperationException("this installer is missing the game inside it");
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                foreach (var e in zip.Entries)
                {
                    string to = Path.GetFullPath(Path.Combine(InstallDir, e.FullName));
                    if (!to.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase)) continue;        // never outside the install folder
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(to); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    e.ExtractToFile(to, true);
                }
        }
        // the uninstaller: this same program, kept beside the game
        string uninstaller = Path.Combine(InstallDir, "Uninstall " + Name + ".exe");
        File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);
        string exe = Path.Combine(InstallDir, Name + ".exe");
        Shortcut(StartMenuLink, exe); Shortcut(DesktopLink, exe);
        // listed in Windows' Apps list, for this user
        using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
        {
            k.SetValue("DisplayName", Name + " (universe preview)");
            k.SetValue("DisplayVersion", Version);
            k.SetValue("Publisher", Publisher);
            k.SetValue("URLInfoAbout", "https://unityailab.com");
            k.SetValue("InstallLocation", InstallDir);
            k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
            k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /quiet");
            k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1);
            k.SetValue("EstimatedSize", (int)(DirSize(InstallDir) / 1024));
        }
        if (!quiet && MessageBox.Show(Name + " is installed.\n\nStart it now?", Name + " Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = InstallDir });
        return 0;
    }

    private static int Uninstall(bool quiet)
    {
        if (!quiet && MessageBox.Show("Remove " + Name + " and its shortcuts?", Name + " Uninstall", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 2;
        foreach (var l in new[] { StartMenuLink, DesktopLink }) if (File.Exists(l)) File.Delete(l);
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        // the folder holds this running uninstaller, so it is removed a moment after it closes
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"")
            { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
        if (!quiet) MessageBox.Show(Name + " has been removed.", Name + " Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 0;
    }

    // a Windows shortcut, made with Windows' own shell (WScript.Shell)
    private static void Shortcut(string link, string target)
    {
        var t = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(t);
        object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
        var st = sc.GetType();
        st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
        st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { Name + ": the universe, from galaxies to atoms" });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    private static long DirSize(string d) { long n = 0; foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) n += new FileInfo(f).Length; return n; }
}
