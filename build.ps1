$ErrorActionPreference = 'Stop'
Get-Process -Name 'Aion2PetsOverlay' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 300

$wpfDir = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$args = @(
    '/nologo',
    '/target:winexe',
    '/codepage:65001',
    '/utf8output',
    '/out:Aion2PetsOverlay.exe',
    "/r:$wpfDir\PresentationFramework.dll",
    "/r:$wpfDir\PresentationCore.dll",
    "/r:$wpfDir\WindowsBase.dll",
    '/r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll',
    '/r:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll',
    'Overlay.cs'
)

Write-Host "Compiling Overlay.cs..." -ForegroundColor Cyan
& $csc $args
if ($LASTEXITCODE -eq 0) {
    Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
    Get-Item 'Aion2PetsOverlay.exe' | Select-Object Name, Length, LastWriteTime
} else {
    Write-Host "BUILD FAILED WITH CODE $LASTEXITCODE" -ForegroundColor Red
}
