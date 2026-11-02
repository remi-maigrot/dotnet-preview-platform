# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/backup/Dockerfile .
# pg_dump, pg_restore and age, with the two scripts run by the backup and restore-test CronJobs of the chart.
# The PostgreSQL client matches the server major version (17) so dumps restore without conversion.
FROM postgres:18-alpine@sha256:10a5c69a83035ab5239f5620d708520e8b28f5aa1f2b20143e6024532edadfb5
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
