using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ExtractBlueprintSC.Infrastructure.Readers;

public sealed partial class StarbreakerReader
{
    private const int MaxRecursionDepth = 10;

    public IReadOnlyList<JsonElement> GetBlueprints()
    {
        if (_blueprintsCache is not null)
            return _blueprintsCache;

        // S'assurer que les ressources et missions sont chargées (pour les lookups)
        _ = GetResources();
        _ = GetMissions();

        var blueprintsDir = Path.Combine(_baseDir, BlueprintsSubPath);
        var results = new List<JsonElement>();

        if (!Directory.Exists(blueprintsDir))
        {
            _logger.LogWarning("Dossier des blueprints introuvable : {Path}", blueprintsDir);
            _blueprintsCache = Array.Empty<JsonElement>();
            return _blueprintsCache;
        }

        foreach (var filePath in FindJsonFiles(blueprintsDir))
        {
            using var doc = TryLoadJson(filePath);
            if (doc is null) continue;

            var blueprint = ParseBlueprint(doc.RootElement, filePath);
            if (blueprint.HasValue)
                results.Add(blueprint.Value.Clone());
        }

        _logger.LogInformation("Blueprints chargés : {Count}", results.Count);
        _blueprintsCache = results.AsReadOnly();
        return _blueprintsCache;
    }

    private JsonElement? ParseBlueprint(JsonElement data, string filePath)
    {
        try
        {
            var root = data;

            // Naviguer dans _RecordValue_.blueprint si présent
            if (root.TryGetProperty("_RecordValue_", out var recordValue))
            {
                root = recordValue;
                if (root.TryGetProperty("blueprint", out var bpNode))
                    root = bpNode;
            }

            var id   = GetString(data, "_RecordId_") ?? GetString(data, "id");
            var name = GetString(data, "_RecordName_")
                    ?? GetString(data, "name")
                    ?? Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrWhiteSpace(id))
                id = Path.GetFileNameWithoutExtension(filePath);

            // Extraire le craftTime depuis tiers[].recipe.costs.craftTime
            var craftTime = ExtractCraftTime(root);

            // Parsing des ingrédients depuis tiers[].recipe.costs
            var ingredients = new List<JsonElement>();
            var qualityOutput = new Dictionary<string, object>();

            if (root.TryGetProperty("tiers", out var tiers) &&
                tiers.ValueKind == JsonValueKind.Array)
            {
                var tierIndex = 0;
                foreach (var tier in tiers.EnumerateArray())
                {
                    if (tier.TryGetProperty("recipe", out var recipe) &&
                        recipe.TryGetProperty("costs", out var costs))
                    {
                        ParseCosts(costs, ingredients, qualityOutput, tierIndex, depth: 0);
                    }
                    tierIndex++;
                }
            }

            // Missions sources depuis la map construite lors du chargement des missions
            var missionSources = new List<string>();
            var cleanName = CleanBlueprintName(name);
            if (_blueprintMissionMap is not null &&
                _blueprintMissionMap.TryGetValue(cleanName, out var missions))
            {
                missionSources.AddRange(missions);
            }

            // Catégorie depuis le nœud category
            var category = GetString(root, "category")
                        ?? GetString(data, "category")
                        ?? string.Empty;

            // Résoudre le nom de catégorie depuis une référence
            if (category.StartsWith("file://") || category.StartsWith("BlueprintCategoryRecord."))
            {
                category = ExtractCategoryFromReference(category);
            }

            // Déterminer la catégorie depuis le nom si vide
            if (string.IsNullOrWhiteSpace(category) || category == "other")
            {
                category = DetermineCategoryFromName(name);
            }

            // Sérialiser en JsonElement normalisé
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms);

            writer.WriteStartObject();
            writer.WriteString("id", id);
            writer.WriteString("name", cleanName);
            writer.WriteString("category", CleanLocalizationKey(category));
            writer.WriteNumber("craft_time", craftTime);
            writer.WriteString("source_file", Path.GetRelativePath(_baseDir, filePath));

