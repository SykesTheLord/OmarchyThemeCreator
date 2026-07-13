# Data Flows

> End-to-end sequence diagrams for the main user actions, tying the layers together.

Related: [[Home]] · [[06-Palette-Flow-and-IPaletteHost]] · [[07-Palette-Extraction]] · [[08-Wallpaper-Tools]] · [[09-Live-Theming-and-Self-Sync]] · [[10-Theme-Persistence]]

Each flow below crosses View → ViewModel → Service and back. The common spine is
[[06-Palette-Flow-and-IPaletteHost|ApplyPalette]], the single choke point for color changes.

## 1. Edit a single color (color picker)

```mermaid
sequenceDiagram
    participant U as User
    participant W as MainWindow (View)
    participant VM as MainWindowViewModel
    participant D as ColorPickerDialog
    participant CW as ColorWheel + ColorPickerViewModel

    U->>VM: EditColor(field) command
    VM->>W: await PickColorAsync(label, field.Color)
    W->>D: new ColorPickerDialog(...).ShowDialog<Color?>()
    U->>CW: drag ring / SV / type hex
    CW->>CW: Hsv (source of truth) → readouts
    U->>D: OK
    D-->>VM: Color?  (null = cancel → no-op)
    VM->>VM: next = _working.Clone(); next.Set(key, hex)
    VM->>VM: ApplyPalette(next, "Set Accent…")
    Note over VM: push undo · Preview.Update · Contrast.Update · rebuild fields
```

## 2. Extract a palette from a wallpaper

```mermaid
sequenceDiagram
    participant U as User
    participant EV as ExtractViewModel
    participant PE as PaletteExtractionService
    participant H as IPaletteHost (MainWindowViewModel)

    U->>EV: PickWallpaper() → path
    EV->>PE: Extract(path, options)
    PE->>PE: QuantizeImage (cache miss) → 32 swatches
    PE->>PE: BuildPalette + modes + WCAG post-pass
    PE-->>EV: ThemeColors
    EV->>H: ApplyPalette(colors, "Extracted…", coalesceKey: "extract:"+path)
    loop drag a slider
        U->>EV: OnVibranceChanged → Extract()
        EV->>PE: Extract(path, options)  (reuses cached swatches)
        EV->>H: ApplyPalette(..., same coalesceKey)  → folds into one undo step
    end
```

See [[07-Palette-Extraction]] for the pipeline internals and [[06-Palette-Flow-and-IPaletteHost#1. coalesceKey — for programmatic bursts (slider drags)|coalesceKey]] for the undo folding.

## 3. Download a wallhaven wallpaper and add it as a background

```mermaid
sequenceDiagram
    participant U as User
    participant WV as WallpaperViewModel
    participant WH as WallhavenService
    participant AN as WallpaperColorAnalyzer
    participant H as IPaletteHost
    participant TR as ThemeRepository

    U->>WV: Search()
    WV->>WH: SearchAsync(filter, apiKey, page)
    WH-->>WV: WallhavenPage
    par per result (background threads)
        WV->>WH: TryGetBytesAsync(thumbUrl)
        WV->>AN: Analyze(bytes) → Dominant + ThemeMatch
    end
    U->>WV: SaveToBackgrounds() (after optional edits)
    WV->>WH: DownloadAsync(fullUrl, StagingDir)
    WV->>H: AddBackground(stagedPath)
    H->>TR: AddBackground(theme, stagedPath) → NN-slug.ext
    H->>H: prompt for name, RenameBackground
    H-->>WV: BackgroundAddResult (final path, WasNew)
```

The wallpaper lands in the [[Glossary#Staging dir|staging dir]] first; it becomes part of the theme
only via `ThemeRepository`. See [[08-Wallpaper-Tools]] and [[10-Theme-Persistence]].

## 4. Save, then apply to the live desktop

```mermaid
sequenceDiagram
    participant U as User
    participant VM as MainWindowViewModel
    participant TR as ThemeRepository
    participant CLI as OmarchyCliService
    participant LT as LiveOmarchyThemeService
    participant App

    U->>VM: Apply command
    VM->>VM: Save() first (disk matches editor)
    VM->>TR: Save(theme) → user dir only
    VM->>CLI: ApplyThemeAsync(name)  (if IsAvailable)
    CLI->>CLI: run `omarchy-theme-set <name>`
    Note over CLI: swaps ~/.config/omarchy/current atomically
    CLI-->>LT: FileSystemWatcher fires (debounced 200ms)
    LT-->>App: Changed → Dispatcher.UIThread.Post(ApplyLiveTheme)
    App->>App: AppThemeSync.Apply(app, colors) → UI recolors live
```

If the CLI isn't installed, the *Apply* button is disabled and only `Save` runs — the editor still
works fully. See [[09-Live-Theming-and-Self-Sync]].
