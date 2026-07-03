# PRD: TurnProgressBroker Concurrent Dispatch

## Context
`TurnProgressBroker.PublishAsync` currently awaits subscribers sequentially. A slow subscriber can delay progress fan-out and add avoidable latency to producer paths.

## Problem
Sequential dispatch causes head-of-line blocking across subscribers and can increase turn latency in progress-reporting flows.

## Goals
- Dispatch subscriber handlers concurrently.
- Preserve exception isolation so one failing subscriber does not break publish.
- Keep public broker contract unchanged.

## Reviewed Plan
1. Keep existing subscriber snapshot-under-lock behavior.
2. In `PublishAsync`, return immediately when cancellation is already requested before scheduling.
3. Schedule all snapshot handlers concurrently and await `Task.WhenAll`.
4. Wrap each handler invocation in a safe helper that catches both synchronous and asynchronous exceptions.
5. Keep behavior explicit: ordering is not guaranteed; once scheduled, in-flight handler tasks are awaited.
6. Add tests for concurrent dispatch, pre-cancel behavior, and exception isolation.

## Acceptance Criteria
- `PublishAsync` no longer awaits handlers one-by-one.
- A slow subscriber does not prevent other subscribers from starting promptly.
- Exceptions from one subscriber do not fail publish.
- Targeted unit tests pass for broker and channel progress consumption paths.
