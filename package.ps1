param(
    [string]$OutputDir = "output"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

$ManifestFile = Join-Path $ProjectRoot "modpack.json"
$SourceFile = Join-Path $ProjectRoot "EnhancedFlanking.cs"
$ProjectFile = Join-Path $ProjectRoot "EnhancedFlanking.csproj"
$AssetsDir = Join-Path $ProjectRoot "assets"

if (-not (Test-Path $ManifestFile)) {
    throw "Required file missing: $ManifestFile"
}
if (-not (Test-Path $SourceFile)) {
    throw "Required file missing: $SourceFile"
}
if (-not (Test-Path $ProjectFile)) {
    throw "Required file missing: $ProjectFile"
}

$manifest = Get-Content $ManifestFile -Raw | ConvertFrom-Json
$modName = [string]$manifest.name
$modVersion = [string]$manifest.version

if ([string]::IsNullOrWhiteSpace($modName)) {
    $modName = "EnhancedFlanking"
}
if ([string]::IsNullOrWhiteSpace($modVersion)) {
    $modVersion = "0.0.0"
}

$outputRoot = Join-Path $ProjectRoot $OutputDir
$stagingRoot = Join-Path $outputRoot "package-staging"
$stagingSrc = Join-Path $stagingRoot "src"
$stagingAssets = Join-Path $stagingRoot "assets"
$zipPath = Join-Path $outputRoot ("{0}-source-{1}.zip" -f $modName, $modVersion)

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Path $stagingSrc -Force | Out-Null
New-Item -ItemType Directory -Path $stagingAssets -Force | Out-Null

Copy-Item $ManifestFile (Join-Path $stagingRoot "modpack.json") -Force
Copy-Item $SourceFile (Join-Path $stagingSrc "EnhancedFlanking.cs") -Force
Copy-Item $ProjectFile (Join-Path $stagingSrc "EnhancedFlanking.csproj") -Force

if (Test-Path $AssetsDir) {
    Copy-Item -Path (Join-Path $AssetsDir "*") -Destination $stagingAssets -Recurse -Force
}

Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $zipPath -Force

Write-Host "Created package: $zipPath"
Write-Host "Contents:"
Write-Host "  modpack.json"
Write-Host "  src/EnhancedFlanking.cs"
Write-Host "  src/EnhancedFlanking.csproj"
Write-Host "  assets/*"
