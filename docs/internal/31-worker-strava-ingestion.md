# 31-worker-strava-ingestion.md

## Purpose

Defines the execution model for the Strava ingestion worker Lambda.

This worker is responsible for: - Receiving webhook-triggered work -
Fetching activity payloads from Strava API - Normalizing activity data -
Persisting workouts into DynamoDB - Enforcing idempotency

------------------------------------------------------------------------

## Trigger Source

Primary trigger: - Strava webhook → API Gateway → API Lambda → async
invoke → Worker Lambda

Future-compatible triggers: - Manual recompute - Scheduled
reconciliation job

------------------------------------------------------------------------

## Input Contract

Worker receives:

``` json
{
  "athleteId": 123,
  "activityId": 456,
  "eventType": "create|update|delete",
  "correlationId": "uuid"
}
```

------------------------------------------------------------------------

## Execution Flow

1.  Resolve athlete → user mapping
2.  Refresh Strava access token (if expired)
3.  Call Strava `GET /activities/{activityId}`
4.  Project normalized workout model
5.  Write to DynamoDB (idempotent upsert)
6.  Emit audit event
7.  Exit

------------------------------------------------------------------------

## Idempotency Strategy

Primary key for workouts:

    PK = USER#{userId}
    SK = WORKOUT#STRAVA#{activityId}

Rules: - Same activityId overwrites safely - Delete events set
`isDeleted = true` - No duplicate rows

------------------------------------------------------------------------

## Failure Modes

### Token Refresh Failure

-   Log error
-   Emit audit failure
-   Worker exits (no retry loop)

### Strava 404

-   Mark activity deleted (soft delete)

### Dynamo Write Conflict

-   Retry once
-   Log + fail if persistent

------------------------------------------------------------------------

## Observability

Logged fields: - correlationId - athleteId - activityId - eventType -
durationMs

CloudWatch alarms recommended: - Error rate spike - Throttling -
Duration \> threshold

------------------------------------------------------------------------

## Guardrails

-   MaxWorkoutsToInspect = 3000 (aggregation safety)
-   Projection-only reads
-   No scans

------------------------------------------------------------------------

## Architectural Guarantees

-   Idempotent writes
-   Soft-delete respected
-   Token lifecycle handled safely
-   Non-blocking audit writes
