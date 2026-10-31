@echo off
REM ============================================================================
REM  ADOps GPO Manager - Génération de l'exécutable autonome (.exe)
REM  Produit : publish\ADOps.GpoManager.App.exe (fichier unique, autonome)
REM  Prérequis : SDK .NET 10 + runtime Windows x64 cible win-x64
REM ============================================================================
setlocal
cd /d "%~dp0"

echo Restauration des packages NuGet...
dotnet restore ADOps.GpoManager.sln
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo.
echo Publication self-contained win-x64 (fichier unique, compress)...
dotnet publish frontend\ADOps.GpoManager.App\ADOps.GpoManager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -o publish
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo.
echo Generation du manifeste d'integrite (SHA-256, anti-alteration)...
publish\ADOps.GpoManager.App.exe --generate-integrity-manifest
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo.
echo ============================================================================
echo  EXÉCUTABLE GÉNÉRÉ : %~dp0publish\ADOps.GpoManager.App.exe
echo ============================================================================
echo.
echo  Ce fichier est autonome (runtime .NET 10 inclus, ~64 Mo).
echo  Il peut être copié sur n'importe quel poste Windows 10/11 ou
echo  Windows Server 2016+ (x64) SANS installation du runtime .NET.
echo.
echo  Prérequis fonctionnels sur le poste (au moins une des deux) :
echo   - RSAT installe (modules GroupPolicy + ActiveDirectory) pour le mode local,
echo   - WinRM configure sur le contrôleur de domaine (winrm quickconfig) pour
echo     le mode distant.
echo.
pause
