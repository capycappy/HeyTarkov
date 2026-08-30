using System.Reflection;
using System.Text;
using System.Text.Json;

namespace HeyTarkov;

/// <summary>
/// Katakana readings for the English words that appear in task names, so a
/// Japanese speaker can say "ウェットジョブパートフォー" instead of fighting
/// English pronunciation.
/// </summary>
public sealed class JapaneseLexicon
{
    private readonly Dictionary<string, string[]> _words;
    private readonly Dictionary<string, string[]> _numbers;

    private JapaneseLexicon(
        Dictionary<string, string[]> words,
        Dictionary<string, string[]> numbers)
    {
        _words = words;
        _numbers = numbers;
    }

    public int WordCount => _words.Count;

    public static JapaneseLexicon Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("japanese-lexicon.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var document = JsonDocument.Parse(stream);

        return new JapaneseLexicon(
            ReadSection(document.RootElement, "words"),
            ReadSection(document.RootElement, "numbers"));
    }

    private static Dictionary<string, string[]> ReadSection(JsonElement root, string section)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.GetProperty(section).EnumerateObject())
        {
            result[property.Name] = property.Value
                .EnumerateArray()
                .Select(v => v.GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToArray();
        }

        return result;
    }

    public string[]? Readings(string token)
    {
        var table = IsAllDigits(token) ? _numbers : _words;
        return table.TryGetValue(token, out var readings) ? readings : null;
    }

    private static bool IsAllDigits(string token)
    {
        foreach (var c in token)
            if (!char.IsDigit(c)) return false;
        return token.Length > 0;
    }
}

/// <summary>
/// Builds the katakana phrases for a task name by looking each word up in the
/// lexicon. Numbers carry two readings (フォー / ヨン), which is where nearly
/// all of the real ambiguity lives.
/// </summary>
public sealed class JapaneseForms
{
    private const int MaxVariantsPerTask = 8;

    /// <summary>
    /// Spelling out a name longer than this is not something anyone would
    /// actually do, and every extra phrase costs grammar-load time.
    /// </summary>
    private const int MaxSpelledLetters = 30;

    private static readonly Dictionary<char, string> LetterReadings = new()
    {
        ['a'] = "エー", ['b'] = "ビー", ['c'] = "シー", ['d'] = "ディー",
        ['e'] = "イー", ['f'] = "エフ", ['g'] = "ジー", ['h'] = "エイチ",
        ['i'] = "アイ", ['j'] = "ジェー", ['k'] = "ケー", ['l'] = "エル",
        ['m'] = "エム", ['n'] = "エヌ", ['o'] = "オー", ['p'] = "ピー",
        ['q'] = "キュー", ['r'] = "アール", ['s'] = "エス", ['t'] = "ティー",
        ['u'] = "ユー", ['v'] = "ブイ", ['w'] = "ダブリュー", ['x'] = "エックス",
        ['y'] = "ワイ", ['z'] = "ゼット",
    };

    private readonly JapaneseLexicon _lexicon;
    private readonly SortedSet<string> _unknown = new(StringComparer.OrdinalIgnoreCase);

    public JapaneseForms(JapaneseLexicon lexicon) => _lexicon = lexicon;

    /// <summary>Words with no reading. A task containing one is left out of the
    /// Japanese grammar rather than being given a bogus pronunciation.</summary>
    public IReadOnlyCollection<string> UnknownWords => _unknown;

    public IReadOnlyList<string> For(string taskName)
    {
        var tokens = SpokenForms.Tokenize(taskName);
        if (tokens.Count == 0) return Array.Empty<string>();

        var perToken = new List<string[]>(tokens.Count);

        foreach (var token in tokens)
        {
            var readings = _lexicon.Readings(token);
            if (readings is null || readings.Length == 0)
            {
                _unknown.Add(token);
                return Array.Empty<string>();
            }
            perToken.Add(readings);
        }

        var joined = Expand(perToken);

        // Both the run-together form and a space-separated one: the recognizer
        // may segment either way, and both map back to the same task.
        // Word-separated, never run together. A phrase with no internal spaces is
        // a single word to SAPI, and a real pause inside a single word kills the
        // match - which is exactly what happens when someone speaks deliberately.
        // Separate words also match continuous speech, so this costs nothing.
        var forms = new List<string>(joined.Count);
        foreach (var (_, spaced) in joined) Add(forms, spaced);

        return forms;
    }

