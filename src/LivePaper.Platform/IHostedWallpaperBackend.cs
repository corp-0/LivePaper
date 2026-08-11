namespace LivePaper.Platform;

/// <summary>Presents a renderer-owned web source inside the desktop environment.</summary>
public interface IHostedWallpaperBackend
{
    Task PresentAsync(Uri source, CancellationToken cancellationToken = default);
}
