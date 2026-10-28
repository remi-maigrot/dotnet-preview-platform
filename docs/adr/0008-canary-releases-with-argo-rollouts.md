# 8. Canary releases with Argo Rollouts, judged by the SLO

- Status: accepted
- Date: 2026-10-28

## Context

Release qualification (docs/release-qualification.md) proves a version works on a preview: synthetic traffic,
seeded data, an empty cluster. Production differs in ways a preview cannot reproduce: real traffic shapes,
real data volumes, clients nobody tested. Some regressions only show up there, and the question is how many
users they reach before someone notices.

A rolling update replaces every pod in a few minutes, whatever the new pods do once they serve traffic. The
readiness probe only proves the process answers `/health/ready`. A release that answers 500 on one route
passes it.

## Decision

When `rollout.enabled` is set, the chart renders the API as an Argo Rollout instead of a Deployment, with the
same pod template:

- **Traffic is shifted by weight, not by pod count.** Traefik routes through a weighted TraefikService that
  Argo Rollouts rewrites at every step (10 %, 30 %, 60 %, then 100 % by default). A canary of one pod can take
  10 % of the traffic, which replica counts alone cannot do.
- **The canary is judged by the SLO, not by a separate threshold.** An AnalysisTemplate queries Prometheus
  every 30 seconds for the canary pods only: their 5xx ratio (at most 1 %) and their p95 latency (at most
  300 ms). The pods are told apart by `rollouts_pod_template_hash`, a resource attribute the chart adds to the
  OTel resource of Rollout pods.
- **A failed analysis aborts the rollout on its own.** Weights go back to 100 % stable and the canary is
  scaled down. Nobody has to be awake for a bad release to be stopped.
- **The release is applied client-side** (`helm upgrade --server-side=false`). While a rollout runs, Argo
  Rollouts owns the Services' selectors and the TraefikService weights. Helm 4's default server-side apply
  claims those fields too and fails on the conflict. A client-side patch only sends what changed in the chart.
- **Rehearsable.** `FaultInjection` makes a share of the API calls fail on purpose. The API refuses to start
  with it in Production, and the chart refuses to render it there. `deploy/canary/e2e.sh` releases a version
  that fails half of its requests and checks it is aborted with no errors left. It then releases a healthy
  version and checks it is promoted. The `Canary` workflow runs it on a kind cluster.

Previews keep a plain Deployment. A preview has one user, the author of the pull request, and nothing to
compare a canary with.

## Consequences

- A release takes longer, about 8 minutes with the default steps instead of 2. That is the time it takes to
  collect enough requests to judge.
- The analysis needs traffic. With no requests on the canary, the queries return 0 and the step passes, so a
  service idle at night is promoted untested. A synthetic load during the rollout, or a minimum request
  count in the query, would close that gap, at the cost of a less general template.
- The judgment is only as good as the metrics. A release that returns 200 with wrong content passes. That is
  what tests and the smoke tests on the release candidate are for.
- A schema migration runs before the first canary pod (Helm pre-upgrade hook). An aborted rollout leaves the
  new schema with the old code. Migrations must therefore stay backward compatible with the previous release
  (expand, then contract in a later release), which the release gate on migrations already enforces.
- One more controller to run and upgrade (Argo Rollouts), and Traefik CRDs instead of a plain Ingress for this
  service.

## Alternatives considered

- **Rolling update plus alerts.** Simpler, but the alert fires after every pod runs the new version, and the
  rollback is manual.
- **Blue-green.** Instant switch and instant rollback, but the new version takes all the traffic at once, and
  the cluster needs twice the capacity during the release.
- **Flagger.** Same model and also a good fit. Argo Rollouts was chosen for its explicit Rollout resource,
  which is easier to read in `kubectl get` and in a review than a Deployment that a controller mirrors.
- **Gateway API traffic splitting.** The direction Kubernetes is taking. Argo Rollouts supports it through a
  plugin. Traefik's native CRDs were used because they ship with k3s, the local cluster of this platform.
