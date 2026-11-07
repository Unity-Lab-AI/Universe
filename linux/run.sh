#!/usr/bin/env bash
# Universe / DNA-AND on Linux: through Wine, which runs Windows programs and carries Direct3D 12 on Vulkan.
# Windows 10 or 11 is the program's home; this is the one Linux path that needs no fiddling: unpack the zip, run this.
# Needs: Wine 8 or newer, and a Vulkan driver for your graphics card. Nothing else.
set -e
here="$(cd "$(dirname "$0")" && pwd)"
exe=""
for f in Universe.exe DNA-AND.exe; do if [ -f "$here/$f" ]; then exe="$f"; break; fi; done
if [ -z "$exe" ]; then
  echo "No Universe.exe or DNA-AND.exe beside this script: unpack the whole zip and run this from inside it."
  exit 1
fi
if ! command -v wine >/dev/null 2>&1; then
  echo "Wine is not installed. Install it with your distribution's package manager, then run this again:"
  echo "  Debian, Ubuntu, Mint:   sudo apt install wine"
  echo "  Fedora:                 sudo dnf install wine"
  echo "  Arch, Manjaro:          sudo pacman -S wine"
  echo "  openSUSE:               sudo zypper install wine"
  exit 1
fi
ver="$(wine --version 2>/dev/null | sed 's/^wine-//; s/[^0-9.].*$//')"
major="${ver%%.*}"
if [ -n "$major" ] && [ "$major" -lt 8 ] 2>/dev/null; then
  echo "Wine $ver is older than 8, and Direct3D 12 needs Wine 8 or newer. Update it, then run this again."
  exit 1
fi
if ! ls /usr/share/vulkan/icd.d/*.json /etc/vulkan/icd.d/*.json /usr/local/share/vulkan/icd.d/*.json >/dev/null 2>&1; then
  echo "No Vulkan driver is installed, and Wine runs Direct3D 12 on Vulkan. Install your card's, then run this again:"
  echo "  AMD or Intel (Mesa):    sudo apt install mesa-vulkan-drivers    (Fedora: mesa-vulkan-drivers; Arch: vulkan-radeon or vulkan-intel)"
  echo "  NVIDIA:                 the proprietary driver (nvidia-driver on Debian and Ubuntu, akmod-nvidia on Fedora, nvidia on Arch)"
  exit 1
fi
# its own Wine prefix (a private Windows-like folder), so nothing of yours is touched; the first run sets it up, and Wine may
# offer to fetch Wine Mono, its .NET: say yes, the program is a .NET one
export WINEPREFIX="${WINEPREFIX:-$HOME/.local/share/universe-wine}"
export WINEDEBUG="${WINEDEBUG:--all}"
mkdir -p "$WINEPREFIX"
cd "$here"
exec wine "./$exe" "$@"
