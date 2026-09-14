var builder = DistributedApplication.CreateBuilder(args);

var mongo = string.IsNullOrWhiteSpace(builder.Configuration["ConnectionStrings:swgoh"])
    ? builder.AddMongoDB("mongodb").WithDataVolume()
    : null;
IResourceBuilder<IResourceWithConnectionString> database = mongo is null
    ? builder.AddConnectionString("swgoh")
    : mongo.AddDatabase("swgoh");

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
    .WaitFor(comlink)
    .WaitFor(stats);

if (mongo is not null)
{
    api.WaitFor(mongo);
}

builder
    .AddProject<Projects.Swgoh_Blazor>("blazor")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
