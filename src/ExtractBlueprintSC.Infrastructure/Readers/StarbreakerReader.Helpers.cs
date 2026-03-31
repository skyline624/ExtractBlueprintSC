using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.Readers;

public sealed partial class StarbreakerReader
{
    private IEnumerable<string> FindJsonFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("Dossier introuvable lors de la recherche JSON : {Directory}", directory);
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
            yield return file;
    }

    private JsonDocument? TryLoadJson(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return JsonDocument.Parse(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger le fichier JSON : {Path}", path);
            return null;
        }
    }

    private string ResolveResourceName(string resourceName, string resourceId)
    {
        // 1. Lookup par ID
        if (!string.IsNullOrWhiteSpace(resourceId) &&
            _resourceById is not null &&
            _resourceById.TryGetValue(resourceId, out var byId))
        {
            var name = GetString(byId, "name") ?? GetString(byId, "_RecordName_");
            if (!string.IsNullOrWhiteSpace(name))
                return CleanLocalizationKey(name);
        }

        // 2. Lookup par nom
        if (!string.IsNullOrWhiteSpace(resourceName) &&
            _resourceByName is not null &&
            _resourceByName.TryGetValue(resourceName, out var byName))
        {
            var name = GetString(byName, "name") ?? GetString(byName, "_RecordName_");
            if (!string.IsNullOrWhiteSpace(name))
                return CleanLocalizationKey(name);
        }

        // 3. Nettoyer la clé de localisation
        if (!string.IsNullOrWhiteSpace(resourceName))
            return CleanLocalizationKey(resourceName);

        return resourceId;
    }

    private static string CleanLocalizationKey(string key)
    {
        // Supprime le préfixe @ utilisé pour les clés de localisation Star Citizen
        if (key.StartsWith('@'))
            key = key[1..];

        // Remplace les underscores par des espaces et met en title case
        if (key.Contains('_') && !key.Any(char.IsUpper))
        {
            var parts = key.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', parts.Select(p => char.ToUpper(p[0]) + p[1..]));
        }

        return key;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static float GetCraftTimeSeconds(JsonElement element)
    {
        // Calcul depuis les composantes temporelles
        var days    = GetFloat(element, "days")    ?? GetFloat(element, "craftingTime_Days")    ?? 0f;
        var hours   = GetFloat(element, "hours")   ?? GetFloat(element, "craftingTime_Hours")   ?? 0f;
        var minutes = GetFloat(element, "minutes") ?? GetFloat(element, "craftingTime_Minutes") ?? 0f;
        var seconds = GetFloat(element, "seconds") ?? GetFloat(element, "craftingTime_Seconds") ?? 0f;

        // Fallback vers valeur directe
        var direct = GetFloat(element, "craft_time") ?? GetFloat(element, "craftTime");
        if (direct.HasValue && days == 0 && hours == 0 && minutes == 0 && seconds == 0)
            return direct.Value;

        return (days * 86400f) + (hours * 3600f) + (minutes * 60f) + seconds;
    }

    private static float? GetFloat(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.TryGetSingle(out var value))
            return value;
        return null;
    }
}
