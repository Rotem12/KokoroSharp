namespace KokoroSharp.Adapters;

/// <summary>
/// Metadata for a voice that can be enumerated without loading its conditioning data.
/// </summary>
public sealed record VoiceDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string LanguageCode { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string ConditioningKey { get; init; } = string.Empty;
    public string SourcePath { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string License { get; init; } = string.Empty;
    public bool IsLoaded { get; init; }
}
