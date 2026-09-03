namespace KokoroSharp.Core;

using KokoroSharp.Adapters;

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

    /// <summary>
    /// Creates standard one-graph options from a model manifest whose
    /// dictionaries use semantic names as keys and ONNX names as values.
    /// </summary>
    public static KokoroGraphOptions FromManifest(GraphManifest graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var options = new KokoroGraphOptions
        {
            TokenInputName = Require(graph.Inputs, "tokens", graph.Id),
            StyleInputName = Require(graph.Inputs, "style", graph.Id),
            SpeedInputName = Require(graph.Inputs, "speed", graph.Id),
            WaveformOutputName = Optional(graph.Outputs, "waveform"),
            DurationOutputName = Optional(graph.Outputs, "durations"),
            TrimBoundaryTokens = graph.TrimBoundaryTokens
        };
        options.Validate();
        return options;
    }

    /// <summary>Validates the names needed by a standard one-graph call.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TokenInputName))
            throw new InvalidDataException("A standard Kokoro graph requires a token input name.");
        if (string.IsNullOrWhiteSpace(StyleInputName))
            throw new InvalidDataException("A standard Kokoro graph requires a style input name.");
        if (string.IsNullOrWhiteSpace(SpeedInputName))
            throw new InvalidDataException("A standard Kokoro graph requires a speed input name.");
    }

    private static string Require(
        IReadOnlyDictionary<string, string> values,
        string semanticName,
        string graphId)
    {
        if (values is not null &&
            values.TryGetValue(semanticName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
            return value;

        var graphLabel = string.IsNullOrWhiteSpace(graphId) ? "the graph" : $"graph '{graphId}'";
        throw new InvalidDataException($"{graphLabel} manifest is missing the '{semanticName}' input mapping.");
    }

    private static string Optional(
        IReadOnlyDictionary<string, string> values,
        string semanticName)
    {
        return values is not null &&
            values.TryGetValue(semanticName, out var value) &&
            !string.IsNullOrWhiteSpace(value)
            ? value
            : string.Empty;
    }
}
