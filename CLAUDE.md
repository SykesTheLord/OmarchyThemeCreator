# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

Omarchy Theme Creator is a Linux-native GUI (**.NET 10 + Avalonia 11.2**) for authoring
[Omarchy](https://omarchy.org) themes. It ships as a single self-contained `linux-x64` binary.

## Commands

- **Build:** `dotnet build src/OmarchyThemeCreator/OmarchyThemeCreator.csproj`
- **Run:** `dotnet run --project src/OmarchyThemeCreator/OmarchyThemeCreator.csproj`
- **Publish single binary:** `bash packaging/build.sh` → `dist/omarchy-theme-creator`
- **No test suite exists.** After code changes, run `dotnet build` and fix compile errors
  before finishing. For UI changes, a brief `dotnet run` catches runtime XAML/binding errors
  that compile cleanly.

## Architecture

Three layers, wired by hand (no DI container) in `App.axaml.cs` — that file is the composition
root where every service and the `MainWindowViewModel` are constructed.

- `Services/` — stateless logic, no Avalonia/UI dependencies (theme I/O, palette extraction,
  wallhaven client, image editing via SkiaSharp, omarchy CLI wrapper). Prefer putting new logic
  here.
- `ViewModels/` — CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). One VM per
  tab, all owned by `MainWindowViewModel`.
- `Views/` — Avalonia XAML. `MainWindow.axaml.cs` wires View-only capabilities (file pickers,
  dialogs) onto the VM via `Func<>` hook properties set in `OnDataContextChanged`.

Key patterns:
- **`IPaletteHost` is the single choke point for palette changes.** Every tab pushes color
  changes through `ApplyPalette(...)` so the live preview, undo/redo history, and contrast panel
  stay in sync. Don't mutate the palette around it.
- **Services degrade gracefully when Omarchy isn't installed** (`OmarchyCliService.IsAvailable`,
  wallhaven failures throw clean messages surfaced in the status bar). Preserve this — the app
  must work as a pure editor with no `omarchy-*` tools on PATH.
- **XAML `IValueConverter`s** live in `Converters.cs` and are registered as resources in
  `App.axaml`.
- **Logging is Serilog via the static facade** (no DI). `AppLog.Initialize()` runs first in
  `Program.Main` and configures a rolling daily file under `$XDG_STATE_HOME/omarchy-theme-creator/logs`
  (fallback `~/.local/state/...`) plus a terminal console sink; it also installs the process-wide
  crash handlers. Each class gets a contextual logger with
  `private static readonly ILogger Log = Serilog.Log.ForContext<T>();`. Convention: services log
  significant state changes at `Information` and swallowed failures at `Warning`; VM command
  handlers log user-facing failures in their `catch` blocks. Keep per-frame/per-slider paths (image
  render, palette extraction) at `Debug` or unlogged to avoid flooding.

## Themes on disk

- User (editable) themes: `~/.config/omarchy/themes/<name>/`
- Built-in (read-only) themes: `~/.local/share/omarchy/themes/` (or `$OMARCHY_PATH/themes`)
- `Theme.IsBuiltIn` distinguishes them; `ThemeRepository` only ever writes to the user dir.
  Never write to or delete from the system dir.

## Avalonia 11.2 gotchas

- `Grid` does **not** support `RowSpacing`/`ColumnSpacing` in this version — use per-child
  `Margin` instead. (`StackPanel.Spacing` and `WrapPanel` are fine.)
- Compiled bindings are on by default (`AvaloniaUseCompiledBindingsByDefault=true`), but
  `MainWindow.axaml` sets `x:CompileBindings="False"`, so bindings there resolve at runtime.
- `AssemblyName` is `omarchy-theme-creator`; `RootNamespace` is `OmarchyThemeCreator`.
