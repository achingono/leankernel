# Risks

| Risk | Mitigation | Status |
| --- | --- | --- |
| Streaming errors or client abort misrepresented as successful completion | Emit marker only after terminal assistant finish and clean framed EOF; suppress on errors; enforce a body timeout and propagate cancellation. | Closed |
| Proxy leaks upstream connections or breaks JSON or tool filtering | Dispose response and timeout after copying; structurally filter only SSE; regression tests cover both modes and mixed events. | Closed |
| Swarm update disrupts identity or other services | Use a unique gateway image tag, preserve other service images, capture the previous image and verify rollout and identity. | Closed |
| Test/model backend or SonarQube unavailable | Capture precise evidence and disclose unmet checks rather than claiming completion. | Closed |
| First unauthenticated guest request resolves a separate session on the internal hop | Guest history may not attach to the outer session; Cumbersome bearer bindings are unaffected. In-process redesign was independently reviewed as a separate change. | Open |
| Direct HTTPS listener requires a trusted localhost certificate; HTTP redirect of the internal hop is unsupported | Current Compose and Swarm listeners are HTTP behind their respective ingress; document the deployment limitation. | Open |
