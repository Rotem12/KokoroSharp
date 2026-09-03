namespace KokoroSharp.Adapters;

/// <summary>
/// Stable, catalog-safe metadata for an installed speech model.
/// </summary>
public sealed record ModelDescriptor
{
    public string Id { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string AdapterId { get; init; } = string.Empty;
    public AdapterTier Tier { get; init; }
    public string Revision { get; init; } = string.Empty;
    public IReadOnlyList<string> LanguageCodes { get; init; } = Array.Empty<string>();
    public int SampleRate { get; init; }
    public int Channels { get; init; } = 1;
    public AdapterCapabilities Capabilities { get; init; }
    public string License { get; init; } = string.Empty;
    public string LicenseUrl { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;
    public bool? CommercialUse { get; init; }
    public string ReviewStatus { get; init; } = "unreviewed";
}
