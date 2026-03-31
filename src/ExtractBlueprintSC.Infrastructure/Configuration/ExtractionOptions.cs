namespace ExtractBlueprintSC.Infrastructure.Configuration;

public sealed class ExtractionOptions
{
    public const string SectionName = "Extraction";

    public string StarBreakerCliPath  { get; init; } = "tools/starbreaker-cli";
    public string DefaultOutputPath   { get; init; } = "output/blueprints.json";
    public string DefaultExtractedDir { get; init; } = "extracted";
    public int    JsonIndent          { get; init; } = 2;
    public bool   EnsureAscii         { get; init; } = false;
}
