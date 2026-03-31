using System.Text.Json;
using ExtractBlueprintSC.Core.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Application.Services;

public sealed class MissionTransformService
{
    private readonly ILogger<MissionTransformService> _logger;

    public MissionTransformService(ILogger<MissionTransformService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public IReadOnlyList<Mission> Transform(IReadOnlyList<JsonElement> raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var results = new List<Mission>(raw.Count);
        foreach (var element in raw)
        {
            var mission = TryTransformOne(element);
            if (mission is not null)
                results.Add(mission);
        }

        _logger.LogInformation("Missions transformées : {Count}/{Total}", results.Count, raw.Count);
        return results.AsReadOnly();
    }

    private Mission? TryTransformOne(JsonElement element)
    {
        try
        {
            var id = GetString(element, "id") ?? GetString(element, "name");
            if (string.IsNullOrWhiteSpace(id))
            {
                _logger.LogWarning("Mission ignorée : id manquant");
                return null;
            }

            var name = GetString(element, "name") ?? GetString(element, "displayName") ?? id;
            var location = GetString(element, "location");
            var missionType = GetString(element, "type") ?? GetString(element, "missionType") ?? "unknown";
            var rewards = ParseBlueprintRewards(element);

            return new Mission(id, name, location, missionType, rewards);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Erreur lors de la transformation d'une mission, ignorée");
            return null;
        }
    }

    private static IReadOnlyList<string> ParseBlueprintRewards(JsonElement element)
    {
        var rewards = new List<string>();

        JsonElement rewardsElement;
        if (!element.TryGetProperty("blueprint_rewards", out rewardsElement) &&
            !element.TryGetProperty("blueprintRewards", out rewardsElement) &&
            !element.TryGetProperty("rewards", out rewardsElement))
            return rewards.AsReadOnly();

        if (rewardsElement.ValueKind != JsonValueKind.Array)
            return rewards.AsReadOnly();

        foreach (var reward in rewardsElement.EnumerateArray())
        {
            if (reward.ValueKind == JsonValueKind.String)
            {
                var value = reward.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    rewards.Add(value);
            }
            else if (reward.ValueKind == JsonValueKind.Object)
            {
                var id = GetStringFromObject(reward, "blueprint_name")
                      ?? GetStringFromObject(reward, "blueprintId")
                      ?? GetStringFromObject(reward, "id");

                if (!string.IsNullOrWhiteSpace(id))
                    rewards.Add(id);
            }
        }

        return rewards.AsReadOnly();
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static string? GetStringFromObject(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
