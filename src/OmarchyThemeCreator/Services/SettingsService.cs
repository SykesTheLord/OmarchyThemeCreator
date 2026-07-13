using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>App-level preferences (e.g. the wallhaven API key) persisted as a small JSON file.</summary>
public sealed class AppSettings
{
    public string? WallhavenApiKey { get; set; }
}

/// <summary>
/// Loads/saves <see cref="AppSettings"/> to
/// <c>~/.config/omarchy-theme-creator/settings.json</c>. All operations degrade gracefully:
/// a missing or malformed file just yields defaults.
/// </summary>
public sealed class SettingsService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<SettingsService>();

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir = Path.Combine(home, ".config", "omarchy-theme-creator");
        _path = Path.Combine(dir, "settings.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
                Log.Debug("Loaded settings from {Path}", _path);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to read settings from {Path}; using defaults", _path);
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
            Log.Debug("Saved settings to {Path}", _path);
        }
        catch (Exception ex)
        {
            // Non-fatal: settings just won't persist this session.
            Log.Warning(ex, "Failed to persist settings to {Path}", _path);
        }
    }
}
