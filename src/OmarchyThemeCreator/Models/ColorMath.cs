using System;

namespace OmarchyThemeCreator.Models;

/// <summary>
/// Color-space math shared by palette extraction, the tuning sliders, presets, and the
/// WCAG contrast checker. All hex I/O uses the canonical <c>#rrggbb</c> form produced by
/// <see cref="ThemeColors.NormalizeHex"/>.
/// </summary>
public static class ColorMath
{
    public readonly record struct Rgb(int R, int G, int B);

    /// <summary>Hue in [0,360), saturation and lightness in [0,1].</summary>
    public readonly record struct Hsl(double H, double S, double L);

    public static Rgb ToRgb(string hex)
    {
        string s = (ThemeColors.NormalizeHex(hex) ?? "#000000").TrimStart('#');
        return new Rgb(
            Convert.ToInt32(s.Substring(0, 2), 16),
            Convert.ToInt32(s.Substring(2, 2), 16),
            Convert.ToInt32(s.Substring(4, 2), 16));
    }

    public static string ToHex(Rgb c) =>
        $"#{Clamp255(c.R):x2}{Clamp255(c.G):x2}{Clamp255(c.B):x2}";

    private static int Clamp255(int v) => v < 0 ? 0 : v > 255 ? 255 : v;
    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    public static Hsl RgbToHsl(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2.0;
        double h = 0, s = 0;
        double d = max - min;
        if (d > 1e-9)
        {
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60;
        }
        return new Hsl(h, s, l);
    }

    public static Rgb HslToRgb(Hsl hsl)
    {
        double h = ((hsl.H % 360) + 360) % 360 / 360.0;
        double s = Clamp01(hsl.S);
        double l = Clamp01(hsl.L);
        if (s <= 1e-9)
        {
            int v = (int)Math.Round(l * 255);
            return new Rgb(v, v, v);
        }
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
        double p = 2 * l - q;
        return new Rgb(
            (int)Math.Round(HueToChannel(p, q, h + 1.0 / 3) * 255),
            (int)Math.Round(HueToChannel(p, q, h) * 255),
            (int)Math.Round(HueToChannel(p, q, h - 1.0 / 3) * 255));
    }

    private static double HueToChannel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    public static Hsl ToHsl(string hex) => RgbToHsl(ToRgb(hex));
    public static string FromHsl(Hsl hsl) => ToHex(HslToRgb(hsl));

    // ---- CMYK (readout only) -------------------------------------------
    // HSV/HSB is not defined here on purpose: Avalonia's HsvColor / Color.ToHsv()
    // already covers it, so the color picker uses those instead of duplicating the math.

    /// <summary>Cyan/Magenta/Yellow/Black, each in [0,1].</summary>
    public readonly record struct Cmyk(double C, double M, double Y, double K);

    /// <summary>Naive (profile-less) sRGB → CMYK, used for the picker's informational readout.</summary>
    public static Cmyk RgbToCmyk(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double k = 1 - Math.Max(r, Math.Max(g, b));
        if (k >= 1 - 1e-9) return new Cmyk(0, 0, 0, 1); // pure black: avoid divide-by-zero
        double inv = 1 - k;
        return new Cmyk((1 - r - k) / inv, (1 - g - k) / inv, (1 - b - k) / inv, k);
    }

    // ---- WCAG contrast -------------------------------------------------

