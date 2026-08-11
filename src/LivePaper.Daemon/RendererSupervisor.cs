using System.Diagnostics;
using LivePaper.Protocol;
using LivePaper.Platform;

namespace LivePaper.Daemon;

public sealed class RendererSupervisor(
    DaemonOptions options,
    IPlatformBackend platformBackend,
    IAudioSpectrumSource? audioSpectrumSource)
{
    private static readonly TimeSpan StableRunTime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RendererShutdownSettleTime = TimeSpan.FromSeconds(1);
    private const int FailureLimit = 3;
    private bool UsesHostedPresentation =>
        !options.ForceDirectWpe && platformBackend is IHostedWallpaperBackend;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var audioStop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var audioTask = audioSpectrumSource?.RunAsync(audioStop.Token);
        var consecutiveFailures = 0;
        try
        {
            await SuperviseRenderersAsync(cancellationToken);
        }
        finally
        {
            audioStop.Cancel();
            if (audioTask is not null)
            {
                try
                {
                    await audioTask;
                }
                catch (OperationCanceledException) when (audioStop.IsCancellationRequested)
                {
                }
            }
        }

        async Task SuperviseRenderersAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                using var ipc = RendererIpcServer.Create();
                using var renderer = StartRenderer(ipc.SocketPath, out var rendererErrors);
                var startedAt = Stopwatch.GetTimestamp();
                Console.WriteLine($"Renderer started (PID {renderer.Id}).");

                try
                {
                    await AcceptRendererAsync(ipc, TimeSpan.FromSeconds(5), cancellationToken);
                    await ipc.SendAsync(HostMessage.ForInitialState(
                        ApplyPolicies(platformBackend.Visibility.Current),
                        platformBackend.PointerPosition?.Current));
                    if (UsesHostedPresentation && platformBackend is IHostedWallpaperBackend hostedBackend)
                    {
                        var presentation = await ReadPresentationAsync(
                            ipc,
                            TimeSpan.FromSeconds(5),
                            cancellationToken);
                        await hostedBackend.PresentAsync(presentation, cancellationToken);
                    }
                    Console.WriteLine($"Renderer connected to protocol {ProtocolVersion.Current}.");

                    var updates = new RendererUpdateBuffer();
                    void OnVisibilityChanged(object? sender, VisibilityChanged state) =>
                        updates.Write(HostMessage.ForVisibility(ApplyPolicies(state)));
                    void OnPointerPositionChanged(object? sender, PointerPositionChanged position) =>
                        updates.Write(HostMessage.ForPointerPosition(position));
                    void OnAudioSpectrumChanged(object? sender, AudioSpectrumChanged spectrum) =>
                        updates.Write(HostMessage.ForAudioSpectrum(spectrum));
                    platformBackend.Visibility.Changed += OnVisibilityChanged;
                    if (platformBackend.PointerPosition is not null)
                    {
                        platformBackend.PointerPosition.Changed += OnPointerPositionChanged;
                    }
                    if (audioSpectrumSource is not null)
                    {
                        audioSpectrumSource.Changed += OnAudioSpectrumChanged;
                    }
                    try
                    {
                        await ForwardUpdatesUntilExitAsync(renderer, ipc, updates, cancellationToken);
                    }
                    finally
                    {
                        platformBackend.Visibility.Changed -= OnVisibilityChanged;
                        if (platformBackend.PointerPosition is not null)
                        {
                            platformBackend.PointerPosition.Changed -= OnPointerPositionChanged;
                        }
                        if (audioSpectrumSource is not null)
                        {
                            audioSpectrumSource.Changed -= OnAudioSpectrumChanged;
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    await Console.Error.WriteLineAsync("Renderer did not complete the protocol handshake within 5 seconds.");
                    await StopRendererAsync(renderer);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await StopRendererAsync(renderer);
                    break;
                }

                var runTime = Stopwatch.GetElapsedTime(startedAt);
                await Console.Error.WriteLineAsync($"Renderer exited with code {renderer.ExitCode} after {runTime:g}.");

                consecutiveFailures = runTime >= StableRunTime ? 0 : consecutiveFailures + 1;
                if (consecutiveFailures >= FailureLimit)
                {
                    var message = GetFallbackMessage(rendererErrors, renderer.ExitCode);
                    await Console.Error.WriteLineAsync(
                        $"Renderer failed {consecutiveFailures} times; showing fallback wallpaper: {message}");
                    using var fallbackIpc = UsesHostedPresentation
                        ? RendererIpcServer.Create()
                        : null;
                    using var fallback = StartFallback(message, fallbackIpc?.SocketPath);
                    try
                    {
                        if (fallbackIpc is not null && platformBackend is IHostedWallpaperBackend hostedBackend)
                        {
                            await AcceptRendererAsync(fallbackIpc, TimeSpan.FromSeconds(5), token);
                            await fallbackIpc.SendAsync(HostMessage.ForInitialState(
                                ApplyPolicies(platformBackend.Visibility.Current),
                                platformBackend.PointerPosition?.Current));
                            var presentation = await ReadPresentationAsync(
                                fallbackIpc,
                                TimeSpan.FromSeconds(5),
                                token);
                            await hostedBackend.PresentAsync(presentation, token);
                        }

                        await fallback.WaitForExitAsync(token);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        await StopRendererAsync(fallback);
                        break;
                    }

                    await Console.Error.WriteLineAsync(
                        $"Fallback renderer exited with code {fallback.ExitCode}; retrying wallpaper.");
                    consecutiveFailures = 0;
                    continue;
                }

                var restartDelay = GetRestartDelay(consecutiveFailures);
                await Console.Error.WriteLineAsync($"Restarting renderer in {restartDelay.TotalSeconds:0.##} seconds.");

                try
                {
                    await Task.Delay(restartDelay, token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }

            Console.WriteLine("LivePaper daemon stopped.");
        }
    }

    internal static async Task AcceptRendererAsync(
        RendererIpcServer ipc,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        handshakeTimeout.CancelAfter(timeout);
        await ipc.AcceptAsync(handshakeTimeout.Token);
    }

    internal static async Task<Uri> ReadPresentationAsync(
        RendererIpcServer ipc,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var presentationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        presentationTimeout.CancelAfter(timeout);
        var message = await ipc.ReadAsync(presentationTimeout.Token);
        if (message.Kind != RendererMessageKind.PresentationReady ||
            !Uri.TryCreate(message.Source, UriKind.Absolute, out var source) ||
            !source.IsLoopback ||
            source.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidDataException("The renderer sent an invalid presentation source.");
        }

        return source;
    }

    private VisibilityChanged ApplyPolicies(VisibilityChanged visibility) => new(
        visibility.State,
        ShouldRender: !Matches(options.DisableRenderingWhen, visibility.State),
        ShouldMute: Matches(options.MuteAudioWhen, visibility.State));

    private static bool Matches(CoveragePolicy policy, VisibilityState state) => policy switch
    {
        CoveragePolicy.Never => false,
        CoveragePolicy.FullyCovered => state == VisibilityState.FullyCovered,
        CoveragePolicy.PartiallyCovered => state is VisibilityState.PartiallyCovered or VisibilityState.FullyCovered,
        CoveragePolicy.Always => true,
        _ => false
    };

    private static async Task ForwardUpdatesUntilExitAsync(
        Process renderer,
        RendererIpcServer ipc,
        RendererUpdateBuffer updates,
        CancellationToken cancellationToken)
    {
        var exitTask = renderer.WaitForExitAsync(cancellationToken);

        while (!exitTask.IsCompleted)
        {
            var updateTask = updates.ReadAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(exitTask, updateTask);
            if (completed == exitTask)
            {
                await exitTask;
                return;
            }

            await ipc.SendAsync(await updateTask);
        }

        await exitTask;
    }

    private Process StartRenderer(string socketPath, out List<string> errors)
    {
        errors = [];
        var startInfo = new ProcessStartInfo
        {
            FileName = options.RendererPath,
            UseShellExecute = false,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--wallpaper");
        startInfo.ArgumentList.Add(options.WallpaperDirectory);
        startInfo.ArgumentList.Add("--socket");
        startInfo.ArgumentList.Add(socketPath);
        if (UsesHostedPresentation)
        {
            startInfo.ArgumentList.Add("--web-host");
        }

        var process = new Process { StartInfo = startInfo };
        var capturedErrors = errors;
        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                return;
            }

            lock (capturedErrors)
            {
                capturedErrors.Add(eventArgs.Data);
            }
            Console.Error.WriteLine(eventArgs.Data);
        };
        if (!process.Start())
        {
            process.Dispose();
            throw new IOException("The renderer process could not be started.");
        }

        process.BeginErrorReadLine();

        return process;
    }

    private Process StartFallback(string message, string? socketPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = options.RendererPath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--fallback");
        startInfo.ArgumentList.Add("--message");
        startInfo.ArgumentList.Add(message);
        if (socketPath is not null)
        {
            startInfo.ArgumentList.Add("--socket");
            startInfo.ArgumentList.Add(socketPath);
            startInfo.ArgumentList.Add("--web-host");
        }
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new IOException("The fallback renderer process could not be started.");
        }

        return process;
    }

    private static string GetFallbackMessage(List<string> errors, int exitCode)
    {
        lock (errors)
        {
            var error = errors.LastOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (error is not null)
            {
                const string prefix = "Cannot load wallpaper: ";
                return error.StartsWith(prefix, StringComparison.Ordinal)
                    ? error[prefix.Length..]
                    : "The renderer stopped during startup.";
            }
        }

        return $"The renderer stopped unexpectedly (exit code {exitCode}).";
    }

    private static async Task StopRendererAsync(Process renderer)
    {
        if (renderer.HasExited)
        {
            return;
        }

        renderer.Kill(entireProcessTree: true);
        await renderer.WaitForExitAsync();
        await Task.Delay(RendererShutdownSettleTime);
    }

    private static TimeSpan GetRestartDelay(int failures)
    {
        var exponent = Math.Min(failures, 5);
        return TimeSpan.FromMilliseconds(Math.Min(250 * Math.Pow(2, exponent), 10_000));
    }
}
