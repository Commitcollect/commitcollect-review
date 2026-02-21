# 30 --- Workouts Ingestion (Strava → CommitCollect)

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Activity Ingestion Pipeline

------------------------------------------------------------------------

## Purpose

This document defines the production ingestion pipeline responsible for
receiving Strava activity events, retrieving activity data, and
persisting normalized workout records into DynamoDB.

The ingestion system is:

-   Event-driven
-   Idempotent
-   Asynchronous
-   Partition-safe
-   Scan-free

------------------------------------------------------------------------

# 1️⃣ High-Level Flow

Strava\
→ Webhook POST\
→ API Gateway\
→ `commitcollect-api` Lambda\
→ Async invoke `commitcollect-strava-worker`\
→ Strava API fetch\
→ DynamoDB write (`WORKOUT#STRAVA#{activityId}`)

No manual triggers required in production.

------------------------------------------------------------------------

# 2️⃣ Webhook Registration

Endpoint:

`POST /webhooks/strava`

Responsibilities:

-   Accept webhook events
-   Parse event payload
-   Validate event type (create/update/delete)
-   Enforce idempotency
-   Trigger worker execution

Webhook verification handshake handled via GET challenge response.

------------------------------------------------------------------------

# 3️⃣ Idempotency Strategy

Table: `CommitCollectIdempotency`

Each webhook event produces a deterministic idempotency key.

Before processing:

1.  Attempt conditional write in idempotency table
2.  If key exists → skip processing
3.  If key created → continue

This guarantees:

-   No duplicate ingestion
-   Safe retries
-   Safe webhook redelivery

------------------------------------------------------------------------

# 4️⃣ Worker Lambda --- `commitcollect-strava-worker`

The worker is responsible for:

1.  Resolving athlete ownership:

    -   Query `STRAVA#ATHLETE#{athleteId}`
    -   Resolve internal `userId`

2.  Refreshing Strava access token if required

3.  Fetching activity from:

    `GET https://www.strava.com/api/v3/activities/{activityId}`

4.  Projecting required fields into normalized domain model

5.  Writing workout item into DynamoDB

------------------------------------------------------------------------

# 5️⃣ Workout Storage Model

Table: `CommitCollect`

Partition:

PK = `USER#{userId}`

Sort Key:

SK = `WORKOUT#STRAVA#{activityId}`

Fields typically include:

-   activityId
-   sportType
-   distanceMeters
-   totalElevationGain
-   startDateUtc
-   isDeleted
-   createdAtUtc
-   raw metadata (minimal subset only)

No raw JSON payload is persisted in production paths.

------------------------------------------------------------------------

# 6️⃣ Delete Handling

If Strava sends a delete event:

-   Mark `isDeleted = true`
-   Do NOT physically delete workout record
-   Preserve for audit traceability

Aggregation filters exclude:

`isDeleted == true`

This ensures:

-   Historical safety
-   No award rollback
-   Deterministic milestone state

------------------------------------------------------------------------

# 7️⃣ Access Patterns

Allowed:

-   Query PK = `USER#{userId}`
-   begins_with(SK, "WORKOUT#STRAVA#")
-   Projection expressions
-   Pagination with guardrail

Forbidden:

-   Table scans
-   Cross-partition aggregation

------------------------------------------------------------------------

# 8️⃣ Guardrails

-   MaxWorkoutsToInspect = 3000
-   No unbounded pagination
-   Projection-only reads
-   Token refresh required before API call
-   Hard filter on `startDateUtc >= periodStartAtUtc`
-   `isDeleted != true` enforced during aggregation

------------------------------------------------------------------------

# 9️⃣ Concurrency Model

-   Idempotency table prevents duplicate ingestion
-   No race condition between worker and API path
-   Updates overwrite full workout record safely
-   No optimistic locking required for workout writes

------------------------------------------------------------------------

# 🔟 Failure Modes

Possible failures:

-   Strava token expired → refresh required
-   Strava API rate limiting
-   Webhook redelivery
-   Athlete not linked
-   DynamoDB conditional failure
-   Lambda timeout

Mitigations:

-   Idempotency key enforcement
-   Retry-safe ingestion
-   Defensive null checking
-   Guarded aggregation model

------------------------------------------------------------------------

# 1️⃣1️⃣ Observability

Logs should include:

-   correlationId
-   webhook event type
-   athleteId
-   resolved userId
-   activityId
-   idempotency status
-   DynamoDB write status

Audit events (if enabled):

-   STRAVA_ACTIVITY_CREATE
-   STRAVA_ACTIVITY_UPDATE
-   STRAVA_ACTIVITY_DELETE

Audit failures must not break ingestion.

------------------------------------------------------------------------

# 1️⃣2️⃣ Design Principles

-   Event-driven
-   Idempotent
-   Deterministic storage model
-   No scans
-   User-partition isolation
-   Aggregation-safe design
-   Deletion-safe design

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production snapshot of Strava ingestion pipeline
