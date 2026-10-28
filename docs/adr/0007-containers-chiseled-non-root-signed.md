# 7. Images: chiseled, non-root, tagged by commit, signed

- Status: accepted
- Date: 2026-10-28

## Context

Images are what actually runs. Their contents decide the attack surface and the CVE count, and their
provenance decides whether anyone can tell what is deployed.

## Decision

- Multi-stage builds: the SDK image compiles and publishes, and the runtime image is Ubuntu chiseled
  (`aspnet` or `runtime` `-noble-chiseled`), with no shell and no package manager. Base images are pinned by
  digest and updated by Dependabot.
- Containers run as a non-root user (1654) with a read-only root filesystem, no privilege escalation, every
  capability dropped and the runtime default seccomp profile. Namespaces enforce Pod Security `restricted`,
  so a chart regression is refused by the cluster.
- Images are tagged with the commit SHA. A version tag is added only after release qualification, to the same
  digest.
- Release images are scanned (no fixable critical CVE), get a CycloneDX SBOM, and are signed with cosign
  keyless along with an SBOM attestation and SLSA build provenance.

## Consequences

- No shell in the API container: `kubectl exec` for debugging does not work. Ephemeral debug containers
  (`kubectl debug`) are the tool instead.
- A tag never changes meaning, so a rollback is just the previous SHA.
- Signing needs OIDC from the CI and Sigstore to be reachable. When it is not, the release waits.

## Alternatives considered

- **Alpine runtime images.** Small, but musl, a shell and a package manager. Chiseled images are smaller in
  attack surface for the same size.
- **Distroless (gcr.io/distroless).** Equivalent idea. Chiseled images are maintained with the .NET images
  themselves and follow their patch cadence.
- **Signing with a stored key.** Works offline, but the key must be stored, rotated and protected. Keyless
  signing ties the signature to the workflow identity instead.
