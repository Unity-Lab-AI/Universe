<p align="center"><img src="img/logo.png" width="90" alt="Universe" title="Universe"></p>

<p align="center">
  <a href="../README.md" title="What Universe is, and how to fly it">Universe</a>
  &nbsp;·&nbsp;
  <a href="COMPARED.md" title="Size, scope and performance against the other universe programs">Against the field</a>
  &nbsp;·&nbsp;
  <a href="GUIDE.md" title="Add kinds of worlds, stars and galaxies, change the laws of physics, write tours">Build your own universe</a>
  &nbsp;·&nbsp;
  <a href="HOW-TO-BUILD.md" title="Build it, make the installer, find your way round the code">Technical guide</a>
  &nbsp;·&nbsp;
  <a href="../CREDITS.md" title="Who made it, and the science it is made from">Credits</a>
</p>

# On Linux

> Universe is a Windows program: it talks to the graphics card through Direct3D 12 and to the screen through Win32, both by hand. On Linux it runs through **Wine**, which runs Windows programs and carries Direct3D 12 on Vulkan. One script, no fiddling. A native Linux build (Vulkan, X11 and Wayland, a Linux C# runtime) is on the roadmap.

---

## The short way

1. Install Wine, 8 or newer, from your distribution: `sudo apt install wine` (Debian, Ubuntu, Mint), `sudo dnf install wine` (Fedora), `sudo pacman -S wine` (Arch, Manjaro), `sudo zypper install wine` (openSUSE).
2. Make sure your card's Vulkan driver is in: Mesa's `mesa-vulkan-drivers` for AMD and Intel, the proprietary driver for NVIDIA. Most desktops already have it.
3. Download the **zip** (not the installer) from the releases page, unpack it anywhere, and run the script inside it:

```
unzip Universe-0.1.1-windows.zip -d universe
cd universe
chmod +x run-on-linux.sh
./run-on-linux.sh
```

The first run sets up a private Wine folder of its own under `~/.local/share/universe-wine`, so nothing of yours is touched. Wine may offer to fetch **Wine Mono**, its .NET: say yes, the program is a .NET one. Then the landing page opens as on Windows.

| What the script checks | If it is missing |
|---|---|
| Wine is installed and is 8 or newer | It prints the install line for your distribution and stops |
| A Vulkan driver is installed | It prints the driver to install and stops |
| `Universe.exe` is beside it | It says to unpack the whole zip and run from inside it |

Everything after that is the program as it is on Windows: the same landing page, tour, menus, documents, screenshots (**F12**) and clips (**F9**), saved in `captures` beside the program.

---

## The Steam way

If you have Steam, its Proton carries the best Direct3D 12 on Linux (vkd3d-proton):

1. In Steam: **Games > Add a Non-Steam Game to My Library**, browse to `Universe.exe` in the unpacked zip.
2. Right-click it in the library: **Properties > Compatibility > Force the use of a specific Steam Play compatibility tool**, and pick the newest Proton.
3. Play.

---

## Where it stands

We build and test on Windows. The Wine path is the plain one, and the program uses nothing exotic: a Direct3D 12 device at feature level 11, a flip-model swap chain, Win32 windows and menus, a .NET 4 runtime, all of which Wine carries. But we have not yet run it on a Linux desktop ourselves, so the first Linux report decides it. If it fails for you, run it once more with Wine's log on and send us the file:

```
WINEDEBUG=warn+all ./run-on-linux.sh 2> wine.log
```

to **[contact@unityailab.com](mailto:contact@unityailab.com)** or an issue on the repository. What the stats on screen say (**View > Show GPU and CPU stats**) helps too.

---

<p align="center">Made by the <b>Unity AI Lab</b> &nbsp;·&nbsp; <a href="https://unityailab.com" title="The Unity AI Lab">unityailab.com</a> &nbsp;·&nbsp; <a href="../CREDITS.md" title="The team, and the science it is made from">Credits</a></p>
