using System;
using OmarchyThemeCreator.Models;
using Xunit;

namespace OmarchyThemeCreator.Tests.Support;

/// <summary>
/// Approximate colour assertions. Median-cut quantization, HSL round-trips and colour-matrix
/// maths are never bit-exact, so tests assert on ranges and relationships rather than exact hex.
/// </summary>
public static class ColorAssert
{
    /// <summary>Assert two hex colours are within <paramref name="tol"/> per channel (0..255).</summary>
    public static void AreClose(string expectedHex, string actualHex, int tol = 2)
    {
        ColorMath.Rgb a = ColorMath.ToRgb(expectedHex);
        ColorMath.Rgb b = ColorMath.ToRgb(actualHex);
        bool close = Math.Abs(a.R - b.R) <= tol
                     && Math.Abs(a.G - b.G) <= tol
                     && Math.Abs(a.B - b.B) <= tol;
        Assert.True(close, $"Expected {expectedHex} ≈ {actualHex} (±{tol}/channel).");
    }

    /// <summary>Assert a string is a canonical <c>#rrggbb</c> lowercase hex colour.</summary>
    public static void IsValidHex(string hex)
    {
        Assert.NotNull(hex);
        string? normalized = ThemeColors.NormalizeHex(hex);
        Assert.True(normalized == hex, $"'{hex}' is not a canonical #rrggbb colour.");
    }

    /// <summary>Assert the WCAG contrast between two colours meets a minimum ratio.</summary>
    public static void ContrastAtLeast(string fg, string bg, double minRatio)
    {
        double ratio = ColorMath.ContrastRatio(fg, bg);
        Assert.True(ratio >= minRatio,
            $"Contrast {ratio:0.00} of {fg} on {bg} is below required {minRatio:0.00}.");
    }
}
