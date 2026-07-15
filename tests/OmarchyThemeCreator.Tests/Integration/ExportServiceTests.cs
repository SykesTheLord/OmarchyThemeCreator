using System;
using System.IO;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// Exports a hand-built saved theme into a temp folder. Parallel-safe: builds the source theme
/// directory directly rather than through <see cref="ThemeRepository"/>, so no <c>$HOME</c>
/// redirection is needed.
/// </summary>
public sealed class ExportServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
    private readonly ExportService _service = new();

    public ExportServiceTests() => Directory.CreateDirectory(_dir);

    private Theme SavedTheme()
    {
        string themeDir = Path.Combine(_dir, "src-theme");
        Directory.CreateDirectory(themeDir);
        ColorsTomlService.Save(Path.Combine(themeDir, "colors.toml"), new ThemeColors());
        return new Theme { Name = "Cool Theme", Path = themeDir, LightMode = true };
    }

    [Fact]
    public void Export_copies_the_theme_and_writes_readme_and_license()
    {
        string parent = Path.Combine(_dir, "out");
        Directory.CreateDirectory(parent);

        ExportService.ExportResult result = _service.Export(SavedTheme(), parent);

        Assert.True(Directory.Exists(result.RepoPath));
        Assert.True(File.Exists(Path.Combine(result.RepoPath, "colors.toml")));

        string readme = File.ReadAllText(Path.Combine(result.RepoPath, "README.md"));
        Assert.Contains("Cool Theme", readme); // the display name

        string license = File.ReadAllText(Path.Combine(result.RepoPath, "LICENSE"));
        Assert.Contains(DateTime.Now.Year.ToString(), license);
    }

    [Fact]
    public void Export_names_the_repo_with_the_omarchy_convention()
    {
        string parent = Path.Combine(_dir, "out2");
        Directory.CreateDirectory(parent);

        ExportService.ExportResult result = _service.Export(SavedTheme(), parent);

        Assert.EndsWith("omarchy-cool-theme-theme", result.RepoPath);
    }

    [Fact]
    public void Export_git_init_matches_git_availability()
    {
        string parent = Path.Combine(_dir, "out3");
        Directory.CreateDirectory(parent);

        ExportService.ExportResult result = _service.Export(SavedTheme(), parent);

        if (GitOnPath())
        {
            Assert.True(result.GitInitialized);
            Assert.True(Directory.Exists(Path.Combine(result.RepoPath, ".git")));
        }
        else
        {
            // Export must still succeed without git.
            Assert.False(result.GitInitialized);
            Assert.True(Directory.Exists(result.RepoPath));
        }
    }

    [Fact]
    public void Export_throws_when_the_theme_is_unsaved()
    {
        Theme unsaved = new Theme { Name = "no-path", Path = null };
        Assert.Throws<InvalidOperationException>(() => _service.Export(unsaved, _dir));
    }

    private static bool GitOnPath()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null) return false;
        foreach (string dir in path.Split(Path.PathSeparator))
        {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "git")))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
