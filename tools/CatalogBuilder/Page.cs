namespace HeyTarkov.CatalogBuilder;

/// <summary>What came back when a page was asked for.</summary>
public enum Fetch
{
    /// <summary>The page is there, and its text came with it.</summary>
    Ok,

    /// <summary>The wiki said 404. The page genuinely does not exist.</summary>
    Missing,

    /// <summary>
    /// The wiki would not say. Rate limited, timed out, or answering with an
    /// error - all of which look identical to a page that is not there, and
    /// none of which mean it.
    /// </summary>
    Unknown,
}

/// <summary>
/// The answer to "is this page there, and what does it say".
///
/// The three states exist because the builder decides whether an entry gets a
/// Japanese link by asking whether its page exists. Collapsing "no" and "could
/// not tell" into one value - which is what a null string did - means a wiki
/// having a busy afternoon produces a catalog with links missing from it, and
/// nothing about the build looks wrong.
/// </summary>
public readonly record struct Page(Fetch Status, string? Body)
{
    public static readonly Page Missing = new(Fetch.Missing, null);
    public static readonly Page Unknown = new(Fetch.Unknown, null);

    public static Page Found(string body) => new(Fetch.Ok, body);

    public bool Exists => Status == Fetch.Ok;

    /// <summary>
    /// The text, or null for either kind of failure. For the callers that only
    /// scrape what they find and have nothing to decide - never for one asking
    /// whether a page is there.
    /// </summary>
    public string? Text => Body;
}
