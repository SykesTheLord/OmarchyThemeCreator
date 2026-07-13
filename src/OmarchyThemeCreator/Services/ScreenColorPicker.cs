using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Eyedropper backed by <c>hyprpicker</c>, the Wayland/Hyprland screen color picker. Prints the
/// picked color as hex to stdout; we parse it into an Avalonia <see cref="Color"/>. Follows the
/// same PATH-probe + <see cref="Process"/> pattern as <see cref="OmarchyCliService"/> and degrades
/// gracefully when hyprpicker isn't installed.
/// </summary>
public sealed class ScreenColorPicker : IScreenColorPicker
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ScreenColorPicker>();

    private readonly string? _exe;

    public ScreenColorPicker()
    {
        _exe = FindOnPath("hyprpicker");
        Log.Information("hyprpicker {Availability} on PATH", _exe is not null ? "found" : "not found");
    }

    public bool IsAvailable => _exe is not null;

    public async Task<Color?> PickAsync()
    {
        if (_exe is null) return null;
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = _exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-f"); // output format
            psi.ArgumentList.Add("hex");

            using Process proc = Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            // Non-zero exit means the user hit Escape / cancelled the pick.
            if (proc.ExitCode != 0) return null;

            string hex = stdout.Trim();
            string? norm = ThemeColors.NormalizeHex(hex);
            if (norm is null)
            {
                Log.Warning("hyprpicker returned unparseable output: {Output}", hex);
                return null;
            }
            return Color.Parse(norm);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "hyprpicker failed");
            return null;
        }
    }

    private static string? FindOnPath(string command)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        foreach (string dir in pathEnv.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string candidate = Path.Combine(dir, command);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
