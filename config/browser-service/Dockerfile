FROM mcr.microsoft.com/playwright/python:v1.49.1-jammy

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PYTHONPATH=/app

WORKDIR /app

COPY config/browser-service/requirements.txt /app/requirements.txt
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
