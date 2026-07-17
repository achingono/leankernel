FROM mcr.microsoft.com/playwright:v1.60.0-noble

ENV NODE_ENV=production

WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && npm install -g mcp-proxy@6.5.4

COPY config/webwright/mcp-server /opt/webwright-mcp
RUN cd /opt/webwright-mcp \
    && npm install --omit=dev

USER pwuser

EXPOSE 8000

CMD ["mcp-proxy", "--port", "8000", "--streamEndpoint", "/mcp", "--sseEndpoint", "/sse", "--", "node", "/opt/webwright-mcp/server.mjs"]
