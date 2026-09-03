namespace KokoroSharp.Core;

/// <summary>
/// Metadata for a Kokoro voice that can be used without loading its speaker
/// embedding tensor.
/// </summary>
public sealed class KokoroVoiceCatalogEntry
{
    public KokoroVoiceCatalogEntry(string name, KokoroLanguage language, KokoroGender gender)
    {
        Name = name;
        Language = language;
        Gender = gender;
    }

    public string Name { get; }

    public KokoroLanguage Language { get; }

    public KokoroGender Gender { get; }
}

/// <summary>
/// Static metadata for the stock voices shipped with the Kokoro v1 voice set.
/// </summary>
/// <remarks>
/// This catalog intentionally does not call <see cref="KokoroVoice.FromPath"/>
/// or touch NumSharp. The actual speaker tensors remain lazy and are loaded
/// by <see cref="KokoroSharp.KokoroVoiceManager"/> when a voice is selected
/// for synthesis.
/// </remarks>
public static class KokoroVoiceCatalog
{
    private static readonly string[] names =
    {
        "af_alloy", "af_aoede", "af_bella", "af_heart", "af_jessica", "af_kore", "af_nicole", "af_nova", "af_river", "af_sarah", "af_sky",
        "am_adam", "am_echo", "am_eric", "am_fenrir", "am_liam", "am_michael", "am_onyx", "am_puck", "am_santa",
        "bf_alice", "bf_emma", "bf_isabella", "bf_lily", "bm_daniel", "bm_fable", "bm_george", "bm_lewis",
        "ef_dora", "em_alex", "em_santa", "ff_siwis", "hf_alpha", "hf_beta", "hm_omega", "hm_psi",
        "if_sara", "im_nicola", "jf_alpha", "jf_gongitsune", "jf_nezumi", "jf_tebukuro", "jm_kumo",
        "pf_dora", "pm_alex", "pm_santa", "zf_xiaobei", "zf_xiaoni", "zf_xiaoxiao", "zf_xiaoyi",
        "zm_yunjian", "zm_yunxi", "zm_yunxia", "zm_yunyang"
    };

    /// <summary>Names of the stock voice files, without the .npy extension.</summary>
    public static IReadOnlyList<string> VoiceNames { get; } = Array.AsReadOnly(names);

    /// <summary>Voice metadata that is safe to enumerate without loading feature tensors.</summary>
    public static IReadOnlyList<KokoroVoiceCatalogEntry> Voices { get; } =
        names.Select(CreateEntry).ToArray();

    /// <summary>Returns metadata for the requested languages and gender without reading voice files.</summary>
    public static IReadOnlyList<KokoroVoiceCatalogEntry> GetVoices(
        IEnumerable<KokoroLanguage> languages,
        KokoroGender gender = KokoroGender.Both)
    {
        if (languages == null)
            return Array.Empty<KokoroVoiceCatalogEntry>();

        var languageSet = languages.ToHashSet();
        return Voices
            .Where(voice => languageSet.Contains(voice.Language) &&
                            (gender == KokoroGender.Both || voice.Gender == gender))
            .ToArray();
    }

    private static KokoroVoiceCatalogEntry CreateEntry(string name)
    {
        return new KokoroVoiceCatalogEntry(
            name,
            (KokoroLanguage)name[0],
            (KokoroGender)name[1]);
    }
}
