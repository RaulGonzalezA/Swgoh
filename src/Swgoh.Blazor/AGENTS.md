# Swgoh.Blazor Instructions

- Consume Swgoh.Api through typed HttpClient services.
- Never reference Infrastructure or MongoDB packages.
- Keep HTTP access out of Razor components when it can live in a client/service.
- Components handle loading, empty, not-found and error states explicitly.
- Do not duplicate business invariants in UI.
