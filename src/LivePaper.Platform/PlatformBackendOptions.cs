namespace LivePaper.Platform;

public record PlatformBackendOptions(
    TimeSpan VisibilityPollInterval,
    bool TrackPointerPosition = false);
