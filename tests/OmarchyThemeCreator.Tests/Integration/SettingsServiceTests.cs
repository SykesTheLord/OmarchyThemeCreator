using System.IO;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

[Collection("env")]
public sealed class SettingsServiceTests
{
    [Fact]
    public void Missing_file_yields_defaults()
    {
        using TempHome home = new();
        SettingsService settings = new();
        Assert.Null(settings.Current.WallhavenApiKey);
    }

    [Fact]
    public void Saved_settings_are_read_back_by_a_fresh_instance()
    {
        using TempHome home = new();

        SettingsService writer = new();
        writer.Current.WallhavenApiKey = "abc123";
        writer.Save();

        SettingsService reader = new();
        Assert.Equal("abc123", reader.Current.WallhavenApiKey);
    }

    [Fact]
    public void Malformed_json_degrades_to_defaults_without_throwing()
    {
        using TempHome home = new();
        string dir = Path.Combine(home.HomeDir, ".config", "omarchy-theme-creator");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "settings.json"), "{ this is not valid json ");

        // Constructor loads eagerly; must not throw.
        SettingsService settings = new();
        Assert.Null(settings.Current.WallhavenApiKey);
    }
}
