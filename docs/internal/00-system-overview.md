# 00 --- System Overview

## Purpose

This document defines the high-level architecture, trust boundaries,
identity model, data ownership model, and production topology of
CommitCollect.

It is the canonical internal reference for how the platform is
structured in production.

------------------------------------------------------------------------

## Production Topology

### Frontend

-   Hosting: Vercel
-   Domain: https://app.commitcollect.com
-   Responsibility:
    -   UI rendering
    -   Session cookie transmission
    -   API orchestration
    -   No direct database access
    -   No Strava secrets stored client-side

### Backend

-   Runtime: AWS Lambda (.NET 8)
-   API Gateway: HTTP API
-   DNS: Route53
-   Auth: Cognito Hosted UI
-   Region: ap-southeast-2 (Sydney)

Primary functions:

-   commitcollect-api
-   commitcollect-strava-worker
-   commitcollect-health

### Data Layer

DynamoDB (On-Demand mode):

1.  CommitCollect (single-table design)
2.  CommitCollectSessions
3.  CommitCollectAudit
4.  CommitCollectIdempotency

All tables use deterministic key strategies. No scans are permitted in
production request paths.

------------------------------------------------------------------------

## High-Level Request Flows

### Authentication (BFF Pattern)

User → app.commitcollect.com\
→ Cognito Hosted UI\
→ api.commitcollect.com\
→ Session persisted in DynamoDB\
→ HttpOnly cookie (.commitcollect.com)

The frontend never holds access tokens. Identity resolution occurs
server-side via session table lookup.

### Strava OAuth

Athlete → Strava OAuth\
→ api.commitcollect.com/oauth/strava/callback\
→ Token exchange\
→ Tokens persisted in DynamoDB\
→ Athlete linked to internal userId

### Webhook Ingestion

Strava → Webhook POST\
→ API Gateway\
→ commitcollect-api Lambda\
→ commitcollect-strava-worker Lambda\
→ Strava API fetch\
→ DynamoDB write (WORKOUT#STRAVA#)

### Milestone Engine

Activity data → Aggregation (PK query only)\
→ Threshold evaluation\
→ TransactWriteItems\
→ Award ledger mint\
→ Immutable part record

------------------------------------------------------------------------

## Trust Boundaries

1.  Public Internet → API Gateway\
2.  Frontend → Backend (BFF boundary)\
3.  Backend → Strava API\
4.  Backend → DynamoDB\
5.  Backend → Cognito

All external provider calls are server-side only.

No client-side token persistence. No direct DynamoDB access from
frontend.

------------------------------------------------------------------------

## Data Ownership Model

### User Root

PK = USER#{userId}

All user-owned entities live under this partition:

-   PROFILE
-   STRAVA#CONNECTION
-   WORKOUT#STRAVA#{activityId}
-   MILESTONE#{milestoneId}
-   MILESTONE#{milestoneId}#AWARD#{index}

### Global Entities

-   MODEL#{modelId}
-   STRAVA#ATHLETE#{athleteId}
-   AUDIT#{...} (separate table)

------------------------------------------------------------------------

## Identity Model

-   Cognito handles authentication
-   BFF session model handles authorization
-   Session cookie: cc_session
-   Session table resolves userId deterministically
-   No JWT validation performed client-side

------------------------------------------------------------------------

## Guardrails

-   No table scans in production
-   PK-based queries only
-   begins_with(SK, prefix) pattern enforced
-   Projection-only reads where possible
-   MaxWorkoutsToInspect guardrail
-   On-demand capacity mode
-   Idempotency protections on ingestion + award minting

------------------------------------------------------------------------

## Idempotency & Concurrency

-   Ingestion events protected by idempotency table
-   Award minting uses TransactWriteItems
-   Optimistic concurrency via version attribute
-   Deletion flow idempotent and safe to re-run

------------------------------------------------------------------------

## Failure Domains

Possible failure zones:

-   Cognito callback failure
-   Strava token refresh failure
-   Webhook signature validation failure
-   DynamoDB throttling
-   Lambda cold start latency

Primary business flows must not fail due to:

-   Audit write errors
-   Observability pipeline issues

------------------------------------------------------------------------

## Observability Model

-   CloudWatch logs per Lambda
-   Correlation IDs propagated across flows
-   Audit table (CommitCollectAudit) records user-scoped actions
-   TTL-based lifecycle management (audit records)

------------------------------------------------------------------------

## Lifecycle Model

User lifecycle:

Create → Authenticate → Connect Strava → Ingest Activity → Mint Awards →
Delete

Deletion guarantees:

-   DynamoDB cleanup
-   Strava deauthorization
-   Session invalidation
-   Cognito AdminDeleteUser

------------------------------------------------------------------------

## Architectural Principles

-   Deterministic state
-   Monotonic progress
-   Idempotent writes
-   Transactional safety
-   Serverless-first design
-   Domain-based documentation (not controller-based)

------------------------------------------------------------------------

## Revision Policy

This document will evolve as new subsystems are introduced.

Subsequent internal documents provide deeper detail:

-   10-data-model-dynamodb.md
-   20-auth-session-model.md
-   30-workouts-ingestion.md
-   40-milestone-engine.md
-   50-award-ledger.md
-   90-operations-troubleshooting.md
