# 4. Build logic lives in Cake Frosting; workflows only call it

- Status: accepted
- Date: 2026-10-28

## Context

Build pipelines written in YAML grow into programs nobody can run locally: shell snippets, conditions in
expressions, copy-paste between workflows. When a step fails in CI, reproducing it means pushing again.

## Decision

- Every step of the pipeline is a Cake Frosting task in `build/`, a C# project: `Restore`, `Format-Check`,
  `Build`, `Test` (with the coverage gate), `Docker-Build`, `Scan`, `Sbom`, `Helm-Lint`,
  `Observability-Check`, `Backup-Test`, `Package`, `Publish`, and `CI`, which chains them.
- GitHub Actions workflows prepare the runner (SDK, tools, credentials) and call
  `dotnet run --project build -- --target <Task>`. They contain no build logic.
- `devctl up` calls the same `Docker-Build` task, so the local cluster runs images built exactly like CI's.

## Consequences

- `dotnet run --project build -- --target CI` reproduces the pipeline on a laptop, minus publishing.
- Build code gets the same tooling as application code: types, refactoring, analyzers, a debugger.
- Cake adds a dependency, and contributors need to read C# to change the build. On a .NET platform, that is
  the audience.
- Workflow-level concerns (permissions, concurrency, caching, matrices, publishing results to the pull
  request) stay in YAML, where GitHub expects them.

## Alternatives considered

- **Plain workflow YAML with scripts.** Fastest to start, and what most repositories do. It does not run
  locally and duplicates steps across workflows.
- **Nuke.** Equivalent to Cake Frosting. Cake was chosen for its wider set of tool aliases.
- **Make or just.** Language-agnostic and simple, but the logic falls back to shell, which is what this
  decision avoids.
- **Dagger.** Runs the same pipeline in containers everywhere, a strong property. It adds a container engine
  and an SDK to every build, which is heavy for a .NET repository.
