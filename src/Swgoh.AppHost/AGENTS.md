# Swgoh.AppHost Instructions

- AppHost only describes topology and resource dependencies.
- Do not place business logic here.
- Resource names become configuration/service-discovery contracts; rename deliberately.
- Secrets must use Aspire parameters/secrets, never source literals.
