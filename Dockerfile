# ── Build stage ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files first for layer caching
COPY src/LeanKernel.Core/LeanKernel.Core.csproj LeanKernel.Core/
COPY src/LeanKernel.Commander/LeanKernel.Commander.csproj LeanKernel.Commander/
COPY src/LeanKernel.Thinker/LeanKernel.Thinker.csproj LeanKernel.Thinker/
COPY src/LeanKernel.Archivist/LeanKernel.Archivist.csproj LeanKernel.Archivist/
COPY src/LeanKernel.Scheduler/LeanKernel.Scheduler.csproj LeanKernel.Scheduler/
COPY src/LeanKernel.Plugins/LeanKernel.Plugins.csproj LeanKernel.Plugins/
COPY src/LeanKernel.Generators/LeanKernel.Generators.csproj LeanKernel.Generators/
COPY src/LeanKernel.Host/LeanKernel.Host.csproj LeanKernel.Host/
RUN dotnet restore LeanKernel.Host/LeanKernel.Host.csproj

# Copy everything and build
COPY src/ .
RUN dotnet publish LeanKernel.Host/LeanKernel.Host.csproj \
    -c Release \
    -o /app/publish

# ── Runtime stage ────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install runtime dependencies + signal-cli (native package)
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates gpg adduser && \
    mkdir -p /etc/apt/keyrings && \
    curl -fsSL https://packaging.gitlab.io/signal-cli/gpg.key \
      | gpg --dearmor -o /etc/apt/keyrings/signal-cli.gpg && \
    echo "deb [signed-by=/etc/apt/keyrings/signal-cli.gpg] https://packaging.gitlab.io/signal-cli signalcli main" \
      > /etc/apt/sources.list.d/signal-cli.list && \
    apt-get update && \
    apt-get install -y --no-install-recommends signal-cli-native && \
    signal-cli --version && \
    rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN useradd -m -s /bin/bash LeanKernel
USER LeanKernel

COPY --from=build /app/publish .

# Create data directories
RUN mkdir -p /home/LeanKernel/data/wiki /home/LeanKernel/data/sessions /home/LeanKernel/data/logs

ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

HEALTHCHECK --interval=30s --timeout=5s --retries=3 --start-period=15s \
    CMD curl -f http://localhost:5080/api/health || exit 1

ENTRYPOINT ["dotnet", "LeanKernel.Host.dll"]