    /// <summary>WCAG relative luminance of a color.</summary>
    public static double RelativeLuminance(string hex)
    {
        Rgb c = ToRgb(hex);
        return 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);
    }

    private static double Linearize(int channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>WCAG contrast ratio in [1,21].</summary>
    public static double ContrastRatio(string a, string b)
    {
        double la = RelativeLuminance(a);
        double lb = RelativeLuminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Best WCAG grade a ratio satisfies for normal-size text ("AAA"/"AA"/"AA Large"/"Fail").</summary>
    public static string Grade(double ratio) => ratio switch
    {
        >= 7.0 => "AAA",
        >= 4.5 => "AA",
        >= 3.0 => "AA Large",
        _ => "Fail",
    };

    // ---- HSL adjustment (tuning sliders) -------------------------------

    /// <summary>Shift hue (degrees) and scale saturation/lightness around their current values.</summary>
    public static string AdjustHsl(string hex, double hueShift = 0, double satFactor = 1, double lightFactor = 1)
    {
        Hsl h = ToHsl(hex);
        return FromHsl(new Hsl(h.H + hueShift, Clamp01(h.S * satFactor), Clamp01(h.L * lightFactor)));
    }

    /// <summary>Push saturation up/down. <paramref name="amount"/> in [-1,1].</summary>
    public static Hsl WithVibrance(Hsl h, double amount) =>
        h with { S = Clamp01(h.S + amount * (1 - h.S)) };

    /// <summary>Push lightness away from (positive) or toward (negative) mid-gray. <paramref name="amount"/> in [-1,1].</summary>
    public static Hsl WithContrast(Hsl h, double amount) =>
        h with { L = Clamp01(0.5 + (h.L - 0.5) * (1 + amount)) };

    /// <summary>Warm (positive) / cool (negative) temperature shift. <paramref name="amount"/> in [-1,1].</summary>
    public static Rgb WithTemperature(Rgb c, double amount)
    {
        int shift = (int)Math.Round(amount * 30);
        return new Rgb(Clamp255(c.R + shift), c.G, Clamp255(c.B - shift));
    }

    // ---- Blending -----------------------------------------------------

    /// <summary>Linear blend between two colors. <paramref name="t"/>=0 → a, =1 → b.</summary>
    public static Rgb Mix(Rgb a, Rgb b, double t)
    {
        t = Clamp01(t);
        return new Rgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>Linear blend of two hex colors, returning canonical <c>#rrggbb</c>.</summary>
    public static string MixHex(string a, string b, double t) => ToHex(Mix(ToRgb(a), ToRgb(b), t));

    /// <summary>Black or white — whichever reads with more contrast on <paramref name="bg"/>.</summary>
    public static string ReadableTextOn(string bg) =>
        ContrastRatio("#000000", bg) >= ContrastRatio("#ffffff", bg) ? "#000000" : "#ffffff";

    // ---- Hue geometry / readability -----------------------------------

    /// <summary>Shortest angular distance between two hues (degrees), in [0,180].</summary>
    public static double HueDistance(double a, double b)
    {
        double d = Math.Abs((a - b) % 360);
        return d > 180 ? 360 - d : d;
    }

    /// <summary>
    /// Nudge <paramref name="fg"/>'s lightness away from <paramref name="bg"/> until their WCAG
    /// contrast reaches <paramref name="minRatio"/>, keeping hue and saturation. Returns the
    /// adjusted <c>#rrggbb</c> (unchanged if it already passes or no adjustment can satisfy it).
    /// </summary>
    public static string EnsureContrast(string fg, string bg, double minRatio)
    {
        if (ContrastRatio(fg, bg) >= minRatio) return fg;

        Hsl h = ToHsl(fg);
        // Move toward whichever end of the lightness axis is further from the background.
        double bgL = RgbToHsl(ToRgb(bg)).L;
        int dir = bgL < 0.5 ? 1 : -1;

        const double step = 0.02;
        string best = fg;
        double bestRatio = ContrastRatio(fg, bg);
        for (double l = h.L + dir * step; l >= 0 && l <= 1; l += dir * step)
        {
            string candidate = FromHsl(h with { L = l });
            double ratio = ContrastRatio(candidate, bg);
            if (ratio > bestRatio) { bestRatio = ratio; best = candidate; }
            if (ratio >= minRatio) return candidate;
        }
        // Couldn't fully satisfy (e.g. mid-gray background); return the best we found.
        return best;
    }
}
