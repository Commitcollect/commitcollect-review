# Milestone 1

**CommitCollect API — First Public Availability**

**Date:** 10 February 2026
**Time:** ~3:14 PM AEDT
**ISO:** 2026-02-10T15:14:00+11:00

**Public Endpoint:**
https://api.commitcollect.com/health

**Region:** ap-southeast-2 (Sydney)

**Status:** ✅ Operational — Public endpoint reachable over HTTPS

---

## Significance

This milestone marks the first successful exposure of the CommitCollect backend to the public internet.

The platform demonstrated the ability to:

* Resolve a production domain via Route53
* Terminate TLS successfully with a trusted certificate
* Route requests through API Gateway
* Execute a live AWS Lambda (.NET 8) function
* Return a deterministic health response

**Result:** CommitCollect transitioned from a local development project into a publicly accessible cloud service.

---

## Architectural Impact

This validated the foundational production path:

Internet → DNS → API Gateway → Lambda → Application Response

With this confirmation, the core infrastructure layer was proven ready to support higher-order capabilities such as authentication flows, third-party integrations, and data persistence.

---

## Engineering Note

This milestone represents the moment CommitCollect established its **production footprint** in AWS.

From this point forward, the project moved beyond infrastructure experimentation into structured platform engineering — enabling rapid iteration on real product features with confidence in the underlying cloud architecture.

-------------------------------------------------------------------------

# Milestone 2

**CommitCollect API — Strava Webhook Verification Successful**

**Date:** 11 February 2026
**Time:** ~8:55 PM AEDT
**ISO:** 2026-02-11T20:55:00+11:00

**Verified Endpoint:**
https://api.commitcollect.com/webhooks/strava

**Verification Response:**

```json
{"hub.challenge":"12345"}
```

**Region:** ap-southeast-2 (Sydney)

**Status:** ✅ Operational — External webhook verification confirmed

---

## Significance

This milestone confirms that CommitCollect’s production backend can successfully:

* Accept inbound HTTPS requests from third-party platforms
* Boot a .NET 8 Lambda runtime without startup failures
* Resolve environment configuration securely
* Route requests through API Gateway into ASP.NET controllers
* Parse verification parameters correctly
* Return protocol-compliant responses required for webhook registration

**Result:** CommitCollect is now capable of real-time event ingestion from Strava.

---

## Architectural Impact

This achievement validates the full production request path:

Internet → Route53 → API Gateway → Lambda → ASP.NET → Controller → Configuration → Response

With this verification complete, the platform is officially ready to:

* Register the production Strava webhook subscription
* Receive athlete activity events
* Trigger milestone evaluation workflows
* Persist activity data into DynamoDB
* Begin fulfillment automation design

---

## Engineering Note

This marks the transition from **infrastructure provisioning** to **live platform capability**.

Future development can now focus on product behavior rather than foundational cloud setup.


# Milestone 3

**CommitCollect API — Strava OAuth Token Persistence Successful**

**Date:** 11 February 2026  
**Time:** ~10:51 PM AEDT  
**ISO:** 2026-02-11T22:51:58+11:00  

**Verified Endpoint:**  
https://api.commitcollect.com/oauth/strava/callback  

**OAuth Response:**

```json
{
  "status": "connected",
  "userId": "usr_commitcollectmvp",
  "tokenExpiresAt": 1770829355
}
```

**Database Record Confirmed:**  
DynamoDB → `CommitCollect`  

**Region:** ap-southeast-2 (Sydney)  

**Status:** ✅ Operational — OAuth flow completed and tokens persisted  

---

## Significance

This milestone confirms that CommitCollect’s production platform can now successfully:

* Complete the full OAuth 2.0 authorization flow with Strava  
* Validate signed state parameters securely  
* Exchange authorization codes for access + refresh tokens  
* Persist encrypted credentials into DynamoDB  
* Associate external athlete authorization with an internal CommitCollect user  

**Result:** CommitCollect has officially achieved **secure athlete identity linking**.

---

## Architectural Impact

This achievement validates one of the most critical paths in the entire platform:

Athlete → Strava OAuth → API Gateway → Lambda → ASP.NET → Token Exchange → DynamoDB  

With OAuth persistence operational, the platform is now capable of:

* Maintaining long-lived athlete connections  
* Refreshing tokens automatically  
* Receiving authenticated activity data  
* Mapping workouts directly to CommitCollect users  
* Powering milestone progression logic  

