using System;
using System.IO;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using SkiaSharp;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// Verifies the SkiaSharp edit pipeline behaviourally — on aggregate pixel statistics, never exact
/// bytes (encoding/rendering is lossy). Parallel-safe.
/// </summary>
public sealed class ImageEditPipelineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
    private readonly ImageEditService _service = new();

    public ImageEditPipelineTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Brightness_up_raises_the_mean_pixel_value()
    {
        using SKBitmap src = ImageFixtures.SolidBitmap(32, 32, new SKColor(100, 100, 100));
        using SKBitmap brighter = _service.Apply(src, new ImageEditOptions { Brightness = 1.8f });

        (double R, double _, double __) baseMean = ImageFixtures.MeanRgb(src);
        (double R, double _, double __) editMean = ImageFixtures.MeanRgb(brighter);
        Assert.True(editMean.R > baseMean.R + 20, $"Expected brighter mean, got {editMean.R} vs {baseMean.R}.");
    }

    [Fact]
    public void Saturation_zero_produces_gray_pixels()
    {
        using SKBitmap src = ImageFixtures.SolidBitmap(32, 32, new SKColor(200, 40, 40));
        using SKBitmap gray = _service.Apply(src, new ImageEditOptions { Saturation = 0f });

        SKColor p = gray.GetPixel(16, 16);
        // R≈G≈B when fully desaturated.
        Assert.True(Math.Abs(p.Red - p.Green) <= 4 && Math.Abs(p.Green - p.Blue) <= 4,
            $"Expected gray, got ({p.Red},{p.Green},{p.Blue}).");
    }

    [Fact]
    public void Blur_changes_pixels_versus_an_unedited_render()
    {
        using SKBitmap src = ImageFixtures.TwoTone(64, 64, SKColors.Black, SKColors.White);
        using SKBitmap identity = _service.Apply(src, new ImageEditOptions());
        using SKBitmap blurred = _service.Apply(src, new ImageEditOptions { Blur = 4f });

        Assert.True(MaxAbsDiff(identity, blurred) > 0, "Blur should alter pixels near the seam.");
    }

    [Fact]
    public void Save_writes_a_decodable_png()
    {
        using SKBitmap src = ImageFixtures.SolidBitmap(16, 16, new SKColor(30, 90, 150));
        string dest = Path.Combine(_dir, "out.png");

        _service.Save(src, dest);

        Assert.True(File.Exists(dest));
        using SKBitmap? decoded = SKBitmap.Decode(dest);
        Assert.NotNull(decoded);
    }

    private static int MaxAbsDiff(SKBitmap a, SKBitmap b)
    {
        int max = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            SKColor pa = a.GetPixel(x, y), pb = b.GetPixel(x, y);
            max = Math.Max(max, Math.Abs(pa.Red - pb.Red));
            max = Math.Max(max, Math.Abs(pa.Green - pb.Green));
            max = Math.Max(max, Math.Abs(pa.Blue - pb.Blue));
        }
        return max;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
