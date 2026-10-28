# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/Preview.DataSeeder/Dockerfile .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Sample.Domain/Sample.Domain.csproj src/Sample.Domain/packages.lock.json src/Sample.Domain/
COPY src/Sample.Application/Sample.Application.csproj src/Sample.Application/packages.lock.json src/Sample.Application/
COPY src/Sample.Infrastructure/Sample.Infrastructure.csproj src/Sample.Infrastructure/packages.lock.json src/Sample.Infrastructure/
COPY src/Preview.DataSeeder/Preview.DataSeeder.csproj src/Preview.DataSeeder/packages.lock.json src/Preview.DataSeeder/
RUN dotnet restore src/Preview.DataSeeder/Preview.DataSeeder.csproj --locked-mode

COPY src/ src/
ARG VERSION=0.0.0-local
RUN dotnet publish src/Preview.DataSeeder/Preview.DataSeeder.csproj \
      --configuration Release --no-restore --output /app \
      -p:InformationalVersion=$VERSION -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:3eb54ac708855f74b032de48e02437d15fb083b2aa593d49fdd512bb17f3e944 AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="sample-seeder" \
      org.opencontainers.image.description="Seeds preview databases with fictitious or pseudonymised data" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

WORKDIR /app
COPY --from=build /app ./
USER 1654
ENTRYPOINT ["dotnet", "Preview.DataSeeder.dll"]
CMD ["synthetic"]
