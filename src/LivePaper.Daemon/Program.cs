using LivePaper.Daemon;
using LivePaper.Platform;
using LivePaper.Protocol;
using Tomlyn;

try
{
    if (args.Contains("--check-presentation", StringComparer.Ordinal))
    {
        var configPath = DaemonOptions.GetConfigPath(args);
        if (File.Exists(configPath) && LivePaperConfig.Load(File.ReadAllText(configPath)).ForceDirectWpe)
        {
            Console.WriteLine("Direct rendering is selected; no hosted plugin needs checking.");
            return 0;
        }
        return await PlatformBackendFactory.CheckPresentationAsync() ? 0 : 1;
    }

    if (args.Contains("--doctor", StringComparer.Ordinal))
    {
        return WallpaperLibrary.Doctor(
            ReadOption(args, "--wallpaper-library"),
            ReadOption(args, "--config"));
    }

    var dependencyFixIndex = Array.IndexOf(args, "--dependency-fix");
    if (dependencyFixIndex >= 0)
    {
        if (dependencyFixIndex + 1 >= args.Length)
        {
            throw new ArgumentException("--dependency-fix requires an installed wallpaper ID.");
        }

        var dependencyPaths = ReadOptions(args, "--dependency");
        if (dependencyPaths.Length == 0)
        {
            throw new ArgumentException("--dependency-fix requires at least one --dependency directory.");
        }

        var fixedWallpaper = WallpaperLibrary.FixDependencies(
            args[dependencyFixIndex + 1],
            dependencyPaths,
            ReadOption(args, "--wallpaper-library"));
        Console.WriteLine($"Updated wallpaper dependencies: {fixedWallpaper}");
        return 0;
    }

    var steamImportId = ReadOption(args, "--steam-import");
    if (steamImportId is not null)
    {
        var imported = WallpaperImporter.ImportSteam(
            steamImportId,
            ReadOption(args, "--import-destination"),
            ReadOption(args, "--steam-directory"));
        Console.WriteLine($"Imported wallpaper: {imported}");
        return 0;
    }

    var importIndex = Array.IndexOf(args, "--import-wallpaper");
    if (importIndex >= 0)
    {
        if (importIndex + 1 >= args.Length)
        {
            throw new ArgumentException("--import-wallpaper requires a source directory.");
        }

        var destinationRoot = ReadOption(args, "--import-destination");
        var dependencies = ReadOptions(args, "--dependency");
        var imported = WallpaperImporter.Import(args[importIndex + 1], destinationRoot, dependencies);
        Console.WriteLine($"Imported wallpaper: {imported}");
        return 0;
    }

    using var shutdown = new ShutdownSignalSource();

    var options = DaemonOptions.Parse(args);
    using var configWatcher = new FileChangeWatcher(options.ConfigPath);
    while (!shutdown.IsCancellationRequested)
    {
        Console.WriteLine($"LivePaper daemon supervising {options.WallpaperDirectory}");
        var manifestPath = Path.Combine(options.WallpaperDirectory, "manifest.toml");
        var manifest = WallpaperManifest.Load(await File.ReadAllTextAsync(manifestPath, shutdown.Token));
        using var manifestWatcher = new FileChangeWatcher(manifestPath);
        var trackPointerPosition = manifest.HasCapability(WallpaperCapabilities.GlobalPointerTracking);
        var audioSpectrumSource = manifest.HasCapability(WallpaperCapabilities.AudioReaction)
            ? new PipeWireAudioSpectrumSource()
            : null;
        await using var backend = await PlatformBackendFactory.CreateAsync(
            new PlatformBackendOptions(options.VisibilityPollInterval, trackPointerPosition));
        using var run = CancellationTokenSource.CreateLinkedTokenSource(shutdown.Token);
        var supervisor = new RendererSupervisor(
            options,
            backend,
            audioSpectrumSource);
        var supervisorTask = supervisor.RunAsync(run.Token);
        var configChangedTask = configWatcher.WaitForChangeAsync(shutdown.Token).AsTask();
        var manifestChangedTask = manifestWatcher.WaitForChangeAsync(shutdown.Token).AsTask();

        while (!shutdown.IsCancellationRequested)
        {
            var completed = await Task.WhenAny(supervisorTask, configChangedTask, manifestChangedTask);
            if (completed == supervisorTask)
            {
                await supervisorTask;
                break;
            }

            try
            {
                if (completed == manifestChangedTask)
                {
                    await manifestChangedTask;
                    await manifestWatcher.DebounceAsync(shutdown.Token);
                    try
                    {
                        _ = WallpaperManifest.Load(await File.ReadAllTextAsync(manifestPath, shutdown.Token));
                        Console.WriteLine($"Active wallpaper manifest changed; reloading {manifestPath}.");
                        run.Cancel();
                        await supervisorTask;
                        break;
                    }
                    catch (Exception exception) when (
                        exception is IOException or InvalidDataException or TomlException)
                    {
                        Console.Error.WriteLine($"Ignoring invalid manifest change: {exception.Message}");
                        manifestChangedTask = manifestWatcher.WaitForChangeAsync(shutdown.Token).AsTask();
                        continue;
                    }
                }

                await configChangedTask;
                await configWatcher.DebounceAsync(shutdown.Token);
            }
            catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
            {
                run.Cancel();
                await supervisorTask;
                break;
            }
            try
            {
                if (!File.Exists(options.ConfigPath))
                {
                    throw new FileNotFoundException("The config file does not exist.", options.ConfigPath);
                }

                var next = DaemonOptions.Parse(args);
                if (next == options)
                {
                    configChangedTask = configWatcher.WaitForChangeAsync(shutdown.Token).AsTask();
                    continue;
                }

                Console.WriteLine($"Config changed; reloading {options.ConfigPath}.");
                options = next;
                run.Cancel();
                await supervisorTask;
                break;
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or InvalidDataException or TomlException)
            {
                Console.Error.WriteLine($"Ignoring invalid config change: {exception.Message}");
                configChangedTask = configWatcher.WaitForChangeAsync(shutdown.Token).AsTask();
            }
        }
    }

    return 0;
}

catch (Exception exception) when (
    exception is ArgumentException or
        IOException or
        InvalidDataException or
        InvalidOperationException or
        PlatformNotSupportedException or
        TomlException or
        UnauthorizedAccessException)
{
    Console.Error.WriteLine($"LivePaper failed: {exception.Message}");
    return 1;
}

static string[] ReadOptions(string[] arguments, string name)
{
    var values = new List<string>();
    for (var index = 0; index < arguments.Length; index++)
    {
        if (!string.Equals(arguments[index], name, StringComparison.Ordinal))
        {
            continue;
        }

        if (++index >= arguments.Length)
        {
            throw new ArgumentException($"{name} requires a directory.");
        }

        values.Add(arguments[index]);
    }

    return [.. values];
}

static string? ReadOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Length)
    {
        throw new ArgumentException($"{name} requires a value.");
    }

    return arguments[index + 1];
}
