# syntax=docker/dockerfile:1
# Build from the repository root: docker build -f src/Preview.Operator/Dockerfile .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Preview.Operator.Crds/Preview.Operator.Crds.csproj src/Preview.Operator.Crds/packages.lock.json src/Preview.Operator.Crds/
COPY src/Preview.Operator/Preview.Operator.csproj src/Preview.Operator/packages.lock.json src/Preview.Operator/
RUN dotnet restore src/Preview.Operator/Preview.Operator.csproj --locked-mode

COPY src/Preview.Operator.Crds/ src/Preview.Operator.Crds/
COPY src/Preview.Operator/ src/Preview.Operator/
ARG VERSION=0.0.0-local
RUN dotnet publish src/Preview.Operator/Preview.Operator.csproj \
      --configuration Release --no-restore --output /app \
      -p:InformationalVersion=$VERSION -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled@sha256:c89a6a61f494f20a5ae302990e814627a8d91b332fd4a8e7c1707c2d4da9555d AS runtime
ARG VERSION=0.0.0-local
ARG REVISION=unknown
LABEL org.opencontainers.image.title="preview-operator" \
      org.opencontainers.image.description="Kubernetes operator for PreviewEnvironment resources" \
      org.opencontainers.image.source="https://github.com/remi-maigrot/dotnet-preview-platform" \
      org.opencontainers.image.licenses="MIT" \
      org.opencontainers.image.version=$VERSION \
      org.opencontainers.image.revision=$REVISION

WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER 1654
ENTRYPOINT ["dotnet", "Preview.Operator.dll"]
