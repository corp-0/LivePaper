using LivePaper.Protocol;
using LivePaper.Platform;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Tomlyn;
using Tomlyn.Serialization;

namespace LivePaper.Daemon;

internal static class WallpaperLibrary
{
    public static string GetDefaultRoot()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome) || !Path.IsPathFullyQualified(dataHome))
        {
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        return Path.Combine(dataHome, "livepaper", "wallpapers");
    }

    public static int Doctor(string? libraryArgument = null, string? configArgument = null)
    {
        var issueCount = CheckDependencies();

        var library = Path.GetFullPath(libraryArgument ?? GetDefaultRoot());
        if (!Directory.Exists(library))
        {
            Console.Error.WriteLine($"ERROR: Wallpaper library does not exist: {library}");
            return 1;
        }

        var checkedCount = 0;
        foreach (var directory in Directory.EnumerateDirectories(library).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(directory)[0] == '.')
            {
                Console.Error.WriteLine($"ERROR: Incomplete import directory: {directory}");
                issueCount++;
                continue;
            }

            checkedCount++;
            var manifestPath = Path.Combine(directory, "manifest.toml");
            if (!File.Exists(manifestPath))
            {
                Console.Error.WriteLine($"ERROR: Missing manifest: {manifestPath}");
                issueCount++;
                continue;
            }

            string manifestToml;
            try
            {
                manifestToml = File.ReadAllText(manifestPath);
            }
            catch (IOException exception)
            {
                Console.Error.WriteLine($"{Path.GetFileName(directory)}:");
                Console.Error.WriteLine("  Cannot read manifest");
                Console.Error.WriteLine($"  Reason: {exception.Message}");
                issueCount++;
                continue;
            }

            WallpaperManifest manifest;
            try
            {
                manifest = WallpaperManifest.Load(manifestToml);
            }
            catch (TomlException exception)
            {
                Console.Error.WriteLine(FormatInvalidToml(directory, manifestToml, exception));
                issueCount++;
                continue;
            }
            catch (InvalidDataException exception)
            {
                Console.Error.WriteLine($"{ReadManifestId(directory, manifestToml)}:");
                Console.Error.WriteLine("  Invalid manifest");
                Console.Error.WriteLine($"  Reason: {exception.Message}");
                issueCount++;
                continue;
            }

            var entryPath = Path.GetFullPath(manifest.Entry, directory);
            if (!IsContained(directory, entryPath) || !File.Exists(entryPath))
            {
                Console.Error.WriteLine($"ERROR: {manifest.Id} has a missing or invalid entry: {manifest.Entry}");
                issueCount++;
            }

            string[] dependencies;
            try
            {
                dependencies = LoadRecordedDependencies(directory, manifest);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or TomlException)
            {
                Console.Error.WriteLine($"ERROR: Invalid dependency state for {manifest.Id}: {exception.Message}");
                issueCount++;
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency) ||
                    dependency.Any(static character => !char.IsAsciiDigit(character)))
                {
                    Console.Error.WriteLine($"ERROR: {manifest.Id} has an invalid dependency ID: {dependency}");
                    issueCount++;
                    continue;
                }

                var dependencyPath = Path.Combine(directory, dependency);
                if (!string.Equals(
                        dependency,
                        manifest.WallpaperEngine?.BaseDependency,
                        StringComparison.Ordinal) &&
                    !Directory.Exists(dependencyPath))
                {
                    Console.Error.WriteLine($"ERROR: {manifest.Id} is missing dependency {dependency}");
                    issueCount++;
                }
            }
        }

        InspectCurrentWallpaperJavaScript(library, configArgument);
        Console.WriteLine($"Checked {checkedCount} wallpaper(s); found {issueCount} issue(s).");
        return issueCount == 0 ? 0 : 1;
    }

    private static void InspectCurrentWallpaperJavaScript(string library, string? configArgument)
    {
        if (configArgument is null)
        {
            Console.WriteLine("JavaScript: not checked (config not specified)");
            Console.WriteLine();
            return;
        }

        var configPath = Path.GetFullPath(configArgument);
        if (!File.Exists(configPath))
        {
            Console.WriteLine("JavaScript: not checked (LivePaper config was not found)");
            Console.WriteLine();
            return;
        }

        var config = LivePaperConfig.Load(File.ReadAllText(configPath));
        FindById(library, config.Wallpaper.Id);
        Console.WriteLine($"JavaScript ({config.Wallpaper.Id}):");
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var logPath = string.IsNullOrWhiteSpace(runtimeDirectory)
            ? null
            : Path.Combine(runtimeDirectory, "livepaper", "javascript.log");
        if (logPath is null || !File.Exists(logPath))
        {
            Console.WriteLine("  No log from the running renderer.");
        }
        else
        {
            var lines = File.ReadAllLines(logPath);
            var expectedHeader = $"wallpaper = {config.Wallpaper.Id}";
            if (lines.Length == 0 || !string.Equals(lines[0], expectedHeader, StringComparison.Ordinal))
            {
                Console.WriteLine("  No log from the current wallpaper.");
            }
            else if (lines.Length == 1)
            {
                Console.WriteLine("  No messages.");
            }
            else
            {
                var messages = lines.Skip(1)
                    .GroupBy(static line => line, StringComparer.Ordinal)
                    .Select(static group => (Message: group.Key, Count: group.Count()))
                    .OrderByDescending(static message => message.Count)
                    .ThenBy(static message => message.Message, StringComparer.Ordinal)
                    .ToArray();
                foreach (var message in messages.Take(50))
                {
                    var count = message.Count == 1 ? string.Empty : $" (repeated {message.Count} times)";
                    Console.WriteLine($"  {message.Message}{count}");
                }

                if (messages.Length > 50)
                {
                    Console.WriteLine($"  ... and {messages.Length - 50} other distinct messages.");
                }
            }
        }
        Console.WriteLine();
    }

    private static int CheckDependencies()
    {
        var issueCount = 0;
        var renderer = FindRenderer();
        var pwRecord = FindExecutable("pw-record", Environment.GetEnvironmentVariable("PATH"));
        var desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP");
        var backend = PlatformBackendFactory.GetBackendInfo(desktop);
        Console.WriteLine("Dependencies:");
        if (renderer is null)
        {
            Console.WriteLine("  Rendering: unavailable (LivePaper.Renderer was not found)");
            issueCount++;
        }
        else if (backend?.HostsWebContent is true)
        {
            Console.WriteLine("  Rendering: available (renderer web host)");
        }
        else
        {
            var dependencyProbe = RunRendererProbe(renderer, "--check-dependencies");
            if (dependencyProbe.ExitCode == 0)
            {
                Console.WriteLine("  Rendering: available (WPE WebKit, WPE FDO, Wayland, EGL, and GLES)");
            }
            else
            {
                Console.WriteLine($"  Rendering: unavailable ({dependencyProbe.SingleLineMessage})");
                issueCount++;
            }
        }

        Console.WriteLine(pwRecord is null
            ? "  Audio reaction: unavailable (`pw-record` was not found in PATH)"
            : $"  Audio reaction: available (PipeWire via {pwRecord})");
        Console.WriteLine();

        Console.WriteLine("Compositor:");
        if (backend is not null)
        {
            Console.WriteLine($"  Backend: {backend.DisplayName}");
        }
        else
        {
            Console.WriteLine($"  Backend: unsupported (XDG_CURRENT_DESKTOP={desktop ?? "<unset>"})");
            issueCount++;
        }
        Console.WriteLine();
        return issueCount;
    }

    private static string? FindRenderer()
    {
        var installed = Path.Combine(AppContext.BaseDirectory, "LivePaper.Renderer");
        if (File.Exists(installed))
        {
            return installed;
        }

        var development = Path.Combine(
            Environment.CurrentDirectory,
            "src",
            "LivePaper.Renderer",
            "bin",
            "Debug",
            "net10.0",
            "LivePaper.Renderer");
        return File.Exists(development) ? development : null;
    }

    private static ProbeResult RunRendererProbe(string renderer, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = renderer,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new IOException($"Could not run renderer: {renderer}");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();
        var message = string.Join(
            Environment.NewLine,
            new[] { output.Trim(), error.Trim() }.Where(static text => !string.IsNullOrWhiteSpace(text)));
        return new ProbeResult(process.ExitCode, message.Trim());
    }

    public static string? FindExecutable(string name, string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        foreach (var directory in executablePath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (!File.Exists(candidate))
            {
                continue;
            }

            try
            {
                var mode = File.GetUnixFileMode(candidate);
                if ((mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    return candidate;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    public static string FixDependencies(
        string wallpaperId,
        IReadOnlyCollection<string> dependencyArguments,
        string? libraryArgument = null)
    {
        if (string.IsNullOrWhiteSpace(wallpaperId))
        {
            throw new ArgumentException("The installed wallpaper ID cannot be empty.");
        }

        var library = Path.GetFullPath(libraryArgument ?? GetDefaultRoot());
        var (wallpaperDirectory, manifest) = FindById(library, wallpaperId);
        var dependencies = dependencyArguments.Select(ResolveDependency).ToArray();
        var duplicate = dependencies.GroupBy(static dependency => dependency.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Workshop dependency '{duplicate.Key}' was specified more than once.");
        }

        var existing = LoadRecordedDependencies(wallpaperDirectory, manifest);
        var dependencyIds = existing.Concat(dependencies.Select(static dependency => dependency.Id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var addedDirectories = new List<string>();
        var stagingDirectories = new List<string>();
        string? temporaryState = null;
        try
        {
            foreach (var dependency in dependencies)
            {
                var destination = Path.Combine(wallpaperDirectory, dependency.Id);
                if (Directory.Exists(destination))
                {
                    continue;
                }

                var staging = Path.Combine(wallpaperDirectory, $".{dependency.Id}.dependency-{Guid.NewGuid():N}");
                stagingDirectories.Add(staging);
                WallpaperImporter.CopyDirectoryForDependency(dependency.Path, staging);
                Directory.Move(staging, destination);
                stagingDirectories.Remove(staging);
                addedDirectories.Add(destination);
            }

            var stateToml = TomlSerializer.Serialize(
                new WallpaperDependencyState { Dependencies = dependencyIds },
                WallpaperDependencyTomlContext.Default);
            var statePath = Path.Combine(wallpaperDirectory, DependencyStateFileName);
            temporaryState = $"{statePath}.tmp-{Guid.NewGuid():N}";
            File.WriteAllText(temporaryState, stateToml);
            File.Move(temporaryState, statePath, overwrite: true);
            temporaryState = null;
        }
        catch
        {
            if (temporaryState is not null && File.Exists(temporaryState))
            {
                File.Delete(temporaryState);
            }

            foreach (var directory in stagingDirectories)
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }

            foreach (var directory in addedDirectories)
            {
                Directory.Delete(directory, recursive: true);
            }

            throw;
        }

        return wallpaperDirectory;
    }

    public static string[] LoadRecordedDependencies(string wallpaperDirectory, WallpaperManifest manifest)
    {
        var statePath = Path.Combine(wallpaperDirectory, DependencyStateFileName);
        var repaired = File.Exists(statePath)
            ? TomlSerializer.Deserialize<WallpaperDependencyState>(
                File.ReadAllText(statePath),
                WallpaperDependencyTomlContext.Default)?.Dependencies ?? []
            : [];
        return (manifest.WallpaperEngine?.Dependencies ?? [])
            .Concat(repaired)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public static string FormatInvalidToml(string wallpaperDirectory, string toml, Exception exception)
    {
        var output = new List<string>
        {
            $"{ReadManifestId(wallpaperDirectory, toml)}:",
            "  Invalid TOML"
        };
        var lines = toml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var lineNumber = FindLastLineNumber(exception.Message);
        if (lineNumber < 1 || lineNumber > lines.Length)
        {
            return string.Join(Environment.NewLine, output);
        }

        var assignment = lines[lineNumber - 1];
        var equals = assignment.IndexOf('=');
        if (equals < 0)
        {
            output.Add($"  Line: {lineNumber}");
            return string.Join(Environment.NewLine, output);
        }

        var key = assignment[..equals].Trim().Trim('"');
        var table = FindTable(lines, lineNumber - 1);
        var qualifiedKey = string.IsNullOrEmpty(table) ? key : $"{table}.{key}";
        output.Add($"  Key: {qualifiedKey}");
        var value = assignment[(equals + 1)..].Trim();
        output.Add($"  Value: {value}");
        if (IsMisspelledBoolean(value))
        {
            output.Add("  Expected: boolean (`true` or `false`)");
        }

        return string.Join(Environment.NewLine, output);
    }

    private static string ReadManifestId(string wallpaperDirectory, string toml)
    {
        foreach (var line in toml.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0 && trimmed[0] == '[')
            {
                break;
            }

            if (!trimmed.StartsWith("id", StringComparison.Ordinal))
            {
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals >= 0)
            {
                var value = trimmed[(equals + 1)..].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return Path.GetFileName(wallpaperDirectory);
    }

    private static int FindLastLineNumber(string message)
    {
        var result = -1;
        for (var index = 0; index < message.Length - 3; index++)
        {
            if (message[index] != '(' || !char.IsAsciiDigit(message[index + 1]))
            {
                continue;
            }

            var end = index + 1;
            while (end < message.Length && char.IsAsciiDigit(message[end]))
            {
                end++;
            }

            if (end < message.Length && message[end] == ',' &&
                int.TryParse(message.AsSpan(index + 1, end - index - 1), out var lineNumber))
            {
                result = lineNumber;
            }
        }

        return result;
    }

    private static string FindTable(string[] lines, int beforeLine)
    {
        for (var index = beforeLine - 1; index >= 0; index--)
        {
            var line = lines[index].Trim();
            if (line.Length >= 3 && line[0] == '[' && line[^1] == ']')
            {
                return line.Trim('[', ']');
            }
        }

        return string.Empty;
    }

    private static bool IsMisspelledBoolean(string value) =>
        IsOneEditAway(value, "true") || IsOneEditAway(value, "false");

    private static bool IsOneEditAway(string value, string expected)
    {
        if (Math.Abs(value.Length - expected.Length) > 1)
        {
            return false;
        }

        var valueIndex = 0;
        var expectedIndex = 0;
        var edits = 0;
        while (valueIndex < value.Length && expectedIndex < expected.Length)
        {
            if (value[valueIndex] == expected[expectedIndex])
            {
                valueIndex++;
                expectedIndex++;
                continue;
            }

            if (++edits > 1)
            {
                return false;
            }

            if (value.Length >= expected.Length)
            {
                valueIndex++;
            }
            if (expected.Length >= value.Length)
            {
                expectedIndex++;
            }
        }

        return edits + (value.Length - valueIndex) + (expected.Length - expectedIndex) == 1;
    }

    private static (string Directory, WallpaperManifest Manifest) FindById(string library, string wallpaperId)
    {
        if (!Directory.Exists(library))
        {
            throw new DirectoryNotFoundException($"The wallpaper library does not exist: {library}");
        }

        var matches = new List<(string, WallpaperManifest)>();
        foreach (var directory in Directory.EnumerateDirectories(library))
        {
            var manifestPath = Path.Combine(directory, "manifest.toml");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            var manifest = WallpaperManifest.Load(File.ReadAllText(manifestPath));
            if (string.Equals(manifest.Id, wallpaperId, StringComparison.Ordinal))
            {
                matches.Add((directory, manifest));
            }
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new FileNotFoundException($"Wallpaper '{wallpaperId}' is not installed in {library}."),
            _ => throw new InvalidDataException($"Multiple installed wallpapers use the ID '{wallpaperId}'.")
        };
    }

    private static WorkshopDependency ResolveDependency(string argument)
    {
        var path = Path.GetFullPath(argument);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Workshop dependency directory does not exist: {path}");
        }

        var id = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(id) || id.Any(static character => !char.IsAsciiDigit(character)))
        {
            throw new InvalidDataException($"Workshop dependency directory name '{id}' is not a numeric item ID.");
        }

        return new WorkshopDependency(id, path);
    }

    private static bool IsContained(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private sealed record WorkshopDependency(string Id, string Path);

    private sealed record ProbeResult(int ExitCode, string Message)
    {
        public string SingleLineMessage => Message.ReplaceLineEndings(" ");
    }

    private const string DependencyStateFileName = ".livepaper-dependencies.toml";
}

public sealed class WallpaperDependencyState
{
    [JsonPropertyName("dependencies")]
    public string[] Dependencies { get; init; } = [];
}

[TomlSerializable(typeof(WallpaperDependencyState))]
public sealed partial class WallpaperDependencyTomlContext : TomlSerializerContext
{
}
