# Dockerfile — LeanKernel Engine

# ── source: copy solution + csproj for restore caching ─────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS source
WORKDIR /src

COPY .editorconfig .
COPY src/LeanKernel.sln src/
COPY src/Directory.Build.props src/
COPY src/Common/LeanKernel.Core/LeanKernel.Core.csproj src/Common/LeanKernel.Core/
COPY src/Common/LeanKernel.Data/LeanKernel.Data.csproj src/Common/LeanKernel.Data/
COPY src/Common/LeanKernel.Logic/LeanKernel.Logic.csproj src/Common/LeanKernel.Logic/
COPY src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj src/Services/LeanKernel.Gateway/


# ── watch: install deps and run dotnet watch ────────────────
FROM source AS watch
WORKDIR /src/src

# DOTNET_USE_POLLING_FILE_WATCHER is intentionally NOT set.
# Native file system events work reliably with modern Docker for Mac
# and avoid false-positive restarts caused by polling on volume mounts.
ENV DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1

USER leankernel

ENV LEANKERNEL_GATEWAY_PORT=8080
ENV ASPNETCORE_URLS=http://+:${LEANKERNEL_GATEWAY_PORT}
EXPOSE ${LEANKERNEL_GATEWAY_PORT}

ENTRYPOINT dotnet watch --project Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj run --configuration Debug

# ── build: restore and publish ───────────────────────────────
FROM source AS build
WORKDIR /src

COPY src/ ./src

RUN dotnet restore src/LeanKernel.sln
RUN dotnet publish src/Services/LeanKernel.Gateway/LeanKernel.Gateway.csproj -c Release -o /app/publish --no-restore

# ── final: runtime image ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

RUN useradd -m -s /bin/bash leankernel \
    && chown -R leankernel:leankernel /app

USER leankernel

ENV LEANKERNEL_GATEWAY_PORT=8080
ENV ASPNETCORE_URLS=http://+:${LEANKERNEL_GATEWAY_PORT}
EXPOSE ${LEANKERNEL_GATEWAY_PORT}

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -f http://localhost:${LEANKERNEL_GATEWAY_PORT}/health || exit 1

ENTRYPOINT ["dotnet", "LeanKernel.Gateway.dll"]
