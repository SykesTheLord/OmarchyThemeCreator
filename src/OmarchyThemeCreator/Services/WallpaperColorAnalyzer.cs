using System;
using System.Collections.Generic;
using System.Linq;
using OmarchyThemeCreator.Models;
using Serilog;
using SkiaSharp;

namespace OmarchyThemeCreator.Services;

/// <summary>A representative image color together with the fraction of the image it accounts for.</summary>
public readonly record struct WeightedColor(ColorMath.Rgb Color, double Weight);

/// <summary>
/// The color make-up of an analyzed image: <see cref="Dominant"/> is the set of wallhaven
/// palette colors (hex, no '#') prominent in it — driving the manual multi-swatch filter — and
/// <see cref="Palette"/> is a compact weighted list of its actual representative colors (weights
/// summing to ~1), used to score how well the image matches an arbitrary target palette.
/// </summary>
public sealed record ImageColors(
    IReadOnlySet<string> Dominant,
    IReadOnlyList<WeightedColor> Palette);

/// <summary>
/// Analyzes a wallpaper thumbnail's colors. wallhaven's own <c>colors</c> search parameter only
/// accepts a single color, so to search by several colors — or to match a whole theme palette —
/// we download the thumbnails and analyze them here. A single downscaled pixel pass produces both
/// the set of prominent wallhaven palette colors and a compact weighted palette of the image's
/// real colors. Pure and stateless — safe to call from background threads on many thumbnails
/// concurrently.
/// </summary>
public sealed class WallpaperColorAnalyzer
{
    private static readonly ILogger Log = Serilog.Log.ForContext<WallpaperColorAnalyzer>();

    /// <summary>Fraction of pixels that must map to a wallhaven color for it to count as present.</summary>
    private const double PresenceThreshold = 0.06;

    /// <summary>Longest edge (px) the image is scaled down to before scanning; big enough to keep
    /// color proportions faithful, small enough that a full-image pixel scan stays cheap.</summary>
    private const int ScanSize = 96;

    /// <summary>How many representative colors to keep for the weighted palette.</summary>
    private const int PaletteSize = 24;

    /// <summary>Squared RGB distance under which a representative color is considered to "match" a
    /// target (theme) color when scoring in <see cref="ThemeMatch"/> — ~72 per the Euclidean norm.</summary>
    private const int ThemeMatchDistanceSq = 72 * 72;

    /// <summary>The wallhaven palette pre-parsed into RGB, paired with its hex string, so the
    /// per-pixel nearest-color search avoids re-parsing hex on every pixel.</summary>
    private static readonly (string Hex, int R, int G, int B)[] Palette =
        WallhavenService.Colors.Select(hex =>
        {
            int r = Convert.ToInt32(hex.Substring(0, 2), 16);
            int g = Convert.ToInt32(hex.Substring(2, 2), 16);
            int b = Convert.ToInt32(hex.Substring(4, 2), 16);
            return (hex, r, g, b);
        }).ToArray();

