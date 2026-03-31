#!/bin/bash
# Build script pour ExtractBlueprintSC
# Compile l'exécutable autonome dans le dossier bin/

set -e

echo "Compilation de ExtractBlueprintSC..."
dotnet publish src/ExtractBlueprintSC.Cli/ExtractBlueprintSC.Cli.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o ./bin

echo ""
echo "Build terminé ! Exécutable disponible dans: ./bin/ExtractBlueprintSC"
echo "Exécutez: ./bin/ExtractBlueprintSC --help"