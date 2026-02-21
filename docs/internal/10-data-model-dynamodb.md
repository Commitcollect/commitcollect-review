# 10 --- Data Model: DynamoDB

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Data Layer

------------------------------------------------------------------------

## Purpose

This document defines the DynamoDB schema, access patterns, indexing
strategy, guardrails, and concurrency model used in production.

CommitCollect uses a deterministic, single-table design for domain
entities, supplemented by dedicated tables for sessions, audit, and
idempotency.

No scans are permitted in production request paths.

------------------------------------------------------------------------

# 1️⃣ Primary Table --- CommitCollect

## Table Configuration

-   Billing Mode: On-Demand
-   Region: ap-southeast-2
-   Primary Key:
    -   PK (Partition Key)
    -   SK (Sort Key)

Single-table design is used for all core domain entities.

------------------------------------------------------------------------

## Key Design Strategy

All domain entities are grouped by logical ownership.

### User-Owned Partition

PK = USER#{userId}

Entities under this partition:

-   SK = PROFILE
-   SK = STRAVA#CONNECTION
-   SK = WORKOUT#STRAVA#{activityId}
-   SK = MILESTONE#{milestoneId}
-   SK = MILESTONE#{milestoneId}#AWARD#{partIndex}

This guarantees:

-   Efficient aggregation queries
-   No cross-user scans
-   Deterministic ownership boundaries

------------------------------------------------------------------------

### Global Entities

Used for non-user scoped records:

-   PK = MODEL#{modelId}
    -   SK = META
    -   SK = PART#{index}
-   PK = STRAVA#ATHLETE#{athleteId}
    -   SK = OWNER

These enable:

-   Athlete → user resolution
-   Static model definitions
-   Award metadata copying

------------------------------------------------------------------------

# 2️⃣ Secondary Indexes

## GSI1 --- Athlete Ownership Lookup

Purpose: Resolve Strava athleteId to internal userId.

Access Pattern: Query by STRAVA#ATHLETE#{athleteId}

Guarantee: No scans required during ingestion or OAuth linking.

------------------------------------------------------------------------

# 3️⃣ Sessions Table --- CommitCollectSessions

## Configuration

-   PK = SESSION#{sessionId}
-   SK = META

Stores:

-   userId
-   createdAtUtc
-   expiresAtUtc (TTL attribute)

Purpose:

-   BFF session resolution
-   Deterministic user lookup
-   HttpOnly cookie backing store

Sessions are deleted on:

-   Logout
-   Account deletion

------------------------------------------------------------------------

# 4️⃣ Audit Table --- CommitCollectAudit

## Primary Keys

-   PK = USER#{userId}
-   SK = AUDIT#{unixEpoch}#{requestId}

## GSI1 --- Correlation Index

-   GSI1PK = CORR#{correlationId}
-   GSI1SK = AUDIT#{unixEpoch}#{requestId}

## GSI2 --- Event Index

-   GSI2PK = EVENT#{eventType}#{yyyyMM}
-   GSI2SK = AUDIT#{unixEpoch}#{requestId}

TTL: 90 days

Purpose:

-   Deterministic audit trail
-   Correlation tracing
-   Compliance grouping

Audit failures must never break primary business logic.

------------------------------------------------------------------------

# 5️⃣ Idempotency Table --- CommitCollectIdempotency

## Key Structure

-   IdempotencyKey (PK)

Used for:

-   Webhook ingestion deduplication
-   Safe retry handling

Guarantees:

-   No duplicate ingestion
-   Safe replay capability

------------------------------------------------------------------------

# 6️⃣ Access Patterns (Production-Safe)

Allowed:

-   Query by PK
-   begins_with(SK, prefix)
-   Projection-only reads
-   TransactWriteItems
-   BatchWriteItem (25-item chunks)

Forbidden:

-   Full table scans
-   Cross-partition aggregation
-   Ad-hoc filtering without key condition

------------------------------------------------------------------------

# 7️⃣ Concurrency Model

-   Optimistic concurrency via version attribute (milestones)
-   Transactional writes for award minting
-   Idempotent ingestion events
-   Deletion flows safe to re-run

------------------------------------------------------------------------

# 8️⃣ Guardrails

-   MaxWorkoutsToInspect = 3000
-   No unbounded pagination
-   Projection expression enforced
-   On-demand billing (no capacity planning required)
-   Deterministic PK grouping

------------------------------------------------------------------------

# 9️⃣ Failure Modes

Possible failures:

-   Conditional check failed (optimistic concurrency)
-   Throttling (rare in on-demand mode)
-   Partial transaction failure
-   Duplicate webhook delivery

Mitigations:

-   Idempotency table
-   Transactional writes
-   Retry-safe flows

------------------------------------------------------------------------

# 🔟 Design Principles

-   Deterministic ownership boundaries
-   Monotonic state transitions
-   Idempotent write paths
-   Transaction safety where required
-   No scans in request paths

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production snapshot of DynamoDB data model
