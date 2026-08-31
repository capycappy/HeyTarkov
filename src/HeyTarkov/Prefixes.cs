namespace HeyTarkov;

/// <summary>
/// Leading fragments of a name, so it can be found without saying all of it.
/// "Broadcast - Part 4" is reachable by "Broadcast" and "Broadcast Part".
///
/// A prefix never identifies one entry - "Broadcast" covers six parts - so it
/// resolves to a candidate list rather than a single answer.
/// </summary>
public static class Prefixes
{
    /// <summary>
    /// Words too common to be worth listening for on their own. A grammar entry
    /// for "the" would fire constantly and match nothing useful.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "to", "in", "on", "at", "and", "or", "is", "it",
        "this", "that", "my", "our", "your", "their", "for", "from", "with",
        "no", "not", "up", "out", "off", "by", "as", "are", "you", "we",
    };

    /// <summary>Below this a fragment is too thin to be distinctive.</summary>
    private const int MinimumLetters = 4;

    /// <summary>
    /// Every leading word-run of the name, shortest first, excluding the whole
    /// name (that is already in the grammar as itself).
    /// </summary>
    public static IEnumerable<string> Of(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) yield break;

        for (var take = 1; take < words.Length; take++)
        {
            var prefix = string.Join(' ', words[..take]);

            // A run of nothing but filler words is not worth a grammar entry.
            if (words[..take].All(w => Stopwords.Contains(Trim(w)))) continue;
            if (prefix.Count(char.IsLetterOrDigit) < MinimumLetters) continue;

            yield return prefix;
        }
    }

    private static string Trim(string word) =>
        new(word.Where(char.IsLetterOrDigit).ToArray());
}
