# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/Sample.Api/Dockerfile .

# The SDK stage runs on the build machine's architecture; the output is portable IL, so one build serves
# both linux/amd64 and linux/arm64 without emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# Restore in its own layer: it is reused until a project file or a lock file changes.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Sample.Domain/Sample.Domain.csproj src/Sample.Domain/packages.lock.json src/Sample.Domain/
COPY src/Sample.Application/Sample.Application.csproj src/Sample.Application/packages.lock.json src/Sample.Application/
COPY src/Sample.Infrastructure/Sample.Infrastructure.csproj src/Sample.Infrastructure/packages.lock.json src/Sample.Infrastructure/
COPY src/Sample.Api.Contracts/Sample.Api.Contracts.csproj src/Sample.Api.Contracts/packages.lock.json src/Sample.Api.Contracts/
COPY src/Sample.Api/Sample.Api.csproj src/Sample.Api/packages.lock.json src/Sample.Api/
RUN dotnet restore src/Sample.Api/Sample.Api.csproj --locked-mode

COPY src/ src/
ARG VERSION=0.0.0-local
RUN dotnet publish src/Sample.Api/Sample.Api.csproj \
      --configuration Release --no-restore --output /app \
      -p:InformationalVersion=$VERSION -p:UseAppHost=false

# Chiseled Ubuntu: no shell, no package manager, non-root user (uid 1654) by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:7c625e44e5d588b4d7b2a913e94ca4bd5098ce47442da404829e4c7936a764e1 AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="sample-api" \
      org.opencontainers.image.description="Absence request API used to demonstrate the preview platform" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0
EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "Sample.Api.dll"]
