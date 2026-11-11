using Hub.Modules.GiftCards.Application;
using Hub.Modules.Platform.Application;

namespace Hub.Web.Infrastructure;

/// <summary>L'entreprise courante, vue par les courriels des cartes-cadeaux.</summary>
internal sealed class GiftCardContextSource(ICurrentTenant tenant) : IGiftCardContextSource
{
    public async Task<GiftCardContext> GetAsync(CancellationToken ct = default)
    {
        var t = await tenant.GetAsync(ct);
        var site = (t.SiteUrl ?? "").TrimEnd('/');
        var logo = SiteFormat.VisualLogo(t);
        return new GiftCardContext(
            t.Name, t.Phone, t.PublicEmail, t.AdminNotificationEmail, site,
            string.IsNullOrWhiteSpace(t.PrimaryColor) ? "#0097ce" : t.PrimaryColor,
            string.IsNullOrWhiteSpace(t.AccentColor) ? "#38d430" : t.AccentColor,
            logo.StartsWith('/') ? site + logo : logo,
            t.GiftCardPaymentInstructions);
    }
}
