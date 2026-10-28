# The preview operator is in CrashLoopBackOff

Alert: `PreviewOperatorDown`. Nothing creates, updates or deletes previews meanwhile, and expired previews
are not cleaned up. Existing previews keep serving.

## 1. What does it say before dying?

```sh
kubectl -n preview-system get pods
kubectl -n preview-system logs deployment/preview-operator --previous --tail 100
```

| Log says | Cause | Fix |
|----------|-------|-----|
| `OptionsValidationException: Operator:...` | Invalid configuration in `preview-operator-config` | Fix the ConfigMap (`deploy/operator/kustomization.yaml`), then `kubectl -n preview-system rollout restart deployment/preview-operator` |
| `Forbidden` on a resource | A code change needs a permission that `rbac.yaml` does not grant | Add the rule (commented, as narrow as possible) and apply `deploy/operator` |
| `the server could not find the requested resource` for `previewenvironments` | The CRD is missing or an old version | `kubectl apply -k deploy/crds` |
| `OOMKilled` in `describe pod` | Memory limit too low for the number of previews watched | Raise the limit in `deploy/operator/deployment.yaml` |

## 2. Is it the code?

If the crash started with a new operator image, roll back to the previous one:

```sh
kubectl -n preview-system rollout undo deployment/preview-operator
kubectl -n preview-system rollout status deployment/preview-operator
```

The operator is stateless: everything it knows is in the cluster. Rolling back loses nothing. After the
rollback it reconciles every preview from scratch, which is safe because every step is idempotent.

## 3. After the fix

- `kubectl get previewenvironments` should show every preview with a recent `observedGeneration`.
- Previews that expired during the outage are deleted within a minute of the restart.
