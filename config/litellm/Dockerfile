FROM ghcr.io/berriai/litellm:main-latest

# Install additional dependencies if needed
RUN pip install --no-cache-dir psycopg2-binary redis httpx pyyaml

ENV PYTHONPATH=/app

# Copy source spec + compiler
COPY config/litellm/config.yaml /app/litellm_spec.yaml
COPY config/litellm/render_litellm_config.py /app/render_litellm_config.py
COPY config/litellm/leankernel_litellm_callbacks.py /app/leankernel_litellm_callbacks.py
COPY scripts/sync_litellm_model_limits.py /app/sync_litellm_model_limits.py

RUN mkdir -p /app/logs

# Set working directory
WORKDIR /app

# Expose port
EXPOSE 4000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD python3 -c "import sys,urllib.request; sys.exit(0 if urllib.request.urlopen('http://localhost:4000/health/liveliness').getcode() == 200 else 1)"

# Compile config from source spec and environment, then start LiteLLM proxy
ENTRYPOINT ["/bin/sh", "-c"]
CMD ["python3 /app/render_litellm_config.py /app/litellm_spec.yaml /app/litellm_config.yaml && exec litellm --config /app/litellm_config.yaml --port 4000 --num_workers ${LITELLM_NUM_WORKERS:-2}"]
