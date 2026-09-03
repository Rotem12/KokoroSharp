namespace KokoroSharp.Core;

/// <summary>
/// Tensor-name options for one-graph Kokoro-compatible ONNX exports.
/// </summary>
/// <remarks>
/// The stock Kokoro export and Thai FastThaiG2P export have the same
/// token/style/speed contract but use different input and duration-output
/// names. Keeping names here makes the standard adapter reusable without
/// weakening the graph-set boundary used by Wayu.
/// </remarks>
public sealed record KokoroGraphOptions
{
    public string TokenInputName { get; init; } = "tokens";
    public string StyleInputName { get; init; } = "style";
    public string SpeedInputName { get; init; } = "speed";
    public string WaveformOutputName { get; init; } = string.Empty;
    public string DurationOutputName { get; init; } = "durations";
    public bool TrimBoundaryTokens { get; init; }
}
