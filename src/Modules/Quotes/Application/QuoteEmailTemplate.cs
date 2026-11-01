using System.Globalization;
using System.Net;
using System.Text;
using Hub.Modules.Quotes.Domain;
using Hub.SharedKernel.Tenancy;

namespace Hub.Modules.Quotes.Application;

/// <summary>Gabarit HTML du courriel de proposition : message de l'administrateur, tableau des prix, mention des taxes.</summary>
internal static class QuoteEmailTemplate
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    public static string Render(TenantInfo t, string message, IReadOnlyList<QuoteLine> lines, decimal total, string taxNote)
    {
        static string E(string? v) => WebUtility.HtmlEncode(v ?? string.Empty);
        string Money(decimal v) => v.ToString("#,##0.00 $", Fr);

        var paragraphs = string.Join("", message.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => $"<p style=\"margin:0 0 14px\">{E(p.Trim()).Replace("\n", "<br>")}</p>"));

        var rows = new StringBuilder();
        foreach (var l in lines)
        {
            var qty = l.Unit == "forfait" ? "" : $"{l.Quantity.ToString("0.##", Fr)} {E(Plural(l.Unit, l.Quantity))} × {Money(l.UnitPrice)}";
            rows.Append($"<tr><td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0\">{E(l.Description)}<div style=\"color:#64748b;font-size:13px\">{qty}</div></td>" +
                        $"<td style=\"padding:8px 0;border-bottom:1px solid #e2e8f0;text-align:right;white-space:nowrap\">{(l.Total == 0 ? "Gratuit" : Money(l.Total))}</td></tr>");
        }

        var table = $"""
            <table style="width:100%;border-collapse:collapse;font-size:14px;margin:8px 0 6px">{rows}
              <tr><td style="padding:10px 0;font-weight:bold">Total</td><td style="padding:10px 0;text-align:right;font-weight:bold;white-space:nowrap">{Money(total)}</td></tr>
            </table>
            <p style="color:#64748b;font-size:13px;margin:0 0 18px">{E(taxNote)}</p>
            """;

        return $"""
            <!DOCTYPE html>
            <html lang="fr"><head><meta charset="utf-8"></head>
            <body style="margin:0;padding:24px;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#1e293b">
              <div style="max-width:600px;margin:0 auto;background:#fff;border-radius:8px;overflow:hidden">
                <div style="background:{E(t.PrimaryColor)};padding:20px 24px">
                  <div style="color:#fff;font-size:20px;font-weight:bold">{E(t.Name)}</div>
                  <div style="color:#fff;opacity:.85;font-size:13px">{E(t.Tagline)}</div>
                </div>
                <div style="padding:24px">
                  <h1 style="font-size:18px;margin:0 0 16px">Votre proposition de prix</h1>
                  {paragraphs}
                  {table}
                  <p style="margin:0">Pour accepter ou poser une question, répondez simplement à ce courriel ou appelez-nous au {E(t.Phone)}.</p>
                </div>
                <div style="padding:16px 24px;border-top:1px solid #e2e8f0;color:#94a3b8;font-size:12px">
                  {E(t.Name)} · {E(t.Phone)} · {E(t.PublicEmail)}
                </div>
              </div>
            </body></html>
            """;
    }

    private static string Plural(string unit, decimal qty) => qty > 1 && !unit.EndsWith('s') ? unit + "s" : unit;
}
