# Long-Running Tasks, Progress Updates, and Continuation

`LeanKernel` now keeps long-running turns visible and active across multiple model rounds. The runtime can continue an incomplete task automatically, while channels keep the typing indicator alive and surface progress notes to the user.

## What it does

- Keeps the channel typing indicator refreshed during the full turn, including continuation rounds.
- Publishes in-flight progress updates for tool activity, continuation rounds, and heartbeat updates.
- Detects whether the current turn is complete before ending the conversation.
- Automatically prompts the model to continue when the task is still incomplete.

## Runtime flow

1. `ChannelRouter` resolves the session id and starts a typing keepalive.
2. `TurnPipeline` executes the normal persisted turn flow and records execution metadata.
3. `ContinuationTurnPipeline` evaluates the response and, when needed, issues a synthetic continuation turn.
4. `ITurnProgressBroker` carries progress events back to the channel router.
5. The user sees short status messages while the turn is still in flight.

## Configuration

### `LeanKernel:Channels:Typing`

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | Enables typing keepalive refreshes for channel turns. |
| `KeepAliveSeconds` | `8` | Refresh interval used while a turn is in flight. |
| `StopTimeoutSeconds` | `5` | Cleanup timeout used when stopping the typing indicator. |

### `LeanKernel:Continuation`

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | Enables automatic continuation for incomplete turns. |
| `MaxAutoContinuations` | `3` | Maximum synthetic continuation rounds per turn. |
| `MaxTotalDurationSeconds` | `600` | Maximum wall-clock duration for one turn. |
| `UseClassifier` | `false` | Enables the classifier fallback for ambiguous completion checks. |
| `ContinuePhrases` | empty | Optional extra completion heuristics. |

### `LeanKernel:Continuation:Progress`

| Key | Default | Purpose |
| --- | --- | --- |
| `Enabled` | `true` | Enables progress updates during in-flight work. |
| `InitialSilenceSeconds` | `20` | Quiet period before the first progress message. |
| `MinIntervalSeconds` | `45` | Minimum time between progress messages. |
| `HeartbeatSeconds` | `90` | Fallback interval used when no broker events arrive. |

## Related documentation

- [Channel Routing and Signal Integration](channel-routing.md)
- [Turn Pipeline](turn-pipeline.md)
- [Configuration Reference](../configuration/configuration-reference.md)
