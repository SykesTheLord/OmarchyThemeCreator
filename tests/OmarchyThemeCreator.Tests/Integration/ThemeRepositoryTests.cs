using System.IO;
using System.Linq;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.Tests.Support;
using SkiaSharp;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// Exercises the real filesystem via <see cref="TempHome"/> (redirected <c>$HOME</c>), so nothing
/// touches the user's real <c>~/.config/omarchy</c>. In the non-parallel "env" collection because
/// it mutates process-global environment variables.
/// </summary>
[Collection("env")]
public sealed class ThemeRepositoryTests
{
    [Fact]
    public void ListThemes_lets_a_user_theme_override_a_builtin_of_the_same_name()
    {
        using TempHome home = new();
        home.SeedTheme("tokyo-night", builtIn: true);
        home.SeedTheme("tokyo-night", builtIn: false);
        home.SeedTheme("nord", builtIn: true);

        ThemeRepository repo = new();
        var themes = repo.ListThemes();

        Theme tokyo = themes.Single(t => t.Name == "tokyo-night");
        Assert.False(tokyo.IsBuiltIn); // user copy wins and is editable
        Theme nord = themes.Single(t => t.Name == "nord");
        Assert.True(nord.IsBuiltIn);
    }

    [Fact]
    public void ListThemes_is_empty_when_no_themes_exist()
    {
        using TempHome home = new();
        Assert.Empty(new ThemeRepository().ListThemes());
    }

    [Fact]
    public void Save_then_Load_round_trips_colors_light_mode_and_icons()
    {
        using TempHome home = new();
        ThemeRepository repo = new();

        Theme theme = new Theme
        {
            Name = "My Theme",
            LightMode = true,
            IconTheme = "Yaru-magenta",
        };
        theme.Colors.Accent = "#123456";

        string dir = repo.Save(theme);
        Theme loaded = repo.Load(dir);

        Assert.Equal("my-theme", loaded.Name); // name is slugified on save
        Assert.Equal("#123456", loaded.Colors.Accent);
        Assert.True(loaded.LightMode);
        Assert.Equal("Yaru-magenta", loaded.IconTheme);
    }

    [Fact]
    public void Save_writes_only_into_the_user_dir_never_the_system_dir()
    {
        using TempHome home = new();
        ThemeRepository repo = new();

        repo.Save(new Theme { Name = "brand-new" });

        Assert.True(Directory.Exists(Path.Combine(home.UserThemesDir, "brand-new")));
        // The read-only system dir must remain empty.
        Assert.Empty(Directory.EnumerateFileSystemEntries(home.SystemThemesDir));
    }

