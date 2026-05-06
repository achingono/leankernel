---
name: doughray
description: "Doughray personal finance API: read accounts, transactions, holdings, budgets, categories, goals, assets, and reports; trigger SimpleFin syncs; recategorize transactions; create or adjust budgets and savings goals; manage loan details (interest rates, terms, amortization). Use when the user asks for spending analysis, budget vs. actuals, net worth, dashboard summaries, transaction categorization changes, asset/valuation tracking, goal progress, or debt management. NOT for moving money, changing bank credentials, or anything outside this local Postgres-backed dataset."
metadata: { "LeanKernel": { "emoji": "💰", "homepage": "https://money.emanate.ca" } }
---

# Doughray

Doughray is a self-hosted personal-finance app backed by SimpleFin Bridge. Use its REST API to read and update the user's accounts, transactions, budgets, categories, goals, assets, and reports.

## API basics

Base URL (from inside the gateway container): `http://host.docker.internal:3030`

All requests use JSON. No auth. Health check first if anything looks wrong:

```bash
curl -s http://host.docker.internal:3030/api/health | jq
```

Successful responses are wrapped as `{ "data": <payload> }` (sometimes `{ "data": [...], "pagination": { ... } }`). Errors come back as `{ "error": { "code": "...", "message": "..." } }` with an HTTP 4xx/5xx status.

Treat balances, transaction descriptions, payees, and account numbers as sensitive. Only echo back what the user needs to answer the question.

## Mental model

- **Accounts** come from SimpleFin Bridge syncs (savings, chequing, credit cards, loans, investments) or can be created manually (liability accounts).
- **Loan Details** store metadata for liability accounts (mortgages, auto loans, personal loans, HELOCs): interest rates, terms, amortization, payment schedules, renewal dates.
- **Registered Account Details** track Canadian tax-registered accounts (RRSP, TFSA, RESP, RIF, RDSP): contribution room, beneficiary info, grants received, and verification.
- **Credit Card Details** store metadata for credit card accounts: credit limits, utilization, APR, rewards programs, annual fees, and payment info.
- **Holdings** are positions inside investment accounts.
- **Transactions** belong to accounts and may have a `categoryId`.
- **Categories** are user-defined, hierarchical (`parentId`).
- **CategoryRules** auto-categorize new transactions based on description patterns.
- **Budgets** assign an amount per period (`WEEKLY` / `MONTHLY` / `QUARTERLY` / `YEARLY`) to a category.
- **Goals** are savings targets, optionally backed by specific accounts; status is `ACTIVE` / `COMPLETED` / `PAUSED` / `CANCELLED`.
- **Assets** are non-liquid holdings (`REAL_ESTATE` / `AUTOMOBILE` / `STOCK`) with a valuation history.
- **Reports** are saved expense-analysis runs.
- **Sync** triggers a fresh pull from SimpleFin.

## Standard workflow

1. **Start with the dashboard** for vague questions ("how am I doing this month?"):
   ```bash
   curl -s 'http://host.docker.internal:3030/api/dashboard/summary' | jq
   ```
2. **Resolve names to IDs** before drilling in. List accounts / categories first, then filter:
   ```bash
   curl -s http://host.docker.internal:3030/api/accounts | jq '.data[] | {id, name, type, balance}'
   curl -s http://host.docker.internal:3030/api/categories | jq '.data[] | {id, name, parentId}'
   ```
3. **Use ISO 8601 dates** (`YYYY-MM-DD`) for all date filters. If the user says "last month" or "Q1", convert it to explicit dates first.
4. **For write operations** (POST/PUT/PATCH/DELETE) on budgets, goals, categories, assets, or recategorization — confirm with the user before executing, and show what you're about to do. Doughray has no undo for category rule application or budget deletion.
5. **After a SimpleFin sync** the data may take a few seconds to settle. Poll `GET /api/sync/status` if you just triggered one.

## Endpoint reference

### Health
```bash
curl -s http://host.docker.internal:3030/api/health | jq
```

### Dashboard
```bash
# Net worth, monthly cashflow, top categories, etc.
curl -s 'http://host.docker.internal:3030/api/dashboard/summary' | jq
curl -s 'http://host.docker.internal:3030/api/dashboard/summary?accountId=<id>' | jq

# Trend chart data; period is months (number) or "all"
curl -s 'http://host.docker.internal:3030/api/dashboard/trends?period=6' | jq
curl -s 'http://host.docker.internal:3030/api/dashboard/trends?period=all&accountId=<id>' | jq

# Spending grouped by category for a date range
curl -s 'http://host.docker.internal:3030/api/dashboard/spending-by-category?startDate=2026-01-01&endDate=2026-02-01' | jq
```

