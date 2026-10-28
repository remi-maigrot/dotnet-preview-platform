# 6. Typed configuration, validated at startup, with secrets only from mounted files

- Status: accepted
- Date: 2026-10-28

## Context

Configuration errors are the most common cause of a failed deployment: a missing connection string, a
timeout in the wrong unit, a flag spelled wrong. They tend to surface on the first request that needs the
setting, minutes after the deployment was declared healthy.

## Decision

- Every section binds to an options class with Data Annotations (`[Required]`, `[Range]`, `[Url]`) and, for
  rules across fields, an `IValidateOptions<T>` or `IValidatableObject`. All of them use `ValidateOnStart`: an
  invalid configuration stops the process before it accepts traffic, with a message naming the key.
- Layers, from lowest to highest priority: `appsettings.json`, `appsettings.{Environment}.json`, user
  secrets (Development only), environment variables, command line, then one file per key mounted from a
  Kubernetes Secret (`/var/run/secrets/sample-api`).
- No secret is ever in `appsettings*.json`, in the chart values or in the repository. The chart only
  references Secrets by name, and the operator generates the ones previews need.
- Some combinations are refused outright: fault injection in Production, a JWT authority and a shared
  signing key together, a free-text retention longer than the request retention.

## Consequences

- A bad configuration fails the readiness of the first new pod, so a rolling update or a canary stops before
  any user sees it.
- The full reference of settings, with defaults and where each one comes from, is docs/configuration.md. It
  has to be kept in sync with the options classes.
- Files from a Secret are read once at startup. Rotating a secret means restarting the pods, which is explicit
  and auditable. Hot reload would make the configuration of a running pod depend on timing.

## Alternatives considered

- **Environment variables for secrets.** Simpler, but they leak into crash dumps, `kubectl describe` output
  and child processes.
- **A secret store SDK in the application (Vault, a cloud key vault).** Ties the code to one store. The
  External Secrets Operator or the CSI driver can fill the same mounted files without the application knowing.
