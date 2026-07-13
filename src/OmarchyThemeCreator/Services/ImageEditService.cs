using System;
using System.Collections.Generic;
using System.IO;
using SkiaSharp;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Non-destructive wallpaper filtering with SkiaSharp: tone/exposure adjustments plus blur,
/// sharpen, vignette, grain, and color toning, driven by <see cref="ImageEditOptions"/>. Also
/// exposes a set of one-click named presets (Cinematic, Vintage, …), mirroring Aether's editor.
/// </summary>
public sealed class ImageEditService
{
    /// <summary>Apply the options to an image on disk, returning a new bitmap the caller owns.</summary>
    public SKBitmap Apply(string imagePath, ImageEditOptions o)
    {
        using SKBitmap src = SKBitmap.Decode(imagePath)
                        ?? throw new InvalidOperationException("Could not decode image.");
        return Apply(src, o);
    }

    /// <summary>Apply the options to an already-decoded bitmap, returning a new bitmap the caller
    /// owns. Lets a caller decode once (e.g. at a reduced preview size) and re-render on every
    /// adjustment without re-reading the file — the live path for the editor dialog's sliders.</summary>
    public SKBitmap Apply(SKBitmap src, ImageEditOptions o)
    {
        SKImageInfo info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKBitmap dst = new SKBitmap(info);
        using SKCanvas canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.Black);

        using SKPaint paint = new SKPaint { IsAntialias = true };
        paint.ColorFilter = BuildColorFilter(o);
        paint.ImageFilter = BuildImageFilter(o);
        canvas.DrawBitmap(src, 0, 0, paint);

        if (o.Vignette > 1e-3) DrawVignette(canvas, info, o.Vignette);
        if (o.Grain > 1e-3) DrawGrain(canvas, info, o.Grain);
        if (o.ToneStrength > 1e-3) DrawTone(canvas, info, o.ToneColor, o.ToneStrength);

