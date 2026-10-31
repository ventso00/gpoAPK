Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$src = Get-Content -Raw .uicheck\navsrc.cs
try { Add-Type -TypeDefinition $src -ErrorAction Stop; Write-Host "COMPILED OK" }
catch { Write-Host "COMPILE FAIL"; $_.ErrorDetails.Message; }
