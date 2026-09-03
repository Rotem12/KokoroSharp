namespace KokoroSharp.Adapters;

public sealed class SynthesisResult
{
    public SynthesisResult(
        AudioBuffer audio,
        FrontendResult frontend = null,
        IReadOnlyList<PhonemeTiming> timings = null,
        AdapterDiagnostics diagnostics = null)
    {
        Audio = audio ?? throw new ArgumentNullException(nameof(audio));
        Frontend = frontend;
        Timings = timings ?? Array.Empty<PhonemeTiming>();
        Diagnostics = diagnostics ?? new AdapterDiagnostics();
    }

    public AudioBuffer Audio { get; }

    public FrontendResult Frontend { get; }

    public IReadOnlyList<PhonemeTiming> Timings { get; }

    public AdapterDiagnostics Diagnostics { get; }
}
