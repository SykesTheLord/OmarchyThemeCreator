using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Tests.Support;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// <see cref="ColorMath"/> is the pure-maths bedrock shared by extraction, the tuning sliders,
/// presets, and the contrast checker — so it gets the most thorough coverage.
/// </summary>
public sealed class ColorMathTests
{
    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#ff8800", 255, 136, 0)]
    [InlineData("#7aa2f7", 122, 162, 247)]
    public void ToRgb_parses_hex(string hex, int r, int g, int b)
    {
        ColorMath.Rgb rgb = ColorMath.ToRgb(hex);
        Assert.Equal((r, g, b), (rgb.R, rgb.G, rgb.B));
    }

    [Theory]
    [InlineData("ff8800")]     // no leading '#'
    [InlineData("#FF8800")]    // uppercase
    public void ToRgb_normalizes_input_forms(string hex)
    {
        ColorMath.Rgb rgb = ColorMath.ToRgb(hex);
        Assert.Equal((255, 136, 0), (rgb.R, rgb.G, rgb.B));
    }

    [Fact]
    public void ToRgb_of_invalid_hex_is_black()
    {
        // NormalizeHex returns null for junk; ToRgb falls back to #000000.
        ColorMath.Rgb rgb = ColorMath.ToRgb("not-a-color");
        Assert.Equal((0, 0, 0), (rgb.R, rgb.G, rgb.B));
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#ff8800")]
    [InlineData("#123456")]
    [InlineData("#7aa2f7")]
    public void ToHex_ToRgb_round_trip(string hex)
    {
        Assert.Equal(hex, ColorMath.ToHex(ColorMath.ToRgb(hex)));
    }

    [Theory]
    [InlineData("#ff0000")]
    [InlineData("#00ff00")]
    [InlineData("#0000ff")]
    [InlineData("#808080")]
    [InlineData("#123456")]
    [InlineData("#abcdef")]
    public void RgbToHsl_HslToRgb_round_trip_within_tolerance(string hex)
    {
        ColorMath.Rgb original = ColorMath.ToRgb(hex);
        ColorMath.Rgb back = ColorMath.HslToRgb(ColorMath.RgbToHsl(original));
        // HSL↔RGB is lossy through rounding; allow one unit per channel.
        ColorAssert.AreClose(hex, ColorMath.ToHex(back), tol: 1);
    }

    [Fact]
    public void RgbToHsl_pure_red_is_hue_zero_full_saturation_mid_lightness()
    {
        ColorMath.Hsl hsl = ColorMath.RgbToHsl(new ColorMath.Rgb(255, 0, 0));
        Assert.Equal(0, hsl.H, 3);
        Assert.Equal(1.0, hsl.S, 3);
        Assert.Equal(0.5, hsl.L, 3);
    }

    [Fact]
    public void RgbToHsl_gray_has_zero_saturation()
    {
        ColorMath.Hsl hsl = ColorMath.RgbToHsl(new ColorMath.Rgb(128, 128, 128));
        Assert.Equal(0.0, hsl.S, 3);
        Assert.Equal(0.5, hsl.L, 2);
    }

    [Fact]
    public void RelativeLuminance_black_is_zero_white_is_one()
    {
        Assert.Equal(0.0, ColorMath.RelativeLuminance("#000000"), 5);
        Assert.Equal(1.0, ColorMath.RelativeLuminance("#ffffff"), 5);
    }

    [Fact]
    public void ContrastRatio_black_on_white_is_21()
    {
        Assert.Equal(21.0, ColorMath.ContrastRatio("#000000", "#ffffff"), 2);
    }

    [Fact]
    public void ContrastRatio_is_symmetric()
    {
        double ab = ColorMath.ContrastRatio("#123456", "#abcdef");
        double ba = ColorMath.ContrastRatio("#abcdef", "#123456");
        Assert.Equal(ab, ba, 6);
    }

    [Theory]
    [InlineData(21.0, "AAA")]
    [InlineData(7.0, "AAA")]
    [InlineData(6.99, "AA")]
    [InlineData(4.5, "AA")]
    [InlineData(4.49, "AA Large")]
    [InlineData(3.0, "AA Large")]
    [InlineData(2.99, "Fail")]
    [InlineData(1.0, "Fail")]
    public void Grade_boundaries(double ratio, string expected)
    {
        Assert.Equal(expected, ColorMath.Grade(ratio));
    }

    [Fact]
    public void ReadableTextOn_picks_black_for_light_and_white_for_dark()
    {
        Assert.Equal("#000000", ColorMath.ReadableTextOn("#ffffff"));
        Assert.Equal("#ffffff", ColorMath.ReadableTextOn("#000000"));
    }

    [Theory]
    [InlineData("#777777", "#808080", 4.5)]  // low-contrast pair on a mid-gray-ish bg
    [InlineData("#555555", "#333333", 4.5)]
    [InlineData("#3366aa", "#101820", 3.0)]
    public void EnsureContrast_result_meets_minimum(string fg, string bg, double min)
    {
        string adjusted = ColorMath.EnsureContrast(fg, bg, min);
        // On a near-black/near-white background the target is always reachable by moving lightness.
        ColorAssert.ContrastAtLeast(adjusted, bg, min);
    }

    [Fact]
    public void EnsureContrast_leaves_passing_colors_untouched()
    {
        // Already 21:1 — must be returned verbatim.
        Assert.Equal("#ffffff", ColorMath.EnsureContrast("#ffffff", "#000000", 4.5));
    }

    [Fact]
    public void Mix_endpoints_and_midpoint()
    {
        ColorMath.Rgb a = new ColorMath.Rgb(0, 0, 0);
        ColorMath.Rgb b = new ColorMath.Rgb(255, 255, 255);
        Assert.Equal((0, 0, 0), Tuple(ColorMath.Mix(a, b, 0)));
        Assert.Equal((255, 255, 255), Tuple(ColorMath.Mix(a, b, 1)));
        ColorMath.Rgb mid = ColorMath.Mix(a, b, 0.5);
        Assert.Equal((128, 128, 128), Tuple(mid));
    }

    [Fact]
    public void MixHex_blends_between_two_colors()
    {
        Assert.Equal("#000000", ColorMath.MixHex("#000000", "#ffffff", 0));
        Assert.Equal("#ffffff", ColorMath.MixHex("#000000", "#ffffff", 1));
    }

    [Theory]
    [InlineData(10, 20, 10)]
    [InlineData(350, 10, 20)]   // wraparound across 0°
    [InlineData(0, 180, 180)]   // antipodal is the maximum
    [InlineData(90, 90, 0)]
    public void HueDistance_takes_the_shorter_arc(double a, double b, double expected)
    {
        Assert.Equal(expected, ColorMath.HueDistance(a, b), 3);
    }

    private static (int, int, int) Tuple(ColorMath.Rgb c) => (c.R, c.G, c.B);
}
