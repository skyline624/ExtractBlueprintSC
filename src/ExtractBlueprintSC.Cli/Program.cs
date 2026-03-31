using System.CommandLine;
using ExtractBlueprintSC.Application.Extensions;
using ExtractBlueprintSC.Cli.Commands;
using ExtractBlueprintSC.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Définir le répertoire de base pour trouver appsettings.json
var appBasePath = AppContext.BaseDirectory;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.SetBasePath(appBasePath);
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
        config.AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddApplication();
        services.AddInfrastructure(context.Configuration);
    })
    .ConfigureLogging((context, logging) =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddConfiguration(context.Configuration.GetSection("Logging"));
    })
    .Build();

var rootCommand = new RootCommand("Star Citizen Blueprint Extractor — Outil d'extraction de blueprints")
{
    ExtractCommand.Build(host.Services),
    ParseCommand.Build(host.Services),
};

// Utiliser Parse et invoquer manuellement
var parseResult = rootCommand.Parse(args);
await parseResult.InvokeAsync();