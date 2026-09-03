namespace KokoroSharp.Adapters;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Python-free text normalization for the FastThaiG2P dictionary frontend.
/// </summary>
/// <remarks>
/// This is a native port of the upstream FastThaiG2P normalization order. It
/// expands the speakable representation of numbers, identifiers, symbols,
/// abbreviations, and other common Thai input rather than trying to make the
/// acoustic model interpret punctuation or digits itself. The pronunciation
/// dictionary and any OOV fallback remain separate from this class.
/// </remarks>
public static class FastThaiTextNormalizer
{
    private static readonly IReadOnlyDictionary<char, string> ThaiDigits =
        new Dictionary<char, string>
        {
            ['0'] = "ศูนย์",
            ['1'] = "หนึ่ง",
            ['2'] = "สอง",
            ['3'] = "สาม",
            ['4'] = "สี่",
            ['5'] = "ห้า",
            ['6'] = "หก",
            ['7'] = "เจ็ด",
            ['8'] = "แปด",
            ['9'] = "เก้า"
        };

    private static readonly IReadOnlyDictionary<char, char> ThaiNumerals =
        new Dictionary<char, char>
        {
            ['๐'] = '0',
            ['๑'] = '1',
            ['๒'] = '2',
            ['๓'] = '3',
            ['๔'] = '4',
            ['๕'] = '5',
            ['๖'] = '6',
            ['๗'] = '7',
            ['๘'] = '8',
            ['๙'] = '9'
        };

    private static readonly IReadOnlyDictionary<char, string> LetterToThai =
        new Dictionary<char, string>
        {
            ['A'] = "เอ",
            ['B'] = "บี",
            ['C'] = "ซี",
            ['D'] = "ดี",
            ['E'] = "อี",
            ['F'] = "เอฟ",
            ['G'] = "จี",
            ['H'] = "เอช",
            ['I'] = "ไอ",
            ['J'] = "เจ",
            ['K'] = "เค",
            ['L'] = "แอล",
            ['M'] = "เอ็ม",
            ['N'] = "เอ็น",
            ['O'] = "โอ",
            ['P'] = "พี",
            ['Q'] = "คิว",
            ['R'] = "อาร์",
            ['S'] = "เอส",
            ['T'] = "ที",
            ['U'] = "ยู",
            ['V'] = "วี",
            ['W'] = "ดับเบิลยู",
            ['X'] = "เอ็กซ์",
            ['Y'] = "วาย",
            ['Z'] = "แซด"
        };

