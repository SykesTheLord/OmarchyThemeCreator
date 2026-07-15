using System.Collections.Generic;
using System.Linq;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using Xunit;

namespace OmarchyThemeCreator.Tests.Unit;

/// <summary>
/// Guards the hard-coded built-in presets against typos: every colour must be a canonical
/// <c>#rrggbb</c>, names must be present and unique. (Base16 <c>.yaml</c> import is covered by an
/// integration test because it reads a file.)
/// </summary>
public sealed class PresetServiceTests
{
    private readonly PresetService _service = new();

    [Fact]
    public void BuiltIns_is_non_empty()
    {
        Assert.NotEmpty(_service.BuiltIns);
    }

    [Fact]
    public void BuiltIns_have_unique_non_blank_names()
    {
        List<string> names = _service.BuiltIns.Select(p => p.Name).ToList();
        Assert.All(names, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Fact]
    public void Every_builtin_color_is_a_canonical_hex()
    {
        foreach (NamedPalette preset in _service.BuiltIns)
        {
            foreach (KeyValuePair<string, string> pair in preset.Colors.AsPairs())
            {
                // A canonical value normalizes to itself; this catches uppercase, shorthand, or typos.
                Assert.True(ThemeColors.NormalizeHex(pair.Value) == pair.Value,
                    $"Preset '{preset.Name}' key '{pair.Key}' has non-canonical colour '{pair.Value}'.");
            }
        }
    }

    [Fact]
    public void Light_presets_are_flagged_and_readable()
    {
        // Sanity on the well-known light schemes: they exist and their fg/bg are legible.
        NamedPalette latte = _service.BuiltIns.Single(p => p.Name == "Catppuccin Latte");
        Assert.True(latte.LightMode);
        ColorAssert.ContrastAtLeast(latte.Colors.Foreground, latte.Colors.Background, 4.5);
    }
}
