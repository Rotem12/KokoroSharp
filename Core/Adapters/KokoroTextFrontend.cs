namespace KokoroSharp.Adapters;

using KokoroSharp.Processing;

/// <summary>
/// The built-in C# frontend for the standard Kokoro vocabulary.
/// </summary>
/// <remarks>
/// This frontend is deliberately separate from the model adapter. A future Thai
/// frontend can implement <see cref="ITextFrontend"/> without changing the
/// standard graph or the application-facing adapter contract.
/// </remarks>
public sealed class KokoroTextFrontend : ITextFrontend
{
    public const string FrontendName = "kokoro-misaki-sharp";

    public string Id => FrontendName;

    public FrontendResult Process(
        string text,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        text ??= string.Empty;
        languageCode = NormalizeLanguageCode(languageCode);
        if (text.Length == 0)
            return Empty(languageCode);

        var phonemes = Tokenizer.Phonemize(text, languageCode);
        var result = FromPhonemes(phonemes, languageCode, cancellationToken);
        if (result.TokenIds.Count == 0)
        {
            return result with
            {
                Coverage = 0f,
                Warnings = [.. result.Warnings, $"The frontend produced no Kokoro phonemes for language '{languageCode}'."]
            };
        }

        return result;
    }

    public FrontendResult FromPhonemes(
        string phonemes,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        languageCode = NormalizeLanguageCode(languageCode);
        phonemes ??= string.Empty;

        var acceptedPhonemes = new List<char>(phonemes.Length);
        var tokenIds = new List<int>(phonemes.Length);
        var droppedSymbols = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phoneme in phonemes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Tokenizer.Vocab.TryGetValue(phoneme, out var tokenId))
            {
                acceptedPhonemes.Add(phoneme);
                tokenIds.Add(tokenId);
            }
            else
            {
                droppedSymbols.Add(phoneme.ToString());
            }
        }

        var coverage = phonemes.Length == 0
            ? 1f
            : acceptedPhonemes.Count / (float) phonemes.Length;
        var warnings = droppedSymbols.Count == 0
            ? Array.Empty<string>()
            : new[] { $"Dropped {droppedSymbols.Count} distinct symbol(s) that are not in the standard Kokoro vocabulary." };

        return new FrontendResult
        {
            FrontendId = Id,
            LanguageCode = languageCode,
            Phonemes = new string([.. acceptedPhonemes]),
            TokenIds = tokenIds,
            Coverage = coverage,
            DroppedSymbols = droppedSymbols.ToArray(),
            Warnings = warnings
        };
    }

    private static FrontendResult Empty(string languageCode) => new()
    {
        FrontendId = FrontendName,
        LanguageCode = languageCode,
        Coverage = 1f
    };

    private static string NormalizeLanguageCode(string languageCode) =>
        string.IsNullOrWhiteSpace(languageCode) ? "en-us" : languageCode.Trim().ToLowerInvariant();
}
