using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// Checks a build for paths from the machine that built it.
//
//   dotnet run --project tools/PathCheck -- dist/HeyTarkov.exe
//
// A string search (findstr) cannot do this. The PDB is embedded Deflate-
// compressed, so the paths inside it are invisible to a byte search - and a
// single-file exe bundles NAudio, whose own build path does show up, so a
// search for "Users\" says yes for the wrong reason and no for the right one.
//
// So this finds every PE image in the file (a single-file bundle carries
// several), decompresses any embedded PDB, and reads the source document names,
// the SourceLink map and the CodeView path. Third-party assemblies are listed
// but do not fail the check: their paths are not ours to change.

var sourceLinkGuid = new Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
var thirdParty = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NAudio.Core", "NAudio.WinMM" };

var needles = new List<string> { "Users\\", "iCloud" };
var user = Environment.UserName;
if (!string.IsNullOrEmpty(user)) needles.Add("\\" + user + "\\");

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: PathCheck <file> [<file>...]");
    return 2;
}

var failures = 0;

foreach (var file in args)
{
    var bytes = File.ReadAllBytes(file);
    Console.WriteLine(file);
    var ours = 0;

    for (var at = 0; at + 0x40 < bytes.Length; at++)
    {
        if (bytes[at] != 'M' || bytes[at + 1] != 'Z') continue;

        var header = BitConverter.ToInt32(bytes, at + 0x3C);
        if (header <= 0 || header > 0x1000 || at + header + 4 > bytes.Length) continue;
        if (bytes[at + header] != 'P' || bytes[at + header + 1] != 'E' ||
            bytes[at + header + 2] != 0 || bytes[at + header + 3] != 0) continue;

        try
        {
            using var stream = new MemoryStream(bytes, at, bytes.Length - at, false);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!pe.HasMetadata) continue;

            var metadata = pe.GetMetadataReader();
            if (!metadata.IsAssembly) continue;

            var name = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var found = new List<string>();
            var documents = 0;

            foreach (var entry in pe.ReadDebugDirectory())
            {
                if (entry.Type == DebugDirectoryEntryType.CodeView)
                {
                    var path = pe.ReadCodeViewDebugDirectoryData(entry).Path;
                    if (Hits(path)) found.Add($"pdb path   {path}");
                }
                else if (entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    using var provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(entry);
                    var pdb = provider.GetMetadataReader();

                    foreach (var handle in pdb.Documents)
                    {
                        documents++;
                        var document = pdb.GetString(pdb.GetDocument(handle).Name);
                        if (Hits(document)) found.Add($"document   {document}");
                    }

                    foreach (var handle in pdb.GetCustomDebugInformation(EntityHandle.ModuleDefinition))
                    {
                        var info = pdb.GetCustomDebugInformation(handle);
                        if (pdb.GetGuid(info.Kind) != sourceLinkGuid) continue;

                        var json = Encoding.UTF8.GetString(pdb.GetBlobBytes(info.Value));
                        if (Hits(json.Replace("\\\\", "\\"))) found.Add($"sourcelink {json}");
                    }
                }
            }

            var external = thirdParty.Contains(name);
            if (!external && documents > 0) ours++;

            if (found.Count == 0)
            {
                if (!external && documents > 0)
                    Console.WriteLine($"  ok    {name}: {documents} source paths, none local");
                continue;
            }

            Console.WriteLine($"  {(external ? "note " : "FAIL ")} {name}{(external ? " (third-party, not ours)" : "")}");
            foreach (var line in found.Take(5)) Console.WriteLine($"          {line}");
            if (found.Count > 5) Console.WriteLine($"          ... {found.Count - 5} more");

            if (!external) failures++;
        }
        catch (BadImageFormatException) { }
        catch (InvalidOperationException) { }
    }

    // Finding nothing because nothing was read is not a pass.
    if (ours == 0)
    {
        Console.WriteLine("  FAIL  no embedded PDB of ours found - nothing was actually checked");
        failures++;
    }
}

Console.WriteLine(failures == 0 ? "PASS" : $"FAIL ({failures})");
return failures == 0 ? 0 : 1;

bool Hits(string text) => needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));
