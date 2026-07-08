param(
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net6.0",
    [string]$OutputDir = "output",
    [string]$GameDir = "",
    [string]$GameExe = "C:\Program Files (x86)\Steam\steamapps\common\Menace\Menace.exe"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$DeployScript = Join-Path $ProjectRoot "deploy.ps1"

if (-not (Test-Path $DeployScript)) {
    throw "Deploy script not found: $DeployScript"
}

if (-not (Test-Path $GameExe)) {
    throw "Game executable not found: $GameExe"
}

Write-Host "Running deploy script..."
& $DeployScript -Configuration $Configuration -TargetFramework $TargetFramework -OutputDir $OutputDir -GameDir $GameDir
if ($LASTEXITCODE -ne 0) {
    throw "deploy.ps1 failed"
}

$gameWorkingDir = Split-Path -Parent $GameExe
Write-Host "Starting game: $GameExe"
Start-Process -FilePath $GameExe -WorkingDirectory $gameWorkingDir

Write-Host "Done."
