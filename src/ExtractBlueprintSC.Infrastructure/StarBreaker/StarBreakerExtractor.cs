using ExtractBlueprintSC.Core.Domain.Exceptions;
using ExtractBlueprintSC.Infrastructure.DataCore;
using ExtractBlueprintSC.Infrastructure.P4k;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.StarBreaker;

public sealed class StarBreakerExtractor
{
    private readonly ILogger<StarBreakerExtractor> _logger;

    public StarBreakerExtractor(ILogger<StarBreakerExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>Toujours disponible : l'implémentation est intégrée nativement.</summary>
    public bool IsAvailable() => true;

    public async Task<string> ExtractDcbAsync(
        string p4kPath,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(p4kPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        if (!File.Exists(p4kPath))
            throw new ExtractionException($"Fichier P4K introuvable : {p4kPath}");

        _logger.LogInformation("Extraction P4K (natif) : {P4K} → {OutputDir}", p4kPath, outputDir);

        Directory.CreateDirectory(outputDir);

        // 1. Ouvrir l'archive et trouver le fichier DCB
        using var p4k = P4kReader.Open(p4kPath);

        var dcbEntry = FindDcbEntry(p4k)
            ?? throw new ExtractionException("Fichier Game.dcb / Game2.dcb introuvable dans le P4K");

        _logger.LogInformation("Fichier DCB cible : {DcbName}", dcbEntry.Name);

        // 2. Lire et décompresser le DCB depuis le P4K
        _logger.LogDebug("Lecture du DCB depuis le P4K...");
        byte[] dcbBytes = await Task.Run(() => p4k.ReadEntry(dcbEntry), cancellationToken);

        // 3. Parser la base de données DataCore
        _logger.LogDebug("Parsing du DataCore ({Size:N0} octets)...", dcbBytes.Length);
        var db = await Task.Run(() => DcbDatabase.FromBytes(dcbBytes), cancellationToken);

        _logger.LogInformation("DataCore chargé : {Records} records", db.Records.Length);

        // 4. Exporter tous les records en JSON
        var jsonOutputDir = Path.Combine(outputDir, "dcb_json");
        _logger.LogDebug("Export JSON vers : {Dir}", jsonOutputDir);

        await Task.Run(
            () => DcbJsonExporter.ExportAll(db, jsonOutputDir, cancellationToken),
            cancellationToken);

        // 5. Retourner le chemin du dossier records (compatible avec StarbreakerReader)
        var recordsDir = Path.Combine(jsonOutputDir, "libs", "foundry", "records");
        if (!Directory.Exists(recordsDir))
        {
            recordsDir = jsonOutputDir;
            _logger.LogWarning("Dossier records standard introuvable, utilisation de : {Path}", recordsDir);
        }

        _logger.LogInformation("Extraction terminée. Dossier records : {Path}", recordsDir);
        return recordsDir;
    }

    private static P4kEntry? FindDcbEntry(P4kReader p4k)
    {
        // Chercher d'abord Game.dcb, puis Game2.dcb (insensible à la casse)
        foreach (var entry in p4k.Entries)
        {
            string name = entry.Name;
            if (name.EndsWith("Game.dcb",  StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith("Game2.dcb", StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        // Fallback : tout fichier .dcb dans Data/
        foreach (var entry in p4k.Entries)
        {
            if (entry.Name.EndsWith(".dcb", StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }
}