    private static readonly IReadOnlyDictionary<string, string> Abbreviations =
        CreateMap(
            // Months
            ("ม.ค.", "มกราคม"), ("ก.พ.", "กุมภาพันธ์"), ("มี.ค.", "มีนาคม"),
            ("เม.ย.", "เมษายน"), ("พ.ค.", "พฤษภาคม"), ("มิ.ย.", "มิถุนายน"),
            ("ก.ค.", "กรกฎาคม"), ("ส.ค.", "สิงหาคม"), ("ก.ย.", "กันยายน"),
            ("ต.ค.", "ตุลาคม"), ("พ.ย.", "พฤศจิกายน"), ("ธ.ค.", "ธันวาคม"),
            // Eras
            ("พ.ศ.", "พุทธศักราช"), ("ค.ศ.", "คริสต์ศักราช"),
            // Civil, academic, medical, and professional titles
            ("น.ส.", "นางสาว"), ("นส.", "นางสาว"), ("ดร.", "ด็อกเตอร์"),
            ("ผศ.", "ผู้ช่วยศาสตราจารย์"), ("รศ.", "รองศาสตราจารย์"),
            ("นพ.", "นายแพทย์"), ("พญ.", "แพทย์หญิง"), ("ทพ.", "ทันตแพทย์"),
            ("ทพญ.", "ทันตแพทย์หญิง"), ("น.สพ.", "นายสััตวแพทย์"),
            ("สพ.ญ.", "สัตวแพทย์หญิง"), ("ภก.", "เภสัชกร"), ("ภญ.", "เภสัชกรหญิง"),
            ("ทนพ.", "เทคนิคการแพทย์"), ("กภ.", "กายภาพบำบัด"),
            // Army officers
            ("พล.อ.", "พลเอก"), ("พล.ท.", "พลโท"), ("พล.ต.", "พลตรี"),
            ("พ.อ.", "พันเอก"), ("พ.ท.", "พันโท"), ("พ.ต.", "พันตรี"),
            ("ร.อ.", "ร้อยเอก"), ("ร.ท.", "ร้อยโท"), ("ร.ต.", "ร้อยตรี"),
            // Army NCOs
            ("จ.ส.อ.", "จ่าสิบเอก"), ("จ.ส.ท.", "จ่าสิบโท"), ("จ.ส.ต.", "จ่าสิบตรี"),
            ("ส.อ.", "สิบเอก"), ("ส.ท.", "สิบโท"), ("ส.ต.", "สิบตรี"),
            // Police
            ("พล.ต.อ.", "พลตำรวจเอก"), ("พล.ต.ท.", "พลตำรวจโท"), ("พล.ต.ต.", "พลตำรวจตรี"),
            ("พ.ต.อ.", "พันตำรวจเอก"), ("พ.ต.ท.", "พันตำรวจโท"), ("พ.ต.ต.", "พันตำรวจตรี"),
            ("ร.ต.อ.", "ร้อยตำรวจเอก"), ("ร.ต.ท.", "ร้อยตำรวจโท"), ("ร.ต.ต.", "ร้อยตำรวจตรี"),
            ("ด.ต.", "ดาบตำรวจ"),
            // Navy
            ("พล.ร.อ.", "พลเรือเอก"), ("พล.ร.ท.", "พลเรือโท"), ("พล.ร.ต.", "พลเรือตรี"),
            ("น.อ.", "นาวาเอก"), ("น.ท.", "นาวาโท"), ("น.ต.", "นาวาตรี"),
            ("พ.จ.อ.", "พันจ่าเอก"), ("พ.จ.ท.", "พันจ่าโท"), ("พ.จ.ต.", "พันจ่าตรี"),
            ("จ.อ.", "จ่าเอก"), ("จ.ท.", "จ่าโท"), ("จ.ต.", "จ่าตรี"),
            // Air force
            ("พล.อ.อ.", "พลอากาศเอก"), ("พล.อ.ท.", "พลอากาศโท"), ("พล.อ.ต.", "พลอากาศตรี"),
            ("พ.อ.อ.", "พันจ่าอากาศเอก"), ("พ.อ.ท.", "พันจ่าอากาศโท"),
            ("พ.อ.ต.", "พันจ่าอากาศตรี"),
            // Royal and noble
            ("ม.จ.", "หม่อมเจ้า"), ("ม.ร.ว.", "หม่อมราชวงศ์"), ("ม.ล.", "หม่อมหลวง"),
            // Common
            ("กทม.", "กรุงเทพมหานคร"), ("รร.", "โรงเรียน"), ("ร.ร.", "โรงเรียน"),
            ("รพ.", "โรงพยาบาล"), ("ร.พ.", "โรงพยาบาล"), ("บจก.", "บริษัทจำกัด"),
            ("ฯลฯ", "เป็นต้น"));

    private static readonly IReadOnlyDictionary<string, string> Symbols =
        CreateMap(
            ("%", "เปอร์เซ็นต์"),
            ("°C", "องศาเซลเซียส"),
            ("°F", "องศาฟาเรนไฮต์"),
            ("°", "องศา"),
            ("@", " แอท "),
            ("/", " ทับ "));

    private static readonly IReadOnlyDictionary<string, string> EnglishAbbreviations =
        CreateMap(
            // Finance
            ("thb", "บาท"), ("usd", "ดอลลาร์"), ("eur", "ยูโร"), ("vat", "แวต"),
            ("pin", "พิน"),
            // Technology
            ("ram", "แรม"), ("wifi", "ไวไฟ"), ("otp", "โอทีพี"),
            // Health
            ("covid", "โควิด"),
            // Organizations
            ("fifa", "ฟีฟ่า"),
            // Brands and internet words
            ("line", "ไลน์"), ("facebook", "เฟซบุ๊ก"), ("instagram", "อินสตาแกรม"),
            ("amazon", "อมาซอน"), ("twitter", "ทวิตเตอร์"), ("google", "กูเกิล"),
            ("youtube", "ยูทูบ"), ("tiktok", "ติ๊กต็อก"), ("gmail", "จีเมล"),
            ("hotmail", "ฮอตเมล"), ("email", "อีเมล"), ("com", "คอม"), ("net", "เน็ต"),
            ("app", "แอป"), ("lazada", "ลาซาด้า"), ("shopee", "ช้อปปี้"),
            ("grab", "แกร็บ"), ("uber", "อูเบอร์"), ("whatsapp", "วอทส์แอป"),
            ("paypal", "เพย์พาล"), ("promptpay", "พร้อมเพย์"), ("truemoney", "ทรูมันนี่"));

