using Hub.Modules.Leads.Application;
using Hub.SharedKernel.Email;
using Hub.SharedKernel.Tenancy;

namespace Hub.UnitTests;

public class LeadReplyTests
{
    private static readonly TenantInfo Tenant = new(Guid.NewGuid(), "ScintiNet", "L'exigence du propre et du net !", "contact@scintinet.com",
        "418 953-3284", "admin@x.com", "#0097CE", "https://www.scintinet.com");

    [Fact]
    public void Le_texte_colle_devient_des_paragraphes_et_des_listes()
    {
        var html = LeadEmails.Reply("Bonjour Madame Renard,\n\nVoici nos services :\n- Entretien ménager\n- Repas <maison>\n\nCordialement,\nSillery", Tenant);
        Assert.Contains($"<p style=\"margin:0 0 14px\">{System.Net.WebUtility.HtmlEncode("Bonjour Madame Renard,")}</p>", html);
        // Une phrase d'introduction suivie de puces : un paragraphe, puis une vraie liste.
        Assert.Contains("Voici nos services :</p><ul", html);
        Assert.Contains($">{System.Net.WebUtility.HtmlEncode("Entretien ménager")}</li>", html);
        Assert.Contains("Repas &lt;maison&gt;", html); // le texte est encodé
        Assert.Contains("Cordialement,<br>Sillery", html);
        Assert.Contains("418 953-3284", html);
    }

    [Fact]
    public void Un_bloc_de_puces_devient_une_vraie_liste()
    {
        var html = LeadEmails.Reply("- Un\n- Deux\n● Trois", Tenant);
        Assert.Contains("<ul", html);
        Assert.Contains(">Trois</li>", html);
    }

    [Fact]
    public void Les_pieces_jointes_sont_controlees()
    {
        var pdf = new EmailAttachment("offre.pdf", "application/pdf", new byte[1000]);
        Assert.Empty(LeadEmails.ValidateReply("Re : prix", "Bonjour", [pdf]));

        var exe = new EmailAttachment("virus.exe", "application/octet-stream", new byte[10]);
        Assert.Contains("Attachments", LeadEmails.ValidateReply("Re", "Bonjour", [exe]).Keys);

        var big = new EmailAttachment("gros.pdf", "application/pdf", new byte[EmailAttachment.MaxTotalBytes + 1]);
        Assert.Contains("Attachments", LeadEmails.ValidateReply("Re", "Bonjour", [big]).Keys);

        var six = Enumerable.Range(0, 6).Select(i => pdf with { FileName = $"{i}.pdf" }).ToList();
        Assert.Contains("Attachments", LeadEmails.ValidateReply("Re", "Bonjour", six).Keys);

        var empty = LeadEmails.ValidateReply("", " ", []);
        Assert.Contains("Subject", empty.Keys);
        Assert.Contains("Body", empty.Keys);
    }
}
