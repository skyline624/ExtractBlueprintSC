namespace ExtractBlueprintSC.Core.Domain.Entities;

public sealed record BlueprintCollection(
    IReadOnlyList<Blueprint> Blueprints,
    IReadOnlyList<Resource> Resources,
    IReadOnlyList<Mission> Missions,
    string Version = "1.0",
    string? GameVersion = null
);
