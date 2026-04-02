# ExtractBlueprintSC

Extracteur de blueprints Star Citizen en C# / .NET 8.0.

Ce projet permet d'extraire les données de crafting (blueprints, ressources, missions) depuis les fichiers de données du jeu Star Citizen (`Data.p4k`).

## Fonctionnalités

- **Extraction native depuis P4K** — Parsing ZIP64 + extensions CIG, déchiffrement AES-128-CBC, décompression zstd/deflate
- **Parsing DCB natif** — Parsing complet du format DataCore v6, export JSON identique au CLI Rust original
- **Export JSON** — Export des blueprints, ressources et missions au format JSON structuré
- **CLI moderne** — Interface en ligne de commande avec `System.CommandLine` et `Spectre.Console`
- **Zero dépendance externe** — Aucun binaire tiers requis

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
    ├── ExtractBlueprintSC.Infrastructure/    # Implémentations techniques
    │   ├── Configuration/                   # ExtractionOptions
    │   ├── P4k/                             # P4kEntry, P4kReader (ZIP64 + AES + zstd)
    │   ├── DataCore/                        # DcbDatabase, DcbWalker, DcbJsonExporter
    │   ├── Readers/                         # StarbreakerReader (5 fichiers partiels)
    │   ├── Exporters/                       # JsonExporter
    │   ├── StarBreaker/                     # StarBreakerExtractor (orchestration native)
    │   └── Extensions/                      # AddInfrastructure()
    │
    └── ExtractBlueprintSC.Cli/              # Point d'entrée CLI
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
│  (Entities + IDataReader + IDataExporter)                       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                       Infrastructure                             │
│  (P4kReader + DcbDatabase + StarbreakerReader + JsonExporter)   │
└─────────────────────────────────────────────────────────────────┘
```

## Implémentation native

Le projet intègre une réécriture complète en C# des algorithmes extraits du projet [StarBreaker](https://github.com/diogotr7/StarBreaker) :

### P4K Reader (`P4kReader.cs`)
- Parsing ZIP64 avec extensions CIG (signature `0x14034B50`)
- Déchiffrement AES-128-CBC (clé CIG hardcoded, IV zéro)
- Décompression zstd (via `ZstdSharp.Port`) et deflate
- Parsing du Central Directory, gestion des champs extra `0x5000/0x5002/0x5003`

### DCB Parser (`DcbDatabase.cs`)
- Parsing du header DCB v6 (120 octets)
- Lecture de toutes les sections : struct defs, property defs, enums, data mappings, records
- Value arrays avec accès non-aligné
- Caches de propriétés (ordre parent-first) et détection transitive des weak pointers

### DCB Walker (`DcbWalker.cs`)
- Pré-scan des weak pointers (identique à `prescan_weak_pointers`)
- Traversée récursive des structs avec résolution de références
- Gestion `_RecordName_`, `_RecordId_`, `_Type_`, `_Pointer_`, `_Pointers_`

## Prérequis

- **.NET 8.0 SDK** — [Télécharger](https://dotnet.microsoft.com/download/dotnet/8.0)

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
--input, -i    Chemin vers le dossier records extrait (requis)
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

## Performances

Extraction typique depuis `Data.p4k` (141 Go) :

| Métrique | Valeur |
|----------|--------|
| Temps total | ~34s |
| Temps système | ~10s |
| Records DCB parsés | 111 811 |
| Blueprints extraits | 1 044 |
| Ressources extraites | 195 |
| Missions extraites | 45 |

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
./bin/ExtractBlueprintSC extract \
    --input /chemin/vers/Data.p4k \
    --output test_output.json
```

## Licence

Ce projet est fourni à titre éducatif. Les données extraites sont la propriété de Cloud Imperium Games.

## Crédits

- Données Star Citizen © Cloud Imperium Games
- Algorithme P4K/DCB dérivé de [StarBreaker](https://github.com/diogotr7/StarBreaker) par diogotr7 — réécrit en C# natif