    /// <summary>
    /// The escape hatch for a name nobody can pronounce: spell it out letter by
    /// letter. "Fall Ailment" -> エフエーエルエル エーアイエルエムイーエヌティー.
    /// Numbers keep their normal reading - "Part 4" is ピーエーアールティーフォー,
    /// not a digit spelled as letters.
    ///
    /// Kept apart from <see cref="For"/> because these phrases are long and slow
    /// to compile, so they are loaded into the recognizer separately.
    /// </summary>
    public IReadOnlyList<string> SpelledFor(string taskName)
    {
        var spelled = BuildSpelled(SpokenForms.Tokenize(taskName));
        return spelled is null ? Array.Empty<string>() : new[] { spelled };
    }

    /// <summary>The spelling, for showing the user how to say it.</summary>
    public string? SpelledHint(string taskName) => BuildSpelled(SpokenForms.Tokenize(taskName));

    /// <summary>
    /// One katakana letter name per word: "エフ エー エル エル エー アイ …".
    /// Spelling is inherently slow and gappy speech, so every letter has to be
    /// its own word in the grammar for the pauses to be legal. It also keeps the
    /// recognizer's lexicon down to ~26 entries instead of ~570 long ones, which
    /// makes the grammar far cheaper to compile.
    /// </summary>
    private string? BuildSpelled(List<string> tokens)
    {
        var letters = tokens.Sum(t => t.Count(char.IsLetter));
        if (letters is 0 or > MaxSpelledLetters) return null;

        var words = new List<string>();

        foreach (var token in tokens)
        {
            if (IsAllDigits(token))
            {
                var readings = _lexicon.Readings(token);
                if (readings is null || readings.Length == 0) return null;
                words.Add(readings[0]);
                continue;
            }

            foreach (var c in token)
            {
                if (!char.IsLetter(c)) continue;   // apostrophes are not spelled
                if (!LetterReadings.TryGetValue(char.ToLowerInvariant(c), out var reading))
                    return null;
                words.Add(reading);
            }
        }

        return words.Count == 0 ? null : string.Join(' ', words);
    }

    private static bool IsAllDigits(string token)
    {
        foreach (var c in token)
            if (!char.IsDigit(c)) return false;
        return token.Length > 0;
    }

    private static void Add(List<string> forms, string value)
    {
        if (value.Length > 0 && !forms.Contains(value, StringComparer.Ordinal))
            forms.Add(value);
    }

    private static List<(string Run, string Spaced)> Expand(List<string[]> perToken)
    {
        var results = new List<(StringBuilder Run, StringBuilder Spaced)>
        {
            (new StringBuilder(), new StringBuilder()),
        };

        foreach (var readings in perToken)
        {
            var next = new List<(StringBuilder, StringBuilder)>();

            foreach (var (run, spaced) in results)
            {
                // Only branch while there is room left in the budget; beyond that
                // every token contributes its primary reading only.
                var take = results.Count * readings.Length <= MaxVariantsPerTask
                    ? readings.Length
                    : 1;

                for (var i = 0; i < take; i++)
                {
                    var runNext = new StringBuilder(run.ToString()).Append(readings[i]);
                    var spacedNext = new StringBuilder(spaced.ToString());
                    if (spacedNext.Length > 0) spacedNext.Append(' ');
                    spacedNext.Append(readings[i]);
                    next.Add((runNext, spacedNext));
                }
            }

            results = next;
        }

        return results.Select(r => (r.Item1.ToString(), r.Item2.ToString())).ToList();
    }

    /// <summary>
    /// Canonical key for matching: drop spaces, fold hiragana onto katakana, and
    /// normalize the long-vowel marks the recognizer may or may not emit.
    /// </summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var raw in text)
        {
            if (char.IsWhiteSpace(raw)) continue;

            var c = raw;

            // Hiragana -> katakana.
            if (c is >= 'ぁ' and <= 'ゖ') c = (char)(c + 0x60);

            // Halfwidth/fullwidth prolonged sound marks -> the standard one.
            if (c is 'ｰ' or '‐' or '―' or '－' or '-') c = 'ー';

            sb.Append(c);
        }

        return sb.ToString();
    }
}
