using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace CallCenterTranscription.Telephony;

/// <summary>
/// IAudioSource implementation backed by ACS media-streaming WebSocket calls.
///
/// Design (POC — single replica, maxReplicas=1 per Athrun's spec):
///   • Each call is a SESSION. <see cref="BeginSession"/> (called by the media-stream WebSocket
///     handler when a call connects) allocates a fresh bounded Channel&lt;AudioFrame&gt; and
///     enqueues its reader on an internal sessions queue.
///   • <see cref="HandleWebSocketMessageAsync"/> decodes PCM frames and writes them to the
///     CURRENT session's channel.
///   • <see cref="ReadAsync"/> dequeues the next session and yields its frames until the call
///     ends — then completes. The consumer (SpeechTranscriptionService) loops, calling ReadAsync
///     once per call, building a fresh recognizer each time and idling between calls.
///   • <see cref="CompleteStream"/> completes the CURRENT session's channel on WebSocket close.
///
/// This per-call model fixes the prior single-shot bug where completing one shared channel
/// permanently shut the transcription pipeline down after the first call.
///
/// Active only when AudioSource:Mode = "Acs".
///
/// Audio format: PCM 16-bit mono 16,000 Hz — matches IAudioSource contract defaults.
/// Frame rate: 50 fps / 20 ms packets / 640 bytes per frame (ACS default).
/// </summary>
public sealed class AcsAudioSource : IAudioSource
{
    // Queue of per-call session readers. Unbounded: a session is enqueued per call; the single
    // consumer drains them one at a time and idles here between calls.
    private readonly Channel<ChannelReader<AudioFrame>> _sessions =
        Channel.CreateUnbounded<ChannelReader<AudioFrame>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly ILogger<AcsAudioSource> _logger;

    // Tracks the most recently started session so CallDisconnected callbacks can complete
    // it without needing the session reference that lives inside the WebSocket handler.
    private volatile Session? _currentSession;

    public sealed class Session
    {
        internal Session(Channel<AudioFrame> channel) => Channel = channel;
        internal Channel<AudioFrame> Channel { get; }
        internal long FrameCount;
        internal long SilentFrameCount;
        internal long RepTaggedFrameCount;
    }

    public AcsAudioSource(ILogger<AcsAudioSource> logger)
    {
        _logger = logger;
    }

