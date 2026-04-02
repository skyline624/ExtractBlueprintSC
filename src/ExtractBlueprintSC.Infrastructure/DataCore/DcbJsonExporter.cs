using System.Text.Json;

namespace ExtractBlueprintSC.Infrastructure.DataCore;

/// <summary>
/// Exporte tous les main records d'une DcbDatabase en fichiers JSON
/// structurés identiquement à la sortie du CLI starbreaker-cli.
/// </summary>
internal static class DcbJsonExporter
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
    };

    /// <summary>
    /// Exporte tous les records principaux vers outputDir,
    /// en reproduisant la hiérarchie de fichiers du DataCore.
    /// </summary>
    public static void ExportAll(
        DcbDatabase db,
        string outputDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(outputDir);

        // Identifier les main records (un seul par file_name_offset unique)
        var mainRecords = db.Records
            .Where(r => db.IsMainRecord(r))
            .ToArray();

        foreach (var record in mainRecords)
        {
            ct.ThrowIfCancellationRequested();
            ExportRecord(db, record, outputDir);
        }
    }

    private static void ExportRecord(DcbDatabase db, DcbRecord record, string outputDir)
    {
        string fileName = db.ResolveString(record.FileNameOffset);
        if (string.IsNullOrEmpty(fileName)) return;

        // Remplacer l'extension par .json (comme Path.ChangeExtension)
        string outRelPath = ChangeExtension(fileName, "json");

        // Normaliser les séparateurs (le DCB utilise '/')
        string outPath = Path.Combine(outputDir, outRelPath.Replace('/', Path.DirectorySeparatorChar));

        // Créer les répertoires parents
        string? dir = Path.GetDirectoryName(outPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var fs     = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new Utf8JsonWriter(fs, WriterOptions);

        DcbWalker.WalkRecord(db, record, writer);
        writer.Flush();
    }

    private static string ChangeExtension(string path, string ext)
    {
        int lastSlash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\')) + 1;
        int dot = path.IndexOf('.', lastSlash);
        if (dot >= 0)
            return path[..dot] + '.' + ext;
        return path + '.' + ext;
    }
}
