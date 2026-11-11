using System.Globalization;
using System.Net;
using Hub.Modules.GiftCards.Domain;

namespace Hub.Modules.GiftCards.Application;

/// <summary>Courriels des cartes-cadeaux : mise en page en tableaux et styles en ligne, lisible dans toutes les messageries.</summary>
internal static class GiftCardEmails
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");
    private static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
    private static string Money(decimal d) => GiftCardService.Money(d);
    private static string Site(GiftCardContext c) => c.SiteUrl.TrimEnd('/');

    public static (string Subject, string Html) RequestReceived(GiftCard card, GiftCardContext c)
    {
        var payment = string.IsNullOrWhiteSpace(c.PaymentInstructions)
            ? $"Nous vous contactons dans un délai d'un jour ouvrable pour le paiement. Vous pouvez aussi nous joindre au {E(c.Phone)}."
            : E(c.PaymentInstructions).Replace("\n", "<br>");
        var when = card.DeliverOn is { } d
            ? $"Elle sera envoyée le <strong>{d.ToString("d MMMM yyyy", Fr)}</strong>, une fois le paiement reçu."
            : "Elle sera envoyée dès la réception du paiement.";
        var body = $"""
            <p style="margin:0 0 14px">Bonjour {E(card.BuyerName)},</p>
            <p style="margin:0 0 14px">Merci ! Nous avons bien reçu votre demande de carte-cadeau de <strong>{Money(card.Amount)}</strong>
              pour <strong>{E(card.RecipientName)}</strong>. {when}</p>
            <div style="margin:0 0 16px;padding:14px 16px;background:#f4f7fa;border-radius:8px">
              <p style="margin:0 0 6px;font-weight:700">Paiement</p>
              <p style="margin:0;line-height:1.6">{payment}</p>
              <p style="margin:8px 0 0;color:#5b6b7a;font-size:13px">Référence à indiquer : {E(card.Code[..4])}</p>
            </div>
            <p style="margin:0">La carte n'a pas de date d'expiration et s'utilise sur tous nos services.</p>
            """;
        return ($"Votre carte-cadeau {c.CompanyName} : demande reçue", Layout(c, body));
    }

    public static (string Subject, string Html) AdminNotice(GiftCard card, GiftCardContext c)
    {
        var body = $"""
            <p style="margin:0 0 12px"><strong>Nouvelle demande de carte-cadeau : {Money(card.Amount)}</strong></p>
            <p style="margin:0 0 4px">De : {E(card.BuyerName)} ({E(card.BuyerEmail)}{(card.BuyerPhone is null ? "" : ", " + E(card.BuyerPhone))})</p>
            <p style="margin:0 0 4px">Pour : {E(card.RecipientName)}{(card.RecipientEmail is null ? " (remise par l'acheteur)" : $" ({E(card.RecipientEmail)})")}</p>
            <p style="margin:0 0 12px">Envoi : {(card.DeliverOn is { } d ? d.ToString("d MMMM yyyy", Fr) : "dès le paiement")}</p>
            <p style="margin:0">Une fois le paiement reçu, cliquez « Paiement reçu » dans
              <a href="{E(Site(c))}/admin/cartes-cadeaux" style="color:{E(c.PrimaryColor)}">Cartes-cadeaux</a> : la carte part toute seule.</p>
            """;
        return ($"Carte-cadeau demandée : {Money(card.Amount)} par {card.BuyerName}", Layout(c, body));
    }

    public static (string Subject, string Html) Card(GiftCard card, GiftCardContext c)
    {
        var forBuyer = card.RecipientEmail is null;
        var intro = forBuyer
            ? $"<p style=\"margin:0 0 16px\">Bonjour {E(card.BuyerName)}, voici la carte-cadeau à offrir à <strong>{E(card.RecipientName)}</strong>. Vous pouvez transférer ce courriel ou l'imprimer.</p>"
            : $"<p style=\"margin:0 0 16px\">Bonjour {E(card.RecipientName)}, <strong>{E(card.BuyerName)}</strong> vous offre une carte-cadeau {E(c.CompanyName)} !</p>";
        var message = string.IsNullOrWhiteSpace(card.Message) ? "" :
            $"<p style=\"margin:16px 0 0;font-style:italic;color:#ffffff;opacity:.95\">« {E(card.Message).Replace("\n", "<br>")} »</p><p style=\"margin:4px 0 0;color:#ffffff;opacity:.85\">— {E(card.BuyerName)}</p>";
        var logo = c.LogoUrl is null ? "" : $"<img src=\"{E(c.LogoUrl)}\" alt=\"{E(c.CompanyName)}\" width=\"150\" style=\"display:block;max-width:150px;height:auto;margin:0 0 18px;background:#ffffff;border-radius:8px;padding:8px\">";
        var body = $"""
            {intro}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:separate;border-radius:16px;background:{ReadableOnWhiteText(c.PrimaryColor)};">
              <tr><td style="padding:26px 26px 22px;color:#ffffff">
                {logo}
                <p style="margin:0;font-size:13px;letter-spacing:.12em;text-transform:uppercase;color:#ffffff;opacity:.9">Carte-cadeau</p>
                <p style="margin:4px 0 0;font-size:44px;line-height:1.1;font-weight:800;color:#ffffff">{Money(card.Amount)}</p>
                <p style="margin:6px 0 0;font-size:15px;color:#ffffff">Pour {E(card.RecipientName)}</p>
                {message}
                <table role="presentation" cellpadding="0" cellspacing="0" style="margin:20px 0 0"><tr>
                  <td style="background:#ffffff;border-radius:10px;padding:10px 16px">
                    <p style="margin:0;font-size:11px;letter-spacing:.1em;text-transform:uppercase;color:#5b6b7a">Code</p>
                    <p style="margin:2px 0 0;font-size:22px;font-weight:800;letter-spacing:.08em;color:#0f2533;font-family:Consolas,Menlo,monospace">{E(card.Code)}</p>
                  </td></tr></table>
              </td></tr>
              <tr><td style="height:8px;background:{E(c.AccentColor)};border-radius:0 0 16px 16px;font-size:0;line-height:0">&nbsp;</td></tr>
            </table>
            <p style="margin:18px 0 6px;font-weight:700">Comment l'utiliser</p>
            <p style="margin:0 0 6px;line-height:1.6">Demandez un devis ou un rendez-vous sur <a href="{E(Site(c))}" style="color:{E(c.PrimaryColor)}">{E(Site(c).Replace("https://", ""))}</a>
              ou au {E(c.Phone)}, et donnez ce code : le montant est déduit de votre facture.</p>
            <p style="margin:0;color:#5b6b7a;font-size:13px">Sans date d'expiration · utilisable en une ou plusieurs fois ·
              <a href="{E(Site(c))}/carte-cadeau?code={Uri.EscapeDataString(card.Code)}" style="color:#5b6b7a">vérifier le solde</a></p>
            """;
        var subject = forBuyer
            ? $"Votre carte-cadeau {c.CompanyName} de {Money(card.Amount)} pour {card.RecipientName}"
            : $"{card.BuyerName} vous offre une carte-cadeau {c.CompanyName}";
        return (subject, Layout(c, body));
    }

    public static (string Subject, string Html) Delivered(GiftCard card, GiftCardContext c)
    {
        var body = $"""
            <p style="margin:0 0 14px">Bonjour {E(card.BuyerName)},</p>
            <p style="margin:0">Votre carte-cadeau de <strong>{Money(card.Amount)}</strong> vient d'être envoyée à
              <strong>{E(card.RecipientName)}</strong> ({E(card.RecipientEmail)}). Merci de faire briller le quotidien de quelqu'un !</p>
            """;
        return ($"Votre carte-cadeau a été envoyée à {card.RecipientName}", Layout(c, body));
    }

    /// <summary>
    /// La couleur de la marque, assombrie juste assez pour que le texte blanc posé dessus reste lisible
    /// (contraste d'au moins 4,5:1, norme AA).
    /// </summary>
    internal static string ReadableOnWhiteText(string hex)
    {
        if (!TryParse(hex, out var r, out var g, out var b)) return "#0b3954";
        for (var i = 0; i < 30 && Contrast(r, g, b) < 4.6; i++) { r *= 0.92; g *= 0.92; b *= 0.92; }
        return $"#{(int)r:x2}{(int)g:x2}{(int)b:x2}";
    }

    internal static double Contrast(double r, double g, double b)
    {
        static double Ch(double v) { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        var l = 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(b);
        return 1.05 / (l + 0.05);
    }

    private static bool TryParse(string hex, out double r, out double g, out double b)
    {
        r = g = b = 0;
        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        r = (v >> 16) & 255; g = (v >> 8) & 255; b = v & 255;
        return true;
    }

    private static string Layout(GiftCardContext c, string body) => $"""
        <!doctype html>
        <html lang="fr"><body style="margin:0;padding:0;background:#eef3f7">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#eef3f7"><tr><td align="center" style="padding:24px 12px">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:560px;background:#ffffff;border-radius:12px">
            <tr><td style="padding:26px 26px 22px;font-family:Arial,Helvetica,sans-serif;font-size:15px;color:#0f2533">
              {body}
            </td></tr>
            <tr><td style="padding:14px 26px;border-top:1px solid #dde6ed;font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#5b6b7a">
              {E(c.CompanyName)} · {E(c.Phone)} · <a href="{E(Site(c))}" style="color:#5b6b7a">{E(Site(c).Replace("https://", ""))}</a>
            </td></tr>
          </table>
        </td></tr></table>
        </body></html>
        """;
}
