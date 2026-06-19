# syntax=docker/dockerfile:1
#
# Multi-stage build for the WalletSync.Api server.
# Runs on a normal host (Postgres is external); this is NOT the deferred Nitro enclave.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore layer — cached unless the SDK pin, central props, or any .csproj changes.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/WalletSync.Api/WalletSync.Api.csproj            src/WalletSync.Api/
COPY src/WalletSync.Core/WalletSync.Core.csproj          src/WalletSync.Core/
COPY src/WalletSync.Auth/WalletSync.Auth.csproj          src/WalletSync.Auth/
COPY src/WalletSync.Postgres/WalletSync.Postgres.csproj  src/WalletSync.Postgres/
COPY src/WalletSync.Cse/WalletSync.Cse.csproj            src/WalletSync.Cse/
RUN dotnet restore src/WalletSync.Api/WalletSync.Api.csproj

# Build + publish. ContinuousIntegrationBuild normalizes paths for a deterministic output.
COPY src/ src/
RUN dotnet publish src/WalletSync.Api/WalletSync.Api.csproj \
    -c Release -o /app --no-restore -p:ContinuousIntegrationBuild=true

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# Run as the non-root user the base image already provides (uid 1654).
USER $APP_UID
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Orchestrators should liveness/readiness-probe GET /health (returns 200).
# Default backend is in-memory; set Backend=Postgres + ConnectionStrings__Postgres for production.
ENTRYPOINT ["dotnet", "WalletSync.Api.dll"]
