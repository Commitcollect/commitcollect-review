# CommitCollect Public API — Overview

**Purpose:** Complete index of production HTTP endpoints.

This document maps:
- HTTP method
- Route
- Controller filename (source location)

Repo snapshot reviewed:
`32b7a96ed3cabd0fed6663aa458bccca0a06484c`

---

## Base URLs

- API: `https://api.commitcollect.com`
- Frontend: `https://app.commitcollect.com`

---

## Health

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/health` | `HealthController.cs` |

## Auth & Session

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/auth/login` | `AuthController.cs` |
| `GET` | `/auth/callback` | `AuthController.cs` |
| `GET` | `/logout` | `LogoutController.cs` |
| `GET` | `/session` | `SessionController.cs` |
| `GET` | `/viewer` | `ViewerController.cs` |

## Strava

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/strava/connect` | `OAuthStravaController.cs` |
| `GET` | `/strava/status` | `StravaStatusController.cs` |
| `DELETE` | `/strava/connection` | `StravaConnectionController.cs` |

## OAuth

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/oauth/strava/callback` | `OAuthStravaController.cs` |

## Webhooks

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/webhooks/strava` | `StravaWebhookController.cs` |
| `POST` | `/webhooks/strava` | `StravaWebhookController.cs` |

## Activities / Workouts

| Method | Route | Controller File |
|--------|-------|----------------|
| `GET` | `/activities/recent?limit=10` | `StravaStatusController.cs` |

## Milestones

| Method | Route | Controller File |
|--------|-------|----------------|
| `POST` | `/milestones` | `MilestonesController.cs` |
| `GET` | `/milestones/{milestoneId}` | `MilestonesController.cs` |
| `POST` | `/milestones/{milestoneId}/recompute` | `MilestonesController.cs` |

## Account

| Method | Route | Controller File |
|--------|-------|----------------|
| `DELETE` | `/account` | `AccountController.cs` |


---

## Authentication Model

Most endpoints require a valid `cc_session` HttpOnly cookie.

Unauthenticated requests return:

```json
{ "status": "invalid_session" }
```

with HTTP `401 Unauthorized`.

---

## Notes

- OAuth and Webhook endpoints may return redirects.
- Webhook POST requests enqueue work to a background worker.
- Milestone recompute includes guardrails to prevent excessive reads.
