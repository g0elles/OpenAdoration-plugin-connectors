# Contributing

Thanks for helping improve the OpenAdoration connector plugins.

## Build & test

```pwsh
dotnet build                 # build all plugins + tests
dotnet test                  # unit tests (offline; live tests no-op without a key)
pwsh build.ps1               # package each plugin into dist/<id>.oaplugin
```

Live tests hit the real APIs and are gated on environment variables (e.g.
`APIBIBLE_KEY`); they no-op without them. **Never commit an API key.**

## Branch flow

`master` is protected (requires the `build-test` check + a PR). Work on `dev` or
a feature branch and open a PR into `master`. Keep PRs focused; squash-merge.

## Adding a new plugin

1. Add `src/OpenAdoration.Plugin.<Name>/` implementing a capability interface
   (e.g. `IBibleSourcePlugin`) from the checked-in
   `lib/OpenAdoration.Plugins.Abstractions.dll` (referenced with `Private=false`).
2. Add a `manifest.json` (id, name, capability, settings) copied to output.
3. Add a matching `tests/OpenAdoration.Plugin.<Name>.Tests/` project.
4. `build.ps1` packages any `src/*` dir that has a `manifest.json` automatically.

## Conventions

- net10.0, nullable enabled, `.editorconfig` enforced (LF, 4-space C#).
- Conventional-commit messages (`feat:`, `fix:`, `chore:` …).
- Pure parsing/logic should be unit-tested against canned payloads.
