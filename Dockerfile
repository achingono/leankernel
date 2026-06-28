# Dockerfile — LeanKernel Engine

# ── source: copy solution + csproj for restore caching ─────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS source
WORKDIR /src

COPY src/LeanKernel.sln .
COPY src/Directory.Build.props .
COPY src/LeanKernel.Abstractions/LeanKernel.Abstractions.csproj LeanKernel.Abstractions/
COPY src/LeanKernel.Agents/LeanKernel.Agents.csproj LeanKernel.Agents/
COPY src/LeanKernel.Context/LeanKernel.Context.csproj LeanKernel.Context/
COPY src/LeanKernel.Knowledge/LeanKernel.Knowledge.csproj LeanKernel.Knowledge/
COPY src/LeanKernel.Tools/LeanKernel.Tools.csproj LeanKernel.Tools/
COPY src/LeanKernel.Persistence/LeanKernel.Persistence.csproj LeanKernel.Persistence/
COPY src/LeanKernel.Scheduler/LeanKernel.Scheduler.csproj LeanKernel.Scheduler/
COPY src/LeanKernel.Diagnostics/LeanKernel.Diagnostics.csproj LeanKernel.Diagnostics/
COPY src/LeanKernel.Gateway/LeanKernel.Gateway.csproj LeanKernel.Gateway/
COPY src/LeanKernel.Channels/LeanKernel.Channels.csproj LeanKernel.Channels/
COPY src/LeanKernel.Plugins/LeanKernel.Plugins.csproj LeanKernel.Plugins/
COPY src/LeanKernel.Learning/LeanKernel.Learning.csproj LeanKernel.Learning/
COPY test/LeanKernel.Tests.Unit/LeanKernel.Tests.Unit.csproj ../test/LeanKernel.Tests.Unit/
COPY test/LeanKernel.Tests.Integration/LeanKernel.Tests.Integration.csproj ../test/LeanKernel.Tests.Integration/
COPY test/LeanKernel.Tests.Playwright/LeanKernel.Tests.Playwright.csproj ../test/LeanKernel.Tests.Playwright/

# ── watch: install deps and run dotnet watch ────────────────
FROM source AS watch
WORKDIR /src

# Install OS deps needed by the runtime image (OCR-backed extraction)
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 python3-pip python3-venv poppler-utils \
    && python3 -m venv /opt/ocr-venv \
    && /opt/ocr-venv/bin/pip install --no-cache-dir paddlepaddle paddleocr pdf2image \
    && rm -rf /var/lib/apt/lists/*

ENV LeanKernel__FileSystem__PythonExecutable=/opt/ocr-venv/bin/python

# DOTNET_USE_POLLING_FILE_WATCHER is intentionally NOT set.
# Native file system events work reliably with modern Docker for Mac
# and avoid false-positive restarts caused by polling on volume mounts.
ENV DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER=1

RUN useradd -m -s /bin/bash leankernel \
    && mkdir -p /home/leankernel/.local/share/signal-cli \
    && chown -R leankernel:leankernel /home/leankernel

USER leankernel

ENV LEANKERNEL_ENGINE_PORT=5080
ENV ASPNETCORE_URLS=http://+:${LEANKERNEL_ENGINE_PORT}
EXPOSE ${LEANKERNEL_ENGINE_PORT}

ENTRYPOINT dotnet watch --project LeanKernel.Gateway/LeanKernel.Gateway.csproj run --configuration Debug

# ── build: restore and publish ───────────────────────────────
FROM source AS build
WORKDIR /src

COPY src/ ./
COPY test/ ../test/

RUN dotnet restore LeanKernel.sln
RUN dotnet publish LeanKernel.Gateway/LeanKernel.Gateway.csproj -c Release -o /app/publish --no-restore

# ── final: runtime image ─────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl python3 python3-pip python3-venv poppler-utils \
    && python3 -m venv /opt/ocr-venv \
    && /opt/ocr-venv/bin/pip install --no-cache-dir paddlepaddle paddleocr pdf2image \
    && rm -rf /var/lib/apt/lists/*

ENV LeanKernel__FileSystem__PythonExecutable=/opt/ocr-venv/bin/python

COPY --from=build /app/publish .

RUN useradd -m -s /bin/bash leankernel \
    && mkdir -p /app/data/documents /app/data/managed-documents \
    && mkdir -p /home/leankernel/.ms-todo-cli \
    && mkdir -p /home/leankernel/.simplefin-cli \
    && mkdir -p /home/leankernel/.local/share/signal-cli \
    && chown -R leankernel:leankernel /app \
    && chown -R leankernel:leankernel /home/leankernel/.ms-todo-cli \
    && chown -R leankernel:leankernel /home/leankernel/.simplefin-cli \
    && chown -R leankernel:leankernel /home/leankernel/.local
USER leankernel

ENV LEANKERNEL_ENGINE_PORT=5080
ENV ASPNETCORE_URLS=http://+:${LEANKERNEL_ENGINE_PORT}
EXPOSE ${LEANKERNEL_ENGINE_PORT}

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 CMD curl -f http://localhost:${LEANKERNEL_ENGINE_PORT}/api/health || exit 1

ENTRYPOINT ["dotnet", "LeanKernel.Gateway.dll"]
