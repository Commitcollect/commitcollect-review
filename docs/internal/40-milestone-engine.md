# 40 --- Milestone Engine (Aggregation + Threshold + Award Minting)

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Milestone Computation Engine

------------------------------------------------------------------------

## Purpose

This document defines the deterministic milestone computation engine
responsible for:

-   Aggregating athlete activity data
-   Evaluating milestone thresholds
-   Minting immutable award artifacts
-   Enforcing monotonic progress guarantees

The milestone engine is:

-   Deterministic
-   Partition-safe
-   Scan-free
-   Transaction-safe
-   Monotonic (no rollback of earned awards)

------------------------------------------------------------------------

# 1️⃣ High-Level Flow

Activity stored\
→ Aggregation query (USER partition only)\
→ Progress calculation\
→ Threshold detection\
→ TransactWriteItems\
→ Award ledger mint\
→ Updated milestone state

The engine performs no cross-user aggregation and no table scans.

------------------------------------------------------------------------

# 2️⃣ Aggregation Model

### Partition Access

PK = `USER#{userId}`\
begins_with(SK, "WORKOUT#STRAVA#")

No scans permitted.

### Projection-Only Reads

Only required attributes are projected:

-   distanceMeters
-   totalElevationGain
-   sportType
-   startDateUtc
-   isDeleted

------------------------------------------------------------------------

## Aggregation Rules

Workout is eligible if:

-   `startDateUtc >= periodStartAtUtc`
-   `sportType` matches milestone filter (normalized)
-   `isDeleted != true`

Aggregation logic:

-   DISTANCE_METERS → sum(distanceMeters)
-   ELEVATION_METERS → sum(totalElevationGain)

------------------------------------------------------------------------

# 3️⃣ Guardrails

-   MaxWorkoutsToInspect = 3000
-   Pagination enforced
-   Hard cap prevents runaway reads
-   Projection expressions required
-   No cross-partition reads

These guardrails ensure bounded cost and predictable latency.

------------------------------------------------------------------------

# 4️⃣ Milestone State Model

Stored under:

PK = `USER#{userId}`\
SK = `MILESTONE#{milestoneId}`

Fields typically include:

-   progressValue
-   thresholdValue
-   partsTotal
-   partTarget
-   modelId
-   status
-   version (optimistic concurrency)

Milestone progress is monotonic --- it never decreases.

------------------------------------------------------------------------

# 5️⃣ Threshold Detection

Award trigger formula:

floor(progressValue / partTarget)

If computed part index \> existing awards count → mint new award(s).

The engine may mint multiple awards if progress leaps past multiple
thresholds in one update.

------------------------------------------------------------------------

# 6️⃣ Transactional Minting

Uses DynamoDB `TransactWriteItems`:

1.  Conditional update milestone (version check)
2.  Insert award item
3.  Increment milestone version

Award key:

PK = `USER#{userId}`\
SK = `MILESTONE#{milestoneId}#AWARD#{partIndex}`

Guarantees:

-   No duplicate awards
-   No partial minting
-   No race-condition corruption
-   Atomic milestone + award update

------------------------------------------------------------------------

# 7️⃣ Concurrency Model

-   Optimistic concurrency via `version` attribute
-   Conditional check failure → safe retry
-   Idempotent award creation (unique SK ensures no duplicates)
-   No award deletion permitted once minted

------------------------------------------------------------------------

# 8️⃣ Monotonic Guarantees

The system guarantees:

-   Progress never decreases
-   Award records are immutable
-   Completion cannot roll back
-   Deleted workouts do not revoke awards
-   Aggregation excludes `isDeleted == true`

This prevents retroactive state mutation.

------------------------------------------------------------------------

# 9️⃣ 3D Model Integration

Static model definitions stored separately:

PK = `MODEL#{modelId}`\
SK = `META`\
SK = `PART#{index}`

When minting an award:

-   Model part metadata is copied into award record
-   Frontend requires no additional joins

Award contains:

-   partIndex
-   partName
-   meshFile
-   attachPoint
-   progressValueAtAward
-   awardedAtUtc

------------------------------------------------------------------------

# 🔟 Failure Modes

Possible failures:

-   ConditionalCheckFailedException (version mismatch)
-   Throttling (rare in on-demand mode)
-   Transaction cancellation
-   Aggregation guardrail breach

Mitigations:

-   Retry-safe transactions
-   Bounded reads
-   Strict key patterns
-   Idempotent design

------------------------------------------------------------------------

# 1️⃣1️⃣ Observability

Logs should include:

-   userId
-   milestoneId
-   progressValue
-   thresholdValue
-   computedPartIndex
-   existingAwardCount
-   transaction outcome
-   correlationId

Optional debug response fields:

-   inspected count
-   awardsCount
-   hasMore pagination flag

------------------------------------------------------------------------

# 1️⃣2️⃣ Design Principles

-   Deterministic computation
-   No scans
-   Transaction-safe award minting
-   Monotonic state progression
-   Immutable reward ledger
-   User-partition isolation
-   Production-cost predictability

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production snapshot of milestone computation engine
