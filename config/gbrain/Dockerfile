FROM oven/bun:1-slim

WORKDIR /app

# Install GBrain from GitHub
RUN bun install -g github:garrytan/gbrain

# Create brain directory
RUN mkdir -p /app/data/wiki

# GBrain uses this as its working directory
WORKDIR /app/data/wiki

# Initialize brain (PGLite mode for self-contained)
# Note: actual init happens at first run via entrypoint
EXPOSE 8789

ENTRYPOINT ["sh", "-lc"]
CMD ["gbrain init --pglite --no-embedding && exec gbrain serve --http --port 8789"]
