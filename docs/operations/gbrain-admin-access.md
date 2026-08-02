# GBrain Admin Dashboard Access

The GBrain admin dashboard at `http://localhost:8789/admin/#login` is protected by a **bootstrap token** that is generated at server startup.

## The Problem

If `GBRAIN_ADMIN_BOOTSTRAP_TOKEN` is **not set**, the server generates a random bootstrap token at startup. However, due to a **non-TTY log-leak guard**, this token is **hidden from container logs** with the message:

```
Admin Token: hidden (non-TTY log-leak guard)
```

This means you cannot retrieve the token from `docker logs` or `docker exec` after the container starts.

## Solution: Set `GBRAIN_ADMIN_BOOTSTRAP_TOKEN`

Set this environment variable to a known value **before starting the container**:

```yaml
# docker-compose.yml
services:
  gbrain:
    environment:
      GBRAIN_ADMIN_BOOTSTRAP_TOKEN: "your-chosen-secure-token-here"
```

Then use `your-chosen-secure-token-here` in the admin login form at `http://localhost:8789/admin/#login`.

## Why the Engine Token Doesn't Work

The file `/app/data/gbrain/.engine-token` contains the **engine token** (for GBrain engine ↔ gateway communication), not the admin bootstrap token. They are different tokens for different purposes:

| Token | Purpose | Location |
|-------|---------|----------|
| Engine token | GBrain engine ↔ gateway auth | `/app/data/gbrain/.engine-token` |
| Admin bootstrap token | Admin dashboard login | `GBRAIN_ADMIN_BOOTSTRAP_TOKEN` env var |

Using the engine token in the admin login form will always fail with "Invalid token."

## Retrieving a Running Server's Token (Not Recommended)

If you cannot restart, you can attempt to start a **temporary second instance** on a different port:

```bash
docker exec -t leankernel-gbrain gbrain serve --http --print-admin-token --port 3131 --bind 127.0.0.1
```

This prints the token for the **new instance** (port 3131), not the running one (port 8789). This is a workaround, not a fix.

## Best Practice

Always set `GBRAIN_ADMIN_BOOTSTRAP_TOKEN` in your deployment configuration (docker-compose, Kubernetes, etc.) to a strong, randomly generated value. Treat it like a password.