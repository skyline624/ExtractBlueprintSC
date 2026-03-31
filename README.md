# ExtractBlueprintSC

Extracteur de blueprints Star Citizen en C# / .NET 8.0.

Ce projet permet d'extraire les données de crafting (blueprints, ressources, missions) depuis les fichiers de données du jeu Star Citizen (`Data.p4k` ou JSON pré-extraits via StarBreaker).

## Fonctionnalités

- **Extraction depuis P4K** — Extraction directe du fichier `Data.p4k` via l'outil StarBreaker CLI
- **Parsing JSON** — Parsing des données pré-extraites par StarBreaker
- **Export JSON** — Export des blueprints, ressources et missions au format JSON structuré
- **CLI moderne** — Interface en ligne de commande avec `System.CommandLine` et `Spectre.Console`

## Structure du projet

```
ExtractBlueprintSC/
├── bin/                                  # Exécutable compilé
│   └── ExtractBlueprintSC                # Exécutable autonome
├── ExtractBlueprintSC.sln
└── src/
    ├── ExtractBlueprintSC.Core/              # Entités métier + interfaces
    │   ├── Domain/Entities/                 # QualityTier, Resource, Ingredient, Mission, Blueprint, BlueprintCollection
    │   ├── Domain/Exceptions/               # ExtractionException, TransformationException
    │   └── Interfaces/                      # IDataReader, IDataExporter
    │
    ├── ExtractBlueprintSC.Application/      # Logique métier
    │   ├── Models/                          # ExtractionResult
    │   ├── Services/                        # ResourceTransformService, MissionTransformService, BlueprintTransformService
    │   ├── UseCases/                        # ExtractBlueprintsUseCase
    │   └── Extensions/                      # AddApplication()
    │
    ├── ExtractBlueprintSC.Infrastructure/  # Implémentations techniques
    │   ├── Configuration/                   # ExtractionOptions (IOptions<T>)
    │   ├── Readers/                         # StarbreakerReader (5 fichiers partiels)
    │   ├── Exporters/                       # JsonExporter
    │   ├── StarBreaker/                     # StarBreakerExtractor (wrapper CLI)
    │   └── Extensions/                      # AddInfrastructure()
    │
    └── ExtractBlueprintSC.Cli/             # Point d'entrée CLI
        ├── Commands/                        # ExtractCommand, ParseCommand
        ├── appsettings.json                 # Configuration
        └── Program.cs                       # Generic Host + System.CommandLine
```

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                              CLI                                 │
│  (System.CommandLine + Spectre.Console + Generic Host)          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                         Application                              │
│  (ExtractBlueprintsUseCase + Transform Services)                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                            Core                                  │
│  (Entities + IDataReader + IDataExporter)                        │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Infrastructure                             │
│  (StarbreakerReader + JsonExporter + StarBreakerExtractor)      │
└─────────────────────────────────────────────────────────────────┘
```

## Prérequis

- **.NET 8.0 SDK** — [Télécharger](https://dotnet.microsoft.com/download/dotnet/8.0)
- **StarBreaker CLI** (optionnel, pour l'extraction depuis P4K) — [Télécharger](https://github.com/diogotr7/StarBreaker/releases)

## Installation

```bash
# Cloner le repository
git clone https://github.com/skyline624/ExtractBlueprintSC
cd ExtractBlueprintSC

# Compiler l'exécutable autonome (Linux)
./build.sh

# Ou sous Windows
# build.bat

# L'exécutable est disponible dans bin/ExtractBlueprintSC
```

L'exécutable est autonome et ne nécessite pas l'installation de .NET Runtime.

## Utilisation

### Commande `parse` — Parser des données pré-extraites

```bash
./bin/ExtractBlueprintSC parse \
    --input /chemin/vers/extracted/dcb_json/libs/foundry/records \
    --output blueprints.json

# Options disponibles
--input, -i    Chemin vers le dossier records extrait par StarBreaker (requis)
--output, -o   Chemin du fichier JSON de sortie (défaut: output/blueprints.json)
--verbose, -v  Activer les logs détaillés
```

### Commande `extract` — Extraire depuis Data.p4k

```bash
./bin/ExtractBlueprintSC extract \
    --input /chemin/vers/Data.p4k \
    --output blueprints.json \
    --extracted-dir extracted