            writer.WriteStartArray("ingredients");
            foreach (var ingredient in ingredients)
                ingredient.WriteTo(writer);
            writer.WriteEndArray();

            writer.WriteStartArray("mission_sources");
            foreach (var ms2 in missionSources)
                writer.WriteStringValue(ms2);
            writer.WriteEndArray();

            writer.WriteStartObject("quality_output");
            foreach (var (key, value) in qualityOutput)
            {
                writer.WritePropertyName(key);
                if (value is List<JsonElement> jsonList)
                {
                    writer.WriteStartArray();
                    foreach (var elem in jsonList)
                        elem.WriteTo(writer);
                    writer.WriteEndArray();
                }
                else if (value is JsonElement qje)
                    qje.WriteTo(writer);
                else
                    writer.WriteStringValue(value.ToString());
            }
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.Flush();

            ms.Position = 0;
            var doc = JsonDocument.Parse(ms);
            return doc.RootElement.Clone();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Blueprint ignoré : {File}", filePath);
            return null;
        }
    }

    private float ExtractCraftTime(JsonElement root)
    {
        // Extraire depuis tiers[].recipe.costs.craftTime
        if (root.TryGetProperty("tiers", out var tiers) &&
            tiers.ValueKind == JsonValueKind.Array)
        {
            foreach (var tier in tiers.EnumerateArray())
            {
                if (tier.TryGetProperty("recipe", out var recipe) &&
                    recipe.TryGetProperty("costs", out var costs))
                {
                    if (costs.TryGetProperty("craftTime", out var craftTime))
                    {
                        var days    = GetFloat(craftTime, "days") ?? 0f;
                        var hours   = GetFloat(craftTime, "hours") ?? 0f;
                        var minutes = GetFloat(craftTime, "minutes") ?? 0f;
                        var seconds = GetFloat(craftTime, "seconds") ?? 0f;
                        return (days * 86400f) + (hours * 3600f) + (minutes * 60f) + seconds;
                    }
                }
            }
        }

        return 0f;
    }

    private void ParseCosts(
        JsonElement costsData,
        List<JsonElement> ingredients,
        Dictionary<string, object> qualityOutput,
        int tierIndex,
        int depth)
    {
        if (depth > MaxRecursionDepth) return;

        // Coûts directs
        if (costsData.TryGetProperty("mandatoryCost", out var mandatoryCost))
        {
            ProcessCostNode(mandatoryCost, ingredients, qualityOutput, tierIndex, depth);
        }

        if (costsData.TryGetProperty("optionalCosts", out var optionalCosts) &&
            optionalCosts.ValueKind == JsonValueKind.Array)
        {
            foreach (var optCost in optionalCosts.EnumerateArray())
                ProcessCostNode(optCost, ingredients, qualityOutput, tierIndex, depth);
        }
    }

    private void ProcessCostNode(
        JsonElement node,
        List<JsonElement> ingredients,
        Dictionary<string, object> qualityOutput,
        int tierIndex,
        int depth)
    {
        var nodeType = GetString(node, "_Type_") ?? string.Empty;

        if (nodeType == "CraftingCost_Resource")
        {
            // Ingrédient direct
            var ingredient = BuildIngredientElement(node, tierIndex);
            if (ingredient.HasValue)
                ingredients.Add(ingredient.Value);
        }
        else if (nodeType == "CraftingCost_Select")
        {
            // Options — traiter chaque option
            if (node.TryGetProperty("options", out var options) &&
                options.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in options.EnumerateArray())
                    ProcessCostNode(option, ingredients, qualityOutput, tierIndex, depth + 1);
            }

            // Chercher gameplayPropertyModifiers dans le contexte
            if (node.TryGetProperty("context", out var context) &&
                context.ValueKind == JsonValueKind.Array)
            {
                foreach (var ctx in context.EnumerateArray())
                {
                    if (ctx.TryGetProperty("gameplayPropertyModifiers", out var modifiers))
                    {
                        ExtractQualityModifiersFromContext(modifiers, qualityOutput, tierIndex);
                    }
                }
            }
        }
        else if (nodeType == "CraftingRecipe" || nodeType == "CraftingRecipeCosts")
        {
            // Sous-recipe
            if (node.TryGetProperty("costs", out var subCosts))
                ParseCosts(subCosts, ingredients, qualityOutput, tierIndex, depth + 1);
        }
        else
        {
            // Tenter de trouver des coûts imbriqués
            if (node.TryGetProperty("options", out var options) &&
                options.ValueKind == JsonValueKind.Array)
            {
                foreach (var option in options.EnumerateArray())
                    ProcessCostNode(option, ingredients, qualityOutput, tierIndex, depth + 1);
            }

            // Chercher gameplayPropertyModifiers
            if (node.TryGetProperty("context", out var context) &&
                context.ValueKind == JsonValueKind.Array)
            {
                foreach (var ctx in context.EnumerateArray())
                {
                    if (ctx.TryGetProperty("gameplayPropertyModifiers", out var modifiers))
                        ExtractQualityModifiersFromContext(modifiers, qualityOutput, tierIndex);
                }
            }
        }
    }

    private JsonElement? BuildIngredientElement(JsonElement node, int tierIndex)
    {
        string? resourceId = null;
        string? resourceName = null;
        float quantity = 0f;
        float minQuality = 0f;

        // Resource ID
        if (node.TryGetProperty("resource", out var resourceRef))
        {
            if (resourceRef.ValueKind == JsonValueKind.Object)
            {
                resourceId = GetString(resourceRef, "_RecordId_")
                          ?? GetString(resourceRef, "_RecordName_");
            }
            else if (resourceRef.ValueKind == JsonValueKind.String)
            {
                resourceId = resourceRef.GetString();
            }
        }

        // Quantity
        if (node.TryGetProperty("quantity", out var qtyRef))
        {
            if (qtyRef.ValueKind == JsonValueKind.Object &&
                qtyRef.TryGetProperty("standardCargoUnits", out var scu))
            {
                if (scu.TryGetSingle(out var scuValue))
                    quantity = scuValue;
            }
            else if (qtyRef.TryGetSingle(out var qtyValue))
            {
                quantity = qtyValue;
            }
        }

        // Min Quality
        if (node.TryGetProperty("minQuality", out var minQty) &&
            minQty.TryGetSingle(out var mq))
        {
            minQuality = mq;
        }

        if (string.IsNullOrWhiteSpace(resourceId))
            return null;

        // Résoudre le nom de la ressource
        resourceName = ResolveResourceName(resourceId, resourceId);

        // Sérialiser
        using var ms = new MemoryStream();
        using var writer = new Utf8JsonWriter(ms);

        writer.WriteStartObject();
        writer.WriteString("resource_id", resourceId);
        writer.WriteString("resource_name", resourceName);
        writer.WriteNumber("quantity", quantity);
        writer.WriteNumber("tier_index", tierIndex);

        writer.WriteStartObject("quality_requirements");
        if (minQuality > 0)
            writer.WriteNumber("min_quality", minQuality);
        writer.WriteEndObject();

        writer.WriteEndObject();
        writer.Flush();

        ms.Position = 0;
        var doc = JsonDocument.Parse(ms);
        return doc.RootElement.Clone();
    }

    private void ExtractQualityModifiersFromContext(
        JsonElement modifiers,
        Dictionary<string, object> qualityOutput,
        int tierIndex)
    {
        if (modifiers.ValueKind != JsonValueKind.Object &&
            modifiers.ValueKind != JsonValueKind.Array)
            return;

        // Peut être un objet avec gameplayPropertyModifiers à l'intérieur
        JsonElement modifiersArray = modifiers;
        if (modifiers.ValueKind == JsonValueKind.Object &&
            modifiers.TryGetProperty("gameplayPropertyModifiers", out var gpm))
        {
            modifiersArray = gpm;
        }

        if (modifiersArray.ValueKind != JsonValueKind.Array)
            return;

        foreach (var modifier in modifiersArray.EnumerateArray())
        {
            var propName = GetString(modifier, "gameplayPropertyRecord");
            if (string.IsNullOrWhiteSpace(propName))
                continue;

            // Extraire le nom de la propriété depuis le chemin
            propName = ExtractPropertyNameFromPath(propName);

            var tierKey = $"tier_{tierIndex}";
            if (!qualityOutput.ContainsKey(propName))
                qualityOutput[propName] = new List<JsonElement>();

            if (modifier.TryGetProperty("valueRanges", out var valueRanges) &&
                valueRanges.ValueKind == JsonValueKind.Array)
            {
                var ranges = (List<JsonElement>)qualityOutput[propName];
                foreach (var range in valueRanges.EnumerateArray())
                    ranges.Add(range.Clone());
            }
        }
    }

    private static string ExtractPropertyNameFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "unknown";

        // file://./../../../../../../../libs/foundry/records/crafting/craftedproperties/gpp_armor_temperaturemax.json
        // -> gpp_armor_temperaturemax
        if (path.StartsWith("file://"))
            path = path.Substring(7);

        var fileName = Path.GetFileNameWithoutExtension(path);
        // gpp_armor_temperaturemax -> armor_temperaturemax
        if (fileName.StartsWith("gpp_"))
            fileName = fileName.Substring(4);

        return fileName;
    }

    private static string CleanBlueprintName(string name)
    {
        // CraftingBlueprintRecord.BP_CRAFT_syfb_flightsuit_helmet_01_01_01
        // -> syfb_flightsuit_helmet_01_01_01
        if (name.StartsWith("CraftingBlueprintRecord.BP_CRAFT_"))
            name = name.Substring("CraftingBlueprintRecord.BP_CRAFT_".Length);
        else if (name.StartsWith("BP_CRAFT_"))
            name = name.Substring("BP_CRAFT_".Length);

        return name;
    }

    private static string ExtractCategoryFromReference(string reference)
    {
        // BlueprintCategoryRecord.FPSArmours -> FPSArmours
        // file://./../../.../BlueprintCategoryRecord.FPSArmours -> FPSArmours
        if (reference.StartsWith("file://"))
            reference = Path.GetFileNameWithoutExtension(reference);

        if (reference.StartsWith("BlueprintCategoryRecord."))
            return reference.Substring("BlueprintCategoryRecord.".Length);

        return reference;
    }

    private static string DetermineCategoryFromName(string name)
    {
        var lowerName = name.ToLowerInvariant();

        if (lowerName.Contains("weapon") || lowerName.Contains("gun") || lowerName.Contains("rifle") ||
            lowerName.Contains("pistol") || lowerName.Contains("shotgun") || lowerName.Contains("launcher"))
            return "weapon";

        if (lowerName.Contains("armor") || lowerName.Contains("suit") || lowerName.Contains("helmet") ||
            lowerName.Contains("vest") || lowerName.Contains("backpack") || lowerName.Contains("undersuit") ||
            lowerName.Contains("flightsuit"))
            return "armor";

        if (lowerName.Contains("ship") || lowerName.Contains("vehicle") || lowerName.Contains("spacecraft"))
            return "ship";

        if (lowerName.Contains("component") || lowerName.Contains("module") || lowerName.Contains("upgrade") ||
            lowerName.Contains("drive") || lowerName.Contains("shield") || lowerName.Contains("cooler") ||
            lowerName.Contains("powerplant"))
            return "component";

        if (lowerName.Contains("consumable") || lowerName.Contains("medical") || lowerName.Contains("food") ||
            lowerName.Contains("drink") || lowerName.Contains("drug") || lowerName.Contains("stim"))
            return "consumable";

        return "other";
    }
}