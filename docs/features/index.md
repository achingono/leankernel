# Features

This section contains product requirements documents (PRDs) and feature design specifications for LeanKernel.

## Contents

| Document | Description |
|----------|-------------|
| [authentication.md](authentication.md) | Authentication and authorization model: local passcode, OIDC, API tokens, endpoint policies, and migration path. |
| [intelligent-model-routing.md](intelligent-model-routing.md) | Intelligent cost-quality model routing: task complexity scoring, free-first policy, quality gates, and spend guardrails. |

## Feature Status

```mermaid
quadrantChart
    title Feature Completeness vs Priority
    x-axis Low Priority --> High Priority
    y-axis Not Started --> Complete
    quadrant-1 Ship Soon
    quadrant-2 Maintain
    quadrant-3 Deprioritize
    quadrant-4 Invest
    Authentication: [0.8, 0.6]
    Model Routing: [0.9, 0.5]
```
