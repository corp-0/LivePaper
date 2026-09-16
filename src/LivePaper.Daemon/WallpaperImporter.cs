using System.Text;
using System.Text.Json;
using LivePaper.Protocol;

namespace LivePaper.Daemon;

public static class WallpaperImporter
{
    public static string ImportSteam(
        string workshopId,
        string? destinationRootArgument,
        string? steamDirectory = null)
    {
        ValidateWorkshopId(workshopId);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] steamDirectories = steamDirectory is not null
            ? [Path.GetFullPath(steamDirectory)]
            : [
                Path.Combine(home, ".local", "share", "Steam"),
                Path.Combine(home, ".steam", "steam"),
                Path.Combine(home, ".steam", "root"),
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam")
            ];
        var workshopDirectory = steamDirectories
            .Select(directory => Path.Combine(directory, "steamapps", "workshop", "content", "431960"))
            .FirstOrDefault(directory => Directory.Exists(Path.Combine(directory, workshopId)));
        if (workshopDirectory is null)
        {
            throw new DirectoryNotFoundException(
                $"Workshop item '{workshopId}' was not found locally. Download it in Steam, " +
                "or use --steam-directory to select the Steam library containing its steamapps directory.");
        }

        var source = Path.Combine(workshopDirectory, workshopId);
        var projectPath = Path.Combine(source, "project.json");
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The import source has no project.json.", projectPath);
        }

        using var project = JsonDocument.Parse(File.ReadAllText(projectPath));
        var dependencies = new List<string>();
        if (project.RootElement.TryGetProperty("dependency", out var dependency))
        {
            var dependencyId = dependency.ValueKind == JsonValueKind.String ? dependency.GetString() : null;
            ValidateWorkshopId(dependencyId);
            var dependencyPath = Path.Combine(workshopDirectory, dependencyId!);
            if (!Directory.Exists(dependencyPath))
            {
                throw new DirectoryNotFoundException(
                    $"Workshop item '{workshopId}' requires dependency '{dependencyId}', " +
                    $"which was not found at '{dependencyPath}'. Download the dependency in Steam and retry.");
            }

            dependencies.Add(dependencyPath);
        }

        return Import(source, destinationRootArgument, dependencies);
    }

    private static void ValidateWorkshopId(string? workshopId)
    {
        if (string.IsNullOrEmpty(workshopId) || workshopId.Any(static character => !char.IsAsciiDigit(character)))
        {
            throw new InvalidDataException($"Workshop ID '{workshopId}' must contain only digits.");
        }
    }

    public static string Import(
        string sourceArgument,
        string? destinationRootArgument,
        IReadOnlyCollection<string>? dependencyPaths = null)
    {
        var source = Path.GetFullPath(sourceArgument);
        var projectPath = Path.Combine(source, "project.json");
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The import source has no project.json.", projectPath);
        }

        using var project = JsonDocument.Parse(File.ReadAllText(projectPath));
        var root = project.RootElement;
        var sourceId = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException("The source directory name cannot be used as a wallpaper ID.");
        }

        var dependencies = ResolveDependencies(source, sourceId, dependencyPaths ?? []);
        JsonDocument? baseProject = null;
        var runnableProject = root;
        var runnableSource = source;
        JsonElement? preset = null;
        WorkshopDependency? baseDependency = null;
        if (root.TryGetProperty("dependency", out var dependencyElement))
        {
            var dependencyId = dependencyElement.GetString();
            baseDependency = dependencies.SingleOrDefault(dependency =>
                string.Equals(dependency.Id, dependencyId, StringComparison.Ordinal));
            if (baseDependency is null)
            {
                throw new InvalidDataException(
                    $"Wallpaper Engine preset requires dependency '{dependencyId}'. Pass its directory with --dependency.");
            }

            var baseProjectPath = Path.Combine(baseDependency.Path, "project.json");
            if (!File.Exists(baseProjectPath))
            {
                throw new FileNotFoundException("The preset dependency has no project.json.", baseProjectPath);
            }

            baseProject = JsonDocument.Parse(File.ReadAllText(baseProjectPath));
            runnableProject = baseProject.RootElement;
            runnableSource = baseDependency.Path;
            if (!root.TryGetProperty("preset", out var presetElement) || presetElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("The dependent Wallpaper Engine item has no preset values.");
            }

            preset = presetElement;
        }

        using var baseProjectOwner = baseProject;
        var type = runnableProject.GetProperty("type").GetString();
        if (!string.Equals(type, "web", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Wallpaper Engine project type '{type}' is not supported.");
        }

        var entry = runnableProject.GetProperty("file").GetString();
        if (string.IsNullOrWhiteSpace(entry))
        {
            throw new InvalidDataException("The Wallpaper Engine project has no entry file.");
        }

        var entryPath = Path.GetFullPath(entry, runnableSource);
        EnsureContained(runnableSource, entryPath);
        if (!File.Exists(entryPath))
        {
            throw new FileNotFoundException("The wallpaper entry does not exist.", entryPath);
        }

        var title = root.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : null;
        var id = $"wallpaper-engine.{ToIdSlug(title, sourceId)}";

        var destinationRoot = Path.GetFullPath(destinationRootArgument ?? WallpaperLibrary.GetDefaultRoot());
        var destination = Path.Combine(destinationRoot, id);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"The imported wallpaper already exists: {destination}");
        }

        Directory.CreateDirectory(destinationRoot);
        var staging = Path.Combine(destinationRoot, $".{id}.importing-{Guid.NewGuid():N}");
        try
        {
            if (baseDependency is not null)
            {
                CopyDirectory(baseDependency.Path, staging);
                CopyDirectory(source, staging, overwrite: true, includeProject: false);
                foreach (var dependency in dependencies.Where(dependency => dependency != baseDependency))
                {
                    CopyDirectory(dependency.Path, Path.Combine(staging, dependency.Id));
                }
            }
            else if (dependencies.Count == 0)
            {
                CopyDirectory(source, staging);
            }
            else
            {
                CopyDirectory(source, staging);
                foreach (var dependency in dependencies)
                {
                    CopyDirectory(dependency.Path, Path.Combine(staging, dependency.Id));
                }
            }

            var manifestToml = BuildManifest(
                root,
                runnableProject,
                preset,
                id,
                entry,
                UsesAudioReaction(runnableSource) || UsesAudioReaction(source),
                sourceId,
                dependencies.Select(static dependency => dependency.Id),
                baseDependency?.Id);
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

    private static List<WorkshopDependency> ResolveDependencies(
        string source,
        string sourceId,
        IReadOnlyCollection<string> dependencyPaths)
    {
        var dependencies = new List<WorkshopDependency>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependencyArgument in dependencyPaths)
        {
            var path = Path.GetFullPath(dependencyArgument);
            var dependencyId = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(dependencyId) ||
                dependencyId.Any(static character => !char.IsAsciiDigit(character)))
            {
                throw new InvalidDataException($"Workshop dependency ID '{dependencyId}' is not numeric.");
            }

            if (string.Equals(dependencyId, sourceId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("A Workshop item cannot depend on itself.");
            }

            if (!seen.Add(dependencyId))
            {
                throw new InvalidDataException($"Workshop dependency '{dependencyId}' was specified more than once.");
            }

            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException(
                    $"Workshop dependency directory does not exist: {path}");
            }

            dependencies.Add(new WorkshopDependency(dependencyId, path));
        }

        return dependencies;
    }

    internal static void CopyDirectoryForDependency(string source, string destination) =>
        CopyDirectory(source, destination);

    private static void CopyDirectory(string source, string destination, bool overwrite = false, bool includeProject = true)
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
            
            if (!includeProject && string.Equals(relative, "project.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite);
        }
    }

    private static string BuildManifest(
        JsonElement metadataProject,
        JsonElement runnableProject,
        JsonElement? preset,
        string id,
        string entry,
        bool usesAudioReaction,
        string workshopId,
        IEnumerable<string> dependencies,
        string? baseDependency)
    {
        var title = metadataProject.TryGetProperty("title", out var titleElement)
            ? titleElement.GetString()
            : null;
        var output = new StringBuilder()
            .AppendLine("format_version = 1")
            .Append("id = ").AppendLine(ToTomlString(id))
            .Append("name = ").AppendLine(ToTomlString(string.IsNullOrWhiteSpace(title) ? id : title))
            .Append("entry = ").AppendLine(ToTomlString(entry.Replace('\\', '/')))
            .Append("capabilities = ").AppendLine(
                usesAudioReaction ? "[\"audio_reaction\"]" : "[]")
            .AppendLine()
            .AppendLine("[wallpaper_engine]")
            .Append("workshop_id = ").AppendLine(ToTomlString(workshopId))
            .Append("dependencies = [")
            .Append(string.Join(", ", dependencies.Select(ToTomlString)))
            .AppendLine("]");
        if (baseDependency is not null)
        {
            output.Append("base_dependency = ").AppendLine(ToTomlString(baseDependency));
        }

        var fileProperties = GetFileProperties(runnableProject);
        output.Append("file_properties = [")
            .Append(string.Join(", ", fileProperties.Select(ToTomlString)))
            .AppendLine("]");

        if (runnableProject.TryGetProperty("general", out var general) &&
            general.TryGetProperty("properties", out var properties))
        {
            output.AppendLine().AppendLine("[wallpaper_engine.properties]");
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in properties.EnumerateObject())
            {
                JsonElement presetValue = default;
                var hasPresetValue = preset is { } presetValues &&
                    presetValues.TryGetProperty(property.Name, out presetValue);
                if (hasPresetValue && presetValue.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                if (!hasPresetValue && !property.Value.TryGetProperty("value", out presetValue))
                {
                    continue;
                }

                if (presetValue.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                output.Append(ToTomlKey(property.Name))
                    .Append(" = ")
                    .AppendLine(ToTomlValue(presetValue));
                written.Add(property.Name);
            }

            if (preset is { } extraPresetValues)
            {
                foreach (var property in extraPresetValues.EnumerateObject())
                {
                    if (written.Contains(property.Name) || property.Value.ValueKind == JsonValueKind.Null)
                    {
                        continue;
                    }

                    output.Append(ToTomlKey(property.Name))
                        .Append(" = ")
                        .AppendLine(ToTomlValue(property.Value));
                }
            }
        }

        return output.ToString();
    }

    private static string[] GetFileProperties(JsonElement project)
    {
        if (!project.TryGetProperty("general", out var general) ||
            !general.TryGetProperty("properties", out var properties))
        {
            return [];
        }

        return properties.EnumerateObject()
            .Where(static property =>
                property.Value.TryGetProperty("type", out var type) &&
                type.ValueKind == JsonValueKind.String &&
                type.GetString() is "file" or "directory")
            .Select(static property => property.Name)
            .ToArray();
    }

    private sealed record WorkshopDependency(string Id, string Path);

    private static bool UsesAudioReaction(string source)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (Path.GetExtension(file).ToLowerInvariant() is not (".html" or ".htm" or ".js"))
            {
                continue;
            }

            using var reader = new StreamReader(file);
            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("wallpaperRegisterAudioListener", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
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

    internal static string ToIdSlug(string? title, string fallback)
    {
        var output = new StringBuilder();
        var pendingSeparator = false;
        foreach (var character in title ?? string.Empty)
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && output.Length > 0)
                {
                    output.Append('-');
                }
                output.Append(char.ToLowerInvariant(character));
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        return output.Length == 0 ? fallback : output.ToString();
    }

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