    private static readonly IReadOnlyDictionary<string, string> Units =
        CreateMap(
            ("km", "กิโลเมตร"), ("cm", "เซนติเมตร"), ("mm", "มิลลิเมตร"),
            ("ml", "มิลลิลิตร"), ("kwh", "กิโลวัตต์ชั่วโมง"), ("kw", "กิโลวัตต์"),
            ("mb", "เมกะไบต์"), ("gb", "กิกะไบต์"), ("tb", "เทราไบต์"),
            ("kb", "กิโลไบต์"), ("mbps", "เมกะบิตต่อวินาที"),
            ("กม.", "กิโลเมตร"), ("ซม.", "เซนติเมตร"), ("มม.", "มิลลิเมตร"),
            ("ตร.กม.", "ตารางกิโลเมตร"), ("ตร.ม.", "ตารางเมตร"),
            ("ตร.ซม.", "ตารางเซนติเมตร"), ("ตร.มม.", "ตารางมิลลิเมตร"),
            ("ตร.ว.", "ตารางวา"), ("ลบ.ม.", "ลูกบาศก์เมตร"),
            ("ลบ.ซม.", "ลูกบาศก์เซนติเมตร"), ("มล.", "มิลลิลิตร"),
            ("กล.", "กิโลลิตร"), ("กก.", "กิโลกรัม"), ("มก.", "มิลลิกรัม"),
            ("kg", "กิโลกรัม"), ("mg", "มิลลิกรัม"));

    private static readonly IReadOnlyDictionary<string, string> EmailWords =
        CreateMap(
            ("gmail", "จีเมล"), ("hotmail", "ฮอตเมล"), ("yahoo", "ยาฮู"),
            ("outlook", "เอาต์ลุก"), ("com", "คอม"), ("net", "เน็ต"),
            ("org", "ออร์ก"), ("mail", "เมล"), ("email", "อีเมล"));

    private static readonly IReadOnlyDictionary<char, string> EmailSeparators =
        new Dictionary<char, string>
        {
            ['@'] = " แอท ",
            ['.'] = " ดอท ",
            ['-'] = " ขีด ",
            ['_'] = " ขีดล่าง "
        };

    private static readonly Regex SymbolPattern =
        CreateLiteralPattern(Symbols.Keys);

