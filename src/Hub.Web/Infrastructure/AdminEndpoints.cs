using System.Globalization;
using System.Text;
using Hub.Modules.Content.Application;
using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Domain;
using Hub.Modules.Platform.Domain;
using Hub.SharedKernel.Tenancy;
using Hub.SharedKernel.Time;

namespace Hub.Web.Infrastructure;

public static class AdminEndpoints
{
    private const string OAuthStateCookie = "hub_oauth_state";

    /// <summary>
    /// Connexion d'un réseau par OAuth (ex. Google Business) : /admin/canaux/oauth/{réseau}/connecter envoie vers la page
    /// d'autorisation du réseau ; /retour échange le code et enregistre la connexion (jeton chiffré), puis revient sur Canaux.
    /// Un jeton anti-falsification (state) lie l'aller et le retour.
    /// </summary>
    public static void MapChannelOAuth(this IEndpointRouteBuilder app)
    {
        static string Redirect(HttpContext ctx, string channel) =>
            $"{ctx.Request.Scheme}://{ctx.Request.Host}/admin/canaux/oauth/{channel}/retour";

        static void UseTenant(HttpContext ctx, ITenantContext tenant)
        {
            if (Guid.TryParse(ctx.User.FindFirst(HubClaims.TenantId)?.Value, out var tenantId)) tenant.Set(tenantId);
        }

        app.MapGet("/admin/canaux/oauth/{channel}/connecter", async (string channel, HttpContext ctx, ITenantContext tenant,
            Hub.Modules.Channels.Application.IChannelConnections connections, Hub.Modules.Channels.Application.IPublisherResolver publishers,
            Hub.Modules.Channels.Application.Autopilot.IAutopilotContextSource context) =>
        {
            UseTenant(ctx, tenant);
            if (!Enum.TryParse<Hub.Modules.Channels.ChannelType>(channel, ignoreCase: true, out var type) || !connections.UsesOAuth(type))
                return Results.Redirect("/admin/canaux?erreur=" + Uri.EscapeDataString("Réseau inconnu."));
            if (publishers.IsSimulated)
            {
                var simulated = await connections.ConnectOAuthAsync(type, null, Redirect(ctx, channel), null, ctx.RequestAborted);
                return Results.Redirect("/admin/canaux?" + (simulated.IsSuccess ? "connecte=" : "erreur=") + Uri.EscapeDataString(simulated.Value ?? simulated.Error ?? ""));
            }
            var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
            var url = connections.AuthorizeUrl(type, Redirect(ctx, channel), state);
            if (url is null)
                return Results.Redirect("/admin/canaux?erreur=" + Uri.EscapeDataString(
                    "L'application Google n'est pas encore configurée : ajoutez Google__ClientId et Google__ClientSecret dans les réglages du site."));
            ctx.Response.Cookies.Append(OAuthStateCookie, state, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(15), Path = "/admin/canaux/oauth",
            });
            return Results.Redirect(url);
        }).RequireAuthorization();

