param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
dotnet build $root -c $Configuration --nologo

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

# Package every plugin under src/ (one .oaplugin each, named by its manifest id). A .oaplugin is
# a zip of manifest.json + the plugin DLL only — the host supplies the shared contract + logging.
Get-ChildItem (Join-Path $root "src") -Directory | ForEach-Object {
    $manifestPath = Join-Path $_.FullName "manifest.json"
    if (-not (Test-Path $manifestPath)) { return }

    $id  = (Get-Content $manifestPath -Raw | ConvertFrom-Json).id
    $out = Join-Path $_.FullName "bin/$Configuration/net10.0"
    $dll = Join-Path $out "$($_.Name).dll"
    $pkg = Join-Path $dist "$id.oaplugin"

    if (Test-Path $pkg) { Remove-Item $pkg }
    Compress-Archive -Path (Join-Path $out "manifest.json"), $dll -DestinationPath $pkg
    Write-Host "Built $pkg"
}
