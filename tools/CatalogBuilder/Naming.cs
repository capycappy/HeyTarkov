using System.Text;

namespace HeyTarkov.CatalogBuilder;

/// <summary>
/// Name handling shared by the fetchers. Normalize has to agree with
/// HeyTarkov.SpokenForms.Normalize closely enough to pair the same name across
/// the two wikis.
/// </summary>
public static class Naming
{
    public static string Normalize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var currentIsDigit = false;

        void Flush()
        {
            if (current.Length == 0) return;
            tokens.Add(current.ToString().ToLowerInvariant());
            current.Clear();
        }

        foreach (var raw in text)
        {
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
            }
        }

        Flush();
        return string.Join(' ', tokens);
    }

    public static bool ContainsJapanese(string s)
    {
        foreach (var c in s)
        {
            if (c is >= '　' and <= 'ヿ') return true;   // kana + CJK punctuation
            if (c is >= '一' and <= '鿿') return true;   // kanji
            if (c is >= '＀' and <= '￯') return true;   // fullwidth forms
        }

        return false;
    }
}
