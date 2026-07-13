# Omarchy Theme Creator

A Linux-native GUI for authoring [Omarchy](https://omarchy.org) themes. Edit the color
palette visually, watch a live preview recolor as you type, apply the theme to your
running desktop, manage wallpapers and icons, clone existing themes, and export a
ready-to-publish theme repository — all without hand-editing `colors.toml`. The editor's
own UI even follows your current desktop theme live.

Built with **.NET 10 + Avalonia**, ships as a **single self-contained binary**, and
includes a standard **AUR PKGBUILD**.

![screenshot placeholder](docs/screenshot.png)

## Features

- **Visual color editor** — all 22 `colors.toml` keys (`accent`, `cursor`,
  `foreground`, `background`, `selection_*`, and `color0`–`color15`) with hex input and
  a color picker, plus multi-step **undo/redo**.
- **Palette from wallpaper** — generate a full 22-key palette from any image with a
  population-weighted median-cut extractor. Colors are mapped onto their **canonical ANSI
  slots** (so `color1` is red, `color2` green, `color4` blue… regardless of the wallpaper's
  dominant hue), anchors come from the image's *dominant* dark/light tones, and a
  readability pass **guarantees WCAG-legible contrast** against the background. Includes 8
  extraction modes (Normal, Monochromatic, Analogous, Pastel, Material, Muted, Bright,
  Colorful), light/dark anchor swap, and fine-tuning sliders (vibrance, saturation,
  contrast, brightness, temperature, shadows, highlights).
- **Presets & Base16 import** — start from a built-in scheme (Dracula, Nord, Gruvbox,
  Catppuccin, Rosé Pine, Solarized, and more) or import any community Base16 `.yaml`.
- **Contrast checker** — live WCAG AA/AAA grading of the key foreground/background pairs.
- **Wallpaper tools** — search and download wallpapers from wallhaven.cc, and apply a
  raster filter editor (brightness, contrast, saturation, blur, vignette, grain, plus
  one-click looks like Cinematic, Vintage, and Noir) before saving to `backgrounds/`.
- **Live preview** — a mock waybar + terminal + desktop panel updates instantly as you
  edit, so you see the theme before saving.
- **Apply live** — one click runs `omarchy-theme-set` and your whole desktop switches
  to the theme for real-world testing.
- **Self-theming UI** — the app recolors its own chrome to match your *current* Omarchy
  desktop theme and updates instantly whenever you switch themes (including right after
  you hit **Apply**). Falls back to a built-in dark theme when Omarchy isn't installed.
- **Backgrounds & icons** — import wallpapers into `backgrounds/`, pick an
  `icons.theme` (Yaru variants detected from your system), toggle light mode, and
  generate a `preview.png`.
- **Open / clone existing themes** — load any installed theme (built-in or user) to
  tweak it, or clone a built-in theme into your user themes directory to modify safely.
- **Export for distribution** — scaffold an `omarchy-<name>-theme` git repo (with
  README + LICENSE) ready to push to GitHub and install via `omarchy-theme-install`.

## How Omarchy themes work

An Omarchy theme is a folder in `~/.config/omarchy/themes/<name>/`. The only mandatory
file is **`colors.toml`**, a flat palette of 22 hex colors. When you apply a theme,
Omarchy runs a template pass (`omarchy-theme-set-templates`) that substitutes those
colors into per-app config templates — producing themed configs for Alacritty, Ghostty,
Kitty, foot, btop, Waybar, Walker, Mako, Hyprland, Hyprlock, SwayOSD, and more.

Optional files a theme may include: `light.mode` (marker enabling light mode),
`icons.theme`, a `backgrounds/` directory of wallpapers, `preview.png`, and app-specific
overrides (`waybar.css`, `hyprland.conf`, `swayosd.css`).

See the official guide:
<https://learn.omacom.io/2/the-omarchy-manual/92/making-your-own-theme>

## Build from source

Requires the **.NET 10 SDK** (`dotnet-sdk` on Arch).

```bash
make -C packaging build
./dist/omarchy-theme-creator
```

This publishes a single self-contained `linux-x64` binary to `dist/`. (`make -C packaging
build` just wraps `bash packaging/build.sh`, which you can also run directly.)

## Install

### From the AUR (once published)

```bash
yay -S omarchy-theme-creator
```

### From this repo with makepkg

```bash
cd packaging
makepkg -si
```

### With make (local, non-AUR)

The Makefile lives in `packaging/`:

```bash
make -C packaging build
sudo make -C packaging install      # installs to /usr/local by default
```

To remove it again: `sudo make -C packaging uninstall`.

### Updating a source install

After pulling new commits (or editing the code), rebuild and replace the installed
binary in one step:

```bash
make -C packaging dev
```

This runs `packaging/dev-update.sh`: it rebuilds the single binary (`make -C packaging
build`) and overwrites whichever copy is already installed — located via your `PATH`,
falling back to `/usr/local/bin` — using `sudo` only when the target isn't writable. It
also refreshes the `.desktop` entry and icon if that prefix owns them. Restart the app to
pick up the new build.

The manual equivalent is `make -C packaging build && sudo make -C packaging install`.

## Usage

1. Launch **Omarchy Theme Creator**.
2. Create a new theme, or open/clone an existing one from the sidebar.
3. Edit colors — the preview updates live.
4. Optionally add wallpapers, choose an icon theme, and toggle light mode.
5. **Save** (writes to `~/.config/omarchy/themes/<name>/`), then **Apply** to switch
   your desktop.
6. **Export for distribution** to produce an `omarchy-<name>-theme/` git repo. Push it
   to GitHub, then anyone can install it with:
   ```bash
   omarchy-theme-install https://github.com/you/omarchy-<name>-theme.git
   ```

## License

MIT — see [LICENSE](LICENSE).
