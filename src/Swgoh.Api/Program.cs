using Swgoh.Api.Endpoints;
using Swgoh.Application;
using Swgoh.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.MapDefaultEndpoints();
app.MapPlayerEndpoints();
app.Run();

public partial class Program;
