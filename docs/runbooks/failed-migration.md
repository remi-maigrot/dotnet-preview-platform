# A database migration failed

Seen as: the release gate "Migrations" is red, a preview stuck at `ApplicationDeployed`, or a Helm upgrade
that stopped on the `sample-api-migrate` hook.

Migrations run in a pre-upgrade Job (ADR 0003). When the Job fails, Helm stops the release and the previous
version keeps serving. Production is not broken, it is not upgraded. There is no need to rush.

## 1. What failed?

```sh
kubectl -n <namespace> logs job/sample-api-migrate
dotnet run --project src/Sample.Migrator -- list     # with Database__ConnectionString set: applied and pending
```

| Error | Meaning | Fix |
|-------|---------|-----|
| `column ... contains null values` | A new `NOT NULL` column without a default, on a table with rows | Add the column nullable or with a default, backfill, make it `NOT NULL` in a later release |
| `could not create unique index` | Duplicates in existing data | Deduplicate in the migration first, or relax the constraint |
| `lock timeout` / Job deadline exceeded | The migration waits for locks held by the running API | Split it. Avoid table rewrites (type changes) on large tables; create indexes `CONCURRENTLY` in a migration of their own |
| `permission denied` | The migrator runs with the wrong role | The migrator Secret must use the role that owns the schema, not the API's |

## 2. Is the database half-migrated?

PostgreSQL has transactional DDL, and EF Core applies migrations in transactions. The failed migration is
rolled back entirely and is not recorded in `__ef_migrations_history`. `Sample.Migrator list` shows exactly
what is applied. Whatever is applied is backward compatible by rule, so the running version is unaffected.

The exceptions are statements that cannot run in a transaction (`CREATE INDEX CONCURRENTLY`). Check the
object they create, drop it if it is half-built (`\d+ <table>` shows an `INVALID` index), and re-run.

## 3. Fix and release again

1. Write the fix as a new migration, or change the failing one if it never succeeded anywhere.
2. Reproduce locally: the release gate runs the previous release's migrator and seeder, then yours. Do the
   same with `git worktree add ../previous <previous tag>`.
3. Release a new version. Never edit a migration that has already been applied somewhere: that database
   will never run it again.

## Rolling back a migration

Not done automatically, on purpose. The previous code works on the new schema, so rolling back the
application is enough. To undo a migration that is harmful in itself, write a new migration that reverts it,
and release it like any other change.
