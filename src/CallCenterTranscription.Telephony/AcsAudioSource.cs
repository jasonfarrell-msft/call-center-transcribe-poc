using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CallCenterTranscription.Telephony;

/// <summary>
/// IAudioSource implementation backed by an ACS media-streaming WebSocket.
///
/// Design (POC — single replica, maxReplicas=1 per Athrun's spec):
///   • An internal bounded Channel&lt;AudioFrame&gt; (capacity 1000, DropOldest) buffers frames.
///   • The media-stream WebSocket handler (API layer) calls HandleWebSocketMessageAsync to
///     push decoded PCM frames in.
///   • ReadAsync yields frames via the Channel reader as IAsyncEnumerable&lt;AudioFrame&gt;.
///   • On WebSocket close, CompleteStream() signals end-of-stream to all consumers.
///
/// Active only when AudioSource:Mode = "Acs". The Channel is always instantiated but stays
/// empty when Mode=Mock because no calls are answered in that mode.
///
/// Audio format: PCM 16-bit mono 16,000 Hz — matches IAudioSource contract defaults.
/// Frame rate: 50 fps / 20 ms packets / 640 bytes per frame (ACS default).
///
/// To go live: set AudioSource__Mode=Acs on the ACA Container App env var.
/// Prerequisite: Event Grid subscription + PSTN number + ACS RBAC role on ACA identity.
/// </summary>
public sealed class AcsAudioSource : IAudioSource
{
    private readonly Channel<AudioFrame> _channel;
    private readonly ILogger<AcsAudioSource> _logger;

    public AcsAudioSource(ILogger<AcsAudioSource> logger)
    {
        _logger = logger;
        // Bounded with DropOldest: TryWrite always succeeds; oldest frame is silently dropped
        // if the consumer falls behind. Prevents unbounded memory growth in a dropped-consumer
        // scenario. 1000 frames = ~20 seconds of audio at 50 fps — ample buffer for a single consumer.
        _channel = Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true  // single WebSocket connection → single writer
        });
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AudioFrame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var frame in _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <summary>
    /// Parses a raw ACS media-streaming WebSocket message (JSON text) and — for AudioData
    /// messages — base64-decodes the PCM payload and writes an AudioFrame to the channel.
    ///
    /// ACS message kinds:
    ///   • "AudioMetadata" — stream metadata (sampleRate, channels, etc.); logged, no frame emitted.
    ///   • "AudioData"     — PCM payload (base64); decoded and written to the channel.
    ///
    /// Malformed frames are skipped with a warning — never thrown to the caller.
    /// </summary>
    public ValueTask HandleWebSocketMessageAsync(
        byte[] rawMessage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawMessage);
            var root = doc.RootElement;

            if (!root.TryGetProperty("kind", out var kindProp))
                return ValueTask.CompletedTask;

            var kind = kindProp.GetString();

            // ── AudioMetadata ─────────────────────────────────────────────────────────────────
            if (string.Equals(kind, "AudioMetadata", StringComparison.Ordinal))
            {
                if (root.TryGetProperty("audioMetadata", out var meta))
                {
                    var sampleRate = meta.TryGetProperty("sampleRate", out var sr) ? sr.GetInt32() : 16000;
                    var channels  = meta.TryGetProperty("channels",    out var ch) ? ch.GetInt32() : 1;
                    _logger.LogInformation(
                        "ACS audio stream metadata: sampleRate={SampleRate}Hz channels={Channels}",
                        sampleRate, channels);
                }
                return ValueTask.CompletedTask;
            }

            // ── AudioData ─────────────────────────────────────────────────────────────────────
            if (string.Equals(kind, "AudioData", StringComparison.Ordinal))
            {
                if (!root.TryGetProperty("audioData", out var audioData))
                    return ValueTask.CompletedTask;

                if (!audioData.TryGetProperty("data", out var dataProp))
                    return ValueTask.CompletedTask;

                var base64Data = dataProp.GetString();
                if (string.IsNullOrEmpty(base64Data))
                    return ValueTask.CompletedTask;

                var payload = Convert.FromBase64String(base64Data);

                // Best-effort timestamp from ACS; fall back to UtcNow.
                DateTimeOffset timestamp = DateTimeOffset.UtcNow;
                if (audioData.TryGetProperty("timestamp", out var tsProp))
                {
                    var tsStr = tsProp.GetString();
                    if (!string.IsNullOrEmpty(tsStr) &&
                        DateTimeOffset.TryParse(tsStr, out var parsedTs))
                    {
                        timestamp = parsedTs;
                    }
                }

                var frame = new AudioFrame
                {
                    TimestampUtc = timestamp,
                    Encoding    = "pcm16",
                    SampleRateHz = 16000,
                    Payload     = payload
                };

                // TryWrite with DropOldest never blocks and always returns true (oldest is
                // silently dropped if the channel is at capacity).
                _channel.Writer.TryWrite(frame);

                return ValueTask.CompletedTask;
            }

            _logger.LogDebug("ACS WebSocket message kind '{Kind}' not handled; skipping.", kind);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed JSON in ACS WebSocket message; frame skipped.");
        }
        catch (FormatException ex)
        {
            _logger.LogWarning(ex, "Invalid base64 in ACS AudioData message; frame skipped.");
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Signals end-of-stream to all ReadAsync consumers. Call this when the WebSocket
    /// connection closes (normally or abnormally). After this call, ReadAsync drains any
    /// remaining buffered frames and then completes without blocking.
    ///
    /// NOTE: No reconnect logic is implemented for the POC. A dropped stream = restart the call.
    /// This is a known limitation; document before going live.
    /// </summary>
    public void CompleteStream()
    {
        _channel.Writer.TryComplete();
        _logger.LogWarning("ACS audio stream completed — WebSocket closed. No reconnect (POC limitation).");
    }
}
