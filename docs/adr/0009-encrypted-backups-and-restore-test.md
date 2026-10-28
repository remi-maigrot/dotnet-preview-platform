# 9. Encrypted logical backups, proven by a daily restore test

- Status: accepted
- Date: 2026-10-28

## Context

The database holds absence requests, and some of them are health data in all but name. Backups are the
copy most likely to leak: they sit somewhere else, are kept longer than anyone remembers, and are read by
tools nobody reviews. They are also the copy nobody checks until the day it is needed.

The requirements:

- a backup must be useless to whoever steals the file or the volume;
- the job that writes backups should not be able to read them;
- someone outside the platform team must be able to recover the data if the cluster and its Secrets are gone;
- every day, something must prove that the latest backup restores and contains what it should.

## Decision

- `pg_dump --format=custom`, piped into `age --encrypt` for a list of X25519 public keys, from a CronJob. The
  plaintext never touches a disk.
- At least two recipients: the restore-test key (private half in a Kubernetes Secret mounted only in the
  restore-test pod) and an offline recovery key kept outside the cluster.
- The dump reads an exported snapshot, and a manifest records the row counts, the latest migration and the
  checksum of that same snapshot.
- A second CronJob restores the latest backup every day into a scratch PostgreSQL running as a native sidecar
  in the same pod. It compares the restored data with the manifest, checks integrity, and fails without retry.
  Alerts fire when it fails, when backups are more than 26 hours old, and when no restore test has passed for
  two days.
- The whole chain is tested in CI with Docker (`deploy/backup/test.sh`), including the failures: wrong key,
  altered file.

## Consequences

- Losing the cluster does not lose the backups' readability, as long as the recovery key is kept and stored
  apart from the cluster. Losing every private key loses the backups, and that is the price of the
  property "the backup job cannot read backups".
- Up to 24 hours of writes can be lost (RPO of one day). Acceptable for absence requests, which people can
  re-enter. A database that cannot lose a day needs WAL archiving and point-in-time recovery instead. The
  restore-test pattern applies unchanged.
- The restore test needs memory and disk for a full copy of the database once a day. With this dataset it
  takes seconds. With a large database it takes longer and costs more, and it is still the only proof.
- Erased personal data survives in backups until they expire (14 days by default). This is documented in
  docs/data-protection.md, and the restore runbook re-runs retention after a restore.

## Alternatives considered

- **Managed backups of a cloud database.** The right answer in production, and the restore test should be
  kept on top of it. A managed snapshot proves the provider took a snapshot, not that the application can
  use it. Not available on a laptop cluster.
- **pgBackRest or WAL-G with continuous archiving.** Better RPO, more moving parts (archive command, repository
  maintenance, retention of WAL). Worth it past a certain data value. Too much for this platform's needs.
- **GPG.** Works, but key management is error-prone (expiry, subkeys, keyrings), and the tooling invites
  mistakes. age has one key type, no configuration, and a format small enough to review.
- **Encryption by the storage layer only** (encrypted volume, server-side encryption in the bucket). It
  protects against a stolen disk, not against anyone who can read the volume or the bucket through the
  normal API. That is the threat that matters. Useful as an extra layer, not as the only one.
- **Restoring into the production server under another database name.** Simpler, but a restore test that runs
  next to production can, through one mistake in a script, write to production. The scratch server has no
  network access at all.
