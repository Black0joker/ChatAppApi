# AGENTS.md — ChatAppApi

Real-time chat backend (.NET 10, layered: `src/ChatApp.{Api,Application,Domain,Infrastructure}`, `tests/ChatApp.UnitTests`). Plan of record: `PLAN.md` (phases 1–12 done; 13 pending).

## Build / test
- Solution file is `ChatApp.slnx` (new SDK format, **not** `.sln`): `dotnet build ChatApp.slnx`
- Tests: `dotnet test tests/ChatApp.UnitTests/ChatApp.UnitTests.csproj`
- `dotnet ef` needs `Microsoft.EntityFrameworkCore.Design` on the **startup** project (`ChatApp.Api`). Migrations live in Infrastructure:
  `dotnet ef migrations add <Name> --project src/ChatApp.Infrastructure --startup-project src/ChatApp.Api --output-dir Persistence/Migrations`
- The API auto-runs `Database.MigrateAsync()` on startup, so `dotnet ef database update` is only needed for explicit migration.
- `dotnet ef` builds the app host at design time: a broken DI graph (e.g. singleton capturing a scoped service) fails `migrations add`, not just `run`.

## Database
- Dev default is LocalDB (`ConnectionStrings:SqlServer` in `src/ChatApp.Api/appsettings.json`). Override per-run via env: `ConnectionStrings__SqlServer`.
- `docker-compose.yml` (SQL Server + Redis) is the canonical multi-instance path, but assume the Docker daemon is **down** in this environment — verify against LocalDB instead.

## Running the API from an agent shell (gotchas)
- Each shell invocation is a **fresh session**: `Start-Job` servers die with the call. Launch detached via WMI (`Invoke-CimMethod Win32_Process Create` with `cmd /c set ... && dotnet <dll>`), logs to a temp file.
- A running server **locks build outputs** (`MSB3027`) — stop the `ChatApp.Api.dll` process before rebuilding.
- Running the DLL directly sets content root to `bin/` (no `appsettings.json` found) — always pass `--contentRoot src/ChatApp.Api`.
- Dev port here is `http://localhost:5000` (`ChatApp.Api.http` scratch file matches it; `launchSettings.json` says 5154 — don't trust it blindly).

## Packages (do not "upgrade" these)
- `Swashbuckle.AspNetCore` pinned to **9.0.6** — v10 pulls `Microsoft.OpenApi` v3 and breaks `Microsoft.OpenApi.Models` usings.
- `Microsoft.AspNetCore.OpenApi` was deliberately **removed** for the same conflict. Do not re-add.

## Architecture rules (enforced, not optional)
- Thin hub, logic in `Application` services. Hub methods take **no user ids** — identity comes from `Context.User` only.
- Non-members get **404, never 403**, on conversation/message access (no existence probing).
- Reads use batched `GetByIdsAsync` (`AsNoTracking`) — never per-entity loops. Writes must **not** reuse those entities: use `ExecuteUpdateAsync` (tracked-entity mutation after `AsNoTracking` silently persists nothing).
- Redis backplane is gated by the `RedisSetup.TryConnect` reachability probe, not config presence — an unreachable-but-configured Redis tears down **every** SignalR connection (`RedisHubLifetimeManager` throws on SUBSCRIBE). Same probe drives the Redis vs in-memory presence-store choice.
- Typing indicators: no SQL/Redis writes, `OthersInGroup` broadcast. Presence transitions are the only cross-instance pub/sub payload.
- `MessageReceived` is delivered by the `OutboxDispatcher`, never by the hub directly — `SendMessage` persists + returns, delivery follows within a poll interval (`Outbox:PollIntervalMs`, default 1s). Expect that delay in live tests.
- SignalR group names come only from `ChatHub.GroupName` (dashed GUIDs). Never reformat conversation ids when publishing — `conversation:{N}` vs `conversation:{D}` silently drops events.
- Hosted services (`BackgroundService`) must resolve scoped deps via `IServiceScopeFactory` per tick, never via constructor injection.
- Retry-safe sends use `ClientMessageId` (sparse unique index per sender); a retry returns the original message with no rebroadcast and no re-notify.

## Workflow
- Branch is `main`, remote is `origin` (`https://github.com/Black0joker/ChatAppApi.git`).
- **Never commit/push unless explicitly asked.** Scratch SignalR test clients go in `%TEMP%\opencode` (console app + `Microsoft.AspNetCore.SignalR.Client`), never in the repo — delete after use.
- `appsettings.json` holds a dev-only JWT key (`CHANGE-ME`); real secrets go in env vars.