    [Fact]
    public void Save_with_a_new_name_moves_the_directory_and_removes_the_old_one()
    {
        using TempHome home = new();
        ThemeRepository repo = new();

        Theme theme = new Theme { Name = "old-name" };
        string oldDir = repo.Save(theme);

        theme.Name = "new-name";
        string newDir = repo.Save(theme);

        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(newDir));
        Assert.EndsWith("new-name", newDir);
    }

    [Fact]
    public void Clone_copies_a_builtin_into_an_editable_user_theme()
    {
        using TempHome home = new();
        ThemeColors builtinColors = new ThemeColors { Accent = "#abcdef" };
        home.SeedTheme("stock", builtinColors, builtIn: true);

        ThemeRepository repo = new();
        Theme source = repo.Load(Path.Combine(home.SystemThemesDir, "stock"));
        Theme clone = repo.Clone(source, "My Copy");

        Assert.False(clone.IsBuiltIn);
        Assert.Equal("#abcdef", clone.Colors.Accent);
        Assert.True(Directory.Exists(Path.Combine(home.UserThemesDir, "my-copy")));
    }

    [Fact]
    public void Save_strips_stale_colour_derived_configs_but_keeps_structural_overrides()
    {
        using TempHome home = new();
        string builtinDir = home.SeedTheme("stock", new ThemeColors { Accent = "#abcdef" }, builtIn: true);
        // A built-in ships hand-authored, colour-derived configs (which Omarchy would otherwise
        // regenerate from colors.toml) plus a structural override that carries more than colour.
        File.WriteAllText(Path.Combine(builtinDir, "btop.theme"), "theme[main_bg]=\"#000000\"\n");
        File.WriteAllText(Path.Combine(builtinDir, "alacritty.toml"), "# stale terminal colours\n");
        File.WriteAllText(Path.Combine(builtinDir, "waybar.css"), "/* custom layout, not just colour */\n");

        ThemeRepository repo = new();
        Theme source = repo.Load(builtinDir);
        Theme clone = repo.Clone(source, "My Copy"); // Clone copies btop.theme, alacritty.toml, … along

        clone.Colors.Accent = "#112233"; // user edits the palette
        string dir = repo.Save(clone);

        // Colour-derived files are removed so omarchy-theme-set re-templates them from the new palette;
        // leaving them would make btop/alacritty keep the original theme's colours.
        Assert.False(File.Exists(Path.Combine(dir, "btop.theme")));
        Assert.False(File.Exists(Path.Combine(dir, "alacritty.toml")));
        // A structural override must be preserved, not silently discarded.
        Assert.True(File.Exists(Path.Combine(dir, "waybar.css")));
        // And the edited palette is what landed on disk.
        Assert.Equal("#112233", repo.Load(dir).Colors.Accent);
    }

    [Fact]
    public void AddBackground_reports_new_then_duplicate()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "bg-theme" };
        repo.Save(theme); // must be saved first (needs a Path)

        string src = MakeSourceImage(home.Root, "pic.png");

        BackgroundAddResult first = repo.AddBackground(theme, src);
        BackgroundAddResult second = repo.AddBackground(theme, src);

        Assert.True(first.WasNew);
        Assert.False(second.WasNew); // same filename → overwrites, not newly tracked
        Assert.Single(theme.Backgrounds);
        Assert.True(File.Exists(first.Path));
    }

    [Fact]
    public void RenameBackground_applies_the_switcher_naming_convention()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "bg-theme" };
        repo.Save(theme);
        string src = MakeSourceImage(home.Root, "raw.png");
        BackgroundAddResult added = repo.AddBackground(theme, src);

        string renamed = repo.RenameBackground(theme, added.Path, "Sunset Ridge");

        // NN-<slug>.<ext>
        Assert.Matches(@"\d{2}-sunset-ridge\.png$", Path.GetFileName(renamed));
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(added.Path));
    }

    [Fact]
    public void ReorderBackgrounds_renumbers_without_collisions()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "bg-theme" };
        repo.Save(theme);

        string a = repo.RenameBackground(theme, repo.AddBackground(theme, MakeSourceImage(home.Root, "a.png")).Path, "Alpha");
        string b = repo.RenameBackground(theme, repo.AddBackground(theme, MakeSourceImage(home.Root, "b.png")).Path, "Bravo");

        var reordered = repo.ReorderBackgrounds(theme, new[] { b, a });

        Assert.Equal(2, reordered.Count);
        // All target files exist and prefixes are sequential/unique.
        Assert.All(reordered, p => Assert.True(File.Exists(p)));
        Assert.Equal(reordered.Count, reordered.Select(Path.GetFileName).Distinct().Count());
    }

    [Fact]
    public void RemoveBackground_deletes_the_file_and_untracks_it()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "bg-theme" };
        repo.Save(theme);
        BackgroundAddResult added = repo.AddBackground(theme, MakeSourceImage(home.Root, "gone.png"));

        repo.RemoveBackground(theme, added.Path);

        Assert.False(File.Exists(added.Path));
        Assert.DoesNotContain(added.Path, theme.Backgrounds);
    }

    [Fact]
    public void SetPreviewImage_writes_a_decodable_png()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "preview-theme" };
        repo.Save(theme);
        string src = MakeSourceImage(home.Root, "source.png");

        string preview = repo.SetPreviewImage(theme, src);

        Assert.True(File.Exists(preview));
        using SKBitmap? decoded = SKBitmap.Decode(preview);
        Assert.NotNull(decoded);
    }

    [Fact]
    public void Delete_removes_the_user_theme_directory()
    {
        using TempHome home = new();
        ThemeRepository repo = new();
        Theme theme = new Theme { Name = "trash" };
        string dir = repo.Save(theme);

        repo.Delete(theme);

        Assert.False(Directory.Exists(dir));
    }

    private static string MakeSourceImage(string root, string name)
    {
        string dir = Path.Combine(root, "sources");
        using SKBitmap bmp = ImageFixtures.SolidBitmap(8, 8, new SKColor(10, 120, 200));
        return ImageFixtures.WritePng(bmp, dir, name);
    }
}
