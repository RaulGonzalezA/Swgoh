using System.Globalization;
using System.Threading.RateLimiting;

using Asp.Versioning;
using Asp.Versioning.OpenApi;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using Scalar.AspNetCore;

using Swgoh.Api.Endpoints;
using Swgoh.Application;
using Swgoh.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    })
    .AddOpenApi();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromMinutes(1)
            }));

    options.AddPolicy("player-refresh", context =>
    {
        string allyCode = context.Request.RouteValues.TryGetValue("allyCode", out object? value)
            ? value?.ToString() ?? "unknown"
            : "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            allyCode,
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 1,
                QueueLimit = 0,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                Window = TimeSpan.FromSeconds(30)
            });
    });

    options.OnRejected = async (context, cancellationToken) =>
    {
        HttpResponse response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        await response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "The API rate limit has been exceeded. Retry after the indicated delay."
            },
            cancellationToken);
    };
});

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseRateLimiter();
app.MapDefaultEndpoints();
app.MapPlayerEndpoints();
app.MapSquadEndpoints();
app.MapGacEndpoints();
app.MapGacPlannerEndpoints();
app.MapGacDataEndpoints();
app.MapOpenApi().WithDocumentPerVersion();
app.MapScalarApiReference("/scalar", options =>
{
    var descriptions = app.DescribeApiVersions();
    for (int index = 0; index < descriptions.Count; index++)
    {
        var description = descriptions[index];
        options.AddDocument(
            description.GroupName,
            $"SWGOH API {description.GroupName}",
            isDefault: index == descriptions.Count - 1);
    }
});
app.Run();

public partial class Program;
