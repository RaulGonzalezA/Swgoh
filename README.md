# SWGOH

.NET 10 solution for Star Wars: Galaxy of Heroes utilities, built with Clean Architecture, Aspire, ASP.NET Core, Blazor and MongoDB.

## Projects

- `Swgoh.Domain`: domain model and invariants.
- `Swgoh.Application`: use cases and ports.
- `Swgoh.Infrastructure`: MongoDB persistence, SWGOH Comlink integration, SWGOH Stats integration and Game Data catalog.
- `Swgoh.Api`: HTTP API.
- `Swgoh.Blazor`: UI host.
- `Swgoh.AppHost`: Aspire orchestration for MongoDB, Comlink, SWGOH Stats, API and Blazor.
- `Swgoh.ServiceDefaults`: service discovery, health checks, resilience and OpenTelemetry defaults.

## Player backend

The API imports live player data from a self-hosted `swgoh-comlink` instance, calculates Galactic Power through `swgoh-stats`, enriches the roster with current Game Data and persists a clean MongoDB document.

Each live refresh also stores a player snapshot so roster evolution can be queried over time.

### Endpoints

- `GET /api/players/{allyCode}` returns the last persisted player profile and roster.
- `POST /api/players/{allyCode}/refresh` fetches the player from Comlink, calculates unit GP, enriches the roster and persists both the current profile and a historical snapshot.
- `PUT /api/players/{allyCode}` remains available for manual/local data while the application evolves.
- `GET /api/players/{allyCode}/analysis` returns roster metrics including character/ship GP, relic thresholds, zetas, omicrons and mod coverage.
- `GET /api/players/{allyCode}/history?limit=30` returns recent snapshots ordered from newest to oldest. The limit is clamped between 1 and 365.
- `GET /api/players/{allyCode}/gl-progress` calculates Galactic Legend unit-requirement progress using current Game Data.

## Runtime dependencies

Aspire orchestrates the local development stack automatically:

- MongoDB
- `ghcr.io/swgoh-utils/swgoh-comlink:latest`
- `ghcr.io/swgoh-utils/swgoh-stats:latest`
- `Swgoh.Api`
- `Swgoh.Blazor`

Outside Aspire, configure:

- `ConnectionStrings:swgoh`
- `Swgoh:Comlink:BaseUrl` (normally `http://localhost:3000`)
- `Swgoh:Stats:BaseUrl` (normally `http://localhost:3223`)
- `Swgoh:GameData:BaseUrl` when overriding the default `swgoh-utils/gamedata` source

Game Data is cached in-process for six hours.

## CI

GitHub Actions validates formatting, builds Release, runs unit/integration tests and executes a live smoke test against MongoDB, Comlink and SWGOH Stats. The smoke test verifies player import, positive Galactic Power, roster consistency, omicron detection, historical snapshot creation, analysis and Galactic Legend progress.

`workflow_dispatch` accepts an optional `ally_code` input for the live smoke test.

## Commands

```powershell
dotnet restore Swgoh.sln
dotnet format Swgoh.sln --verify-no-changes --no-restore
dotnet build Swgoh.sln --configuration Release --no-restore
dotnet test Swgoh.sln --configuration Release --no-build --no-restore
dotnet run --project src/Swgoh.AppHost
```
