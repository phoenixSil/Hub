using Hub.Modules.Leads.Application;

namespace Hub.UnitTests;

public class SpamFilterTests
{
    [Theory]
    [InlineData("Hi, i am writing about the price")]
    [InlineData("Hallo, i am wrote about your the price for reseller")]
    [InlineData("Aloha, wrote about your the price")]
    [InlineData("Hi i am writing about your the price for reseller")]
    [InlineData("Hello, i writing about price")]
    [InlineData("Aloha, i wrote about prices")]
    public void Les_messages_de_RobertBuimi_sont_des_pourriels(string message) =>
        Assert.True(SpamFilter.Check("RobertBuimi", message, message, null, 0).IsSpam);

    [Theory]
    [InlineData("Bonjour, je voudrais connaître le prix pour un ménage de mon condo de 4 et demi.", null)]
    [InlineData("Quel est votre prix pour le lavage de vitres ?", "418 555-1234")]
    [InlineData("Hi, I need a cleaning quote for my apartment in Sainte-Foy, what's the price?", "418 555-1234")]
    [InlineData("Hello, we are moving out on December 1st, can you clean the apartment?", null)]
    public void Les_vrais_clients_passent(string message, string? phone) =>
        Assert.False(SpamFilter.Check("Marie Tremblay", null, message, phone, 0).IsSpam);

    [Fact]
    public void Un_client_qui_remplit_trois_formulaires_n_est_pas_bloque()
    {
        Assert.False(SpamFilter.Check("Marie Tremblay", "Devis", "Bonjour, je veux un devis pour mon chalet.", null, 2).IsSpam);
    }

    [Fact]
    public void Envois_repetes_en_anglais_sans_telephone_sont_bloques()
    {
        var v = SpamFilter.Check("John", null, "Hello, are you the owner? I want to talk about my offer.", null, 3);
        Assert.True(v.IsSpam);
        Assert.Contains(v.Reasons, r => r.Contains("24 h"));
    }

    [Fact]
    public void Les_liens_et_les_alphabets_etrangers_comptent()
    {
        Assert.True(SpamFilter.Check("Иван", null, "Скидки http://promo.ru www.promo.ru", null, 0).IsSpam);
        Assert.StartsWith("Classée en pourriel automatiquement : ", SpamFilter.Describe(SpamFilter.Check("x", null, "Hi i am writing about the price", null, 0)));
    }
}
