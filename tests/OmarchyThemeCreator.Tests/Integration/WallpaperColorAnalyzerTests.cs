using System.Collections.Generic;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using SkiaSharp;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// Decodes real PNG bytes with SkiaSharp (native, no Avalonia needed) and checks the colour
/// make-up. Parallel-safe — no env vars.
/// </summary>
public sealed class WallpaperColorAnalyzerTests
{
    private readonly WallpaperColorAnalyzer _analyzer = new();

    private static byte[] RedBluePng() =>
        ImageFixtures.Png(ImageFixtures.TwoTone(96, 96, new SKColor(255, 0, 0), new SKColor(0, 0, 255)));

    [Fact]
    public void Analyze_detects_multiple_dominant_colors_in_a_two_tone_image()
    {
        ImageColors result = _analyzer.Analyze(RedBluePng());

        // Each half is ~50% of pixels, well above the 6% presence threshold.
        Assert.True(result.Dominant.Count >= 2);
        Assert.NotEmpty(result.Palette);
    }

    [Fact]
    public void Analyze_returns_empty_for_undecodable_bytes()
    {
        // The documented contract: an image that can't be decoded matches nothing. SkiaSharp throws
        // rather than returning null for garbage bytes, so Analyze catches it and returns empty.
        ImageColors result = _analyzer.Analyze(new byte[] { 1, 2, 3, 4 });
        Assert.Empty(result.Dominant);
        Assert.Empty(result.Palette);
    }

    [Fact]
    public void Analyze_returns_empty_for_a_fully_transparent_image()
    {
        // Every pixel has alpha 0 and is skipped, so the scan sees no pixels and returns empty.
        byte[] transparent = ImageFixtures.Png(ImageFixtures.SolidBitmap(16, 16, SKColors.Transparent));
        ImageColors result = _analyzer.Analyze(transparent);
        Assert.Empty(result.Dominant);
        Assert.Empty(result.Palette);
    }

    [Fact]
    public void ThemeMatch_scores_the_images_own_colors_higher_than_unrelated_ones()
    {
        ImageColors image = _analyzer.Analyze(RedBluePng());

        IReadOnlyList<ColorMath.Rgb> ownColors = new[]
        {
            new ColorMath.Rgb(255, 0, 0),
            new ColorMath.Rgb(0, 0, 255),
        };
        IReadOnlyList<ColorMath.Rgb> unrelated = new[] { new ColorMath.Rgb(0, 130, 0) };

        double selfScore = WallpaperColorAnalyzer.ThemeMatch(image, ownColors);
        double otherScore = WallpaperColorAnalyzer.ThemeMatch(image, unrelated);

        Assert.True(selfScore > otherScore,
            $"Expected self-match {selfScore:0.00} to exceed unrelated {otherScore:0.00}.");
    }

    [Fact]
    public void ThemeMatch_is_zero_when_either_side_is_empty()
    {
        ImageColors empty = new ImageColors(new HashSet<string>(), new List<WeightedColor>());
        Assert.Equal(0, WallpaperColorAnalyzer.ThemeMatch(empty, new[] { new ColorMath.Rgb(1, 2, 3) }));
    }
}
