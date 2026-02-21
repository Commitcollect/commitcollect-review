# CommitCollect Documentation Architecture

## 1️⃣ Internal Engineering Documentation

**Location:** `/docs/internal/`  
**Audience:** You + future maintainers  
**Purpose:** Engineering clarity, long-term maintainability, production safety  

Includes:
- DynamoDB patterns
- Guardrails
- Idempotency strategy
- Versioning strategy
- Failure modes
- Concurrency controls
- Observability patterns

### Structure

/docs/internal
  00-system-overview.md
  10-data-model-dynamodb.md
  20-auth-session-model.md
  30-workouts-ingestion.md
  40-milestone-engine.md
  50-award-ledger.md
  90-operations-troubleshooting.md


---

## 2️⃣ Public API Documentation

**Location:** `/docs/public/`  
**Audience:** Frontend, Strava review, external consumers  
**Purpose:** Clear contract. No secrets. Minimal internal exposure.

### Structure

/docs/public
  api-overview.md
  auth.md
  workouts.md
  milestones.md
  models.md
  account.md
  errors.md
  changelog.md


---

# Documentation Templates

## Public Endpoint Template (Fast to Read)

Use this structure for each endpoint in `/docs/public/*.md`

Required sections:
- Route + method
- Purpose
- Authentication
- Query / body
- Response example
- Error codes
- Notes (client-relevant only)

Example:

# GET /milestones/{id}

## Purpose

## Authentication

## Request

## Response

## Error codes

## Notes


---

## Internal Endpoint Template (Engineering-Grade)

Use this for each domain section in `/docs/internal/*.md`

Required sections:
- Route + method
- BFF/session assumptions
- DynamoDB access pattern (PK/SK, begins_with, projection)
- Guardrails
- Idempotency + concurrency
- Failure modes
- Observability notes (what to log, correlation IDs)

Example:

# GET /milestones/{id}

## Behaviour

## Auth model

## DynamoDB access pattern

## Guardrails

## Concurrency / Idempotency

## Failure modes

## Debug fields in response


---

# How We Build It Without Pain

## Phase 1 — Inventory (15 minutes)

List every route currently in production, grouped by domain:

- Auth / session
- Strava connection / status
- Activities / workouts
- Milestones
- Models
- Account / deletion

This is not code review. It is a system map.


## Phase 2 — Document by Domain (Not by Controller)

Controllers change. Domains don’t.

Write:

/docs/public/milestones.md  
/docs/internal/40-milestone-engine.md  

Instead of:

MilestonesController.md


## Phase 3 — Keep Docs Synced

Lightweight rule:

Every time you add an endpoint:
- Add one entry in the relevant public doc
- Add one entry in the relevant internal doc

No large rewrite sessions.  
No documentation debt.  
Small increments. Always synced.
