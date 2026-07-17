FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONPATH=/app

WORKDIR /app

COPY config/webwright/requirements.txt /app/requirements.txt

# Pre-install CPU-only PyTorch to prevent webwright's transitive dependencies
# from pulling in large NVIDIA GPU packages (torch, nvidia-cuda-*, etc.).
RUN pip install --no-cache-dir torch==2.10.0+cpu torchvision==0.25.0+cpu \
    --index-url https://download.pytorch.org/whl/cpu

# Install webwright (browser automation engine) without its broken playsound dep
RUN pip install --no-cache-dir webwright==0.0.7 --no-deps && \
    pip install --no-cache-dir \
        gitpython \
        prompt-toolkit \
        beautifulsoup4 \
        coolname \
        pygments \
        openai \
        tenacity \
        anthropic \
        Halo \
        PyGithub \
        google-generativeai \
        unidiff \
        patch \
        cowsay

# Install remaining Python dependencies
RUN pip install --no-cache-dir -r /app/requirements.txt

# curl for healthchecks
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY config/webwright/app /app/app
COPY config/webwright/config /app/config
COPY config/webwright/mcp_server.py /app/mcp_server.py

RUN useradd --create-home --uid 10001 webwright \
    && mkdir -p /app/outputs \
    && chown -R webwright:webwright /app

USER webwright

EXPOSE 8000

CMD ["python", "/app/mcp_server.py"]
