$names = 'UIAutomationClient', 'UIAutomationTypes', 'UIAutomationProvider'
foreach ($n in $names) {
    $a = [Reflection.Assembly]::LoadWithPartialName($n)
    if ($null -eq $a) {
        Write-Output "$n = NULL"
        continue
    }
    Write-Output ($n + ' v' + $a.GetName().Version + ' at ' + $a.Location)
    $t = $a.GetExportedTypes() | Where-Object { $_.Name -like '*PatternInterface*' -or $_.Name -eq 'AutomationElement' }
    foreach ($x in $t) { Write-Output ('   ' + $x.FullName) }
}