This is the moment CommitCollect transitions from **integration-ready** to **user-capable**.

---

## Engineering Note

This milestone represents the completion of the **Identity Layer** of the platform.

Infrastructure is no longer the primary risk.

From this point forward, development momentum shifts toward:

**activity ingestion → milestone computation → reward fulfillment**

CommitCollect is no longer just a cloud architecture.

It is now a functioning connected fitness platform.


------------ 

# Milestone 4

**Strava Integration — First Successful Activity Retrieval & Ingestion**

**Date:** 12 February 2026
**Time:** ~9:14 PM AEDT
**ISO:** 2026-02-12T21:14:00+11:00

**Verified Endpoint:**
https://www.strava.com/api/v3/activities/{activityId}

**Region:** ap-southeast-2 (Sydney)

**Status:** ✅ Operational — Successfully retrieved live activity JSON and executed end-to-end ingestion into DynamoDB

---

## Significance

This milestone marks the first verified end-to-end execution of CommitCollect’s Strava ingestion pipeline using a real athlete connection and production OAuth tokens.

The platform demonstrated the ability to:

* Successfully refresh a Strava OAuth access token
* Authenticate requests against the Strava v3 API
* Retrieve a full activity payload for a live athlete
* Execute the asynchronous ingestion worker Lambda (.NET 8)
* Project curated activity fields into a lean domain model
* Persist the workout record into DynamoDB

**Result:** CommitCollect transitioned from infrastructure readiness into **live third-party data ingestion**, validating the platform’s ability to capture real-world athlete activity data.

---

## Architectural Impact

This milestone validated the first external data acquisition path:

Athlete → Strava API → OAuth → Worker Lambda → DynamoDB

With this confirmation, CommitCollect’s event-driven architecture proved capable of reliably integrating with upstream providers — establishing the foundation required for milestone tracking, rewards logic, and longitudinal athlete data modeling.

This also verified several production-critical subsystems:

* Token lifecycle management (refresh flow)
* Athlete-to-user resolution via GSI
* Idempotent event handling
* Structured payload projection
* Durable persistence

---

## Engineering Note

This milestone represents the moment CommitCollect crossed from **platform construction into product capability**.

For the first time, real athlete telemetry flowed through the system and was successfully committed to the data layer — confirming the viability of the serverless ingestion design.

From this point forward, the architecture can confidently support:

* Webhook-driven automation
* Goal tracking engines
* Milestone computation
* Subscription fulfillment workflows

CommitCollect is no longer preparing to ingest data — it is now **operationally ingesting live performance data**, marking a major step toward production maturity.


--------------------------------

# Milestone 5

**CommitCollect — First Fully Automated Strava Webhook Ingestion**

**Date:** 13 February 2026  
**Time:** ~8:30 PM AEDT  
**ISO:** 2026-02-13T20:30:00+11:00  

**Webhook Endpoint:**  
https://api.commitcollect.com/webhooks/strava  

**Region:** ap-southeast-2 (Sydney)

**Status:** ✅ Operational — Real Strava activity automatically ingested via webhook

---

## Significance

This milestone marks the first successful end-to-end automated ingestion of a real Strava activity into CommitCollect without any manual API calls.

Upon saving a live Strava activity, the platform demonstrated the ability to:

* Receive a production Strava webhook (POST) over HTTPS
* Route the webhook through API Gateway
* Execute the `commitcollect-api` Lambda handler
* Asynchronously invoke the `commitcollect-strava-worker`
* Fetch activity data from Strava API
* Persist normalized workout data into DynamoDB
* Enforce idempotency protections during ingestion

No Postman calls were required.  
No manual triggering occurred.

**Result:** CommitCollect transitioned from manual integration testing to a live, event-driven ingestion system powered entirely by Strava webhooks.

---

## Architectural Impact

This validated the complete distributed ingestion path:

Strava → Webhook → API Gateway → Lambda (API) → Lambda (Worker) → DynamoDB

With this confirmation, the platform proved its ability to operate as a production-grade, externally triggered event-driven system.

The most critical boundary — third-party system → public endpoint → internal processing — is now verified and stable.

---

## Engineering Note

This milestone represents the moment CommitCollect became a truly autonomous integration platform.

From this point forward, engineering focus shifts from connectivity validation to:

