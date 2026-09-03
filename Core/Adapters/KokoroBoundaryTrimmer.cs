namespace KokoroSharp.Adapters;

/// <summary>
/// Removes the static generated for Kokoro-style BOS/EOS padding tokens.
/// </summary>
/// <remarks>
/// Some exports expose the padded waveform directly while others trim it in
/// their reference wrapper. This managed implementation keeps that behavior
/// an explicit graph option instead of changing the stock adapter default.
/// </remarks>
internal static class KokoroBoundaryTrimmer
{
    public static float[] Trim(
        float[] audio,
        IReadOnlyList<int> durations,
        int sampleRate,
        out int startSamples)
    {
        startSamples = 0;
        if (audio is null || audio.Length == 0 || durations is null || durations.Count < 2)
            return audio ?? Array.Empty<float>();

        var totalFrames = durations.Sum(duration => (long) Math.Max(0, duration));
        if (totalFrames <= 0)
            return audio;

        var samplesPerFrame = audio.Length / (double) totalFrames;
        var bosEnd = (int) Math.Clamp(
            Math.Floor(durations[0] * samplesPerFrame),
            0,
            audio.Length);
        var eosStart = (int) Math.Clamp(
            audio.Length - Math.Floor(durations[^1] * samplesPerFrame),
            0,
            audio.Length);

        var window = Math.Max(1, sampleRate / 100);
        var windowCount = audio.Length / window;
        if (windowCount == 0)
            return audio;

        var energies = new double[windowCount];
        var maxEnergy = 0d;
        for (var index = 0; index < windowCount; index++)
        {
            var offset = index * window;
            var sum = 0d;
            for (var sample = 0; sample < window; sample++)
            {
                var value = audio[offset + sample];
                sum += value * value;
            }
            energies[index] = Math.Sqrt(sum / window);
            maxEnergy = Math.Max(maxEnergy, energies[index]);
        }

        var threshold = maxEnergy * 0.1;
        var lowLag = Math.Max(1, sampleRate / 400);
        var highLag = Math.Min(window - 1, sampleRate / 60);
        var speech = new bool[windowCount];
        for (var index = 0; index < windowCount; index++)
        {
            if (energies[index] < threshold || highLag <= lowLag)
                continue;

            var offset = index * window;
            var mean = 0d;
            for (var sample = 0; sample < window; sample++)
                mean += audio[offset + sample];
            mean /= window;

            var zeroLag = 0d;
            for (var sample = 0; sample < window; sample++)
            {
                var centered = audio[offset + sample] - mean;
                zeroLag += centered * centered;
            }
            if (zeroLag <= 1e-9)
                continue;

            var best = 0d;
            for (var lag = lowLag; lag < highLag; lag++)
            {
                var correlation = 0d;
                for (var sample = 0; sample < window - lag; sample++)
                    correlation +=
                        (audio[offset + sample] - mean) *
                        (audio[offset + sample + lag] - mean);
                best = Math.Max(best, correlation);
            }
            speech[index] = best / zeroLag > 0.5;
        }

        const int maxGap = 5;
        var margin = Math.Max(0, sampleRate / 40);

        var anchor = Math.Min(bosEnd / window, windowCount - 1);
        var firstSpeech = anchor;
        var gap = 0;
        for (var index = anchor; index >= 0; index--)
        {
            if (speech[index])
            {
                firstSpeech = index;
                gap = 0;
            }
            else if (++gap > maxGap)
            {
                break;
            }
        }
        var start = Math.Max(0, Math.Min(firstSpeech * window, bosEnd) - margin);

        anchor = Math.Min(eosStart / window, windowCount - 1);
        var lastSpeech = anchor;
        gap = 0;
        for (var index = anchor; index < windowCount; index++)
        {
            if (speech[index])
            {
                lastSpeech = index;
                gap = 0;
            }
            else if (++gap > maxGap)
            {
                break;
            }
        }
        var end = Math.Min(
            audio.Length,
            Math.Max(lastSpeech * window + window, eosStart) + margin);

        if (end <= start)
            return audio;

        startSamples = start;
        var trimmed = new float[end - start];
        Array.Copy(audio, start, trimmed, 0, trimmed.Length);
        var fade = Math.Min(window, trimmed.Length);
        if (fade > 1)
        {
            for (var index = 0; index < fade; index++)
            {
                var ramp = index / (float) (fade - 1);
                trimmed[index] *= ramp;
                trimmed[trimmed.Length - 1 - index] *= ramp;
            }
        }
        return trimmed;
    }
}
