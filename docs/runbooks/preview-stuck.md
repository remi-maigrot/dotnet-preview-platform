# A preview is stuck, failed, or never became Ready

Alerts: `PreviewEnvironmentStuck`, `PreviewEnvironmentFailed`, `PreviewReconciliationFailing`. Or a pull
request comment that never came.

## 1. Ask devctl first

```sh
devctl preview diagnose --pr <n>
```

It reads the preview's status, the pods, jobs and warning events of its namespace, the logs of whatever is
failing and the recent failed requests in Tempo, then lists findings. Most cases end here.

## 2. Read the status

```sh
kubectl get previewenvironment pr-<n> -o yaml          # status.phase, status.message, status.conditions
kubectl get events -n default --field-selector involvedObject.name=pr-<n>
```

The conditions say how far the operator got: `NamespaceReady`, `DatabaseReady`, `ApplicationDeployed`,
`Ready`. The first one that is not `True` is where to look.

| Stuck at | Usual cause | Fix |
|----------|-------------|-----|
| `NamespaceReady` | The operator lacks a permission, or is not running | `devctl logs operator`; see [operator-crashloop.md](operator-crashloop.md) |
| `DatabaseReady` | The PostgreSQL pod cannot be scheduled (quota, node memory) | `kubectl -n pr-<n> describe pod postgresql-0` |
| `ApplicationDeployed` | The deploy Job failed: image not found, migration failed, chart error | `kubectl -n pr-<n> logs job/<deploy job>`; for a migration see [failed-migration.md](failed-migration.md) |
| `Ready` | The API does not pass its readiness probe | `devctl logs api --pr <n>`; usually configuration, which the log names |

`Failed` stays `Failed` until the PreviewEnvironment changes. The failed Job is kept for its logs, and a new
spec (new commit, new image) gets a new one. Fix the cause and push again, since the workflow re-applies the
resource, or run `devctl preview create --pr <n>`.

## 3. When the operator is the problem

```sh
kubectl -n preview-system get pods,lease
devctl logs operator
```

- `PreviewOperatorNoLeader`: replicas run but none holds the Lease `preview-operator`. Deleting the Lease lets
  them elect again.
- `PreviewOperatorQueueBacklog`: reconciliations are slower than changes. Look at the reconciliation
  duration on the operator dashboard, then raise `Operator__Workers`.

## 4. Last resort

```sh
kubectl delete previewenvironment pr-<n>     # the finalizer deletes the namespace and everything in it
```

If the namespace stays in `Terminating`, an object in it has a finalizer of its own:
`kubectl api-resources --verbs=list --namespaced -o name | xargs -n1 kubectl get -n pr-<n> --ignore-not-found`.
Never remove the operator's finalizer by hand unless the operator is gone for good: that leaves the
namespace behind.
