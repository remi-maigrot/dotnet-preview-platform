#!/bin/bash
# Dumps the database and encrypts the dump for the age recipients, in one stream: the plaintext never touches
# a disk. Writes next to it a manifest with the row counts and the schema version seen by the same snapshot,
# which the restore test compares against. Then deletes backups older than BACKUP_KEEP_DAYS.
#
# Environment: PGHOST PGPORT PGDATABASE PGUSER PGPASSWORD (libpq), AGE_RECIPIENTS_FILE (public keys, one per
# line), BACKUP_DIR, BACKUP_KEEP_DAYS (default 14).
set -euo pipefail

: "${PGDATABASE:?}" "${AGE_RECIPIENTS_FILE:?}" "${BACKUP_DIR:?}"
keep_days="${BACKUP_KEEP_DAYS:-14}"
stamp="$(date -u +%Y%m%dT%H%M%SZ)"
dump="${BACKUP_DIR}/${PGDATABASE}-${stamp}.dump.age"
manifest="${BACKUP_DIR}/${PGDATABASE}-${stamp}.json"

log() { jq -cn --arg level "$1" --arg msg "$2" '{time: (now | todate), level: $level, msg: $msg}'; }

# One transaction exports its snapshot: pg_dump and the counts below read exactly the same data.
coproc PSQL { psql -X -q -A -t -v ON_ERROR_STOP=1; }
printf 'BEGIN ISOLATION LEVEL REPEATABLE READ, READ ONLY;\nSELECT pg_export_snapshot();\n' >&"${PSQL[1]}"
read -r snapshot <&"${PSQL[0]}"

pg_dump --format=custom --snapshot="$snapshot" --no-owner --no-privileges \
  | age --encrypt --recipients-file "$AGE_RECIPIENTS_FILE" --output "${dump}.partial"

cat >&"${PSQL[1]}" <<'SQL'
SELECT json_build_object(
  'employees', (SELECT count(*) FROM employees),
  'absence_requests', (SELECT count(*) FROM absence_requests),
  'latest_migration', (SELECT max(migration_id) FROM __ef_migrations_history));
COMMIT;
SQL
read -r counts <&"${PSQL[0]}"
psql_in="${PSQL[1]}"
exec {psql_in}>&-
wait "$PSQL_PID"

mv "${dump}.partial" "$dump"
jq -n --arg file "$(basename "$dump")" --arg created "$(date -u +%FT%TZ)" --arg database "$PGDATABASE" \
  --arg sha256 "$(sha256sum "$dump" | cut -d' ' -f1)" --argjson counts "$counts" \
  '{file: $file, created_at: $created, database: $database, sha256: $sha256, counts: $counts}' > "$manifest"
log info "backup written: $(basename "$dump") ($(du -h "$dump" | cut -f1)), $(jq -c .counts "$manifest")"

# Rotation. Erased personal data survives in backups until they expire: keep_days bounds that delay.
find "$BACKUP_DIR" -maxdepth 1 -name "${PGDATABASE}-*" -mtime "+${keep_days}" -print -delete \
  | while read -r removed; do log info "expired backup deleted: $(basename "$removed")"; done
