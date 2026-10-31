@echo off
rem ============================================================
rem  ADOps GPO Manager - MODE PRODUCTION (session implicite)
rem  ------------------------------------------------------------
rem  EXE single-file self-contained (.NET 10 inclus, compresse)
rem  - Aucune installation .NET requise sur la machine cible
rem  - AUCUN ecran de connexion : la session Windows courante
rem    (compte administrateur de la session) est l'identite
rem  - Demarrage = detection du domaine + synchronisation
rem    immediate de l'inventaire (OU, machines, comptes, GPO)
rem  - Protection : refuse un 2e lancement si instance en cours
rem ============================================================
setlocal
set "EXE=%~dp0publish\ADOps.GpoManager.App.exe"

if not exist "%EXE%" (
    echo.
    echo [ERREUR] EXE de production introuvable : %EXE%
    echo Lancez d'abord la publication : dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
    pause
    exit /b 1
)

tasklist /FI "IMAGENAME eq ADOps.GpoManager.App.exe" 2>nul | find /I "ADOps.GpoManager.App.exe" >nul
if not errorlevel 1 (
    echo.
    echo  [!] Une instance d'ADOps GPO Manager est DEJA en cours d'execution.
    echo      Fermez-la d'abord pour proteger audit.db / settings.json.
    echo.
    pause
    exit /b 0
)

echo === DEMARRAGE - Session implicite + synchronisation immediate ===
echo  L'application utilise le compte Windows de la session courante.
echo  Lancez ce script EN TANT QU'ADMINISTRATEUR (clic droit > Executer
echo  en tant qu'administrateur) : la creation de liens de GPO l'exige.
start "" "%EXE%"
endlocal
