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

### API versioning

The public HTTP API uses URL-segment versioning. Version 1 is exposed under `/api/v1` and supported versions are reported in the API version response headers.

### Endpoints

- `GET /api/v1/players/{allyCode}` returns the last persisted player profile and roster.
- `GET /api/v1/players/{allyCode}/roster` returns an enriched, filtered, sorted and paged roster. Each item includes the localized unit `name`, `nameKey`, Game Data `thumbnailName`, readable factions and raw SWGOH tags in addition to progression/GP data. Query options: `page`, `pageSize` (1-100), `search`, `type` (`All`, `Character`, `Ship`), `minRarity`, `minRelic`, `hasZeta`, `hasOmicron`, `orderBy` (`GalacticPower`, `RelicTier`, `Rarity`, `GearTier`, `Level`, `DefinitionId`, `Name`) and `direction` (`Ascending`, `Descending`). Search matches IDs, localized names, name keys, factions and tags.
- `POST /api/v1/players/{allyCode}/refresh` fetches the player from Comlink, calculates unit GP, enriches the roster and persists both the current profile and a historical snapshot.
- `PUT /api/v1/players/{allyCode}` remains available for manual/local data while the application evolves.
- `GET /api/v1/players/{allyCode}/analysis` returns roster metrics including character/ship GP, relic thresholds, zetas, omicrons and mod coverage.
- `GET /api/v1/players/{allyCode}/history?limit=30` returns recent snapshots ordered from newest to oldest. The limit is clamped between 1 and 365.
- `GET /api/v1/players/{allyCode}/gl-progress` calculates Galactic Legend unit-requirement progress using current Game Data.
- `GET /api/v1/squads` searches persisted squad definitions. Optional filters: `search`, `format` (`3v3` or `5v5`), `use` (`Flexible`, `Offense`, `Defense`), `tag` and `limit` (1-200).
- `GET /api/v1/squads/{id}` returns one enriched squad definition.
- `POST /api/v1/squads` creates a squad definition; `PUT /api/v1/squads/{id}` updates it and `DELETE /api/v1/squads/{id}` removes it.
- `GET /api/v1/gac/defense-requirements?league=Kyber&format=5v5` returns the number of squad and fleet defenses required for a GAC league/format.
- `GET /api/v1/gac/defense-requirements/transition?fromLeague=Aurodium&toLeague=Kyber&format=5v5` compares two leagues and reports defense deltas plus the additional slots introduced by a promotion.
- `POST /api/v1/gac/opponents/{allyCode}/history` imports one or more normalized historical GAC rounds. Imports are idempotent per opponent, season, event, round and format, and every unit is validated against current Game Data before persistence.
- `GET /api/v1/gac/opponents/{allyCode}/history?format=5v5&maxRounds=30` returns persisted opponent rounds newest first. The format filter is optional and `maxRounds` is clamped between 1 and 200.
- `GET /api/v1/gac/opponents/{allyCode}/scouting?format=5v5&targetLeague=Kyber&maxRounds=30` analyzes the opponent's historical behavior for a specific format and optionally projects it into another league.

Squad definitions are character-only GAC team archetypes. A definition groups one or more complete variants under the same name, format and intended use. A 3v3 variant always contains one leader plus two unique members; a 5v5 variant contains one leader plus four unique members. Unit IDs are validated against current Game Data and API responses enrich leaders/members with localized names, thumbnails and factions. Definitions are persisted in MongoDB so they can later be reused by opponent scouting and GAC recommendation features.

GAC defense requirements are modeled as game rules rather than player persistence. The current requirements are Carbonite 3/3 squads for 5v5/3v3 with 1 fleet, Bronzium 5/7 with 1 fleet, Chromium 7/10 with 2 fleets, Aurodium 9/13 with 2 fleets and Kyber 11/15 with 3 fleets. League transitions expose positive defense deltas so opponent scouting can lower prediction confidence for newly required slots after a promotion.

### Historical GAC opponent scouting

Historical GAC data is deliberately modeled independently from any external website. The import contract records season/event/round, format, league, start time, full-clear result, defensive placements, holds, offense battles, banners, attempts and optional attack timestamps. Character and fleet DefinitionIds are canonicalized and validated through Game Data before the round is stored in MongoDB.

The scout separates 3v3 and 5v5 and reports repeated defensive compositions, placement rate, zones, average holds, hold rate, repeated counters, win rate, one-shot rate, banners, average attempts, full-clear rate and average delay before the first attack. Character defenses are matched exactly against persisted `SquadDefinition` variants, so reports can expose the saved archetype/variant name instead of only raw unit IDs. Unit names are localized through the current Game Data catalog.

Predicted defenses are ranked from the opponent's observed placement frequency and capped by the target league's required squad/fleet slots. When the target league requires more defenses than the latest observed league, the report exposes those additional slots as unobserved instead of fabricating a team prediction. Confidence is sample-size-aware (`Low`, `Medium`, `High`).

