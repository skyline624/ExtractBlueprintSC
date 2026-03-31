using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.Readers;

public sealed partial class StarbreakerReader
{
    public IReadOnlyList<JsonElement> GetMissions()
    {
        if (_missionsCache is not null)
            return _missionsCache;

        var rewardsDir = Path.Combine(_baseDir, RewardsSubPath);
        var results = new List<JsonElement>();
        var bpMissionMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(rewardsDir))
        {
            _logger.LogWarning("Dossier des missions introuvable : {Path}", rewardsDir);
            _missionsCache = Array.Empty<JsonElement>();
            _blueprintMissionMap = new Dictionary<string, IReadOnlyList<string>>();
            return _missionsCache;
        }

        foreach (var filePath in FindJsonFiles(rewardsDir))
        {
            using var doc = TryLoadJson(filePath);
            if (doc is null) continue;

            var mission = ParseMission(doc.RootElement, filePath);
            if (mission.HasValue)
            {
                results.Add(mission.Value.Clone());

                // Construire la map inverse blueprint → missions
                var missionId = GetString(mission.Value, "id") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(missionId) &&
                    mission.Value.TryGetProperty("blueprint_rewards", out var rewardsEl) &&
                    rewardsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var reward in rewardsEl.EnumerateArray())
                    {
                        var bpName = reward.ValueKind == JsonValueKind.String
                            ? reward.GetString()
                            : GetString(reward, "blueprint_name");

                        if (!string.IsNullOrWhiteSpace(bpName))
                        {
                            if (!bpMissionMap.TryGetValue(bpName, out var list))
                            {
                                list = new List<string>();
                                bpMissionMap[bpName] = list;
                            }
                            list.Add(missionId);
                        }
                    }
                }
            }
        }

        _logger.LogInformation("Missions chargées : {Count}", results.Count);
        _missionsCache = results.AsReadOnly();
        _blueprintMissionMap = bpMissionMap.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.AsReadOnly());

        return _missionsCache;
    }

    private JsonElement? ParseMission(JsonElement data, string filePath)
    {
        try
        {
            var root = data;

            // Naviguer dans _RecordValue_ si présent
            if (root.TryGetProperty("_RecordValue_", out var recordValue))
                root = recordValue;

            var id = GetString(data, "_RecordId_") ?? GetString(data, "id");
            if (string.IsNullOrWhiteSpace(id))
                id = Path.GetFileNameWithoutExtension(filePath);

            var name = GetString(data, "_RecordName_")
                    ?? GetString(data, "name")
                    ?? Path.GetFileNameWithoutExtension(filePath);

            var rewards = new List<object>();

            // Extraire les blueprintMissionPool ou récompenses directes
            JsonElement rewardsEl;
            if (root.TryGetProperty("blueprintMissionPool", out rewardsEl) ||
                root.TryGetProperty("missionPool", out rewardsEl) ||
                root.TryGetProperty("rewards", out rewardsEl) ||
                root.TryGetProperty("BlueprintRewards", out rewardsEl))
            {
                if (rewardsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var reward in rewardsEl.EnumerateArray())
                    {
                        var bpName = GetString(reward, "blueprint")
                                  ?? GetString(reward, "blueprintName")
                                  ?? GetString(reward, "name");
                        if (!string.IsNullOrWhiteSpace(bpName))
                            rewards.Add(bpName);
                    }
                }
            }

            // Sérialiser en JsonElement normalisé
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("name", CleanLocalizationKey(name));
            writer.WriteString("type", "BlueprintMission");
            writer.WriteString("source_file", Path.GetRelativePath(_baseDir, filePath));

            writer.WriteStartArray("blueprint_rewards");
            foreach (var reward in rewards)
                writer.WriteStringValue(reward.ToString());
            writer.WriteEndArray();

            writer.WriteEndObject();
            writer.Flush();

            ms.Position = 0;
            var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Mission ignorée : {File}", filePath);
            return null;
        }
    }
}
