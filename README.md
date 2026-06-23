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

## Configuration

- **API.Bible key** (required) — your key, masked in the UI.
- **API base URL** (advanced, optional) — defaults to `https://rest.api.bible/v1/`. Set this only if
  your account was issued a different host. Must include the version path (`/v1/`).

## Build

```powershell
pwsh build.ps1            # -> dist/apibible.oaplugin
```

## Tests

```powershell
dotnet test                           # parser unit tests (offline)
$env:APIBIBLE_KEY="<your key>"; dotnet test   # also runs the live API checks
```

The live tests hit the real service to confirm the documented endpoints/JSON still match the parser.
They are a no-op without `APIBIBLE_KEY`, so the key never lands in source or a transcript. Override the
host with `APIBIBLE_BASE` if needed.

The package contains only `manifest.json` + `OpenAdoration.Plugin.ApiBible.dll`. The contract
assembly (`OpenAdoration.Plugins.Abstractions`) is **not** bundled — the host shares it from its own
load context so plugin/host types match. The build references a checked-in copy in `lib/` (no NuGet).

## How it fetches

- `GetAvailableVersionsAsync` → `GET /v1/bibles` (the versions your key can access).
- `FetchAsync` → `/bibles/{id}` (metadata) → `/books` → per-book `/chapters` → per-chapter
  `chapters/{id}?content-type=json`. Verses are read from each text node's explicit `attrs.verseId`
  (no fragile in-text delimiter parsing). **Per-chapter** granularity keeps a whole-Bible sync under
  the free-tier daily request cap (~1,250 requests/version). Progress reports verses fetched; the
  request loop retries on HTTP 429 honoring `Retry-After`.
- Book numbering follows the API's canonical order; Old/New Testament is derived from the 27 USFM
  NT codes.

## FUMS (Fair Use Management System)

API.Bible returns a `meta.fumsToken` per request that its terms expect you to report. The plugin
captures the token (logged) so it's never silently dropped. **Full reporting (POSTing the token to
the FUMS endpoint) is account-specific and not yet wired** — see the `ReportFums` TODO. Complete it
once your account confirms the reporting contract.

## Status

- ✅ Phase B — repo scaffold.
- ✅ Phase C — versions + whole-Bible fetch implemented; parser unit-tested. Pending: a live run with
  a real key, and full FUMS reporting.

## License

MIT (this connector's own code). Scripture text fetched at runtime is governed by API.Bible's terms
and the church's account — not redistributed by this plugin.
