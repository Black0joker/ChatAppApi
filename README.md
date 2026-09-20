# ChatAppApi — Real-Time Chat Backend

Production-oriented chat backend: ASP.NET Core 10, SignalR, SQL Server (EF Core),
Redis (backplane + presence), JWT auth. Supports direct/group conversations,
presence, typing indicators, message history, read receipts, attachments,
notifications, and horizontal scaling. Built in phases per `PLAN.md` (1–12 done).

## Architecture

```text
Clients ──HTTPS/WebSocket──► Load Balancer ──► API instances (A, B, …)
                                                        │ SignalR backplane
                                                        ▼
                                                      Redis (presence, pub/sub)
SQL Server ◄── EF Core ── API (users, conversations, messages, receipts, outbox)
File storage ◄── attachments (local provider; S3 slots into IFileStorage)
```

Layered solution (`ChatApp.slnx`):

```text
src/ChatApp.Api            Controllers, ChatHub, middleware, SignalR adapters
src/ChatApp.Application    Use cases + ports (no SQL/Redis references)
src/ChatApp.Domain         Entities, enums
src/ChatApp.Infrastructure EF Core, Redis stores, storage, push, dispatchers
tests/ChatApp.UnitTests    Service + hub unit tests
```

Key design rules: thin hub (logic in `Application`); identity only from
`Context.User`; non-members get 404, never 403; messages persist before delivery
via a transactional outbox; Redis is for ephemeral/distributed state, SQL Server
is the source of truth. Details in `AGENTS.md`.

## Prerequisites

- .NET 10 SDK
- SQL Server: LocalDB works out of the box, or `docker compose up sqlserver`
- Redis (optional for single-instance; required for multi-instance): `docker compose up redis`

## Getting started

```powershell
# 1. Start infrastructure (or rely on LocalDB + in-memory fallbacks)
docker compose up -d

# 2. Apply migrations (the API also migrates on startup)
dotnet ef database update --project src/ChatApp.Infrastructure --startup-project src/ChatApp.Api

# 3. Run
dotnet run --project src/ChatApp.Api

# 4. Test
dotnet test tests/ChatApp.UnitTests/ChatApp.UnitTests.csproj
```

Swagger (Development): `/swagger`. Health: `/health` (liveness), `/health/ready` (SQL Server).

Override the dev connection strings without editing files:

```powershell
$env:ConnectionStrings__SqlServer = "Server=...;Database=ChatApp;..."
$env:ConnectionStrings__Redis = "localhost:6379"
```

## REST API (selection)

```text
POST /api/auth/register | /login | /refresh | /logout
GET  /api/users/me, /api/users/{id}            (lastSeenAt, isOnline)
POST /api/conversations/direct | /groups
GET  /api/conversations, /api/conversations/{id}
POST /api/conversations/{id}/members  |  DELETE /api/conversations/{id}/members/{userId}
GET  /api/conversations/{id}/messages?before=&after=&limit=   (keyset pagination)
POST /api/conversations/{id}/messages          (optional clientMessageId for safe retries)
PUT | DELETE /api/messages/{id}
POST /api/conversations/{id}/messages/{messageId}/read
POST /api/attachments  |  GET | DELETE /api/attachments/{id}
GET  /api/notifications  |  POST /api/notifications/{id}/read
```

## SignalR (`/hubs/chat`, JWT via `Authorization` header or `?access_token=`)

Client → server: `JoinConversation`, `LeaveConversation`, `SendMessage`,
`EditMessage`, `DeleteMessage`, `MarkAsRead`, `TypingStarted`, `TypingStopped`,
`Heartbeat`, `Ping`, `WhoAmI`.

Server → client: `MessageReceived` (via outbox, ~1s delay), `MessageUpdated`,
`MessageDeleted`, `MessageRead`, `TypingStarted`, `TypingStopped`, `UserOnline`,
`UserOffline`, `NotificationReceived`.

## Configuration (`appsettings.json` sections)

`ConnectionStrings` (SqlServer, Redis) · `Jwt` · `Chat` (message/group/page/
attachment limits) · `Presence` (heartbeat, TTL, sweep) · `Outbox` (poll
interval, batch, retries, retention) · `Storage` (provider, local path).

## Reliability notes

- Retried sends with the same `clientMessageId` return the original message —
  no duplicates, no rebroadcasts.
- `MessageReceived` is dispatched from the `OutboxEvents` table (at-least-once;
  clients dedupe by message id). Reconnecting clients sync via `?after=` cursor.
- Without Redis the API runs fully single-instance (in-memory presence, no
  backplane); a configured-but-unreachable Redis never enables the backplane.
