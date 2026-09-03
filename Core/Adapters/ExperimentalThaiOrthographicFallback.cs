namespace KokoroSharp.Adapters;

using System.Text;

/// <summary>
/// Small, data-free Thai OOV fallback for experiments and host-side A/B tests.
/// </summary>
/// <remarks>
/// This is intentionally not selected by <see cref="FastThaiG2PFrontend" />
/// automatically. It uses Thai orthographic vowel and tone rules, not a
/// pronunciation dictionary or statistical syllable model, so names, loan
/// words, consonant clusters, and ambiguous spellings require an audio-quality
/// gate before this can be enabled for a catalog. It has no Python, TorchSharp,
/// or TLTK data dependency.
/// </remarks>
public sealed class ExperimentalThaiOrthographicFallback
{
    public const string FallbackName = "thai-orthographic-heuristic";

    private static readonly VowelPattern[] Vowels =
    [
        new("", "ัว", "ua", false),
        new("เ", "ีย", "ia", true),
        new("เ", "ือ", "ɯa", true),
        new("เ", "า", "aw", true),
        new("แ", "ะ", "ɛ", false),
        new("แ", "", "ɛː", true),
        new("โ", "ะ", "o", false),
        new("โ", "", "oː", true),
        new("เ", "ะ", "e", false),
        new("เ", "", "eː", true),
        new("ใ", "", "aj", true),
        new("ไ", "", "aj", true),
        new("", "ำ", "am", true),
        new("", "ะ", "a", false),
        new("", "า", "aː", true),
        new("", "ิ", "i", false),
        new("", "ี", "iː", true),
        new("", "ึ", "ɯ", false),
        new("", "ื", "ɯː", true),
        new("", "ุ", "u", false),
        new("", "ู", "uː", true),
        new("", "ั", "a", false),
        new("", "อ", "ɤː", true)
    ];

    private static readonly IReadOnlyDictionary<char, string> Initials =
        new Dictionary<char, string>
        {
            ['ก'] = "k", ['ข'] = "kʰ", ['ฃ'] = "kʰ", ['ค'] = "kʰ", ['ฅ'] = "kʰ", ['ฆ'] = "kʰ",
            ['ง'] = "ŋ", ['จ'] = "t͡ɕ", ['ฉ'] = "t͡ɕʰ", ['ช'] = "t͡ɕʰ", ['ซ'] = "s", ['ฌ'] = "t͡ɕʰ",
            ['ญ'] = "j", ['ฎ'] = "d", ['ฏ'] = "t", ['ฐ'] = "tʰ", ['ฑ'] = "tʰ", ['ฒ'] = "tʰ",
            ['ณ'] = "n", ['ด'] = "d", ['ต'] = "t", ['ถ'] = "tʰ", ['ท'] = "tʰ", ['ธ'] = "tʰ",
            ['น'] = "n", ['บ'] = "b", ['ป'] = "p", ['ผ'] = "pʰ", ['ฝ'] = "f", ['พ'] = "pʰ",
            ['ฟ'] = "f", ['ภ'] = "pʰ", ['ม'] = "m", ['ย'] = "j", ['ร'] = "r", ['ล'] = "l",
            ['ว'] = "w", ['ศ'] = "s", ['ษ'] = "s", ['ส'] = "s", ['ห'] = "h", ['ฬ'] = "l",
            ['อ'] = "ʔ", ['ฮ'] = "h"
        };

    private static readonly IReadOnlyDictionary<char, ThaiToneClass> ToneClasses =
        new Dictionary<char, ThaiToneClass>
        {
            ['ก'] = ThaiToneClass.Middle, ['จ'] = ThaiToneClass.Middle, ['ฎ'] = ThaiToneClass.Middle,
            ['ฏ'] = ThaiToneClass.Middle, ['ด'] = ThaiToneClass.Middle, ['ต'] = ThaiToneClass.Middle,
            ['บ'] = ThaiToneClass.Middle, ['ป'] = ThaiToneClass.Middle, ['อ'] = ThaiToneClass.Middle,
            ['ข'] = ThaiToneClass.High, ['ฃ'] = ThaiToneClass.High, ['ฉ'] = ThaiToneClass.High,
            ['ฐ'] = ThaiToneClass.High, ['ถ'] = ThaiToneClass.High, ['ผ'] = ThaiToneClass.High,
            ['ฝ'] = ThaiToneClass.High, ['ศ'] = ThaiToneClass.High, ['ษ'] = ThaiToneClass.High,
            ['ส'] = ThaiToneClass.High, ['ห'] = ThaiToneClass.High,
            ['ค'] = ThaiToneClass.Low, ['ฅ'] = ThaiToneClass.Low, ['ฆ'] = ThaiToneClass.Low,
            ['ช'] = ThaiToneClass.Low, ['ซ'] = ThaiToneClass.Low, ['ฌ'] = ThaiToneClass.Low,
            ['ญ'] = ThaiToneClass.Low, ['ฑ'] = ThaiToneClass.Low, ['ฒ'] = ThaiToneClass.Low,
            ['ณ'] = ThaiToneClass.Low, ['ท'] = ThaiToneClass.Low, ['ธ'] = ThaiToneClass.Low,
            ['น'] = ThaiToneClass.Low, ['พ'] = ThaiToneClass.Low, ['ฟ'] = ThaiToneClass.Low,
            ['ภ'] = ThaiToneClass.Low, ['ม'] = ThaiToneClass.Low, ['ย'] = ThaiToneClass.Low,
            ['ร'] = ThaiToneClass.Low, ['ล'] = ThaiToneClass.Low, ['ว'] = ThaiToneClass.Low,
            ['ฬ'] = ThaiToneClass.Low, ['ง'] = ThaiToneClass.Low, ['ฮ'] = ThaiToneClass.Low
        };

