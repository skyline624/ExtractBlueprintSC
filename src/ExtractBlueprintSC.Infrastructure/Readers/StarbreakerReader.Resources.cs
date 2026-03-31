using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.Readers;

public sealed partial class StarbreakerReader
{
    public IReadOnlyList<JsonElement> GetResources()
    {
        if (_resourcesCache is not null)
            return _resourcesCache;

        var resourcesPath = Path.Combine(_baseDir, ResourcesSubPath);
        if (!File.Exists(resourcesPath))
        {
            _logger.LogWarning("Fichier des ressources introuvable : {Path}", resourcesPath);
            _resourcesCache = Array.Empty<JsonElement>();
            BuildResourceMaps(_resourcesCache);
            return _resourcesCache;
        }

        var results = new List<JsonElement>();

        using var doc = TryLoadJson(resourcesPath);
        if (doc is null)
        {
            _resourcesCache = Array.Empty<JsonElement>();
            BuildResourceMaps(_resourcesCache);
            return _resourcesCache;
        }

        var root = doc.RootElement;

        // Structure attendue : { "_RecordValue_": { "groups": [ { "resources": [...] } ] } }
        JsonElement recordValue = root;
        if (root.TryGetProperty("_RecordValue_", out var rv))
            recordValue = rv;

        // Parcourir les groupes de ressources
        if (recordValue.TryGetProperty("groups", out var groups) &&
            groups.ValueKind == JsonValueKind.Array)
        {
            foreach (var group in groups.EnumerateArray())
            {
                var groupName = GetString(group, "_RecordName_")
                             ?? GetString(group, "displayName")
                             ?? GetString(group, "name")
                             ?? "unknown";

                // Nettoyer le nom du groupe
                groupName = CleanLocalizationKey(groupName);

                // Ressources directes dans le groupe
                if (group.TryGetProperty("resources", out var resources) &&
                    resources.ValueKind == JsonValueKind.Array)
                {
                    foreach (var resource in resources.EnumerateArray())
                    {
                        var parsed = ParseResource(resource, groupName);
                        if (parsed.HasValue)
                            results.Add(parsed.Value.Clone());
                    }
                }

                // Sous-groupes récursifs
                if (group.TryGetProperty("groups", out var subGroups) &&
                    subGroups.ValueKind == JsonValueKind.Array)
                {
                    foreach (var subGroup in subGroups.EnumerateArray())
                    {
                        var subGroupName = GetString(subGroup, "_RecordName_")
                                        ?? GetString(subGroup, "displayName")
                                        ?? groupName;

                        subGroupName = CleanLocalizationKey(subGroupName);

                        if (subGroup.TryGetProperty("resources", out var subResources) &&
                            subResources.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var resource in subResources.EnumerateArray())
                            {
                                var parsed = ParseResource(resource, subGroupName);
                                if (parsed.HasValue)
                                    results.Add(parsed.Value.Clone());
                            }
                        }
                    }
                }
            }
        }

        _logger.LogInformation("Ressources chargées : {Count}", results.Count);
        _resourcesCache = results.AsReadOnly();
        BuildResourceMaps(_resourcesCache);
        return _resourcesCache;
    }

    private JsonElement? ParseResource(JsonElement data, string groupName)
    {
        try
        {
            var id = GetString(data, "_RecordId_")
                  ?? GetString(data, "id")
                  ?? GetString(data, "_RecordName_");

            if (string.IsNullOrWhiteSpace(id))
                return null;

            var name = GetString(data, "_RecordName_")
                    ?? GetString(data, "name")
                    ?? GetString(data, "displayName")
                    ?? id;

            var displayName = GetString(data, "displayName") ?? name;
            var description = GetString(data, "description") ?? string.Empty;

            // Construire un objet JSON normalisé
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("name", CleanLocalizationKey(name));
            writer.WriteString("display_name", CleanLocalizationKey(displayName));
            writer.WriteString("category", groupName);
            writer.WriteString("description", CleanLocalizationKey(description));

            // Propriétés additionnelles si présentes
            if (data.TryGetProperty("properties", out var props) &&
                props.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray("properties");
                foreach (var prop in props.EnumerateArray())
                {
                    if (prop.ValueKind == JsonValueKind.Null) continue;
                    prop.WriteTo(writer);
                }
                writer.WriteEndArray();
            }

            // Version raffinée si présente
            if (data.TryGetProperty("refinedVersion", out var refined) &&
                refined.ValueKind != JsonValueKind.Null)
            {
                writer.WritePropertyName("refined_version");
                refined.WriteTo(writer);
            }

            writer.WriteEndObject();
            writer.Flush();

            ms.Position = 0;
            var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ressource ignorée lors du parsing dans le groupe {GroupName}", groupName);
            return null;
        }
    }

    private void BuildResourceMaps(IReadOnlyList<JsonElement> resources)
    {
        var byId   = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var byName = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            var id   = GetString(resource, "id");
            var name = GetString(resource, "name");

            if (!string.IsNullOrWhiteSpace(id))
                byId[id] = resource;
            if (!string.IsNullOrWhiteSpace(name))
                byName[name] = resource;
        }

        _resourceById   = byId;
        _resourceByName = byName;
    }
}