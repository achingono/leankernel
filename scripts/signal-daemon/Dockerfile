FROM debian:bookworm-slim

ARG LEANKERNEL_USER=LeanKernel
ARG LEANKERNEL_UID=1001

# Install runtime dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
        python3 \
        python3-aiohttp \
    && rm -rf /var/lib/apt/lists/*

# Install signal-cli-native from the official packaging repository
RUN mkdir -p /etc/apt/keyrings \
    && curl -fsSL https://packaging.gitlab.io/signal-cli/gpg.key \
       | gpg --dearmor -o /etc/apt/keyrings/signal-cli.gpg \
    && echo "deb [signed-by=/etc/apt/keyrings/signal-cli.gpg] https://packaging.gitlab.io/signal-cli signalcli main" \
       > /etc/apt/sources.list.d/signal-cli.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends signal-cli-native \
    && rm -rf /var/lib/apt/lists/*

# Create a dedicated user that matches the UID used in LeanKernel-engine so that
# the shared signal-config Docker volume has consistent ownership.
RUN useradd -u ${LEANKERNEL_UID} -m -s /bin/bash ${LEANKERNEL_USER}

COPY daemon.py /app/daemon.py

USER ${LEANKERNEL_USER}

EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/v1/health || exit 1

CMD ["python3", "/app/daemon.py"]
