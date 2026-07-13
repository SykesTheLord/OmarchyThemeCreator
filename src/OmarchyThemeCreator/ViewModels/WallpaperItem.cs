using System.IO;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OmarchyThemeCreator.Models;

namespace OmarchyThemeCreator.ViewModels;

/// <summary>One wallpaper already in the open theme's <c>backgrounds/</c> folder, with its
/// on-disk path and a downscaled thumbnail for the Current Wallpapers grid.</summary>
public sealed partial class WallpaperItem : ObservableObject
{
    // Path is observable because a rename (or reorder) moves the file; the displayed filename and
    // the derived display name both track it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _path;

    [ObservableProperty] private Bitmap? _thumb;

    /// <summary>True when this is the first wallpaper, i.e. the theme's default that the Omarchy
    /// switcher starts on. Maintained by <see cref="CurrentWallpapersViewModel"/> as the list order
    /// changes.</summary>
    [ObservableProperty] private bool _isDefault;

    public string FileName => System.IO.Path.GetFileName(Path);

    /// <summary>Human-friendly name for the wallpaper: the filename with its <c>NN-</c> ordering
    /// prefix and extension stripped, title-cased (e.g. <c>03-sunset-ridge.jpg</c> → "Sunset Ridge").</summary>
    public string DisplayName => Theme.ToDisplayName(
        Regex.Replace(System.IO.Path.GetFileNameWithoutExtension(Path), @"^\d+-", ""));

    public WallpaperItem(string path, Bitmap? thumb)
    {
        _path = path;
        _thumb = thumb;
    }
}