        canvas.Flush();
        return dst;
    }

    public void Save(SKBitmap bitmap, string destPath)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        string ext = Path.GetExtension(destPath).ToLowerInvariant();
        SKEncodedImageFormat format = ext == ".png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using SKData data = image.Encode(format, 92);
        using FileStream fs = File.Create(destPath);
        data.SaveTo(fs);
    }

    // ---- Color adjustments (composed color filters) -------------------

    private static SKColorFilter? BuildColorFilter(ImageEditOptions o)
    {
        SKColorFilter? filter = null;
        Compose(ref filter, BrightnessMatrix(o.Brightness * MathF.Pow(2, o.Exposure)));
        Compose(ref filter, ContrastMatrix(o.Contrast));
        Compose(ref filter, SaturationMatrix(o.Saturation));
        return filter;
    }

    private static void Compose(ref SKColorFilter? acc, float[]? matrix)
    {
        if (matrix is null) return;
        SKColorFilter next = SKColorFilter.CreateColorMatrix(matrix);
        acc = acc is null ? next : SKColorFilter.CreateCompose(next, acc);
    }

    private static float[]? BrightnessMatrix(float b) => Math.Abs(b - 1f) < 1e-3f ? null : new[]
    {
        b, 0, 0, 0, 0,
        0, b, 0, 0, 0,
        0, 0, b, 0, 0,
        0, 0, 0, 1, 0,
    };

    private static float[]? ContrastMatrix(float c)
    {
        if (Math.Abs(c - 1f) < 1e-3f) return null;
        float t = 0.5f * (1f - c);
        return new[]
        {
            c, 0, 0, 0, t,
            0, c, 0, 0, t,
            0, 0, c, 0, t,
            0, 0, 0, 1, 0,
        };
    }

    private static float[]? SaturationMatrix(float s)
    {
        if (Math.Abs(s - 1f) < 1e-3f) return null;
        const float lr = 0.3086f, lg = 0.6094f, lb = 0.0820f;
        float ir = (1 - s) * lr, ig = (1 - s) * lg, ib = (1 - s) * lb;
        return new[]
        {
            ir + s, ig,     ib,     0, 0,
            ir,     ig + s, ib,     0, 0,
            ir,     ig,     ib + s, 0, 0,
            0,      0,      0,      1, 0,
        };
    }

    // ---- Structural filters -------------------------------------------

    private static SKImageFilter? BuildImageFilter(ImageEditOptions o)
    {
        SKImageFilter? filter = null;
        if (o.Blur > 1e-3f)
            filter = SKImageFilter.CreateBlur(o.Blur, o.Blur);
        if (o.Sharpen > 1e-3f)
        {
            float a = o.Sharpen;
            float[] kernel = new[]
            {
                0f, -a, 0f,
                -a, 1f + 4 * a, -a,
                0f, -a, 0f,
            };
            SKImageFilter sharpen = SKImageFilter.CreateMatrixConvolution(
                new SKSizeI(3, 3), kernel, 1f, 0f, new SKPointI(1, 1),
                SKShaderTileMode.Clamp, convolveAlpha: false, input: filter);
            filter = sharpen;
        }
        return filter;
    }

    private static void DrawVignette(SKCanvas canvas, SKImageInfo info, float strength)
    {
        SKPoint center = new SKPoint(info.Width / 2f, info.Height / 2f);
        float radius = MathF.Sqrt(center.X * center.X + center.Y * center.Y);
        byte edge = (byte)(255 * (1 - Math.Clamp(strength, 0, 1)));
        using SKShader shader = SKShader.CreateRadialGradient(
            center, radius,
            new[] { SKColors.White, new SKColor(edge, edge, edge) },
            new[] { 0.45f, 1f }, SKShaderTileMode.Clamp);
        using SKPaint paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Multiply };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }

    private static void DrawGrain(SKCanvas canvas, SKImageInfo info, float strength)
    {
        using SKShader noise = SKShader.CreatePerlinNoiseFractalNoise(0.9f, 0.9f, 2, 0f);
        using SKPaint paint = new SKPaint
        {
            Shader = noise,
            BlendMode = SKBlendMode.SoftLight,
            Color = SKColors.White.WithAlpha((byte)(Math.Clamp(strength, 0, 1) * 120)),
        };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }

    private static void DrawTone(SKCanvas canvas, SKImageInfo info, SKColor tone, float strength)
    {
        using SKPaint paint = new SKPaint
        {
            Color = tone.WithAlpha((byte)(Math.Clamp(strength, 0, 1) * 160)),
            BlendMode = SKBlendMode.Overlay,
        };
        canvas.DrawRect(new SKRect(0, 0, info.Width, info.Height), paint);
    }

    // ---- Presets ------------------------------------------------------

    public IReadOnlyList<string> PresetNames => new[]
    {
        "Original", "Cinematic", "Vintage", "Film", "Dramatic",
        "Noir", "Warm", "Cool", "Faded", "Vivid", "Matte", "Dreamy",
    };

    public ImageEditOptions Preset(string name) => name switch
    {
        "Cinematic" => new() { Contrast = 1.15f, Saturation = 0.9f, Vignette = 0.35f, ToneColor = new SKColor(20, 40, 80), ToneStrength = 0.25f },
        "Vintage" => new() { Contrast = 0.9f, Saturation = 0.7f, Exposure = 0.1f, Grain = 0.35f, ToneColor = new SKColor(120, 90, 40), ToneStrength = 0.3f },
        "Film" => new() { Contrast = 1.05f, Saturation = 0.95f, Grain = 0.4f, Vignette = 0.2f },
        "Dramatic" => new() { Contrast = 1.35f, Saturation = 1.2f, Sharpen = 0.4f, Vignette = 0.4f },
        "Noir" => new() { Saturation = 0f, Contrast = 1.3f, Vignette = 0.45f, Grain = 0.25f },
        "Warm" => new() { ToneColor = new SKColor(200, 120, 40), ToneStrength = 0.3f, Saturation = 1.1f },
        "Cool" => new() { ToneColor = new SKColor(40, 120, 200), ToneStrength = 0.3f, Saturation = 1.05f },
        "Faded" => new() { Contrast = 0.8f, Saturation = 0.8f, Brightness = 1.08f },
        "Vivid" => new() { Saturation = 1.4f, Contrast = 1.1f, Sharpen = 0.25f },
        "Matte" => new() { Contrast = 0.85f, Brightness = 1.05f, Vignette = 0.15f },
        "Dreamy" => new() { Blur = 2.5f, Brightness = 1.1f, Saturation = 1.1f, Vignette = 0.2f },
        _ => new ImageEditOptions(),
    };
}

/// <summary>
/// Wallpaper filter parameters. Factors default to 1.0 and additive amounts to 0, so a fresh
/// <see cref="ImageEditOptions"/> is a no-op passthrough.
/// </summary>
public sealed record ImageEditOptions
{
    public float Brightness { get; init; } = 1f;   // multiplicative
    public float Contrast { get; init; } = 1f;      // multiplicative around mid-gray
    public float Saturation { get; init; } = 1f;    // 0 = grayscale
    public float Exposure { get; init; }            // stops (2^x brightness)
    public float Blur { get; init; }                // gaussian sigma (px)
    public float Sharpen { get; init; }             // [0,1]
    public float Vignette { get; init; }            // [0,1]
    public float Grain { get; init; }               // [0,1]
    public SKColor ToneColor { get; init; } = SKColors.Transparent;
    public float ToneStrength { get; init; }        // [0,1]
}
