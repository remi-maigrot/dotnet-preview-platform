# 3. Database migrations run in a Helm pre-upgrade Job, never at application startup

- Status: accepted
- Date: 2026-10-28

## Context

EF Core can apply migrations when the application starts (`Database.Migrate()`). It is convenient, and wrong
for a service that runs several replicas:

- replicas start in parallel and race to migrate. EF Core has a migration lock, but a slow migration still
  holds every new pod in startup until the probes kill it;
- the application then needs DDL privileges at all times, so an SQL injection can drop tables;
- a failed migration surfaces as pods in CrashLoopBackOff, while the previous version is already being scaled
  down.

## Decision

- Migrations ship as a separate executable and image, `Sample.Migrator` (`apply` and `list`), built from the
  same commit as the API.
- The chart runs it as a Job with the hook `pre-install,pre-upgrade`. Helm waits for it to succeed before it
  touches the Deployment. If it fails, the release stops there and the pods of the previous version keep
  serving.
- The migrator uses a role that owns the schema. The API uses a role limited to SELECT, INSERT, UPDATE and
  DELETE.
- Migrations must be backward compatible with the previous release (expand, then contract in a later release).
  During a rolling update or a canary, both versions run on the new schema. The release workflow checks each
  migration against the previous release's schema and data.

## Consequences

- A deployment has one more step and one more image.
- The API cannot alter the schema, even if compromised.
- Renaming or dropping a column takes two releases. The release gate catches the mistakes that do not break
  an empty database but break real data.
- A migration that succeeded is not rolled back with the application. Rolling back means deploying the
  previous code on the newer schema, which is why backward compatibility is a rule rather than a guideline.

## Alternatives considered

- **Migrate at startup.** Rejected for the reasons above.
- **An init container in the API pod.** Same race between replicas, and DDL credentials mounted in every pod.
- **Migrations from CI against the database.** CI then needs network access to every database and its
  credentials. The hook runs inside the cluster, next to the database.
