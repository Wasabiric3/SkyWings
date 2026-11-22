using System.Collections.Concurrent;
using System.Reflection;

namespace SkyWings.UI;

public enum LogoVariant { Green, White, Black }

/// <summary>
/// Brand images (logo, icons, hero, watermark) pre-rendered from the asset kit at 1x / 1.5x / 2x and embedded as resources.
/// Each call returns one cached, shared bitmap sized for the given DPI: callers draw it with
/// <c>DrawImageUnscaled</c> and must never dispose it. A missing image returns <c>null</c> so a screen still opens without it.
/// </summary>
internal static class Assets
{
    private static readonly Assembly Source = typeof(Assets).Assembly;
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    /// <summary>The size set to use for a display DPI: "x1" (up to 108), "x15" (up to 156), otherwise "x2".</summary>
    internal static string Bucket(int dpi) => dpi <= 108 ? "x1" : dpi <= 156 ? "x15" : "x2";

    /// <param name="name">Kit icon name without colour, e.g. "Flights", "Check_in", "Manage_Booking".</param>
    public static Bitmap? Icon(string name, bool white, int dpi)
        => Load($"icon_{name}_{(white ? "white" : "green")}_{Bucket(dpi)}");

    public static Bitmap? Logo(LogoVariant variant, int dpi)
        => Load($"logo_{variant.ToString().ToLowerInvariant()}_{Bucket(dpi)}");

    public static Bitmap? Hero(int dpi) => Load($"hero_{Bucket(dpi)}");

    public static Bitmap? Watermark(int dpi) => Load($"watermark_{Bucket(dpi)}");

    /// <summary>A photograph (the home banner or a region card): "hero", "domestic", "southeast_asia", "europe" or "north_america". One size, scaled when drawn.</summary>
    public static Bitmap? Photo(string key) => Load("photo_" + key, "jpg");

    /// <summary>The part of a photograph that matters (0..1 across and down), kept in view when it is cropped to a different shape.</summary>
    public static PointF PhotoFocus(string key) => key switch
    {
        "hero" => new PointF(0.5f, 0.45f),            // the plane
        "domestic" => new PointF(0.5f, 0.45f),        // the rocks, beach and boats
        "southeast_asia" => new PointF(0.5f, 0.55f),  // the floating villages
        "europe" => new PointF(0.5f, 0.5f),           // the dome and the rooftops
        "north_america" => new PointF(0.5f, 0.52f),   // the skyline
        "economy" => new PointF(0.5f, 0.45f),         // the seat backs and screens
        "premium_economy" => new PointF(0.5f, 0.5f),  // the seats and windows
        "business" => new PointF(0.5f, 0.55f),        // the seat and its pillow
        "first_class" => new PointF(0.5f, 0.5f),      // the seat and its suite
        _ => new PointF(0.5f, 0.5f)
    };

    private static Bitmap? Load(string key, string extension = "png") => Cache.GetOrAdd(key + "." + extension, static k =>
    {
        try
        {
            using Stream? stream = Source.GetManifestResourceStream($"Assets/{k}");
            if (stream is null) return null;
            using var decoded = new Bitmap(stream);
            return new Bitmap(decoded);   // independent copy: the decoded bitmap needs its stream open
        }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.ExternalException or IOException)
        {
            AppLogger.Error("Assets", ex);
            return null;
        }
    });
}
