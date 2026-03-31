using System.Text.Json;
using ExtractBlueprintSC.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Application.Services;

public sealed class ResourceTransformService
{
    private readonly ILogger<ResourceTransformService> _logger;

    public ResourceTransformService(ILogger<ResourceTransformService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public IReadOnlyList<Resource> Transform(IReadOnlyList<JsonElement> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var results = new List<Resource>(raw.Count);
        foreach (var element in raw)
        {
            var resource = TryTransformOne(element);
            if (resource is not null)
                results.Add(resource);
        }

        _logger.LogInformation("Ressources transformées : {Count}/{Total}", results.Count, raw.Count);
        return results.AsReadOnly();
    }

    private Resource? TryTransformOne(JsonElement element)
    {
        try
        {
            var id = GetString(element, "id") ?? GetString(element, "name");
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Ressource ignorée : id manquant");
                return null;
            }

            var name = GetString(element, "name") ?? GetString(element, "displayName") ?? id;
            var category = GetString(element, "category") ?? GetString(element, "type") ?? "unknown";
            var qualityTiers = ParseQualityTiers(element);

            return new Resource(id, name, category, qualityTiers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la transformation d'une ressource, ignorée");
            return null;
        }
    }

    private static IReadOnlyDictionary<QualityTier, float> ParseQualityTiers(JsonElement element)
    {
        var result = new Dictionary<QualityTier, float>();

        if (element.TryGetProperty("qualityTiers", out var tiersElement) ||
            element.TryGetProperty("quality_levels", out tiersElement))
        {
            foreach (var tier in tiersElement.EnumerateObject())
            {
                if (TryParseQualityTier(tier.Name, out var qualityTier) &&
                    tier.Value.TryGetSingle(out var multiplier))
                {
                    result[qualityTier] = multiplier;
                }
            }
        }

        return result;
    }

    private static bool TryParseQualityTier(string name, out QualityTier tier)
    {
        tier = name.ToLowerInvariant() switch
        {
            "standard" or "tier_0" or "0" => QualityTier.Standard,
            "certified" or "tier_1" or "1" => QualityTier.Certified,
            "premium" or "tier_2" or "2" => QualityTier.Premium,
            _ => QualityTier.Standard
        };
        return true;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