    /// <summary>
    /// Analyze the image's colors. Returns empty results for an image that can't be decoded, so
    /// callers can treat "undecodable" the same as "matches nothing".
    /// </summary>
    public ImageColors Analyze(byte[] imageBytes)
    {
        using SKBitmap? decoded = SKBitmap.Decode(imageBytes);
        if (decoded is null)
        {
            Log.Debug("Analyze: could not decode {Bytes}-byte image", imageBytes.Length);
            return new ImageColors(new HashSet<string>(), Array.Empty<WeightedColor>());
        }

        // Downscale so the scan cost is independent of the source resolution.
        int max = Math.Max(decoded.Width, decoded.Height);
        double scale = max > ScanSize ? (double)ScanSize / max : 1.0;
        SKImageInfo info = new SKImageInfo(
            Math.Max(1, (int)(decoded.Width * scale)),
            Math.Max(1, (int)(decoded.Height * scale)),
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap small = decoded.Resize(info, SKFilterQuality.Medium) ?? decoded;

        // One pass fills two structures: per-wallhaven-color counts (for the Dominant set) and a
        // coarse 16-levels-per-channel RGB histogram accumulating sums (for the weighted palette).
        int[] whCounts = new int[Palette.Length];
        const int Bins = 16 * 16 * 16;
        long[] binR = new long[Bins], binG = new long[Bins], binB = new long[Bins];
        int[] binN = new int[Bins];
        int total = 0;
        // Read the raw pixel bytes directly rather than calling GetPixel per pixel, which allocates
        // and marshals an SKColor each time and dominates this per-thumbnail scan (run across many
        // thumbnails). The resized bitmap is Rgba8888; guard the byte order for the rare fallback
        // where Resize returned the source bitmap in its native layout.
        ReadOnlySpan<byte> px = small.GetPixelSpan();
        bool fastPath = small.BytesPerPixel == 4 &&
            (small.ColorType == SKColorType.Rgba8888 || small.ColorType == SKColorType.Bgra8888);
        int ri = small.ColorType == SKColorType.Bgra8888 ? 2 : 0;
        int bi = small.ColorType == SKColorType.Bgra8888 ? 0 : 2;
        int stride = small.RowBytes;
        for (int y = 0; y < small.Height; y++)
        for (int x = 0; x < small.Width; x++)
        {
            int r, g, b, a;
            if (fastPath)
            {
                int o = y * stride + x * 4;
                a = px[o + 3]; r = px[o + ri]; g = px[o + 1]; b = px[o + bi];
            }
            else
            {
                SKColor p = small.GetPixel(x, y);
                a = p.Alpha; r = p.Red; g = p.Green; b = p.Blue;
            }
            if (a < 8) continue;
            whCounts[NearestIndex(r, g, b)]++;
            int bin = (r >> 4) | ((g >> 4) << 4) | ((b >> 4) << 8);
            binR[bin] += r; binG[bin] += g; binB[bin] += b; binN[bin]++;
            total++;
        }
        if (total == 0)
            return new ImageColors(new HashSet<string>(), Array.Empty<WeightedColor>());

        HashSet<string> dominant = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Palette.Length; i++)
            if ((double)whCounts[i] / total >= PresenceThreshold)
                dominant.Add(Palette[i].Hex);

        return new ImageColors(dominant, BuildPalette(binR, binG, binB, binN));
    }

    /// <summary>Take the most-populated histogram bins, turn each into its centroid color weighted
    /// by pixel share, and normalize the kept weights to sum to 1 (so scores land in [0,1]).</summary>
    private static IReadOnlyList<WeightedColor> BuildPalette(long[] r, long[] g, long[] b, int[] n)
    {
        List<int> top = Enumerable.Range(0, n.Length)
            .Where(i => n[i] > 0)
            .OrderByDescending(i => n[i])
            .Take(PaletteSize)
            .ToList();

        double keptTotal = top.Sum(i => (double)n[i]);
        List<WeightedColor> palette = new List<WeightedColor>(top.Count);
        foreach (int i in top)
        {
            ColorMath.Rgb color = new ColorMath.Rgb(
                (int)(r[i] / n[i]), (int)(g[i] / n[i]), (int)(b[i] / n[i]));
            palette.Add(new WeightedColor(color, n[i] / keptTotal));
        }
        return palette;
    }

    /// <summary>
    /// Score how well an analyzed image matches a target palette (e.g. the current theme colors),
    /// in [0,1]: the summed weight of the image's representative colors that fall within
    /// <see cref="ThemeMatchDistanceSq"/> of some target color. 0 when either side is empty.
    /// </summary>
    public static double ThemeMatch(ImageColors image, IReadOnlyList<ColorMath.Rgb> targets)
    {
        if (targets.Count == 0 || image.Palette.Count == 0) return 0;

        double covered = 0;
        foreach (WeightedColor wc in image.Palette)
        {
            int best = int.MaxValue;
            foreach (ColorMath.Rgb t in targets)
            {
                int dr = wc.Color.R - t.R, dg = wc.Color.G - t.G, db = wc.Color.B - t.B;
                int dist = dr * dr + dg * dg + db * db;
                if (dist < best) best = dist;
            }
            if (best <= ThemeMatchDistanceSq) covered += wc.Weight;
        }
        return covered;
    }

    private static int NearestIndex(int r, int g, int b)
    {
        int best = 0, bestDist = int.MaxValue;
        for (int i = 0; i < Palette.Length; i++)
        {
            int dr = r - Palette[i].R, dg = g - Palette[i].G, db = b - Palette[i].B;
            int dist = dr * dr + dg * dg + db * db;
            if (dist < bestDist) { bestDist = dist; best = i; }
        }
        return best;
    }
}
