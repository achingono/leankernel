# Brave Search Skill

A LeanKernel skill that enables web searches using the Brave Search API.

## Structure

- `SKILL.md` - Skill definition with operations and parameters
- `bin/brave-search` - CLI wrapper script that handles API communication

## Setup

1. **Install dependencies**: Ensure `curl` and `jq` are available in your environment
2. **Get API key**: Register at [Brave Search Developer](https://api.search.brave.com/)
3. **Set environment variable**:
   ```bash
   export BRAVE_SEARCH_API_KEY=your-api-key-here
   ```

## How it Works

The skill uses a CLI-based approach:
1. LLM agents invoke the `brave-search:search` operation with a query
2. The DynamicSkillToolFactory executes `/bin/brave-search -q "query" -c count`
3. The bash script calls the Brave Search API with proper authentication
4. Results are returned as JSON to the LLM

## Manual Testing

```bash
export BRAVE_SEARCH_API_KEY=your-api-key
./bin/brave-search -q "latest AI developments" -c 5
```

## Notes

- The script requires `curl` and `jq` to be installed
- API key is read from the `BRAVE_SEARCH_API_KEY` environment variable
- Results include title, description, and URL for each result
- Response timeout is enforced by the skill runtime
