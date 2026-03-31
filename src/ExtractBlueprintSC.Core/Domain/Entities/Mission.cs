namespace ExtractBlueprintSC.Core.Domain.Entities;

public sealed record Mission(
    string Id,
    string Name,
    string? Location,
    string MissionType,
    IReadOnlyList<string> BlueprintRewards
);
