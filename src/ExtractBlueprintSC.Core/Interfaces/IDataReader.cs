using System.Text.Json;

namespace ExtractBlueprintSC.Core.Interfaces;

public interface IDataReader
{
    IReadOnlyList<JsonElement> GetBlueprints();
    IReadOnlyList<JsonElement> GetResources();
    IReadOnlyList<JsonElement> GetMissions();
}
