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
    private readonly Dictionary<string, string[]> _japanese;

    private JapaneseLexicon(
        Dictionary<string, string[]> words,
        Dictionary<string, string[]> numbers,
        Dictionary<string, string[]> japanese)
    {
        _words = words;
        _numbers = numbers;
        _japanese = japanese;
    }

    /// <summary>
    /// Japanese words whose reading the recognizer cannot be relied on to pick:
    /// 西棟 is にしとう to one person and にしむね to another. Each carries every
    /// reading it has, so a name containing it is heard whichever is used.
    /// </summary>
    public IReadOnlyCollection<string> AmbiguousWords => _japanese.Keys;

    public string[]? AmbiguousReadings(string word) =>
        _japanese.TryGetValue(word, out var readings) ? readings : null;

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
            ReadSection(document.RootElement, "numbers"),
            ReadSection(document.RootElement, "japanese"));
    }

    private static Dictionary<string, string[]> ReadSection(JsonElement root, string section)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(section, out var entries)) return result;

        foreach (var property in entries.EnumerateObject())
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
    /// A name as the game shows it can carry several words with more than one
    /// reading - "保養所 西棟306号室" has three - so it is allowed more.
    /// </summary>
    private const int MaxVariantsPerName = 16;

    /// <summary>
    /// Part of a name is said far more often than all of it, and there are many
    /// parts, so each gets fewer readings than a whole name - but enough for
    /// two ambiguous words in a row: "保養所 西棟" is three readings times
    /// three.
    /// </summary>
    private const int MaxVariantsPerFragment = 9;

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
    /// Phrases for a name as the game shows it in Japanese - "マークの刻まれた
    /// 廃工場の鍵", "RB-AM の鍵", "保養所 西棟306号室の鍵".
    ///
    /// The Japanese recognizer reads kanji from its own dictionary, so most of
    /// such a name goes into the grammar as it stands. Two things it cannot do:
    /// read Latin letters at all, and know which reading a person will use for
    /// a word like 西棟. So Latin runs become every reading the lexicon has for
    /// them - "OLI" is オリ to some players and オーエルアイ to others - and
    /// the few ambiguous Japanese words are offered each way. Room numbers keep
    /// their digits, which the recognizer reads itself, and gain the readings
    /// people actually use for them (サンマルロク).
    /// </summary>
    public IReadOnlyList<string> ForJapaneseName(string name) =>
        ForJapaneseName(name, MaxVariantsPerName);

    /// <summary>
    /// Runs of words from a Japanese name, for saying part of it - "保養所"
    /// for every key in the health resort, "保養所 西棟" for its west wing,
    /// "西棟306号室" for one room. Japanese has no spaces to split on, so a
    /// name is cut where it plainly has joints: after の, around Latin letters
    /// and numbers, and around the words that have more than one reading.
    ///
    /// Not every such run is worth listening for. "鍵" or "の鍵" alone would
    /// match all two hundred keys, and a bare number means nothing without the
    /// word in front of it.
    /// </summary>
    public IReadOnlyList<string> FragmentsOfJapaneseName(string name)
    {
        var words = JapaneseWords(name).ToList();
        if (words.Count < 2) return Array.Empty<string>();

        var result = new List<string>();

        for (var start = 0; start < words.Count; start++)
        {
            for (var end = start + 1; end <= words.Count; end++)
            {
                if (start == 0 && end == words.Count) continue;

                // A run ending on "…の" is said without it: "マーク", not "マークの".
                var text = string.Join(' ', words.GetRange(start, end - start)).TrimEnd('の', ' ');
                var plain = text.Replace(" ", "");

                if (plain.Length < 2 || plain is "の鍵" || plain.All(char.IsAsciiDigit)) continue;
                if (words[start] is "鍵" || words[start].StartsWith('の')) continue;

                foreach (var phrase in ForJapaneseName(text, MaxVariantsPerFragment)) Add(result, phrase);
            }
        }

        return result;
    }

    /// <summary>The joints of a Japanese name, in order.</summary>
    private IEnumerable<string> JapaneseWords(string name)
    {
        foreach (var (text, kind) in Segments(name))
        {
            if (kind != Segment.Japanese)
            {
                yield return text;
                continue;
            }

            foreach (var part in SplitAmbiguous(text).Select(options => options[0]))
            {
                var from = 0;

                for (var i = 0; i < part.Length; i++)
                {
                    if (part[i] != 'の') continue;

                    yield return part[from..(i + 1)];
                    from = i + 1;
                }

                if (from < part.Length) yield return part[from..];
            }
        }
    }

    private IReadOnlyList<string> ForJapaneseName(string name, int max)
    {
        var parts = new List<string[]>();

        foreach (var (text, kind) in Segments(name))
        {
            switch (kind)
            {
                case Segment.Latin:
                    parts.Add(LatinReadings(text));
                    break;

                case Segment.Digits:
                    var numbers = _lexicon.Readings(text);
                    parts.Add(numbers is { Length: > 0 } ? numbers.Prepend(text).ToArray() : new[] { text });
                    break;

                default:
                    parts.AddRange(SplitAmbiguous(text));
                    break;
            }
        }

        if (parts.Count == 0) return Array.Empty<string>();

        var forms = new List<string>();
        foreach (var (_, spaced) in Expand(parts, max)) Add(forms, spaced);
        return forms;
    }

    private enum Segment { Japanese, Latin, Digits }

    /// <summary>Punctuation the game puts in names and nobody says.</summary>
    private static readonly HashSet<char> Separators = new("-_/.,・「」『』\"“”'()（）");

    /// <summary>Runs of Japanese, Latin letters and digits, in order.</summary>
    private static IEnumerable<(string Text, Segment Kind)> Segments(string name)
    {
        var current = new StringBuilder();
        Segment? kind = null;

        foreach (var c in name)
        {
            Segment? next =
                char.IsAsciiLetter(c) || (c == '\'' && kind == Segment.Latin) ? Segment.Latin
                : char.IsAsciiDigit(c) ? Segment.Digits
                : char.IsWhiteSpace(c) || Separators.Contains(c) ? null
                : Segment.Japanese;

            if (next != kind && current.Length > 0 && kind is { } done)
            {
                yield return (current.ToString(), done);
                current.Clear();
            }

            kind = next;
            if (next is not null) current.Append(c);
        }

        if (current.Length > 0 && kind is { } last) yield return (current.ToString(), last);
    }

    /// <summary>
    /// Every reading the lexicon has for a Latin word, or the word spelled out
    /// when it has none - an unknown abbreviation is still sayable letter by
    /// letter.
    /// </summary>
    private string[] LatinReadings(string word)
    {
        if (_lexicon.Readings(word) is { Length: > 0 } readings) return readings;

        if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase)
            && _lexicon.Readings(word[..^2]) is { Length: > 0 } stem)
        {
            return stem.Select(r => r + "ズ").ToArray();
        }

        var spelled = string.Concat(word
            .Where(char.IsLetter)
            .Select(c => LetterReadings.GetValueOrDefault(char.ToLowerInvariant(c), "")));

        return new[] { spelled };
    }

    /// <summary>
    /// Splits a run of Japanese around the words with more than one reading,
    /// which are offered as written and in each reading.
    /// </summary>
    private IEnumerable<string[]> SplitAmbiguous(string text)
    {
        var at = 0;

        while (at < text.Length)
        {
            var hit = _lexicon.AmbiguousWords
                .Select(word => (Word: word, Index: text.IndexOf(word, at, StringComparison.Ordinal)))
                .Where(found => found.Index >= 0)
                .OrderBy(found => found.Index)
                .ThenByDescending(found => found.Word.Length)
                .FirstOrDefault();

            if (hit.Word is null)
            {
                yield return new[] { text[at..] };
                yield break;
            }

            if (hit.Index > at) yield return new[] { text[at..hit.Index] };

            yield return (_lexicon.AmbiguousReadings(hit.Word) ?? Array.Empty<string>())
                .Prepend(hit.Word)
                .ToArray();

            at = hit.Index + hit.Word.Length;
        }
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

    private static List<(string Run, string Spaced)> Expand(
        List<string[]> perToken, int max = MaxVariantsPerTask)
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
                var take = results.Count * readings.Length <= max
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
