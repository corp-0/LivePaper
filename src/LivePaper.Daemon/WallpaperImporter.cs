using System.Text;
using System.Text.Json;
using LivePaper.Protocol;

namespace LivePaper.Daemon;

public static class WallpaperImporter
{
    public static string Import(string sourceArgument, string? destinationRootArgument)
    {
        var source = Path.GetFullPath(sourceArgument);
        var projectPath = Path.Combine(source, "project.json");
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The import source has no project.json.", projectPath);
        }

        using var project = JsonDocument.Parse(File.ReadAllText(projectPath));
        var root = project.RootElement;
        var type = root.GetProperty("type").GetString();
        if (!string.Equals(type, "web", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Wallpaper Engine project type '{type}' is not supported.");
        }

        var entry = root.GetProperty("file").GetString();
        if (string.IsNullOrWhiteSpace(entry))
        {
            throw new InvalidDataException("The Wallpaper Engine project has no entry file.");
        }

        var entryPath = Path.GetFullPath(entry, source);
        EnsureContained(source, entryPath);
        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException("The wallpaper entry does not exist.", entryPath);
        }

        var sourceId = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The source directory name cannot be used as a wallpaper ID.");
        }

        var id = $"wallpaper-engine.{sourceId}";

        var destinationRoot = Path.GetFullPath(destinationRootArgument ?? GetDefaultDestinationRoot());
        var destination = Path.Combine(destinationRoot, id);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"The imported wallpaper already exists: {destination}");
        }

        Directory.CreateDirectory(destinationRoot);
        var staging = Path.Combine(destinationRoot, $".{id}.importing-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(source, staging);
            var manifestToml = BuildManifest(root, id, entry);
            _ = WallpaperManifest.Load(manifestToml);
            File.WriteAllText(Path.Combine(staging, "manifest.toml"), manifestToml);
            Directory.Move(staging, destination);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            throw;
        }

        return destination;
    }

    private static string GetDefaultDestinationRoot()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        return Path.Combine(dataHome, "livepaper", "wallpapers");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (string.Equals(relative, "project.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static string BuildManifest(JsonElement project, string id, string entry)
    {
        var title = project.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : null;
        var output = new StringBuilder()
            .AppendLine("format_version = 1")
            .Append("id = ").AppendLine(ToTomlString(id))
            .Append("name = ").AppendLine(ToTomlString(string.IsNullOrWhiteSpace(title) ? id : title))
            .Append("entry = ").AppendLine(ToTomlString(entry.Replace('\\', '/')))
            .AppendLine("capabilities = []");

        if (project.TryGetProperty("general", out var general) &&
            general.TryGetProperty("properties", out var properties))
        {
            output.AppendLine().AppendLine("[wallpaper_engine.properties]");
            foreach (var property in properties.EnumerateObject())
            {
                if (!property.Value.TryGetProperty("value", out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                output.Append(ToTomlKey(property.Name))
                    .Append(" = ")
                    .AppendLine(ToTomlValue(value));
            }
        }

        return output.ToString();
    }

    private static string ToTomlValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => ToTomlString(value.GetString() ?? string.Empty),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => throw new InvalidDataException($"Wallpaper property value type '{value.ValueKind}' is not supported.")
    };

    private static string ToTomlKey(string value) => ToTomlString(value);

    private static string ToTomlString(string value) =>
        $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)}\"";

    private static void EnsureContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The wallpaper entry must stay inside its source directory.");
        }
    }
}
