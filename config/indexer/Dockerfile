FROM python:3.12-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY indexer.py .

# Create data directories
RUN mkdir -p /app/data/wiki /app/data/documents

CMD ["python", "-u", "indexer.py"]