* Observability hardening
* Failure isolation and retry tuning
* Alarm and monitoring strategy
* Scalability validation
* Product-layer feature expansion

The ingestion backbone is now production-real.

------------------------------


# Milestone 6

**CommitCollect API — End-to-End User Deletion (Data + Cognito)**

**Date:** 15 February 2026  
**Region:** ap-southeast-2 (Sydney)  
**Environment:** Production  
**Endpoint:** DELETE https://api.commitcollect.com/account  

---

## Summary

Successfully implemented and validated full user deletion across:

- DynamoDB (CommitCollect)
- DynamoDB (CommitCollectSessions)
- DynamoDB (CommitCollectIdempotency, if applicable)
- Strava connection records
- Cognito User Pool

The deletion flow executed cleanly and returned:

```
204 No Content
```

All associated data and identity artifacts were permanently removed.

---

## What Was Validated

### 1. Session Resolution (BFF Pattern)

- Session cookie (`cc_session`) successfully resolved to `SESSION#{sessionId}` record
- `userId` extracted from DynamoDB session table
- Consistent read confirmed session validity

### 2. Strava Disconnect (Best Effort)

- Loaded `USER#{userId} / STRAVA#CONNECTION`
- Retrieved `accessToken`
- Called:

```
POST https://www.strava.com/oauth/deauthorize
```

- Deleted:
  - `USER#{userId} / STRAVA#CONNECTION`
  - `STRAVA#ATHLETE#{athleteId} / OWNER` (conditional ownership)

### 3. DynamoDB User Data Removal

- Queried all records under:

```
PK = USER#{userId}
```

- Batched deletion (25-item chunks)
- Ensured idempotency and safe re-run capability

### 4. Session Invalidation

Deleted:

```
PK = SESSION#{sessionId}
SK = META
```

Session table confirmed empty post-execution.

### 5. Cognito User Deletion

Executed:

```
AdminDeleteUser
```

User successfully removed from:

```
arn:aws:cognito-idp:ap-southeast-2:348942470294:userpool/ap-southeast-2_EpLHQoTbh
```

IAM role updated to allow:

```
cognito-idp:AdminDeleteUser
```

User pool confirmed empty post-execution.

---

## Result

All traces of the user were removed:

- No DynamoDB records
- No active sessions
- No Strava ownership references
- No Cognito identity

The platform now supports **true account destruction**.

---

## Architectural Impact

This milestone completes the BFF account lifecycle:

Create → Authenticate → Persist → Integrate → Destroy

The system now demonstrates:

- Deterministic identity resolution
- Cross-service transactional consistency
- Safe external provider deauthorization
- Proper IAM least-privilege enforcement
- Idempotent deletion behavior

This validates CommitCollect’s production-grade identity and data governance model.

---

## Engineering Significance

This milestone proves that CommitCollect:

- Owns its identity lifecycle
- Controls third-party integration teardown
- Avoids orphaned data
- Enforces secure session invalidation
- Operates cleanly under AWS production IAM constraints

This represents the first fully verified destructive workflow in production.

CommitCollect is no longer just capable of creating data —  
it can now responsibly and completely remove it.

---

**Status:** ✅ Production Verified  
**Confidence Level:** High  
**Reproducibility:** Confirmed via repeated test cycles  

# Milestone 7

**CommitCollect — Production Frontend Domain + Secure BFF Session Architecture**

**Date:** 16 February 2026  
**Time:** ~2:35 PM AEDT  
**ISO:** 2026-02-16T14:35:00+11:00  

**Frontend Domain:**  
https://app.commitcollect.com  

**API Domain:**  
https://api.commitcollect.com  

**Region:** ap-southeast-2 (Sydney)

**Status:** ✅ Operational — Custom frontend domain live with cross-subdomain secure session handling

---

## Significance

This milestone marks the successful separation of frontend and backend production domains while preserving secure, HttpOnly session-based authentication using the BFF pattern.

The platform demonstrated the ability to:

* Attach a custom production frontend domain via Vercel
* Configure Route53 CNAME records correctly
* Provision automatic SSL certificates
* Complete Cognito Hosted UI authentication on the new domain
* Issue a secure cross-subdomain session cookie (`cc_session`)
* Persist session state in DynamoDB
* Automatically include the session cookie in API requests
* Maintain Strava integration state across domain boundaries

**Result:** CommitCollect now operates with a fully production-grade domain architecture.

---

