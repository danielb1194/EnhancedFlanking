param(
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net6.0",
    [string]$OutputDir = "output",
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"
$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

$ProjectFile = Join-Path $ProjectRoot "EnhancedFlanking.csproj"
$ManifestFile = Join-Path $ProjectRoot "modpack.json"
$BuildScript = Join-Path $ProjectRoot "build.ps1"

if (-not (Test-Path $BuildScript)) {
    throw "Build script not found: $BuildScript"
}
if (-not (Test-Path $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}
if (-not (Test-Path $ManifestFile)) {
    throw "Manifest file not found: $ManifestFile"
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    [xml]$csproj = Get-Content $ProjectFile -Raw
    foreach ($pg in $csproj.Project.PropertyGroup) {
        if ($pg.GameDir) {
            $GameDir = [string]$pg.GameDir.'#text'
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($GameDir)) {
    throw "GameDir was not provided and could not be read from EnhancedFlanking.csproj"
}

if (-not (Test-Path $GameDir)) {
    throw "GameDir does not exist: $GameDir"
}

$manifest = Get-Content $ManifestFile -Raw | ConvertFrom-Json
$modName = [string]$manifest.name
if ([string]::IsNullOrWhiteSpace($modName)) {
    throw "modpack.json is missing 'name'"
}

$stagingRoot = Join-Path (Join-Path $ProjectRoot $OutputDir) ("{0}-modpack" -f $modName)
$modsDir = Join-Path $GameDir "Mods"
$destinationFolder = Join-Path $modsDir ("{0}-modpack" -f $modName)

Write-Host "Running build script..."
& $BuildScript -Configuration $Configuration -TargetFramework $TargetFramework -OutputDir $OutputDir
if ($LASTEXITCODE -ne 0) {
    throw "build.ps1 failed"
}

if (-not (Test-Path $stagingRoot)) {
    throw "Staging folder not found after build: $stagingRoot"
}

New-Item -ItemType Directory -Path $modsDir -Force | Out-Null

if (Test-Path $destinationFolder) {
    Write-Host "Removing existing deployed folder: $destinationFolder"
    Remove-Item $destinationFolder -Recurse -Force
}

Write-Host "Copying staging folder to game Mods..."
Copy-Item -Path $stagingRoot -Destination $destinationFolder -Recurse -Force

Write-Host "Done."
Write-Host "Source:      $stagingRoot"
Write-Host "Destination: $destinationFolder"
