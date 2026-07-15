using System.Collections.Generic;
using System.Threading.Tasks;
using OmarchyThemeCreator.Models;
using OmarchyThemeCreator.Services;
using OmarchyThemeCreator.ViewModels;

namespace OmarchyThemeCreator.Tests.Support;

/// <summary>
/// A recording <see cref="IPaletteHost"/> test double. Every tab view-model pushes its results
/// through this interface (the palette choke point), so a fake that records the calls lets a test
/// assert exactly what a VM produced — the applied palette, its status message, and the
/// <c>coalesceKey</c> that controls undo-history folding.
///
/// Hand-rolled rather than built with NSubstitute because the call-sequence assertions
/// (inspect <see cref="AppliedPalettes"/>) read more clearly this way.
/// </summary>
public sealed class FakePaletteHost : IPaletteHost
{
    public sealed record PaletteApplication(ThemeColors Palette, string Status, string? CoalesceKey);

    public List<PaletteApplication> AppliedPalettes { get; } = new();
    public List<bool> LightModeCalls { get; } = new();
    public List<string> StatusMessages { get; } = new();

    public ThemeColors CurrentPalette { get; set; } = new();

    /// <summary>Value returned by <see cref="AddBackground"/>; null models "not saved / copy failed".</summary>
    public BackgroundAddResult? AddBackgroundResult { get; set; }

    public void ApplyPalette(ThemeColors next, string status, string? coalesceKey = null)
    {
        AppliedPalettes.Add(new PaletteApplication(next, status, coalesceKey));
        CurrentPalette = next;
    }

    public void SetLightMode(bool light) => LightModeCalls.Add(light);

    public void SetStatus(string status) => StatusMessages.Add(status);

    public Task<string?> PickImageAsync() => Task.FromResult<string?>(null);

    public Task<string?> PickFileAsync() => Task.FromResult<string?>(null);

    public Task<BackgroundAddResult?> AddBackground(string sourcePath) =>
        Task.FromResult(AddBackgroundResult);

    public string StagingDir { get; set; } = System.IO.Path.GetTempPath();
}
