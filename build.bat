@echo off
REM Build script pour ExtractBlueprintSC (Windows)
REM Compile l'exécutable autonome dans le dossier bin/

echo Compilation de ExtractBlueprintSC...
dotnet publish src\ExtractBlueprintSC.Cli\ExtractBlueprintSC.Cli.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o .\bin

echo.
echo Build terminé ! Exécutable disponible dans: .\bin\ExtractBlueprintSC.exe
echo Exécutez: .\bin\ExtractBlueprintSC.exe --help