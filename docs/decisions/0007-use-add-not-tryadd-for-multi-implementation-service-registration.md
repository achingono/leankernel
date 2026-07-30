# ADR 0007: Use `Add` not `TryAdd` for multi-implementation service registration

- Status: Accepted
- Date: 2026-07-29

## Context

EF Core save-change interceptors (`ISaveChangesInterceptor`) are registered as a **multi-implementation service** — multiple concrete types registered against the same service type, all resolved via `sp.GetServices<ISaveChangesInterceptor>()`.

The repository had three interceptors:

- `AuditableInterceptor`
- `RecyclableInterceptor`
- `SenderInterceptor`

All three were registered in `IServiceCollectionExtensions.AddEntityContext`:

```csharp
services.TryAddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
services.TryAddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();
services.TryAddScoped<ISaveChangesInterceptor, SenderInterceptor>();
```

Despite all three appearing in the DI configuration, only the first (`AuditableInterceptor`) was ever resolved by `GetServices`. The `SenderInterceptor` — responsible for generating bearer tokens — never fired, causing new channel sender bindings to be saved with an empty `BearerToken` column.

## Decision

Use `AddScoped` instead of `TryAddScoped` when registering multiple implementations of the same service type:

```csharp
services.AddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
services.AddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();
services.AddScoped<ISaveChangesInterceptor, SenderInterceptor>();
```

## Rationale

The .NET DI `TryAdd` family (`TryAddScoped`, `TryAddSingleton`, `TryAddTransient`) calls `TryAdd`, whose implementation checks whether **any** descriptor with the same `ServiceType` already exists in the collection:

```csharp
public static IServiceCollection TryAdd(this IServiceCollection collection, ServiceDescriptor descriptor)
{
    if (!collection.Any(d => d.ServiceType == descriptor.ServiceType))
    {
        collection.Add(descriptor);
    }
    return collection;
}
```

The check is against `ServiceType` alone — **not** a combination of `ServiceType` and `ImplementationType`. Therefore, after the first `TryAddScoped<ISaveChangesInterceptor, AuditableInterceptor>()` succeeds, every subsequent `TryAddScoped<ISaveChangesInterceptor, …>()` is silently ignored, because `ISaveChangesInterceptor` already has a descriptor.

This is by design. `TryAdd` is intended for the **default-registration** pattern (register a service only if nothing else has already registered it). It is **not** suitable for multi-implementation service registration.

When you need multiple implementations of the same service type, always use `Add*` (`AddScoped`, `AddSingleton`, `AddTransient`).

## Consequences

Positive:

- All interceptors are now resolved and participate in the save-change pipeline.
- New channel sender bindings get a bearer token generated on creation.

Negative:

- If a downstream consumer (e.g. a plugin project) calls `AddEntityContext`, its own interceptors added *before* the call may be overridden rather than merged. However, since the entire interceptor set is defined in one place (`IServiceCollectionExtensions`), this risk is low in practice.
- `AddScoped` means no other caller can suppress a built-in interceptor by using `TryAddScoped` first — they would need to explicitly remove the descriptor.

## Identification Pattern

If a multi-implementation service appears to be registered but only some implementations fire:

1. Check for `TryAdd*` used for the registration.
2. Verify with `sp.GetServices<IServiceType>()` — count the resolved instances.
3. If the count is 1 despite multiple `TryAdd` calls, this is the bug.

## Evidence From Session Logs

- OpenCode session `ses_05062dc27ffepsLooAb5paduHx`, `2026-07-29`:
  - `AddEntityContext` registered 3 interceptors with `TryAddScoped`.
  - `sp.GetServices<ISaveChangesInterceptor>()` returned only 1 instance (`AuditableInterceptor`).
  - Changing to `AddScoped` resulted in 3 instances and correct bearer-token generation.
