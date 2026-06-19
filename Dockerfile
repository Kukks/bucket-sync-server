# syntax=docker/dockerfile:1
#
# Multi-stage build for the WalletSync.Api server.
# Runs on a normal host (Postgres is external); this is NOT the deferred Nitro enclave.
#
# Reproducible: base images pinned by digest, SDK pinned via global.json (10.0.301, rollForward
# disable), dependencies pinned via packages.lock.json (--locked-mode), deterministic IL +
# ContinuousIntegrationBuild for path-normalized, byte-stable assemblies.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:548d93f8a18a1acbe6cc127bc4f47281430d34a9e35c18afa80a8d6741c2adc3 AS build
WORKDIR /src

# Restore layer — cached unless the SDK pin, central props, lock files, or any .csproj changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/WalletSync.Api/WalletSync.Api.csproj           src/WalletSync.Api/packages.lock.json           src/WalletSync.Api/
COPY src/WalletSync.Core/WalletSync.Core.csproj         src/WalletSync.Core/packages.lock.json          src/WalletSync.Core/
COPY src/WalletSync.Auth/WalletSync.Auth.csproj         src/WalletSync.Auth/packages.lock.json          src/WalletSync.Auth/
COPY src/WalletSync.Postgres/WalletSync.Postgres.csproj src/WalletSync.Postgres/packages.lock.json      src/WalletSync.Postgres/
COPY src/WalletSync.Cse/WalletSync.Cse.csproj           src/WalletSync.Cse/packages.lock.json           src/WalletSync.Cse/
RUN dotnet restore src/WalletSync.Api/WalletSync.Api.csproj --locked-mode

# Build + publish. ContinuousIntegrationBuild normalizes embedded source paths.
COPY src/ src/
RUN dotnet publish src/WalletSync.Api/WalletSync.Api.csproj \
    -c Release -o /app --no-restore -p:ContinuousIntegrationBuild=true

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:ddcf70ad1ab963a4fcd41fbd722a6b660e404e87567cfbd46fd2809c21b02088 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user the base image already provides (uid 1654).
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Orchestrators should liveness/readiness-probe GET /health (returns 200).
# Default backend is in-memory; set Backend=Postgres + ConnectionStrings__Postgres for production.
ENTRYPOINT ["dotnet", "WalletSync.Api.dll"]
