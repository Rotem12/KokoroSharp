namespace KokoroSharp.Adapters;

/// <summary>
/// Raised when a caller requested fail-closed frontend coverage and the
/// selected frontend could not represent all of the input.
/// </summary>
public sealed class FrontendCoverageException : InvalidOperationException
{
    public FrontendCoverageException(
        FrontendResult frontend,
        float minimumCoverage)
        : base(CreateMessage(frontend, minimumCoverage))
    {
        Frontend = frontend ?? throw new ArgumentNullException(nameof(frontend));
        MinimumCoverage = minimumCoverage;
    }

    public FrontendResult Frontend { get; }

    public float MinimumCoverage { get; }

    private static string CreateMessage(FrontendResult frontend, float minimumCoverage)
    {
        var frontendId = string.IsNullOrWhiteSpace(frontend?.FrontendId)
            ? "the selected frontend"
            : $"frontend '{frontend.FrontendId}'";
        var dropped = frontend?.DroppedSymbols is { Count: > 0 }
            ? $" Dropped: {string.Join(", ", frontend.DroppedSymbols.Take(8))}."
            : string.Empty;
        return $"{frontendId} covered {frontend?.Coverage:P2} of the input, below the required {minimumCoverage:P2}.{dropped}";
    }
}

internal static class FrontendCoverageGuard
{
    public static void Ensure(FrontendResult frontend, float minimumCoverage)
    {
        if (frontend is null || minimumCoverage <= 0 || frontend.Coverage >= minimumCoverage)
            return;

        throw new FrontendCoverageException(frontend, minimumCoverage);
    }
}
