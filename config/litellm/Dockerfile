FROM ghcr.io/berriai/litellm:main-latest

# Install additional dependencies if needed
RUN pip install --no-cache-dir psycopg2-binary redis httpx

# Copy configuration files
COPY config/litellm/config.yaml /app/config.yaml
COPY config/render_litellm_config.py /app/render_litellm_config.py

# Set working directory
WORKDIR /app

# Expose port
EXPOSE 4000

# Health check
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:4000/health || exit 1

# Render config from environment, then start LiteLLM proxy
ENTRYPOINT ["/bin/sh", "-c"]
CMD ["python3 /app/render_litellm_config.py /app/config.yaml /tmp/litellm_config.yaml && exec litellm --config /tmp/litellm_config.yaml --port 4000 --num_workers 4 --detailed_debug"]
