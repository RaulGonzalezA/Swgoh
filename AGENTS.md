# SWGOH Repository Instructions

These instructions apply to the whole repository. A nested `AGENTS.md` adds more specific rules for its directory and takes precedence when there is a conflict.

## Repository

- Solution: `Swgoh.sln`
- Runtime: .NET 10
- Language: C# with nullable reference types enabled
- Architecture: Clean Architecture
- Orchestration: Aspire
- Backend: ASP.NET Core minimal API
- UI: Blazor Web App
- Persistence: MongoDB through `MongoDb.Generic.Repository`
- Testing: xUnit v3 and Testcontainers.MongoDb

## Dependency rule

- `Swgoh.Domain` depends on nothing outside the BCL.
- `Swgoh.Application` depends on Domain only, except DI abstractions for registration.
- `Swgoh.Infrastructure` depends on Application and Domain.
- `Swgoh.Api` composes Application + Infrastructure and exposes transport endpoints.
- `Swgoh.Blazor` consumes Api over HTTP and never references Infrastructure.
- `Swgoh.AppHost` only orchestrates resources and projects.
- `Swgoh.ServiceDefaults` contains cross-cutting host defaults only.

Never introduce a dependency from an inner layer to an outer layer.

## Coding rules

- Use async I/O exclusively and propagate `CancellationToken`.
- 4 spaces for C#; UTF-8; LF; file-scoped namespaces.
- Nullable remains enabled and warnings are errors.
- Validate public arguments early.
- Prefer constructor injection.
- Avoid static mutable state, service locator, warning suppressions, TODOs and machine-specific paths.
- Keep package versions in `Directory.Packages.props`.
- Do not leak MongoDB, HTTP or provider DTOs into Domain/Application.

## Testing

- Domain tests cover invariants.
- Application tests use fakes for ports.
- Infrastructure persistence tests use isolated MongoDB via Testcontainers.
- Tests must be independent and deterministic.

## Commands

```powershell
dotnet restore Swgoh.sln
dotnet format Swgoh.sln --verify-no-changes --no-restore
dotnet build Swgoh.sln --configuration Release --no-restore
dotnet test Swgoh.sln --configuration Release --no-build --no-restore
```

Never claim verification passed unless it actually ran.
