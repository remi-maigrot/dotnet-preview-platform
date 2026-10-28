#!/bin/bash
# End-to-end test of the backup image, with Docker only: a database migrated and seeded by the real images,
# a backup, a restore into a scratch server, then the two failures the restore test exists for (an altered
# file, the wrong key). Run by the Cake task Backup-Test.
#
# Usage: test.sh <backup image> <migrator image> <seeder image>
set -euo pipefail

backup_image="$1" migrator_image="$2" seeder_image="$3"
run_id="backup-test-$$"
network="$run_id" keys="$run_id-keys" backups="$run_id-backups"
password="$(head -c 18 /dev/urandom | base64 | tr -d '/+=')"

cleanup() {
  docker rm -f "$run_id-source" "$run_id-scratch" > /dev/null 2>&1 || true
  docker volume rm "$keys" "$backups" > /dev/null 2>&1 || true
  docker network rm "$network" > /dev/null 2>&1 || true
}
trap cleanup EXIT

step() { printf '\n== %s\n' "$1"; }
in_backup() { docker run --rm --network "$network" -v "$keys:/keys" -v "$backups:/backups" "$@"; }

docker network create "$network" > /dev/null
docker volume create "$keys" > /dev/null
docker volume create "$backups" > /dev/null
# Named volumes are created root-owned; the image runs as uid 70.
docker run --rm -u 0 -v "$keys:/keys" -v "$backups:/backups" "$backup_image" chown 70:70 /keys /backups

step "source database, migrated and seeded"
docker run -d --name "$run_id-source" --network "$network" -e POSTGRES_PASSWORD="$password" -e POSTGRES_DB=absences \
  "$backup_image" docker-entrypoint.sh postgres > /dev/null
until docker exec "$run_id-source" pg_isready -q -U postgres -d absences; do sleep 1; done
connection="Host=$run_id-source;Database=absences;Username=postgres;Password=$password;Gss Encryption Mode=Disable"
docker run --rm --network "$network" -e Database__ConnectionString="$connection" "$migrator_image" apply > /dev/null
docker run --rm --network "$network" -e Database__ConnectionString="$connection" "$seeder_image" synthetic | tail -n 1

step "age key pair"
in_backup "$backup_image" sh -c 'age-keygen -o /keys/identity 2> /dev/null && age-keygen -y /keys/identity > /keys/recipients'

step "backup"
in_backup -e PGHOST="$run_id-source" -e PGDATABASE=absences -e PGUSER=postgres -e PGPASSWORD="$password" \
  -e AGE_RECIPIENTS_FILE=/keys/recipients -e BACKUP_DIR=/backups "$backup_image" backup.sh
in_backup "$backup_image" sh -c 'ls -l /backups; ! grep -qa "example.invalid" /backups/*.dump.age' \
  || { echo "the backup contains plaintext personal data"; exit 1; }

restore_check() {
  docker rm -f "$run_id-scratch" > /dev/null 2>&1 || true
  docker run -d --name "$run_id-scratch" --network "$network" -e POSTGRES_HOST_AUTH_METHOD=trust \
    "$backup_image" docker-entrypoint.sh postgres > /dev/null
  in_backup -e PGHOST="$run_id-scratch" -e PGUSER=postgres -e BACKUP_DIR=/backups -e BACKUP_DATABASE=absences \
    -e AGE_IDENTITY_FILE="$1" "$backup_image" restore-check.sh
}

step "restore test on the intact backup: must pass"
restore_check /keys/identity

step "restore test with another key: must fail"
in_backup "$backup_image" sh -c 'age-keygen -o /keys/other 2> /dev/null'
if restore_check /keys/other; then echo "FAIL: a backup decrypted with the wrong key"; exit 1; fi

step "restore test on an altered file: must fail"
# The glob must be expanded by a loop: sh does not expand it in a redirection. $f belongs to the inner shell.
# shellcheck disable=SC2016
in_backup "$backup_image" sh -c 'for f in /backups/*.dump.age; do printf x >> "$f"; done'
if restore_check /keys/identity; then echo "FAIL: an altered backup passed the restore test"; exit 1; fi

printf '\nbackup test passed\n'