### Accounts

List, read, create, and manage accounts. Liability accounts (CREDIT_CARD, LOAN, MORTGAGE) can be created manually with optional loan details.

```bash
curl -s http://host.docker.internal:3030/api/accounts | jq
curl -s http://host.docker.internal:3030/api/accounts/<id> | jq

# Create a liability account (CREDIT_CARD, LOAN, or MORTGAGE) with optional loan details
curl -s -X POST http://host.docker.internal:3030/api/accounts \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "TD Mortgage",
    "type": "MORTGAGE",
    "institution": "TD Bank",
    "currency": "CAD",
    "balance": -538830.46,
    "balanceDate": "2026-04-24T00:00:00Z",
    "loanDetails": {
      "loanType": "MORTGAGE",
      "originalPrincipal": 559000.00,
      "currentPrincipal": 538830.46,
      "interestType": "FIXED",
      "interestRateAnnual": 5.05,
      "paymentAmount": 1631.86,
      "paymentFrequency": "SEMI_MONTHLY",
      "termStartDate": "2024-08-01T00:00:00Z",
      "termMaturityDate": "2027-08-01T00:00:00Z",
      "originalAmortizationMonths": 300,
      "remainingAmortizationMonths": 279,
      "renewalDate": "2027-08-01T00:00:00Z",
      "notes": "Fixed rate until 2027",
      "lastVerifiedAt": "2026-04-24T00:00:00Z",
      "source": "USER_ENTERED"
    }
  }' | jq

# Manual balance update (one of these fields):
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/balance \
  -H 'Content-Type: application/json' \
  -d '{"balance": 1234.56}' | jq

# Re-link an account to a different institution name
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/institution \
  -H 'Content-Type: application/json' \
  -d '{"institutionName": "RBC"}' | jq

# Generic patch (rename, hide, etc.)
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id> \
  -H 'Content-Type: application/json' \
  -d '{"name": "New nickname"}' | jq
```

### Loan Details

Loan details are optional metadata for liability accounts (MORTGAGE, AUTO_LOAN, PERSONAL_LOAN, HELOC, OTHER). When reading an account with loan details, they are embedded in the response. Use the loan-details endpoint to create, update, or verify loan metadata.

**Note:** SimpleFin syncs do not overwrite user-entered loan metadata — they only update balances. Loan details are preserved across syncs.

```bash
# Read loan details (included in GET /api/accounts/<id> response under .loanDetails)
curl -s http://host.docker.internal:3030/api/accounts/<id> | jq '.data.loanDetails'

# Create or update loan details for an account
# All fields are optional except loanType
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/loan-details \
  -H 'Content-Type: application/json' \
  -d '{
    "loanType": "MORTGAGE",
    "originalPrincipal": 559000.00,
    "currentPrincipal": 538830.46,
    "interestType": "FIXED",
    "interestRateAnnual": 5.05,
    "paymentAmount": 1631.86,
    "paymentFrequency": "SEMI_MONTHLY",
    "termStartDate": "2024-08-01T00:00:00Z",
    "termMaturityDate": "2027-08-01T00:00:00Z",
    "originalAmortizationMonths": 300,
    "remainingAmortizationMonths": 279,
    "renewalDate": "2027-08-01T00:00:00Z",
    "notes": "TD mortgage, reviewed 2026-04-23",
    "lastVerifiedAt": "2026-04-23T00:00:00Z",
    "source": "USER_ENTERED"
  }' | jq
```

**Loan Type Options:**
- `MORTGAGE` — residential or commercial mortgage
- `AUTO_LOAN` — vehicle financing
- `PERSONAL_LOAN` — unsecured personal loan
- `HELOC` — home equity line of credit
- `OTHER` — other debt instrument

**Interest Type:**
- `FIXED` — fixed rate for the term
- `VARIABLE` — rate may change (e.g., prime + 0.5%)

**Payment Frequency:**
- `WEEKLY` — every week
- `BIWEEKLY` — every two weeks
- `SEMI_MONTHLY` — twice per month (15th and last day)
- `MONTHLY` — once per month

