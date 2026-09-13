using Swgoh.Blazor.Clients;
using Swgoh.Blazor.Components;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddHttpClient<PlayerApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacApiClient>(client => client.BaseAddress = new Uri("https+http://api"));
builder.Services.AddHttpClient<GacPlannerApiClient>(client => client.BaseAddress = new Uri("https+http://api"));

WebApplication app = builder.Build();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapDefaultEndpoints();
app.Run();