## Architectural Impact

This milestone validates the complete modern web application request path:

User → app.commitcollect.com → Cognito Hosted UI → api.commitcollect.com → DynamoDB → Session Cookie (.commitcollect.com) → Frontend

Key architectural confirmations:

* Parent-domain cookie strategy (`Domain = .commitcollect.com`)
* Secure + HttpOnly + SameSite=None enforcement
* Cross-subdomain credential inclusion
* API isolation from frontend origin
* DNS authority migrated to Route53
* SSL termination at Vercel edge
* Zero reliance on default *.vercel.app domains

The BFF session model is now production-real and domain-correct.

---

## Engineering Note

This milestone represents the maturation of CommitCollect’s identity boundary.

Previously:
Frontend and backend operated functionally.

Now:
Frontend and backend operate as properly segmented production services under a shared parent domain with secure identity propagation.

This eliminates:

* Localhost dependency
* Mixed-domain testing artifacts
* Relative-path routing ambiguity
* Cookie scope inconsistencies

From this point forward, CommitCollect’s authentication layer is stable and production-hardened.

Future engineering focus shifts from infrastructure correctness to:

* Product-layer API expansion
* Deterministic connection state endpoints
* Observability hardening
* Usage analytics
* Subscription and fulfillment logic

CommitCollect is no longer “running in production”.

It is now architected for production.


-----------

# Milestone 8

**CommitCollect — Deterministic Audit System Live**

**Date:** 18 February 2026  
**Region:** ap-southeast-2 (Sydney)  
**Environment:** Production  

---

## Summary

The CommitCollect platform now has a fully operational, production-grade audit system.

This milestone establishes deterministic, user-scoped audit logging across authentication and Strava flows — with correlation tracking, event indexing, and TTL-based lifecycle management.

Audit writes are non-blocking and resilient: primary business logic cannot fail due to audit errors.

---

## What Was Achieved

### 1️⃣ Dedicated Audit Table Created

**Table:** `CommitCollectAudit`

**Primary Keys**
- `PK` → `USER#{userId}`
- `SK` → `AUDIT#{unixEpoch}#{requestId}`

This enables:
- Fast retrieval of all events for a specific user
- Time-ordered sorting
- Unique records even during high-frequency operations

---

### 2️⃣ Global Secondary Indexes Implemented

#### GSI1 — Correlation Index

- `GSI1PK` → `CORR#{correlationId}`
- `GSI1SK` → `AUDIT#{unixEpoch}#{requestId}`

Purpose:
- Trace full request lifecycles
- Debug multi-step flows (Auth → Strava → Worker)

Status: ✅ Active

---

#### GSI2 — Event Type Index

- `GSI2PK` → `EVENT#{eventType}#{yyyyMM}`
- `GSI2SK` → `AUDIT#{unixEpoch}#{requestId}`

Purpose:
- Monthly grouping by event type
- Operational and compliance reporting

Status: ✅ Active

---

### 3️⃣ AUTH_SIGNUP Marker Implemented

A deterministic `UserProfile` item is now created during first Cognito callback.

**PK:** `USER#{userId}`  
**SK:** `PROFILE`  
**entityType:** `UserProfile`

This allows CommitCollect to distinguish:

- First-ever Cognito login → `AUTH_SIGNUP`
- Subsequent logins → `AUTH_LOGIN`

No ambiguity.
No guesswork.
No reliance on session heuristics.

---

### 4️⃣ AUTH_LOGIN Audit Event

Successful session creation now writes:

### 4️⃣ AUTH_LOGIN Audit Event

eventType: AUTH_LOGIN  
result: success  

Includes:
- Session ID
- Login method
- TTL metrics
- Correlation ID
- Client metadata (IP hash, origin, user agent)
- HTTP method and path

---

### 5️⃣ Resilient Write Pattern

Audit writes use:

```csharp
await _audit.TryWriteAsync(...)
```

Failures:

- Logged to CloudWatch  
- Do NOT interrupt primary flow  

This guarantees:

- No user-impact from audit errors  
- Clean separation of concerns  

---

## Architectural Impact

CommitCollect now has:

- Structured compliance trail  
- Deterministic first-user detection  
- Correlation-based debugging capability  
- Time-indexed audit retrieval  
- TTL-based automatic cleanup (90 days)  

The platform transitioned from “functional logging” to auditable system architecture.

---

## Production Validation

