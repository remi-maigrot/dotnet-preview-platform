#!/bin/bash
# End-to-end test of the canary release: on a cluster with Traefik and deploy/observability installed, it
# deploys the API as an Argo Rollout, then
#   1. releases a version that fails half of its requests: the analysis must abort it, back to 100 % stable;
#   2. releases a healthy version: the analysis must let it through to 100 %.
# With a Grafana in the cluster, every release is also annotated on the dashboards (and checked).
#
# Usage: e2e.sh <registry> <tag> [base url]     e.g. e2e.sh preview-registry.localhost:5050 abc123 http://canary.preview.localtest.me:8080
# Environment: KUBE_CONTEXT (default: current context), KEEP=1 to keep the namespace afterwards.
set -euo pipefail

registry="$1" tag="$2" base_url="${3:-http://canary.preview.localtest.me}"
ns=canary-e2e release=sample-api
here="$(cd "$(dirname "$0")" && pwd)" root="$(cd "$here/../.." && pwd)"
K() { kubectl ${KUBE_CONTEXT:+--context "$KUBE_CONTEXT"} "$@"; }
H() { helm ${KUBE_CONTEXT:+--kube-context "$KUBE_CONTEXT"} "$@"; }
step() { printf '\n== %s\n' "$1"; }
fail() { printf '\nFAIL: %s\n' "$1"; K -n "$ns" get rollout,analysisrun,pods || true; exit 1; }

traffic_pid=""
cleanup() {
  [ -n "$traffic_pid" ] && kill "$traffic_pid" 2> /dev/null || true
  [ "${KEEP:-0}" = 1 ] || K delete namespace "$ns" --wait=false > /dev/null 2>&1 || true
}
trap cleanup EXIT

phase() { K -n "$ns" get rollout "$release" -o jsonpath='{.status.phase}'; }
wait_phase() {
  local expected="$1" timeout="$2" waited=0
  until [ "$(phase)" = "$expected" ]; do
    [ "$waited" -lt "$timeout" ] || return 1
    sleep 5; waited=$((waited + 5))
  done
}
error_ratio() {
  local errors=0 total=40
  for _ in $(seq "$total"); do
    code="$(curl -s -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $employee_token" "$base_url/v1/absence-requests")"
    [ "$code" = 200 ] || errors=$((errors + 1))
  done
  echo "$errors/$total"
}
# Client-side apply: Argo Rollouts owns the Services' selectors and the TraefikService weights while a rollout
# runs, and Helm 4's default server-side apply would fight it over them (see docs/adr/0008).
helm_release() {
  H upgrade --install "$release" "$root/deploy/helm/sample-api" --namespace "$ns" --server-side=false \
    --values "$here/values.yaml" "${grafana_args[@]}" \
    --set image.repository="$registry/sample-api" --set image.tag="$tag" \
    --set migrations.image.repository="$registry/sample-migrator" "$@" > /dev/null
}

step "Argo Rollouts controller"
H upgrade --install argo-rollouts argo-rollouts --repo https://argoproj.github.io/argo-helm --version 2.43.8 \
  --namespace argo-rollouts --create-namespace --wait --timeout 5m > /dev/null
K get crd analysistemplates.argoproj.io > /dev/null

step "namespace, database and secrets"
K create namespace "$ns" --dry-run=client -o yaml | K apply -f - > /dev/null
K label namespace "$ns" pod-security.kubernetes.io/enforce=restricted --overwrite > /dev/null
password="$(head -c 18 /dev/urandom | base64 | tr -d '/+=')"
signing_key="$(head -c 48 /dev/urandom | base64 | tr -d '/+=')"
connection="Host=postgresql;Database=absences;Username=postgres;Password=$password;Gss Encryption Mode=Disable"
K -n "$ns" create secret generic postgresql --from-literal=POSTGRES_PASSWORD="$password" --dry-run=client -o yaml | K apply -f - > /dev/null
K -n "$ns" create secret generic sample-api-secrets --from-literal=Database__ConnectionString="$connection" \
  --from-literal=Authentication__Jwt__SigningKey="$signing_key" --dry-run=client -o yaml | K apply -f - > /dev/null
K -n "$ns" create secret generic sample-api-migrator-secrets --from-literal=Database__ConnectionString="$connection" \
  --dry-run=client -o yaml | K apply -f - > /dev/null
K -n "$ns" apply -f "$here/postgresql.yaml" > /dev/null
K -n "$ns" rollout status statefulset/postgresql --timeout 3m > /dev/null

