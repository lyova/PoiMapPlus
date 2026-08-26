# Builds the mod and packs a distributable archive into dist\
param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"

$modDir = $PSScriptRoot
$name = Split-Path $modDir -Leaf
$project = Join-Path $modDir "src\$name.csproj"
$pack = Join-Path $modDir "pack"
$modInfo = Join-Path $pack "ModInfo.xml"

$version = ([xml](Get-Content $modInfo)).xml.Version.value
Write-Host "Packing $name $version"

dotnet build $project -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dist = Join-Path $modDir "dist"
$staging = Join-Path $dist "staging"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $staging $name) | Out-Null

# One folder at the archive root: that is what Vortex and the Mod Launcher expect
Copy-Item -Path (Join-Path $pack "*") -Destination (Join-Path $staging $name) -Recurse
foreach ($doc in @("README.md", "CHANGELOG.md", "LICENSE")) {
    $path = Join-Path $modDir $doc
    if (Test-Path $path) { Copy-Item $path (Join-Path $staging $name) }
}

$zip = Join-Path $dist "$name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging $name) -DestinationPath $zip

Remove-Item $staging -Recurse -Force
Write-Host "Done: $zip"
