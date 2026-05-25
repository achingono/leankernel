# Legacy function-call compatibility PRD

- **Status:** Reviewed
- **Audience:** LeanKernel maintainers and implementers
- **Phase goal:** Prevent chat sessions from persisting raw legacy tool-call JSON by translating those payloads into a normal tool execution flow and final assistant response.
- **Plan review:** Reviewed by `claude-sonnet-4.6`. Review outcome: proceed if detection is strict, unknown tools fall back to the original response instead of exposing executor errors, and successful tool execution replays a proper assistant/tool message sequence before re-querying the model.

## Problem statement

Some chat turns return a literal legacy payload such as:

```json
{"type":"function","name":"wiki_write","parameters":{"key":"greeting","content":"Hello!"}}
```

Instead of executing `wiki_write`, the session persists that payload as the assistant message. This makes the conversation unreadable and bypasses the intended wiki write.

## Scope

This task will:

1. Add a narrow compatibility layer in `LeanKernel.Agents` that detects only pure legacy function-call JSON objects.
2. Execute the named tool through the existing `IToolExecutor` when the payload is valid and the tool exists.
3. Re-query the model with a proper assistant `FunctionCallContent` plus tool `FunctionResultContent` message sequence so the persisted assistant turn is a normal final response.
4. Fall back to the original response text when the payload is malformed, the tool is missing, or execution fails.
5. Add focused unit tests for success, malformed passthrough, unknown-tool fallback, and factory wiring.

## Out of scope

- Changing the tool registry or wiki tools themselves.
- Adding a new authorization model.
- Changing gateway chat endpoints or UI rendering.
- Broad JSON parsing of arbitrary assistant output.

## Design notes

- Detection must be strict: only a trimmed JSON object with the exact legacy tool-call shape should be intercepted.
- The compatibility layer should preserve the existing `FunctionInvokingChatClient` path for normal tool calls.
- Unknown tools should not surface executor internals to users.
- If a compatibility replay still fails to produce a normal assistant response, return the tool result rather than the raw JSON payload.

## Validation plan

1. Run `dotnet build src/LeanKernel.sln --no-restore -v minimal`.
2. Run `dotnet test src/LeanKernel.sln --no-build -v minimal`.
