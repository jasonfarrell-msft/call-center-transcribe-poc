namespace CallCenterTranscription.Telephony;

public sealed record AudioFrame
{
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string Encoding { get; init; } = "pcm16";
    public int SampleRateHz { get; init; } = 16000;
    public byte[] Payload { get; init; } = [];
}