Verified in DynamoDB:

- UserProfile item created  
- AUTH_LOGIN audit record successfully written  
- GSI1 correlation indexing functioning  
- GSI2 event indexing functioning  
- TTL attribute present (`ExpiresAt`)  

CloudWatch confirmed successful write operations.

---

## Engineering Significance

This milestone establishes:

- Observability  
- Accountability  
- Deterministic user lifecycle tracking  
- Future readiness for Strava review  
- Enterprise-grade audit foundation  

From this point forward, every major user action can be formally recorded and queried.

CommitCollect is no longer just an app.

It is now an auditable platform.

---

**Status:** COMPLETE  
**Confidence Level:** High


-------------

# Milestone 9

**CommitCollect --- Milestone Engine v1 (Distance + Elevation + 3D Award
Minting)**

**Date:** 20 February 2026\
**Time:** \~10:15 PM AEDT\
**ISO:** 2026-02-20T22:15:00+11:00

**Environment:** Production\
**Region:** ap-southeast-2 (Sydney)\
**Table:** CommitCollect (Single-Table Design)

------------------------------------------------------------------------

## Achievement Summary

The CommitCollect Milestone Engine (MVP) was successfully implemented,
deployed, and validated in production.

This milestone confirms:

-   Annual total milestones (Distance + Elevation) operational
-   12-part statue system enforced
-   Hard eligibility filter (periodStartAtUtc) enforced
-   Soft-deleted Strava activities excluded from aggregation
-   Monotonic award minting implemented
-   Monotonic completion logic enforced
-   Transactional milestone + award writes via TransactWriteItems
-   3D model part metadata successfully copied into award ledger
-   Debug-enhanced /milestones/{id} endpoint operational

Result:

CommitCollect can now convert athletic effort into immutable 3D statue
parts.

------------------------------------------------------------------------

## Architectural Validation

### Aggregation Model

-   PK Query only (USER#{userId})
-   begins_with(SK, "WORKOUT#STRAVA#")
-   No scans
-   Guardrail: MaxWorkoutsToInspect = 3000
-   Projection-only reads
-   Full pagination support

Aggregation Rules:

-   startDateUtc \>= periodStartAtUtc
-   sportType match (normalized)
-   isDeleted != true
-   DISTANCE_METERS → sum(distanceMeters)
-   ELEVATION_METERS → sum(total_elevation_gain)

------------------------------------------------------------------------

## Award Engine

When progress crosses threshold:

floor(progressValue / partTarget)

System performs:

1.  Optimistic concurrency check (version)
2.  Milestone update (progress + status + version++)
3.  Idempotent award mint:
    -   PK = USER#{userId}
    -   SK = MILESTONE#{milestoneId}#AWARD#{partIndex}

Award contains:

-   partIndex
-   partName
-   meshFile
-   attachPoint
-   progressValueAtAward
-   awardedAtUtc

Guarantees:

-   No duplicate awards
-   No revoked awards
-   No completion rollback
-   Deterministic state

------------------------------------------------------------------------

## 3D Model Integration

Static model definitions:

-   PK = MODEL#{modelId}
-   SK = META
-   SK = PART#{index}

Milestone stores:

-   modelId
-   partsTotal
-   partTarget

Award copies model part metadata at mint time.

Frontend can now render unlocked parts without additional joins.

------------------------------------------------------------------------

## Debug Instrumentation

GET /milestones/{id} now returns:

{ "milestone": { ... }, "awards": \[ ... \], "debug": { "pk": "...",
"skPrefix": "...", "inspected": 2, "awardsCount": 1, "hasMore": false }
}

Backend performs heavy lifting. Frontend remains lightweight and
deterministic.

------------------------------------------------------------------------

## Product Impact

Activities → Aggregation → Threshold Detection → Transactional Mint →
Durable Artifact

This marks the first time:

A real athlete activity triggered a real statue part mint in production.

------------------------------------------------------------------------

## First Artifact Minted

Model: DRAGON_2026\
Part Index: 01\
Part Name: Base\
Progress at Award: 889,716 meters

The dragon has begun.

------------------------------------------------------------------------

## Engineering Note

This milestone establishes:

-   Deterministic milestone state
-   Idempotent reward ledger
-   Monotonic progress guarantees
-   Transaction-safe award minting
-   Production-grade DynamoDB patterns

CommitCollect is officially alive.


