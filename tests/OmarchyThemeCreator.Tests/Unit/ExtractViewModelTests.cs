using System;
using System.IO;
using System.Linq;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using OmarchyThemeCreator.ViewModels;
using SkiaSharp;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// The one view-model cheap to unit-test without a running Avalonia app. It exercises the
/// <see cref="IPaletteHost"/> contract from CLAUDE.md: a slider change must re-extract and push a
/// palette through the host with a <em>stable</em> coalesceKey so a slider drag folds into a
/// single undo step. Uses a real <see cref="PaletteExtractionService"/> against a generated PNG.
/// </summary>
public sealed class ExtractViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly string _wallpaper;
    private readonly FakePaletteHost _host = new();
    private readonly ExtractViewModel _vm;

    public ExtractViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
        using SKBitmap bmp = ImageFixtures.TwoTone(64, 64,
            new SKColor(20, 30, 60), new SKColor(200, 120, 40));
        _wallpaper = ImageFixtures.WritePng(bmp, _dir, "wall.png");

        _vm = new ExtractViewModel(new PaletteExtractionService(), _host);
        // LoadWallpaper sets the path without extracting (BitmapInterop.TryLoad returns null
        // headless — that's fine, it degrades gracefully).
        _vm.LoadWallpaper(_wallpaper);
    }

    [Fact]
    public void Changing_a_slider_pushes_a_palette_through_the_host()
    {
        _vm.Vibrance = 0.5;

        Assert.Single(_host.AppliedPalettes);
        FakePaletteHost.PaletteApplication applied = _host.AppliedPalettes[0];
        Assert.Equal("extract:" + _wallpaper, applied.CoalesceKey);
        Assert.Contains("wall.png", applied.Status);
    }

    [Fact]
    public void Successive_slider_changes_share_a_coalesce_key()
    {
        _vm.Vibrance = 0.3;
        _vm.Saturation = 1.5;
        _vm.Contrast = 0.2;

        Assert.Equal(3, _host.AppliedPalettes.Count);
        // Every drag folds into the same undo step: identical coalesceKey each time.
        Assert.All(_host.AppliedPalettes,
            a => Assert.Equal("extract:" + _wallpaper, a.CoalesceKey));
    }

    [Fact]
    public void Changing_light_mode_notifies_the_host_and_reextracts()
    {
        _vm.LightMode = true;

        Assert.Contains(true, _host.LightModeCalls);
        Assert.NotEmpty(_host.AppliedPalettes); // re-extracted after the mode flip
    }

    [Fact]
    public void Extraction_produces_a_readable_palette()
    {
        // Switch to a non-default mode so the change handler fires and an extract runs.
        _vm.Mode = ExtractionMode.Muted;

        OmarchyThemeCreator.Models.ThemeColors palette = _host.AppliedPalettes.Last().Palette;
        ColorAssert.ContrastAtLeast(palette.Foreground, palette.Background, 4.5);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
