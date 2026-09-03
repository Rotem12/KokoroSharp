namespace KokoroSharp.Adapters;

/// <summary>
/// Model-specific text frontend. Each model owns its normalization and vocabulary rules.
/// </summary>
public interface ITextFrontend
{
    string Id { get; }

    FrontendResult Process(
        string text,
        string languageCode,
        CancellationToken cancellationToken = default);

    FrontendResult FromPhonemes(
        string phonemes,
        string languageCode,
        CancellationToken cancellationToken = default);
}
