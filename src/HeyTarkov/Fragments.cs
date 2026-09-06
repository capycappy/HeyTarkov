namespace HeyTarkov;

/// <summary>
/// The parts of a name that can be said on their own.
///
/// Not just the opening words: "The Punisher - Part 4" is reachable by
/// "Punisher", and "Disease History" by "History". Nobody remembers which
/// article a task name starts with, and the distinctive word is rarely the
/// first one.
///
/// A fragment never identifies one entry - "Punisher" covers six parts - so it
/// resolves to a candidate list rather than a single answer.
/// </summary>
public static class Fragments
{
    /// <summary>
    /// Words not worth listening for on their own. A grammar entry for "the"
    /// would fire constantly and match nothing useful.
    ///
    /// "Part" is here for a different reason: it is structural rather than
    /// descriptive. It names a position in a chain, never the thing itself, and
    /// a fragment starting there would answer "part two" with every second
    /// instalment in the game - fifty-nine of them.
    /// </summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "to", "in", "on", "at", "and", "or", "is", "it",
        "this", "that", "my", "our", "your", "their", "for", "from", "with",
        "no", "not", "up", "out", "off", "by", "as", "are", "you", "we",
        "part",
    };

    /// <summary>Below this a fragment is too thin to be distinctive.</summary>
    private const int MinimumLetters = 4;

    /// <summary>
    /// More is asked of a fragment that starts in the middle of a name.
    ///
    /// A short interior word - "Wave", "Gear", "Born" - is a poor thing to say
    /// out loud: it is rarely what anyone reaches for, and it is usually either
    /// a whole name already or too generic to narrow anything. Six letters drops
    /// about 230 such phrases from the grammar. "Punisher" and "History" - the
    /// words this exists for - are well clear of the line.
    ///
    /// It is not a fix for acoustic crowding. Raising it from four to six was
    /// tried for that and measured no difference: the grammar fell from 4,348
    /// phrases to 4,115 and every confidence stayed where it was.
    /// </summary>
    private const int MinimumInteriorLetters = 6;

    /// <summary>
    /// Every run of consecutive words in the name, excluding the whole name
    /// (that is already in the grammar as itself).
    ///
    /// A run may only open on a filler word if it is the start of the name -
    /// where "The Punisher" is a reasonable thing to say. Beginning one in the
    /// middle at "the" or "part" is not.
    /// </summary>
    public static IEnumerable<string> Of(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) yield break;

        for (var start = 0; start < words.Length; start++)
        {
            if (start > 0 && !CanOpen(words[start])) continue;

            for (var end = start + 1; end <= words.Length; end++)
            {
                if (start == 0 && end == words.Length) continue;

                var run = words[start..end];

                if (run.All(word => Stopwords.Contains(Trim(word)))) continue;

                var least = start == 0 ? MinimumLetters : MinimumInteriorLetters;
                if (run.Sum(word => word.Count(char.IsLetterOrDigit)) < least) continue;

                yield return string.Join(' ', run);
            }
        }
    }

    /// <summary>
    /// Punctuation is a word of its own once a name is split on spaces, and a
    /// fragment beginning at the dash in "Broadcast - Part 1" is not something
    /// anyone would say.
    /// </summary>
    private static bool CanOpen(string word)
    {
        var trimmed = Trim(word);
        return trimmed.Length > 0 && !Stopwords.Contains(trimmed);
    }

    private static string Trim(string word) =>
        new(word.Where(char.IsLetterOrDigit).ToArray());
}
