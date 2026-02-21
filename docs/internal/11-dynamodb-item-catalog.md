# 11-dynamodb-item-catalog.md

## Purpose

Authoritative catalog of all DynamoDB tables and item types used in
CommitCollect.

This document defines: - Table name - PK / SK patterns - Required
attributes - Optional attributes - TTL usage - Index participation

------------------------------------------------------------------------

# PRIMARY APPLICATION TABLE

**Table Name:** `CommitCollect`\
**Design:** Single-table strategy

------------------------------------------------------------------------

## USER PROFILE

    PK = USER#{userId}
    SK = PROFILE

Required: - userId - email - createdAtUtc

Optional: - plan - role

------------------------------------------------------------------------

## STRAVA CONNECTION

    PK = USER#{userId}
    SK = STRAVA#CONNECTION

Required: - athleteId - accessToken - refreshToken - expiresAtUtc

------------------------------------------------------------------------

## STRAVA ATHLETE OWNER LOCK

    PK = STRAVA#ATHLETE#{athleteId}
    SK = OWNER

Purpose: - Enforce single CommitCollect owner per Strava athlete

------------------------------------------------------------------------

## WORKOUT

    PK = USER#{userId}
    SK = WORKOUT#STRAVA#{activityId}

Required: - activityId - sportType - startDateUtc

Optional: - distanceMeters - total_elevation_gain - isDeleted

------------------------------------------------------------------------

## MILESTONE

    PK = USER#{userId}
    SK = MILESTONE#{milestoneId}

Required: - modelId - sport - targetType - totalTarget - partTarget -
partsTotal - periodStartAtUtc

State Fields: - progressValue - percentComplete - partsEarned - version

------------------------------------------------------------------------

## AWARD

    PK = USER#{userId}
    SK = MILESTONE#{milestoneId}#AWARD#{partIndex}

Required: - partIndex - partName - meshFile - attachPoint - awardedAtUtc

Guarantee: - Immutable once created

------------------------------------------------------------------------

## MODEL META

    PK = MODEL#{modelId}
    SK = META

Defines: - partsTotal - status

------------------------------------------------------------------------

## MODEL PART

    PK = MODEL#{modelId}
    SK = PART#{index}

Defines: - partName - meshFile - attachPoint

------------------------------------------------------------------------

# SESSION TABLE

**Table Name:** `CommitCollectSessions`

## SESSION

    PK = SESSION#{sessionId}
    SK = META

Required: - userId - createdAtUtc - expiresAtUtc

TTL: - expiresAtUtc

------------------------------------------------------------------------

# AUDIT TABLE

**Table Name:** `CommitCollectAudit`

## AUDIT EVENT

    PK = USER#{userId}
    SK = AUDIT#{unixEpoch}#{requestId}

Indexes: - GSI1 → Correlation index - GSI2 → EventType + yyyyMM

TTL: - ExpiresAt (90 days)

------------------------------------------------------------------------

# IDEMPOTENCY TABLE (if enabled)

**Table Name:** `CommitCollectIdempotency`

    PK = IDEMPOTENCY#{requestId}
    SK = META

Purpose: - Protect against duplicate processing of external events

------------------------------------------------------------------------

## Design Guarantees

-   Single-table strategy for core domain data
-   Separate tables for sessions, audit, idempotency
-   Strongly consistent session reads
-   Optimistic concurrency on milestones (version++)
-   No scans in hot paths
-   Projection-only reads where possible
