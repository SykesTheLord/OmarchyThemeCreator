using System;
using System.IO;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;

namespace OmarchyThemeCreator.Tests.Support;

/// <summary>
/// Redirects <c>$HOME</c> and <c>$OMARCHY_PATH</c> to a throwaway temp directory for the lifetime
/// of the instance, then restores and deletes everything on <see cref="Dispose"/>.
///
/// This is the single seam that makes the filesystem-touching services testable without mocking:
/// both <see cref="ThemeRepository"/> and <see cref="SettingsService"/> derive their base
/// directory from <c>Environment.GetFolderPath(SpecialFolder.UserProfile)</c>, which on Linux
/// reads <c>$HOME</c>. Point <c>$HOME</c> at a temp dir and the real <c>~/.config</c> is never
/// touched.
///
/// Because env vars are process-global, every test class using this type must be in the
/// non-parallel <c>"env"</c> collection (see <see cref="EnvCollection"/>).
/// </summary>
public sealed class TempHome : IDisposable
{
    private readonly string? _originalHome;
    private readonly string? _originalOmarchyPath;

    public string Root { get; }

    /// <summary>The redirected <c>$HOME</c>.</summary>
    public string HomeDir { get; }

    /// <summary>Where <see cref="ThemeRepository"/> writes user themes: <c>$HOME/.config/omarchy/themes</c>.</summary>
    public string UserThemesDir { get; }

    /// <summary>The read-only built-in themes root: <c>$OMARCHY_PATH/themes</c>.</summary>
    public string SystemThemesDir { get; }

    public TempHome()
    {
        Root = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
        HomeDir = Path.Combine(Root, "home");
        string omarchyPath = Path.Combine(Root, "system");
        UserThemesDir = Path.Combine(HomeDir, ".config", "omarchy", "themes");
        SystemThemesDir = Path.Combine(omarchyPath, "themes");

        Directory.CreateDirectory(UserThemesDir);
        Directory.CreateDirectory(SystemThemesDir);

        _originalHome = Environment.GetEnvironmentVariable("HOME");
        _originalOmarchyPath = Environment.GetEnvironmentVariable("OMARCHY_PATH");
        Environment.SetEnvironmentVariable("HOME", HomeDir);
        Environment.SetEnvironmentVariable("OMARCHY_PATH", omarchyPath);
    }

    /// <summary>
    /// Write a minimal theme directory (<c>colors.toml</c> plus optional <c>light.mode</c> and
    /// backgrounds) under the user or system themes root, so repository tests have input to load.
    /// Returns the theme directory path.
    /// </summary>
    public string SeedTheme(
        string name,
        ThemeColors? colors = null,
        bool builtIn = false,
        bool lightMode = false,
        string? iconTheme = null)
    {
        string root = builtIn ? SystemThemesDir : UserThemesDir;
        string dir = Path.Combine(root, name);
        Directory.CreateDirectory(dir);
        ColorsTomlService.Save(Path.Combine(dir, "colors.toml"), colors ?? new ThemeColors());
        if (lightMode)
            File.WriteAllText(Path.Combine(dir, "light.mode"), string.Empty);
        if (iconTheme is not null)
            File.WriteAllText(Path.Combine(dir, "icons.theme"), iconTheme + "\n");
        return dir;
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", _originalHome);
        Environment.SetEnvironmentVariable("OMARCHY_PATH", _originalOmarchyPath);
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a leaked temp dir must never fail a test.
        }
    }
}
