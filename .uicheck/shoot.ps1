param([string]$Exe,[string]$OutDir)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,UIAutomationProvider
Add-Type -Path .uicheck\navsrc.cs -ReferencedAssemblies `
 ([Reflection.Assembly]::LoadWithPartialName('UIAutomationClient')).Location,
 ([Reflection.Assembly]::LoadWithPartialName('UIAutomationTypes')).Location,
 ([Reflection.Assembly]::LoadWithPartialName('UIAutomationProvider')).Location,
 ([Reflection.Assembly]::LoadWithPartialName('WindowsBase')).Location
Write-Output 'nav-helper loaded'

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
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint f);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 public delegate bool EnumProc(IntPtr h,IntPtr l);
 public static IntPtr ByPid(uint t){
  IntPtr f=IntPtr.Zero;
  EnumWindows((h,l)=>{uint p;GetWindowThreadProcessId(h,out p);if(p==t){f=h;return false;}return true;},IntPtr.Zero);
  return f;
 }
 [StructLayout(LayoutKind.Sequential)] public struct RECT{public int Left,Top,Right,Bottom;}
}
'@

$pageFile = Join-Path $OutDir 'titles.txt'
$p = Start-Process $Exe -PassThru
Write-Output ("pid=" + $p.Id)
$end=(Get-Date).AddSeconds(120)
$h=[IntPtr]::Zero
while((Get-Date) -lt $end){
 Start-Sleep -Milliseconds 700
 if($p.HasExited){ Write-Output ('app exited ' + $p.ExitCode); exit 1 }
 $h=[W]::ByPid([uint32]$p.Id)
 if($h -ne [IntPtr]::Zero){ break }
}
if($h -eq [IntPtr]::Zero){ Write-Output 'no window'; exit 2 }

function Save([string]$name,[IntPtr]$hwnd){
 Start-Sleep -Milliseconds 900
 $r=New-Object W+RECT
 [void][W]::GetWindowRect($hwnd,[ref]$r)
 $cx=$r.Right-$r.Left
 $cy=$r.Bottom-$r.Top
 $bmp=New-Object System.Drawing.Bitmap $cx,$cy
 $g=[System.Drawing.Graphics]::FromImage($bmp)
 $dc=$g.GetHdc()
 [void][W]::PrintWindow($hwnd,$dc,2)
 $g.ReleaseHdc($dc)
 $bmp.Save((Join-Path $OutDir ($name + '.png')))
 $g.Dispose(); $bmp.Dispose()
 Write-Output ("shot " + $name + " " + $cx + "x" + $cy)
}

[void][W]::ShowWindow($h,3)
[void][W]::SetForegroundWindow($h)
Start-Sleep -Seconds 14
Save '01-open' $h
Write-Output ('buttons:' + [Nav]::Dump())

$items=[System.IO.File]::ReadAllLines($pageFile,[System.Text.Encoding]::UTF8)
$i=0
foreach($lbl in $items){
 $i=$i+1
 if([string]::IsNullOrWhiteSpace($lbl)){ continue }
 [void][W]::SetForegroundWindow($h)
 $ok=[Nav]::Focus($lbl)
 $sp=[IntPtr]0x20
 [void][W]::PostMessage($h,0x0100,$sp,[IntPtr]::Zero)
 [void][W]::PostMessage($h,0x0101,$sp,[IntPtr]::Zero)
 Start-Sleep -Seconds 5
 $tag=$lbl -replace '[^A-Za-z0-9]',''
 Write-Output ("nav " + $i + " focus=" + $ok)
 Save ('02-' + $i) $h
}
Stop-Process -Id $p.Id -Force
Write-Output 'finished'
