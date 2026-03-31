using System.CommandLine;
using ExtractBlueprintSC.Application.UseCases;
using ExtractBlueprintSC.Core.Domain.Exceptions;
using ExtractBlueprintSC.Core.Interfaces;
using ExtractBlueprintSC.Infrastructure.Readers;
using ExtractBlueprintSC.Infrastructure.StarBreaker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace ExtractBlueprintSC.Cli.Commands;

public static class ExtractCommand
{
    public static Command Build(IServiceProvider services)
    {
        var inputOption = new Option<FileInfo>("--input")
        {
            Description = "Chemin vers le fichier Data.p4k de Star Citizen",
            Required = true,
        };
        inputOption.Aliases.Add("-i");

        var outputOption = new Option<string>("--output")
        {
            Description = "Chemin du fichier JSON de sortie",
            DefaultValueFactory = _ => "output/blueprints.json",
        };
        outputOption.Aliases.Add("-o");

        var extractedDirOption = new Option<string>("--extracted-dir")
        {
            Description = "Dossier temporaire pour l'extraction",
            DefaultValueFactory = _ => "extracted",
        };
        extractedDirOption.Aliases.Add("-e");

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Activer les logs détaillés",
            DefaultValueFactory = _ => false,
        };
        verboseOption.Aliases.Add("-v");

        var command = new Command("extract", "Extraire les blueprints directement depuis Data.p4k")
        {
            inputOption,
            outputOption,
            extractedDirOption,
            verboseOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var inputFile = parseResult.GetValue(inputOption)!;
            var outputPath = parseResult.GetValue(outputOption)!;
            var extractedDir = parseResult.GetValue(extractedDirOption)!;

            if (!inputFile.Exists)
            {
                AnsiConsole.MarkupLine($"[red]Erreur :[/] Fichier P4K introuvable : {inputFile.FullName}");
                return;
            }

            var extractor = services.GetRequiredService<StarBreakerExtractor>();
            if (!extractor.IsAvailable())
            {
                AnsiConsole.MarkupLine("[red]Erreur :[/] starbreaker-cli introuvable.");
                AnsiConsole.MarkupLine("Téléchargez-le depuis : https://github.com/diogotr7/StarBreaker/releases");
                return;
            }

            Application.Models.ExtractionResult? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("Extraction en cours...", async ctx =>
                {
                    try
                    {
                        ctx.Status("Extraction du DCB depuis le P4K...");
                        var recordsDir = await extractor.ExtractDcbAsync(
                            inputFile.FullName, extractedDir, cancellationToken);

                        ctx.Status("Chargement des fichiers JSON...");
                        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
                        var logger = loggerFactory.CreateLogger<StarbreakerReader>();
                        var reader = new StarbreakerReader(recordsDir, logger);

                        var useCase = CreateUseCase(services, reader);

                        ctx.Status("Transformation des données...");
                        result = await useCase.ExecuteAsync(outputPath, cancellationToken);
                    }
                    catch (ExtractionException ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Erreur d'extraction :[/] {ex.Message}");
                    }
                    catch (OperationCanceledException)
                    {
                        AnsiConsole.MarkupLine("[yellow]Extraction annulée.[/]");
                        throw;
                    }
                });

            if (result is not null)
                ParseCommand.DisplayResults(result);
        });

        return command;
    }

    private static ExtractBlueprintsUseCase CreateUseCase(
        IServiceProvider services,
        IDataReader reader)
    {
        return ActivatorUtilities.CreateInstance<ExtractBlueprintsUseCase>(services, reader);
    }
}