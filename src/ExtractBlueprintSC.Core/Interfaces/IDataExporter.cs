using ExtractBlueprintSC.Core.Domain.Entities;

namespace ExtractBlueprintSC.Core.Interfaces;

public interface IDataExporter
{
    Task ExportAsync(BlueprintCollection collection, string outputPath,
                     CancellationToken cancellationToken = default);
}
