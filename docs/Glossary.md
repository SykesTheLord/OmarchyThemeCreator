# Glossary

> Terms used throughout the docs and the codebase.

Related: [[Home]] · [[06-Palette-Flow-and-IPaletteHost]] · [[07-Palette-Extraction]] · [[10-Theme-Persistence]]

### 22-key palette
An Omarchy `colors.toml` has 22 color keys: 6 **named** (`accent`, `cursor`, `foreground`,
`background`, `selection_foreground`, `selection_background`) + 16 **ANSI** (`color0`…`color15`).
Modeled by `ThemeColors`. See [[10-Theme-Persistence]].

### ANSI colors
`color0`…`color15` — the standard terminal palette. `0/8` are black shades, `7/15` white shades, and
`1–6` / `9–14` are the normal/bright chromatic colors (red, green, yellow, blue, magenta, cyan). The
extractor pins `1–6` to canonical terminal hues so "red" stays red. See [[07-Palette-Extraction]].

### Anchor (extraction)
The dominant dark and light colors chosen as background/foreground, selected by pixel **weight**
(not extreme outliers). Anchors skip hue/saturation shaping in `FinalHex` to stay neutral.
See [[07-Palette-Extraction]].

### Base16
A community color-scheme format (16 colors as a `.yaml` file). `PresetService.ImportBase16` maps a
Base16 scheme onto Omarchy's 22 keys. See [[03-Services-Layer]].

### Choke point
The single method every color change must pass through:
[[06-Palette-Flow-and-IPaletteHost|IPaletteHost.ApplyPalette]]. Guarantees preview, undo/redo, and
the contrast panel stay in sync.

### coalesceKey
A string passed to `ApplyPalette`. When consecutive calls share the same key (e.g. a slider drag
re-extracting the same wallpaper), the changes **fold into one undo step** instead of many. Distinct
from the `_editBurstActive` mechanism used for manual hex typing. See
[[06-Palette-Flow-and-IPaletteHost]].

### Composition root
`App.axaml.cs` → `OnFrameworkInitializationCompleted`, where every service and the
`MainWindowViewModel` are constructed by hand (no DI container). See
[[02-Bootstrap-and-Composition-Root]].

### DynamicResource
An Avalonia binding to a resource that re-evaluates when the resource changes. The app's chrome
brushes are `DynamicResource`s, so [[09-Live-Theming-and-Self-Sync|AppThemeSync]] can recolor the UI
live by overwriting them.

### Extraction mode
One of `Normal, Monochromatic, Analogous, Pastel, Material, Muted, Bright, Colorful` — an HSL
transform applied to non-anchor colors during extraction. See [[07-Palette-Extraction]].

### Func<> hook
A delegate property a ViewModel exposes so a View can supply a UI capability (file picker, dialog)
without the VM referencing Avalonia. Wired in `MainWindow.OnDataContextChanged`. See
[[05-Views-and-Dialogs]].

### Graceful degradation
The contract that desktop integrations report absence (`IsAvailable == false`, or return `null`)
rather than throwing, so the app runs as a pure editor with no `omarchy-*` tools on `PATH`. Applies
to `OmarchyCliService`, `LiveOmarchyThemeService`, `ScreenColorPicker`. See [[01-Overview]].

### IPaletteHost
The interface the tab VMs use to read/write the shared palette and reach a few host capabilities
(image pick, background add, staging dir). Implemented by `MainWindowViewModel`. See
[[06-Palette-Flow-and-IPaletteHost]].

### Live theme
The Omarchy theme **currently applied to the desktop** (under `~/.config/omarchy/current`), as
opposed to the theme being **edited**. Tracked by `LiveOmarchyThemeService`. See
[[09-Live-Theming-and-Self-Sync]].

### Median-cut
The quantization algorithm that reduces an image to ~32 representative swatches by repeatedly
splitting the color box with the largest range × population. See [[07-Palette-Extraction]].

### `NN-slug.ext`
The naming convention for theme backgrounds (e.g. `01-sunset.jpg`). The `NN-` prefix sets the order
Omarchy's background switcher cycles through them. Managed by `ThemeRepository`. See
[[10-Theme-Persistence]].

### Self-theming
The app recoloring its **own** UI chrome to match the live desktop theme, via `AppThemeSync`. See
[[09-Live-Theming-and-Self-Sync]].

### Staging dir
A temp folder (`IPaletteHost.StagingDir`, under `Path.GetTempPath()/omarchy-theme-creator`) where
downloaded/edited wallpapers sit **before** a theme is saved. Only `ThemeRepository` promotes them
into a theme's `backgrounds/`. See [[08-Wallpaper-Tools]].

### `_suppress` / `_updating` (reentrancy guard)
A boolean flag used when a value is mirrored across representations (hex ⇄ Color, HSV ⇄ RGB) to stop
one setter from re-triggering the other in an infinite loop. See
[[04-ViewModels-and-MVVM#The _suppress reentrancy pattern]].

### Swatch
A quantized color plus its pixel population (`Weight`), the unit produced by median-cut and consumed
by palette assembly. See [[07-Palette-Extraction]].

### WCAG contrast
The accessibility ratio (1–21) between two colors. `ColorMath.ContrastRatio` computes it,
`ColorMath.Grade` labels it (AAA/AA/AA Large/Fail), and `EnsureContrast` nudges lightness to hit a
threshold. Used by the contrast panel and the extractor's readability pass. See
[[07-Palette-Extraction]].
