# EF Core Save-Change Interceptors

This document explains how save-change interceptors are registered, resolved, and debugged in this repository.

## Registration

All save-change interceptors are registered in `IServiceCollectionExtensions.AddEntityContext`:

```csharp
services.AddScoped<ISaveChangesInterceptor, AuditableInterceptor>();
services.AddScoped<ISaveChangesInterceptor, RecyclableInterceptor>();
services.AddScoped<ISaveChangesInterceptor, SenderInterceptor>();
```

They are added to the `DbContextOptions` via `sp.GetServices<ISaveChangesInterceptor>()` inside the `AddDbContext` factory, which runs when `EntityContext` is first resolved.

## Current Interceptors

| Interceptor | Responsibility |
|---|---|
| `AuditableInterceptor` | Sets `CreatedOn`/`CreatedBy` on `Added` entities; `UpdatedOn`/`UpdatedBy` on `Modified` entities implementing `IAuditable`. |
| `RecyclableInterceptor` | Sets `IsDeleted = false` on `Added` entities implementing `IRecyclable`. |
| `SenderInterceptor` | Generates a JWT bearer token for `ChannelSenderBindingEntity` on `Added` or `Modified` when `BearerToken` is empty. Loads `User`, `Tenant`, and `Channel` navigations before calling `ISecurityTokenGenerator`. |

## Debugging Interceptors

### Verify an interceptor is registered

Add a temporary diagnostic message to the `AddDbContext` factory in `IServiceCollectionExtensions.cs`:

```csharp
services.AddDbContext<EntityContext>((sp, option) =>
{
    var interceptors = sp.GetServices<ISaveChangesInterceptor>();
    // TEMP: log count and types
    System.Console.Error.WriteLine($"Interceptor count: {interceptors.Count()}");
    option.AddInterceptors(interceptors);
    optionsAction(option);
});
```

Check the output in `docker logs leankernel-gateway` or the `dotnet run` console.

### Verify an interceptor is instantiated

Add a `Console.Error.WriteLine` to the interceptor's constructor:

```csharp
public SenderInterceptor(ISecurityTokenGenerator securityTokenGenerator)
{
    System.Console.Error.WriteLine("SenderInterceptor constructed");
    this.securityTokenGenerator = securityTokenGenerator;
}
```

### Verify an interceptor method is called

Add logging to the `SavingChanges` or `SavingChangesAsync` method. Use string concatenation (`+`) rather than string interpolation (`$""`) to avoid StyleCop `SA1122` issues in verbose debug output.

```csharp
public InterceptionResult<int> SavingChanges(...)
{
    System.Console.Error.WriteLine("SavingChanges called");
    ...
}
```

### Check that all interceptors are resolved

When `sp.GetServices<ISaveChangesInterceptor>()` returns fewer instances than expected, the most likely cause is incorrect use of `TryAdd*` — see [ADR 0007](../decisions/0007-use-add-not-tryadd-for-multi-implementation-service-registration.md).

## Known Registration Traps

- **`TryAdd` with multi-implementation services**: `TryAddScoped<TService, TImpl>()` only registers the **first** implementation. Subsequent calls for the same `TService` are silently ignored. Always use `AddScoped` when registering multiple implementations of the same service type.
- **`AddDbContextFactory` interceptor gap**: The factory-registered options builder may not include interceptors unless explicitly configured. Both `AddDbContext` and `AddDbContextFactory` factories must call `option.AddInterceptors(interceptors)`.
- **Root provider scope**: `DbContextOptions<TContext>` is a singleton, so the `sp` in the factory is the root service provider. Scoped interceptors are captively resolved from the root, which works with `ValidateScopes = false` but means interceptor instances are shared across all requests.
