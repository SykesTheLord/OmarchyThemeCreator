using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// Only the pure <see cref="WallhavenService.NearestColor"/> is unit-tested; the networked
/// search/download methods are deliberately out of scope (they'd need HTTP interception).
/// </summary>
public sealed class WallhavenServiceTests
{
    [Theory]
    [InlineData("660000")]
    [InlineData("ffffff")]
    [InlineData("000000")]
    public void NearestColor_returns_an_exact_palette_color_unchanged(string hex)
    {
        Assert.Equal(hex, WallhavenService.NearestColor(hex));
    }

    [Fact]
    public void NearestColor_accepts_a_leading_hash()
    {
        Assert.Equal("ffffff", WallhavenService.NearestColor("#ffffff"));
    }

    [Fact]
    public void NearestColor_snaps_an_off_color_to_the_closest_palette_entry()
    {
        // Pure red is closest to wallhaven's "cc0000".
        Assert.Equal("cc0000", WallhavenService.NearestColor("#ff0000"));
    }

    [Fact]
    public void NearestColor_returns_result_that_is_in_the_palette()
    {
        string result = WallhavenService.NearestColor("#123abc");
        Assert.Contains(result, WallhavenService.Colors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]        // wrong length
    [InlineData("#12")]
    public void NearestColor_falls_back_to_first_color_for_malformed_input(string hex)
    {
        Assert.Equal(WallhavenService.Colors[0], WallhavenService.NearestColor(hex));
    }
}
