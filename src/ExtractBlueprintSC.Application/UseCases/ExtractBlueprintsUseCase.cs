using ExtractBlueprintSC.Application.Models;
using ExtractBlueprintSC.Application.Services;
using ExtractBlueprintSC.Core.Domain.Entities;
using ExtractBlueprintSC.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Application.UseCases;

public sealed class ExtractBlueprintsUseCase
{
    private readonly IDataReader _reader;
    private readonly IDataExporter _exporter;
    private readonly ResourceTransformService _resourceTransformer;
    private readonly MissionTransformService _missionTransformer;
    private readonly BlueprintTransformService _blueprintTransformer;
    private readonly ILogger<ExtractBlueprintsUseCase> _logger;

    public ExtractBlueprintsUseCase(
        IDataReader reader,
        IDataExporter exporter,
        ResourceTransformService resourceTransformer,
        MissionTransformService missionTransformer,
        BlueprintTransformService blueprintTransformer,
        ILogger<ExtractBlueprintsUseCase> logger)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(resourceTransformer);
        ArgumentNullException.ThrowIfNull(missionTransformer);
        ArgumentNullException.ThrowIfNull(blueprintTransformer);
        ArgumentNullException.ThrowIfNull(logger);

        _reader = reader;
        _exporter = exporter;
        _resourceTransformer = resourceTransformer;
        _missionTransformer = missionTransformer;
        _blueprintTransformer = blueprintTransformer;
        _logger = logger;
    }

    public async Task<ExtractionResult> ExecuteAsync(string outputPath,
                                                       CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        _logger.LogInformation("Démarrage de l'extraction vers {OutputPath}", outputPath);

        // 1. Lecture des données brutes
        _logger.LogDebug("Lecture des ressources...");
        var rawResources = _reader.GetResources();

        _logger.LogDebug("Lecture des missions...");
        var rawMissions = _reader.GetMissions();

        _logger.LogDebug("Lecture des blueprints...");
        var rawBlueprints = _reader.GetBlueprints();

        cancellationToken.ThrowIfCancellationRequested();

        // 2. Transformation des entités domain
        var resources = _resourceTransformer.Transform(rawResources);
        var missions = _missionTransformer.Transform(rawMissions);

        var resourceById = resources.ToDictionary(r => r.Id, r => r);
        var blueprints = _blueprintTransformer.Transform(rawBlueprints, resourceById);

        cancellationToken.ThrowIfCancellationRequested();

        // 3. Assemblage de la collection
        var collection = new BlueprintCollection(blueprints, resources, missions);

        // 4. Export
        await _exporter.ExportAsync(collection, outputPath, cancellationToken);

        var result = new ExtractionResult(
            blueprints.Count,
            resources.Count,
            missions.Count,
            outputPath);

        _logger.LogInformation(
            "Extraction terminée — {Blueprints} blueprints, {Resources} ressources, {Missions} missions",
            result.BlueprintsCount, result.ResourcesCount, result.MissionsCount);

        return result;
    }
}
