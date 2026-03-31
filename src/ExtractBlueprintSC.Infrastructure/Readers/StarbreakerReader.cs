using System.Text.Json;
using ExtractBlueprintSC.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.Readers;

public sealed partial class StarbreakerReader : IDataReader
{
    private readonly string _baseDir;
    private readonly ILogger<StarbreakerReader> _logger;

    private const string BlueprintsSubPath = "crafting/blueprints/crafting";
    private const string ResourcesSubPath  = "resourcetypedatabase/resourcetypedatabase.json";
    private const string RewardsSubPath    = "crafting/blueprintrewards";

    private IReadOnlyList<JsonElement>? _blueprintsCache;
    private IReadOnlyList<JsonElement>? _resourcesCache;
    private IReadOnlyList<JsonElement>? _missionsCache;

    private IReadOnlyDictionary<string, JsonElement>? _resourceById;
    private IReadOnlyDictionary<string, JsonElement>? _resourceByName;
    private IReadOnlyDictionary<string, IReadOnlyList<string>>? _blueprintMissionMap;

    public StarbreakerReader(string baseDir, ILogger<StarbreakerReader> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDir);
        ArgumentNullException.ThrowIfNull(logger);

        if (!Directory.Exists(baseDir))
            throw new DirectoryNotFoundException($"Dossier introuvable : {baseDir}");

        _baseDir = baseDir;
        _logger = logger;

        _logger.LogDebug("StarbreakerReader initialisé sur : {BaseDir}", baseDir);
    }
}
