namespace LivePaper.Platform;

public static class PlatformBackendFactory
{
    public static Task<IPlatformBackend> CreateAsync(PlatformBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.VisibilityPollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "The visibility poll interval must be greater than zero.");
        }

        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        return SelectBackend(desktop) switch
        {
            PlatformBackendKind.KWin => KWinPlatformBackend.CreateAsync(
                options.VisibilityPollInterval,
                options.TrackPointerPosition),
            _ => throw new PlatformNotSupportedException(
                $"No LivePaper platform backend supports XDG_CURRENT_DESKTOP={desktop ?? "<unset>"}.")
        };
    }

    public static PlatformBackendKind SelectBackend(string? desktop)
    {
        var names = desktop?.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        return names.Any(static name =>
            name.Equals("KDE", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Plasma", StringComparison.OrdinalIgnoreCase))
            ? PlatformBackendKind.KWin
            : PlatformBackendKind.Unsupported;
    }
}

public enum PlatformBackendKind
{
    Unsupported,
    KWin
}
