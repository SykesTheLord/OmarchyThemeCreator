using System.Threading.Tasks;
using Avalonia.Media;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Picks a color from anywhere on screen (the "eyedropper"). Implementations degrade gracefully:
/// when no backend is available the app must still run, so <see cref="IsAvailable"/> is false and
/// <see cref="PickAsync"/> returns null.
/// </summary>
public interface IScreenColorPicker
{
    /// <summary>True when a screen picker backend was found (so the UI can show/hide the button).</summary>
    bool IsAvailable { get; }

    /// <summary>Pick a color from the screen, or null if unavailable or the user cancelled.</summary>
    Task<Color?> PickAsync();
}

/// <summary>No-op picker for design time / when no backend exists.</summary>
public sealed class NullScreenColorPicker : IScreenColorPicker
{
    public bool IsAvailable => false;
    public Task<Color?> PickAsync() => Task.FromResult<Color?>(null);
}
