# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/backup/Dockerfile .
# pg_dump, pg_restore and age, with the two scripts run by the backup and restore-test CronJobs of the chart.
# The PostgreSQL client matches the server major version (17) so dumps restore without conversion.
FROM postgres:17-alpine@sha256:266d3c257594e195b309ae9d1805e54c788f1d7b375f56bf6c367aac6c917e7b
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="sample-backup" \
      org.opencontainers.image.description="Encrypted PostgreSQL backups (pg_dump + age) and their restore test" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

# gosu only serves to drop root in docker-entrypoint.sh; this image never runs as root, and the copy shipped
# by the base image is built with an outdated Go (CVE-2025-68121).
RUN apk add --no-cache age jq && rm /usr/local/bin/gosu

COPY --chmod=0555 deploy/backup/backup.sh deploy/backup/restore-check.sh /usr/local/bin/
ENV HOME=/tmp
# The postgres user of the image (uid 70): not root, and the owner of the data directory when the image runs
# the scratch server of the restore test.
USER 70:70
ENTRYPOINT []
CMD ["backup.sh"]
