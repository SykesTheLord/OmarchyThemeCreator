using System;
using System.IO;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using Xunit;

namespace OmarchyThemeCreator.Tests.Integration;

/// <summary>
/// <see cref="PresetService.ImportBase16"/> reads a <c>.yaml</c> file, so it's an integration
/// test. Parallel-safe (temp files only).
/// </summary>
public sealed class PresetServiceImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "otc-tests", Guid.NewGuid().ToString("N"));
    private readonly PresetService _service = new();

    public PresetServiceImportTests() => Directory.CreateDirectory(_dir);

    private string WriteYaml(string contents)
    {
        string path = Path.Combine(_dir, "scheme.yaml");
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void ImportBase16_maps_base_slots_to_the_palette()
    {
        // base00 → background, base05 → foreground, base0D → accent, base02 → selection background.
        string path = WriteYaml("""
            base00: "1a1b26"
            base02: "292e42"
            base05: "a9b1d6"
            base0D: "7aa2f7"
            """);

        ThemeColors colors = _service.ImportBase16(path);

        Assert.Equal("#1a1b26", colors.Background);
        Assert.Equal("#a9b1d6", colors.Foreground);
        Assert.Equal("#7aa2f7", colors.Accent);
        Assert.Equal("#292e42", colors.SelectionBackground);
    }

    [Fact]
    public void ImportBase16_accepts_a_leading_hash()
    {
        string path = WriteYaml("""
            base00: "#101010"
            base05: "#f0f0f0"
            """);

        ThemeColors colors = _service.ImportBase16(path);
        Assert.Equal("#101010", colors.Background);
        Assert.Equal("#f0f0f0", colors.Foreground);
    }

    [Fact]
    public void ImportBase16_rejects_a_file_missing_required_slots()
    {
        string path = WriteYaml("just some text\nnothing: useful\n");
        Assert.Throws<InvalidOperationException>(() => _service.ImportBase16(path));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }
}
