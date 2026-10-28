# Security

What the platform does about each way in, and what it leaves to whoever runs it for real. Data protection
(personal data, backups, retention) has its own page: [data-protection.md](data-protection.md).

## Supply chain

The code that ends up running comes from three places: this repository, its dependencies, and the CI that
builds it. Each one is pinned or checked.

| Risk | Control |
|------|---------|
| A dependency is swapped or yanked | `packages.lock.json` for every project, restored in locked mode in CI; versions in `Directory.Packages.props` only, transitive ones pinned |
| A dependency has a known vulnerability | NuGet audit on every restore (warnings are errors, so a vulnerable package fails the build); Trivy on every image; Dependabot |
| A fresh release of a dependency is malicious | Dependabot waits 7 days before proposing an update |
| A GitHub Action is replaced under its tag | Every action pinned by commit SHA; actionlint and zizmor (workflow security linter) on every pull request |
| A workflow token does too much | `contents: read` by default; each job asks for what it needs; checkouts do not keep credentials |
| A fork's pull request abuses CI | Fork pull requests get a read-only token: no image push, no comment, no secret; previews load images into the cluster instead of pulling them |
| A cache from a pull request poisons a release | The release workflow uses no dependency cache |
| An image is built from something else than this repository | Releases are signed with cosign keyless (identity: this repository's `release.yml`, on a tag), with an SBOM attestation and SLSA provenance; the release gate verifies the signature before deploying |
| Code-level vulnerabilities | CodeQL on C# and on the workflows; analyzers at `latest-recommended` with warnings as errors |

How to check a release image yourself is in
[release-qualification.md](release-qualification.md#verifying-a-release-image-yourself).

## Images

- Runtime images are Ubuntu chiseled: no shell, no package manager, only the .NET runtime and its
  dependencies. Base images are pinned by digest.
- Every container runs as a non-root user with a read-only root filesystem.
- The backup image is based on the official PostgreSQL image for its client tools. `gosu`, which it ships
  for dropping root, is removed: the image never runs as root, and the shipped binary carried a critical CVE.
- Trivy fails the build on any fixable critical CVE, on both architectures. Unfixable ones are reported in
  the scan artifact without blocking, since nothing can be done about them yet.

## Kubernetes

- **Pod Security `restricted`** on every preview namespace, enforced by the API server. Containers drop all
  capabilities, cannot escalate privileges, use the runtime default seccomp profile and run as non-root.
- **No service account token** in the API, migration, seed, backup and annotation pods
  (`automountServiceAccountToken: false`). None of them calls the Kubernetes API.
- **NetworkPolicies, deny by default.** In a preview, DNS is allowed, and so are the ingress controller to the
  API, the platform's pods to their own database, the API to the OpenTelemetry collector, and the deployment
  Job to the Kubernetes API. Nothing else gets through, including traffic between previews. Backups only reach
  the database. The restore test has no network access at all.
- **Operator RBAC.** Cluster-wide, it can manage PreviewEnvironments, create and delete namespaces, and bind
  two named ClusterRoles (`bind` restricted by `resourceNames`). Everything else (Secrets, Jobs,
  NetworkPolicies) is granted only inside the namespaces it creates, by a RoleBinding it applies there. Every
  rule is commented in `deploy/operator/rbac.yaml`. Trivy flags the namespace-scoped rules on Secrets and
  networking; `.trivyignore.yaml` accepts them for that file only, with the reason.
- **Database roles.** The API connects with a role that can only read and write rows. Only the migration Job
  holds the role that owns the schema.

## Application

- **Authentication.** Bearer JWT, validated against an OpenID Connect authority in production, or a shared
  HMAC key of at least 256 bits in local and preview environments. Issuer, audience and lifetime are always
  checked. Configuring both modes at once is refused at startup.
- **Authorization.** Every `/v1` endpoint needs an authenticated user. Deciding needs the `manager` role and
  being the employee's manager. Nobody decides on their own request. A request the caller may not see answers
  404, not 403, so ids cannot be probed.
- **Input.** Requests are validated before they reach the handlers, then business rules are checked by the
  domain. Errors are problem documents with a stable code and no stack trace.
- **Abuse.** Token-bucket rate limiting per user, or per IP for anonymous calls. Creation is idempotent, so
  client retries do not duplicate data.
- **Proxies.** `X-Forwarded-*` headers are trusted because the NetworkPolicy only admits traffic from the
  ingress controller's namespace. Without that policy, this setting would let anyone spoof their IP.
- **Fault injection** exists for rehearsals and is refused in Production by the API and by the chart.

## Secrets

- None in the repository, the chart values or the images. `.env.example` documents what a local run needs,
  with empty values.
- Previews: the operator generates the database passwords and the JWT signing key when it creates the
  namespace, stores them in Secrets of that namespace, and never logs them.
- The API reads secrets from files mounted from Secrets, not from environment variables, which leak into
  `kubectl describe`, crash dumps and child processes.
- CI uses the workflow's own short-lived token and OIDC. There is no long-lived registry or signing credential.

## devctl and language models

`devctl preview diagnose` can send its report to a language model, and only does so when
`DEVCTL_LLM_ENDPOINT` is set. Before anything is sent, the report goes through `Redactor`. It removes passwords
and connection strings, JWTs and bearer tokens, credentials in URLs, values of any setting whose name looks
like a secret, age private keys and email addresses. The redaction rules are tested, including what must be
left intact. `--show-payload` prints the exact request without sending it. Preview data is synthetic or
pseudonymised to begin with, so the logs do not contain real people either.

## Known limits

- **No admission control on signatures.** Release images are signed, but the cluster does not refuse unsigned
  ones. A policy controller (Sigstore policy-controller, Kyverno) would close that, and it is the next step.
- **No mutual TLS between services.** Traffic inside the cluster is restricted by NetworkPolicies, not
  encrypted. A service mesh, or TLS to PostgreSQL, would add it.
- **Previews are not behind authentication.** Anyone who can reach the preview domain can call a preview, with
  a valid token for that preview. That is acceptable because previews hold no real data. A shared cluster
  would put the preview domain behind the company's identity-aware proxy.
- **Grafana allows anonymous read-only access** in the local stack, for convenience. A shared stack would use
  single sign-on.
- **NetworkPolicies need a CNI that enforces them, statefully.** k3s does. kindnet, the default plugin of kind,
  drops the DNS answers to pods that have an Ingress default deny, so the CI clusters run Calico instead
  (`deploy/ci/install-calico.sh`). Some managed clusters need enforcement enabled.
