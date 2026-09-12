# Swgoh.Api Instructions

- Api is a thin transport adapter and composition root.
- Endpoints call Application only.
- Never use Mongo repositories directly from endpoints.
- Validate transport input and propagate request cancellation.
- Do not expose provider payloads, stack traces or secrets.
