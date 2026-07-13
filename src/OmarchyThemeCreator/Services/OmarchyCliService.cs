using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Thin wrapper around the <c>omarchy-*</c> command-line tools. Degrades gracefully when
/// Omarchy is not installed so the app still works as a pure theme editor.
/// </summary>
public sealed class OmarchyCliService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<OmarchyCliService>();

    /// <summary>True if omarchy-theme-set is available on PATH.</summary>
    public bool IsAvailable { get; }

    public OmarchyCliService()
    {
        IsAvailable = FindOnPath("omarchy-theme-set") is not null;
        Log.Information("omarchy-theme-set {Availability} on PATH", IsAvailable ? "found" : "not found");
    }

    public record CliResult(bool Success, string Output);

    /// <summary>Run <c>omarchy-theme-set &lt;name&gt;</c> to apply a theme to the live desktop.</summary>
    public Task<CliResult> ApplyThemeAsync(string themeName)
        => RunAsync("omarchy-theme-set", themeName);

    private async Task<CliResult> RunAsync(string command, params string[] args)
    {
        string? exe = FindOnPath(command);
        if (exe is null)
        {
            Log.Warning("Cannot run {Command}: not found on PATH", command);
            return new CliResult(false, $"'{command}' not found on PATH.");
        }

        Log.Information("Running {Command} {Args}", command, args);
        try
        {
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (string a in args) psi.ArgumentList.Add(a);

            using Process proc = Process.Start(psi)!;
            string stdout = await proc.StandardOutput.ReadToEndAsync();
            string stderr = await proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();

            string output = (stdout + stderr).Trim();
            bool success = proc.ExitCode == 0;
            if (success)
                Log.Information("{Command} exited 0", command);
            else
                Log.Warning("{Command} exited {ExitCode}: {Output}", command, proc.ExitCode, output);
            return new CliResult(success, output);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to run {Command}", command);
            return new CliResult(false, ex.Message);
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
