using Nightshift.Sonarr;
using Nightshift.Web;
using Nightshift.Web.Components;
using Nightshift.Web.Endpoints;
using Nightshift.Web.Library;
using Nightshift.Web.Sample;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<NightshiftOptions>()
    .Bind(builder.Configuration.GetSection(NightshiftOptions.SectionName))
    .Validate(o => string.IsNullOrWhiteSpace(o.TimeZone) || TimeZoneInfo.TryFindSystemTimeZoneById(o.TimeZone, out _),
        "Nightshift:TimeZone must be an IANA time zone id such as Europe/London.")
    .ValidateOnStart();

var sampleData = builder.Configuration.GetValue<bool>($"{NightshiftOptions.SectionName}:{nameof(NightshiftOptions.SampleData)}");
if (sampleData)
{
    builder.Services.AddSingleton<ISonarrClient, SampleSonarrClient>();
}
else
{
    builder.Services.AddSonarrClient(builder.Configuration);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddMemoryCache();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddHealthChecks().AddCheck<SonarrHealthCheck>("sonarr");

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (sampleData)
{
    app.Logger.LogWarning("Nightshift is running with sample data. Sonarr is not being called.");
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// No HTTPS redirection or HSTS: Nightshift is meant to sit on the LAN or behind a reverse proxy that terminates TLS.
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health");
app.MapPosterEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
