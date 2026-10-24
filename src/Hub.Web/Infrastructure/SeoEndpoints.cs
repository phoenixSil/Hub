using System.Text;
using System.Xml;
using Hub.Modules.Catalog.Application;
using Hub.Modules.Content.Application;

namespace Hub.Web.Infrastructure;

public static class SeoEndpoints
{
    public static void MapSeo(this IEndpointRouteBuilder app)
    {
        app.MapGet("/robots.txt", (HttpContext ctx) =>
        {
            var baseUrl = BaseUrl(ctx);
            var text = $"User-agent: *\nDisallow: /admin\nDisallow: /compte\nDisallow: /api\nSitemap: {baseUrl}/sitemap.xml\n";
            return Results.Text(text, "text/plain", Encoding.UTF8);
        });

        app.MapGet("/sitemap.xml", async (HttpContext ctx, ICatalogQueries catalog, IContentQueries content) =>
        {
            var baseUrl = BaseUrl(ctx);
            var paths = new List<string> { "/", "/devis", "/rendez-vous", "/contact", "/faq", "/secteurs" };

            foreach (var category in await catalog.GetCatalogAsync(ctx.RequestAborted))
                paths.AddRange(category.Services.Select(s => s.Url));

            var site = await content.GetAsync(ctx.RequestAborted);
            paths.AddRange(site.Areas.Select(a => $"/secteurs/{a.Slug}"));
            paths.AddRange(site.Pages.Select(p => $"/{p.Slug}"));

            var sb = new StringBuilder();
            using (var xml = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
            {
                xml.WriteStartDocument();
                xml.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
                foreach (var path in paths.Distinct())
                {
                    xml.WriteStartElement("url");
                    xml.WriteElementString("loc", baseUrl + path);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
                xml.WriteEndDocument();
            }
            return Results.Text(sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\""), "application/xml", Encoding.UTF8);
        });
    }

    public static string BaseUrl(HttpContext ctx) => $"{ctx.Request.Scheme}://{ctx.Request.Host}";
}
