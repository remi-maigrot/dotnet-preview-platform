# Build

The whole pipeline is a C# console app built on [Cake Frosting](https://cakebuild.net/docs/running-builds/runners/cake-frosting).
GitHub Actions only installs tools and calls it, so a red CI run can always be reproduced locally with the same command.

```bash
dotnet run --project build -- --target <Task> [options]
```

| Task | What it does |
|---|---|
| `Clean` | Deletes `artifacts/` and the build outputs |
| `Restore` | Restores local tools and NuGet packages (locked mode in CI) |
| `Format-Check` | `dotnet format --verify-no-changes` |
| `Build` | Compiles the solution; any warning is an error |
| `Test` | Runs every test project with Coverlet, merges coverage with ReportGenerator, fails under 80 % line coverage on `Sample.Domain` and `Sample.Application` |
| `Coverage-Badge` | Writes the shields.io endpoint file published by CI |
| `Docker-Build` | Builds the images, tagged with the commit SHA |
| `Scan` | Trivy: fixable critical CVEs in images, high/critical misconfigurations in `deploy/` |
| `Sbom` | CycloneDX SBOMs of the NuGet graph and of each image |
| `Helm-Lint` | `helm lint --strict`, then `helm template` with each `ci/*.yaml` file, validated by kubeconform |
| `Package` | Packs `devctl` as a dotnet tool and the Helm chart |
| `Publish` | Pushes the chart to the OCI registry (CI only, with `--push=true`) |
| `CI` | Everything above, in dependency order |

Options: `--configuration` (default `Release`), `--version`, `--image-tag` (default: commit SHA), `--registry`
(default `ghcr.io/remi-maigrot`), `--push`, `--platforms linux/amd64,linux/arm64`, `--skip-integration-tests`,
`--coverage-threshold`.

`Scan` and `Helm-Lint` need `trivy`, `helm` and `kubeconform` on the `PATH`; `devctl doctor` lists them.
