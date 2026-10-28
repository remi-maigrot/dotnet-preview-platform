# Release qualification

A release starts when someone pushes a tag such as `v1.4.0`. The tag only marks a release candidate.
`.github/workflows/release.yml` decides whether the candidate is released. Its jobs are chained with
`needs`, so the first red gate stops the run and the run graph shows which gate failed.

Nothing is published under a version number until every gate has passed. The candidate's images
exist only under their commit SHA tag until the last job adds the version tag.

## The gates

| # | Gate | What fails it | Why it is a gate |
|---|------|---------------|------------------|
| 1 | Unit and integration tests | any failing test, or line coverage of `Sample.Domain` and `Sample.Application` under 80 % | The same Cake `Test` target as CI, rerun on the tagged commit itself. |
| 2 | Architecture rules | a forbidden dependency between layers | Layering erodes one shortcut at a time and nobody notices in review. |
| 3 | Migrations | a migration that fails on an empty database, or one that fails on top of the previous release's schema and data | The empty-database run is what a new preview does. The upgrade run is what production does. |
| 4 | Vulnerabilities | a fixable CRITICAL CVE in any image, on amd64 or arm64; a HIGH or CRITICAL misconfiguration in `deploy/` | Images are scanned as pushed, by digest, not as built locally. |
| 5 | SBOM | SBOM generation fails | Each image gets a CycloneDX SBOM of its contents, and the API gets one of its NuGet graph too. They are attached to the release. |
| 6 | Signature and provenance | signing, attestation or verification fails | Every image is signed with cosign keyless and carries its SBOM as an attestation. The API image also gets SLSA build provenance. The job verifies its own signatures before it finishes. |
| 7 | Smoke and load tests | a smoke test fails; k6 p95 at or above 300 ms, or more than 1 % errors | The candidate is deployed by the preview operator, with seed data, exactly like a pull request preview. Unsigned images are refused before deployment. |

Then the `publish` job:

1. adds the version tag to the qualified images with `docker buildx imagetools create`, which copies no
   layers and keeps the digests that were tested and signed;
2. pushes the Helm chart to `oci://ghcr.io/<owner>/charts` and packs the `devctl` tool;
3. writes the changelog from Conventional Commits with git-cliff (`cliff.toml`);
4. creates the GitHub Release with the changelog, the SBOMs, the image digests, the chart and the `devctl`
   package.

## The migration gate in detail

The previous release is the closest `v*.*.*` tag before the current one. The job checks it out in a
`git worktree`, runs that version's migrator against a fresh database, then that version's seeder (when
the release had one), so the schema holds realistic rows. It then runs the current migrator on top.

This catches the migrations that pass on an empty table and break on real data: a new `NOT NULL` column
without a default, a unique index over duplicated values, a narrowed type. On the very first release there
is no previous tag, and only the empty-database run happens.

The gate does not test a rollback of the schema. Migrations are written to be backward compatible with the
previous release (expand, then contract in a later release), and the procedure is in
[runbooks/failed-migration.md](runbooks/failed-migration.md).

## Verifying a release image yourself

```sh
cosign verify ghcr.io/<owner>/sample-api:1.4.0 \
  --certificate-identity-regexp '^https://github.com/<owner>/dotnet-preview-platform/.github/workflows/release.yml@refs/tags/v' \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com

gh attestation verify oci://ghcr.io/<owner>/sample-api:1.4.0 --owner <owner>
```

The first command proves that the image was signed by this repository's release workflow, from a tag.
A developer pushing from a laptop cannot produce such a signature. The second checks the build provenance.

## Retention

`.github/workflows/cleanup.yml` runs every Monday. It deletes image versions that carry only a commit SHA tag
and are older than 14 days, along with their signatures, attestations and platform manifests. Anything
reachable from a release tag is kept. A manual run is a dry run by default and writes what it would delete
to the job summary.

## Known limits

- The preview in gate 7 runs on a kind cluster created inside the job, not on a shared cluster. The
  operator, the chart and the images are the real ones. The network, the storage class and the node sizes
  are not.
- The load test is a smoke-sized load (10 virtual users, 1 minute). It catches a large regression, such as
  an N+1 query or a missing index on a hot path. It is not a capacity test.
- Gates 4 and 6 depend on GHCR, Sigstore and the Trivy database being reachable. When one is down, the
  release waits. That is the intended behaviour, not an incident.
