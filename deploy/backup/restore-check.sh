#!/bin/bash
# Proves the latest backup can be restored. Restores it into a scratch PostgreSQL server (the sidecar of the
# restore-test Job, never the production database), then checks it against the manifest written at backup time.
# Exits non-zero on any failure, so the Job fails and the RestoreTestFailed alert fires.
#
# Environment: BACKUP_DIR, BACKUP_DATABASE, AGE_IDENTITY_FILE (private key), BACKUP_MAX_AGE_HOURS (default 26),
# PGHOST PGPORT PGUSER of the scratch server.
set -euo pipefail

: "${BACKUP_DIR:?}" "${BACKUP_DATABASE:?}" "${AGE_IDENTITY_FILE:?}"
max_age_hours="${BACKUP_MAX_AGE_HOURS:-26}"

log() { jq -cn --arg level "$1" --arg msg "$2" '{time: (now | todate), level: $level, msg: $msg}'; }
fail() { log error "$1"; exit 1; }

manifest="$(find "$BACKUP_DIR" -maxdepth 1 -name "${BACKUP_DATABASE}-*.json" | sort | tail -n 1)"
[ -n "$manifest" ] || fail "no backup found in ${BACKUP_DIR}"
dump="${BACKUP_DIR}/$(jq -r .file "$manifest")"
log info "checking $(basename "$dump")"

created="$(date -u -d "$(jq -r .created_at "$manifest" | sed 's/T/ /; s/Z$//')" +%s)"
age_hours=$(( ($(date -u +%s) - created) / 3600 ))
[ "$age_hours" -le "$max_age_hours" ] || fail "latest backup is ${age_hours} h old (limit ${max_age_hours} h): backups are not running"

[ "$(sha256sum "$dump" | cut -d' ' -f1)" = "$(jq -r .sha256 "$manifest")" ] || fail "checksum mismatch: the backup file was altered or truncated"

until pg_isready -q; do sleep 1; done
createdb restore_check
age --decrypt --identity "$AGE_IDENTITY_FILE" "$dump" \
  | pg_restore --dbname restore_check --no-owner --no-privileges --exit-on-error \
  || fail "restore failed"

actual="$(psql -X -A -t -d restore_check -v ON_ERROR_STOP=1 <<'SQL'
SELECT json_build_object(
  'employees', (SELECT count(*) FROM employees),
  'absence_requests', (SELECT count(*) FROM absence_requests),
  'latest_migration', (SELECT max(migration_id) FROM __ef_migrations_history),
  'orphan_requests', (SELECT count(*) FROM absence_requests r LEFT JOIN employees e ON e.id = r.employee_id WHERE e.id IS NULL),
  'invalid_periods', (SELECT count(*) FROM absence_requests WHERE end_date < start_date));
SQL
)"
expected="$(jq -c .counts "$manifest")"
log info "restored: ${actual}"

for key in employees absence_requests latest_migration; do
  [ "$(jq -r ".${key}" <<<"$actual")" = "$(jq -r ".${key}" <<<"$expected")" ] \
    || fail "${key} differs: backup says $(jq -r ".${key}" <<<"$expected"), restore has $(jq -r ".${key}" <<<"$actual")"
done
[ "$(jq -r .employees <<<"$actual")" -gt 0 ] || fail "the restored database has no employees"
[ "$(jq -r .orphan_requests <<<"$actual")" = 0 ] || fail "absence requests reference missing employees"
[ "$(jq -r .invalid_periods <<<"$actual")" = 0 ] || fail "absence requests with an end before their start"

log info "restore test passed: backup of $(jq -r .created_at "$manifest") restored and matches its manifest"
