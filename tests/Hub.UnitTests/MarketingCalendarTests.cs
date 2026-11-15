using Hub.Modules.Channels;
using Hub.Modules.Channels.Application.Marketing;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.UnitTests;

public class MarketingCalendarTests
{
    private static DateTimeOffset At(int day, int hour) => BusinessTime.FromLocal(new DateTime(2026, 11, day, hour, 0, 0));

    [Fact]
    public void La_semaine_commence_le_lundi_et_range_chaque_publication_dans_son_jour_par_heure()
    {
        Assert.Equal(new DateOnly(2026, 11, 16), CalendarLogic.Monday(new DateOnly(2026, 11, 22))); // dimanche → lundi précédent
        Assert.Equal(new DateOnly(2026, 11, 16), CalendarLogic.Monday(new DateOnly(2026, 11, 16)));

        var a = new Post { Title = "Soir", ScheduledAt = At(17, 19), Status = PostStatus.AwaitingApproval, FromAutopilot = true };
        var b = new Post { Title = "Matin", ScheduledAt = At(17, 8), Status = PostStatus.Scheduled };
        var c = new Post { Title = "Retirée", ScheduledAt = At(18, 9), Status = PostStatus.Cancelled };
        var d = new Post { Title = "", Message = "Publiée sans titre", PublishedAt = At(22, 23), Status = PostStatus.Published };
        var targets = new[]
        {
            new PostTarget { PostId = a.Id, Channel = ChannelType.Facebook },
            new PostTarget { PostId = a.Id, Channel = ChannelType.Instagram },
            new PostTarget { PostId = d.Id, Channel = ChannelType.Facebook, Error = null },
        };

        var week = CalendarLogic.Week(new DateOnly(2026, 11, 16), [a, b, c, d], targets);

        Assert.Equal(7, week.Days.Count);
        Assert.Equal(new[] { "Matin", "Soir" }, week.Days[1].Posts.Select(p => p.Title));
        Assert.Equal(new[] { ChannelType.Facebook, ChannelType.Instagram }, week.Days[1].Posts[1].Channels);
        Assert.Empty(week.Days[2].Posts); // la publication retirée n'apparaît pas
        Assert.Equal("Publiée sans titre", week.Days[6].Posts.Single().Title); // dimanche 23 h, heure de Québec
        Assert.Equal(1, week.ToApprove);
    }

    [Fact]
    public void Le_voyant_dit_ce_qui_ne_va_pas_avec_le_bouton_qui_le_regle()
    {
        var ok = MarketingHealth.Evaluate(new MarketingHealthInput(true, true, [ChannelType.Facebook], "7 publication(s) préparée(s).", null, 0, 0));
        Assert.Empty(ok);

        var bad = MarketingHealth.Evaluate(new MarketingHealthInput(true, true, [], "0 publication(s) préparée(s). Problèmes : Réponse de l'IA incomplète. Réessayez.", null, 1, 2));
        Assert.Contains(bad, i => i.Action == HealthAction.OpenChannels && i.Level == HealthLevel.Error);
        var retry = Assert.Single(bad, i => i.Action == HealthAction.Retry);
        Assert.Contains("Réponse de l'IA incomplète", retry.Message);
        Assert.Contains(bad, i => i.Message.Contains("faute de validation"));
        Assert.Contains(bad, i => i.Message.Contains("n'ont pas pu partir"));

        var paused = MarketingHealth.Evaluate(new MarketingHealthInput(true, false, [ChannelType.Instagram], null, null, 0, 0));
        Assert.Equal(HealthAction.ResumeAutopilot, Assert.Single(paused).Action);
        Assert.Equal(HealthAction.CreateCampaign, Assert.Single(MarketingHealth.Evaluate(new MarketingHealthInput(false, false, [ChannelType.Facebook], null, null, 0, 0))).Action);
    }

    [Fact]
    public void L_avancement_d_une_preparation_est_suivi_par_entreprise()
    {
        var progress = new AutopilotProgress();
        var tenant = Guid.NewGuid();
        Assert.Null(progress.Get(tenant));
        progress.Queue(tenant, DateTimeOffset.UtcNow);
        Assert.False(progress.Get(tenant)!.Finished);
        progress.Start(tenant, 7, DateTimeOffset.UtcNow);
        progress.Step(tenant, 3);
        Assert.Equal((3, 7), (progress.Get(tenant)!.Done, progress.Get(tenant)!.Target));
        progress.Finish(tenant, "3 publication(s) préparée(s).", failed: false);
        Assert.True(progress.Get(tenant)!.Finished);
        Assert.Null(progress.Get(Guid.NewGuid()));
    }

    [Fact]
    public void Le_jeudi_toute_la_semaine_suivante_est_preparee_au_rythme_de_la_campagne()
    {
        // Jeudi 19 novembre : jusqu'au dimanche 29 (semaine suivante complète).
        Assert.Equal(new DateOnly(2026, 11, 29), Hub.Modules.Channels.Application.Autopilot.AutopilotSchedule.HorizonEnd(new DateOnly(2026, 11, 19)));
        // Lundi 16 : jusqu'au dimanche 22.
        Assert.Equal(new DateOnly(2026, 11, 22), Hub.Modules.Channels.Application.Autopilot.AutopilotSchedule.HorizonEnd(new DateOnly(2026, 11, 16)));

        var thursday = At(19, 18);
        var none = new Dictionary<DateOnly, int>();
        var slots = Hub.Modules.Channels.Application.Autopilot.AutopilotSchedule.WeekSlots(thursday, 7, none, none)
            .Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s, BusinessTime.Zone).DateTime)).ToList();
        // Fin de cette semaine (ven, sam, dim : 3 jours) puis la semaine suivante complète (7).
        Assert.Equal(3, slots.Count(d => d <= new DateOnly(2026, 11, 22)));
        Assert.Equal(7, slots.Count(d => d >= new DateOnly(2026, 11, 23)));

        // Ce qui est déjà prévu par le pilote n'est pas refait.
        var planned = Enumerable.Range(23, 7).ToDictionary(d => new DateOnly(2026, 11, d), _ => 1);
        var again = Hub.Modules.Channels.Application.Autopilot.AutopilotSchedule.WeekSlots(thursday, 7, planned, planned);
        Assert.Equal(3, again.Count);

        // Rendez-vous du jeudi 18 h : le vendredi, le dernier rendez-vous est la veille ; le jeudi à 17 h, celui de la semaine d'avant.
        var friday = At(20, 9);
        Assert.Equal(At(19, 18), Hub.Modules.Channels.Application.Autopilot.AutopilotWorker.LastPreparationTime(friday));
        Assert.Equal(At(12, 18), Hub.Modules.Channels.Application.Autopilot.AutopilotWorker.LastPreparationTime(At(19, 17)));
    }
}
