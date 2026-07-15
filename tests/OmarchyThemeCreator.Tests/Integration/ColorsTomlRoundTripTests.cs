using System;
using System.IO;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// The on-disk half of the TOML round-trip: <see cref="ColorsTomlService.Save"/> then
/// <see cref="ColorsTomlService.Load"/> against a real temp file. Complements the in-memory
/// Parse/Serialize unit test. No env vars, so this stays parallel-safe.
/// </summary>
public sealed class ColorsTomlRoundTripTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));

    public ColorsTomlRoundTripTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Save_then_Load_preserves_the_whole_palette()
    {
        ThemeColors original = new ThemeColors
        {
            Accent = "#010203",
            Background = "#0a0b0c",
            Foreground = "#070809",
        };
        for (int i = 0; i < 16; i++)
            original.Ansi[i] = $"#{i:x2}00{i:x2}";

        string path = Path.Combine(_dir, "colors.toml");
        ColorsTomlService.Save(path, original);
        ThemeColors loaded = ColorsTomlService.Load(path);

        Assert.Equal(original.AsPairs(), loaded.AsPairs());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
