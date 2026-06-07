namespace CallCenterTranscription.Telephony;

public interface IAudioSource
{
    IAsyncEnumerable<AudioFrame> ReadAsync(CancellationToken cancellationToken = default);
}
