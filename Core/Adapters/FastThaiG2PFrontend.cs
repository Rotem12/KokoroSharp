namespace KokoroSharp.Adapters;

using System.Text;
using System.Text.Json;
using System.Globalization;

/// <summary>
/// Native .NET dictionary frontend for the FastThaiG2P IPA data format.
/// </summary>
/// <remarks>
/// The acoustic model and the frontend are separate contracts. This class
/// intentionally consumes the pinned FastThaiG2P <c>dict.txt</c> and
/// <c>ipa.json</c> as external artifacts instead of bundling model or
/// generated pronunciation data into KokoroSharp. It provides a Python-free
/// dictionary path; the optional fallback is an explicit host callback and is
/// not silently substituted with eSpeak.
/// </remarks>
public sealed class FastThaiG2PFrontend : ITextFrontend
{
    public const string FrontendName = "fastthai-g2p-dictionary";

    /// <summary>
    /// Maps FastThaiG2P's five IPA tone contours to the Wayu/TLTK-trained
    /// Kokoro tone symbols. FastThaiG2P uses a distinct high-tone arrow; Wayu
    /// uses the existing rising arrow for that contour.
    /// </summary>
    public static IReadOnlyDictionary<char, char> WayuToneMap { get; } =
        new Dictionary<char, char>
        {
            ['↑'] = '↗', // high
            ['↓'] = '˩', // low
            ['↘'] = '↘', // falling
            ['↗'] = '↓', // rising
            ['→'] = '→'  // mid
        };

    private readonly Dictionary<string, string> ipaByWord;
    private readonly ThaiTrie dictionary;
    private readonly IReadOnlyDictionary<char, int> vocabulary;
    private readonly IReadOnlyDictionary<char, char> phonemeMap;
    private readonly Func<string, string> fallback;

    public FastThaiG2PFrontend(
        string dictionaryPath,
        string ipaPath,
        IReadOnlyDictionary<char, int> vocabulary,
        IReadOnlyDictionary<char, char> phonemeMap = null,
        Func<string, string> fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dictionaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipaPath);
        ArgumentNullException.ThrowIfNull(vocabulary);

        if (!File.Exists(dictionaryPath))
            throw new FileNotFoundException("The FastThaiG2P dictionary was not found.", dictionaryPath);
        if (!File.Exists(ipaPath))
            throw new FileNotFoundException("The FastThaiG2P IPA map was not found.", ipaPath);
        if (vocabulary.Count == 0)
            throw new ArgumentException("The target vocabulary cannot be empty.", nameof(vocabulary));

