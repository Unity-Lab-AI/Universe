// Universe · installer · our own Windows setup wizard, built with the C# compiler and the forms that come with Windows (no
// outside tools). It carries the game packed inside itself. The wizard: welcome; the readme, the MIT License (accepted to go
// on) and the privacy policy, read from the very files it installs; the requirements, each checked on this
// computer (Windows 10 or 11, 64-bit; a Direct3D 12 graphics card; memory; disk space); where to install it (any folder,
// Browse); shortcuts; installing, with a progress bar; finished, start it now. Run over an install it asks: update it,
// uninstall everything, or leave it; a running copy is closed first. The uninstaller (in the Apps list and beside the game)
// asks whether your settings, screenshots and clips go too, and leaves nothing else behind.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
#if UNIVERSE
    private const string AppName = "Universe";   // (AppName, never Name: inside the wizard Name is the window's own, empty)
    private const string Tagline = "One universe you can zoom through without a break: from the cosmic web of galaxies down to the quarks inside a proton.";
#else
    private const string AppName = "Universe";
    private const string Tagline = "The universe of Universe, a 3D god game of life (universe preview): from the cosmic web of galaxies down to the quarks.";
#endif
    private const string Version = "0.1.0";
    private const string Publisher = "Unity AI Lab";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName;

    private static string DefaultDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName); } }
    private static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); } }
    private static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); } }
    private static string SettingsDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName); } }

    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            if (Array.IndexOf(args, "/uninstall") >= 0) return Uninstall(Array.IndexOf(args, "/quiet") >= 0);
            string existing = InstalledAt();
            if (existing != null)
            {
                var r = MessageBox.Show(AppName + " is already installed in\n" + existing + "\n\nYes: update it (your settings, screenshots and clips stay)\nNo: uninstall it\nCancel: leave it alone",
                    AppName + " Setup", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return 2;
                if (r == DialogResult.No) return Uninstall(false);
            }
            Application.Run(new Wizard(existing ?? LastDir(), existing != null));
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Something went wrong:\n\n" + ex.Message, AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    // where it is installed: Windows' Apps list entry first; failing that, where its shortcuts lead
    private static string InstalledAt()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            try
            {
                using (var k = root.OpenSubKey(UninstallKey))
                {
                    var v = k == null ? null : k.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(v) && File.Exists(Path.Combine(v, AppName + ".exe"))) return v.TrimEnd('\\');
                }
            }
            catch { }
        try { using (var k = Registry.CurrentUser.OpenSubKey(ChoiceKey)) { var v = k == null ? null : k.GetValue("InstallDir") as string; if (!string.IsNullOrEmpty(v) && File.Exists(Path.Combine(v, AppName + ".exe"))) return v.TrimEnd('\\'); } } catch { }
        foreach (var l in new[] { StartMenuLink, DesktopLink })
            try
            {
                if (!File.Exists(l)) continue;
                var t = Type.GetTypeFromProgID("WScript.Shell"); object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { l });
                var target = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string;
                if (target != null && File.Exists(target) && Path.GetFileName(target).Equals(AppName + ".exe", StringComparison.OrdinalIgnoreCase)) return Path.GetDirectoryName(target);
            }
            catch { }
        return null;
    }

    // the folder chosen last time (kept for the next install), else the usual place
    private const string ChoiceKey = @"Software\Unity AI Lab\" + AppName;
    private static string LastDir()
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(ChoiceKey)) { var v = k == null ? null : k.GetValue("InstallDir") as string; if (!string.IsNullOrEmpty(v)) return v; } } catch { }
        return DefaultDir;
    }

    // ── the requirements, checked on this computer ──
    [DllImport("d3d12.dll")] private static extern int D3D12CreateDevice(IntPtr adapter, int minLevel, ref Guid riid, IntPtr device);
    [StructLayout(LayoutKind.Sequential)] private struct MemStatus { public uint Length, Load; public ulong Total, Avail, TotalPage, AvailPage, TotalVirtual, AvailVirtual, AvailExtended; }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MemStatus m);
    internal static bool HasD3D12()
    {
        try { var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7"); return D3D12CreateDevice(IntPtr.Zero, 0xb000, ref iid, IntPtr.Zero) >= 0; }   // asks for a device at feature level 11_0 (not made: only whether one can be)
        catch { return false; }
    }
    internal static int WindowsMajor()
    {
        try { using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")) { var v = k.GetValue("CurrentMajorVersionNumber"); return v is int ? (int)v : Environment.OSVersion.Version.Major; } }
        catch { return Environment.OSVersion.Version.Major; }
    }
    internal static double MemoryGB() { var m = new MemStatus { Length = (uint)Marshal.SizeOf(typeof(MemStatus)) }; return GlobalMemoryStatusEx(ref m) ? m.Total / 1073741824.0 : 0; }
    internal static long PayloadBytes()
    {
        using (var s = Payload()) using (var zip = new ZipArchive(s, ZipArchiveMode.Read)) { long n = 0; foreach (var e in zip.Entries) n += e.Length; return n + new FileInfo(Assembly.GetExecutingAssembly().Location).Length; }
    }
    internal static Stream Payload()
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (s == null) throw new InvalidOperationException("this installer is missing the game inside it");
        return s;
    }
    // a text file from inside the packed game (the readme, the licence, the privacy policy), as it will be installed
    internal static string PayloadText(string name)
    {
        using (var s = Payload()) using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
        {
            var e = zip.GetEntry(name); if (e == null) return "(" + name + " is missing from this installer)";
            using (var r = new StreamReader(e.Open())) return r.ReadToEnd();
        }
    }
    internal static long FreeBytes(string dir)
    {
        try { var root = Path.GetPathRoot(Path.GetFullPath(dir)); return new DriveInfo(root).AvailableFreeSpace; } catch { return -1; }
    }
    internal static string Size(long b) { return b >= 1L << 30 ? (b / 1073741824.0).ToString("0.0") + " GB" : (b / 1048576.0).ToString("0.0") + " MB"; }

    // ── the wizard ──
    private sealed class Wizard : Form
    {
        private readonly Panel[] _pages = new Panel[9]; private readonly CheckBox _accept = new CheckBox(); private int _page;
        private readonly Button _back = new Button(), _next = new Button(), _cancel = new Button();
        private readonly TextBox _dir = new TextBox(); private readonly Label _space = new Label(), _file = new Label();
        private readonly CheckBox _desktop = new CheckBox(), _start = new CheckBox(), _launch = new CheckBox(), _clearClips = new CheckBox(), _clearShots = new CheckBox(), _clearSettings = new CheckBox();
        private readonly ProgressBar _bar = new ProgressBar(); private readonly bool _update; private bool _blocked, _installed; private string _installedDir;
        private readonly long _need;

        public Wizard(string dir, bool update)
        {
            _update = update; _need = PayloadBytes();
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }   // its own icon (the program's)
            Text = AppName + " " + Version + " Setup"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10f); AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(640, 440);
            var head = new Panel { Dock = DockStyle.Top, Height = 70, BackColor = Color.FromArgb(10, 12, 20) };
            head.Controls.Add(new Label { Text = AppName, ForeColor = Color.FromArgb(234, 238, 242), Font = new Font("Segoe UI Light", 22f), Location = new Point(18, 10), AutoSize = true });
            head.Controls.Add(new Label { Text = "version " + Version + " · " + Publisher, ForeColor = Color.FromArgb(160, 170, 180), Location = new Point(22, 46), AutoSize = true });
            Controls.Add(head);
            var foot = new Panel { Dock = DockStyle.Bottom, Height = 52 };
            _cancel.Text = "Cancel"; _next.Text = "Next >"; _back.Text = "< Back";
            foreach (var b in new[] { _back, _next, _cancel }) { b.Size = new Size(100, 32); foot.Controls.Add(b); }
            _back.Location = new Point(316, 10); _next.Location = new Point(420, 10); _cancel.Location = new Point(528, 10);
            _back.Click += (o, e) => Show(_page - 1); _next.Click += (o, e) => Next(); _cancel.Click += (o, e) => { if (!_installed) Close(); };
            Controls.Add(foot); AcceptButton = _next;
            for (int i = 0; i < _pages.Length; i++) { _pages[i] = new Panel { Location = new Point(0, 70), Size = new Size(640, 318), Visible = false, Padding = new Padding(24) }; Controls.Add(_pages[i]); }

            // 0 welcome
            Add(0, "Welcome", "This will install " + AppName + " " + Version + " on this computer.\n\n" + Tagline +
                "\n\nIt installs for you alone (no administrator rights needed), adds shortcuts and an entry in Windows' Apps list, and can be removed again there." +
                (update ? "\n\nIt is already installed: this will update it, keeping your settings, screenshots and clips." : "") + "\n\nClick Next to go on.");
            // 1 readme, 2 licence, 3 privacy: the files that are installed, to read
            Doc(1, "Readme", "README.txt", null);
            Doc(2, "License", "LICENSE.txt", "I accept the terms of the MIT License");
            Doc(3, "Privacy", "PRIVACY.txt", null);
            // 4 requirements
            int win = WindowsMajor(); bool x64 = Environment.Is64BitOperatingSystem, d3d = HasD3D12(); double mem = MemoryGB(); long free = FreeBytes(dir);
            _blocked = win < 10 || !x64 || !d3d;
            var req = "What " + AppName + " needs, checked on this computer:\n\n" +
                Mark(win >= 10) + "  Windows 10 or 11  (this is Windows " + win + ")\n" +
                Mark(x64) + "  64-bit Windows\n" +
                Mark(d3d) + "  A graphics card that runs Direct3D 12  " + (d3d ? "(found)" : "(none found: update the graphics driver)") + "\n" +
                Mark(mem >= 8) + "  8 GB of memory or more  (this has " + mem.ToString("0") + " GB" + (mem < 8 ? "; it may run slowly" : "") + ")\n" +
                Mark(free < 0 || free > _need * 2) + "  " + Size(_need) + " of disk space  (" + (free >= 0 ? Size(free) + " free" : "unknown") + ")\n\n" +
                (_blocked ? "This computer does not have everything " + AppName + " needs, so it cannot be installed." : "Everything it needs is here. It runs in a window and never changes your display settings.");
            Add(4, "Requirements", req);
            // 2 folder
            Add(5, "Where to install it", "Choose the folder " + AppName + " goes in. Your settings are kept in your own profile; screenshots and clips in a captures folder inside this one.");
            _dir.Text = dir; _dir.Location = new Point(24, 120); _dir.Width = 470; _pages[5].Controls.Add(_dir);
            var browse = new Button { Text = "Browse...", Location = new Point(504, 118), Size = new Size(100, 30) };
            browse.Click += (o, e) => { using (var f = new FolderBrowserDialog { Description = "Install " + AppName + " in:", SelectedPath = Directory.Exists(_dir.Text) ? _dir.Text : Path.GetDirectoryName(_dir.Text) }) if (f.ShowDialog(this) == DialogResult.OK) _dir.Text = Path.GetFileName(f.SelectedPath).Equals(AppName, StringComparison.OrdinalIgnoreCase) ? f.SelectedPath : Path.Combine(f.SelectedPath, AppName); };
            _pages[5].Controls.Add(browse);
            _space.Location = new Point(24, 160); _space.AutoSize = true; _pages[5].Controls.Add(_space);
            _dir.TextChanged += (o, e) => SpaceText(); SpaceText();
            if (update) { _dir.ReadOnly = true; browse.Enabled = false; }
            // 3 options
            Add(6, "Shortcuts", "Where you would like shortcuts to " + AppName + ":");
            _desktop.Text = "On the desktop"; _desktop.Checked = true; _desktop.Location = new Point(28, 110); _desktop.AutoSize = true; _pages[6].Controls.Add(_desktop);
            _start.Text = "On the Start menu"; _start.Checked = true; _start.Location = new Point(28, 140); _start.AutoSize = true; _pages[6].Controls.Add(_start);
            // what an earlier copy left behind, each offered to be deleted on its own: its clips, its screenshots, its settings
            try
            {
                string caps = Path.Combine(dir, "captures"); int y = 190;
                Action<CheckBox, string[], string, bool> offer = (box, files, what, on) =>
                {
                    if (files.Length == 0) return;
                    long bytes = 0; foreach (var f in files) bytes += new FileInfo(f).Length;
                    box.Text = "Delete my old " + what + " (" + files.Length + " file" + (files.Length == 1 ? "" : "s") + ", " + (bytes / 1048576.0).ToString("N0") + " MB)";
                    box.Checked = on; box.Location = new Point(28, y); box.AutoSize = true; _pages[6].Controls.Add(box); y += 30;
                };
                offer(_clearClips, Directory.Exists(caps) ? Directory.GetFiles(caps, "*.mp4") : new string[0], "clips", true);
                offer(_clearShots, Directory.Exists(caps) ? Directory.GetFiles(caps, "*.png") : new string[0], "screenshots", false);
                offer(_clearSettings, Directory.Exists(SettingsDir) ? Directory.GetFiles(SettingsDir, "*", SearchOption.AllDirectories) : new string[0], "settings (back to how it starts)", false);
            }
            catch { }
            // 4 installing
            Add(7, "Installing", "Please wait while " + AppName + " is installed.");
            _bar.Location = new Point(24, 120); _bar.Size = new Size(580, 26); _pages[7].Controls.Add(_bar);
            _file.Location = new Point(24, 156); _file.Size = new Size(580, 40); _pages[7].Controls.Add(_file);
            // 5 done
            Add(8, "Finished", AppName + " is installed in:\n" + "{dir}" + "\n\nYou can start it from its shortcuts, and remove it from Windows' Apps list.");
            _launch.Text = "Start " + AppName + " now"; _launch.Checked = true; _launch.Location = new Point(28, 200); _launch.AutoSize = true; _pages[8].Controls.Add(_launch);
            Show(0);
        }

        private void Doc(int i, string title, string file, string accept)
        {
            _pages[i].Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 13f), Location = new Point(24, 16), AutoSize = true });
            var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(24, 52), Size = new Size(590, accept != null ? 220 : 252),
                Font = new Font("Consolas", 9f), BackColor = Color.White, Text = PayloadText(file).Replace("\r\n", "\n").Replace("\n", "\r\n") };
            _pages[i].Controls.Add(box);
            if (accept != null) { _accept.Text = accept; _accept.Location = new Point(26, 282); _accept.AutoSize = true; _accept.CheckedChanged += (o, e) => Show(_page); _pages[i].Controls.Add(_accept); }
        }
        private static string Mark(bool ok) { return ok ? "✔" : "✖"; }
        private void SpaceText() { long f = FreeBytes(_dir.Text); _space.Text = "Space needed: " + Size(_need) + "     Space free: " + (f >= 0 ? Size(f) : "unknown"); }
        private void Add(int i, string title, string text)
        {
            _pages[i].Controls.Add(new Label { Text = title, Font = new Font("Segoe UI Semibold", 13f), Location = new Point(24, 16), AutoSize = true });
            _pages[i].Controls.Add(new Label { Name = "body", Text = text, Location = new Point(24, 52), Size = new Size(590, i == 5 || i == 6 || i == 7 ? 60 : 250) });
        }
        private void Show(int i)
        {
            _page = Math.Max(0, Math.Min(8, i));
            for (int k = 0; k < _pages.Length; k++) _pages[k].Visible = k == _page;
            _back.Enabled = _page > 0 && _page < 7;
            _next.Text = _page == 6 ? "Install" : _page == 8 ? "Finish" : "Next >";
            _next.Enabled = !(_page == 4 && _blocked) && !(_page == 2 && !_accept.Checked) && _page != 7;
            _cancel.Enabled = _page < 7;
        }
        private void Next()
        {
            if (_page == 5)
            {
                string d = _dir.Text.Trim();
                if (d.Length == 0 || !Path.IsPathRooted(d)) { MessageBox.Show(this, "Choose a full folder path.", Text); return; }
                long f = FreeBytes(d); if (f >= 0 && f < _need) { MessageBox.Show(this, "There is not enough space there: " + Size(_need) + " is needed.", Text); return; }
                if (File.Exists(Path.Combine(d, AppName + ".exe")))
                { if (MessageBox.Show(this, AppName + " is already installed in\n" + d + "\n\nUpdate that copy? (your screenshots and clips stay)", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return; }
                else if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length > 0)
                { if (MessageBox.Show(this, "The folder\n" + d + "\nalready has other things in it. They will be left as they are and " + AppName + " installed beside them.\n\nInstall there?", Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return; }
            }
            if (_page == 6) { Show(7); Install(); return; }
            if (_page == 8)
            {
                if (_launch.Checked && _installedDir != null) { string exe = Path.Combine(_installedDir, AppName + ".exe"); if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = _installedDir }); }
                Close(); return;
            }
            Show(_page + 1);
        }

        private void Install()
        {
            string dir = Path.GetFullPath(_dir.Text.Trim());
            try
            {
                CloseRunning(this);
                // an update replaces the game and keeps the captures; a fresh install starts empty
                if (File.Exists(Path.Combine(dir, AppName + ".exe")))
                    foreach (var name in new[] { "build", "content", "docs", AppName + ".exe", "Uninstall " + AppName + ".exe", "README.txt", "LICENSE.txt", "PRIVACY.txt" })
                    {
                        string p = Path.Combine(dir, name);
                        if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p);
                    }
                Directory.CreateDirectory(dir);
                {
                    // what was chosen to be deleted from the earlier copy: its clips, its screenshots, its settings
                    string caps = Path.Combine(dir, "captures");
                    if (_clearClips.Checked && Directory.Exists(caps)) foreach (var c in Directory.GetFiles(caps, "*.mp4")) { try { File.Delete(c); } catch { } }
                    if (_clearShots.Checked && Directory.Exists(caps)) foreach (var c in Directory.GetFiles(caps, "*.png")) { try { File.Delete(c); } catch { } }
                    if (_clearSettings.Checked) try { if (Directory.Exists(SettingsDir)) Directory.Delete(SettingsDir, true); } catch { }
                }
                using (var s = Payload()) using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                {
                    _bar.Maximum = zip.Entries.Count + 3; int n = 0;
                    foreach (var e in zip.Entries)
                    {
                        string to = Path.GetFullPath(Path.Combine(dir, e.FullName));
                        if (!to.StartsWith(dir, StringComparison.OrdinalIgnoreCase)) continue;            // never outside the install folder
                        _file.Text = e.FullName; _bar.Value = ++n; Application.DoEvents();
                        if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(to); continue; }
                        Directory.CreateDirectory(Path.GetDirectoryName(to)); e.ExtractToFile(to, true);
                    }
                }
                _file.Text = "the uninstaller and shortcuts"; Application.DoEvents();
                string uninstaller = Path.Combine(dir, "Uninstall " + AppName + ".exe");
                File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true);
                string exe = Path.Combine(dir, AppName + ".exe");
                if (_start.Checked) Shortcut(StartMenuLink, exe); else if (File.Exists(StartMenuLink)) File.Delete(StartMenuLink);
                if (_desktop.Checked) Shortcut(DesktopLink, exe); else if (File.Exists(DesktopLink)) File.Delete(DesktopLink);
                _bar.Value = Math.Min(_bar.Maximum, _bar.Value + 2);
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", AppName); k.SetValue("DisplayVersion", Version); k.SetValue("Publisher", Publisher);
                    k.SetValue("URLInfoAbout", "https://unityailab.com"); k.SetValue("InstallLocation", dir); k.SetValue("DisplayIcon", exe);
                    k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall"); k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /quiet");
                    k.SetValue("NoModify", 1); k.SetValue("NoRepair", 1); k.SetValue("EstimatedSize", (int)(DirSize(dir) / 1024));
                }
                using (var c = Registry.CurrentUser.CreateSubKey(ChoiceKey)) c.SetValue("InstallDir", dir);   // (offered again next time)
                _bar.Value = _bar.Maximum; _installed = true; _installedDir = dir;
                foreach (Control c in _pages[8].Controls) if (c.Name == "body") c.Text = c.Text.Replace("{dir}", dir);
                Show(8);
            }
            catch (Exception ex) { MessageBox.Show(this, "It could not be installed:\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); Show(5); }
        }
    }

    // a running copy is closed first (asked), so its files can be replaced or removed
    private static void CloseRunning(IWin32Window owner)
    {
        var running = Process.GetProcessesByName(AppName); if (running.Length == 0) return;
        if (MessageBox.Show(owner, AppName + " is running. Close it to go on?", AppName + " Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) throw new InvalidOperationException(AppName + " is still running");
        foreach (var p in running) { try { p.CloseMainWindow(); if (!p.WaitForExit(4000)) p.Kill(); p.WaitForExit(4000); } catch { } }
    }

    private static int Uninstall(bool quiet)
    {
        string dir = InstalledAt() ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (!quiet && MessageBox.Show("Remove " + AppName + " from\n" + dir + "\nand its shortcuts?", AppName + " Uninstall", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return 2;
        bool all = !quiet && MessageBox.Show("Also remove your settings, screenshots and clips?\n\n" + SettingsDir + "\n" + Path.Combine(dir, "captures") + "\n\n(Cannot be undone.)", AppName + " Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        if (!quiet) CloseRunning(null);
        foreach (var l in new[] { StartMenuLink, DesktopLink }) try { if (File.Exists(l)) File.Delete(l); } catch { }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
        if (all) try { if (Directory.Exists(SettingsDir)) Directory.Delete(SettingsDir, true); } catch { }
        // the game goes now; this running uninstaller, and the folder if it is left empty, a moment after it closes
        string self = Assembly.GetExecutingAssembly().Location;
        // only the game's own parts: anything else in the folder is left alone
        foreach (var name in new[] { "build", "content", "docs", AppName + ".exe", "README.txt", "LICENSE.txt", "PRIVACY.txt", all ? "captures" : null })
        {
            if (name == null) continue; string p = Path.Combine(dir, name);
            if (p.Equals(self, StringComparison.OrdinalIgnoreCase)) continue;
            try { if (Directory.Exists(p)) Directory.Delete(p, true); else if (File.Exists(p)) File.Delete(p); } catch { }
        }
        string after = "del /f /q \"" + self + "\" & rmdir \"" + dir + "\"";   // (rmdir without /s: the folder goes only if nothing else is in it)
        Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 > nul & " + after) { WindowStyle = ProcessWindowStyle.Hidden, CreateNoWindow = true });
        if (!quiet) MessageBox.Show(AppName + " has been removed." + (all ? "" : "\n\nYour screenshots and clips are kept in\n" + Path.Combine(dir, "captures")), AppName + " Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { AppName });
        st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
    }

    private static long DirSize(string d) { long n = 0; foreach (var f in Directory.GetFiles(d, "*", SearchOption.AllDirectories)) n += new FileInfo(f).Length; return n; }
}
