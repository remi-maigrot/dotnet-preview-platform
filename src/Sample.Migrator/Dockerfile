# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/Sample.Migrator/Dockerfile .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Sample.Domain/Sample.Domain.csproj src/Sample.Domain/packages.lock.json src/Sample.Domain/
COPY src/Sample.Application/Sample.Application.csproj src/Sample.Application/packages.lock.json src/Sample.Application/
COPY src/Sample.Infrastructure/Sample.Infrastructure.csproj src/Sample.Infrastructure/packages.lock.json src/Sample.Infrastructure/
COPY src/Sample.Migrator/Sample.Migrator.csproj src/Sample.Migrator/packages.lock.json src/Sample.Migrator/
RUN dotnet restore src/Sample.Migrator/Sample.Migrator.csproj --locked-mode

COPY src/ src/
ARG VERSION=0.0.0-local
RUN dotnet publish src/Sample.Migrator/Sample.Migrator.csproj \
      --configuration Release --no-restore --output /app \
      -p:InformationalVersion=$VERSION -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:3eb54ac708855f74b032de48e02437d15fb083b2aa593d49fdd512bb17f3e944 AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="sample-migrator" \
      org.opencontainers.image.description="Applies the sample-api database migrations" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

WORKDIR /app
COPY --from=build /app ./
USER 1654
ENTRYPOINT ["dotnet", "Sample.Migrator.dll"]
CMD ["apply"]
