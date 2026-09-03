namespace KokoroSharp.Adapters;

/// <summary>
/// A synthesis request. Exactly one of Text, PrePhonemes, or TokenIds is supplied.
/// </summary>
public sealed record SynthesisRequest
{
    public string Text { get; init; } = string.Empty;
    public string PrePhonemes { get; init; } = string.Empty;
    public IReadOnlyList<int> TokenIds { get; init; } = Array.Empty<int>();
    public string VoiceId { get; init; } = string.Empty;
    public float Speed { get; init; } = 1f;
    public int? RequestedSampleRate { get; init; }

    /// <summary>
    /// Minimum frontend coverage required before inference is allowed. Keep
    /// this at zero for legacy best-effort behavior; set it to 1 for a
    /// fail-closed catalog path that must not synthesize dropped text.
    /// </summary>
    public float MinimumFrontendCoverage { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(VoiceId))
            throw new ArgumentException("A voice id is required.", nameof(VoiceId));
        if (!float.IsFinite(Speed) || Speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(Speed));
        if (!float.IsFinite(MinimumFrontendCoverage) ||
            MinimumFrontendCoverage < 0 ||
            MinimumFrontendCoverage > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumFrontendCoverage));

        int inputKinds = 0;
        if (!string.IsNullOrWhiteSpace(Text))
            inputKinds++;
        if (!string.IsNullOrWhiteSpace(PrePhonemes))
            inputKinds++;
        if (TokenIds != null && TokenIds.Count > 0)
            inputKinds++;
        if (inputKinds != 1)
            throw new ArgumentException("Supply exactly one of Text, PrePhonemes, or TokenIds.");
    }
}
