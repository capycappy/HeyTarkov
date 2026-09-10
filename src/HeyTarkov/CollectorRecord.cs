using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeyTarkov;

/// <summary>
/// Which of the KAPPA items are already in the stash.
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

    /// <summary>
    /// The file is there but could not be read.
    ///
    /// This matters more than it looks. Without it a failed read is
    /// indistinguishable from an empty record, and the app carries on with
    /// nothing ticked - then the first tick writes one name over the forty that
    /// were in the file. A machine that has just booted is exactly when a read
    /// is most likely to fail, with a backup or a virus scanner holding the
    /// file for a moment, and exactly when someone would open the app and see
    /// an empty list.
    ///
    /// So a record in this state refuses to save anything at all.
    /// </summary>
    [JsonIgnore]
    public bool Unreadable { get; private set; }

    /// <summary>
    /// Set only by the self-test, which needs somewhere to write that is not
    /// the user's own record. It used to save the real file, scribble on it and
    /// put it back, which quietly threw away anything ticked while the test was
    /// running. A test has no business touching data it did not create.
    /// </summary>
    internal static string? Elsewhere;

    private static string Path =>
        Elsewhere ?? System.IO.Path.Combine(TaskCatalog.DataDirectory, "collector.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private HashSet<string>? _lookup;

    private HashSet<string> Lookup =>
        _lookup ??= new HashSet<string>(Checked, StringComparer.OrdinalIgnoreCase);

    public bool Has(string name) => Lookup.Contains(name);

    [JsonIgnore]
    public int Count => Lookup.Count;

    public void Set(string name, bool held)
    {
        if (Unreadable) return;

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

    /// <summary>
    /// Empties the record. A season turning over resets everyone's stash, and
    /// the alternative is forty-four clicks.
    /// </summary>
    public void Clear()
    {
        if (Unreadable || Checked.Count == 0) return;

        Checked.Clear();
        _lookup = null;
        Save();
    }

    /// <summary>
    /// Reads the record, trying more than once.
    ///
    /// A file that is not there is an empty record - that is a first run. A
    /// file that is there but will not open is not: something else has it for
    /// the moment, which is ordinary just after a boot, so it is worth waiting
    /// out. Only when it will not come is the record marked unreadable, and
    /// then nothing is written over it.
    /// </summary>
    public static CollectorRecord Load()
    {
        var path = Path;

        if (!File.Exists(path)) return new CollectorRecord();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var record = JsonSerializer.Deserialize<CollectorRecord>(
                    File.ReadAllText(path), Options);

                if (record is not null) return record;
                break;   // it parsed, to nothing: this is not a record
            }
            catch (IOException)
            {
                Thread.Sleep(200);   // held by someone else; give it a moment
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(200);
            }
            catch (JsonException)
            {
                break;   // waiting will not fix the contents
            }
        }

        return new CollectorRecord { Unreadable = true };
    }

    /// <summary>
    /// Written through a temporary file and then moved over the old one, so a
    /// crash midway leaves the previous record intact rather than a half-written
    /// file where the record used to be. The version being replaced is kept
    /// alongside as .bak, which costs a kilobyte and has to be worth it once.
    /// </summary>
    public void Save()
    {
        if (Unreadable) return;

        UpdatedAt = DateTimeOffset.Now;

        var path = Path;

        try
        {
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, Options));

            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        catch (Exception)
        {
            // Nothing useful to say to the user mid-click. The next tick saves
            // again, and the list on screen is still right.
        }
    }
}
