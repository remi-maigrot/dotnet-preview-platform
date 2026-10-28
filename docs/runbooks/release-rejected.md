# A release was rejected

A tag was pushed and the Release workflow stopped at a gate. Nothing was published: the candidate's images
exist under their commit SHA only, no version tag, no GitHub Release. Fixing means a new commit and a new
tag. Never move an existing tag.

Find the red job in the run graph. Its name says which gate failed.

| Gate | Look at | Usual fix |
|------|---------|-----------|
| 1. Tests | The job log; the coverage summary | Fix the test or the code. A coverage drop below 80 % on the domain or application needs tests, not a lower threshold |
| 2. Architecture | The failing rule names the forbidden dependency | Move the code to the right layer |
| 3. Migrations | Which step failed: empty database, or upgrade from the previous release | See [failed-migration.md](failed-migration.md). An upgrade failure means the migration breaks existing data |
| 4. Vulnerabilities | The Trivy table: package, fixed version | Bump the package or the base image digest. If no fix exists yet, the CVE does not block: it is only reported |
| 5. SBOM | The job log | Usually a tool or network error: re-run the job |
| 6. Signature | The cosign output | Sigstore or GHCR unavailable: re-run. An identity mismatch means the workflow file was renamed or the tag is not `v*.*.*` |
| 7. Smoke and load | The smoke test report, then the k6 summary (p95, error rate) | Reproduce on a preview: `devctl preview create --pr <n> --image <candidate image>` and `devctl preview diagnose` |

Re-running a job is fine for infrastructure failures (registry, Sigstore, a runner lost). Re-running a real
failure until it passes is not. A flaky gate is a bug in the gate: fix it the same day.

Then:

```sh
git tag -d vX.Y.Z                    # only the local tag; the remote one stays, as the record of the attempt
git commit ...                       # the fix
git tag vX.Y.(Z+1) && git push origin vX.Y.(Z+1)
```
