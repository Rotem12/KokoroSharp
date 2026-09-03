namespace KokoroSharp.Adapters;

/// <summary>
/// Frontend output with enough information to report coverage and dropped symbols.
/// </summary>
public sealed record FrontendResult
{
    public string FrontendId { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Phonemes { get; init; } = string.Empty;
    public IReadOnlyList<int> TokenIds { get; init; } = Array.Empty<int>();
    public float Coverage { get; init; } = 1f;
    public IReadOnlyList<string> DroppedSymbols { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
