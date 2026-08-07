namespace LivePaper.Protocol;

public static class WallpaperCapabilities
{
    public const string PointerInput = "pointer_input";
    public const string GlobalPointerTracking = "global_pointer_tracking";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        PointerInput,
        GlobalPointerTracking
    };

    public static bool IsKnown(string capability) => Known.Contains(capability);
}
