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

# Install skill binaries (Tier 1 - image-managed)
# Setup directories
RUN mkdir -p /opt/LeanKernel/tools/ms-todo-cli/0.0.2 && \
    mkdir -p /opt/LeanKernel/tools/simplefin-cli/0.0.2 && \
    mkdir -p /opt/LeanKernel/tools/paddleocr/2.7.0 && \
    mkdir -p /usr/local/bin

# ms-todo-cli v0.0.2
RUN curl -fsSL -o /tmp/ms-todo-cli.tar.gz \
    "https://github.com/achingono/ms-todo-cli/releases/download/v0.0.2/ms-todo-cli-ubuntu-latest.tar.gz" && \
    tar -xzf /tmp/ms-todo-cli.tar.gz -C /opt/LeanKernel/tools/ms-todo-cli/0.0.2 && \
    cd /opt/LeanKernel/tools/ms-todo-cli/0.0.2 && \
    npm ci --omit=dev --omit=optional --no-fund --no-audit && \
    chmod +x /opt/LeanKernel/tools/ms-todo-cli/0.0.2/dist/cli.js && \
    ln -sf /opt/LeanKernel/tools/ms-todo-cli/0.0.2/dist/cli.js /usr/local/bin/ms-todo-cli && \
    rm -f /tmp/ms-todo-cli.tar.gz

# simplefin-cli v0.0.2
RUN curl -fsSL -o /tmp/simplefin-cli.tar.gz \
    "https://github.com/achingono/simplefin-cli/releases/download/v0.0.2/simplefin-cli-ubuntu-latest.tar.gz" && \
    tar -xzf /tmp/simplefin-cli.tar.gz -C /opt/LeanKernel/tools/simplefin-cli/0.0.2 && \
    cd /opt/LeanKernel/tools/simplefin-cli/0.0.2 && \
    npm ci --omit=dev --omit=optional --no-fund --no-audit && \
    chmod +x /opt/LeanKernel/tools/simplefin-cli/0.0.2/dist/cli.js && \
    ln -sf /opt/LeanKernel/tools/simplefin-cli/0.0.2/dist/cli.js /usr/local/bin/simplefin-cli && \
    rm -f /tmp/simplefin-cli.tar.gz

# paddleocr via pip
RUN apt-get update && \
    apt-get install -y --no-install-recommends python3-pip && \
    pip install --no-cache-dir paddleocr==2.7.0 paddlepaddle && \
    rm -rf /var/lib/apt/lists/* && \
    rm -rf ~/.cache/pip

# Create tools manifest
RUN cat > /opt/LeanKernel/tools/tools-manifest.json << 'EOF'
{
  "version": 1,
  "tools": [
    {
      "name": "signal-cli-native",
      "version": "0.13.x",
      "path": "/usr/bin/signal-cli",
      "type": "system"
    },
    {
      "name": "ms-todo-cli",
      "version": "0.0.2",
      "path": "/usr/local/bin/ms-todo-cli",
      "type": "npm"
    },
    {
      "name": "simplefin-cli",
      "version": "0.0.2",
      "path": "/usr/local/bin/simplefin-cli",
      "type": "npm"
    },
    {
      "name": "paddleocr",
      "version": "2.7.0",
      "path": "/usr/local/bin/paddleocr",
      "type": "python",
      "note": "Available as python module; invoke via: python3 -m paddleocr"
    }
  ]
}
EOF

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
