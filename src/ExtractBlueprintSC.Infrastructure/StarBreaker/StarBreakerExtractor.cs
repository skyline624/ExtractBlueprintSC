using System.Diagnostics;
using ExtractBlueprintSC.Core.Domain.Exceptions;
using ExtractBlueprintSC.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExtractBlueprintSC.Infrastructure.StarBreaker;

public sealed class StarBreakerExtractor
{
    private readonly IOptions<ExtractionOptions> _options;
    private readonly ILogger<StarBreakerExtractor> _logger;

    public StarBreakerExtractor(IOptions<ExtractionOptions> options, ILogger<StarBreakerExtractor> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable()
        => File.Exists(_options.Value.StarBreakerCliPath);

    public async Task<string> ExtractDcbAsync(
        string p4kPath,
        string outputDir,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(p4kPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        if (!File.Exists(p4kPath))
            throw new ExtractionException($"Fichier P4K introuvable : {p4kPath}");

        if (!IsAvailable())
            throw new ExtractionException(
                $"starbreaker-cli introuvable : {_options.Value.StarBreakerCliPath}. " +
                "Téléchargez-le depuis https://github.com/diogotr7/StarBreaker/releases");

        _logger.LogInformation("Extraction P4K : {P4K} → {OutputDir}", p4kPath, outputDir);

        Directory.CreateDirectory(outputDir);

        // 1. Trouver le fichier DCB dans le P4K
        var dcbPath = await FindDcbFileAsync(p4kPath, cancellationToken);
        _logger.LogInformation("Fichier DCB cible : {DcbPath}", dcbPath);

        // 2. Extraire le DCB du P4K
        var dcbOutputDir = Path.Combine(outputDir, "dcb");
        await RunCommandAsync(
            [_options.Value.StarBreakerCliPath, "p4k", "extract", p4kPath, "--filter", dcbPath, "--output", dcbOutputDir],
            cancellationToken);

        var extractedDcb = Path.Combine(dcbOutputDir, dcbPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(extractedDcb))
            throw new ExtractionException($"DCB extrait introuvable : {extractedDcb}");

        // 3. Convertir le DCB en JSON
        var jsonOutputDir = Path.Combine(outputDir, "dcb_json");
        await RunCommandAsync(
            [_options.Value.StarBreakerCliPath, "dcb", "extract", extractedDcb, "--output", jsonOutputDir],
            cancellationToken);

        var recordsDir = Path.Combine(jsonOutputDir, "libs", "foundry", "records");
        if (!Directory.Exists(recordsDir))
        {
            // Tenter un chemin alternatif
            recordsDir = jsonOutputDir;
            _logger.LogWarning("Dossier records standard introuvable, utilisation de : {Path}", recordsDir);
        }

        _logger.LogInformation("Extraction terminée. Dossier records : {Path}", recordsDir);
        return recordsDir;
    }

    private async Task<string> FindDcbFileAsync(string p4kPath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Recherche du fichier DCB dans {P4K}...", p4kPath);

        var output = await RunCommandWithOutputAsync(
            [_options.Value.StarBreakerCliPath, "p4k", "list", p4kPath, "--filter", "*.dcb"],
            cancellationToken);

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith("Game.dcb", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("Game2.dcb", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
        }

        _logger.LogWarning("Game.dcb non trouvé, utilisation du chemin par défaut");
        return "Data/Game2.dcb";
    }

    private async Task RunCommandAsync(string[] args, CancellationToken cancellationToken)
    {
        var (exitCode, _, stderr) = await ExecuteAsync(args, cancellationToken);
        if (exitCode != 0)
            throw new ExtractionException($"starbreaker-cli a échoué (code {exitCode}) : {stderr}");
    }

    private async Task<string> RunCommandWithOutputAsync(string[] args, CancellationToken cancellationToken)
    {
        var (_, stdout, _) = await ExecuteAsync(args, cancellationToken);
        return stdout;
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> ExecuteAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Exécution : {Command}", string.Join(' ', args));

        var psi = new ProcessStartInfo
        {
            FileName = args[0],
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute = false,
        };

        foreach (var arg in args.Skip(1))
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
