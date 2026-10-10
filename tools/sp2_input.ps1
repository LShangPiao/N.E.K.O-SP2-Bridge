param(
    [int]$X, [int]$Y,
    [string]$Key = "",
    [int]$DelayMs = 400
)
$ErrorActionPreference = "Stop"
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Inp {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
  public const uint LD = 0x0002; public const uint LU = 0x0004; public const uint KU = 0x0002;
}
"@
$p = Get-Process -Name "SimplePlanes 2" -ErrorAction Stop | Select-Object -First 1
$h = $p.MainWindowHandle
[Inp]::ShowWindow($h, 9) | Out-Null
try { (New-Object -ComObject wscript.shell).AppActivate($p.Id) | Out-Null } catch {}
[Inp]::SetForegroundWindow($h) | Out-Null
Start-Sleep -Milliseconds 700

if ($Key -ne "") {
  $map = @{ "ENTER"=0x0D; "ESC"=0x1B; "SPACE"=0x20; "TAB"=0x09; "UP"=0x26; "DOWN"=0x28; "LEFT"=0x25; "RIGHT"=0x27 }
  $vk = $map[$Key.ToUpper()]
  if (-not $vk) { throw "unknown key $Key" }
  [Inp]::keybd_event([byte]$vk, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 80
  [Inp]::keybd_event([byte]$vk, 0, [Inp]::KU, [IntPtr]::Zero)
  Write-Host "key: $Key"
} else {
  [Inp]::SetCursorPos($X, $Y) | Out-Null
  Start-Sleep -Milliseconds 250
  [Inp]::mouse_event([Inp]::LD, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 70
  [Inp]::mouse_event([Inp]::LU, 0, 0, 0, [IntPtr]::Zero)
  Write-Host "click: $X,$Y"
}
Start-Sleep -Milliseconds $DelayMs
