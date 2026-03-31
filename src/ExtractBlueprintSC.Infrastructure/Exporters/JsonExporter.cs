using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExtractBlueprintSC.Core.Domain.Entities;
using ExtractBlueprintSC.Core.Interfaces;
using ExtractBlueprintSC.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtractBlueprintSC.Infrastructure.Exporters;

public sealed class JsonExporter : IDataExporter
{
    private readonly IOptions<ExtractionOptions> _options;
    private readonly ILogger<JsonExporter> _logger;

    public JsonExporter(IOptions<ExtractionOptions> options, ILogger<JsonExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    public async Task ExportAsync(BlueprintCollection collection, string outputPath,
                                   CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var opts = _options.Value;

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = opts.EnsureAscii
                ? JavaScriptEncoder.Default
                : JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        };

        try
        {
            await using var stream = new FileStream(outputPath, FileMode.Create,
                                                     FileAccess.Write, FileShare.None,
                                                     bufferSize: 65536, useAsync: true);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            var json = JsonSerializer.Serialize(collection, serializerOptions);
            await writer.WriteAsync(json);

            _logger.LogInformation(
                "Export JSON terminé : {Blueprints} blueprints, {Resources} ressources, {Missions} missions → {Path}",
                collection.Blueprints.Count,
                collection.Resources.Count,
                collection.Missions.Count,
                outputPath);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Erreur I/O lors de l'export vers {Path}", outputPath);
            throw;
        }
    }
}
