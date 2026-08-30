namespace HeyTarkov;

public enum RecognitionLanguage
{
    English,
    Japanese,
}

/// <summary>
/// How a task name turns into the phrases the recognizer listens for, and how a
/// recognized phrase turns back into a lookup key. One implementation per
/// language keeps <see cref="TaskIndex"/> language-agnostic.
/// </summary>
public interface IPhraseScheme
{
    RecognitionLanguage Language { get; }

    /// <summary>Grammar phrases for one task. Empty means "leave this task out".</summary>
    IReadOnlyList<string> Phrases(string taskName);

    /// <summary>
    /// Extra phrases that resolve to the same task but are expensive to compile,
    /// so they are loaded into the recognizer after the main grammar is ready.
    /// </summary>
    IReadOnlyList<string> DeferredPhrases(string taskName);

    /// <summary>Exact-match key.</summary>
    string Key(string text);

    /// <summary>Looser key used for the fuzzy fallback (folds 4 / four together).</summary>
    string FuzzyKey(string text);
}

public sealed class EnglishScheme : IPhraseScheme
{
    public RecognitionLanguage Language => RecognitionLanguage.English;

    public IReadOnlyList<string> Phrases(string taskName) => SpokenForms.For(taskName);

    public IReadOnlyList<string> DeferredPhrases(string taskName) => Array.Empty<string>();

    public string Key(string text) => SpokenForms.Normalize(text);

    public string FuzzyKey(string text) => SpokenForms.DigitsToWords(SpokenForms.Normalize(text));
}

public sealed class JapaneseScheme : IPhraseScheme
{
    private readonly JapaneseForms _forms;

    public JapaneseScheme(JapaneseForms forms) => _forms = forms;

    public RecognitionLanguage Language => RecognitionLanguage.Japanese;

    public IReadOnlyCollection<string> UnknownWords => _forms.UnknownWords;

    public IReadOnlyList<string> Phrases(string taskName) => _forms.For(taskName);

    public IReadOnlyList<string> DeferredPhrases(string taskName) => _forms.SpelledFor(taskName);

    /// <summary>How to spell this task out loud, for display.</summary>
    public string? SpelledHint(string taskName) => _forms.SpelledHint(taskName);

    public string Key(string text) => JapaneseForms.Normalize(text);

    public string FuzzyKey(string text) => JapaneseForms.Normalize(text);
}