grafana_args=()
if K -n observability get deployment grafana > /dev/null 2>&1; then
  step "Grafana token for release annotations"
  K -n observability port-forward svc/grafana 13000:80 > /dev/null 2>&1 &
  forward_pid=$!; sleep 3
  admin="admin:$(K -n observability get secret grafana -o jsonpath='{.data.admin-password}' | base64 --decode)"
  account="$(curl -sf -u "$admin" "http://localhost:13000/api/serviceaccounts/search?query=release-annotations" | jq '.serviceAccounts[0].id // empty')"
  [ -n "$account" ] || account="$(curl -sf -u "$admin" -H 'Content-Type: application/json' \
    -d '{"name":"release-annotations","role":"Editor"}' http://localhost:13000/api/serviceaccounts | jq .id)"
  grafana_token="$(curl -sf -u "$admin" -H 'Content-Type: application/json' -d "{\"name\":\"$ns-$(date +%s)\",\"secondsToLive\":3600}" \
    "http://localhost:13000/api/serviceaccounts/$account/tokens" | jq -r .key)"
  K -n "$ns" create secret generic grafana-annotation --from-literal=token="$grafana_token" --dry-run=client -o yaml | K apply -f - > /dev/null
  grafana_args=(--set grafanaAnnotation.enabled=true --set grafanaAnnotation.tokenSecret=grafana-annotation)
  since="$(($(date +%s) * 1000))"
fi

step "first release (stable)"
helm_release
wait_phase Healthy 300 || fail "the first release did not become Healthy"
K -n "$ns" create job seed --image="$registry/sample-seeder:$tag" --dry-run=client -o json \
  | jq --arg ns "$ns" '.spec.template.spec.containers[0] += {args: ["synthetic"], env: [{name: "Database__ConnectionString", valueFrom: {secretKeyRef: {name: "sample-api-migrator-secrets", key: "Database__ConnectionString"}}}], securityContext: {allowPrivilegeEscalation: false, runAsNonRoot: true, capabilities: {drop: ["ALL"]}, seccompProfile: {type: "RuntimeDefault"}}} | .spec.template.metadata.labels["app.kubernetes.io/part-of"] = "preview-platform"' \
  | K -n "$ns" apply -f - > /dev/null
K -n "$ns" wait job/seed --for=condition=Complete --timeout=3m > /dev/null
employee_token="$(k6 run --quiet -e SIGNING_KEY="$signing_key" "$here/print-token.js" 2>&1 | sed -n 's/.*token=\([^ "]*\).*/\1/p' | sed -n 1p)"
[ -n "$employee_token" ] || fail "could not mint a token"
echo "stable errors: $(error_ratio)"

step "steady traffic"
k6 run --quiet -e BASE_URL="$base_url" -e SIGNING_KEY="$signing_key" -e DURATION=12m "$root/load/canary-traffic.js" > /tmp/canary-traffic.log 2>&1 &
traffic_pid=$!

step "faulty release: 50 % of its requests fail; the analysis must abort it"
helm_release --reuse-values --set config.FaultInjection__Enabled=true --set config.FaultInjection__ErrorRatio=0.5
wait_phase Progressing 60 || true
wait_phase Degraded 300 || fail "the faulty canary was not aborted"
K -n "$ns" get analysisrun -o custom-columns=NAME:.metadata.name,PHASE:.status.phase,MESSAGE:.status.message --sort-by=.metadata.creationTimestamp | tail -n 2
[ "$(K -n "$ns" get rollout "$release" -o jsonpath='{.status.abort}')" = true ] || fail "the rollout is not marked aborted"
sleep 10
errors="$(error_ratio)"; echo "errors after the abort: $errors"
[ "${errors%%/*}" -eq 0 ] || fail "requests still fail after the abort: traffic did not go back to stable"

step "healthy release: the analysis must promote it"
helm_release --reuse-values --set config.FaultInjection__Enabled=false --set config.Features__AutoApproveSickLeave=true
wait_phase Progressing 60 || true
wait_phase Healthy 420 || fail "the healthy canary was not promoted"
K -n "$ns" get analysisrun -o custom-columns=NAME:.metadata.name,PHASE:.status.phase --sort-by=.metadata.creationTimestamp | tail -n 1
echo "errors after the promotion: $(error_ratio)"

if [ ${#grafana_args[@]} -gt 0 ]; then
  step "release annotations in Grafana"
  count="$(curl -sf -u "$admin" "http://localhost:13000/api/annotations?tags=release&tags=$ns&from=$since" | jq length)"
  kill "$forward_pid" 2> /dev/null || true
  echo "annotations: $count"
  [ "$count" -ge 3 ] || fail "expected one Grafana annotation per release (3), found $count"
fi

printf '\ncanary e2e passed: faulty release aborted, healthy release promoted\n'
