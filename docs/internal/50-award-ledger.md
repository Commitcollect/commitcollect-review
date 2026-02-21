# 50 --- Award Ledger (Immutable Reward Model)

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Award Persistence + 3D Artifact Layer

------------------------------------------------------------------------

## Purpose

This document defines the immutable award ledger model used to persist
milestone rewards in production.

The award ledger is responsible for:

-   Recording earned milestone parts
-   Guaranteeing immutability
-   Preventing duplicate minting
-   Preserving historical integrity
-   Decoupling reward state from aggregation logic

Awards are permanent artifacts.

They are never edited. They are never deleted (except via full account
destruction).

------------------------------------------------------------------------

# 1️⃣ Storage Model

Awards are stored in the primary single-table (`CommitCollect`).

Partition:

PK = `USER#{userId}`

Sort Key:

SK = `MILESTONE#{milestoneId}#AWARD#{partIndex}`

This guarantees:

-   All awards are user-partition isolated
-   Award enumeration is a simple begins_with query
-   No scans required
-   Deterministic ownership boundary

------------------------------------------------------------------------

# 2️⃣ Award Record Structure

Each award record contains:

-   milestoneId
-   partIndex
-   partName
-   meshFile
-   attachPoint
-   progressValueAtAward
-   awardedAtUtc
-   modelId
-   correlationId (optional)

Metadata from model definitions is copied at mint time.

This removes runtime dependency on model joins.

------------------------------------------------------------------------

# 3️⃣ Immutability Guarantees

The system enforces:

-   No updates to award items after creation
-   No deletion of award items
-   No revocation of previously earned parts
-   No modification of progressValueAtAward

Even if workouts are deleted later, awards remain intact.

This ensures:

-   Monotonic reward state
-   Audit-safe history
-   No retroactive mutation

------------------------------------------------------------------------

# 4️⃣ Award Minting Boundary

Award minting occurs only inside a DynamoDB transaction:

TransactWriteItems:

1.  Conditional milestone update (version check)
2.  Put award record
3.  Increment milestone version

If transaction fails:

-   No award is created
-   No partial state is committed

This guarantees atomicity.

------------------------------------------------------------------------

# 5️⃣ Duplicate Protection

Duplicate awards are prevented by:

-   Unique SK pattern
-   Conditional put inside transaction
-   Version-based optimistic concurrency

If the same partIndex is attempted twice, the transaction fails safely.

------------------------------------------------------------------------

# 6️⃣ Award Enumeration

To list all awards for a milestone:

Query:

PK = `USER#{userId}`\
begins_with(SK, "MILESTONE#{milestoneId}#AWARD#")

No scans. No cross-user reads.

------------------------------------------------------------------------

# 7️⃣ Relationship to Milestone State

Milestone item stores:

-   progressValue
-   partsTotal
-   partTarget
-   version
-   status

Award ledger stores earned parts.

The milestone record represents computed state. The award ledger
represents immutable artifacts.

Separation ensures:

-   Safe recomputation
-   Clean projection to frontend
-   Immutable artifact guarantee

------------------------------------------------------------------------

# 8️⃣ Failure Modes

Possible failures:

-   ConditionalCheckFailedException (version mismatch)
-   Transaction cancellation
-   Duplicate mint attempt

Mitigations:

-   Retry-safe logic
-   Idempotent detection
-   Strict key construction
-   Monotonic progression model

------------------------------------------------------------------------

# 9️⃣ Observability

Logs should include:

-   userId
-   milestoneId
-   partIndex
-   transaction outcome
-   version before/after
-   correlationId

Audit events (if enabled):

-   MILESTONE_PART_MINTED

Award minting must be traceable but never block primary flow.

------------------------------------------------------------------------

# 🔟 Design Principles

-   Immutable artifacts
-   Deterministic key construction
-   Transaction-safe minting
-   User-partition isolation
-   No scans
-   Monotonic reward model
-   Separation of computation vs artifact storage

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production snapshot of award ledger model
