using System.Text.Json;

namespace HeyTarkov;

/// <summary>
/// Which of the Collector items are already in the stash.
///
/// Kept in its own file rather than in settings.json. Settings are
/// preferences - losing them costs a few seconds of clicking. This is the
/// user's own record of forty-odd raids, and losing it means collecting them
/// again, so it does not share a file with anything that gets rewritten every
/// time a dropdown changes.
///
/// Items are stored by name, not by position, so a catalog update that adds or
/// removes one cannot shift the record out of line. A name that leaves the task
/// stays in the file: it is not this code's business to decide the user was
/// wrong about owning something.
/// </summary>
public sealed class CollectorRecord
{
    /// <summary>Bumped only if the shape changes in a way a reader must know
    /// about. Nothing reads it yet, and that is the point of writing it.</summary>
    public int Version { get; set; } = 1;

    public List<string> Checked { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; }

    private static string Path =>
        System.IO.Path.Combine(TaskCatalog.DataDirectory, "collector.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private HashSet<string>? _lookup;

    private HashSet<string> Lookup =>
        _lookup ??= new HashSet<string>(Checked, StringComparer.OrdinalIgnoreCase);

    public bool Has(string name) => Lookup.Contains(name);

    public int Count => Lookup.Count;

    public void Set(string name, bool held)
    {
        if (held)
        {
            if (!Lookup.Add(name)) return;
            Checked.Add(name);
        }
        else
        {
            if (!Lookup.Remove(name)) return;
            Checked.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        }

        Save();
    }

    public static CollectorRecord Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                return JsonSerializer.Deserialize<CollectorRecord>(File.ReadAllText(Path), Options)
                       ?? new CollectorRecord();
            }
        }
        catch (Exception)
        {
            // Better an empty list than a window that will not open. The file
            // is left where it is rather than overwritten, so whatever is in
            // there can still be recovered by hand.
        }

        return new CollectorRecord();
    }

    /// <summary>
    /// Written through a temporary file and then moved over the old one, so a
    /// crash midway leaves the previous record intact rather than a half-written
    /// file where the record used to be.
    /// </summary>
    public void Save()
    {
        UpdatedAt = DateTimeOffset.Now;

        try
        {
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));

            if (File.Exists(Path)) File.Replace(temporary, Path, null);
            else File.Move(temporary, Path);
        }
        catch (Exception)
        {
            // Nothing useful to say to the user mid-click. The next tick saves
            // again, and the list on screen is still right.
        }
    }
}
