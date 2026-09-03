namespace KokoroSharp.Adapters;

/// <summary>
/// Native-rate floating-point audio returned by an adapter.
/// </summary>
public sealed class AudioBuffer
{
    public AudioBuffer(ReadOnlyMemory<float> samples, int sampleRate, int channels = 1)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(channels));

        Samples = samples;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public ReadOnlyMemory<float> Samples { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public TimeSpan Duration =>
        TimeSpan.FromSeconds(Samples.Length / (double) (SampleRate * Channels));
}