    private static readonly IReadOnlyDictionary<char, string> Finals =
        new Dictionary<char, string>
        {
            ['ก'] = "k̚", ['ข'] = "k̚", ['ฃ'] = "k̚", ['ค'] = "k̚", ['ฅ'] = "k̚", ['ฆ'] = "k̚",
            ['จ'] = "t̚", ['ฉ'] = "t̚", ['ช'] = "t̚", ['ซ'] = "t̚", ['ฌ'] = "t̚", ['ฎ'] = "t̚",
            ['ฏ'] = "t̚", ['ฐ'] = "t̚", ['ฑ'] = "t̚", ['ฒ'] = "t̚", ['ด'] = "t̚", ['ต'] = "t̚",
            ['ถ'] = "t̚", ['ท'] = "t̚", ['ธ'] = "t̚", ['ศ'] = "t̚", ['ษ'] = "t̚", ['ส'] = "t̚",
            ['บ'] = "p̚", ['ป'] = "p̚", ['ผ'] = "p̚", ['ฝ'] = "p̚", ['พ'] = "p̚", ['ฟ'] = "p̚", ['ภ'] = "p̚",
            ['ง'] = "ŋ", ['ญ'] = "n", ['ณ'] = "n", ['น'] = "n", ['ร'] = "n", ['ล'] = "n", ['ฬ'] = "n",
            ['ม'] = "m", ['ย'] = "j", ['ว'] = "w"
        };

    public string Id => FallbackName;

    /// <summary>
    /// Converts one Thai orthographic run to approximate FastThai IPA.
    /// Returns an empty string when the run cannot be parsed safely.
    /// </summary>
    public string Convert(string word)
    {
        if (string.IsNullOrWhiteSpace(word) || word.Any(character => !IsThaiBlockCharacter(character)))
            return string.Empty;

        var segments = Segment(word.Normalize(NormalizationForm.FormC));
        return segments is null ? string.Empty : string.Join('.', segments);
    }

    private static IReadOnlyList<string> Segment(string word)
    {
        var paths = new PathResult[word.Length + 1];
        paths[word.Length] = new PathResult([], 0, 0);

        for (var start = word.Length - 1; start >= 0; start--)
        {
            var maxEnd = Math.Min(word.Length, start + 12);
            for (var end = start + 1; end <= maxEnd; end++)
            {
                if (paths[end] is null)
                    continue;
                if (!TryParseSyllable(word[start..end], out var syllable, out var score))
                    continue;

                var candidate = new PathResult(
                    [syllable],
                    score + paths[end].Score,
                    1 + paths[end].SyllableCount);
                candidate.Segments.AddRange(paths[end].Segments);
                if (paths[start] is null || IsBetter(candidate, paths[start]))
                    paths[start] = candidate;
            }
        }

        return paths[0]?.Segments;
    }

    private static bool IsBetter(PathResult candidate, PathResult current) =>
        candidate.Score > current.Score ||
        candidate.Score == current.Score && candidate.SyllableCount < current.SyllableCount;

    private static bool TryParseSyllable(
        string source,
        out string ipa,
        out int score)
    {
        ipa = string.Empty;
        score = 0;
        if (source.Length == 0)
            return false;

        var (core, toneMark) = RemoveMarks(source);
        if (core.Length == 0)
            return false;

        foreach (var vowel in Vowels)
        {
            if (!core.StartsWith(vowel.Prefix, StringComparison.Ordinal))
                continue;

            var onsetStart = vowel.Prefix.Length;
            for (var onsetLength = 1; onsetStart + onsetLength <= core.Length; onsetLength++)
            {
                var onset = core.Substring(onsetStart, onsetLength);
                if (onset.Any(character => !Initials.ContainsKey(character)))
                    break;

                var vowelStart = onsetStart + onsetLength;
                if (!core[vowelStart..].StartsWith(vowel.Suffix, StringComparison.Ordinal))
                    continue;

                var finalText = core[(vowelStart + vowel.Suffix.Length)..];
                if (finalText.Length > 1 || finalText.Any(character => !Initials.ContainsKey(character)))
                    continue;

                var final = finalText.Length == 0 ? (char?) null : finalText[0];
                ipa = BuildSyllable(onset, vowel.Sound, vowel.IsLong, final, toneMark);
                score = 100 + source.Length * 3 + (vowel.IsLong ? 2 : 0);
                return true;
            }
        }

        // A final consonant with no written vowel is common in imperfectly
        // segmented input. Keep this lower-scored than an explicit vowel so a
        // later valid syllable boundary wins when one exists.
        var (cleanOnset, cleanFinal) = SplitInherentSyllable(core);
        if (cleanOnset.Length > 0 && cleanOnset.All(character => Initials.ContainsKey(character)))
        {
            var final = cleanFinal is null ? (char?) null : cleanFinal.Value;
            ipa = BuildSyllable(cleanOnset, "ɔː", true, final, toneMark);
            score = 24 + source.Length;
            return true;
        }

        return false;
    }

