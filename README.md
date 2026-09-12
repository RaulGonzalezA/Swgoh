# SWGOH

.NET 10 solution for Star Wars: Galaxy of Heroes utilities, built with Clean Architecture, Aspire, ASP.NET Core, Blazor and MongoDB.

## Projects

- `Swgoh.Domain`: domain model and invariants.
- `Swgoh.Application`: use cases and ports.
- `Swgoh.Infrastructure`: MongoDB persistence and SWGOH Comlink integration.
- `Swgoh.Api`: HTTP API.
- `Swgoh.Blazor`: UI host.
- `Swgoh.AppHost`: Aspire orchestration for MongoDB, Comlink, API and Blazor.
- `Swgoh.ServiceDefaults`: service discovery, health checks, resilience and OpenTelemetry defaults.

## Player backend

The API imports live player data from a self-hosted `swgoh-comlink` instance and persists a clean MongoDB document containing the player profile and roster.

- `GET /api/players/{allyCode}` returns the last persisted player state.
- `POST /api/players/{allyCode}/refresh` fetches the player from Comlink and upserts profile + roster in MongoDB.
- `PUT /api/players/{allyCode}` remains available for manual/local data while the application evolves.

Comlink is orchestrated automatically by Aspire using `ghcr.io/swgoh-utils/swgoh-comlink:latest`. Outside Aspire, configure `Swgoh:Comlink:BaseUrl` to point at your Comlink instance (normally `http://localhost:3000`).

Comlink's raw `/player` response does not provide a directly usable total Galactic Power value, so imported profiles currently keep `GalacticPower` at `0`. A later stats/game-data integration can calculate exact GP without leaking Comlink DTOs into Domain/Application.

## Commands

```powershell
dotnet restore Swgoh.sln
dotnet format Swgoh.sln --verify-no-changes --no-restore
dotnet build Swgoh.sln --configuration Release --no-restore
dotnet test Swgoh.sln --configuration Release --no-build --no-restore
dotnet run --project src/Swgoh.AppHost
```
