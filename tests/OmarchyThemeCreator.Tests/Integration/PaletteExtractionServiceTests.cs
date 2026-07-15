using System;
using System.IO;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using SkiaSharp;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// Drives the private median-cut / palette-build / readability pipeline through the public
/// <see cref="PaletteExtractionService.Extract"/> using tiny generated wallpapers. No env vars, so
/// parallel-safe.
/// </summary>
public sealed class PaletteExtractionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
    private readonly PaletteExtractionService _service = new();

    public PaletteExtractionServiceTests() => Directory.CreateDirectory(_dir);

    private string TwoToneWallpaper() =>
        ImageFixtures.WritePng(
            ImageFixtures.TwoTone(120, 120, new SKColor(18, 24, 48), new SKColor(210, 120, 40)),
            _dir, "two-tone.png");

    [Fact]
    public void Extract_populates_all_22_keys_with_valid_hex()
    {
        ThemeColors palette = _service.Extract(TwoToneWallpaper(), new ExtractionOptions());

        foreach (var pair in palette.AsPairs())
            ColorAssert.IsValidHex(pair.Value);
    }

    [Fact]
    public void Extract_guarantees_readable_foreground_on_background()
    {
        ThemeColors palette = _service.Extract(TwoToneWallpaper(), new ExtractionOptions());
        // This is the whole point of the EnforceReadability post-pass.
        ColorAssert.ContrastAtLeast(palette.Foreground, palette.Background, 4.5);
    }

    [Fact]
    public void Extract_accent_differs_from_background()
    {
        ThemeColors palette = _service.Extract(TwoToneWallpaper(), new ExtractionOptions());
        Assert.NotEqual(palette.Background, palette.Accent);
    }

    [Theory]
    [InlineData(ExtractionMode.Normal)]
    [InlineData(ExtractionMode.Monochromatic)]
    [InlineData(ExtractionMode.Analogous)]
    [InlineData(ExtractionMode.Pastel)]
    [InlineData(ExtractionMode.Material)]
    [InlineData(ExtractionMode.Muted)]
    [InlineData(ExtractionMode.Bright)]
    [InlineData(ExtractionMode.Colorful)]
    public void Every_mode_produces_a_valid_readable_palette(ExtractionMode mode)
    {
        ThemeColors palette = _service.Extract(TwoToneWallpaper(), new ExtractionOptions { Mode = mode });

        foreach (var pair in palette.AsPairs())
            ColorAssert.IsValidHex(pair.Value);
        ColorAssert.ContrastAtLeast(palette.Foreground, palette.Background, 4.5);
    }

    [Fact]
    public void Light_mode_produces_a_light_background()
    {
        ThemeColors palette = _service.Extract(TwoToneWallpaper(), new ExtractionOptions { LightMode = true });
        // Light mode shapes the background lightness up to ~0.94.
        Assert.True(ColorMath.ToHsl(palette.Background).L > 0.7);
    }

    [Fact]
    public void Repeated_extract_with_different_tuning_reuses_cache_and_still_differs()
    {
        string wallpaper = TwoToneWallpaper();

        // Same path twice → the second call hits the swatch cache. Different options must still
        // yield a different palette (the cache only stores quantization, not the tuned result).
        ThemeColors muted = _service.Extract(wallpaper, new ExtractionOptions { Mode = ExtractionMode.Muted });
        ThemeColors colorful = _service.Extract(wallpaper, new ExtractionOptions { Mode = ExtractionMode.Colorful });

        Assert.NotEqual(muted.Accent, colorful.Accent);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
