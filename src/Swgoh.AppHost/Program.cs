var builder = DistributedApplication.CreateBuilder(args);

var mongo = builder.AddMongoDB("mongodb").WithDataVolume();
var database = mongo.AddDatabase("swgoh");

var comlink = builder
    .AddContainer("comlink", "ghcr.io/swgoh-utils/swgoh-comlink", "latest")
    .WithHttpEndpoint(port: 3000, targetPort: 3000, name: "http");

var api = builder
    .AddProject<Projects.Swgoh_Api>("api")
    .WithReference(database)
    .WithEnvironment("Swgoh__Comlink__BaseUrl", "http://localhost:3000")
    .WaitFor(database)
    .WaitFor(comlink);

builder
    .AddProject<Projects.Swgoh_Blazor>("blazor")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
