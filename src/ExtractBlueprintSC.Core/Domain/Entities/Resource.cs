namespace ExtractBlueprintSC.Core.Domain.Entities;

public sealed record Resource(
    string Id,
    string Name,
    string Category,
    IReadOnlyDictionary<QualityTier, float> QualityTiers
);
