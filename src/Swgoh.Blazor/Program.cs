using Microsoft.Extensions.Caching.Memory;

using Swgoh.Blazor.Clients;
using Swgoh.Blazor.Components;
using Swgoh.Blazor.State;

const string unitAssetClientName = "swgoh-unit-assets";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient(unitAssetClientName, client =>
{
    client.BaseAddress = new Uri("https://game-assets.swgoh.gg/textures/", UriKind.Absolute);
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient<PlayerApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacScoutingCacheApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacPlannerApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacPlannerPerformanceApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacDefenseStrategyApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacJointRoundOptimizerApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacAttackExecutionApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacHistoryApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<ConquestApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<ConquestDailyPlanApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<RiseOfEmpireApiClient>(client =>
{
    client.BaseAddress = new Uri("https+http://api");
    client.Timeout = TimeSpan.FromMinutes(10);
});
builder.Services.AddScoped<PlayerSessionState>();
builder.Services.AddScoped<PlayerPreferenceService>();
builder.Services.AddScoped<PlayerContextService>();

WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/unit-assets/{assetName}", async Task<IResult> (
    string assetName,
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    CancellationToken cancellationToken) =>
{
    string? normalizedAssetName = NormalizeUnitAssetName(assetName);
    if (normalizedAssetName is null)
    {
        return Results.BadRequest();
    }

    string cacheKey = $"unit-asset:{normalizedAssetName}";
    if (!cache.TryGetValue(cacheKey, out byte[]? payload) || payload is null)
    {
        HttpClient httpClient = httpClientFactory.CreateClient(unitAssetClientName);
        using HttpResponseMessage response = await httpClient.GetAsync(
            Uri.EscapeDataString(normalizedAssetName),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.NotFound();
        }

        if (!response.IsSuccessStatusCode)
        {
            return Results.StatusCode((int)response.StatusCode);
        }

        payload = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        cache.Set(cacheKey, payload, TimeSpan.FromHours(6));
    }

    context.Response.Headers.CacheControl = "public,max-age=86400";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(payload, "image/png");
});
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();
app.Run();

static string? NormalizeUnitAssetName(string assetName)
{
    if (string.IsNullOrWhiteSpace(assetName))
    {
        return null;
    }

    string normalized = assetName.Trim();
    if (normalized.Length > 128
        || !normalized.StartsWith("tex.", StringComparison.OrdinalIgnoreCase)
        || normalized.Any(character => !char.IsLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
    {
        return null;
    }

    return normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? normalized
        : $"{normalized}.png";
}
