param(
    [string]$Configuration = "Release",
    [string]$TargetFramework = "net6.0",
    [string]$OutputDir = "output"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }

$ProjectFile = Join-Path $ProjectRoot "EnhancedFlanking.csproj"
$ManifestFile = Join-Path $ProjectRoot "modpack.json"

if (-not (Test-Path $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}
if (-not (Test-Path $ManifestFile)) {
    throw "Manifest file not found: $ManifestFile"
}

$manifest = Get-Content $ManifestFile -Raw | ConvertFrom-Json
$modName = [string]$manifest.name
$modVersion = [string]$manifest.version

if ([string]::IsNullOrWhiteSpace($modName)) {
    throw "modpack.json is missing 'name'"
}
if ([string]::IsNullOrWhiteSpace($modVersion)) {
    throw "modpack.json is missing 'version'"
}

# Prefer explicit AssemblyName from csproj; fall back to project filename.
[xml]$csproj = Get-Content $ProjectFile -Raw
$assemblyName = $null
foreach ($pg in $csproj.Project.PropertyGroup) {
    if ($pg.AssemblyName) {
        $assemblyName = [string]$pg.AssemblyName
        break
    }
}
if ([string]::IsNullOrWhiteSpace($assemblyName)) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectFile)
}

$builtDll = Join-Path $ProjectRoot ("bin/{0}/{1}/{2}.dll" -f $Configuration, $TargetFramework, $assemblyName)

$outputRoot = Join-Path $ProjectRoot $OutputDir
$modpackFolderName = "$modName-modpack"
$stagingRoot = Join-Path $outputRoot $modpackFolderName
$dllsDir = Join-Path $stagingRoot "dlls"
$zipPath = Join-Path $outputRoot ("{0}-{1}.zip" -f $modpackFolderName, $modVersion)

Write-Host "Building $assemblyName ($Configuration/$TargetFramework)..."
dotnet build $ProjectFile -c $Configuration -f $TargetFramework
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed"
}

if (-not (Test-Path $builtDll)) {
    throw "Built DLL not found: $builtDll"
}

Write-Host "Preparing package staging..."
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (Test-Path $stagingRoot) {
    Remove-Item $stagingRoot -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

New-Item -ItemType Directory -Path $dllsDir -Force | Out-Null
Copy-Item $ManifestFile (Join-Path $stagingRoot "modpack.json") -Force
Copy-Item $builtDll (Join-Path $dllsDir ("{0}.dll" -f $assemblyName)) -Force

Write-Host "Creating zip package..."
Compress-Archive -Path $stagingRoot -DestinationPath $zipPath -Force

Write-Host "Done."
Write-Host "Staging folder: $stagingRoot"
Write-Host "Zip package:    $zipPath"
