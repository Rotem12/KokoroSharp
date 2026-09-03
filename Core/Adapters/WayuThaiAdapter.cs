namespace KokoroSharp.Adapters;

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Adapter for the pinned Wayu Thai three-graph Kokoro bundle.
/// </summary>
/// <remarks>
/// Wayu is intentionally a graph-set adapter. It cannot be represented by the
/// standard Kokoro [tokens, style, speed] call because it requires a prosody
/// pass, host-side duration expansion, a curves pass, a deterministic harmonic
/// source/STFT input, and a decoder pass. This first library slice exposes the
/// proven pre-phonemized/token-id route; raw Thai text is enabled only when a
/// model-matched <see cref="ITextFrontend"/> is supplied.
/// </remarks>
public sealed class WayuThaiAdapter : IModelAdapter
{
    private const int NativeSampleRate = 24_000;
    private const int MaxTokens = 510;
    private const int StyleWidth = 256;
    private const int StyleSplit = 128;
    private const int ProsodyChannels = 640;
    private const int AsrChannels = 512;
    private const int Harmonics = 9;
    private const int UpsampleScale = 300;
    private const int FftSize = 20;
    private const int StftHop = 5;
    private const float VoicedThreshold = 10f;
    private const float SineAmplitude = 0.1f;
    private const float NoiseStandardDeviation = 0.003f;
    private const int DefaultSeed = 1234;

    private readonly string modelDirectory;
    private readonly string voicePackPath;
    private readonly Dictionary<string, int> vocabulary;
    private readonly IReadOnlyList<VoiceDescriptor> voiceDescriptors;
    private readonly ITextFrontend frontend;
    private readonly ModelDescriptor descriptor;
    private readonly InferenceSession prosody;
    private readonly InferenceSession curves;
    private readonly InferenceSession decoder;
    private readonly float[] sourceWeight;
    private readonly float[] sourceBias;
    private readonly float[] analysisWindow;
    private readonly Dictionary<string, float[]> styleCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object styleGate = new();
    private readonly SemaphoreSlim inferenceGate = new(1, 1);
    private readonly SessionOptions sessionOptions;
    private readonly bool ownsSessionOptions;
    private bool isDisposed;

    public WayuThaiAdapter(
        string modelDirectory,
        ModelDescriptor descriptor = null,
        IEnumerable<VoiceDescriptor> voices = null,
        ITextFrontend frontend = null,
        SessionOptions options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);
        if (!Directory.Exists(modelDirectory))
            throw new DirectoryNotFoundException(modelDirectory);

        this.modelDirectory = Path.GetFullPath(modelDirectory);
        this.frontend = frontend;
        this.descriptor = descriptor ?? CreateDefaultDescriptor();
        ValidateDescriptor(this.descriptor);
        voicePackPath = RequireFile("voicepacks.npz");
        vocabulary = LoadVocabulary(RequireFile("onnx_manifest.json"));
        ValidateVocabulary(vocabulary);

        var sourceParametersPath = RequireFile("source_params.npz");
        sourceWeight = NpzTensorReader.ReadFloat32(sourceParametersPath, "weight").Values;
        sourceBias = NpzTensorReader.ReadFloat32(sourceParametersPath, "bias").Values;
        analysisWindow = NpzTensorReader.ReadFloat32(sourceParametersPath, "window").Values;
        ValidateSourceParameters(sourceWeight, sourceBias, analysisWindow);
        voiceDescriptors = (voices ?? CreateVoiceDescriptors(voicePackPath)).ToArray();
        ValidateVoices(voiceDescriptors);

