# Changelog

All notable changes to the OpenAdoration connector plugins are documented here.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
No versioned release has been cut yet — everything below is unreleased.

## [Unreleased]

### apibible (API.Bible)
- Implement version listing and whole-version fetch against scripture.api.bible.
- Fetch versions via a **200-verse passage walk** (`books?include-chapters` +
  `/passages`), cutting a full import from ~1,257 to ~160 requests (~8×).
- **FUMS** usage reporting to `fums.api.bible/f3` (separate host — no quota cost)
  per the API.Bible fair-use terms.
- Configurable `baseUrl` / `fumsBaseUrl`; 429 retry; import progress.

### helloao (Free Use Bible)
- Add the helloao.org connector — no key, 1,000+ free / public-domain
  translations across many languages.

### Repository
- Multi-plugin monorepo (renamed from `OpenAdoration-apibible`).
- CI (build + offline tests) and release workflow (per-plugin `<id>-v<version>` tags).
