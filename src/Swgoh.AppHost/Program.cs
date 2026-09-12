IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<MongoDBServerResource> mongo = builder.AddMongoDB("mongodb").WithDataVolume();
IResourceBuilder<MongoDBDatabaseResource> database = mongo.AddDatabase("swgoh");

IResourceBuilder<ProjectResource> api = builder
    .AddProject<Projects.Swgoh_Api>("api")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.Swgoh_Blazor>("blazor").WithReference(api).WaitFor(api);
builder.Build().Run();
