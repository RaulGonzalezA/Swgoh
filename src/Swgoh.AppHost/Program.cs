var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongodb").WithDataVolume();
var database = mongo.AddDatabase("swgoh");

var comlink = builder
    .AddContainer("comlink", "ghcr.io/swgoh-utils/swgoh-comlink", "latest")
    .WithEnvironment("APP_NAME", "swgoh")
    .WithEnvironment("PORT", "3000")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http");

var stats = builder
    .AddContainer("swgoh-stats", "ghcr.io/swgoh-utils/swgoh-stats", "latest")
    .WithEnvironment("PORT", "3223")
    .WithEnvironment("CLIENT_URL", "http://comlink:3000")
    .WithHttpEndpoint(port: 3223, targetPort: 3223, name: "http")
    .WaitFor(comlink);

var api = builder
    .AddProject<Projects.Swgoh_Api>("api")
    .WithReference(database)
    .WithEnvironment("Swgoh__Comlink__BaseUrl", "http://localhost:3000")
    .WithEnvironment("Swgoh__Stats__BaseUrl", "http://localhost:3223")
    .WaitFor(database)
    .WaitFor(comlink)
    .WaitFor(stats);

builder
    .AddProject<Projects.Swgoh_Blazor>("blazor")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
