using System;
using System.IO;
using SkiaSharp;

namespace OmarchyThemeCreator.Tests.Support;

/// <summary>
/// Generates small, deterministic test images in-memory with SkiaSharp, so no binary fixture
/// files need to be committed. Sizes are kept tiny (extraction/analysis downscale anyway) to keep
/// the tests fast.
/// </summary>
public static class ImageFixtures
{
    /// <summary>A solid single-colour bitmap.</summary>
    public static SKBitmap SolidBitmap(int w, int h, SKColor color)
    {
        SKBitmap bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using SKCanvas canvas = new SKCanvas(bmp);
        canvas.Clear(color);
        return bmp;
    }

    /// <summary>A half/half two-colour bitmap (left | right), giving a predictable dominant palette.</summary>
    public static SKBitmap TwoTone(int w, int h, SKColor left, SKColor right)
    {
        SKBitmap bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        using SKCanvas canvas = new SKCanvas(bmp);
        canvas.Clear(left);
        using SKPaint paint = new SKPaint { Color = right };
        canvas.DrawRect(new SKRect(w / 2f, 0, w, h), paint);
        return bmp;
    }

    /// <summary>Encode a bitmap to a PNG file in <paramref name="dir"/> and return its path.</summary>
    public static string WritePng(SKBitmap bmp, string dir, string name)
    {
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        using SKImage image = SKImage.FromBitmap(bmp);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.Create(path);
        data.SaveTo(fs);
        return path;
    }

    /// <summary>Encode a bitmap to PNG bytes (for APIs that take a <c>byte[]</c>).</summary>
    public static byte[] Png(SKBitmap bmp)
    {
        using SKImage image = SKImage.FromBitmap(bmp);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Mean per-channel value across every pixel — used to compare edit effects in aggregate.</summary>
    public static (double R, double G, double B) MeanRgb(SKBitmap bmp)
    {
        long r = 0, g = 0, b = 0;
        int n = bmp.Width * bmp.Height;
        for (int y = 0; y < bmp.Height; y++)
        for (int x = 0; x < bmp.Width; x++)
        {
            SKColor p = bmp.GetPixel(x, y);
            r += p.Red; g += p.Green; b += p.Blue;
        }
        return ((double)r / n, (double)g / n, (double)b / n);
    }
}
