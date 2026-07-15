using OmarchyThemeCreator.Models;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

public sealed class ThemeTests
{
    [Theory]
    [InlineData("tokyo-night", "Tokyo Night")]
    [InlineData("nord", "Nord")]
    [InlineData("rose-pine-moon", "Rose Pine Moon")]
    [InlineData("catppuccin-mocha", "Catppuccin Mocha")]
    public void ToDisplayName_titlecases_kebab(string name, string expected)
    {
        Assert.Equal(expected, Theme.ToDisplayName(name));
    }

    [Fact]
    public void ToDisplayName_drops_empty_segments()
    {
        Assert.Equal("Tokyo Night", Theme.ToDisplayName("tokyo--night"));
    }

    [Theory]
    [InlineData("Tokyo Night", "tokyo-night")]
    [InlineData("Nord", "nord")]
    [InlineData("  Catppuccin   Mocha  ", "catppuccin-mocha")] // collapse whitespace, trim
    [InlineData("Solarized (Dark)", "solarized-dark")]          // punctuation collapses to one dash, trailing trimmed
    public void ToSlug_kebabizes_display_names(string display, string expected)
    {
        Assert.Equal(expected, Theme.ToSlug(display));
    }

    [Theory]
    [InlineData("tokyo-night")]
    [InlineData("nord")]
    [InlineData("gruvbox-dark")]
    public void ToSlug_round_trips_with_ToDisplayName(string slug)
    {
        Assert.Equal(slug, Theme.ToSlug(Theme.ToDisplayName(slug)));
    }

    [Fact]
    public void DisplayName_property_derives_from_name()
    {
        Theme theme = new Theme { Name = "one-dark" };
        Assert.Equal("One Dark", theme.DisplayName);
    }
}
