# 90 --- Operations & Troubleshooting

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Diagnostics + Recovery Procedures

------------------------------------------------------------------------

## Purpose

This document defines operational diagnostics, failure isolation
patterns, and recovery procedures for CommitCollect production.

It is intended for:

-   Incident debugging
-   Runtime anomaly investigation
-   Safe recovery execution
-   Production stability assurance

------------------------------------------------------------------------

# 1️⃣ Core Production Components

### Frontend

-   Vercel (app.commitcollect.com)

### API

-   AWS Lambda (.NET 8)
-   API Gateway (HTTP API)

### Worker

-   commitcollect-strava-worker Lambda

### Data

-   DynamoDB (CommitCollect)
-   CommitCollectSessions
-   CommitCollectAudit
-   CommitCollectIdempotency

### Identity

-   Cognito Hosted UI

------------------------------------------------------------------------

# 2️⃣ Logging Locations

### Lambda Logs

-   CloudWatch → Log Groups
    -   /aws/lambda/commitcollect-api
    -   /aws/lambda/commitcollect-strava-worker

Logs should always include: - correlationId - route + method - userId
(internal only) - activityId (if applicable) - milestoneId (if
applicable) - error type

------------------------------------------------------------------------

# 3️⃣ Common Production Issues

## 🔐 Auth Failures

### Symptom

401 Unauthorized

### Possible Causes

-   Missing cc_session cookie
-   Expired session
-   Cookie SameSite misconfiguration
-   Cookie domain mismatch

### Checks

-   Inspect browser cookies
-   Verify Secure + Domain settings
-   Query CommitCollectSessions table
-   Confirm TTL not expired

------------------------------------------------------------------------

## 🔄 Strava OAuth Issues

### Symptom

User cannot connect Strava

### Possible Causes

-   Token exchange failure
-   Invalid client secret
-   Redirect URI mismatch

### Checks

-   Inspect /oauth/strava/callback logs
-   Confirm environment variables
-   Verify Strava app configuration

------------------------------------------------------------------------

## 🔔 Webhook Not Firing

### Symptom

Activity not ingested

### Possible Causes

-   Webhook subscription inactive
-   Signature verification failure
-   Idempotency skip

### Checks

-   Confirm Strava webhook dashboard
-   Inspect API logs for webhook POST
-   Inspect Idempotency table
-   Check worker Lambda logs

------------------------------------------------------------------------

## 📦 Ingestion Failures

### Symptom

Activity exists in Strava but not in DynamoDB

### Possible Causes

-   Token refresh failure
-   Worker timeout
-   Conditional failure
-   Athlete ownership missing

### Checks

-   Query STRAVA#ATHLETE#{athleteId}
-   Inspect worker logs
-   Confirm DynamoDB write permissions

------------------------------------------------------------------------

## 🏆 Milestone Not Progressing

### Symptom

User activity exists but milestone unchanged

### Possible Causes

-   startDateUtc \< periodStartAtUtc
-   sportType mismatch
-   isDeleted = true
-   Guardrail exceeded

### Checks

-   Query workouts via PK
-   Inspect milestone record
-   Check inspected count in debug response

------------------------------------------------------------------------

## ❌ Award Not Minted

### Symptom

Threshold crossed but no award record

### Possible Causes

-   Version mismatch
-   Transaction cancellation
-   Incorrect partTarget

### Checks

-   Inspect milestone version
-   Query award SK pattern
-   Check CloudWatch transaction errors

------------------------------------------------------------------------

# 4️⃣ Safe Recovery Procedures

## Session Reset

1.  Delete SESSION#{sessionId} in table
2.  Clear browser cookie
3.  Force re-authentication

------------------------------------------------------------------------

## Reprocess Webhook (Safe)

If ingestion failed before idempotency write:

-   Re-send event manually (if needed)

If idempotency record exists:

-   Delete idempotency key cautiously
-   Re-trigger ingestion manually

------------------------------------------------------------------------

## Milestone Recompute (Safe)

Because awards are immutable:

-   Recompute aggregation safely
-   Engine will mint only missing parts
-   No risk of duplicate award

------------------------------------------------------------------------

## Account Deletion Recovery

If deletion partially failed:

-   Re-run DELETE /account
-   Flow is idempotent
-   Safe to execute multiple times

------------------------------------------------------------------------

# 5️⃣ Guardrail Breach Handling

If MaxWorkoutsToInspect exceeded:

-   Increase temporarily (controlled change)
-   Or implement rolling window strategy
-   Never allow unbounded scan

------------------------------------------------------------------------

# 6️⃣ Observability Strategy

All major flows must include:

-   correlationId propagation
-   structured logging
-   deterministic error categorization
-   non-blocking audit writes

Audit failures must not break primary flow.

------------------------------------------------------------------------

# 7️⃣ Production Safety Principles

-   No manual DynamoDB edits unless incident requires it
-   No partial state writes
-   No direct cross-partition queries
-   Always validate conditional writes before override
-   Maintain least-privilege IAM policies

------------------------------------------------------------------------

# 8️⃣ Incident Post-Mortem Template

When production issue occurs:

1.  Summary of incident
2.  Affected subsystem
3.  Root cause
4.  Guardrail bypass (if any)
5.  Fix implemented
6.  Preventative measure added
7.  Overview document impact (Y/N)

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production troubleshooting reference
