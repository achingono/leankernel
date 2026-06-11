FROM mcr.microsoft.com/playwright/python:v1.49.1-jammy

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONPATH=/app

WORKDIR /app

COPY config/browser-service/requirements.txt /app/requirements.txt

# Pre-install CPU-only PyTorch to prevent webwright's transitive dependencies
# from pulling in large NVIDIA GPU packages (torch, nvidia-cuda-*, etc.).
# This significantly reduces image size and avoids CI disk-space exhaustion.
RUN pip install --no-cache-dir torch==2.10.0+cpu torchvision==0.25.0+cpu \
    --index-url https://download.pytorch.org/whl/cpu

RUN pip install --no-cache-dir -r /app/requirements.txt \
    && python -m playwright install firefox

COPY config/browser-service/app /app/app
COPY config/browser-service/config /app/config

RUN useradd --create-home --uid 10001 browser \
    && mkdir -p /app/outputs \
    && chown -R browser:browser /app

USER browser

EXPOSE 8000

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "8000"]