    // Bounded with DropOldest: TryWrite always succeeds; oldest frame is silently dropped if the
    // consumer falls behind. 1000 frames ≈ 20 s of audio at 50 fps — ample for a single consumer.
    private static Channel<AudioFrame> CreateFrameChannel() =>
        Channel.CreateBounded<AudioFrame>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    /// <summary>
    /// Starts a new per-call audio session. Called by the media-stream WebSocket handler when a
    /// call connects, BEFORE any frames arrive. Allocates a fresh frame channel and enqueues its
    /// reader for the consumer.
    /// </summary>
    public Session BeginSession()
    {
        var channel = CreateFrameChannel();
        var session = new Session(channel);
        _currentSession = session; // volatile write — visible to ForceCompleteCurrentSession
        _sessions.Writer.TryWrite(channel.Reader);
        _logger.LogInformation("AcsAudioSource: new audio session started.");
        return session;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Yields frames for the NEXT call session, completing when that call ends. Blocks (idle)
    /// between calls. Designed to be called repeatedly — once per call — by the consumer loop.
    /// </remarks>
    public async IAsyncEnumerable<AudioFrame> ReadAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var reader = await _sessions.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var frame in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <summary>
    /// Parses a raw ACS media-streaming WebSocket message (JSON text) and — for AudioData
    /// messages — base64-decodes the PCM payload and writes an AudioFrame to the current session.
    ///
    /// ACS message kinds:
    ///   • "AudioMetadata" — stream metadata (sampleRate, channels, etc.); logged, no frame emitted.
    ///   • "AudioData"     — PCM payload (base64) with a "silent" flag; decoded and written.
    ///
    /// Malformed frames are skipped with a warning — never thrown to the caller.
    /// </summary>
    public ValueTask HandleWebSocketMessageAsync(
        Session session,
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

                // Some ACS payloads include participant raw ids ("8:" CommunicationUser).
                // Track these for diagnostics, but do not filter frames here; filtering at this
                // layer can starve recognition input when stream topology differs from assumptions.
                if (TryGetParticipantRawId(audioData, out var participantRawId) &&
                    participantRawId.StartsWith("8:", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref session.RepTaggedFrameCount);
                }

                if (!audioData.TryGetProperty("data", out var dataProp))
                    return ValueTask.CompletedTask;

                var base64Data = dataProp.GetString();
                if (string.IsNullOrEmpty(base64Data))
                    return ValueTask.CompletedTask;

                var payload = Convert.FromBase64String(base64Data);

                // ACS marks frames with no speaker audio as silent. Tracking these separately is
                // the key diagnostic: "all frames silent" ⇒ audio routing problem (caller not in
                // the mixed stream); "non-silent frames but no transcript" ⇒ Speech/auth problem.
                var silent = audioData.TryGetProperty("silent", out var silentProp)
                             && silentProp.ValueKind == JsonValueKind.True;

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

                // TryWrite with DropOldest never blocks and always returns true. Null-safe if a
                // frame somehow arrives before BeginSession (frame is dropped).
                session.Channel.Writer.TryWrite(frame);

                var count = Interlocked.Increment(ref session.FrameCount);
                if (silent)
                    Interlocked.Increment(ref session.SilentFrameCount);

                // Diagnostic logging: first frame, then every ~5 s (250 frames @ 50 fps).
                if (count == 1)
                {
                    _logger.LogInformation(
                        "AcsAudioSource: first AudioData frame received ({Bytes} bytes, silent={Silent}).",
                        payload.Length, silent);
                }
                else if (count % 250 == 0)
                {
                    var silentSoFar = Interlocked.Read(ref session.SilentFrameCount);
                    _logger.LogInformation(
                        "AcsAudioSource: {Count} AudioData frames received ({Silent} silent).",
                        count, silentSoFar);
                }

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
    /// Completes the CURRENT session's channel, signalling end-of-call to the consumer's ReadAsync
    /// loop. Call this when the call's WebSocket closes. The transcription pipeline stays alive and
    /// idles for the next call (no permanent shutdown).
    /// </summary>
    public void CompleteStream(Session session)
    {
        _currentSession = null; // volatile write — clear before completing so ForceComplete is a no-op
        session.Channel.Writer.TryComplete();

        var total  = Interlocked.Read(ref session.FrameCount);
        var silent = Interlocked.Read(ref session.SilentFrameCount);
        var rep    = Interlocked.Read(ref session.RepTaggedFrameCount);
        _logger.LogInformation(
            "AcsAudioSource: audio session completed — {Total} AudioData frames received " +
            "({Silent} silent, {NonSilent} with audio); {RepTagged} frames tagged as CommunicationUser.",
            total, silent, total - silent, rep);
    }

    /// <summary>
    /// Completes the current session's channel without holding a session reference — for use by
    /// ACS callback handlers (e.g., <c>CallDisconnected</c>) that need to signal end-of-audio but
    /// do not have access to the <see cref="Session"/> object that lives inside the WebSocket
    /// handler. Safe to call concurrently with <see cref="CompleteStream"/>; <c>TryComplete</c> is
    /// idempotent. No-op if no session is currently active (e.g., Mock mode).
    /// </summary>
    public void ForceCompleteCurrentSession()
    {
        var session = _currentSession; // volatile read
        if (session is not null)
            CompleteStream(session);
    }

    /// <summary>
    /// Reads the source participant raw id from an unmixed AudioData payload. ACS has historically
    /// used both "participantRawID" and "participantRawId" casings, so we try both.
    /// </summary>
    private static bool TryGetParticipantRawId(JsonElement audioData, out string rawId)
    {
        if (audioData.TryGetProperty("participantRawID", out var a) && a.ValueKind == JsonValueKind.String)
        {
            rawId = a.GetString() ?? string.Empty;
            return rawId.Length > 0;
        }

        if (audioData.TryGetProperty("participantRawId", out var b) && b.ValueKind == JsonValueKind.String)
        {
            rawId = b.GetString() ?? string.Empty;
            return rawId.Length > 0;
        }

        rawId = string.Empty;
        return false;
    }
}
