using System;
using System.Collections.Generic;
using System.Linq;
using OmarchyThemeCreator.Models;
using SkiaSharp;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Generates a full 22-key Omarchy palette from a wallpaper. Decodes the image with
/// SkiaSharp, quantizes it with a median-cut algorithm, then maps the dominant colors onto
/// <see cref="ThemeColors"/>. A post-pass applies the chosen <see cref="ExtractionMode"/> and
/// the fine-tuning sliders. Pure function of (image, options) so it can be re-run cheaply as
/// the user drags sliders. Mirrors the core idea of Aether's extractor.
/// </summary>
public sealed class PaletteExtractionService
{
    // Quantization depends only on the image, not the options, so the Extract tab's per-slider
    // re-runs can reuse the swatches from the last image instead of re-decoding and re-quantizing
    // on every drag. Single-entry cache: the user works one wallpaper at a time, and this service
    // is only ever touched from the UI thread (see ExtractViewModel), so no locking is needed.
    private string? _cachedPath;
    private List<Swatch>? _cachedSwatches;

    public ThemeColors Extract(string imagePath, ExtractionOptions opts)
    {
        List<Swatch> swatches;
        if (imagePath == _cachedPath && _cachedSwatches is not null)
        {
            swatches = _cachedSwatches;
        }
        else
        {
            swatches = QuantizeImage(imagePath, 32);
            _cachedPath = imagePath;
            _cachedSwatches = swatches;
        }

        if (swatches.Count == 0)
            return new ThemeColors();

        return BuildPalette(swatches, opts);
    }

    // ---- Median-cut quantization --------------------------------------

    /// <summary>A quantized color together with how many source pixels it represents.</summary>
    private readonly record struct Swatch(ColorMath.Rgb Color, int Weight);

    private static List<Swatch> QuantizeImage(string imagePath, int targetColors)
    {
        using SKBitmap? original = SKBitmap.Decode(imagePath);
        if (original is null) return new();

        // Downscale to keep the pixel scan cheap regardless of source resolution.
        const int target = 200;
        int max = Math.Max(original.Width, original.Height);
        double scale = max > target ? (double)target / max : 1.0;
        SKImageInfo info = new SKImageInfo(
            Math.Max(1, (int)(original.Width * scale)),
            Math.Max(1, (int)(original.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Unpremul);

        SKBitmap? small = original.Resize(info, SKFilterQuality.Medium);
        SKBitmap src = small ?? original;

        // Read the raw pixel bytes in one shot: SKBitmap.GetPixel allocates and marshals an SKColor
        // on every call, which dominates a tight per-pixel scan. GetPixelSpan hands back the backing
        // buffer so we can index it directly. The resized bitmap is always Rgba8888; guard the byte
        // order anyway since the (rare) resize-failure fallback keeps the source's native layout.
        List<ColorMath.Rgb> pixels = new List<ColorMath.Rgb>(src.Width * src.Height);
        ReadOnlySpan<byte> bytes = src.GetPixelSpan();
        if (src.BytesPerPixel == 4 &&
            (src.ColorType == SKColorType.Rgba8888 || src.ColorType == SKColorType.Bgra8888))
        {
            int ri = src.ColorType == SKColorType.Bgra8888 ? 2 : 0;
            int bi = src.ColorType == SKColorType.Bgra8888 ? 0 : 2;
            int stride = src.RowBytes;
            for (int y = 0; y < src.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < src.Width; x++)
                {
                    int o = row + x * 4;
                    if (bytes[o + 3] < 8) continue;
                    pixels.Add(new ColorMath.Rgb(bytes[o + ri], bytes[o + 1], bytes[o + bi]));
                }
            }
        }
        else
        {
            for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                SKColor p = src.GetPixel(x, y);
                if (p.Alpha < 8) continue;
                pixels.Add(new ColorMath.Rgb(p.Red, p.Green, p.Blue));
            }
        }

        small?.Dispose();
        return MedianCut(pixels, targetColors);
    }

