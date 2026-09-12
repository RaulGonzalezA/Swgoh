# SWGOH

Aplicación .NET 10 para consultar, persistir y analizar información de **Star Wars: Galaxy of Heroes**.

## Arquitectura

```text
Swgoh.AppHost
 ├── Swgoh.Api
 │    ├── Swgoh.Application
 │    │    └── Swgoh.Domain
 │    ├── Swgoh.Infrastructure
 │    │    ├── Swgoh.Application
 │    │    └── Swgoh.Domain
 │    └── Swgoh.ServiceDefaults
 ├── Swgoh.Blazor
 │    └── Swgoh.ServiceDefaults
 └── MongoDB

tests
 ├── Swgoh.Domain.UnitTests
 ├── Swgoh.Application.UnitTests
 └── Swgoh.Infrastructure.IntegrationTests
```

Las dependencias apuntan hacia dentro: Domain no conoce infraestructura, Application no conoce MongoDB y Blazor consume la API por HTTP.

## Persistencia

`Swgoh.Infrastructure` usa `MongoDb.Generic.Repository` 3.0.0 encapsulado detrás de `IPlayerRepository`.

## Ejecutar

```powershell
dotnet restore Swgoh.sln
dotnet build Swgoh.sln --configuration Release --no-restore
dotnet test Swgoh.sln --configuration Release --no-build --no-restore
dotnet run --project src/Swgoh.AppHost
```

Lee `AGENTS.md` antes de modificar el repositorio.
