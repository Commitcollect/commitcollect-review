Documentation layout 1) Internal engineering docs (repo-private or internal folder) Purpose: you + future maintainers. Includes Dynamo patterns, guardrails, idempotency, versioning, failure modes. /docs/internal 00-system-overview.md 10-data-model-dynamodb.md 20-auth-session-model.md 30-workouts-ingestion.md 40-milestone-engine.md 50-award-ledger.md 90-operations-troubleshooting.md 2) Public API docs (safe to share) Purpose: frontend + Strava review + external consumers. No secrets, minimal internals, but clear contract. /docs/public api-overview.md auth.md workouts.md milestones.md models.md account.md errors.md changelog.md Templates Public endpoint template (fast to read) Use this for each endpoint in /docs/public/*.md: Route + method Purpose Auth Query/body Response example Errors Notes (only client-relevant) Example headings: # GET /milestones/{id} ## Purpose ## Authentication ## Request ## Response ## Error codes ## Notes Internal endpoint template (engineering-grade) Use this for each domain section in /docs/internal/*.md: Route + method BFF/session assumptions Dynamo access pattern (PK/SK, begins_with, projection) Guardrails Idempotency + concurrency Failure modes Observability notes (what to log, correlation IDs) Example headings: # GET /milestones/{id} ## Behaviour ## Auth model ## DynamoDB access pattern ## Guardrails ## Concurrency / Idempotency ## Failure modes ## Debug fields in response How we build it without pain Phase 1 — Inventory (15 minutes) We list every route currently in prod, grouped by domain: Auth/session Strava connection/status Activities/workouts Milestones Models Account/deletion This is not code review yet. It’s just a map. Phase 2 — Document by domain (not by controller) Controllers change. Domains don’t. So we write: /docs/public/milestones.md /docs/internal/40-milestone-engine.md …instead of “MilestonesController.md”. Phase 3 — Keep docs synced Lightweight habit: Every time you add an endpoint, you add one entry in the relevant public doc + one in internal doc.


# COMMITCOLLECT_ARCHITECTURE_CONTEXT.md

Version: 1.0  
Last Updated: 20 February 2026  
Environment: Production  
Region: ap-southeast-2 (Sydney)

---

# 1. Project Identity

CommitCollect is a serverless AWS SaaS platform that converts Strava activity data into milestone-based physical 3D statue parts.

Core principle:

Effort → Deterministic aggregation → Threshold detection → Transactional mint → Immutable award ledger

The backend performs all computation.  
The frontend renders deterministic state returned by the API.

---

# 2. Architecture Overview

Frontend:
- Next.js (Vercel)
- BFF pattern (no direct Dynamo access)
- Uses HttpOnly `cc_session` cookie

Backend:
- .NET 8
- AWS Lambda
- API Gateway (HTTP API)
- Cognito Hosted UI (authentication)
- DynamoDB single-table design

Database:
- Table: CommitCollect
- Single-table architecture
- PK/SK patterns only
- No scans allowed

---

# 3. DynamoDB Single-Table Patterns

User partition:

PK = USER#{userId}

  SK = PROFILE
  SK = STRAVA#CONNECTION
  SK = WORKOUT#STRAVA#{activityId}
  SK = MILESTONE#{milestoneId}
  SK = MILESTONE#{milestoneId}#AWARD#{partIndex}

Model definitions (global config):

PK = MODEL#{modelId}

  SK = META
  SK = PART#{partIndex:00}

Strava athlete ownership (existing GSI model):

PK = STRAVA#ATHLETE#{athleteId}
  SK = OWNER

No additional GSIs introduced for MVP.

---

# 4. Non-Negotiable System Rules

- No DynamoDB scans.
- PK-only queries.
- begins_with(SK, ...) allowed.
- ProjectionExpression used for read efficiency.
- MaxWorkoutsToInspect = 3000 (hard guardrail).
- All activity deletes are soft deletes (`isDeleted = true`).
- Awards are never revoked.
- Completion is never reverted.
- Milestone writes + award minting must be transactional.
- Optimistic concurrency enforced via `version`.
- All milestone recomputes must be idempotent.

---

# 5. Session Model

Authentication:
- Cognito Hosted UI
- Backend resolves session via `cc_session` cookie
- `ISessionResolver` required for all authenticated endpoints

The frontend never handles Dynamo identity directly.

---

# 6. Workout Storage Model

SK = WORKOUT#STRAVA#{activityId}

Persisted attributes (required):

- sportType
- startDateUtc (epoch seconds)
- distanceMeters
- total_elevation_gain
- isDeleted (bool)

Soft delete:
- isDeleted = true
- deletedAtUtc set
- item retained for history and recompute correctness

---

# 7. Milestone Engine (MVP)

Supported target types:
- DISTANCE_METERS
- ELEVATION_METERS

Eligibility rules:
- workout.startDateUtc >= milestone.periodStartAtUtc
- workout.sportType matches milestone.sport (normalized)
- workout.isDeleted != true

Aggregation:
- DISTANCE_METERS → sum(distanceMeters)
- ELEVATION_METERS → sum(total_elevation_gain)

Part calculation:
- partTarget = ceil(totalTarget / partsTotal)
- partsEarned = floor(progressValue / partTarget)

Monotonic guarantees:
- partsAwardedCount never decreases
- awards never removed
- COMPLETED status never reverts
- completion timestamp never removed

---

# 8. Award Ledger Model

PK = USER#{userId}  
SK = MILESTONE#{milestoneId}#AWARD#{partIndex:00}

Award attributes:
- milestoneId
- modelId
- partIndex
- partName
- meshFile
- attachPoint
- awardedAtUtc
- progressValueAtAward

Awards copy model metadata at mint time.

Award creation uses:
ConditionExpression: attribute_not_exists(PK) AND attribute_not_exists(SK)

Ensures idempotency under retries.

---

# 9. Transaction Model

Milestone recompute uses TransactWriteItems:

1. Update milestone (progress, version++, status)
2. Put new award rows (if threshold crossed)

Condition:
- version must match expectedVersion

On conflict:
- return 409 milestone_version_conflict

---

# 10. Implemented Endpoints (Production)

Authentication / Session:
- Cognito Hosted UI login
- Session resolution via cookie

Strava:
- GET /strava/status
- DELETE /strava/connection
- Webhook ingestion (create/update/delete activity)

Activities:
- GET /activities/recent

Milestones:
- POST /milestones
- POST /milestones/{id}/recompute
- GET /milestones/{id}

Account:
- DELETE /account

---

# 11. Guardrails

- MaxWorkoutsToInspect = 3000 (per user partition)
- WorkoutsPageSize = 200
- Limit caps on list endpoints (max 50 where applicable)
- No cross-partition scans

If workout count exceeds guardrail:
Return:
{
  "error": "too_many_workouts_for_mvp_recompute",
  "max": 3000
}

---

# 12. Model Definition System

MODEL#{modelId}
  SK = META
  SK = PART#{partIndex}

Model META:
- modelId
- name
- year
- partsTotal
- isActive

Model PART:
- partIndex
- partName
- meshFile
- attachPoint
- displayOrder

Milestone requires explicit modelId at creation.

---

# 13. Current Production State

Milestone Engine v1: Live  
Distance + Elevation supported  
Soft delete implemented  
Award ledger operational  
First production award minted (DRAGON_2026 Part 01)  
Debug-enhanced milestone endpoint active  

System considered stable for MVP.

---

# 14. Architectural Philosophy

Backend performs heavy lifting.  
Frontend renders deterministic state.  
State transitions are monotonic.  
Ledger entries are immutable.  
No hidden recomputation side effects.  
Correctness over premature optimization.  

---

# 15. How to Use This File in New Chats

When starting a new session:

Paste:

"Use COMMITCOLLECT_ARCHITECTURE_CONTEXT.md as authoritative system context.  
Operate in structured engineering mode.  
Preserve all monotonic guarantees and Dynamo guardrails."

This ensures architectural continuity.