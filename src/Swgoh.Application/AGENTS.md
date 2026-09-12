# Swgoh.Application Instructions

- References Domain only, except DI abstractions for registration.
- Define ports here; implement them in Infrastructure.
- Do not expose MongoDB, repository-package, HTTP or provider-specific types.
- Use cases accept and propagate CancellationToken.
- Transport concerns do not belong here.
