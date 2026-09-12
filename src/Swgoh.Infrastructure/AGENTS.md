# Swgoh.Infrastructure Instructions

- Implement Application ports here.
- `MongoDb.Generic.Repository` stays behind infrastructure adapters.
- Use async I/O and propagate CancellationToken.
- Keep MongoDB filtering/paging server-side.
- Provider DTOs remain internal and are mapped before entering Application.
- Use typed HttpClient + resilience for external providers.
- Integration tests use isolated MongoDB containers.
