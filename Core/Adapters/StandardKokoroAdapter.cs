namespace KokoroSharp.Adapters;

using KokoroSharp.Core;
using KokoroSharp.Processing;

using Microsoft.ML.OnnxRuntime;

using System.Diagnostics;

/// <summary>
/// Universal adapter for the standard Kokoro one-graph contract.
/// </summary>
/// <remarks>
/// This is intentionally a compatibility layer over <see cref="KokoroModel"/>
/// rather than a replacement for KokoroSharp's existing high-level APIs. It
/// covers the current [tokens, style, speed] graph and the standard [510, 1,
/// 256] NumPy voice pack. Models with different graph topology or conditioning
/// semantics must use another adapter tier.
/// </remarks>
public sealed class StandardKokoroAdapter : IModelAdapter
{
    private const int NativeSampleRate = 24_000;
    private static readonly int[] WarmupTokens = [4];

    private readonly KokoroModel model;
    private readonly string voicesPath;
    private readonly ITextFrontend frontend;
    private readonly KokoroGraphOptions graphOptions;
    private readonly ModelDescriptor descriptor;
    private readonly IReadOnlyList<VoiceDescriptor> voiceDescriptors;
    private readonly Dictionary<string, KokoroVoice> loadedVoices = new(StringComparer.OrdinalIgnoreCase);
    private readonly object voiceGate = new();
    private bool isDisposed;

    public StandardKokoroAdapter(
        string modelPath,
        string voicesPath = null,
        ModelDescriptor descriptor = null,
        IEnumerable<VoiceDescriptor> voices = null,
        ITextFrontend frontend = null,
        SessionOptions options = null,
        KokoroGraphOptions graphOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (!File.Exists(modelPath))
            throw new FileNotFoundException("The Kokoro ONNX model was not found.", modelPath);

        this.voicesPath = Path.GetFullPath(voicesPath ?? Path.Combine(AppContext.BaseDirectory, "voices"));
        this.frontend = frontend ?? new KokoroTextFrontend();
        this.graphOptions = graphOptions ?? new KokoroGraphOptions();
        this.descriptor = descriptor ?? CreateDefaultDescriptor();
        ValidateDescriptor(this.descriptor);
        voiceDescriptors = (voices ?? CreateStockVoiceDescriptors(this.voicesPath)).ToArray();
        ValidateVoices(voiceDescriptors);
        model = new KokoroModel(modelPath, options, this.graphOptions);
    }

    public ModelDescriptor Describe() => descriptor;

    public IReadOnlyList<VoiceDescriptor> GetVoices(
        string languageCode = null,
        string gender = null)
    {
        EnsureNotDisposed();
        var normalizedLanguage = string.IsNullOrWhiteSpace(languageCode) ? null : languageCode.Trim().ToLowerInvariant();
        var normalizedGender = NormalizeGender(gender);

        return voiceDescriptors
            .Where(voice => normalizedLanguage == null || string.Equals(voice.LanguageCode, normalizedLanguage, StringComparison.OrdinalIgnoreCase))
            .Where(voice => normalizedGender == null || string.Equals(NormalizeGender(voice.Gender), normalizedGender, StringComparison.OrdinalIgnoreCase))
            .Select(voice => voice with { IsLoaded = IsVoiceLoaded(voice.Id) })
            .ToArray();
    }

