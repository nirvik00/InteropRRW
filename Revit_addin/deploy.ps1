<#
.SYNOPSIS
  Dev helper: close Revit 2027 (it locks the add-in DLLs), build + deploy the
  Charis add-in, and optionally relaunch Revit.

.NOTES
  Stop-Process -Force does NOT prompt to save — close Revit yourself if you have
  unsaved work. This is a developer convenience for the tight edit/test loop.

.EXAMPLE
  ./deploy.ps1            # close Revit, build + deploy
  ./deploy.ps1 -Launch    # ...then relaunch Revit 2027
#>
param([switch]$Launch)

$ErrorActionPreference = 'Stop'

$proc = Get-Process Revit -ErrorAction SilentlyContinue
if ($proc) {
    Write-Host "Closing Revit (PID $($proc.Id))..."
    $proc | Stop-Process -Force
    Start-Sleep -Seconds 2
}

$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
& $dotnet build "$PSScriptRoot\CharisRevitConnector.sln" -c Debug --nologo -v minimal

$revit = 'C:\Program Files\Autodesk\Revit 2027\Revit.exe'
if ($Launch -and (Test-Path $revit)) {
    Write-Host 'Relaunching Revit 2027...'
    Start-Process $revit
}
