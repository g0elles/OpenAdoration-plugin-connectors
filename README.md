# OpenAdoration plugins

[![ci](https://github.com/g0elles/OpenAdoration-plugin-connectors/actions/workflows/ci.yml/badge.svg)](https://github.com/g0elles/OpenAdoration-plugin-connectors/actions/workflows/ci.yml)

Bible-source plugins for [OpenAdoration](https://github.com/g0elles/OpenAdoration). Each is a separate
project here but shares the `OpenAdoration.Plugins.Abstractions` contract; the core app stays MIT and
ships no keys, telemetry, or copyrighted text.

| Plugin | id | Source | Key? | Notes |
|---|---|---|---|---|
| **Free Use Bible** | `helloao` | [helloao.org](https://bible.helloao.org) | No | 1,000+ free / public-domain translations (incl. Spanish RVR1909). No limits; a whole Bible in one request. **Default free source.** |
| **API.Bible** | `apibible` | [scripture.api.bible](https://scripture.api.bible) | Yes (BYO) | For the church's *licensed* versions (NVI, etc.) under its own key. Free tier is 5,000 calls/month. |

## Install (per plugin)

1. Download the plugin's `.oaplugin` from this repo's Releases.
2. In OpenAdoration: **Settings → Plugins → Add…**, pick the file.
3. Select it, fill any settings (e.g. API.Bible needs a key — helloao needs nothing), **Fetch versions**,
   tick what you want, **Import selected**.

## Build

```powershell
pwsh build.ps1            # -> dist/<id>.oaplugin for every plugin under src/
```

Each package contains only `manifest.json` + the plugin DLL — the host shares the contract assembly
from its own load context. The build references a checked-in `lib/OpenAdoration.Plugins.Abstractions.dll`
(no NuGet).

## Tests

```powershell
dotnet test                            # offline unit tests (parsers)
$env:APIBIBLE_KEY="<key>"; dotnet test # also runs API.Bible live checks (helloao live needs no key)
```

## Repo layout

```
src/   OpenAdoration.Plugin.ApiBible      OpenAdoration.Plugin.HelloAo
tests/ ...ApiBible.Tests                  ...HelloAo.Tests
lib/   OpenAdoration.Plugins.Abstractions.dll   (shared contract, checked in)
```

## Releases

Per-plugin tags: `<id>-v<version>` (e.g. `helloao-v0.1.0`, `apibible-v0.1.0`). Pushing the tag builds
and publishes a GitHub release with that plugin's `.oaplugin` attached.

## License

MIT. Scripture text is fetched at runtime from the source above under its own terms; this repo
redistributes none.
