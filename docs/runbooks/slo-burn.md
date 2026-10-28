# The API is burning its error budget

Alerts: `SampleApiErrorBudgetBurnFast`, `SampleApiErrorBudgetBurnMedium` (page), `SampleApiErrorBudgetBurnSlow`,
`SampleApiHighErrorRate`, `SampleApiHighLatency` (ticket). The SLO is 99.5 % of requests without a 5xx and
under 300 ms ([observability.md](../observability.md)).

## 1. Is it a release?

Open the *Sample API* dashboard on the environment in the alert (`k8s_namespace_name`). Purple markers are
releases. If the errors start at one:

- with a canary, the analysis has probably aborted it already (`kubectl argo rollouts get rollout sample-api`,
  or `kubectl get rollout,analysisrun`). Check that traffic is back to stable;
- otherwise roll back: `helm rollback sample-api` or deploy the previous SHA. Do it first, investigate after.

## 2. Errors or latency?

The SLI counts both. On the dashboard:

- **Responses by status code** and **Error ratio**: which status, which route.
- **Latency**: p95 and p99. Click a diamond (an exemplar) to open that slow request's trace.
- **Database query duration**: if it rises with the API latency, the database is the bottleneck.

## 3. From the trace to the cause

In the trace, the slowest span says where the time goes: the database (one slow query, or many queries,
the N+1 pattern), the external holiday calendar (its timeout and circuit breaker are in the span
attributes), or the API itself. "Logs for this span" shows the log lines of the same request.

| Symptom | Likely cause | Action |
|---------|--------------|--------|
| 5xx on every route, database spans failing | Database down or out of connections | `kubectl -n <ns> get pods`; check `db_client_connection_count` against the pool size |
| Latency up, holiday calendar spans long | External calendar slow; the breaker should open | Turn `Features:ExternalHolidayCalendar` off: the built-in calendar takes over |
| 5xx with `request.fault_injected` | Fault injection is on | Turn `FaultInjection:Enabled` off. It cannot be on in Production |
| 429 rising, not 5xx | Rate limiting a client | Not an SLO burn (4xx). Check whether the limit or the client is wrong |

## 4. Afterwards

A page means part of the monthly budget is gone. Write down the cause, the time to detect and the time to
recover. If the budget is exhausted, reliability work comes before features until the 30-day window
recovers.
