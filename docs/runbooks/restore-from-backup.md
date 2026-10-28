# Restore the database from a backup

Alerts: `RestoreTestFailed`, `BackupTooOld`, `RestoreTestNotRunning`. Or data was lost and must be recovered.

Backups are `pg_dump` files encrypted with age, plus a manifest, on the backup volume
([data-protection.md](../data-protection.md#backups)).

## When the restore test or the backup fails

```sh
kubectl -n <ns> get cronjob,jobs                           # last runs of sample-api-backup and its restore test
kubectl -n <ns> logs job/<failed job> -c restore-check     # or -c backup
```

Each failure says what it checked: no backup at all, a backup older than 26 hours, a checksum mismatch (the
file was altered or truncated), a decryption error (wrong key), a restore error, or counts that differ from
the manifest. Until a restore test passes again, assume there is no usable backup, and take one by hand:

```sh
kubectl -n <ns> create job backup-now --from=cronjob/sample-api-backup
kubectl -n <ns> create job restore-test-now --from=cronjob/sample-api-backup-restore-test
```

## Restoring for real

1. **Stop writes.** Scale the API to zero (`kubectl -n <ns> scale deployment/sample-api --replicas 0`, or
   pause the Rollout). A restore over live writes loses them silently.
2. **Pick the backup.** `ls /backups` from a pod that mounts the volume. The manifest next to each file gives
   its time and row counts.
3. **Get a private key.** In-cluster, the restore-test key (Secret `restoreTest.identitySecret`). If the cluster
   is gone, the offline recovery key, from whoever keeps it.
4. **Restore into a new database**, never over the current one:

   ```sh
   createdb absences_restored
   age --decrypt --identity key.txt absences-<time>.dump.age | pg_restore --dbname absences_restored --no-owner --exit-on-error
   ```

5. **Check it** with the same queries as the restore test (`deploy/backup/restore-check.sh`): row counts
   against the manifest, no orphan requests.
6. **Switch.** Point the connection string Secrets of the API and the migrator at the restored database (or
   rename the databases), then run the migrator: the backup may predate the latest migration.
7. **Re-apply retention.** Personal data erased since the backup was taken is back. Start the API, which
   applies retention at startup, and check its log for `Retention applied`.
8. **Scale the API back up**, and write down what was lost: everything between the backup and the incident.