    private static readonly Regex EnglishAbbreviationPattern =
        new(
            @"\b(" + CreateAlternation(EnglishAbbreviations.Keys) + ")",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex UnitPattern =
        new(
            @"(?<=[0-9])\s*(" + CreateAlternation(Units.Keys) + ")",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ThaiNumeralPattern =
        new("[๐-๙]+", RegexOptions.Compiled);

    private static readonly Regex TimePattern =
        new(
            @"\b([0-9]{1,2}):([0-9]{2})(?:\s*(?:นาฬิกา|น\.))?",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EmailPattern =
        new("[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}", RegexOptions.Compiled);

    private static readonly Regex PhonePattern =
        new("[0-9]{2,4}[-\\.][0-9]{3,4}[-\\.][0-9]{3,4}", RegexOptions.Compiled);

    private static readonly Regex AlphaNumericIdPattern =
        new(
            "[A-Za-z]+[-]?[0-9]+[-0-9A-Za-z]*|[0-9]+[-]?[A-Za-z]+[-0-9A-Za-z]*",
            RegexOptions.Compiled);

    private static readonly Regex CommaNumberPattern =
        new("-?[0-9]{1,3}(?:,[0-9]{3})+(?:\\.[0-9]+)?", RegexOptions.Compiled);

    private static readonly Regex NumberPattern =
        new("-?[0-9]+(?:\\.[0-9]+)?", RegexOptions.Compiled);

    private static readonly Regex AbbreviationPattern =
        new(CreateAlternation(Abbreviations.Keys), RegexOptions.Compiled);

    private static readonly Regex LatinResiduePattern =
        new("[A-Za-z]+", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes text in the same broad order as FastThaiG2P's Python
    /// frontend, without importing Python or pythainlp into the host process.
    /// </summary>
    public static string Normalize(string text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
            return text;

        text = ExpandMaiyamok(text);
        text = ReplaceThaiNumerals(text);
        text = EmailPattern.Replace(text, EmailToThai);
        text = EnglishAbbreviationPattern.Replace(text, ExpandEnglishAbbreviation);
        text = UnitPattern.Replace(text, ExpandUnit);
        text = SymbolPattern.Replace(text, ExpandSymbol);
        text = TimePattern.Replace(text, TimeToThai);
        text = PhonePattern.Replace(text, PhoneNumberToThai);
        text = AlphaNumericIdPattern.Replace(text, AlphaNumericToThai);
        text = CommaNumberPattern.Replace(text, StripCommasAndConvert);
        text = NumberPattern.Replace(text, NumberOrIdToThai);
        text = AbbreviationPattern.Replace(text, ExpandAbbreviation);
        text = LatinResiduePattern.Replace(text, SpellLatinResidue);

        // Hyphens remaining in Thai names are connectors, not speakable
        // tokens (the upstream normalizer makes the same final substitution).
        return text.Replace("-", " ", StringComparison.Ordinal);
    }

    private static string ReplaceThaiNumerals(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
            builder.Append(ThaiNumerals.TryGetValue(character, out var arabic) ? arabic : character);
        return builder.ToString();
    }

    private static string ExpandMaiyamok(string text)
    {
        if (!text.Contains('ๆ', StringComparison.Ordinal))
            return text;

        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            if (text[index] != 'ๆ')
            {
                result.Append(text[index++]);
                continue;
            }

            // FastThaiG2P delegates this to pythainlp's word tokenizer. The
            // native path has no Python tokenizer, so use the preceding Thai
            // run and handle attached/separated repeated markers. This is
            // exact for the normal word-level forms and deliberately drops an
            // orphan marker rather than passing an unpronounceable symbol on.
            while (result.Length > 0 && char.IsWhiteSpace(result[^1]))
                result.Length--;

            var start = PreviousThaiRunStart(result);
            var word = start < result.Length
                ? result.ToString(start, result.Length - start)
                : string.Empty;
            if (word.Length == 0)
            {
                index++;
                continue;
            }

            result.Append(word);
            index++;
            while (index < text.Length)
            {
                var whitespaceStart = index;
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;

                if (index >= text.Length || text[index] != 'ๆ')
                {
                    // Preserve whitespace that belongs after the repeated
                    // word. Whitespace between consecutive markers is dropped.
                    if (index > whitespaceStart)
                        result.Append(text[whitespaceStart..index]);
                    break;
                }

                result.Append(word);
                index++;
            }
        }
        return result.ToString();
    }

    private static int PreviousThaiRunStart(StringBuilder value)
    {
        var index = value.Length;
        while (index > 0 && IsThaiBlockCharacter(value[index - 1]))
            index--;
        return index;
    }

    private static bool IsThaiBlockCharacter(char value) => value is >= '\u0E00' and <= '\u0E7F';

    private static string EmailToThai(Match match)
    {
        var result = new StringBuilder();
        var token = new StringBuilder();

        void Flush()
        {
            if (token.Length == 0)
                return;

            var value = token.ToString();
            if (EmailWords.TryGetValue(value.ToLowerInvariant(), out var word))
            {
                result.Append(word);
            }
            else
            {
                foreach (var character in value)
                {
                    if (char.IsDigit(character) && ThaiDigits.TryGetValue(character, out var digit))
                        result.Append(digit);
                    else if (LetterToThai.TryGetValue(char.ToUpperInvariant(character), out var letter))
                        result.Append(letter);
                    else
                        result.Append(character);
                }
            }
            token.Clear();
        }

        foreach (var character in match.Value)
        {
            if (EmailSeparators.TryGetValue(character, out var separator))
            {
                Flush();
                result.Append(separator);
            }
            else
            {
                token.Append(character);
            }
        }
        Flush();
        return result.ToString();
    }

    private static string TimeToThai(Match match)
    {
        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var result = IntegerToThai(hours.ToString(CultureInfo.InvariantCulture)) + "นาฬิกา";
        if (minutes != 0)
            result += IntegerToThai(minutes.ToString(CultureInfo.InvariantCulture)) + "นาที";
        return result;
    }

    private static string PhoneNumberToThai(Match match)
    {
        var parts = match.Value.Split(['-', '.'], StringSplitOptions.None);
        return string.Join(' ', parts.Select(DigitsToThai));
    }

    private static string AlphaNumericToThai(Match match)
    {
        var result = new StringBuilder(match.Value.Length * 2);
        foreach (var character in match.Value)
        {
            if (ThaiDigits.TryGetValue(character, out var digit))
                result.Append(digit);
            else if (LetterToThai.TryGetValue(char.ToUpperInvariant(character), out var letter))
                result.Append(letter);
            else if (character == '-')
                result.Append(' ');
            else
                result.Append(character);
        }
        return result.ToString();
    }

    private static string StripCommasAndConvert(Match match) =>
        DecimalToThai(match.Value.Replace(",", string.Empty, StringComparison.Ordinal));

    private static string NumberOrIdToThai(Match match)
    {
        var text = match.Value;
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        var raw = negative ? text[1..] : text;
        var integerPart = raw.Split('.', 2)[0];

        if (integerPart.Length >= 7)
        {
            var result = DigitsToThai(integerPart);
            if (raw.Contains('.', StringComparison.Ordinal))
            {
                var decimalPart = raw.Split('.', 2)[1];
                result += "จุด" + DigitsToThai(decimalPart);
            }
            return negative ? "ลบ" + result : result;
        }

        return DecimalToThai(text);
    }

    private static string DigitsToThai(string digits)
    {
        var result = new StringBuilder(digits.Length * 3);
        foreach (var character in digits)
        {
            if (ThaiDigits.TryGetValue(character, out var word))
                result.Append(word);
        }
        return result.ToString();
    }

    private static string DecimalToThai(string text)
    {
        var parts = text.Split('.', 2);
        var integerPart = parts[0].Length == 0 ? "0" : parts[0];
        var result = IntegerToThai(integerPart);
        if (parts.Length == 2)
            result += "จุด" + DigitsToThai(parts[1]);
        return result;
    }

    private static string IntegerToThai(string text)
    {
        var negative = text.StartsWith("-", StringComparison.Ordinal);
        var digits = negative ? text[1..] : text;
        digits = digits.TrimStart('0');
        if (digits.Length == 0)
            return "ศูนย์";

        var groups = new List<string>();
        for (var end = digits.Length; end > 0; end -= 6)
        {
            var start = Math.Max(0, end - 6);
            groups.Add(digits[start..end]);
        }

        var parts = new List<string>(groups.Count);
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var groupNumber = int.Parse(groups[index], CultureInfo.InvariantCulture);
            if (groupNumber == 0)
                continue;

            var groupText = NumberGroupToThai(groupNumber);
            var suffix = string.Concat(Enumerable.Repeat("ล้าน", index));
            parts.Add(groupText + suffix);
        }

        var result = string.Concat(parts);
        return negative ? "ลบ" + result : result;
    }

    private static string NumberGroupToThai(int value)
    {
        if (value == 0)
            return string.Empty;

        var result = new StringBuilder();
        var remaining = value;
        foreach (var (place, suffix) in new[]
        {
            (100000, "แสน"),
            (10000, "หมื่น"),
            (1000, "พัน"),
            (100, "ร้อย"),
            (10, "สิบ"),
            (1, string.Empty)
        })
        {
            var digit = remaining / place;
            remaining %= place;
            if (digit == 0)
                continue;

            if (place == 1)
            {
                result.Append(value > 1 && digit == 1 ? "เอ็ด" : ThaiDigits[(char)('0' + digit)]);
            }
            else if (place == 10 && digit == 1)
            {
                result.Append("สิบ");
            }
            else if (place == 10 && digit == 2)
            {
                result.Append("ยี่สิบ");
            }
            else
            {
                result.Append(ThaiDigits[(char)('0' + digit)]);
                result.Append(suffix);
            }
        }
        return result.ToString();
    }

    private static string ExpandAbbreviation(Match match) => Abbreviations[match.Value];

    private static string SpellLatinResidue(Match match)
    {
        var result = new StringBuilder(match.Value.Length * 2);
        foreach (var character in match.Value)
        {
            if (LetterToThai.TryGetValue(char.ToUpperInvariant(character), out var letter))
                result.Append(letter);
        }
        return result.ToString();
    }

    private static string ExpandSymbol(Match match) => Symbols[match.Value];

    private static string ExpandEnglishAbbreviation(Match match) =>
        EnglishAbbreviations[match.Value.ToLowerInvariant()];

    private static string ExpandUnit(Match match) => Units[match.Groups[1].Value];

    private static IReadOnlyDictionary<string, string> CreateMap(
        params (string Key, string Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

    private static Regex CreateLiteralPattern(IEnumerable<string> values) =>
        new(CreateAlternation(values), RegexOptions.Compiled);

    private static string CreateAlternation(IEnumerable<string> values) =>
        string.Join('|', values.OrderByDescending(value => value.Length).Select(Regex.Escape));
}
