using System.Text.Json;
using ExtractBlueprintSC.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Application.Services;

public sealed class BlueprintTransformService
{
    private readonly ILogger<BlueprintTransformService> _logger;

    private static readonly IReadOnlyDictionary<string, string[]> CategoryKeywords =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["weapon"]     = ["weapon", "gun", "rifle", "pistol", "shotgun", "launcher", "sniper"],
            ["armor"]      = ["armor", "suit", "helmet", "vest", "backpack", "undersuit", "flightsuit"],
            ["ship"]       = ["ship", "vehicle", "spacecraft", "hull"],
            ["component"]  = ["component", "module", "upgrade", "drive", "shield", "cooler", "powerplant"],
            ["consumable"] = ["consumable", "medical", "food", "drink", "drug", "stim"],
        };

    public BlueprintTransformService(ILogger<BlueprintTransformService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public IReadOnlyList<Blueprint> Transform(
        IReadOnlyList<JsonElement> raw,
        IReadOnlyDictionary<string, Resource> resourceById)
    {
        ArgumentNullException.ThrowIfNull(raw);
        ArgumentNullException.ThrowIfNull(resourceById);

        var results = new List<Blueprint>(raw.Count);
        foreach (var element in raw)
        {
            var blueprint = TryTransformOne(element, resourceById);
            if (blueprint is not null)
                results.Add(blueprint);
        }

        _logger.LogInformation("Blueprints transformés : {Count}/{Total}", results.Count, raw.Count);
        return results.AsReadOnly();
    }

    private Blueprint? TryTransformOne(JsonElement element, IReadOnlyDictionary<string, Resource> resourceById)
    {
        try
        {
            var id = GetString(element, "id") ?? GetString(element, "name");
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Blueprint ignoré : id manquant");
                return null;
            }

            var name = GetString(element, "name") ?? GetString(element, "displayName") ?? id;
            var category = DetermineCategory(element);
            var craftTime = GetFloat(element, "craft_time") ?? GetFloat(element, "craftTime") ?? 0f;
            var ingredients = ParseIngredients(element, resourceById);
            var missionSources = ParseMissionSources(element);
            var qualityOutput = ParseQualityOutput(element);

            return new Blueprint(id, name, category, craftTime, ingredients, missionSources, qualityOutput);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la transformation d'un blueprint, ignoré");
            return null;
        }
    }

    private static string DetermineCategory(JsonElement element)
    {
        var name = GetString(element, "name") ?? string.Empty;
        var type = GetString(element, "type") ?? GetString(element, "category") ?? string.Empty;
        var searchText = $"{name} {type}".ToLowerInvariant();

        foreach (var (category, keywords) in CategoryKeywords)
        {
            foreach (var keyword in keywords)
            {
                if (searchText.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return category;
            }
        }

        return "other";
    }

    private IReadOnlyList<Ingredient> ParseIngredients(
        JsonElement element,
        IReadOnlyDictionary<string, Resource> resourceById)
    {
        var ingredients = new List<Ingredient>();

        JsonElement ingredientsElement;
        if (!element.TryGetProperty("ingredients", out ingredientsElement) &&
            !element.TryGetProperty("costs", out ingredientsElement))
            return ingredients.AsReadOnly();

        if (ingredientsElement.ValueKind != JsonValueKind.Array)
            return ingredients.AsReadOnly();

        foreach (var item in ingredientsElement.EnumerateArray())
        {
            try
            {
                var ingredient = ParseOneIngredient(item, resourceById);
                if (ingredient is not null)
                    ingredients.Add(ingredient);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ingrédient ignoré lors du parsing");
            }
        }

        return ingredients.AsReadOnly();
    }

    private static Ingredient? ParseOneIngredient(
        JsonElement item,
        IReadOnlyDictionary<string, Resource> resourceById)
    {
        string? resourceId = null;
        string? resourceName = null;
        float quantity = 0f;

        if (item.ValueKind == JsonValueKind.Object)
        {
            resourceId = GetString(item, "resource_id")
                      ?? GetString(item, "resourceId")
                      ?? GetString(item, "resource");
            resourceName = GetString(item, "resource_name") ?? GetString(item, "name");
            quantity = GetFloat(item, "quantity") ?? GetFloat(item, "amount") ?? 0f;
        }
        else if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() >= 2)
        {
            var arr = item.EnumerateArray().ToArray();
            resourceId = arr[0].ValueKind == JsonValueKind.String ? arr[0].GetString() : null;
            if (arr[1].TryGetSingle(out var q)) quantity = q;
        }

        if (string.IsNullOrWhiteSpace(resourceId))
            return null;

        if (string.IsNullOrWhiteSpace(resourceName) &&
            resourceById.TryGetValue(resourceId, out var resource))
            resourceName = resource.Name;

        resourceName ??= resourceId;

        var qualityReqs = ParseQualityRequirements(item);
        return new Ingredient(resourceId, resourceName, quantity, qualityReqs);
    }

    private static IReadOnlyDictionary<QualityTier, float> ParseQualityRequirements(JsonElement element)
    {
        var result = new Dictionary<QualityTier, float>();

        if (!element.TryGetProperty("quality_requirements", out var reqs) &&
            !element.TryGetProperty("min_quality", out reqs))
            return result;

        if (reqs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in reqs.EnumerateObject())
            {
                if (prop.Value.TryGetSingle(out var val))
                {
                    var tier = prop.Name.ToLowerInvariant() switch
                    {
                        "standard" or "0" => QualityTier.Standard,
                        "certified" or "1" => QualityTier.Certified,
                        "premium" or "2"   => QualityTier.Premium,
                        _                  => QualityTier.Standard
                    };
                    result[tier] = val;
                }
            }
        }
        else if (reqs.TryGetSingle(out var single))
        {
            result[QualityTier.Standard] = single;
        }

        return result;
    }

    private static IReadOnlyList<string> ParseMissionSources(JsonElement element)
    {
        var sources = new List<string>();

        JsonElement sourcesElement;
        if (!element.TryGetProperty("mission_sources", out sourcesElement) &&
            !element.TryGetProperty("missionSources", out sourcesElement))
            return sources.AsReadOnly();

        if (sourcesElement.ValueKind != JsonValueKind.Array)
            return sources.AsReadOnly();

        foreach (var source in sourcesElement.EnumerateArray())
        {
            if (source.ValueKind == JsonValueKind.String)
            {
                var val = source.GetString();
                if (!string.IsNullOrWhiteSpace(val))
                    sources.Add(val);
            }
            else if (source.ValueKind == JsonValueKind.Object)
            {
                var id = GetString(source, "id") ?? GetString(source, "name");
                if (!string.IsNullOrWhiteSpace(id))
                    sources.Add(id);
            }
        }

        return sources.AsReadOnly();
    }

    private static JsonElement ParseQualityOutput(JsonElement element)
    {
        JsonElement outputElement;
        if (!element.TryGetProperty("quality_output", out outputElement) &&
            !element.TryGetProperty("qualityOutput", out outputElement))
        {
            // Retourner un objet JSON vide
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteEndObject();
            writer.Flush();
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }

        if (outputElement.ValueKind != JsonValueKind.Object)
        {
            // Retourner un objet JSON vide
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);
            writer.WriteStartObject();
            writer.WriteEndObject();
            writer.Flush();
            ms.Position = 0;
            using var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }

        return outputElement.Clone();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static float? GetFloat(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.TryGetSingle(out var value))
            return value;
        return null;
    }
}
