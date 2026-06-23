# OpenAdoration — API.Bible Source plugin

A **bring-your-own-key** Bible source for [OpenAdoration](https://github.com/g0elles/OpenAdoration).
The church supplies its own [API.Bible](https://scripture.api.bible) key; this plugin fetches the
versions that key can access and imports them into OpenAdoration's Bible library.

It ships as a separate plugin **on purpose**: API.Bible's key, terms, and FUMS telemetry stay out of
the MIT core. OpenAdoration core embeds no key and no copyrighted scripture — sourcing a licensed
version is the church's responsibility, under its own account and terms.

## Install

1. Download `apibible.oaplugin` from this repo's Releases.
2. In OpenAdoration: **Settings → Plugins → Add…**, pick the file.
3. Select the plugin, paste your API.Bible key into **API.Bible key**, **Save settings**.
4. **Fetch versions**, tick the ones you want, **Import selected**.

## Build

```powershell
pwsh build.ps1            # -> dist/apibible.oaplugin
```

The package contains only `manifest.json` + `OpenAdoration.Plugin.ApiBible.dll`. The contract
assembly (`OpenAdoration.Plugins.Abstractions`) is **not** bundled — the host shares it from its own
load context so plugin/host types match. The build references a checked-in copy in `lib/` (no NuGet).

## Status

- ✅ Phase B — repo scaffold: project, manifest, contract reference, packaging.
- ⏳ Phase C — implement `GetAvailableVersionsAsync` / `FetchAsync` against scripture.api.bible
  (`/bibles`, `/books`, `/chapters`, per-chapter text), report progress, honor FUMS.

## License

MIT (this connector's own code). Scripture text fetched at runtime is governed by API.Bible's terms
and the church's account — not redistributed by this plugin.