    public async ValueTask PrewarmAsync(
        string voiceId,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var voice = LoadVoice(voiceId);
            model.Prewarm(WarmupTokens, voice.Features);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SynthesisResult> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        EnsureNotDisposed();

        return await Task.Run(() => SynthesizeCore(request, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        lock (voiceGate)
            loadedVoices.Clear();
        model.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private SynthesisResult SynthesizeCore(SynthesisRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var voiceDescriptor = FindVoice(request.VoiceId);
        var voice = LoadVoice(voiceDescriptor.Id);
        var languageCode = voiceDescriptor.LanguageCode;

        FrontendResult frontendResult;
        int[] tokenIds;
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            frontendResult = frontend.Process(request.Text, languageCode, cancellationToken);
            tokenIds = [.. frontendResult.TokenIds];
        }
        else if (!string.IsNullOrWhiteSpace(request.PrePhonemes))
        {
            frontendResult = frontend.FromPhonemes(request.PrePhonemes, languageCode, cancellationToken);
            tokenIds = [.. frontendResult.TokenIds];
        }
        else
        {
            tokenIds = request.TokenIds?.ToArray() ?? [];
            frontendResult = new FrontendResult
            {
                FrontendId = "provided-token-ids",
                LanguageCode = languageCode,
                TokenIds = tokenIds,
                Coverage = 1f
            };
        }

        if (request.RequestedSampleRate is { } requestedSampleRate && requestedSampleRate != descriptor.SampleRate)
            throw new NotSupportedException($"The standard Kokoro adapter returns native {descriptor.SampleRate} Hz audio; resampling belongs at the host output boundary.");

        if (tokenIds.Length == 0)
        {
            return new SynthesisResult(
                new AudioBuffer(ReadOnlyMemory<float>.Empty, descriptor.SampleRate, descriptor.Channels),
                frontendResult,
                diagnostics: CreateDiagnostics(TimeSpan.Zero));
        }

        var stopwatch = Stopwatch.StartNew();
        var samples = new List<float>();
        var timings = new List<PhonemeTiming>();
        foreach (var segment in SplitIntoSafeSegments(tokenIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rawSegmentSamples = model.Infer(segment, voice.Features, request.Speed, out var paddedDurations);
            var segmentSamples = rawSegmentSamples;
            var trimStartSamples = 0;
            if (graphOptions.TrimBoundaryTokens)
            {
                segmentSamples = KokoroBoundaryTrimmer.Trim(
                    segmentSamples,
                    paddedDurations,
                    descriptor.SampleRate,
                    out trimStartSamples);
            }
            var segmentStartSecond = samples.Count / (double) descriptor.SampleRate;
            samples.AddRange(segmentSamples);

            var segmentTimings = PhonemeTimestamp.FromModelOutput(segment, paddedDurations, rawSegmentSamples.Length);
            foreach (var timing in segmentTimings ?? [])
            {
                var trimOffsetSecond = trimStartSamples / (double) descriptor.SampleRate;
                timings.Add(new PhonemeTiming(
                    timing.Phoneme.ToString(),
                    segmentStartSecond + Math.Max(0, timing.StartSecond - trimOffsetSecond),
                    segmentStartSecond + Math.Max(0, timing.EndSecond - trimOffsetSecond)));
            }
        }
        stopwatch.Stop();

        return new SynthesisResult(
            new AudioBuffer(samples.ToArray(), descriptor.SampleRate, descriptor.Channels),
            frontendResult,
            timings,
            CreateDiagnostics(stopwatch.Elapsed));
    }

    private KokoroVoice LoadVoice(string voiceId)
    {
        EnsureNotDisposed();
        var voiceDescriptor = FindVoice(voiceId);
        lock (voiceGate)
        {
            if (loadedVoices.TryGetValue(voiceDescriptor.Id, out var loaded))
                return loaded;

            var sourcePath = voiceDescriptor.SourcePath;
            if (string.IsNullOrWhiteSpace(sourcePath))
                sourcePath = Path.Combine(voicesPath, $"{voiceDescriptor.Id}.npy");
            else if (!Path.IsPathRooted(sourcePath))
                sourcePath = Path.Combine(voicesPath, sourcePath);
            sourcePath = Path.GetFullPath(sourcePath);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"The Kokoro voice '{voiceDescriptor.Id}' was not found.", sourcePath);

            loaded = KokoroVoice.FromPath(sourcePath);
            loadedVoices.Add(voiceDescriptor.Id, loaded);
            return loaded;
        }
    }

    private VoiceDescriptor FindVoice(string voiceId) =>
        voiceDescriptors.FirstOrDefault(voice => string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"The Kokoro voice '{voiceId}' is not present in this adapter's catalog.");

    private bool IsVoiceLoaded(string voiceId)
    {
        lock (voiceGate)
            return loadedVoices.ContainsKey(voiceId);
    }

    private AdapterDiagnostics CreateDiagnostics(TimeSpan inferenceDuration) => new()
    {
        AdapterId = descriptor.AdapterId,
        ModelRevision = descriptor.Revision,
        Provider = "Microsoft.ML.OnnxRuntime",
        InferenceDuration = inferenceDuration,
        RealTimeFactor = null
    };

    private static IEnumerable<int[]> SplitIntoSafeSegments(IReadOnlyList<int> tokenIds)
    {
        for (var offset = 0; offset < tokenIds.Count; offset += KokoroModel.maxTokens)
        {
            var length = Math.Min(KokoroModel.maxTokens, tokenIds.Count - offset);
            var segment = new int[length];
            for (var i = 0; i < length; i++)
                segment[i] = tokenIds[offset + i];
            yield return segment;
        }
    }

    private static IReadOnlyList<VoiceDescriptor> CreateStockVoiceDescriptors(string voicesPath) =>
        KokoroVoiceCatalog.Voices
            .Select(voice => new VoiceDescriptor
            {
                Id = voice.Name,
                DisplayName = voice.Name,
                LanguageCode = voice.Language.GetLangCode(),
                Gender = voice.Gender == KokoroGender.Male ? "male" : "female",
                ConditioningKey = voice.Name,
                SourcePath = Path.Combine(voicesPath, $"{voice.Name}.npy")
            })
            .ToArray();

    private static ModelDescriptor CreateDefaultDescriptor() => new()
    {
        Id = "kokoro-standard",
        Family = "Kokoro",
        AdapterId = "kokoro-standard",
        Tier = AdapterTier.Standard,
        Revision = "local",
        LanguageCodes = ["en-us", "en-gb", "ja", "cmn", "es", "fr", "hi", "it", "pt-br"],
        SampleRate = NativeSampleRate,
        Channels = 1,
        Capabilities = AdapterCapabilities.RawText | AdapterCapabilities.PrePhonemized | AdapterCapabilities.TokenIds | AdapterCapabilities.Timings
    };

    private static void ValidateDescriptor(ModelDescriptor value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Family);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AdapterId);
        if (value.SampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "The model sample rate must be positive.");
        if (value.Channels <= 0)
            throw new ArgumentOutOfRangeException(nameof(value), "The model channel count must be positive.");
    }

    private static void ValidateVoices(IReadOnlyList<VoiceDescriptor> values)
    {
        if (values.Count == 0)
            throw new ArgumentException("At least one voice descriptor is required.", nameof(values));
        if (values.Any(voice => string.IsNullOrWhiteSpace(voice.Id)))
            throw new ArgumentException("Every voice descriptor must have an id.", nameof(values));
        if (values.Select(voice => voice.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new ArgumentException("Voice descriptor ids must be unique.", nameof(values));
    }

    private static string NormalizeGender(string gender)
    {
        if (string.IsNullOrWhiteSpace(gender))
            return null;
        return gender.Trim().ToLowerInvariant() switch
        {
            "m" or "male" => "male",
            "f" or "female" => "female",
            _ => gender.Trim().ToLowerInvariant()
        };
    }

    private void EnsureNotDisposed()
    {
        if (isDisposed)
            throw new ObjectDisposedException(nameof(StandardKokoroAdapter));
    }
}
