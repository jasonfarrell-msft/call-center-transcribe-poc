namespace CallCenterTranscription.Telephony;

public sealed class MockAudioSource : IAudioSource
{
    // Represents one perpetual session of silence frames (50 fps) until cancellation. Streaming
    // continuously — rather than completing after a single frame — keeps the consumer's per-call
    // outer loop from churning through recognizers in Mock mode.
    public async IAsyncEnumerable<AudioFrame> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            yield return new AudioFrame
            {
                Encoding = "pcm16",
                SampleRateHz = 16000,
                Payload = new byte[640]
            };

            try
            {
                await Task.Delay(20, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
        }
    }
}
