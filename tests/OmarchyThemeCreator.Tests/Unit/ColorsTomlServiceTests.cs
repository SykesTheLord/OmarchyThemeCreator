using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// <see cref="ColorsTomlService.Parse"/> and <see cref="ColorsTomlService.Serialize"/> are pure
/// string transforms — the filesystem <c>Load</c>/<c>Save</c> wrappers are covered by the
/// integration round-trip test.
/// </summary>
public sealed class ColorsTomlServiceTests
{
    [Fact]
    public void Parse_reads_double_quoted_values()
    {
        ThemeColors c = ColorsTomlService.Parse("accent = \"#7aa2f7\"");
        Assert.Equal("#7aa2f7", c.Accent);
    }

    [Fact]
    public void Parse_reads_single_quoted_values()
    {
        ThemeColors c = ColorsTomlService.Parse("accent = '#7aa2f7'");
        Assert.Equal("#7aa2f7", c.Accent);
    }

    [Fact]
    public void Parse_ignores_inline_comment_after_quoted_value()
    {
        ThemeColors c = ColorsTomlService.Parse("accent = \"#7aa2f7\"  # primary");
        Assert.Equal("#7aa2f7", c.Accent);
    }

    [Fact]
    public void Parse_skips_blank_and_comment_lines()
    {
        string toml = """
            # a heading comment
            accent = "#111111"

            background = "#222222"
            """;
        ThemeColors c = ColorsTomlService.Parse(toml);
        Assert.Equal("#111111", c.Accent);
        Assert.Equal("#222222", c.Background);
    }

    [Fact]
    public void Parse_tolerates_surrounding_whitespace()
    {
        ThemeColors c = ColorsTomlService.Parse("   foreground   =   \"#abcdef\"   ");
        Assert.Equal("#abcdef", c.Foreground);
    }

    [Fact]
    public void Parse_maps_color0_through_color15_into_ansi()
    {
        string toml = """
            color0 = "#000010"
            color7 = "#000070"
            color15 = "#0000f0"
            """;
        ThemeColors c = ColorsTomlService.Parse(toml);
        Assert.Equal("#000010", c.Ansi[0]);
        Assert.Equal("#000070", c.Ansi[7]);
        Assert.Equal("#0000f0", c.Ansi[15]);
    }

    [Fact]
    public void Serialize_emits_named_block_then_blank_line_then_ansi()
    {
        string toml = ColorsTomlService.Serialize(new ThemeColors());
        Assert.StartsWith("accent = \"", toml);
        Assert.Contains("selection_background = \"", toml);
        Assert.Contains("\n\ncolor0 = \"", toml); // blank line separates named from ansi
        Assert.Contains("color15 = \"", toml);
    }

    [Fact]
    public void Parse_of_Serialize_round_trips_a_full_palette()
    {
        ThemeColors original = new ThemeColors
        {
            Accent = "#010203",
            Cursor = "#040506",
            Foreground = "#070809",
            Background = "#0a0b0c",
            SelectionForeground = "#0d0e0f",
            SelectionBackground = "#101112",
        };
        for (int i = 0; i < 16; i++)
            original.Ansi[i] = $"#{i:x2}{i:x2}{i:x2}";

        ThemeColors round = ColorsTomlService.Parse(ColorsTomlService.Serialize(original));

        Assert.Equal(original.AsPairs(), round.AsPairs());
    }
}
