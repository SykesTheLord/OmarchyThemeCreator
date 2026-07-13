using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using OmarchyThemeCreator.Models;
using Serilog;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Scaffolds a distributable theme repository named <c>omarchy-&lt;name&gt;-theme</c>,
/// copying the theme's files and generating a README + MIT LICENSE. Initializes git if
/// available (no network push — the user pushes to GitHub themselves).
/// </summary>
public sealed class ExportService
{
    private static readonly ILogger Log = Serilog.Log.ForContext<ExportService>();

    public record ExportResult(string RepoPath, bool GitInitialized, string InstallCommand);

    public ExportResult Export(Theme theme, string targetParentDir)
    {
        if (theme.Path is null || !Directory.Exists(theme.Path))
            throw new InvalidOperationException("Save the theme before exporting.");

        string repoName = $"omarchy-{Theme.ToSlug(theme.Name)}-theme";
        string repoPath = Path.Combine(targetParentDir, repoName);
        if (Directory.Exists(repoPath))
            throw new IOException($"'{repoName}' already exists in the chosen folder.");

        CopyDirectory(theme.Path, repoPath);

        WriteIfAbsent(Path.Combine(repoPath, "README.md"), BuildReadme(theme, repoName));
        WriteIfAbsent(Path.Combine(repoPath, "LICENSE"), BuildMitLicense());

        bool gitOk = TryGitInit(repoPath);
        string installCmd = $"omarchy-theme-install https://github.com/<you>/{repoName}.git";
        Log.Information("Exported theme {Name} to {RepoPath} (git initialized: {GitOk})",
            theme.Name, repoPath, gitOk);
        return new ExportResult(repoPath, gitOk, installCmd);
    }

    private static string BuildReadme(Theme theme, string repoName)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("# ").Append(theme.DisplayName).Append(" — Omarchy theme\n\n");
        sb.Append("An [Omarchy](https://omarchy.org) theme.\n\n");
        sb.Append("## Install\n\n```bash\n");
        sb.Append("omarchy-theme-install https://github.com/<you>/").Append(repoName).Append(".git\n");
        sb.Append("```\n\n");
        sb.Append("Then select it from **Omarchy menu → Style → Theme**.\n\n");
        sb.Append("## Contents\n\n");
        sb.Append("- `colors.toml` — the color palette (required)\n");
        if (theme.LightMode) sb.Append("- `light.mode` — enables light mode\n");
        if (!string.IsNullOrWhiteSpace(theme.IconTheme))
            sb.Append("- `icons.theme` — `").Append(theme.IconTheme).Append("`\n");
        if (theme.Backgrounds.Count > 0)
            sb.Append("- `backgrounds/` — wallpapers\n");
        sb.Append("\nGenerated with [Omarchy Theme Creator](https://github.com/).\n");
        return sb.ToString();
    }

    private static string BuildMitLicense()
    {
        int year = DateTime.Now.Year;
        return $"MIT License\n\nCopyright (c) {year}\n\n" +
               "Permission is hereby granted, free of charge, to any person obtaining a copy " +
               "of this software and associated documentation files (the \"Software\"), to deal " +
               "in the Software without restriction...\n";
    }

    private static bool TryGitInit(string repoPath)
    {
        try
        {
            if (RunGit(repoPath, "init") != 0) return false;
            RunGit(repoPath, "add", "-A");
            RunGit(repoPath, "commit", "-m", "Initial theme commit");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "git init failed for {RepoPath}; exported without a repo", repoPath);
            return false;
        }
    }

    private static int RunGit(string cwd, params string[] args)
    {
        ProcessStartInfo psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string a in args) psi.ArgumentList.Add(a);
        using Process proc = Process.Start(psi)!;
        proc.WaitForExit();
        return proc.ExitCode;
    }

    private static void WriteIfAbsent(string path, string content)
    {
        if (!File.Exists(path)) File.WriteAllText(path, content);
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (string file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
        foreach (string sub in Directory.EnumerateDirectories(source))
        {
            if (Path.GetFileName(sub) == ".git") continue;
            CopyDirectory(sub, Path.Combine(dest, Path.GetFileName(sub)));
        }
    }
}
