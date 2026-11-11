using Hub.Modules.GiftCards.Application;
using Hub.Modules.GiftCards.Domain;

namespace Hub.UnitTests;

public class GiftCardTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static GiftCardRequest Valid() => new()
    {
        Amount = 100, BuyerName = "Marie Tremblay", BuyerEmail = "marie@example.com",
        RecipientName = "Jeanne Tremblay", RecipientEmail = "jeanne@example.com",
        Message = "Joyeux Noël maman !", DeliverOn = new DateOnly(2026, 12, 24), ConsentGiven = true,
    };

    [Fact]
    public void Les_codes_sont_uniques_lisibles_et_sans_caracteres_ambigus()
    {
        var codes = Enumerable.Range(0, 2000).Select(_ => GiftCardCodes.New()).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$", c));
    }

    [Theory]
    [InlineData("k7pq 4mzx 9tra", "K7PQ-4MZX-9TRA")]
    [InlineData("K7PQ-4MZX-9TRA", "K7PQ-4MZX-9TRA")]
    [InlineData("K7PQ4MZX9TR", null)]       // trop court
    [InlineData("K7PQ-4MZX-9TR0", null)]    // 0 n'existe pas dans les codes
    [InlineData("", null)]
    public void Un_code_saisi_a_la_main_est_remis_au_bon_format(string input, string? expected) =>
        Assert.Equal(expected, GiftCardCodes.Normalize(input));

    [Fact]
    public void Une_demande_complete_est_acceptee()
    {
        Assert.Empty(GiftCardService.Validate(Valid(), Today));
        var noRecipientEmail = Valid();
        noRecipientEmail.RecipientEmail = null;
        noRecipientEmail.DeliverOn = null;
        Assert.Empty(GiftCardService.Validate(noRecipientEmail, Today));
    }

    [Theory]
    [InlineData(10, "Amount")]
    [InlineData(1005, "Amount")]
    [InlineData(102, "Amount")]
    public void Le_montant_va_de_25_a_1000_par_tranches_de_5(decimal amount, string field)
    {
        var r = Valid();
        r.Amount = amount;
        Assert.Contains(field, GiftCardService.Validate(r, Today).Keys);
    }

    [Fact]
    public void Les_erreurs_sont_signalees_champ_par_champ()
    {
        var r = new GiftCardRequest { Amount = 100, BuyerEmail = "pas-un-courriel", RecipientEmail = "x@", DeliverOn = Today.AddDays(-1) };
        var errors = GiftCardService.Validate(r, Today);
        Assert.Contains("BuyerName", errors.Keys);
        Assert.Contains("BuyerEmail", errors.Keys);
        Assert.Contains("RecipientName", errors.Keys);
        Assert.Contains("RecipientEmail", errors.Keys);
        Assert.Contains("DeliverOn", errors.Keys);
        Assert.Contains("ConsentGiven", errors.Keys);
    }

    private static readonly GiftCardContext Context = new("ScintiNet", "418 953-3284", "info@scintinet.com", "admin@scintinet.com",
        "https://www.scintinet.com", "#0097CE", "#E2E735", "https://www.scintinet.com/images/site/logo-couleur.png", "Virement Interac à paiement@scintinet.com");

    private static GiftCard Card() => new()
    {
        Code = "K7PQ-4MZX-9TRA", Amount = 150, Balance = 150, BuyerName = "Marie <Tremblay>", BuyerEmail = "marie@example.com",
        RecipientName = "Jeanne", RecipientEmail = "jeanne@example.com", Message = "Joyeux Noël !", DeliverOn = new DateOnly(2026, 12, 24),
    };

    [Fact]
    public void La_carte_envoyee_montre_le_montant_le_code_et_le_mot_sans_date_d_expiration()
    {
        var (subject, html) = GiftCardEmails.Card(Card(), Context);
        Assert.Contains("vous offre une carte-cadeau ScintiNet", subject);
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("150 $", text);
        Assert.Contains("K7PQ-4MZX-9TRA", text);
        Assert.Contains("Joyeux Noël !", text);
        Assert.Contains("Sans date d'expiration", text);
        Assert.DoesNotContain("<Tremblay>", html); // le nom est encodé
    }

    [Fact]
    public void L_acheteur_recoit_les_instructions_de_paiement()
    {
        var (_, html) = GiftCardEmails.RequestReceived(Card(), Context);
        var text = System.Net.WebUtility.HtmlDecode(html);
        Assert.Contains("Virement Interac à paiement@scintinet.com", text);
        Assert.Contains("24 décembre 2026", text);
    }

    [Theory]
    [InlineData("#0097CE")]
    [InlineData("#E2E735")]
    [InlineData("#ffffff")]
    [InlineData("pas-une-couleur")]
    public void Le_fond_de_la_carte_garde_le_texte_blanc_lisible(string brand)
    {
        var hex = GiftCardEmails.ReadableOnWhiteText(brand).TrimStart('#');
        var v = Convert.ToInt32(hex, 16);
        Assert.True(GiftCardEmails.Contrast((v >> 16) & 255, (v >> 8) & 255, v & 255) >= 4.5, $"{brand} → #{hex}");
    }
}
