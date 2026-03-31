namespace ExtractBlueprintSC.Core.Domain.Entities;

public sealed record Ingredient(
    string ResourceId,
    string ResourceName,
    float Quantity,
    IReadOnlyDictionary<QualityTier, float> QualityRequirements
);
