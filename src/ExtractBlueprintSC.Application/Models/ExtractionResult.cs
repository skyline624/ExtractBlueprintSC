namespace ExtractBlueprintSC.Application.Models;

public sealed record ExtractionResult(
    int BlueprintsCount,
    int ResourcesCount,
    int MissionsCount,
    string OutputPath
);
