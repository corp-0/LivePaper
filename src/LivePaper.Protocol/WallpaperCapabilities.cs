namespace LivePaper.Protocol;

public static class WallpaperCapabilities
{
    public const string PointerInput = "pointer_input";
    public const string GlobalPointerTracking = "global_pointer_tracking";
    public const string AudioReaction = "audio_reaction";

    private static readonly HashSet<string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        PointerInput,
        GlobalPointerTracking,
        AudioReaction
    };

    public static bool IsKnown(string capability) => Known.Contains(capability);
}