    private static List<Swatch> MedianCut(List<ColorMath.Rgb> pixels, int targetColors)
    {
        if (pixels.Count == 0) return new();

        List<List<ColorMath.Rgb>> boxes = new List<List<ColorMath.Rgb>> { pixels };
        while (boxes.Count < targetColors)
        {
            // Split the box with the largest range × population so dominant regions of the
            // image get resolved into more swatches than sparse, noisy ones.
            int splitIndex = -1;
            long bestPriority = -1;
            char widestChannel = 'r';
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                (int range, char channel) = WidestChannel(boxes[i]);
                long priority = (long)range * boxes[i].Count;
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    splitIndex = i;
                    widestChannel = channel;
                }
            }
            if (splitIndex < 0) break;

            List<ColorMath.Rgb> box = boxes[splitIndex];
            box.Sort((a, b) => Channel(a, widestChannel).CompareTo(Channel(b, widestChannel)));
            int mid = box.Count / 2;
            boxes[splitIndex] = box.GetRange(0, mid);
            boxes.Add(box.GetRange(mid, box.Count - mid));
        }

        return boxes.Where(b => b.Count > 0).Select(b => new Swatch(Average(b), b.Count)).ToList();
    }

    private static (int range, char channel) WidestChannel(List<ColorMath.Rgb> box)
    {
        int rMin = 255, gMin = 255, bMin = 255, rMax = 0, gMax = 0, bMax = 0;
        foreach (ColorMath.Rgb c in box)
        {
            rMin = Math.Min(rMin, c.R); rMax = Math.Max(rMax, c.R);
            gMin = Math.Min(gMin, c.G); gMax = Math.Max(gMax, c.G);
            bMin = Math.Min(bMin, c.B); bMax = Math.Max(bMax, c.B);
        }
        int rr = rMax - rMin, gr = gMax - gMin, br = bMax - bMin;
        if (rr >= gr && rr >= br) return (rr, 'r');
        return gr >= br ? (gr, 'g') : (br, 'b');
    }

    private static int Channel(ColorMath.Rgb c, char ch) => ch switch { 'r' => c.R, 'g' => c.G, _ => c.B };

    private static ColorMath.Rgb Average(List<ColorMath.Rgb> box)
    {
        long r = 0, g = 0, b = 0;
        foreach (ColorMath.Rgb c in box) { r += c.R; g += c.G; b += c.B; }
        int n = box.Count;
        return new ColorMath.Rgb((int)(r / n), (int)(g / n), (int)(b / n));
    }

    // ---- Palette assembly ---------------------------------------------

    /// <summary>Canonical terminal hues (degrees) for ANSI color1..6: red, green, yellow, blue, magenta, cyan.</summary>
    private static readonly double[] AnsiHues = { 0, 120, 55, 225, 300, 185 };

    private static ThemeColors BuildPalette(List<Swatch> swatches, ExtractionOptions opts)
    {
        // Pair every swatch with its HSL, keeping the population weight for dominance decisions.
        List<(ColorMath.Hsl Hsl, int Weight)> items = swatches
            .Select(s => (Hsl: ColorMath.RgbToHsl(s.Color), s.Weight))
            .ToList();

        // Anchors: the *dominant* dark and light colors (most pixels), not the extreme outliers
        // that a lone JPEG-black pixel or specular highlight would otherwise win.
        List<(ColorMath.Hsl Hsl, int Weight)> darkSide = items.Where(i => i.Hsl.L < 0.5).ToList();
        List<(ColorMath.Hsl Hsl, int Weight)> lightSide = items.Where(i => i.Hsl.L >= 0.5).ToList();
        if (darkSide.Count == 0) darkSide = items;
        if (lightSide.Count == 0) lightSide = items;

        ColorMath.Hsl darkDom = darkSide.OrderByDescending(i => i.Weight).First().Hsl;
        ColorMath.Hsl lightDom = lightSide.OrderByDescending(i => i.Weight).First().Hsl;

        ColorMath.Hsl bgSource = opts.LightMode ? lightDom : darkDom;
        ColorMath.Hsl fgSource = opts.LightMode ? darkDom : lightDom;
        ColorMath.Hsl bgHsl = ShapeBackground(bgSource, opts.LightMode);
        ColorMath.Hsl fgHsl = ShapeForeground(fgSource, opts.LightMode);

        // Accent: the most colorful *prominent* swatch (saturation, mid-lightness, and weight).
        ColorMath.Hsl accentHsl = items
            .OrderByDescending(i => i.Hsl.S * (1 - Math.Abs(i.Hsl.L - 0.5)) * Math.Log(i.Weight + 1))
            .First().Hsl;

        ThemeColors colors = new ThemeColors();

        colors.Background = FinalHex(bgHsl, opts, isAnchor: true);
        colors.Foreground = FinalHex(fgHsl, opts, isAnchor: true);
        colors.Accent = FinalHex(accentHsl, opts);
        colors.Cursor = colors.Foreground;
        colors.SelectionBackground = colors.Accent;
        colors.SelectionForeground = colors.Background;

        // ANSI black/white shades stay near-neutral and mode-independent (color0 = black).
        colors.Ansi[0] = FinalHex(NeutralShade(darkDom, 0.12), opts, isAnchor: true);
        colors.Ansi[8] = FinalHex(NeutralShade(darkDom, 0.28), opts, isAnchor: true);
        colors.Ansi[7] = FinalHex(NeutralShade(lightDom, 0.75), opts, isAnchor: true);
        colors.Ansi[15] = FinalHex(NeutralShade(lightDom, 0.9), opts, isAnchor: true);

        // color1..6 (normal) and color9..14 (bright), each pinned to its canonical terminal hue
        // so red is red and green is green regardless of the wallpaper's dominant color.
        List<(ColorMath.Hsl Hsl, int Weight)> chromatic = items.Where(i => i.Hsl.S > 0.12).ToList();
        for (int i = 0; i < 6; i++)
        {
            double target = AnsiHues[i];
            ColorMath.Hsl baseHsl;

            (ColorMath.Hsl Hsl, int Weight) match = chromatic
                .OrderBy(c => ColorMath.HueDistance(c.Hsl.H, target))
                .ThenByDescending(c => c.Weight)
                .FirstOrDefault();

            if (chromatic.Count > 0 && ColorMath.HueDistance(match.Hsl.H, target) <= 40)
            {
                // Pull the matched hue ~65% toward canonical; keep the image's saturation feel.
                double h = NudgeHue(match.Hsl.H, target, 0.65);
                baseHsl = new ColorMath.Hsl(h, Math.Max(0.4, match.Hsl.S), 0.6);
            }
            else
            {
                // No swatch near this hue (e.g. a monochrome wallpaper): synthesize one.
                baseHsl = new ColorMath.Hsl(target, Math.Max(0.5, accentHsl.S), 0.6);
            }

            colors.Ansi[1 + i] = FinalHex(baseHsl, opts);
            colors.Ansi[9 + i] = FinalHex(baseHsl with { S = Math.Max(0.5, baseHsl.S), L = 0.72 }, opts);
        }

        // Readability post-pass: runs last (on final hex) so it can't be undone by the sliders.
        EnforceReadability(colors);
        return colors;
    }

    /// <summary>Guarantee legible text/accents against the extracted background via WCAG contrast.</summary>
    private static void EnforceReadability(ThemeColors colors)
    {
        colors.Foreground = ColorMath.EnsureContrast(colors.Foreground, colors.Background, 4.5);
        colors.Cursor = colors.Foreground;
        colors.Accent = ColorMath.EnsureContrast(colors.Accent, colors.Background, 3.0);
        colors.SelectionBackground = colors.Accent;
        for (int i = 1; i <= 6; i++)
        {
            colors.Ansi[i] = ColorMath.EnsureContrast(colors.Ansi[i], colors.Background, 3.0);
            colors.Ansi[i + 8] = ColorMath.EnsureContrast(colors.Ansi[i + 8], colors.Background, 3.0);
        }
    }

    // Anchor shaping: keep the dominant hue but tame saturation and pin lightness to a cohesive band.
    private static ColorMath.Hsl ShapeBackground(ColorMath.Hsl h, bool lightMode) =>
        new(h.H, Math.Min(h.S, 0.22), lightMode ? 0.94 : 0.12);

    private static ColorMath.Hsl ShapeForeground(ColorMath.Hsl h, bool lightMode) =>
        new(h.H, Math.Min(h.S, 0.35), lightMode ? 0.20 : 0.86);

    private static ColorMath.Hsl NeutralShade(ColorMath.Hsl h, double l) =>
        new(h.H, Math.Min(h.S, 0.18), l);

    /// <summary>Interpolate a hue toward <paramref name="target"/> along the shorter arc. <paramref name="t"/> in [0,1].</summary>
    private static double NudgeHue(double from, double target, double t)
    {
        double delta = ((target - from + 540) % 360) - 180;
        return (((from + delta * t) % 360) + 360) % 360;
    }

    /// <summary>Apply the extraction mode + tuning sliders, then serialize to hex.</summary>
    private static string FinalHex(ColorMath.Hsl hsl, ExtractionOptions opts, bool isAnchor = false)
    {
        if (!isAnchor)
            hsl = ApplyMode(hsl, opts.Mode, opts.DominantHue);

        // Tuning sliders (skip hue/sat shaping on anchors to keep bg/fg neutral-ish).
        if (!isAnchor)
        {
            hsl = ColorMath.WithVibrance(hsl, opts.Vibrance);
            hsl = hsl with { S = Math.Clamp(hsl.S * opts.Saturation, 0, 1) };
        }
        hsl = ColorMath.WithContrast(hsl, opts.Contrast);
        hsl = hsl with { L = Math.Clamp(hsl.L * opts.Brightness, 0, 1) };
        hsl = ApplyShadowsHighlights(hsl, opts);

        ColorMath.Rgb rgb = ColorMath.HslToRgb(hsl);
        if (Math.Abs(opts.Temperature) > 1e-6)
            rgb = ColorMath.WithTemperature(rgb, opts.Temperature);
        return ColorMath.ToHex(rgb);
    }

    private static ColorMath.Hsl ApplyShadowsHighlights(ColorMath.Hsl h, ExtractionOptions opts)
    {
        if (h.L < 0.5 && Math.Abs(opts.Shadows) > 1e-6)
            h = h with { L = Math.Clamp(h.L + opts.Shadows * 0.2, 0, 1) };
        else if (h.L >= 0.5 && Math.Abs(opts.Highlights) > 1e-6)
            h = h with { L = Math.Clamp(h.L + opts.Highlights * 0.2, 0, 1) };
        return h;
    }

    private static ColorMath.Hsl ApplyMode(ColorMath.Hsl h, ExtractionMode mode, double dominantHue) => mode switch
    {
        ExtractionMode.Normal => h,
        ExtractionMode.Monochromatic => h with { H = dominantHue },
        ExtractionMode.Analogous => h with { H = dominantHue + Math.Clamp(DeltaHue(h.H, dominantHue), -30, 30) },
        ExtractionMode.Pastel => h with { S = Math.Min(h.S, 0.45), L = Math.Max(h.L, 0.75) },
        ExtractionMode.Muted => h with { S = h.S * 0.5 },
        ExtractionMode.Bright => h with { S = Math.Max(h.S, 0.7), L = Math.Clamp(h.L, 0.5, 0.65) },
        ExtractionMode.Colorful => h with { S = Math.Max(h.S, 0.85) },
        ExtractionMode.Material => h with { S = Math.Round(h.S * 4) / 4, L = Math.Round(h.L * 4) / 4 },
        _ => h,
    };

    private static double DeltaHue(double h, double reference)
    {
        double d = (h - reference) % 360;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }
}

public enum ExtractionMode
{
    Normal,
    Monochromatic,
    Analogous,
    Pastel,
    Material,
    Muted,
    Bright,
    Colorful,
}

/// <summary>
/// Extraction mode plus the fine-tuning sliders. Slider values are neutral at their defaults
/// (factors at 1.0, additive amounts at 0) so an untouched extraction is faithful to the image.
/// </summary>
public sealed record ExtractionOptions
{
    public ExtractionMode Mode { get; init; } = ExtractionMode.Normal;
    public bool LightMode { get; init; }

    /// <summary>Hue that Monochromatic/Analogous modes pivot around (degrees).</summary>
    public double DominantHue { get; init; }

    public double Vibrance { get; init; }       // [-1,1] additive toward full saturation
    public double Saturation { get; init; } = 1; // multiplicative
    public double Contrast { get; init; }        // [-1,1]
    public double Brightness { get; init; } = 1; // multiplicative
    public double Temperature { get; init; }     // [-1,1] cool..warm
    public double Shadows { get; init; }         // [-1,1] lift/deepen darks
    public double Highlights { get; init; }      // [-1,1] lift/deepen lights
}
