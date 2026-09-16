#!/usr/bin/env bash
set -euo pipefail

LOCATION="${LOCATION:-spaincentral}"
RESOURCE_GROUP="${RESOURCE_GROUP:-rg-swgoh-prod}"
ENVIRONMENT="${ENVIRONMENT:-cae-swgoh-prod}"
COSMOS_ACCOUNT="${COSMOS_ACCOUNT:-swgoh-$RANDOM-$RANDOM}"
COSMOS_DATABASE="${COSMOS_DATABASE:-swgoh}"
API_IMAGE="${API_IMAGE:-ghcr.io/raulgonzaleza/swgoh-api:main}"
BLAZOR_IMAGE="${BLAZOR_IMAGE:-ghcr.io/raulgonzaleza/swgoh-blazor:main}"
COMLINK_IMAGE="${COMLINK_IMAGE:-ghcr.io/swgoh-utils/swgoh-comlink:latest}"
STATS_IMAGE="${STATS_IMAGE:-ghcr.io/swgoh-utils/swgoh-stats:latest}"

az extension add --name containerapp --upgrade --only-show-errors >/dev/null
az provider register --namespace Microsoft.App --wait
az provider register --namespace Microsoft.DocumentDB --wait

az group create \
  --name "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --output none

az containerapp env create \
  --name "$ENVIRONMENT" \
  --resource-group "$RESOURCE_GROUP" \
  --location "$LOCATION" \
  --logs-destination none \
  --output none

az cosmosdb create \
  --name "$COSMOS_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --locations regionName="$LOCATION" failoverPriority=0 isZoneRedundant=False \
  --kind MongoDB \
  --enable-free-tier true \
  --default-consistency-level Session \
  --output none

az cosmosdb mongodb database create \
  --account-name "$COSMOS_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --name "$COSMOS_DATABASE" \
  --throughput 1000 \
  --output none

MONGO_CONNECTION="$(az cosmosdb keys list \
  --name "$COSMOS_ACCOUNT" \
  --resource-group "$RESOURCE_GROUP" \
  --type connection-strings \
  --query 'connectionStrings[0].connectionString' \
  --output tsv)"

az containerapp create \
  --name swgoh-comlink \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$ENVIRONMENT" \
  --image "$COMLINK_IMAGE" \
  --ingress internal \
  --target-port 3000 \
  --cpu 0.25 \
  --memory 0.5Gi \
  --min-replicas 0 \
  --max-replicas 1 \
  --env-vars APP_NAME=swgoh PORT=3000 \
  --output none

az containerapp create \
  --name swgoh-stats \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$ENVIRONMENT" \
  --image "$STATS_IMAGE" \
  --ingress internal \
  --target-port 3223 \
  --cpu 0.25 \
  --memory 0.5Gi \
  --min-replicas 0 \
  --max-replicas 1 \
  --env-vars PORT=3223 CLIENT_URL=http://swgoh-comlink \
  --output none

az containerapp create \
  --name swgoh-api \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$ENVIRONMENT" \
  --image "$API_IMAGE" \
  --ingress internal \
  --target-port 8080 \
  --cpu 0.25 \
  --memory 0.5Gi \
  --min-replicas 0 \
  --max-replicas 1 \
  --secrets mongodb="$MONGO_CONNECTION" \
  --env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__swgoh=secretref:mongodb \
    Swgoh__Comlink__BaseUrl=http://swgoh-comlink \
    Swgoh__Stats__BaseUrl=http://swgoh-stats \
    Swgoh__GameData__Locale=SPA_XM \
  --output none

az containerapp create \
  --name swgoh-blazor \
  --resource-group "$RESOURCE_GROUP" \
  --environment "$ENVIRONMENT" \
  --image "$BLAZOR_IMAGE" \
  --ingress external \
  --target-port 8080 \
  --cpu 0.25 \
  --memory 0.5Gi \
  --min-replicas 0 \
  --max-replicas 1 \
  --env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    Swgoh__Api__BaseUrl=http://swgoh-api \
  --output none

FQDN="$(az containerapp show \
  --name swgoh-blazor \
  --resource-group "$RESOURCE_GROUP" \
  --query properties.configuration.ingress.fqdn \
  --output tsv)"

printf '\nSWGOH provisioned.\n'
printf 'Resource group: %s\n' "$RESOURCE_GROUP"
printf 'Region: %s\n' "$LOCATION"
printf 'Cosmos account: %s\n' "$COSMOS_ACCOUNT"
printf 'Application: https://%s\n' "$FQDN"
