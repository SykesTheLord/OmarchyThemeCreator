using System;
using System.IO;
using Avalonia.Media.Imaging;
using Serilog;
using SkiaSharp;

namespace OmarchyThemeCreator.Services;

/// <summary>Small helpers to move images between SkiaSharp and Avalonia for on-screen display.</summary>
public static class BitmapInterop
{
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(BitmapInterop));

    /// <summary>Load a file into an Avalonia <see cref="Bitmap"/>, or null on failure.</summary>
    public static Bitmap? TryLoad(string path)
    {
        try
        {
            return new Bitmap(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load image {Path}", path);
            return null;
        }
    }

    /// <summary>Load a file as a downscaled thumbnail (decoded to <paramref name="width"/> px wide),
    /// or null on failure. Cheaper than <see cref="TryLoad"/> for grids of large wallpapers.</summary>
    public static Bitmap? TryLoadThumbnail(string path, int width)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Bitmap.DecodeToWidth(stream, width);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load thumbnail for {Path}", path);
            return null;
        }
    }

    /// <summary>Encode an <see cref="SKBitmap"/> to PNG in memory and wrap it as an Avalonia bitmap.</summary>
    public static Bitmap ToAvalonia(SKBitmap bitmap)
    {
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
        using MemoryStream ms = new MemoryStream(data.ToArray());
        return new Bitmap(ms);
    }
}
