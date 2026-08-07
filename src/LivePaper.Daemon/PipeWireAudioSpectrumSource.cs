using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using LivePaper.Protocol;

namespace LivePaper.Daemon;

public sealed class PipeWireAudioSpectrumSource : IAudioSpectrumSource
{
    public event EventHandler<AudioSpectrumChanged>? Changed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await CaptureAsync(cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    await Console.Error.WriteLineAsync("PipeWire audio capture stopped; restarting.");
                }
            }
            catch (Win32Exception exception)
            {
                await Console.Error.WriteLineAsync(
                    $"Audio reaction is unavailable because pw-record could not start: {exception.Message}");
                return;
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                await Console.Error.WriteLineAsync($"PipeWire audio capture failed: {exception.Message}");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        using var process = StartCaptureProcess();
        var errors = DrainErrorsAsync(process.StandardError, cancellationToken);
        var analyzer = new AudioSpectrumAnalyzer();
        var bytes = new byte[16 * 1024];
        var carry = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await process.StandardOutput.BaseStream.ReadAsync(
                    bytes.AsMemory(carry),
                    cancellationToken);
                if (read == 0)
                {
                    break;
                }

                var byteCount = carry + read;
                var completeBytes = byteCount - byteCount % (sizeof(float) * 2);
                var samples = MemoryMarshal.Cast<byte, float>(bytes.AsSpan(0, completeBytes));
                analyzer.AddInterleavedSamples(samples, spectrum => Changed?.Invoke(this, spectrum));

                carry = byteCount - completeBytes;
                if (carry > 0)
                {
                    bytes.AsSpan(completeBytes, carry).CopyTo(bytes);
                }
            }
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
            }

            await process.WaitForExitAsync(CancellationToken.None);
            await errors;
        }
    }

    private static Process StartCaptureProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "pw-record",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--properties={ stream.capture.sink = true node.name = livepaper-audio }");
        startInfo.ArgumentList.Add($"--rate={AudioSpectrumAnalyzer.SampleRate}");
        startInfo.ArgumentList.Add("--channels=2");
        startInfo.ArgumentList.Add("--channel-map=FL,FR");
        startInfo.ArgumentList.Add("--format=f32");
        startInfo.ArgumentList.Add("--raw");
        startInfo.ArgumentList.Add("-");
        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            process.Dispose();
            throw new IOException("pw-record did not start.");
        }

        return process;
    }

    private static async Task DrainErrorsAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                await Console.Error.WriteLineAsync($"pw-record: {line}");
            }
        }
    }
}
