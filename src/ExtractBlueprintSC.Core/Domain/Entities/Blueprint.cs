using System.Text.Json;

namespace ExtractBlueprintSC.Core.Domain.Entities;

public sealed record Blueprint(
    string Id,
    string Name,
    string Category,
    float CraftTime,
    IReadOnlyList<Ingredient> Ingredients,
    IReadOnlyList<string> MissionSources,
    JsonElement QualityOutput
);
