namespace HeyTarkov;

public readonly record struct TaskMatch(WikiEntry Task, double Score, string MatchedPhrase);

/// <summary>
/// Maps a recognized phrase back to a task. Because the recognizer runs on a
/// closed vocabulary, the common case is an exact hit; the fuzzy pass only
/// covers a result that came back slightly reshaped.
/// </summary>
public sealed class TaskIndex
{
    private readonly IPhraseScheme _scheme;
    private readonly Dictionary<string, WikiEntry> _byPhrase = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WikiEntry> _byFuzzyKey = new(StringComparer.Ordinal);
    private readonly List<(string Key, WikiEntry Task)> _all = new();
    private readonly HashSet<WikiEntry> _covered = new();
    private readonly HashSet<WikiEntry> _spellOnly = new();

    /// <summary>A run of words from a name to everything containing that run.</summary>
    private readonly Dictionary<string, List<WikiEntry>> _byFragment = new(StringComparer.Ordinal);

    public TaskIndex(IEnumerable<WikiEntry> tasks, IPhraseScheme scheme)
    {
        _scheme = scheme;

        // The grammar gets the phrases as written; the dictionaries are keyed by
        // the normalized form. Keeping these separate matters for Japanese,
        // where normalizing strips the spaces that the grammar wants to keep.
        var grammar = new List<string>();
        var deferred = new List<string>();
        var grammarSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var task in tasks)
        {
            // An entry can be said more than one way; every variant resolves to
            // the same entry.
            var phrases = task.SpokenVariants().SelectMany(scheme.Phrases).Distinct().ToList();
            var spelled = task.SpokenVariants().SelectMany(scheme.DeferredPhrases)
                .Distinct().ToList();

            // Spelling a name out needs no dictionary - just the letters - so a
            // task whose words have no reading yet is still reachable that way.
            // Dropping it entirely would make a freshly added task invisible
            // until someone extends the lexicon.
            if (phrases.Count == 0 && spelled.Count == 0) continue;

            _covered.Add(task);
            if (phrases.Count == 0) _spellOnly.Add(task);

            foreach (var phrase in phrases)
            {
                if (grammarSeen.Add(phrase)) grammar.Add(phrase);

                _all.Add((scheme.FuzzyKey(phrase), task));

                // First writer wins: two tasks sharing a phrase is possible in
                // principle, and the ranked fallback will still surface both.
                _byPhrase.TryAdd(scheme.Key(phrase), task);
                _byFuzzyKey.TryAdd(scheme.FuzzyKey(phrase), task);
            }

            foreach (var phrase in spelled)
            {
                if (grammarSeen.Add(phrase)) deferred.Add(phrase);

                // Spelled phrases resolve exactly, but they are deliberately kept
                // out of the fuzzy pool: edit distance between two long letter
                // sequences is noise.
                _byPhrase.TryAdd(scheme.Key(phrase), task);
                _byFuzzyKey.TryAdd(scheme.FuzzyKey(phrase), task);
            }
        }

        // Fragments go in last so one never displaces a whole name that happens
        // to read the same way.
        foreach (var task in _covered)
        {
            foreach (var variant in task.SpokenVariants())
            {
                foreach (var fragment in Fragments.Of(variant))
                {
                    foreach (var phrase in scheme.Phrases(fragment))
                    {
                        var key = scheme.Key(phrase);
                        if (_byPhrase.ContainsKey(key)) continue;   // a real name wins

                        if (!_byFragment.TryGetValue(key, out var list))
                        {
                            list = new List<WikiEntry>();
                            _byFragment[key] = list;
                        }

                        if (!list.Contains(task)) list.Add(task);
                        if (grammarSeen.Add(phrase)) grammar.Add(phrase);
                    }
                }
            }
        }

        // Read in name order, so "Broadcast - Part 1" through "Part 5" come out
        // in that order rather than in whatever order the catalog held them.
        foreach (var list in _byFragment.Values)
        {
            list.Sort((a, b) =>
            {
                var byName = NaturalOrder.Instance.Compare(a.Name, b.Name);
                if (byName != 0) return byName;

                var byFaction = string.CompareOrdinal(a.Faction ?? "", b.Faction ?? "");
                return byFaction != 0 ? byFaction : string.CompareOrdinal(a.Group, b.Group);
            });
        }

        GrammarPhrases = grammar;
        DeferredGrammarPhrases = deferred;
    }

    /// <summary>
    /// Everything whose name contains what was said, as a run of whole words -
    /// "punisher" finds "The Punisher - Part 4". Empty when the phrase is a
    /// whole name rather than a fragment of one.
    /// </summary>
    public IReadOnlyList<WikiEntry> Containing(string recognizedText)
    {
        var key = _scheme.Key(recognizedText);
        return _byFragment.TryGetValue(key, out var list)
            ? list
            : Array.Empty<WikiEntry>();
    }

    /// <summary>Phrases loaded into the recognizer up front.</summary>
    public IReadOnlyList<string> GrammarPhrases { get; }

    /// <summary>Phrases loaded afterwards, once the microphone is already usable.</summary>
    public IReadOnlyList<string> DeferredGrammarPhrases { get; }

    /// <summary>Tasks that made it into the grammar.</summary>
    public int TaskCount => _covered.Count;

    /// <summary>
    /// Tasks with no word reading, reachable only by spelling them out. These
    /// are the ones worth adding to the lexicon.
    /// </summary>
    public IReadOnlyCollection<WikiEntry> SpellOnlyTasks => _spellOnly;

    public WikiEntry? Exact(string recognizedText)
    {
        if (_byPhrase.TryGetValue(_scheme.Key(recognizedText), out var hit)) return hit;
        return _byFuzzyKey.TryGetValue(_scheme.FuzzyKey(recognizedText), out hit) ? hit : null;
    }

    public IReadOnlyList<TaskMatch> Rank(string recognizedText, int max = 5)
    {
        var key = _scheme.FuzzyKey(recognizedText);
        if (key.Length == 0) return Array.Empty<TaskMatch>();

        var best = new Dictionary<WikiEntry, TaskMatch>();

        foreach (var (phraseKey, task) in _all)
        {
            var score = Similarity(key, phraseKey);
            if (best.TryGetValue(task, out var existing) && existing.Score >= score) continue;
            best[task] = new TaskMatch(task, score, phraseKey);
        }

        return best.Values
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Task.Name, NaturalOrder.Instance)
            .Take(max)
            .ToArray();
    }

    private static double Similarity(string a, string b)
    {
        if (a == b) return 1.0;
        var longest = Math.Max(a.Length, b.Length);
        if (longest == 0) return 1.0;
        return 1.0 - (double)Levenshtein(a, b) / longest;
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
