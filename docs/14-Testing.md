# 14 — Testing

> The automated test suite: what it covers, how it stays isolated from your real config, and what
> it deliberately leaves out.

Related: [[Home]] · [[03-Services-Layer]] · [[07-Palette-Extraction]] · [[06-Palette-Flow-and-IPaletteHost]] · [[10-Theme-Persistence]] · [[08-Wallpaper-Tools]]

The suite lives in `tests/OmarchyThemeCreator.Tests/` (xUnit) and is registered in
`OmarchyThemeCreator.sln`, so `dotnet test` on the solution builds and runs it. It has two tiers.

## The two tiers

**Unit tests** (`Unit/`) cover the pure-logic core — the code that has no filesystem, network, or
SkiaSharp dependency — through its **public API only**. No `InternalsVisibleTo`, no reflection: if
an algorithm is private (median-cut, the colour matrices), it's exercised through its public entry
point rather than by prying it open. This keeps encapsulation intact and the tests honest about
what a caller can actually observe.

- `ColorMathTests` — the bedrock: hex/RGB/HSL round-trips, WCAG luminance/contrast/grade,
  `EnsureContrast`, blending, hue geometry. See [[07-Palette-Extraction]] for how this maths is used.
- `ThemeColorsTests`, `ThemeTests`, `WallpaperFilterTests` — the model transforms
  (hex normalisation, slug/display-name, wallhaven purity/category masks).
- `ColorsTomlServiceTests` — `Parse`/`Serialize` and their in-memory round-trip (see [[10-Theme-Persistence]]).
- `WallhavenServiceTests`, `PresetServiceTests`, `ImageEditServiceTests` — the pure slices of
  otherwise I/O-bound services (`NearestColor`, built-in preset integrity, preset selection).
- `ExtractViewModelTests` — the one view-model cheap to test headless. It asserts the
  [[06-Palette-Flow-and-IPaletteHost|IPaletteHost]] contract: a slider change re-extracts and pushes
  a palette through the host with a **stable `coalesceKey`**, so a slider drag folds into a single
  undo step.

**Integration tests** (`Integration/`) cover the filesystem and SkiaSharp boundaries with real I/O.

- `ThemeRepositoryTests` — list/save/load/clone, the background lifecycle, the safety
  invariant that writes never escape the user dir into the read-only system dir, and that `Save`
  strips stale colour-derived configs (`btop.theme`, terminal themes) while keeping structural
  overrides so edited palettes actually reach btop (see [[10-Theme-Persistence]]).
- `SettingsServiceTests` — graceful degradation on a missing or malformed settings file.
- `ColorsTomlRoundTripTests` — the on-disk `Save` → `Load` round-trip.
- `PaletteExtractionServiceTests` — drives the private median-cut → palette-build → readability
  pipeline through `Extract`, asserting the **readability invariant** (foreground/background WCAG
  contrast ≥ 4.5) for every [[07-Palette-Extraction|extraction mode]].
- `WallpaperColorAnalyzerTests`, `ImageEditPipelineTests` — colour analysis and the SkiaSharp edit
  pipeline, asserted on aggregate pixel statistics (never exact bytes — encoding is lossy).
- `ExportServiceTests`, `PresetServiceImportTests` — theme export scaffolding and Base16 `.yaml`
  import.

## Running

```bash
dotnet test                             # whole solution
dotnet test --filter FullyQualifiedName~Unit          # unit tier only
dotnet test --filter FullyQualifiedName~Integration   # integration tier only
```

The whole suite runs in well under a second; SkiaSharp fixture generation and temp-dir I/O
dominate the time.

## The `$HOME` seam (most important thing to understand)

Both `ThemeRepository` and `SettingsService` derive their base directory from
`Environment.GetFolderPath(SpecialFolder.UserProfile)`, which on Linux reads **`$HOME`**.
`ThemeRepository` additionally honours **`$OMARCHY_PATH`** for the read-only built-in themes dir.

`Support/TempHome` (an `IDisposable`) redirects both env vars to a throwaway temp directory for the
lifetime of a test, then restores them and deletes the temp tree on dispose. That's the entire
trick that makes the filesystem services testable **without mocking and without ever touching your
real `~/.config/omarchy`**.

```csharp
using TempHome home = new();
home.SeedTheme("nord", builtIn: true);   // writes $OMARCHY_PATH/themes/nord/colors.toml
ThemeRepository repo = new();            // picks up the redirected dirs
```

Because environment variables are **process-global**, every test class that uses `TempHome` is
annotated `[Collection("env")]` — a non-parallel collection (see `Support/EnvCollection`). Without
that, one test's `$HOME` would leak into another running concurrently. Tests that touch no shared
global state (all the `Unit/` tests and the SkiaSharp integration tests) stay fully parallel.

## Fixtures and helpers

`Support/` holds the shared scaffolding:

- **`ImageFixtures`** — generates tiny deterministic images with SkiaSharp (`SolidBitmap`,
  `TwoTone`) and encodes them to a temp file or `byte[]`. **No binary fixtures are committed** — every
  test image is synthesised in-memory, so the tests stay deterministic and the repo stays clean.
- **`FakePaletteHost`** — a hand-rolled recording [[06-Palette-Flow-and-IPaletteHost|IPaletteHost]]
  double. Records every `ApplyPalette` call (palette + status + `coalesceKey`) for assertions.
- **`ColorAssert`** — approximate colour assertions (`AreClose`, `IsValidHex`, `ContrastAtLeast`).
  Quantization and matrix maths are never bit-exact, so tests assert on **ranges and relationships**,
  never a fixed hex string.

## What we deliberately don't test

- **Real network** (wallhaven search/download) — would need HTTP interception for little value; only
  the pure `NearestColor` helper is covered.
- **Real `omarchy-*` CLI** (`OmarchyCliService`) — shells out to external tools that aren't present
  in CI-like environments.
- **Avalonia rendering** (`BitmapInterop`, views, dialogs) — needs a running UI toolkit. Consistent
  with `CLAUDE.md`, these paths are exercised by hand with a quick `dotnet run`.

## A bug the suite caught

Writing `WallpaperColorAnalyzerTests` surfaced a real contract violation. `Analyze(byte[])` is
documented to return **empty results** for an image it can't decode, but `SKBitmap.Decode(byte[])`
(SkiaSharp 2.88) **throws** on genuinely malformed bytes rather than returning `null` — so the
graceful path was never actually reached, and `Analyze` would propagate an `ArgumentNullException`
to its callers. The fix wraps the decode in a try/catch so both "returned null" and "threw" collapse
to the documented empty result. This is exactly what a test suite is for: the behaviour compiled and
looked correct, but only exercising it revealed the gap.

## Conventions

- Explicit types only (no `var`), per `.editorconfig`.
- xUnit's built-in `Assert` — **no FluentAssertions** (its v8 licence is commercial).
- `NSubstitute` is available for mocking, though the single interface in play (`IPaletteHost`) uses
  the hand-rolled `FakePaletteHost` for clearer call-sequence assertions.