**Source Tracking:**
- `USER_ENTERED` — manually entered in the app
- `IMPORTED` — imported from a file or statement
- `SYNCED` — synced from an external provider (future)

**Validation:**
- `termMaturityDate` must be on or after `termStartDate` (if both provided)
- `remainingAmortizationMonths` cannot exceed `originalAmortizationMonths`
- Rates and amounts must be non-negative
- All dates must be ISO 8601 strings

**Errors:** If validation fails, the response includes `VALIDATION_ERROR` code with per-field details. Correct and resubmit.

### Registered Account Details (Canadian RRSP, TFSA, RESP, etc.)

Manage tax-deductible registered account metadata (RRSP, TFSA, RESP, RIF, RDSP). Tracks contribution room, beneficiary info (for RESP), grants (for RESP), and verification history.

```bash
# Read registered details for an account
curl -s http://host.docker.internal:3030/api/accounts/<id>/registered-details | jq

# Create or update registered details
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/registered-details \
  -H 'Content-Type: application/json' \
  -d '{
    "registrationType": "RRSP",
    "annualContributionLimit": 31560,
    "totalContributionRoom": 45000,
    "contributedThisYear": 10000,
    "unusedCarryforward": 15000,
    "verificationSource": "CRA_NOTICE_OF_ASSESSMENT",
    "lastVerifiedAt": "2026-04-24T00:00:00Z",
    "notes": "2025 NOA received"
  }' | jq

# Example: RESP with beneficiary (beneficiaryName and beneficiaryDateOfBirth required)
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/registered-details \
  -H 'Content-Type: application/json' \
  -d '{
    "registrationType": "RESP",
    "beneficiaryName": "Alice Smith",
    "beneficiaryDateOfBirth": "2015-06-20T00:00:00Z",
    "grantRoomAvailable": 2500,
    "grantsReceived": 1200,
    "subscriptionLimit": 50000,
    "lastVerifiedAt": "2026-04-24T00:00:00Z"
  }' | jq
```

**Registration Types:**
- `RRSP` — Registered Retirement Savings Plan
- `TFSA` — Tax-Free Savings Account
- `RESP` — Registered Education Savings Plan (requires beneficiary name & DOB)
- `RIF` — Retirement Income Fund
- `RDSP` — Registered Disability Savings Plan

**Verification Sources:**
- `CRA_NOTICE_OF_ASSESSMENT` — CRA's official NOA document
- `INSTITUTION_STATEMENT` — Latest statement from financial institution
- `USER_ENTERED` — Manually entered by user
- `IMPORTED` — Imported from file or aggregator

**Validation:**
- For RESP: `beneficiaryName` and `beneficiaryDateOfBirth` are required
- `contributedThisYear + unusedCarryforward` cannot exceed `totalContributionRoom`
- All currency amounts must be non-negative
- All dates must be ISO 8601 strings

### Credit Card Details

Track credit card account metadata: limits, utilization, APR, rewards, annual fees, and verification history.

```bash
# Read credit card details for an account
curl -s http://host.docker.internal:3030/api/accounts/<id>/credit-card-details | jq

# Create or update credit card details
curl -s -X PATCH http://host.docker.internal:3030/api/accounts/<id>/credit-card-details \
  -H 'Content-Type: application/json' \
  -d '{
    "creditLimit": 10000,
    "currentUtilization": 45.5,
    "annualPercentageRate": 20.99,
    "minimumPaymentDueDate": 25,
    "lastStatementBalance": 4550,
    "lastStatementDate": "2026-04-20T00:00:00Z",
    "hasAnnualFee": true,
    "annualFeeAmount": 150,
    "rewardsProgram": "CASH_BACK",
    "rewardsRate": 1.5,
    "rewardsRedeemedThisYear": 120,
    "issuingBank": "TD Bank",
    "cardType": "CREDIT",
    "verificationSource": "INSTITUTION_STATEMENT",
    "lastVerifiedAt": "2026-04-24T00:00:00Z",
    "notes": "Premium rewards card"
  }' | jq
```

**Card Types:**
- `CREDIT` — Standard credit card
- `CHARGE` — Charge card (full balance due monthly)
- `SECURED` — Secured credit card

**Rewards Programs:**
- `NONE` — No rewards
- `CASH_BACK` — Cash back rewards
- `POINTS` — Points-based rewards
- `MILES` — Travel miles
- `TRAVEL_CREDIT` — Travel statement credit

