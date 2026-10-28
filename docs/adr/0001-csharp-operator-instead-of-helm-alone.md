# 1. A C# operator for preview environments, rather than Helm alone

- Status: accepted
- Date: 2026-10-28

## Context

A preview environment is more than a Helm release. It needs a namespace with a quota and deny-by-default
network policies, a database with generated credentials, the application, optional seed data, a URL, and an
expiry date. When it expires, all of that must go, even if nobody remembers it exists.

The simplest approach is a CI job that runs `helm install` on pull request open and `helm uninstall` on close.
It works until a job is cancelled, a runner dies, or a pull request is closed while CI is down. The leftovers
then stay until someone notices the cluster bill.

## Decision

Previews are a Kubernetes resource, `PreviewEnvironment` (cluster-scoped CRD `platform.remimaigrot.dev/v1alpha1`),
reconciled by an operator written in C# with the official KubernetesClient:

- the desired state is declared once. CI, `devctl` or a person apply the same resource and the operator
  converges, retries on failure and reports progress in `status.conditions`;
- the TTL is enforced by the operator, not by whoever created the preview. Deleting the resource deletes
  everything through a finalizer and owner references;
- the application itself is still installed by Helm, from a Job running the chart of the same commit. The
  chart stays the single description of how the API is deployed, in previews and in production.

C# because the platform's users are .NET teams: they can read, debug and extend the operator without
learning Go, and it shares its contracts with `devctl` (`Preview.Operator.Crds`).

## Consequences

- One more component to run, with its own RBAC, metrics, alerts and runbook. It runs with leader election,
  two replicas, and permissions scoped to the namespaces it creates.
- Reconciliation is idempotent by construction (server-side apply of every object), so a crash midway is
  harmless. That property must be kept by every change, and the reconciler tests check it.
- The C# Kubernetes ecosystem is thinner than Go's: no controller-runtime, so the work queue and the leader
  election wiring are written here (about 700 lines, tested).

## Alternatives considered

- **Helm from CI only.** No component to run, but no TTL, no garbage collection, and failures leave state
  behind.
- **Argo CD ApplicationSet with the pull request generator.** A strong option when Argo CD is already the
  deployment tool. It still needs something for the database credentials, seeding and TTL, and it hides the
  reconciliation logic that this project means to show.
- **KubeOps (a .NET operator framework).** Less code, but it hides the controller mechanics (queue, resync,
  finalizers) behind conventions. Worth revisiting if more resources are added.
- **Go and controller-runtime.** The reference implementation, and the right call for a team of Go
  platform engineers. Not for platform users who write C# every day.
