using Hub.Modules.Channels.Application.Marketing;

namespace Hub.UnitTests;

public class GuardianTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 17, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("Facebook : Service temporarily unavailable (code 2).", false)]
    [InlineData("Délai dépassé.", false)]
    [InlineData("Facebook n'est pas connecté : back-office → Canaux.", true)]
    [InlineData("Le jeton de la page Facebook est expiré ou invalide : reconnectez la page.", true)]
    [InlineData("Facebook : (#200) Permissions error (code 200).", true)]
    [InlineData("Facebook : (#100) Invalid parameter (code 100).", true)]
    public void Une_panne_passagere_se_relance_un_probleme_de_reglage_non(string error, bool permanent) =>
        Assert.Equal(permanent, RetryPolicy.IsPermanent(error));

    [Fact]
    public void Les_relances_sont_espacees_et_limitees_a_trois_tentatives()
    {
        const string busy = "Service temporairement indisponible.";
        Assert.False(RetryPolicy.ShouldRetry(1, Now.AddMinutes(-10), busy, Now));  // trop tôt
        Assert.True(RetryPolicy.ShouldRetry(1, Now.AddMinutes(-31), busy, Now));   // 30 min après la 1re
        Assert.False(RetryPolicy.ShouldRetry(2, Now.AddMinutes(-90), busy, Now));  // 2 h après la 2e
        Assert.True(RetryPolicy.ShouldRetry(2, Now.AddHours(-2), busy, Now));
        Assert.False(RetryPolicy.ShouldRetry(3, Now.AddDays(-1), busy, Now));      // 3 tentatives : on arrête
        Assert.False(RetryPolicy.ShouldRetry(1, Now.AddDays(-1), "Facebook n'est pas connecté", Now));
        Assert.True(RetryPolicy.ShouldRetry(1, Now, busy, Now, TimeSpan.Zero));     // délai réglé à 0 (tests)
    }
}
