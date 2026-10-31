param([string]$Exe, [string]$OutDir)
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int d,bool r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static IntPtr FindByPid(uint target) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h,l) => { uint pid; GetWindowThreadProcessId(h, out pid); if (pid==target) { found = h; return false; } return true; }, IntPtr.Zero);
    return found;
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$p = Start-Process $Exe -PassThru
Write-Host "PID=$($p.Id)"
$deadline = (Get-Date).AddSeconds(120)
$h = [IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
  Start-Sleep -Milliseconds 800
  if ($p.HasExited) { Write-Host "EXITED code=$($p.ExitCode)"; exit 1 }
  $h = [W]::FindByPid([uint32]$p.Id)
  if ($h -ne [IntPtr]::Zero) { break }
}
if ($h -eq [IntPtr]::Zero) { Write-Host "NO WINDOW"; Stop-Process -Id $p.Id -Force; exit 2 }
Write-Host "HWND=$h"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$shot = {
  param($name)
  Start-Sleep -Milliseconds 1200
  $r = New-Object W+RECT
  [void][W]::GetWindowRect($h, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap ($r.Right-$r.Left), ($r.Bottom-$r.Top)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
  $bmp.Save("$OutDir\$name.png")
  $g.Dispose(); $bmp.Dispose()
  Write-Host "saved $name ($($r.Right-$r.Left)x$($r.Bottom-$r.Top))"
}
[void][W]::ShowWindow($h, 3); [void][W]::SetForegroundWindow($h); Start-Sleep -Seconds 10
& $shot "01-max"

Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$nav = @'
using System.Windows.Automation;
public static class Nav {
  public static bool ClickButton(string title) {
    var root = AutomationElement.RootElement;
    var cond = new AndCondition(
      new PropertyCondition(AutomationElement.NameProperty, title),
      new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
    var el = root.FindFirst(TreeScope.Descendants, cond);
    if (el == null) return false;
    object pat;
    if (el.TryPattern(PatternInterface.Invoke, out pat)) { ((IInvokeProvider)pat).Invoke(); return true; }
    return false;
  }
}
'@
Add-Type -TypeDefinition $nav
$titles = @('Tableau de bord','Comptes utilisateurs ciblés','Gestion des GPO','Paramètres des GPO','Appliquer aux postes','Postes clients & console',"Journal d'audit",'Paramètres & session')
foreach ($t in $titles) {
  [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
  $ok = [Nav]::ClickButton($t)
  Start-Sleep -Seconds 5
  $tag = ($t -replace '[^A-Za-z0-9]+','-')
  & $shot ("02-$tag")
  Write-Host "nav '$t' => $ok"
}
[void][W]::ShowWindow($h, 3)
Stop-Process -Id $p.Id -Force
Write-Host "done"
