namespace KokoroSharp.Adapters;

/// <summary>
/// Common lifecycle and synthesis boundary for local speech model adapters.
/// </summary>
public interface IModelAdapter : IDisposable, IAsyncDisposable
{
    ModelDescriptor Describe();

    IReadOnlyList<VoiceDescriptor> GetVoices(
        string languageCode = null,
        string gender = null);

    ValueTask PrewarmAsync(
        string voiceId,
        CancellationToken cancellationToken = default);

    ValueTask<SynthesisResult> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default);
}
