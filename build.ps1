param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$proj = Join-Path $root "src/OpenAdoration.Plugin.ApiBible"
$out  = Join-Path $proj "bin/$Configuration/net10.0"

dotnet build $proj -c $Configuration --nologo

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$pkg = Join-Path $dist "apibible.oaplugin"
if (Test-Path $pkg) { Remove-Item $pkg }

# A .oaplugin is a zip of manifest.json + the plugin DLL only. The host supplies the shared
# contract (OpenAdoration.Plugins.Abstractions) and logging assemblies from its own load context.
Compress-Archive -Path `
    (Join-Path $out "manifest.json"), `
    (Join-Path $out "OpenAdoration.Plugin.ApiBible.dll") `
    -DestinationPath $pkg

Write-Host "Built $pkg"
