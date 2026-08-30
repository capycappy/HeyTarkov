using System.Text;

namespace HeyTarkov;

/// <summary>
/// Turns a wiki task name into the phrases a person actually says. These are
/// also the phrases handed to the recognizer as its closed vocabulary, so a
/// name has to cover every reasonable reading:
///
///   "Wet Job - Part 4"  -> "wet job part 4" / "wet job part four"
///   "Gunsmith - AKS-74N" -> "gunsmith aks 74 n" / "gunsmith aks seventy four n"
///                         / "gunsmith a k s seven four n"
/// </summary>
public static class SpokenForms
{
    private enum NumberStyle
    {
        /// <summary>Leave "74" as digits.</summary>
        Digits,

        /// <summary>"74" -> "seventy four".</summary>
        Words,

        /// <summary>"74" -> "seven four", how model numbers are usually read.</summary>
        PerDigit,
    }

    public static IReadOnlyList<string> For(string taskName)
    {
        var tokens = Tokenize(taskName);

        var forms = new List<string>(6);
        AddWithApostropheVariant(forms, Render(tokens, NumberStyle.Digits, spellAcronyms: false));
        AddWithApostropheVariant(forms, Render(tokens, NumberStyle.Words, spellAcronyms: false));
        AddWithApostropheVariant(forms, Render(tokens, NumberStyle.PerDigit, spellAcronyms: true));

        return forms;
    }

    /// <summary>
    /// Canonical key for matching. Idempotent, so running it over a phrase that
    /// already came out of <see cref="For"/> leaves it unchanged.
    /// </summary>
    public static string Normalize(string text) =>
        Render(Tokenize(text), NumberStyle.Digits, spellAcronyms: false);

    /// <summary>"wet job part 4" -> "wet job part four".</summary>
    public static string DigitsToWords(string text) =>
        Render(Tokenize(text), NumberStyle.Words, spellAcronyms: false);

    private static void AddWithApostropheVariant(List<string> forms, string value)
    {
        Add(forms, value);
        if (value.Contains('\'')) Add(forms, value.Replace("'", ""));
    }

    private static void Add(List<string> forms, string value)
    {
        if (value.Length > 0 && !forms.Contains(value, StringComparer.Ordinal))
            forms.Add(value);
    }

    /// <summary>
    /// Splits on whitespace, dashes and slashes, drops the punctuation the
    /// recognizer cannot pronounce, and breaks letter/digit runs apart so
    /// "74N" becomes "74" + "N".
    /// </summary>
    internal static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var currentIsDigit = false;

        void Flush()
        {
            if (current.Length == 0) return;
            tokens.Add(current.ToString());
            current.Clear();
        }

        foreach (var raw in text)
        {
            // A few task names use curly quotes and em/en dashes.
            var c = raw switch
            {
                '’' or '‘' => '\'',
                '—' or '–' => '-',
                _ => raw,
            };

            if (char.IsWhiteSpace(c) || c is '-' or '_' or '/' or '\\')
            {
                Flush();
                currentIsDigit = false;
                continue;
            }

            if (char.IsDigit(c))
            {
                if (current.Length > 0 && !currentIsDigit) Flush();
                currentIsDigit = true;
                current.Append(c);
                continue;
            }

            if (char.IsLetter(c) || c == '\'')
            {
                if (current.Length > 0 && currentIsDigit) Flush();
                currentIsDigit = false;
                current.Append(c);
                continue;
            }

            // '?', '!', '.', ',', ':' and friends are simply not spoken.
        }

        Flush();
        return tokens;
    }

    private static string Render(List<string> tokens, NumberStyle numbers, bool spellAcronyms)
    {
        var parts = new List<string>(tokens.Count);

        foreach (var token in tokens)
        {
            if (IsAllDigits(token))
            {
                parts.Add(RenderNumber(token, numbers));
                continue;
            }

            if (spellAcronyms && IsAcronym(token))
            {
                parts.Add(string.Join(' ', token.ToLowerInvariant().ToCharArray()));
                continue;
            }

            parts.Add(token.ToLowerInvariant());
        }

        return string.Join(' ', parts);
    }

    private static bool IsAllDigits(string token)
    {
        foreach (var c in token)
            if (!char.IsDigit(c)) return false;
        return token.Length > 0;
    }

    /// <summary>
    /// "AKM", "MPX", "SKS" get spelled out letter by letter; ordinary words and
    /// pronounceable names like "VAL" or "Vector" do not. Uppercase in the wiki
    /// name is the signal, capped at 5 letters so real words are never split.
    /// </summary>
    private static bool IsAcronym(string token)
    {
        if (token.Length is < 2 or > 5) return false;
        foreach (var c in token)
            if (!char.IsLetter(c) || !char.IsUpper(c)) return false;
        return true;
    }

    private static string RenderNumber(string digits, NumberStyle style)
    {
        switch (style)
        {
            case NumberStyle.Words when int.TryParse(digits, out var n) && n <= 999:
                return NumberToWords(n);

            case NumberStyle.PerDigit:
                return string.Join(' ', digits.Select(d => Ones[d - '0']));

            default:
                return digits;
        }
    }

    private static readonly string[] Ones =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight",
        "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen",
        "sixteen", "seventeen", "eighteen", "nineteen",
    };

    private static readonly string[] Tens =
    {
        "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy",
        "eighty", "ninety",
    };

    public static string NumberToWords(int n)
    {
        if (n < 20) return Ones[n];

        if (n < 100)
        {
            var tens = Tens[n / 10];
            var ones = n % 10;
            return ones == 0 ? tens : $"{tens} {Ones[ones]}";
        }

        var hundreds = $"{Ones[n / 100]} hundred";
        var rest = n % 100;
        return rest == 0 ? hundreds : $"{hundreds} {NumberToWords(rest)}";
    }
}
