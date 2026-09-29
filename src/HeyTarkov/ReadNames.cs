using System.Text.RegularExpressions;

namespace HeyTarkov;

/// <summary>What one line of a screenshot turned out to be.</summary>
/// <param name="Read">The line as the recognizer read it.</param>
/// <param name="Task">The entry it was taken to mean.</param>
/// <param name="Score">How sure that is, 0 to 1.</param>
public readonly record struct ReadName(string Read, WikiEntry Task, double Score);

/// <summary>
/// Turning the lines of a screenshot into entries from the catalog.
///
/// The recognizer gets most of a name right and mangles the rest - "Invasive"
/// comes back as "lnvasive", a dash comes back as a wide one - so nothing here
/// insists on an exact reading. It leans on the same matching the search box
/// uses, and every match is shown with the line it came from, because the one
/// thing worse than a name not being found is the wrong page opening for it.
/// </summary>
public static partial class ReadNames
{
    /// <summary>
    /// Below this the match is a guess. A task name is several words long, and
    /// the recognizer rarely damages more than a letter or two of it.
    /// </summary>
    private const double Confident = 0.62;

    /// <summary>Shorter than this, a line is a column heading or a number.</summary>
    private const int Shortest = 4;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>Letters, digits and the spaces between them - everything else
    /// the recognizer invents around the edges.</summary>
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Noise();

    /// <summary>
    /// The entries the lines name, in the order they were read, one per entry.
    ///
    /// Both halves of the catalog are searched: a screenshot of the task list
    /// holds tasks, but the same picture of a stash or a key bar should not
    /// come back empty because a button was in the wrong position.
    /// </summary>
    public static List<ReadName> Match(
        IEnumerable<string> lines, TaskIndex? tasks, TaskIndex? keys)
    {
        var found = new List<ReadName>();
        var seen = new HashSet<WikiEntry>();

        foreach (var line in lines)
        {
            var text = Tidy(line);
            if (text.Length < Shortest) continue;

            var best = Best(text, tasks);
            var other = Best(text, keys);

            if (other.Task is not null && other.Score > best.Score) best = other;

            if (best.Task is null || best.Score < Confident) continue;
            if (!seen.Add(best.Task)) continue;

            found.Add(new ReadName(line.Trim(), best.Task, best.Score));
        }

        return found;
    }

    /// <summary>
    /// The closest entry in one index, by the same three steps the search box
    /// takes: the whole name, a run of whole words, then the nearest thing.
    /// </summary>
    private static (WikiEntry? Task, double Score) Best(string text, TaskIndex? index)
    {
        if (index is null) return (null, 0);

        if (index.AllExact(text) is { Count: > 0 } exact) return (exact[0], 1.0);

        if (index.Containing(text) is { Count: 1 } only) return (only[0], 0.95);

        var ranked = index.Rank(text, 1);
        return ranked.Count > 0 ? (ranked[0].Task, ranked[0].Score) : (null, 0);
    }

    /// <summary>
    /// What the recognizer read, as the index would like it: no punctuation it
    /// may have invented, no double spaces, no stray case.
    /// </summary>
    private static string Tidy(string line) =>
        Spaces().Replace(Noise().Replace(line, " "), " ").Trim();
}
