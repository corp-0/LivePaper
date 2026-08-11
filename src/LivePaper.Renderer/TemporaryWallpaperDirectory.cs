using System.Net;
using System.Text;

namespace LivePaper.Renderer;

public sealed class TemporaryWallpaperDirectory : IDisposable
{
    private TemporaryWallpaperDirectory(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TemporaryWallpaperDirectory CreateFallback(string message)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"livepaper-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            var html = $$"""
                <!doctype html>
                <html>
                <head>
                  <meta charset="utf-8">
                  <meta name="viewport" content="width=device-width,initial-scale=1">
                  <style>
                    html,body{width:100%;height:100%;margin:0;background:#111;color:#ddd;font:16px system-ui,sans-serif}
                    body{display:grid;place-items:center}p{max-width:40rem;padding:2rem;text-align:center}
                  </style>
                </head>
                <body><p>{{WebUtility.HtmlEncode(message)}}</p></body>
                </html>
                """;
            File.WriteAllText(System.IO.Path.Combine(path, "index.html"), html, Encoding.UTF8);
            return new TemporaryWallpaperDirectory(path);
        }
        catch
        {
            Directory.Delete(path, recursive: true);
            throw;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
