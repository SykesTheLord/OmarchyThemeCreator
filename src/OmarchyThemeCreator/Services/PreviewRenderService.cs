using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace OmarchyThemeCreator.Services;

/// <summary>
/// Rasterizes an Avalonia control (the live preview panel) to a <c>preview.png</c> for a
/// theme folder, so the generated image matches exactly what the user sees.
/// </summary>
public static class PreviewRenderService
{
    /// <summary>
    /// Render <paramref name="control"/> to a PNG at <paramref name="outputPath"/>.
    /// The control should already be laid out (measured/arranged) and visible.
    /// </summary>
    public static void RenderPng(Control control, string outputPath, double scale = 1.0)
    {
        Size size = control.Bounds.Size;
        if (size.Width < 1 || size.Height < 1)
        {
            // Fall back to the desired size if the control has not been arranged yet.
            control.Measure(Size.Infinity);
            control.Arrange(new Rect(control.DesiredSize));
            size = control.DesiredSize;
        }
        if (size.Width < 1 || size.Height < 1)
            throw new InvalidOperationException("Preview control has no size to render.");

        PixelSize pixelSize = new PixelSize(
            Math.Max(1, (int)(size.Width * scale)),
            Math.Max(1, (int)(size.Height * scale)));
        Vector dpi = new Vector(96 * scale, 96 * scale);

        using RenderTargetBitmap rtb = new RenderTargetBitmap(pixelSize, dpi);
        rtb.Render(control);
        rtb.Save(outputPath);
    }
}
