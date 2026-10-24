using Hub.Modules.Leads.Application;
using Hub.Modules.Leads.Contracts;
using Hub.Modules.Leads.Domain;

namespace Hub.UnitTests;

public class LeadValidatorTests
{
    private static readonly DateOnly Today = new(2026, 10, 26); // un lundi

    private static LeadSubmission ValidQuote() => new()
    {
        Kind = LeadKind.Quote,
        FullName = "Marie Tremblay",
        Email = "marie@example.com",
        Phone = "418 555-1234",
        Services = ["Nettoyage résidentiel"],
        ConsentGiven = true,
    };

    private static Dictionary<string, string> Validate(LeadSubmission s, params DateOnly[] closed) =>
        LeadValidator.Validate(s, new SchedulingSettings(), closed, Today);

    [Fact]
    public void Une_demande_de_devis_complete_est_valide() =>
        Assert.Empty(Validate(ValidQuote()));

    [Fact]
    public void Le_consentement_est_obligatoire()
    {
        var s = ValidQuote();
        s.ConsentGiven = false;
        Assert.Contains(nameof(LeadSubmission.ConsentGiven), Validate(s).Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pas-un-courriel")]
    [InlineData("a@b")]
    public void Un_courriel_invalide_est_refuse(string email)
    {
        var s = ValidQuote();
        s.Email = email;
        Assert.Contains(nameof(LeadSubmission.Email), Validate(s).Keys);
    }

    [Fact]
    public void Un_devis_sans_service_est_refuse()
    {
        var s = ValidQuote();
        s.Services = [];
        Assert.Contains(nameof(LeadSubmission.Services), Validate(s).Keys);
    }

    [Fact]
    public void Le_telephone_est_obligatoire_pour_un_devis()
    {
        var s = ValidQuote();
        s.Phone = "555";
        Assert.Contains(nameof(LeadSubmission.Phone), Validate(s).Keys);
    }

    [Fact]
    public void Un_message_de_contact_doit_avoir_du_contenu()
    {
        var s = new LeadSubmission { Kind = LeadKind.Contact, FullName = "Jean", Email = "jean@example.com", Message = "Allo", ConsentGiven = true };
        Assert.Contains(nameof(LeadSubmission.Message), Validate(s).Keys);
    }

    [Fact]
    public void Un_rendez_vous_un_dimanche_est_refuse_par_defaut()
    {
        var s = ValidQuote();
        s.Kind = LeadKind.Appointment;
        s.PreferredDate = new DateOnly(2026, 11, 1); // dimanche
        s.PreferredSlot = TimeSlot.Morning;
        Assert.Contains(nameof(LeadSubmission.PreferredDate), Validate(s).Keys);
    }

    [Fact]
    public void Un_rendez_vous_un_jour_ferme_est_refuse()
    {
        var s = ValidQuote();
        s.Kind = LeadKind.Appointment;
        s.PreferredDate = new DateOnly(2026, 10, 28);
        s.PreferredSlot = TimeSlot.Afternoon;
        Assert.Empty(Validate(s));
        Assert.Contains(nameof(LeadSubmission.PreferredDate), Validate(s, new DateOnly(2026, 10, 28)).Keys);
    }

    [Fact]
    public void Un_rendez_vous_le_jour_meme_est_refuse()
    {
        var s = ValidQuote();
        s.Kind = LeadKind.Appointment;
        s.PreferredDate = Today;
        s.PreferredSlot = TimeSlot.Morning;
        Assert.Contains(nameof(LeadSubmission.PreferredDate), Validate(s).Keys);
    }
}