        this.vocabulary = new Dictionary<char, int>(vocabulary);
        this.phonemeMap = phonemeMap is null
            ? new Dictionary<char, char>()
            : new Dictionary<char, char>(phonemeMap);
        this.fallback = fallback;
        dictionary = LoadDictionary(dictionaryPath);
        ipaByWord = LoadIpaMap(ipaPath);
    }

    public string Id => FrontendName;

    /// <summary>True when the caller supplied an explicit native fallback.</summary>
    public bool HasFallback => fallback is not null;

    public FrontendResult Process(
        string text,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var language = NormalizeLanguageCode(languageCode);
        text ??= string.Empty;
        if (text.Length == 0)
            return Empty(language);
        if (language != "th" && language != "th-th")
        {
            return Empty(language) with
            {
                Coverage = 0f,
                Warnings = [$"The FastThaiG2P frontend accepts Thai ('th'), not '{language}'."]
            };
        }

        var normalized = FastThaiTextNormalizer.Normalize(text);
        var phonemeBuilder = new StringBuilder(normalized.Length * 2);
        var droppedWords = new HashSet<string>(StringComparer.Ordinal);
        var sourceWords = 0;
        var convertedWords = 0;
        var usedFallback = false;

        foreach (var token in Tokenize(normalized))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (token == " ")
            {
                AppendBoundary(phonemeBuilder);
                continue;
            }
            if (!ContainsThai(token))
                continue;

            sourceWords++;
            if (!ipaByWord.TryGetValue(token, out var ipa) && fallback is not null)
            {
                ipa = fallback(token);
                usedFallback |= !string.IsNullOrWhiteSpace(ipa);
            }

            if (string.IsNullOrWhiteSpace(ipa))
            {
                droppedWords.Add(token);
                continue;
            }

            var mapped = MapIpaToKokoro(ipa, phonemeMap);
            if (string.IsNullOrWhiteSpace(mapped))
            {
                droppedWords.Add(token);
                continue;
            }

            if (phonemeBuilder.Length > 0 && phonemeBuilder[^1] != ' ')
                phonemeBuilder.Append(' ');
            phonemeBuilder.Append(mapped);
            convertedWords++;
        }

        var phonemes = phonemeBuilder.ToString().Trim();
        var result = FromPhonemes(phonemes, language, cancellationToken);
        var warnings = new List<string>(result.Warnings);
        if (droppedWords.Count > 0)
            warnings.Add($"No FastThaiG2P pronunciation was available for {droppedWords.Count} Thai word(s).");
        if (fallback is null)
            warnings.Add("This frontend is dictionary-only; OOV Thai requires an explicit native fallback.");
        else if (usedFallback)
            warnings.Add("The configured OOV fallback was used; pronunciation quality is model- and fallback-dependent.");

        var coverage = sourceWords == 0 ? 1f : convertedWords / (float)sourceWords;
        return result with
        {
            Coverage = Math.Min(coverage, result.Coverage),
            UsedFallback = usedFallback || result.UsedFallback,
            DroppedSymbols = droppedWords.Count == 0
                ? result.DroppedSymbols
                : [.. new HashSet<string>(result.DroppedSymbols, StringComparer.Ordinal).Concat(droppedWords)],
            Warnings = warnings.ToArray()
        };
    }

    public FrontendResult FromPhonemes(
        string phonemes,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var language = NormalizeLanguageCode(languageCode);
        phonemes ??= string.Empty;

        var accepted = new StringBuilder(phonemes.Length);
        var dropped = new HashSet<string>(StringComparer.Ordinal);
        foreach (var phoneme in phonemes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (vocabulary.TryGetValue(phoneme, out _))
                accepted.Append(phoneme);
            else
                dropped.Add(phoneme.ToString());
        }

        var coverage = phonemes.Length == 0 ? 1f : accepted.Length / (float)phonemes.Length;
        var warnings = dropped.Count == 0
            ? Array.Empty<string>()
            : new[] { $"Dropped {dropped.Count} distinct symbol(s) that are not in the target Thai vocabulary." };

        return new FrontendResult
        {
            FrontendId = Id,
            LanguageCode = language,
            Phonemes = accepted.ToString(),
            TokenIds = accepted.ToString().Select(character => vocabulary[character]).ToArray(),
            Coverage = coverage,
            DroppedSymbols = dropped.ToArray(),
            Warnings = warnings
        };
    }

    /// <summary>
    /// Converts FastThaiG2P's slash-delimited IPA words into the target
    /// Kokoro phoneme convention. This is intentionally public for parity
    /// tests against the upstream Python reference.
    /// </summary>
    public static string MapIpaToKokoro(
        string ipa,
        IReadOnlyDictionary<char, char> phonemeMap = null)
    {
        if (string.IsNullOrWhiteSpace(ipa))
            return string.Empty;

        var words = ipa
            .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim('/'))
            .Where(word => word.Length > 0)
            .ToArray();
        var value = words.Length == 0 ? ipa.Replace("/", string.Empty) : string.Join(' ', words);

        value = value.Replace("t͡ɕʰ", "ʨʰ", StringComparison.Ordinal)
            .Replace("t͡ɕ", "ʨ", StringComparison.Ordinal);
        value = value.Replace("˩˩˦", "↗", StringComparison.Ordinal)
            .Replace("˥˩", "↘", StringComparison.Ordinal)
            .Replace("˦˥", "↑", StringComparison.Ordinal)
            .Replace("˨˩", "↓", StringComparison.Ordinal)
            .Replace("˧", "→", StringComparison.Ordinal);
        value = value.Replace("̚", string.Empty, StringComparison.Ordinal)
            .Replace("̯", string.Empty, StringComparison.Ordinal)
            .Replace("͡", string.Empty, StringComparison.Ordinal)
            .Replace('g', 'ɡ');

        if (phonemeMap is not null)
        {
            var mapped = new StringBuilder(value.Length);
            foreach (var character in value)
                mapped.Append(phonemeMap.TryGetValue(character, out var replacement) ? replacement : character);
            value = mapped.ToString();
        }

        return value;
    }

    private static Dictionary<string, string> LoadIpaMap(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
                result[property.Name] = property.Value.GetString() ?? string.Empty;
        }
        return result;
    }

    private static ThaiTrie LoadDictionary(string path)
    {
        var trie = new ThaiTrie();
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            var word = line.Trim();
            if (word.Length == 0 || word.StartsWith('#'))
                continue;
            trie.Add(word);
        }
        return trie;
    }

    private IEnumerable<string> Tokenize(string text)
    {
        for (var index = 0; index < text.Length;)
        {
            if (char.IsWhiteSpace(text[index]))
            {
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
                yield return " ";
                continue;
            }

            if (dictionary.TryMatch(text, index, out var length, out var word))
            {
                yield return word;
                index += length;
                continue;
            }

            if (!IsThaiBlockCharacter(text[index]))
            {
                yield return text[index].ToString();
                index++;
                continue;
            }

            // Keep an unmatched Thai run together. Splitting an OOV word into
            // single characters prevents any native syllable fallback from
            // seeing the orthographic context it needs for consonant class,
            // vowel form, final consonant, and tone assignment. Stop before a
            // dictionary match so known words in mixed text retain the fast
            // longest-match path.
            var start = index++;
            while (index < text.Length && IsThaiBlockCharacter(text[index]))
            {
                if (dictionary.TryMatch(text, index, out _, out _))
                    break;
                index++;
            }
            yield return text[start..index];
        }
    }

    private static bool ContainsThai(string value) => value.Any(character => character is >= 'ก' and <= '๛');

    private static bool IsThaiBlockCharacter(char value) => value is >= '\u0E00' and <= '\u0E7F';

    private static bool IsCombiningMark(char value) =>
        CharUnicodeInfo.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;

    private static void AppendBoundary(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
            builder.Append(' ');
    }

    private static FrontendResult Empty(string languageCode) => new()
    {
        FrontendId = FrontendName,
        LanguageCode = languageCode,
        Coverage = 1f
    };

    private static string NormalizeLanguageCode(string languageCode) =>
        string.IsNullOrWhiteSpace(languageCode) ? "th" : languageCode.Trim().ToLowerInvariant();

    private sealed class ThaiTrie
    {
        private readonly Node root = new();

        public void Add(string word)
        {
            var node = root;
            foreach (var character in word)
            {
                if (!node.Children.TryGetValue(character, out var next))
                    node.Children.Add(character, next = new Node());
                node = next;
            }
            node.Word = word;
        }

        public bool TryMatch(string text, int start, out int length, out string word)
        {
            var node = root;
            length = 0;
            word = null;
            for (var index = start; index < text.Length; index++)
            {
                if (!node.Children.TryGetValue(text[index], out node))
                    break;
                if (node.Word is not null)
                {
                    length = index - start + 1;
                    word = node.Word;
                }
            }
            return word is not null;
        }

        private sealed class Node
        {
            public Dictionary<char, Node> Children { get; } = new();
            public string Word { get; set; }
        }
    }
}
