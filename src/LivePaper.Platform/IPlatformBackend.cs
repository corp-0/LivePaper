namespace LivePaper.Platform;

public interface IPlatformBackend : IAsyncDisposable
{
    IVisibilitySource Visibility { get; }

    IPointerPositionSource? PointerPosition { get; }
}