        var ownOptions = options is null;
        var localOptions = options ?? CreateSessionOptions();
        InferenceSession localProsody = null;
        InferenceSession localCurves = null;
        InferenceSession localDecoder = null;
        try
        {
            localProsody = new InferenceSession(RequireFile("prosody_fp32.onnx"), localOptions);
            localCurves = new InferenceSession(RequireFile("curves_fp32.onnx"), localOptions);
            localDecoder = new InferenceSession(RequireFile("decoder_fp32.onnx"), localOptions);
            ValidateGraphContract(localProsody, "prosody", ["input_ids", "style_dur", "speed"], ["pred_dur", "d", "t_en"]);
            ValidateGraphContract(localCurves, "curves", ["en", "style_dur"], ["f0_curve", "n_curve"]);
            ValidateGraphContract(localDecoder, "decoder", ["asr", "f0_curve", "n_curve", "style_acou", "har"], ["audio"]);
        }
        catch
        {
            localProsody?.Dispose();
            localCurves?.Dispose();
            localDecoder?.Dispose();
            if (ownOptions)
                localOptions.Dispose();
            throw;
        }

        prosody = localProsody;
        curves = localCurves;
        decoder = localDecoder;
        sessionOptions = localOptions;
        ownsSessionOptions = ownOptions;
    }

    public ModelDescriptor Describe() => descriptor;

    public IReadOnlyList<VoiceDescriptor> GetVoices(
        string languageCode = null,
        string gender = null)
    {
        EnsureNotDisposed();
        var language = string.IsNullOrWhiteSpace(languageCode) ? null : languageCode.Trim();
        var normalizedGender = NormalizeGender(gender);
        return voiceDescriptors
            .Where(voice => language is null || string.Equals(voice.LanguageCode, language, StringComparison.OrdinalIgnoreCase))
            .Where(voice => normalizedGender is null || string.Equals(NormalizeGender(voice.Gender), normalizedGender, StringComparison.OrdinalIgnoreCase))
            .Select(voice => voice with { IsLoaded = IsStyleLoaded(voice.ConditioningKey) })
            .ToArray();
    }

    public async ValueTask PrewarmAsync(
        string voiceId,
        CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(voiceId);
        await SynthesizeAsync(
            new SynthesisRequest
            {
                VoiceId = voiceId,
                PrePhonemes = "a",
                Speed = 1f
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SynthesisResult> SynthesizeAsync(
        SynthesisRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();
        EnsureNotDisposed();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            inferenceGate.Wait(cancellationToken);
            try
            {
                return SynthesizeCore(request, cancellationToken);
            }
            finally
            {
                inferenceGate.Release();
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (isDisposed)
            return;

        isDisposed = true;
        inferenceGate.Dispose();
        prosody.Dispose();
        curves.Dispose();
        decoder.Dispose();
        if (ownsSessionOptions)
            sessionOptions.Dispose();
        lock (styleGate)
            styleCache.Clear();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private SynthesisResult SynthesizeCore(SynthesisRequest request, CancellationToken cancellationToken)
    {
        var voice = FindVoice(request.VoiceId);
        var (tokenIds, frontendResult) = ResolveInput(request, voice, cancellationToken);
        ValidateTokenIds(tokenIds);

        if (request.RequestedSampleRate is { } requestedSampleRate && requestedSampleRate != descriptor.SampleRate)
            throw new NotSupportedException($"The Wayu adapter returns native {descriptor.SampleRate} Hz audio; resampling belongs at the host output boundary.");

        if (tokenIds.Length == 0)
        {
            return new SynthesisResult(
                new AudioBuffer(ReadOnlyMemory<float>.Empty, descriptor.SampleRate, descriptor.Channels),
                frontendResult,
                diagnostics: new AdapterDiagnostics
                {
                    AdapterId = descriptor.AdapterId,
                    ModelRevision = descriptor.Revision,
                    Provider = "Microsoft.ML.OnnxRuntime"
                });
        }

        var stopwatch = Stopwatch.StartNew();
        var samples = new List<float>();
        var timings = new List<PhonemeTiming>();
        var segmentIndex = 0;
        foreach (var segment in SplitIntoSafeSegments(tokenIds))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var style = LoadStyle(voice.ConditioningKey, segment.Length);
            var styleDur = style[StyleSplit..StyleWidth];
            var styleAcou = style[..StyleSplit];
            var intermediate = RunProsody(segment, styleDur, request.Speed, cancellationToken);
            if (intermediate.Frames == 0)
            {
                segmentIndex++;
                continue;
            }

            var curve = RunCurves(intermediate.En, intermediate.Frames, styleDur, cancellationToken);
            var harmonic = BuildHarmonicSource(curve.F0, cancellationToken, DefaultSeed + segmentIndex);
            var audio = RunDecoder(
                intermediate.Asr,
                intermediate.Frames,
                curve.F0,
                curve.Noise,
                styleAcou,
                harmonic,
                cancellationToken);

            var segmentStartSecond = samples.Count / (double) descriptor.SampleRate;
            samples.AddRange(audio);
            AddTimings(timings, segment, intermediate.Durations, audio.Length, segmentStartSecond);
            segmentIndex++;
        }
        stopwatch.Stop();

        var audioSeconds = samples.Count / (double) descriptor.SampleRate;
        return new SynthesisResult(
            new AudioBuffer(samples.ToArray(), descriptor.SampleRate, descriptor.Channels),
            frontendResult,
            timings,
            new AdapterDiagnostics
            {
                AdapterId = descriptor.AdapterId,
                ModelRevision = descriptor.Revision,
                Provider = "Microsoft.ML.OnnxRuntime",
                InferenceDuration = stopwatch.Elapsed,
                RealTimeFactor = audioSeconds > 0 ? (float) (stopwatch.Elapsed.TotalSeconds / audioSeconds) : null,
                Warnings = frontendResult?.Warnings ?? Array.Empty<string>()
            });
    }

    private (int[] TokenIds, FrontendResult Frontend) ResolveInput(
        SynthesisRequest request,
        VoiceDescriptor voice,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            if (frontend is null)
                throw new NotSupportedException("Raw Thai text requires a model-matched native ITextFrontend; use PrePhonemes until it is supplied.");
            var result = frontend.Process(request.Text, voice.LanguageCode, cancellationToken);
            return ([.. result.TokenIds], result);
        }

        if (!string.IsNullOrWhiteSpace(request.PrePhonemes))
        {
            if (frontend is not null)
            {
                var result = frontend.FromPhonemes(request.PrePhonemes, voice.LanguageCode, cancellationToken);
                return ([.. result.TokenIds], result);
            }
            return EncodePhonemes(request.PrePhonemes, voice.LanguageCode);
        }

        var tokenIds = request.TokenIds?.ToArray() ?? [];
        return (
            tokenIds,
            new FrontendResult
            {
                FrontendId = "provided-token-ids",
                LanguageCode = voice.LanguageCode,
                TokenIds = tokenIds,
                Coverage = 1f
            });
    }

    private (int[] TokenIds, FrontendResult Frontend) EncodePhonemes(string phonemes, string languageCode)
    {
        var accepted = new List<char>(phonemes.Length);
        var ids = new List<int>(phonemes.Length);
        var dropped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phoneme in phonemes)
        {
            if (vocabulary.TryGetValue(phoneme.ToString(), out var id))
            {
                accepted.Add(phoneme);
                ids.Add(id);
            }
            else
            {
                dropped.Add(phoneme.ToString());
            }
        }

        var coverage = phonemes.Length == 0 ? 1f : accepted.Count / (float) phonemes.Length;
        var warnings = dropped.Count == 0
            ? Array.Empty<string>()
            : new[] { $"Dropped {dropped.Count} distinct symbol(s) that are not in the Wayu Thai vocabulary." };
        var frontendResult = new FrontendResult
        {
            FrontendId = "wayu-vocab-only",
            LanguageCode = languageCode,
            Phonemes = new string([.. accepted]),
            TokenIds = ids,
            Coverage = coverage,
            DroppedSymbols = dropped.ToArray(),
            Warnings = warnings
        };
        return ([.. ids], frontendResult);
    }

    private ProsodyResult RunProsody(
        int[] tokenIds,
        float[] styleDur,
        float speed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var padded = new long[tokenIds.Length + 2];
        for (var i = 0; i < tokenIds.Length; i++)
            padded[i + 1] = tokenIds[i];

        using var results = prosody.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(padded, [1, padded.Length])),
            NamedOnnxValue.CreateFromTensor("style_dur", new DenseTensor<float>(styleDur, [1, StyleSplit])),
            NamedOnnxValue.CreateFromTensor("speed", new DenseTensor<float>(new[] { speed }, new[] { 1 }))
        ]);

        var durations = results.First(result => result.Name == "pred_dur").AsTensor<long>().ToArray();
        if (durations.Length != padded.Length)
            throw new InvalidDataException($"Wayu prosody returned {durations.Length} duration values for {padded.Length} padded tokens.");
        var indexes = new List<int>();
        for (var i = 0; i < durations.Length; i++)
        {
            var duration = durations[i];
            if (duration < 0 || duration > 1_000_000)
                throw new InvalidDataException($"Wayu prosody returned an invalid duration at index {i}: {duration}.");
            for (var frame = 0L; frame < duration; frame++)
                indexes.Add(i);
        }

        var frames = indexes.Count;
        if (frames == 0)
            return new ProsodyResult([], [], 0, durations);

        var d = results.First(result => result.Name == "d").AsTensor<float>();
        var tEn = results.First(result => result.Name == "t_en").AsTensor<float>();
        var en = new float[ProsodyChannels * frames];
        var asr = new float[AsrChannels * frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var source = indexes[frame];
            for (var channel = 0; channel < ProsodyChannels; channel++)
                en[channel * frames + frame] = d[0, source, channel];
            for (var channel = 0; channel < AsrChannels; channel++)
                asr[channel * frames + frame] = tEn[0, channel, source];
        }
        return new ProsodyResult(en, asr, frames, durations);
    }

    private CurvesResult RunCurves(
        float[] en,
        int frames,
        float[] styleDur,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var results = curves.Run(
        [
            NamedOnnxValue.CreateFromTensor("en", new DenseTensor<float>(en, [1, ProsodyChannels, frames])),
            NamedOnnxValue.CreateFromTensor("style_dur", new DenseTensor<float>(styleDur, [1, StyleSplit]))
        ]);
        return new CurvesResult(
            results.First(result => result.Name == "f0_curve").AsTensor<float>().ToArray(),
            results.First(result => result.Name == "n_curve").AsTensor<float>().ToArray());
    }

    private float[] RunDecoder(
        float[] asr,
        int frames,
        float[] f0,
        float[] noise,
        float[] styleAcou,
        float[] harmonic,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var harmonicFrames = harmonic.Length / 22;
        using var results = decoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("asr", new DenseTensor<float>(asr, [1, AsrChannels, frames])),
            NamedOnnxValue.CreateFromTensor("f0_curve", new DenseTensor<float>(f0, [1, f0.Length])),
            NamedOnnxValue.CreateFromTensor("n_curve", new DenseTensor<float>(noise, [1, noise.Length])),
            NamedOnnxValue.CreateFromTensor("style_acou", new DenseTensor<float>(styleAcou, [1, StyleSplit])),
            NamedOnnxValue.CreateFromTensor("har", new DenseTensor<float>(harmonic, [1, 22, harmonicFrames]))
        ]);
        return results.First(result => result.Name == "audio").AsTensor<float>().ToArray();
    }

    private float[] LoadStyle(string voiceId, int tokenCount)
    {
        lock (styleGate)
        {
            if (!styleCache.TryGetValue(voiceId, out var pack))
            {
                var tensor = NpzTensorReader.ReadFloat32(voicePackPath, voiceId);
                if (tensor.Shape.Length != 3 || tensor.Shape[1] != 1 || tensor.Shape[2] != StyleWidth || tensor.Shape[0] <= 0)
                    throw new InvalidDataException($"Wayu voice '{voiceId}' has unsupported style shape [{string.Join(',', tensor.Shape)}].");
                pack = tensor.Values;
                styleCache.Add(voiceId, pack);
            }

            var rowCount = pack.Length / StyleWidth;
            var row = Math.Clamp(tokenCount - 1, 0, rowCount - 1);
            var style = new float[StyleWidth];
            Array.Copy(pack, row * StyleWidth, style, 0, StyleWidth);
            return style;
        }
    }

    private float[] BuildHarmonicSource(float[] f0Curve, CancellationToken cancellationToken, int seed)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var curveSteps = f0Curve.Length;
        if (curveSteps == 0)
            return [];

        var sampleCount = checked(curveSteps * UpsampleScale);
        var random = new GaussianRandom(seed);
        var initialPhase = new double[Harmonics];
        for (var harmonic = 0; harmonic < Harmonics; harmonic++)
            initialPhase[harmonic] = random.NextUniform();
        initialPhase[0] = 0;

        var rad = new double[sampleCount * Harmonics];
        for (var sample = 0; sample < sampleCount; sample++)
        {
            var f0 = f0Curve[sample / UpsampleScale];
            for (var harmonic = 0; harmonic < Harmonics; harmonic++)
            {
                double value = f0 * (harmonic + 1) / NativeSampleRate;
                value -= Math.Floor(value);
                rad[sample * Harmonics + harmonic] = value + initialPhase[harmonic];
            }
        }

        var downsampled = Resample(rad, sampleCount, Harmonics, curveSteps);
        var phaseDownsampled = new double[downsampled.Length];
        for (var harmonic = 0; harmonic < Harmonics; harmonic++)
        {
            var total = 0d;
            for (var sample = 0; sample < curveSteps; sample++)
            {
                total += downsampled[sample * Harmonics + harmonic];
                phaseDownsampled[sample * Harmonics + harmonic] = total * 2 * Math.PI;
            }
        }
        var phase = Resample(
            phaseDownsampled.Select(value => value * UpsampleScale).ToArray(),
            curveSteps,
            Harmonics,
            sampleCount);

        var merged = new float[sampleCount];
        for (var sample = 0; sample < sampleCount; sample++)
        {
            if ((sample & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var f0 = f0Curve[sample / UpsampleScale];
            var voiced = f0 > VoicedThreshold;
            var amplitude = voiced ? NoiseStandardDeviation : SineAmplitude / 3;
            var sum = 0d;
            for (var harmonic = 0; harmonic < Harmonics; harmonic++)
            {
                var noise = random.NextGaussian();
                var wave = (voiced ? Math.Sin(phase[sample * Harmonics + harmonic]) * SineAmplitude : 0) +
                    amplitude * noise;
                sum += wave * sourceWeight[harmonic];
            }
            merged[sample] = (float) Math.Tanh(sum + sourceBias[0]);
        }
        return Stft(merged, analysisWindow);
    }

    private static double[] Resample(double[] input, int inputLength, int channels, int outputLength)
    {
        var output = new double[outputLength * channels];
        var scale = outputLength / (double) inputLength;
        for (var j = 0; j < outputLength; j++)
        {
            var source = (j + 0.5) / scale - 0.5;
            source = Math.Clamp(source, 0, inputLength - 1);
            var lower = (int) Math.Floor(source);
            var upper = Math.Min(lower + 1, inputLength - 1);
            var weight = source - lower;
            for (var channel = 0; channel < channels; channel++)
            {
                output[j * channels + channel] =
                    input[lower * channels + channel] * (1 - weight) +
                    input[upper * channels + channel] * weight;
            }
        }
        return output;
    }

    private static float[] Stft(float[] audio, float[] window)
    {
        if (audio.Length < 2)
            return [];

        var pad = FftSize / 2;
        var padded = new float[audio.Length + FftSize];
        Array.Copy(audio, 0, padded, pad, audio.Length);
        for (var i = 0; i < pad; i++)
        {
            padded[pad - 1 - i] = audio[i + 1];
            padded[pad + audio.Length + i] = audio[audio.Length - 2 - i];
        }

        var frames = (padded.Length - FftSize) / StftHop + 1;
        var output = new float[22 * frames];
        for (var frame = 0; frame < frames; frame++)
        {
            var start = frame * StftHop;
            for (var k = 0; k <= FftSize / 2; k++)
            {
                var real = 0d;
                var imaginary = 0d;
                for (var n = 0; n < FftSize; n++)
                {
                    var angle = 2 * Math.PI * k * n / FftSize;
                    var value = padded[start + n] * window[n];
                    real += value * Math.Cos(angle);
                    imaginary -= value * Math.Sin(angle);
                }
                var outputIndex = k * frames + frame;
                output[outputIndex] = (float) Math.Sqrt(real * real + imaginary * imaginary);
                output[(k + 11) * frames + frame] = (float) Math.Atan2(imaginary, real);
            }
        }
        return output;
    }

    private void AddTimings(
        List<PhonemeTiming> target,
        int[] tokenIds,
        long[] durations,
        int sampleCount,
        double segmentStartSecond)
    {
        if (durations.Length != tokenIds.Length + 2 || sampleCount == 0)
            return;
        var totalUnits = durations.Sum();
        if (totalUnits <= 0)
            return;

        var secondsPerUnit = sampleCount / (double) descriptor.SampleRate / totalUnits;
        var elapsedUnits = durations[0];
        for (var i = 0; i < tokenIds.Length; i++)
        {
            var start = segmentStartSecond + elapsedUnits * secondsPerUnit;
            elapsedUnits += durations[i + 1];
            var end = segmentStartSecond + elapsedUnits * secondsPerUnit;
            target.Add(new PhonemeTiming(
                vocabulary.FirstOrDefault(pair => pair.Value == tokenIds[i]).Key ?? string.Empty,
                start,
                end));
        }
    }

    private string RequireFile(string fileName)
    {
        var path = Path.Combine(modelDirectory, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The Wayu artifact '{fileName}' was not found.", path);
        return path;
    }

    private VoiceDescriptor FindVoice(string voiceId) =>
        voiceDescriptors.FirstOrDefault(voice => string.Equals(voice.Id, voiceId, StringComparison.OrdinalIgnoreCase))
        ?? throw new KeyNotFoundException($"The Wayu voice '{voiceId}' is not present in this adapter's catalog.");

    private bool IsStyleLoaded(string voiceId)
    {
        lock (styleGate)
            return styleCache.ContainsKey(voiceId);
    }

    private static Dictionary<string, int> LoadVocabulary(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.GetProperty("vocab").EnumerateObject())
            result.Add(property.Name, property.Value.GetInt32());
        return result;
    }

    private static IReadOnlyList<VoiceDescriptor> CreateVoiceDescriptors(string voicePackPath)
    {
        var size = new FileInfo(voicePackPath).Length;
        return NpzTensorReader.ListKeys(voicePackPath)
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key =>
            {
                var gender = key.StartsWith("f_", StringComparison.OrdinalIgnoreCase) ? "female" :
                    key.StartsWith("m_", StringComparison.OrdinalIgnoreCase) ? "male" : "unknown";
                var displayName = key.Length > 2
                    ? string.Join(' ', key[2..].Split('_').Select(word => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..]))
                    : key;
                return new VoiceDescriptor
                {
                    Id = key,
                    DisplayName = displayName,
                    LanguageCode = "th",
                    Gender = gender,
                    ConditioningKey = key,
                    SourcePath = voicePackPath,
                    SizeBytes = size
                };
            })
            .ToArray();
    }

    private static IEnumerable<int[]> SplitIntoSafeSegments(IReadOnlyList<int> tokenIds)
    {
        for (var offset = 0; offset < tokenIds.Count; offset += MaxTokens)
        {
            var length = Math.Min(MaxTokens, tokenIds.Count - offset);
            var segment = new int[length];
            for (var i = 0; i < length; i++)
                segment[i] = tokenIds[offset + i];
            yield return segment;
        }
    }

    private void ValidateTokenIds(IReadOnlyList<int> tokenIds)
    {
        var maxId = vocabulary.Values.Max();
        if (tokenIds.Any(id => id < 0 || id > maxId))
            throw new ArgumentOutOfRangeException(nameof(tokenIds), $"Wayu token ids must be in the range 0..{maxId}.");
    }

    private static void ValidateVocabulary(IReadOnlyDictionary<string, int> value)
    {
        if (value.Count == 0)
            throw new InvalidDataException("The Wayu vocabulary is empty.");
        if (value.Values.Any(id => id < 0))
            throw new InvalidDataException("The Wayu vocabulary contains a negative token id.");
    }

    private static void ValidateSourceParameters(float[] weight, float[] bias, float[] window)
    {
        if (weight.Length < Harmonics || bias.Length == 0 || window.Length != FftSize)
            throw new InvalidDataException("Wayu source_params.npz does not contain the expected weight, bias, and 20-sample window.");
    }

    private static void ValidateGraphContract(
        InferenceSession session,
        string graphName,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs)
    {
        var missingInputs = inputs.Where(input => !session.InputMetadata.ContainsKey(input)).ToArray();
        var missingOutputs = outputs.Where(output => !session.OutputMetadata.ContainsKey(output)).ToArray();
        if (missingInputs.Length > 0 || missingOutputs.Length > 0)
        {
            throw new InvalidDataException(
                $"Wayu {graphName} graph contract mismatch. Missing inputs: {string.Join(',', missingInputs)}; missing outputs: {string.Join(',', missingOutputs)}.");
        }
    }

    private static ModelDescriptor CreateDefaultDescriptor() => new()
    {
        Id = "wayu-kokoro-thai-v1",
        Family = "Kokoro",
        AdapterId = "wayu-thai-graph-set",
        Tier = AdapterTier.GraphSet,
        Revision = "abf63dc118365bea784fa881bdd1dce4b2cc1fd4",
        LanguageCodes = ["th"],
        SampleRate = NativeSampleRate,
        Channels = 1,
        Capabilities = AdapterCapabilities.PrePhonemized | AdapterCapabilities.TokenIds | AdapterCapabilities.Timings | AdapterCapabilities.GraphSet
    };

    private static SessionOptions CreateSessionOptions() => new()
    {
        IntraOpNumThreads = 2,
        InterOpNumThreads = 1,
        GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
    };

    private static void ValidateDescriptor(ModelDescriptor value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.AdapterId);
        if (value.SampleRate <= 0 || value.Channels <= 0)
            throw new ArgumentException("The adapter descriptor must contain a positive sample rate and channel count.", nameof(value));
    }

    private static void ValidateVoices(IReadOnlyList<VoiceDescriptor> values)
    {
        if (values.Count == 0)
            throw new InvalidDataException("The Wayu voice pack contains no voices.");
        if (values.Any(voice => string.IsNullOrWhiteSpace(voice.Id) || string.IsNullOrWhiteSpace(voice.ConditioningKey)))
            throw new InvalidDataException("Every Wayu voice must have an id and conditioning key.");
        if (values.Select(voice => voice.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != values.Count)
            throw new InvalidDataException("Wayu voice ids must be unique.");
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
            throw new ObjectDisposedException(nameof(WayuThaiAdapter));
    }

    private sealed record ProsodyResult(float[] En, float[] Asr, int Frames, long[] Durations);

    private sealed record CurvesResult(float[] F0, float[] Noise);

    private sealed class GaussianRandom
    {
        private readonly Random random;
        private bool hasSpare;
        private double spare;

        public GaussianRandom(int seed) => random = new(seed);

        public double NextUniform() => random.NextDouble();

        public double NextGaussian()
        {
            if (hasSpare)
            {
                hasSpare = false;
                return spare;
            }
            var u1 = Math.Max(double.Epsilon, random.NextDouble());
            var u2 = random.NextDouble();
            var scale = Math.Sqrt(-2 * Math.Log(u1));
            var angle = 2 * Math.PI * u2;
            spare = scale * Math.Sin(angle);
            hasSpare = true;
            return scale * Math.Cos(angle);
        }
    }
}
