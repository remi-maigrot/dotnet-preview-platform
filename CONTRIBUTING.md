# Contributing

## Prerequisites

- .NET SDK 10 (the exact feature band is pinned in `global.json`)
- Docker (Docker Desktop, OrbStack or Colima)
- For the local cluster: `k3d`, `kubectl` and `helm`. Run `devctl doctor` to check versions.

## Build and test

Everything the CI does can be run locally through the Cake build project:

```bash
dotnet run --project build -- --target Test     # restore, build, unit + integration tests, coverage
dotnet run --project build -- --target CI       # the full pipeline, as run by GitHub Actions
```

Integration tests start PostgreSQL with Testcontainers, so Docker must be running.

## Conventions

- **Commits** follow [Conventional Commits](https://www.conventionalcommits.org/). The type drives the release changelog:
  `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `build`, `ci`, `chore`. Breaking changes use `!` (`feat!: ...`).
- **Branches**: `feat/<short-topic>`, `fix/<short-topic>`, `docs/<short-topic>`.
- **Warnings are errors.** Fix the analyzer finding rather than suppressing it. If a suppression is justified, scope it as
  narrowly as possible and write down why next to it.
- **Package versions** live in `Directory.Packages.props` only. After changing one, run `dotnet restore` and commit the
  updated `packages.lock.json` files.
- **Architecture decisions** that are hard to reverse get an ADR in `docs/adr/` (copy the latest one and keep it short).

## Pull requests

Every pull request gets its own preview environment, created by the operator in this repository. The preview workflow
posts the URL and the smoke and load test results as a comment. A pull request is ready to merge when CI is green, the
preview checks pass and the description explains how the change was tested.
