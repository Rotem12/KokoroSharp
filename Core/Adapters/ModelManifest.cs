namespace KokoroSharp.Adapters;

/// <summary>
/// Serializable install and runtime contract for one model variant.
/// </summary>
public sealed class ModelManifest
{
    public string Id { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string AdapterId { get; init; } = string.Empty;
    public AdapterTier Tier { get; init; }
    public string Revision { get; init; } = string.Empty;
    public AdapterCapabilities Capabilities { get; init; }
    public List<string> LanguageCodes { get; init; } = new();
    public AudioManifest Audio { get; init; } = new();
    public FrontendManifest Frontend { get; init; } = new();
    public VoicePackManifest VoicePack { get; init; } = new();
    public LicenseManifest License { get; init; } = new();
    public List<GraphManifest> Graphs { get; init; } = new();
    public List<ArtifactManifest> Files { get; init; } = new();
}

public sealed class AudioManifest
{
    public int SampleRate { get; init; }
    public int Channels { get; init; } = 1;
    public string SampleFormat { get; init; } = "float32";
    public bool StripBosEos { get; init; }
    public int FrameHopSamples { get; init; }
}

public sealed class FrontendManifest
{
    public string Id { get; init; } = string.Empty;
    public string VocabularyPath { get; init; } = string.Empty;
    public string TokenIdType { get; init; } = "int64";
    public bool RequiresWordSegmentation { get; init; }
    public bool SupportsPrePhonemes { get; init; }
}

public sealed class VoicePackManifest
{
    public string Format { get; init; } = string.Empty;
    public int[] Shape { get; init; } = Array.Empty<int>();
    public string Conditioning { get; init; } = string.Empty;
    public string StyleIndexPolicy { get; init; } = string.Empty;
}

public sealed class LicenseManifest
{
    public string Name { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Attribution { get; init; } = string.Empty;
    public bool? CommercialUse { get; init; }
    public string ReviewStatus { get; init; } = "unreviewed";
}

public sealed class GraphManifest
{
    public string Id { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public int MaxTokens { get; init; }
    public Dictionary<string, string> Inputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Outputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DerivedInputs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ArtifactManifest
{
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public string LicenseRole { get; init; } = string.Empty;
}
