# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f deploy/helm/Dockerfile .
# helm plus the sample-api chart of the same commit. The preview operator runs it as a Job, so a preview always
# deploys the chart that was reviewed with the code.
FROM alpine/helm:4.3.1@sha256:e3f8d5c462d75470fbb1fce79ad5c20063657096d1f98c0fcea869e2600e2e5e
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="sample-deployer" \
      org.opencontainers.image.description="helm and the sample-api chart, run by the preview operator" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

COPY deploy/helm/sample-api /charts/sample-api
ENV HOME=/tmp
USER 65532:65532
ENTRYPOINT ["helm"]
