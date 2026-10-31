param([string]$Exe,[string]$OutDir,[string]$pages)
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class W {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h,int n);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int d,bool r);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out RECT r);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h,out uint pid);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb,IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
  [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
  [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h,IntPtr dc);
  [DllImport("user32.dll",SetLastError=true)] public static extern IntPtr GetDesktopWindow();
  public delegate bool EnumProc(IntPtr h,IntPtr l);
  public static IntPtr FindByPid(uint target){
    IntPtr found=IntPtr.Zero;
    EnumWindows((h,l)=>{uint pid;GetWindowThreadProcessId(h,out pid);if(pid==target){found=h;return false;}return true;},IntPtr.Zero);
    return found;
  }
  [StructLayout(LayoutKind.Sequential)] public struct RECT{public int Left,Top,Right,Bottom;}
}
'@
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$dcZero = [W]::GetDC([IntPtr]::Zero)
Write-Host "screenDC=$dcZero"
$p = Start-Process $Exe -PassThru
Write-Host "PID=$($p.Id)"
$d=(Get-Date).AddSeconds(120);$h=[IntPtr]::Zero
while((Get-Date) -lt $d){Start-Sleep -Milliseconds 700;if($p.HasExited){Write-Host "EXITED $($p.ExitCode)";exit 1};$h=[W]::FindByPid([uint32]$p.Id);if($h -ne [IntPtr]::Zero){break}}
if($h -eq [IntPtr]::Zero){Write-Host "NO WINDOW";exit 2}
Write-Host "HWND=$h"
$shot = {
  param($name)
  Start-Sleep -Milliseconds 1000
  $r=New-Object W+RECT;[void][W]::GetWindowRect($h,[ref]$r)
  $w=$r.Right-$r.Left; $bt=$r.Bottom-$r.Top
  $bmp=New-Object System.Drawing.Bitmap $w,$bt
  $g=[System.Drawing.Graphics]::FromImage($bmp)
  $ptr=$g.GetHdc()
  $ok=[W]::PrintWindow($h,$ptr,2)
  $g.ReleaseHdc($ptr)
  $bmp.Save("$OutDir\$name.png")
  $g.Dispose();$bmp.Dispose()
  Write-Host "saved $name ${w}x${bt} printWindow=$ok"
}
[void][W]::ShowWindow($h,3);[void][W]::SetForegroundWindow($h);Start-Sleep -Seconds 12
& $shot "01-max"
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Windows.Automation;
public static class Nav {
  public static bool ClickButton(string title){
    var root=AutomationElement.RootElement;
    var c=new AndCondition(
      new PropertyCondition(AutomationElement.NameProperty,title),
      new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
    var el=root.FindFirst(TreeScope.Descendants,c);
    if(el==null) return false;
    try {
      var pat=el.GetPattern(PatternInterface.Invoke) as IInvokeProvider;
      if(pat!=null){ pat.Invoke(); return true; }
    } catch (Exception) { }
    return false;
  }
}
'@
$pageItems = [System.IO.File]::ReadAllLines((Join-Path $OutDir 'titles.txt'), [System.Text.Encoding]::UTF8)
foreach ($lbl in $pageItems) {
  if ([string]::IsNullOrWhiteSpace($lbl)) { continue }
  [void][W]::SetForegroundWindow($h); Start-Sleep -Milliseconds 400
  $clicked = [Nav]::ClickButton($lbl)
  Start-Sleep -Seconds 5
  $tag = ($lbl -replace '[^A-Za-z0-9]+','-')
  & $shot "02-$tag"
  Write-Host ("nav [" + $lbl + "] -> " + $clicked)
}
Stop-Process -Id $p.Id -Force
Write-Host "done"
