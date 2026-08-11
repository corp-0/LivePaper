using System.Text;

namespace LivePaper.Renderer;

internal sealed class WallpaperJavaScriptLog
{
    private const long MaximumBytes = 256 * 1024;
    private const long RetainedBytes = 192 * 1024;
    private readonly object _lock = new();
    private readonly string _path;
    private readonly string _header;

    private WallpaperJavaScriptLog(string wallpaperId, string path)
    {
        _path = path;
        _header = $"wallpaper = {wallpaperId}";
        File.WriteAllText(_path, _header + Environment.NewLine);
    }

    public static WallpaperJavaScriptLog? TryCreate(string wallpaperId)
    {
        try
        {
            var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtimeDirectory))
            {
                return null;
            }

            var directory = Path.Combine(runtimeDirectory, "livepaper");
            Directory.CreateDirectory(directory);
            return new WallpaperJavaScriptLog(wallpaperId, Path.Combine(directory, "javascript.log"));
        }
        catch (IOException exception)
        {
            Console.Error.WriteLine($"Could not create the wallpaper JavaScript log: {exception.Message}");
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.Error.WriteLine($"Could not create the wallpaper JavaScript log: {exception.Message}");
            return null;
        }
    }

    public void Write(string level, string message)
    {
        var cleanLevel = SingleLine(level);
        var cleanMessage = SingleLine(message);
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_path, $"[{cleanLevel}] {cleanMessage}{Environment.NewLine}");
                if (new FileInfo(_path).Length > MaximumBytes)
                {
                    Compact();
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void Compact()
    {
        var lines = File.ReadAllLines(_path).Skip(1).ToArray();
        var retained = new Stack<string>();
        var bytes = 0L;
        for (var index = lines.Length - 1; index >= 0; index--)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(lines[index]) + Environment.NewLine.Length;
            if (bytes + lineBytes > RetainedBytes)
            {
                break;
            }

            retained.Push(lines[index]);
            bytes += lineBytes;
        }

        File.WriteAllLines(_path, new[] { _header }.Concat(retained));
    }

    private static string SingleLine(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');
}
