namespace KokoroSharp.Adapters;

[Flags]
public enum AdapterCapabilities
{
    None = 0,
    RawText = 1 << 0,
    PrePhonemized = 1 << 1,
    TokenIds = 1 << 2,
    Streaming = 1 << 3,
    Timings = 1 << 4,
    VoiceMixing = 1 << 5,
    GraphSet = 1 << 6
}

public enum AdapterTier
{
    Standard,
    GraphSet,
    ExternalRuntime
}
