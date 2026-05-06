---
name: simplefin
description: "Inspect SimpleFin Bridge accounts and transactions with simplefin-cli. Use when the user wants balances, transaction history, or account-level cashflow analysis from linked financial institutions. NOT for moving money, changing bank settings, or anything outside the read-only SimpleFin Bridge dataset."
metadata: { "LeanKernel": { "emoji": "💸", "homepage": "https://github.com/achingono/simplefin-cli", "requires": { "bins": ["simplefin-cli"] } } }
---

# SimpleFin Bridge

Use `simplefin-cli` to inspect accounts and transaction history from SimpleFin Bridge.

## Before doing anything

1. Check configuration first:

```bash
simplefin-cli status | jq
```

2. If `configured` is `false`, ask the user for a SimpleFin setup token and exchange it with:

```bash
simplefin-cli setup "<base64-token>" | jq
```

3. Never ask the user to paste the saved access URL. `simplefin-cli status` already masks embedded credentials.
4. If setup fails with `SETUP_INVALID`, the token is malformed or expired. If it fails with `NETWORK_ERROR` or `RATE_LIMITED`, explain that and stop.

## General workflow

1. Start with account discovery when the user names an institution or account loosely:

```bash
simplefin-cli account list | jq
```

2. Resolve account names to IDs before filtering transactions.
3. Use ISO 8601 dates for transaction filters. `--start-date` is inclusive and `--end-date` is exclusive.
4. If the user gives a vague period like "last month" or "this quarter", convert it to explicit ISO 8601 dates before running the command.
5. Treat balances, descriptions, payees, and memos as sensitive financial data; only echo back the details needed to answer the request.

## Common commands

### Check status

```bash
simplefin-cli status | jq
```

### Initial setup

```bash
simplefin-cli setup "<base64-token>" | jq
```

### List accounts

```bash
simplefin-cli account list | jq
# alias:
simplefin-cli accounts | jq
```

Account results include `id`, `name`, `currency`, `balance`, `available-balance`, `balance-date`, and `org`.

### List transactions across all accounts

```bash
simplefin-cli transaction list | jq
# alias:
simplefin-cli transactions | jq
```

### Filter transactions by account

```bash
simplefin-cli transaction list --account-id "ACCOUNT_ID" | jq
```

If the CLI returns `ACCOUNT_NOT_FOUND`, re-list accounts and confirm the correct account.

### Filter transactions by date range

```bash
simplefin-cli transaction list \
  --start-date "2026-01-01" \
  --end-date "2026-02-01" | jq
```

### Filter transactions by account and date range

```bash
simplefin-cli transaction list \
  --account-id "ACCOUNT_ID" \
  --start-date "2026-01-01" \
  --end-date "2026-02-01" | jq
```

Transaction results include the original transaction fields plus `accountId`, `accountName`, and `currency`.

## Response style

When you report SimpleFin results, include:
- the accounts used
- the date range used
- the most relevant balances, totals, or transaction patterns
- any upstream `errors` returned in the JSON payload

If the request is ambiguous about the target account or date range, clarify instead of guessing.
