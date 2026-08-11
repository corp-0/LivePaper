using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using LivePaper.Protocol;

namespace LivePaper.Renderer;

public sealed class WallpaperEventHub(
    VisibilityChanged? initialVisibility,
    PointerPositionChanged? initialPointerPosition)
{
    private readonly Lock _lock = new();
    private readonly HashSet<EventClient> _clients = [];
    private VisibilityChanged? _visibility = initialVisibility;
    private PointerPositionChanged? _pointerPosition = initialPointerPosition;
    private float[]? _audioSpectrum;

    public void Dispatch(VisibilityChanged visibility)
    {
        lock (_lock)
        {
            _visibility = visibility;
            Publish(EventKind.Visibility, SerializeVisibilityEvent(visibility));
        }
    }

    public void Dispatch(PointerPositionChanged pointerPosition)
    {
        lock (_lock)
        {
            _pointerPosition = pointerPosition;
            Publish(EventKind.PointerPosition, SerializePointerPositionEvent(pointerPosition));
        }
    }

    public void Dispatch(AudioSpectrumChanged audioSpectrum)
    {
        lock (_lock)
        {
            _audioSpectrum = audioSpectrum.Samples;
            Publish(EventKind.AudioSpectrum, SerializeAudioSpectrumEvent(audioSpectrum.Samples));
        }
    }

    public byte[] GetVisibility()
    {
        lock (_lock)
        {
            return SerializeVisibility(_visibility ?? new(
                VisibilityState.Visible,
                ShouldRender: true,
                ShouldMute: false));
        }
    }

    public async Task WriteEventsAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var client = new EventClient();
        lock (_lock)
        {
            _clients.Add(client);
            if (_visibility is not null)
            {
                client.Publish(EventKind.Visibility, SerializeVisibilityEvent(_visibility));
            }
            if (_pointerPosition is not null)
            {
                client.Publish(EventKind.PointerPosition, SerializePointerPositionEvent(_pointerPosition));
            }
            if (_audioSpectrum is not null)
            {
                client.Publish(EventKind.AudioSpectrum, SerializeAudioSpectrumEvent(_audioSpectrum));
            }
        }

        try
        {
            await stream.WriteAsync(
                "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\n"u8.ToArray(),
                cancellationToken);
            await stream.WriteAsync(
                "Cache-Control: no-cache\r\nConnection: keep-alive\r\n\r\n"u8.ToArray(),
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
            await foreach (var message in client.ReadAllAsync(cancellationToken))
            {
                await stream.WriteAsync("data: "u8.ToArray(), cancellationToken);
                await stream.WriteAsync(message, cancellationToken);
                await stream.WriteAsync("\n\n"u8.ToArray(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
        finally
        {
            lock (_lock)
            {
                _clients.Remove(client);
            }
        }
    }

    private void Publish(EventKind kind, byte[] message)
    {
        foreach (var client in _clients)
        {
            client.Publish(kind, message);
        }
    }

    private static byte[] SerializeVisibilityEvent(VisibilityChanged visibility)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteEventStart(writer, "visibility");
        WriteVisibility(writer, visibility);
        return FinishEvent(stream, writer);
    }

    private static byte[] SerializeVisibility(VisibilityChanged visibility)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteVisibility(writer, visibility);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteVisibility(Utf8JsonWriter writer, VisibilityChanged visibility)
    {
        writer.WriteStartObject();
        writer.WriteString("state", visibility.State.ToString());
        writer.WriteBoolean("shouldRender", visibility.ShouldRender);
        writer.WriteBoolean("shouldMute", visibility.ShouldMute);
        writer.WriteEndObject();
    }

    private static byte[] SerializePointerPositionEvent(PointerPositionChanged pointerPosition)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteEventStart(writer, "pointerPosition");
        writer.WriteStartObject();
        writer.WriteNumber("x", pointerPosition.X);
        writer.WriteNumber("y", pointerPosition.Y);
        writer.WriteEndObject();
        return FinishEvent(stream, writer);
    }

    private static byte[] SerializeAudioSpectrumEvent(float[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        WriteEventStart(writer, "audioSpectrum");
        writer.WriteStartArray();
        foreach (var sample in samples)
        {
            writer.WriteNumberValue(sample);
        }
        writer.WriteEndArray();
        return FinishEvent(stream, writer);
    }

    private static void WriteEventStart(Utf8JsonWriter writer, string type)
    {
        writer.WriteStartObject();
        writer.WriteString("type", type);
        writer.WritePropertyName("value");
    }

    private static byte[] FinishEvent(MemoryStream stream, Utf8JsonWriter writer)
    {
        writer.WriteEndObject();
        writer.Flush();
        return stream.ToArray();
    }

    private enum EventKind
    {
        Visibility,
        PointerPosition,
        AudioSpectrum
    }

    private sealed class EventClient
    {
        private readonly object _lock = new();
        private readonly Channel<byte> _changed = Channel.CreateBounded<byte>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        private byte[]? _visibility;
        private byte[]? _pointerPosition;
        private byte[]? _audioSpectrum;

        public void Publish(EventKind kind, byte[] message)
        {
            lock (_lock)
            {
                switch (kind)
                {
                    case EventKind.Visibility:
                        _visibility = message;
                        break;
                    case EventKind.PointerPosition:
                        _pointerPosition = message;
                        break;
                    case EventKind.AudioSpectrum:
                        _audioSpectrum = message;
                        break;
                }

                _changed.Writer.TryWrite(0);
            }
        }

        public async IAsyncEnumerable<byte[]> ReadAllAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _changed.Reader.ReadAsync(cancellationToken);
                byte[]? visibility;
                byte[]? pointerPosition;
                byte[]? audioSpectrum;
                lock (_lock)
                {
                    visibility = _visibility;
                    pointerPosition = _pointerPosition;
                    audioSpectrum = _audioSpectrum;
                    _visibility = null;
                    _pointerPosition = null;
                    _audioSpectrum = null;
                }

                if (visibility is not null)
                {
                    yield return visibility;
                }
                if (pointerPosition is not null)
                {
                    yield return pointerPosition;
                }
                if (audioSpectrum is not null)
                {
                    yield return audioSpectrum;
                }
            }
        }
    }
}
