# 20 --- Auth & Session Model (BFF)

**Architecture Version:** v1.0\
**Last Updated:** 2026-02-21\
**Scope:** Production Authentication + Authorization model

------------------------------------------------------------------------

## Purpose

This document defines the authentication and session strategy used in
CommitCollect.

CommitCollect uses:

-   Cognito Hosted UI for authentication (identity proof)
-   A backend-for-frontend (BFF) session model for authorization
-   A secure HttpOnly session cookie (`cc_session`) bound to a
    DynamoDB-backed session record

This ensures:

-   No tokens are stored or handled by the frontend
-   All identity resolution occurs server-side
-   Session invalidation is deterministic

------------------------------------------------------------------------

# 1️⃣ Identity Source: Cognito Hosted UI

## Role

Cognito provides:

-   User authentication
-   Federated identity support (future)
-   Token issuance (id_token / access_token)

CommitCollect does not treat Cognito tokens as the long-lived
authentication mechanism for the frontend. Tokens are used only at
callback time to establish a server-side session.

------------------------------------------------------------------------

## Callback Flow

User signs in via Cognito Hosted UI and is redirected to:

-   `/auth/callback` (API domain)

At callback time the backend:

1.  Validates request parameters
2.  Extracts user identity from id_token claims (e.g., `sub`, `email`)
3.  Creates a session record in DynamoDB
4.  Issues the `cc_session` cookie to the client

------------------------------------------------------------------------

# 2️⃣ BFF Session Model

## Session Cookie

Cookie name: `cc_session`

Properties (intended):

-   HttpOnly: true
-   Secure: true
-   SameSite: None (to support cross-subdomain)
-   Domain: `.commitcollect.com`
-   Path: `/`
-   Max-Age aligned to session TTL

The cookie contains only a session identifier. It contains no user
identity and no JWTs.

------------------------------------------------------------------------

## Session Record Storage

Table: `CommitCollectSessions`

Key structure:

-   PK = `SESSION#{sessionId}`
-   SK = `META`

Fields (typical):

-   userId
-   createdAtUtc
-   expiresAtUtc
-   ttl / ExpiresAt (TTL attribute)
-   client metadata (optional, hashed/normalized)

SessionId should be:

-   Random and unguessable
-   Long enough for security
-   Safe for cookie transport

------------------------------------------------------------------------

## Session Resolution

For authenticated requests:

1.  API reads `cc_session` cookie
2.  Queries `CommitCollectSessions` for `SESSION#{sessionId}`
3.  Resolves `userId`
4.  Treats request as authenticated if session exists and not expired

This model is deterministic and avoids:

-   Client-side token parsing
-   JWT validation in frontend
-   Token refresh in browser

------------------------------------------------------------------------

# 3️⃣ Authorization

CommitCollect authorization is based on resolved `userId`.

Rules:

-   All user-owned entities live under PK = `USER#{userId}`
-   Requests must only read/write within the caller's partition
-   No cross-user access allowed

------------------------------------------------------------------------

# 4️⃣ Logout Model

Logout consists of:

1.  Deleting the session record from `CommitCollectSessions`
2.  Clearing the `cc_session` cookie

This ensures deterministic invalidation even if Cognito tokens remain
valid elsewhere.

------------------------------------------------------------------------

# 5️⃣ Account Deletion Auth Considerations

Account deletion requires an authenticated session. Flow:

-   Resolve session → userId
-   Execute account teardown (Strava + DynamoDB + Cognito)
-   Delete session
-   Clear cookie

If session is missing or invalid:

-   Return 401 Unauthorized (`missing_session` / `invalid_session`)

------------------------------------------------------------------------

# 6️⃣ Guardrails

-   No JWT tokens stored client-side
-   No refresh tokens stored in browser
-   Cookie must be HttpOnly + Secure
-   Domain scope must be parent-domain (`.commitcollect.com`) for
    cross-subdomain
-   Session TTL must be enforced via DynamoDB TTL + server checks

------------------------------------------------------------------------

# 7️⃣ Failure Modes

Common failure modes:

-   Missing cookie → 401
-   Expired session → 401
-   Session table misconfigured → 500 (guarded by startup config
    validation)
-   Cookie domain mismatch → session never sent to API
-   SameSite misconfigured → cookie dropped by browser

------------------------------------------------------------------------

# 8️⃣ Observability

Authentication/session logs should include:

-   correlationId
-   route + method
-   result status (success / attempt / failure)
-   sessionId (hashed or truncated, optional)
-   userId (internal only)
-   origin / user agent (sanitized)

Audit events (if enabled):

-   AUTH_SIGNUP (first profile creation)
-   AUTH_LOGIN (session issuance)
-   AUTH_LOGOUT (session invalidation)
-   ACCOUNT_DELETE (destructive flow)

------------------------------------------------------------------------

# 9️⃣ Security Notes

-   Session IDs must not be predictable
-   Avoid logging full session IDs
-   Always set Secure in production
-   Do not allow CORS wildcard with credentials
-   Use least privilege IAM for session table access

------------------------------------------------------------------------

## Change Log

### v1.0

-   Initial production snapshot of Cognito + BFF session model
