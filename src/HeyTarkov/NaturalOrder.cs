namespace HeyTarkov;

/// <summary>
/// Orders names the way a person reads them: "Part 2" before "Part 10".
///
/// A plain string sort puts "Part 10" before "Part 2" because '1' sorts before
/// '2', which is wrong wherever a name ends in a number - and most of them do.
/// </summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;

        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                var (left, nextI) = ReadNumber(x, i);
                var (right, nextJ) = ReadNumber(y, j);

                if (left != right) return left.CompareTo(right);

                i = nextI;
                j = nextJ;
                continue;
            }

            var a = char.ToUpperInvariant(x[i]);
            var b = char.ToUpperInvariant(y[j]);
            if (a != b) return a.CompareTo(b);

            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }

    /// <summary>Reads a run of digits as one value, so it compares numerically.</summary>
    private static (long Value, int Next) ReadNumber(string text, int start)
    {
        var end = start;
        while (end < text.Length && char.IsDigit(text[end])) end++;

        // Longer than a long can hold is not a number anyone speaks; clamp it.
        var digits = text[start..end];
        return (long.TryParse(digits, out var value) ? value : long.MaxValue, end);
    }
}
