using System.CommandLine;
using ExtractBlueprintSC.Application.UseCases;
using ExtractBlueprintSC.Core.Interfaces;
using ExtractBlueprintSC.Infrastructure.Readers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace ExtractBlueprintSC.Cli.Commands;

public static class ParseCommand
{
    public static Command Build(IServiceProvider services)
    {
        // Options avec aliases multiples
        var inputOption = new Option<DirectoryInfo>("--input")
        {
            Description = "Dossier records extrait par StarBreaker (ex: extracted/dcb_json/libs/foundry/records)",
            Required = true,
        };
        inputOption.Aliases.Add("-i");

        var outputOption = new Option<string>("--output")
        {
            Description = "Chemin du fichier JSON de sortie",
            DefaultValueFactory = _ => "output/blueprints.json",
        };
        outputOption.Aliases.Add("-o");

        var verboseOption = new Option<bool>("--verbose")
        {
            Description = "Activer les logs détaillés",
            DefaultValueFactory = _ => false,
        };
        verboseOption.Aliases.Add("-v");

        var command = new Command("parse", "Parser des données pré-extraites par StarBreaker")
        {
            inputOption,
            outputOption,
            verboseOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var inputDir = parseResult.GetValue(inputOption)!;
            var outputPath = parseResult.GetValue(outputOption)!;
            var verbose = parseResult.GetValue(verboseOption);

            if (!inputDir.Exists)
            {
                AnsiConsole.MarkupLine($"[red]Erreur :[/] Dossier introuvable : {inputDir.FullName}");
                return;
            }

            var loggerFactory = services.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger<StarbreakerReader>();

            Application.Models.ExtractionResult? result = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync("Parsing des données...", async ctx =>
                {
                    ctx.Status("Chargement des fichiers JSON...");

                    var reader = new StarbreakerReader(inputDir.FullName, logger);
                    var useCase = CreateUseCase(services, reader);

                    ctx.Status("Transformation des données...");
                    result = await useCase.ExecuteAsync(outputPath, cancellationToken);
                });

            if (result is not null)
                DisplayResults(result);
        });

        return command;
    }

    private static ExtractBlueprintsUseCase CreateUseCase(IServiceProvider services, IDataReader reader)
    {
        return ActivatorUtilities.CreateInstance<ExtractBlueprintsUseCase>(services, reader);
    }

    internal static void DisplayResults(Application.Models.ExtractionResult result)
    {
        AnsiConsole.WriteLine();

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Cyan1)
            .AddColumn(new TableColumn("[bold]Donnée[/]").Centered())
            .AddColumn(new TableColumn("[bold]Quantité[/]").RightAligned());

        table.AddRow("[cyan]Blueprints[/]",  $"[green]{result.BlueprintsCount:N0}[/]");
        table.AddRow("[cyan]Ressources[/]",  $"[green]{result.ResourcesCount:N0}[/]");
        table.AddRow("[cyan]Missions[/]",    $"[green]{result.MissionsCount:N0}[/]");

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[bold green]Fichier exporté :[/] {result.OutputPath}");
    }
}