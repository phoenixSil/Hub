using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.Extensions.WebEncoders;
using Hub.Modules.Analytics;
using Hub.Modules.Catalog;
using Hub.Modules.Channels;
using Hub.Modules.Content;
using Hub.Modules.Leads;
using Hub.Modules.Platform;
using Hub.Modules.Platform.Application;
using Hub.Modules.Platform.Domain;
using Hub.Modules.Quotes;
using Hub.Modules.Quotes.Application;
using Hub.SharedKernel.Caching;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Events;
using Hub.SharedKernel.Modules;
using Hub.Web.Components;
using Hub.Web.Infrastructure;
using Hub.Web.Seed;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Hub");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("La chaîne de connexion « ConnectionStrings:Hub » est absente.");

// Socle
// Accents écrits tels quels dans le HTML (« é » plutôt que « &#xE9; ») : pages plus légères et lisibles.
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ContentCache>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IEventPublisher, InMemoryEventPublisher>();
builder.Services.AddHttpContextAccessor();

// Modules
builder.Services
    .AddPlatformModule(builder.Configuration, connectionString)
    .AddCatalogModule(connectionString)
    .AddContentModule(connectionString)
    .AddLeadsModule(connectionString)
    .AddAnalyticsModule(connectionString)
    .AddChannelsModule(builder.Configuration, connectionString)
    .AddQuotesModule(builder.Configuration, connectionString);
builder.Services.AddScoped<IQuoteContextSource, QuoteContextSource>();
builder.Services.AddScoped<Hub.Modules.Channels.Application.Autopilot.IAutopilotContextSource, AutopilotContextSource>();
builder.Services.AddSingleton<Hub.Modules.Channels.Application.Autopilot.IVisualRenderer, SkiaVisualRenderer>();
builder.Services.AddScoped<Hub.Modules.Channels.Application.Autopilot.IVisualStore, MediaVisualStore>();

// IA générique (rédaction des publications…) : Claude si une clé est fournie, sinon aucun (replis sans IA).
if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{AnthropicOptions.Section}:ApiKey"]))
    builder.Services.AddHttpClient<Hub.SharedKernel.Ai.IAiTextGenerator, ClaudeTextGenerator>(c => c.Timeout = TimeSpan.FromSeconds(240));
else
    builder.Services.AddSingleton<Hub.SharedKernel.Ai.IAiTextGenerator, NoAiTextGenerator>();

// Courriels : fournisseur choisi par Email:Provider, sinon détecté (Resend, Azure Communication, dossier local, journal).
builder.Services.AddEmail(builder.Configuration, builder.Environment);

// Images : Blob Storage si configuré, sinon wwwroot/uploads.
builder.Services.Configure<MediaOptions>(builder.Configuration.GetSection(MediaOptions.Section));
if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{MediaOptions.Section}:BlobConnectionString"]))
    builder.Services.AddScoped<IMediaStorage, BlobMediaStorage>();
else
    builder.Services.AddScoped<IMediaStorage, LocalMediaStorage>();

builder.Services.Configure<SeniorCreditOptions>(builder.Configuration.GetSection(SeniorCreditOptions.Section));
builder.Services.Configure<TurnstileOptions>(builder.Configuration.GetSection(TurnstileOptions.Section));
builder.Services.AddHttpClient<TurnstileVerifier>();

// Interface
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CircuitHandler, TenantCircuitHandler>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/compte/connexion";
    o.LogoutPath = "/compte/deconnexion";
    o.AccessDeniedPath = "/compte/connexion";
    o.Cookie.Name = "hub_auth";
    o.ExpireTimeSpan = TimeSpan.FromHours(12);
    o.SlidingExpiration = true;
});
builder.Services.AddAuthorization();

var app = builder.Build();
app.Logger.LogInformation("Courriels envoyés par : {Provider}", app.Services.GetRequiredService<EmailProviderInfo>().Name);

// Migrations et données de départ
await using (var scope = app.Services.CreateAsyncScope())
{
    foreach (var initializer in scope.ServiceProvider.GetServices<IModuleInitializer>().OrderBy(i => i.Order))
        await initializer.InitializeAsync(scope.ServiceProvider);

    await ScintiNetSeeder.SeedAsync(scope.ServiceProvider);
}

var fr = new CultureInfo("fr-CA");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(fr),
    SupportedCultures = [fr],
    SupportedUICultures = [fr],
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erreur", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/introuvable");
app.UseHttpsRedirection();

// Une seule adresse pour Google : scintinet.com → www.scintinet.com (si Hub:CanonicalHost est défini).
var canonicalHost = app.Configuration["Hub:CanonicalHost"];
if (!string.IsNullOrWhiteSpace(canonicalHost))
{
    app.Use(async (ctx, next) =>
    {
        var host = ctx.Request.Host.Host;
        if (canonicalHost.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            && string.Equals(host, canonicalHost[4..], StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect($"https://{canonicalHost}{ctx.Request.PathBase}{ctx.Request.Path}{ctx.Request.QueryString}", permanent: true);
            return;
        }
        await next();
    });
}
app.UseStaticFiles(); // images envoyées par le back-office (wwwroot/uploads)

app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<VisitTrackingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapSeo();
app.MapTracking();
app.MapAdminExports();
app.MapSocialImages();

app.MapPost("/compte/deconnexion", async (SignInManager<HubUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.LocalRedirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
