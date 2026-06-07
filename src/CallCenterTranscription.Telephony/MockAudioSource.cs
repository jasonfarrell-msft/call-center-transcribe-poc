namespace CallCenterTranscription.Telephony;

public sealed class MockAudioSource : IAudioSource
{
    public async IAsyncEnumerable<AudioFrame> ReadAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();

        yield return new AudioFrame
        {
            Encoding = "pcm16",
            SampleRateHz = 16000,
            Payload = [0, 0, 0, 0]
        };
    }
}
