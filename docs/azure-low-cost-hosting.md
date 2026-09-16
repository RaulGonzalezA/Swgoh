# Azure low-cost hosting

This deployment is intentionally optimized for a small guild beta and the lowest practical recurring cost.

## Target architecture

- Azure Container Apps Consumption environment in `spaincentral`.
- `swgoh-blazor`: external ingress, 0.25 vCPU / 0.5 GiB, 0-1 replicas.
- `swgoh-api`: internal ingress, 0.25 vCPU / 0.5 GiB, 0-1 replicas.
- `swgoh-comlink`: internal ingress, 0.25 vCPU / 0.5 GiB, 0-1 replicas.
- `swgoh-stats`: internal ingress, 0.25 vCPU / 0.5 GiB, 0-1 replicas.
- Azure Cosmos DB for MongoDB with the lifetime Free Tier enabled and a shared 1,000 RU/s `swgoh` database.
- GitHub Container Registry instead of Azure Container Registry.
- GitHub Actions instead of a paid build service.
- Container Apps built-in secrets instead of Key Vault for the first beta.
- Container Apps logging destination set to `none`; real-time log streaming remains available.
- Default `azurecontainerapps.io` hostname initially. A custom domain can later use a free managed certificate.

## Cost controls

The Consumption plan should always keep `minReplicas=0`. All four apps are capped at one replica until load data proves that more capacity is needed.

The Container Apps monthly free grant is shared by the subscription: 180,000 vCPU-seconds, 360,000 GiB-seconds and two million HTTP requests. At the minimum 0.25 vCPU / 0.5 GiB allocation, the CPU and memory grants each correspond to about 200 accumulated replica-hours per month.

Cosmos DB Free Tier must be enabled when the account is created. The free allowance is 1,000 RU/s and 25 GB for the life of the account. Only one Free Tier Cosmos account is available per Azure subscription.

GitHub Packages is free for public packages and GitHub Actions standard runners are free for public repositories. The first publication of a GHCR package is private by default, so make `swgoh-api` and `swgoh-blazor` public once after the first image publication. Public container packages can then be pulled by Azure anonymously.

## Important Cosmos DB compatibility gate

The production code continues to use `MongoDB.Driver` through the repository abstraction. Cosmos DB's MongoDB API is wire-compatible for many MongoDB workloads, but it is not the MongoDB server used by the integration test suite.

Before moving production data to Cosmos DB:

1. Provision the free account with `infra/azure/provision-minimal.sh` in a disposable resource group or subscription.
2. Point a test run at the Cosmos connection string.
3. Exercise player refresh, snapshot history, GAC planner concurrency, RotE guild sync and all repository index creation paths.
4. If any MongoDB behavior is incompatible, use MongoDB Atlas Free temporarily instead. No application code change is required because the connection string remains `ConnectionStrings:swgoh`.

## First deployment

Prerequisites:

- Azure CLI authenticated to the intended subscription.
- Container Apps CLI extension available; the script installs or updates it.
- `ghcr.io/raulgonzaleza/swgoh-api:main` and `ghcr.io/raulgonzaleza/swgoh-blazor:main` already published and made public.
- No existing Cosmos DB Free Tier account in the subscription, unless Atlas or another MongoDB endpoint will be used instead.

Run:

```bash
COSMOS_ACCOUNT=swgoh-<globally-unique-name> bash infra/azure/provision-minimal.sh
```

Optional overrides:

```bash
LOCATION=westeurope \
RESOURCE_GROUP=rg-swgoh-prod \
ENVIRONMENT=cae-swgoh-prod \
COSMOS_ACCOUNT=swgoh-<globally-unique-name> \
bash infra/azure/provision-minimal.sh
```

The script prints the public Blazor URL when provisioning completes.

## Continuous deployment

`.github/workflows/azure-deploy.yml` publishes the API and Blazor images to GHCR. Azure deployment is enabled when these repository variables exist:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Use an Azure federated credential for GitHub Actions rather than storing an Azure client secret. The deployment updates only the API and Blazor image revisions; Comlink, SWGOH Stats and Cosmos DB are long-lived infrastructure resources.

The existing `NUGET_PAT` secret remains required while the API image restores `MongoDb.Generic.*` packages from GitHub Packages.

## Production progression

Do not add paid services until there is evidence that they are needed.

1. Beta: scale to zero, no persisted Azure logs, built-in Container Apps secrets, one replica maximum.
2. If cold starts become annoying, first keep only Blazor at one minimum replica and measure the bill.
3. If diagnostics become necessary, enable Azure Monitor selectively instead of ingesting every console log by default.
4. Add Key Vault only when secret rotation or centralized audit requirements justify it.
5. Raise `maxReplicas` only after authentication and distributed Blazor/session behavior are ready.
6. Move from Cosmos Free Tier or Atlas Free only when storage, RU/s or MongoDB compatibility requires it.
