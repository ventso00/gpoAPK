@echo off
REM ============================================================================
REM  ADOps GPO Manager - Compilation (build de développement)
REM  Prérequis : SDK .NET 10 (dotnet --version)
REM ============================================================================
setlocal
cd /d "%~dp0"

echo Restauration des packages NuGet...
dotnet restore ADOps.GpoManager.sln
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo Compilation (Debug)...
dotnet build ADOps.GpoManager.sln -c Debug --no-restore
if %ERRORLEVEL% NEQ 0 exit /b %ERRORLEVEL%

echo.
echo Build OK. Executable de développement :
echo   frontend\ADOps.GpoManager.App\bin\Debug\net10.0-windows\ADOps.GpoManager.App.exe
echo.
echo Pour generer l'executable autonome (.exe single-file), utiliser publish.bat
pause