    private static (string Onset, char? Final) SplitInherentSyllable(string core)
    {
        if (core.Length == 1 && Initials.ContainsKey(core[0]))
            return (core, null);
        if (core.Length >= 2 && Initials.ContainsKey(core[^1]))
            return (core[..^1], core[^1]);
        return (string.Empty, null);
    }

    private static string BuildSyllable(
        string onset,
        string vowel,
        bool isLong,
        char? final,
        char toneMark)
    {
        var result = new StringBuilder();
        var toneClass = GetToneClass(onset);
        var voicedOnset = onset;
        if (onset.Length >= 2 && onset[0] == 'ห' && IsLowSingle(onset[1]))
        {
            toneClass = ThaiToneClass.High;
            voicedOnset = onset[1..];
        }

        foreach (var consonant in voicedOnset)
            result.Append(Initials[consonant]);
        result.Append(vowel);
        if (final is { } finalConsonant && Finals.TryGetValue(finalConsonant, out var finalSound))
            result.Append(finalSound);

        var live = isLong || final is { } && Finals.TryGetValue(final.Value, out var coda) && IsLiveCoda(coda);
        result.Append(ToneToIpa(ResolveTone(toneClass, toneMark, live, isLong && final is not null)));
        return result.ToString();
    }

    private static ThaiTone ResolveTone(
        ThaiToneClass toneClass,
        char toneMark,
        bool live,
        bool longDead)
    {
        if (toneMark == '่')
            return toneClass == ThaiToneClass.Low ? ThaiTone.Falling : ThaiTone.Low;
        if (toneMark == '้')
            return toneClass == ThaiToneClass.Low ? ThaiTone.High : ThaiTone.Falling;
        if (toneMark == '๊')
            return ThaiTone.High;
        if (toneMark == '๋')
            return ThaiTone.Rising;

        return toneClass switch
        {
            ThaiToneClass.High => live ? ThaiTone.Rising : ThaiTone.Low,
            ThaiToneClass.Middle => live ? ThaiTone.Mid : ThaiTone.Low,
            _ => live ? ThaiTone.Mid : longDead ? ThaiTone.Falling : ThaiTone.High
        };
    }

    private static ThaiToneClass GetToneClass(string onset)
    {
        if (onset.Length >= 2 && onset[0] == 'ห' && IsLowSingle(onset[1]))
            return ThaiToneClass.High;
        return ToneClasses.TryGetValue(onset[0], out var value) ? value : ThaiToneClass.Low;
    }

    private static bool IsLowSingle(char value) => value is 'ง' or 'ญ' or 'ณ' or 'น' or 'ม' or 'ย' or 'ร' or 'ล' or 'ว' or 'ฬ';

    private static bool IsLiveCoda(string value) => value is "ŋ" or "n" or "m" or "j" or "w";

    private static string ToneToIpa(ThaiTone value) => value switch
    {
        ThaiTone.Low => "˨˩",
        ThaiTone.Falling => "˥˩",
        ThaiTone.High => "˦˥",
        ThaiTone.Rising => "˩˩˦",
        _ => "˧"
    };

    private static (string Core, char ToneMark) RemoveMarks(string source)
    {
        var core = new StringBuilder(source.Length);
        var toneMark = '\0';
        foreach (var character in source)
        {
            if (character is '่' or '้' or '๊' or '๋')
            {
                toneMark = character;
                continue;
            }

            if (character == '์')
            {
                if (core.Length > 0 && Initials.ContainsKey(core[^1]))
                    core.Length--;
                continue;
            }

            if (character == '็')
                continue;
            core.Append(character);
        }
        return (core.ToString(), toneMark);
    }

    private static bool IsThaiBlockCharacter(char value) => value is >= '\u0E00' and <= '\u0E7F';

    private sealed record VowelPattern(string Prefix, string Suffix, string Sound, bool IsLong);

    private sealed class PathResult
    {
        public PathResult(IEnumerable<string> segments, int score, int syllableCount)
        {
            Segments = [.. segments];
            Score = score;
            SyllableCount = syllableCount;
        }

        public List<string> Segments { get; }
        public int Score { get; }
        public int SyllableCount { get; }
    }

    private enum ThaiToneClass
    {
        Low,
        Middle,
        High
    }

    private enum ThaiTone
    {
        Mid,
        Low,
        Falling,
        High,
        Rising
    }
}