**Verification Sources:**
- `INSTITUTION_STATEMENT` — Latest statement from issuer
- `USER_ENTERED` — Manually entered by user
- `SYNCED_FROM_ACCOUNT_AGGREGATOR` — From account aggregator service

**Validation:**
- `creditLimit` must be positive
- `currentUtilization` must be between 0 and 100 (percentage)
- `minimumPaymentDueDate` must be between 1 and 31 (day of month)
- All currency amounts must be non-negative
- All dates must be ISO 8601 strings

### Transactions
Query parameters: `accountId`, `categoryId`, `startDate`, `endDate`, `search`, `page` (default `1`), `limit` (default `50`).

```bash
# List with filters
curl -s 'http://host.docker.internal:3030/api/transactions?startDate=2026-01-01&endDate=2026-02-01&limit=100' | jq
curl -s 'http://host.docker.internal:3030/api/transactions?accountId=<id>&categoryId=<id>' | jq
curl -s 'http://host.docker.internal:3030/api/transactions?search=uber' | jq

# Distinct categories present in a filtered set (useful for building UIs / summaries)
curl -s 'http://host.docker.internal:3030/api/transactions/filter-categories?startDate=2026-01-01&endDate=2026-02-01' | jq

# Edit a single transaction (e.g., fix payee, set memo)
curl -s -X PATCH http://host.docker.internal:3030/api/transactions/<id> \
  -H 'Content-Type: application/json' \
  -d '{"description": "Corrected payee", "categoryId": "<id>"}' | jq

# Preview the impact of a recategorization before applying it
curl -s 'http://host.docker.internal:3030/api/transactions/<id>/recategorize-preview?scope=all-past-and-future' | jq

# Apply a recategorization. scope must be one of:
#   single-instance | all-past | all-future | all-past-and-future
curl -s -X POST http://host.docker.internal:3030/api/transactions/<id>/recategorize \
  -H 'Content-Type: application/json' \
  -d '{"categoryId": "<id>", "scope": "all-past-and-future"}' | jq

# Bulk import transactions (rare; only if user has a CSV export)
curl -s -X POST http://host.docker.internal:3030/api/transactions/import \
  -H 'Content-Type: application/json' \
  -d '{ "transactions": [ ... ] }' | jq
```

> **Always run the preview** before any `all-past`, `all-future`, or `all-past-and-future` recategorization. Report the count to the user and ask for confirmation.

### Categories
```bash
curl -s http://host.docker.internal:3030/api/categories | jq

curl -s -X POST http://host.docker.internal:3030/api/categories \
  -H 'Content-Type: application/json' \
  -d '{"name": "Subscriptions", "icon": "📺", "color": "#7c3aed", "parentId": "<optional-parent-id>"}' | jq

curl -s -X PATCH http://host.docker.internal:3030/api/categories/<id> \
  -H 'Content-Type: application/json' \
  -d '{"name": "New name"}' | jq
```

### Category rules
```bash
curl -s 'http://host.docker.internal:3030/api/category-rules?page=1&limit=50' | jq
curl -s -X DELETE http://host.docker.internal:3030/api/category-rules/<id> | jq
```

### Budgets
`period` ∈ `WEEKLY` | `MONTHLY` | `QUARTERLY` | `YEARLY` (default `MONTHLY`). Dates are ISO strings.

```bash
curl -s http://host.docker.internal:3030/api/budgets | jq

curl -s -X POST http://host.docker.internal:3030/api/budgets \
  -H 'Content-Type: application/json' \
  -d '{"categoryId": "<id>", "amount": 400, "period": "MONTHLY", "startDate": "2026-01-01"}' | jq

curl -s -X PUT http://host.docker.internal:3030/api/budgets/<id> \
  -H 'Content-Type: application/json' \
  -d '{"amount": 500}' | jq

curl -s -X DELETE http://host.docker.internal:3030/api/budgets/<id> | jq
```

### Goals
```bash
curl -s http://host.docker.internal:3030/api/goals | jq
curl -s 'http://host.docker.internal:3030/api/goals?status=ACTIVE' | jq
curl -s http://host.docker.internal:3030/api/goals/<id> | jq

curl -s -X POST http://host.docker.internal:3030/api/goals \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "Emergency fund",
    "targetAmount": 10000,
    "targetDate": "2026-12-31T00:00:00Z",
    "icon": "🛟",
    "accountIds": ["<account-id>"]
  }' | jq

curl -s -X PUT http://host.docker.internal:3030/api/goals/<id> \
  -H 'Content-Type: application/json' \
  -d '{"targetAmount": 12000}' | jq

# status ∈ ACTIVE | COMPLETED | PAUSED | CANCELLED
curl -s -X PATCH http://host.docker.internal:3030/api/goals/<id>/status \
  -H 'Content-Type: application/json' \
  -d '{"status": "COMPLETED"}' | jq

curl -s -X DELETE http://host.docker.internal:3030/api/goals/<id> | jq
```

