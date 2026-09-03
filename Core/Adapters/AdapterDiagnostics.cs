namespace KokoroSharp.Adapters;

public sealed record AdapterDiagnostics
{
    public string AdapterId { get; init; } = string.Empty;
    public string ModelRevision { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public TimeSpan? InferenceDuration { get; init; }
    public float? RealTimeFactor { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
