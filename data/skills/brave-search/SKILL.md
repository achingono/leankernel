---
name: brave-search
description: "Search the web using Brave Search API for current, high-quality information. Use when you need fresh, accurate search results with privacy-first indexing. Requires BRAVE_SEARCH_API_KEY environment variable."
metadata:
  emoji: "🔍"
  homepage: "https://api.search.brave.com/"
  category: information
  tags: [search, web, information, privacy]
runtime:
  type: cli
  command: brave-search
  auth:
    type: none
  requires:
    bins:
      - name: brave-search
        minVersion: "1.0"
  egress:
    allowHosts:
      - "api.search.brave.com"
operations:
  - id: search
    summary: "Search the web using Brave Search API and return top results."
    invoke:
      argv: []
      flags:
        query: "-q"
        count: "-c"
    parameters:
      type: object
      properties:
        query:
          type: string
          description: "Search query (e.g., 'AI developments 2026', 'best restaurants in Seattle')"
        count:
          type: string
          description: "Number of results to return (default: 5)"
      required: [query]
      additionalProperties: false
---

# Brave Search

Perform web searches using the **Brave Search API** for current, accurate information with privacy-first indexing.

## Setup

1. **Get an API key**: Visit [Brave Search Developer](https://api.search.brave.com/) to obtain your API key
2. **Set environment variable**:
   ```bash
   export BRAVE_SEARCH_API_KEY=your-api-key-here
   ```

## When to Use

- **Current events**: Ask about news, recent developments, or trending topics
- **Research**: Find authoritative sources on topics not in training data
- **Factual lookups**: Get specific information like addresses, phone numbers, or product details
- **Comparisons**: Search for pros/cons of competing products or services

## When NOT to Use

- For general knowledge that doesn't require current data
- For sensitive personal information searches
- For bulk data scraping or analysis

## Examples

**Query**: "What are the latest AI breakthroughs in 2026?"
- Returns: Top 5 web results with titles, descriptions, and URLs

**Query**: "Best TypeScript frameworks 2026"
- Returns: Current information about available frameworks

**Query**: "React vs Vue.js comparison 2026"
- Returns: Recent comparison articles and resources

## Rate Limiting

The Brave Search API has rate limits based on your subscription tier. Monitor API usage to avoid hitting limits.

## Privacy

Brave Search is privacy-focused and does not track users or build profiles, unlike traditional search engines.