        app.MapGet("/admin/canaux/oauth/{channel}/retour", async (string channel, string? code, string? state, string? error, HttpContext ctx,
            ITenantContext tenant, Hub.Modules.Channels.Application.IChannelConnections connections,
            Hub.Modules.Channels.Application.Autopilot.IAutopilotContextSource context) =>
        {
            UseTenant(ctx, tenant);
            var expected = ctx.Request.Cookies[OAuthStateCookie];
            ctx.Response.Cookies.Delete(OAuthStateCookie, new CookieOptions { Path = "/admin/canaux/oauth" });
            if (!Enum.TryParse<Hub.Modules.Channels.ChannelType>(channel, ignoreCase: true, out var type))
                return Results.Redirect("/admin/canaux?erreur=" + Uri.EscapeDataString("Réseau inconnu."));
            if (error is not null)
                return Results.Redirect("/admin/canaux?erreur=" + Uri.EscapeDataString("Connexion refusée ou annulée chez Google (" + error + ")."));
            if (string.IsNullOrEmpty(expected) || !string.Equals(expected, state, StringComparison.Ordinal))
                return Results.Redirect("/admin/canaux?erreur=" + Uri.EscapeDataString("La connexion a expiré : recommencez."));
            var company = (await context.GetAsync(ctx.RequestAborted)).CompanyName;
            var result = await connections.ConnectOAuthAsync(type, code, Redirect(ctx, channel), company, ctx.RequestAborted);
            return Results.Redirect("/admin/canaux?" + (result.IsSuccess ? "connecte=" : "erreur=") + Uri.EscapeDataString(result.Value ?? result.Error ?? ""));
        }).RequireAuthorization();
    }

    /// <summary>GET /admin/demandes/export.csv : toutes les demandes, ouvrable dans Excel (séparateur « ; », UTF-8).</summary>
    public static void MapAdminExports(this IEndpointRouteBuilder app)
    {
        app.MapGet("/admin/demandes/export.csv", async (HttpContext ctx, ITenantContext tenant, ILeadInbox leads, IContentQueries content) =>
        {
            // L'entreprise de l'administrateur connecté prime sur celle du nom de domaine.
            if (Guid.TryParse(ctx.User.FindFirst(HubClaims.TenantId)?.Value, out var tenantId))
                tenant.Set(tenantId);

            var areaNames = (await content.GetAsync(ctx.RequestAborted)).Areas
                .GroupBy(a => a.Slug).ToDictionary(g => g.Key, g => g.First().Name);
            areaNames["autre"] = "Autre secteur";
            var rows = await leads.ListAsync(new LeadListFilter(Take: 100_000), ctx.RequestAborted);
            var fr = CultureInfo.GetCultureInfo("fr-CA");
            var sb = new StringBuilder();
            string[] header =
            [
                "Reçue le", "Type", "Statut", "Nom", "Courriel", "Téléphone", "Adresse", "Secteur", "Services",
                "Propriété", "Superficie", "Fréquence", "Sujet", "Message", "Date souhaitée", "Plage",
                "Rendez-vous confirmé", "Source", "Support", "Campagne", "Page d'arrivée",
            ];
            sb.AppendLine(string.Join(';', header.Select(Csv)));

            string Local(DateTimeOffset d) => TimeZoneInfo.ConvertTime(d, BusinessTime.Zone).ToString("yyyy-MM-dd HH:mm", fr);

            foreach (var l in rows)
            {
                string?[] cells =
                [
                    Local(l.CreatedAt), LeadLabels.Of(l.Kind), LeadLabels.Of(l.Status), l.FullName, l.Email, l.Phone,
                    l.Address, l.AreaSlug is { } a ? areaNames.GetValueOrDefault(a, a) : null, l.Services, l.PropertyType, l.PropertySize, l.Frequency, l.Subject, l.Message,
                    l.PreferredDate?.ToString("yyyy-MM-dd"), l.PreferredSlot is { } s ? LeadLabels.Of(s) : null,
                    l.ConfirmedFor is { } c ? Local(c) : null, l.Source, l.Medium, l.Campaign, l.LandingPage,
                ];
                sb.AppendLine(string.Join(';', cells.Select(Csv)));
            }

            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
            var name = $"demandes-{BusinessTime.Today(TimeProvider.System):yyyy-MM-dd}.csv";
            return Results.File(bytes, "text/csv; charset=utf-8", name);
        }).RequireAuthorization();

        app.MapGet("/admin/resultats/decisions.csv", async (HttpContext ctx, ITenantContext tenant,
            Hub.Modules.Channels.Application.Decisions.IDecisionJournal journal) =>
        {
            if (Guid.TryParse(ctx.User.FindFirst(HubClaims.TenantId)?.Value, out var tenantId))
                tenant.Set(tenantId);
            var csv = await journal.ExportCsvAsync(ctx.RequestAborted);
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
            return Results.File(bytes, "text/csv; charset=utf-8", $"journal-decisions-{BusinessTime.Today(TimeProvider.System):yyyy-MM-dd}.csv");
        }).RequireAuthorization();
    }

    /// <summary>Échappe une cellule CSV et neutralise les formules (=, +, -, @) pour Excel.</summary>
    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var v = value.Replace("\r\n", " ").Replace('\n', ' ');
        if (v[0] is '=' or '+' or '-' or '@') v = "'" + v;
        return v.Contains(';') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }
}