The backend does not automate scraping of third-party GAC-history pages. Historical rounds can be imported manually or by a future authorized provider adapter through the normalized Application contract, without changing Domain, Mongo persistence or the scouting algorithm.

Example import shape:

```json
{
  "rounds": [
    {
      "season": 82,
      "eventNumber": 1,
      "roundNumber": 1,
      "format": "5v5",
      "league": "Aurodium",
      "startedAtUtc": "2026-08-12T18:00:00Z",
      "fullClear": true,
      "source": "manual",
      "defenses": [
        {
          "zone": "front-top",
          "squad": {
            "leaderDefinitionId": "LEADER",
            "memberDefinitionIds": ["A", "B", "C", "D"],
            "isFleet": false
          },
          "holds": 2,
          "defeated": true
        }
      ],
      "offenseBattles": []
    }
  ]
}
```

### API hardening

The API applies a global fixed-window rate limit of 120 requests per minute per remote IP address.

`POST /api/v1/players/{allyCode}/refresh` has an additional dedicated limit of one refresh every 30 seconds per Ally Code, with no queue. Rejected requests return HTTP `429 Too Many Requests` and include `Retry-After` when available. This protects Comlink, SWGOH Stats and MongoDB from repeated or concurrent refresh attempts for the same player.

OpenAPI documents are generated per API version. The v1 document is available at `/openapi/v1.json` and Scalar exposes the interactive API reference at `/scalar`.

## Runtime dependencies

GAC opponent discovery runs in a bounded in-memory background queue. Repeated requests for the same player and format reuse the pending lookup. The API returns `202 Accepted` with status `Pending` while the worker runs; `409 Conflict` is reserved for real unavailable/conflict states. The Blazor clients poll every two seconds and update the screen on completion. Leaving the page cancels polling, not the server job. Jobs have a six-minute deadline, and pending jobs are lost when the API restarts. Provider failures become an unavailable result rather than an unhandled HTTP failure.

Aspire orchestrates the local development stack automatically:

- MongoDB (an existing instance when `ConnectionStrings:swgoh` is configured; otherwise an Aspire container)
- `ghcr.io/swgoh-utils/swgoh-comlink:latest`
- `ghcr.io/swgoh-utils/swgoh-stats:latest`
- `Swgoh.Api`
- `Swgoh.Blazor`

The AppHost `https` launch profile uses the existing local MongoDB at `localhost:27017`, with a separate `swgoh` database. Start that MongoDB before pressing F5. Override `ConnectionStrings__swgoh` in the launch profile for another server; store credentials in user secrets rather than this file. Existing Aspire MongoDB volumes are preserved and are not migrated to the external database.

Outside Aspire, configure:

- `ConnectionStrings:swgoh`
- `Swgoh:Comlink:BaseUrl` (normally `http://localhost:3000`)
- `Swgoh:Stats:BaseUrl` (normally `http://localhost:3223`)
- `Swgoh:GameData:BaseUrl` when overriding the default `swgoh-utils/gamedata` source
- `Swgoh:GameData:Locale` to select the localization bundle used for enriched roster names/factions (`SPA_XM` / Spanish by default; for example `ENG_US` for English)

Game Data, category metadata and localization are cached in-process for six hours.

## CI and smoke test

GitHub Actions uses two separate workflows:

- `CI` validates formatting, builds Release and runs all unit/integration tests. Pull requests run only this workflow.
- `Smoke Test` starts MongoDB, Comlink and SWGOH Stats, builds and starts the API, validates OpenAPI/Scalar, GAC defense rules and league transitions, accepts `202 Pending` while current-opponent discovery runs, refreshes a live player, confirms the dedicated refresh rate limit, and validates player import, enriched roster paging, positive Galactic Power, roster consistency, omicron detection, historical snapshot creation, analysis, Galactic Legend progress, squad definitions and a synthetic historical GAC import/opponent-scout flow.

A manual `CI` run exposes `run_smoke`. When enabled, `Smoke Test` is dispatched only after CI has completed successfully. Disable it to execute CI alone. The same manual run accepts `ally_code` for the chained smoke test.

Pushes to `main` run CI and, by default, trigger `Smoke Test` through `workflow_run` after a successful CI. Set the repository Actions variable `RUN_SMOKE_AFTER_CI=false` to keep automatic `main` runs as CI-only.

`Smoke Test` also has its own `workflow_dispatch`, so it can be executed independently with a configurable `ally_code`.

## Commands

```powershell
dotnet restore Swgoh.sln
dotnet format Swgoh.sln --verify-no-changes --no-restore
dotnet build Swgoh.sln --configuration Release --no-restore
dotnet test Swgoh.sln --configuration Release --no-build --no-restore
dotnet run --project src/Swgoh.AppHost
```
