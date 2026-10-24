using Hub.Modules.Content.Domain;
using Hub.Modules.Leads.Domain;
using Hub.SharedKernel.Text;
using Hub.SharedKernel.Time;

namespace Hub.UnitTests;

public class SharedKernelTests
{
    [Theory]
    [InlineData("Lavage de vitres après l'hiver", "lavage-de-vitres-apres-l-hiver")]
    [InlineData("Vieux-Québec – Cap-Blanc", "vieux-quebec-cap-blanc")]
    [InlineData("  L'Ancienne-Lorette ", "l-ancienne-lorette")]
    [InlineData("Saint-Émile", "saint-emile")]
    [InlineData("", "")]
    public void Slug_retire_accents_et_ponctuation(string input, string expected) =>
        Assert.Equal(expected, Slug.From(input));

    [Fact]
    public void Les_jours_ouverts_par_defaut_vont_du_lundi_au_samedi()
    {
        var days = new SchedulingSettings().OpenDaySet();
        Assert.Equal(6, days.Count);
        Assert.DoesNotContain(DayOfWeek.Sunday, days);
    }

    [Fact]
    public void Une_offre_datee_n_apparait_que_pendant_sa_periode()
    {
        var promo = new Promotion { StartsOn = new DateOnly(2027, 3, 15), EndsOn = new DateOnly(2027, 6, 15) };
        Assert.False(promo.IsLiveOn(new DateOnly(2027, 3, 14)));
        Assert.True(promo.IsLiveOn(new DateOnly(2027, 4, 1)));
        Assert.False(promo.IsLiveOn(new DateOnly(2027, 6, 16)));
    }

    [Fact]
    public void L_heure_locale_tient_compte_du_decalage_de_Quebec()
    {
        var local = BusinessTime.FromLocal(new DateTime(2026, 7, 1, 9, 0, 0));
        Assert.Equal(TimeSpan.FromHours(-4), local.Offset);
        var winter = BusinessTime.FromLocal(new DateTime(2026, 12, 1, 9, 0, 0));
        Assert.Equal(TimeSpan.FromHours(-5), winter.Offset);
    }
}
