# Infrastructure Integration Test Instructions

- Use real MongoDB through Testcontainers for persistence behavior.
- Use a unique database per test fixture/test boundary.
- Never depend on developer-local MongoDB state.
- Cover identifier/index semantics, cancellation and repository mapping.
- Docker absence means tests were not run, not that they passed.
