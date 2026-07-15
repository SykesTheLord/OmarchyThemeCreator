using System.Collections.Generic;
using System.Linq;
using OmarchyThemeCreator.Models;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

public sealed class ThemeColorsTests
{
    [Theory]
    [InlineData("#ff8800", "#ff8800")]
    [InlineData("ff8800", "#ff8800")]     // missing '#' is prepended
    [InlineData("#FF8800", "#ff8800")]    // canonicalised to lowercase
    [InlineData("  #FfAa00 ", "#ffaa00")] // trimmed + lowercased
    public void NormalizeHex_canonicalizes_valid_input(string input, string expected)
    {
        Assert.Equal(expected, ThemeColors.NormalizeHex(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#fff")]        // 3-digit shorthand is NOT supported (must be 6 hex digits)
    [InlineData("#12345")]      // too short
    [InlineData("#1234567")]    // too long
    [InlineData("#gggggg")]     // non-hex digits
    [InlineData("nonsense")]
    public void NormalizeHex_rejects_invalid_input(string? input)
    {
        Assert.Null(ThemeColors.NormalizeHex(input));
    }

    [Fact]
    public void ToRgb_converts_hex_to_decimal_triplet()
    {
        Assert.Equal("255,136,0", ThemeColors.ToRgb("#ff8800"));
        Assert.Equal("0,0,0", ThemeColors.ToRgb("#000000"));
    }

    [Theory]
    [InlineData("accent")]
    [InlineData("cursor")]
    [InlineData("foreground")]
    [InlineData("background")]
    [InlineData("selection_foreground")]
    [InlineData("selection_background")]
    public void Set_named_keys_round_trip_through_AsPairs(string key)
    {
        ThemeColors colors = new ThemeColors();
        colors.Set(key, "#abcdef");
        KeyValuePair<string, string> pair = colors.AsPairs().Single(p => p.Key == key);
        Assert.Equal("#abcdef", pair.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(15)]
    public void Set_ansi_color_keys(int index)
    {
        ThemeColors colors = new ThemeColors();
        colors.Set($"color{index}", "#010203");
        Assert.Equal("#010203", colors.Ansi[index]);
    }

    [Theory]
    [InlineData("color16")]     // out of range (only 0..15)
    [InlineData("colorx")]      // not a number
    [InlineData("totally-unknown")]
    public void Set_unknown_key_is_ignored(string key)
    {
        ThemeColors colors = new ThemeColors();
        ThemeColors before = colors.Clone();
        colors.Set(key, "#010203");
        // Nothing changed.
        Assert.Equal(before.AsPairs(), colors.AsPairs());
    }

    [Fact]
    public void AsPairs_yields_six_named_plus_sixteen_ansi_keys()
    {
        List<KeyValuePair<string, string>> pairs = new ThemeColors().AsPairs().ToList();
        Assert.Equal(22, pairs.Count);
        Assert.Contains(pairs, p => p.Key == "color0");
        Assert.Contains(pairs, p => p.Key == "color15");
    }

    [Fact]
    public void Clone_is_a_deep_copy()
    {
        ThemeColors original = new ThemeColors();
        ThemeColors clone = original.Clone();

        clone.Accent = "#111111";
        clone.Ansi[0] = "#222222";

        Assert.NotEqual(original.Accent, clone.Accent);
        Assert.NotEqual(original.Ansi[0], clone.Ansi[0]);
    }
}