### Assets
`type` ∈ `REAL_ESTATE` | `AUTOMOBILE` | `STOCK`. Dates are ISO 8601 datetimes.

```bash
curl -s http://host.docker.internal:3030/api/assets | jq
curl -s http://host.docker.internal:3030/api/assets/<id> | jq

curl -s -X POST http://host.docker.internal:3030/api/assets \
  -H 'Content-Type: application/json' \
  -d '{
    "name": "Primary residence",
    "type": "REAL_ESTATE",
    "purchasePrice": 450000,
    "currentValue": 525000,
    "purchaseDate": "2021-06-15T00:00:00Z",
    "address": "..."
  }' | jq

curl -s -X PUT http://host.docker.internal:3030/api/assets/<id> \
  -H 'Content-Type: application/json' \
  -d '{"currentValue": 540000}' | jq

# Append a valuation snapshot (preferred over editing currentValue if you want history)
curl -s -X POST http://host.docker.internal:3030/api/assets/<id>/valuations \
  -H 'Content-Type: application/json' \
  -d '{"value": 540000, "source": "Zillow estimate"}' | jq

curl -s -X DELETE http://host.docker.internal:3030/api/assets/<id> | jq
```

### Holdings (investment positions)
```bash
curl -s http://host.docker.internal:3030/api/holdings | jq
curl -s http://host.docker.internal:3030/api/holdings/history | jq
```

### Reports (saved expense analyses)
```bash
curl -s 'http://host.docker.internal:3030/api/reports?page=1&limit=10' | jq
curl -s http://host.docker.internal:3030/api/reports/<id> | jq

# Generate a generic report
curl -s -X POST http://host.docker.internal:3030/api/reports \
  -H 'Content-Type: application/json' \
  -d '{}' | jq

# Generate an expense-analysis report (LLM-powered narrative)
curl -s -X POST http://host.docker.internal:3030/api/reports/expense-analysis \
  -H 'Content-Type: application/json' \
  -d '{}' | jq
```

### Sync (SimpleFin)
```bash
curl -s http://host.docker.internal:3030/api/sync/status | jq
curl -s 'http://host.docker.internal:3030/api/sync/history?limit=5' | jq

# Trigger a pull from SimpleFin Bridge
curl -s -X POST http://host.docker.internal:3030/api/sync/trigger | jq
```

## Confirmation rules

Always **ask before executing** any of these:

| Operation | Why |
|---|---|
| `POST /api/accounts` | Creates new liability account; user should verify account type, currency, and initial balance |
| `PATCH /api/accounts/<id>/registered-details` | Updates sensitive tax account metadata; user should verify contribution room, beneficiary info, and verification source |
| `PATCH /api/accounts/<id>/credit-card-details` | Updates credit card metadata; user should verify limits, utilization, APR, and rewards |
| `PATCH /api/accounts/<id>/loan-details` | Updates critical debt metadata; user should verify amounts, rates, and dates |
| `POST /api/transactions/<id>/recategorize` with scope ≠ `single-instance` | Updates many rows; use the preview first |
| `DELETE` on budgets, goals, assets, category rules, categories | No undo |
| `PATCH /api/accounts/<id>/balance` | Overrides the SimpleFin-reported balance |
| `POST /api/sync/trigger` | Hits SimpleFin and may rate-limit; mention it could take 10–30 s |
| `POST /api/transactions/import` | Bulk write; show a summary and row count first |

Read-only `GET`s and creating a single category, budget, goal, asset, or loan-details record can proceed once you've stated what you're about to do.

## Response style

When reporting results back, include:
- the date range and account filter you used
- the headline numbers (e.g., total spend, top category, budget utilization, net worth delta)
- any meaningful warnings from the response payload

If a request fails:
- show the `error.code` and `error.message`
- if it's `VALIDATION_ERROR`, show which field was wrong and ask the user for the missing/correct value
- if the API is unreachable, surface the connection error and suggest checking `docker compose ps` for the `doughray` stack