# Options disponibles
--input, -i          Chemin vers le fichier Data.p4k (requis)
--output, -o          Chemin du fichier JSON de sortie (défaut: output/blueprints.json)
--extracted-dir, -e   Dossier temporaire pour l'extraction (défaut: extracted)
--verbose, -v        Activer les logs détaillés
```

**Note :** La commande `extract` nécessite que `starbreaker-cli` soit disponible dans le dossier `tools/` ou configuré dans `appsettings.json`.

## Format de sortie

Le fichier JSON de sortie contient :

```json
{
  "blueprints": [
    {
      "id": "uuid",
      "name": "syfb_flightsuit_helmet_01_01_01",
      "category": "armor",
      "craft_time": 90,
      "ingredients": [
        {
          "resource_id": "uuid",
          "resource_name": "ResourceType.Silicon",
          "quantity": 0.03,
          "quality_requirements": {}
        }
      ],
      "mission_sources": [],
      "quality_output": {
        "armor_temperaturemax": [
          {
            "startQuality": 0,
            "endQuality": 1000,
            "modifierAtStart": 0.8,
            "modifierAtEnd": 1.2
          }
        ]
      }
    }
  ],
  "resources": [...],
  "missions": [...]
}
```

## Configuration

Le fichier `appsettings.json` permet de configurer :

```json
{
  "Extraction": {
    "StarBreakerCliPath": "tools/starbreaker-cli",
    "DefaultOutputPath": "output/blueprints.json",
    "DefaultExtractedDir": "extracted",
    "JsonIndent": 2,
    "EnsureAscii": false
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "ExtractBlueprintSC": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

## Principes de conception

### SOLID
- **Single Responsibility** — Chaque service a une responsabilité unique (transformation, lecture, export)
- **Open/Closed** — Nouveaux readers/exporters via interfaces sans modification du code existant
- **Liskov Substitution** — Toutes les implémentations sont substituables
- **Interface Segregation** — Interfaces `IDataReader` et `IDataExporter` séparées
- **Dependency Inversion** — Dépendances par abstraction, injection par constructeur

### DRY
- Pas de duplication — logique partagée dans les classes partielles (`StarbreakerReader.Helpers.cs`)
- Transformation centralisée dans les services dédiés

### KISS
- Pas de sur-ingénierie
- Code direct et lisible
- Pas d'abstraction inutile

### Enterprise-Grade
- Guard clauses (`ArgumentNullException.ThrowIfNull`)
- Nullable reference types activés
- Logging structuré avec `ILogger<T>`
- Exception handling défensif
- Configuration via `IOptions<T>`
- Generic Host pour la gestion du cycle de vie

## Extensions possibles

### Ajouter un nouveau reader

```csharp
public class CustomReader : IDataReader
{
    public IReadOnlyList<JsonElement> GetBlueprints() { ... }
    public IReadOnlyList<JsonElement> GetResources() { ... }
    public IReadOnlyList<JsonElement> GetMissions() { ... }
}

// Dans Program.cs ou extension DI
services.AddTransient<IDataReader, CustomReader>();
```

### Ajouter un nouvel exporter

```csharp
public class CsvExporter : IDataExporter
{
    public async Task ExportAsync(BlueprintCollection collection, string outputPath, CancellationToken ct = default)
    {
        // Export CSV
    }
}

// Enregistrement
services.AddTransient<IDataExporter, CsvExporter>();
```

## Dépendances NuGet

| Projet | Package | Version |
|--------|---------|---------|
| Application | Microsoft.Extensions.Logging.Abstractions | 10.0.5 |
| Infrastructure | Microsoft.Extensions.Logging.Abstractions | 10.0.5 |
| Infrastructure | Microsoft.Extensions.Options | 10.0.5 |
| Infrastructure | Microsoft.Extensions.Configuration.Abstractions | 10.0.5 |
| Infrastructure | ZstdSharp.Port | 0.8.7 |
| Cli | Microsoft.Extensions.Hosting | 10.0.5 |
| Cli | System.CommandLine | 3.0.0-preview |
| Cli | Spectre.Console | 0.54.0 |

## Tests

```bash
# Exécuter les tests unitaires
dotnet test

# Tests d'intégration avec données réelles
./bin/ExtractBlueprintSC parse \
    --input /chemin/vers/extracted/dcb_json/libs/foundry/records \
    --output test_output.json
```

## Licence

Ce projet est fourni à titre éducatif. Les données extraites sont la propriété de Cloud Imperium Games.

## Crédits

- Données Star Citizen © Cloud Imperium Games
- Outil StarBreaker — [diogotr7/StarBreaker](https://github.com/diogotr7/StarBreaker)
- Architecture inspirée des patterns de [Liberastra-Bot-Discord](https://github.com/...)