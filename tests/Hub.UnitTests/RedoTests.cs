using Hub.Modules.Channels.Application.Autopilot;
using Hub.Modules.Channels.Domain;
using Hub.SharedKernel.Time;

namespace Hub.UnitTests;

public class RedoTests
{
    private static readonly DateTimeOffset Now = BusinessTime.FromLocal(new DateTime(2026, 11, 9, 18, 0, 0));

    private static Post At(int days, int hour, PostStatus status = PostStatus.AwaitingApproval, bool autopilot = true, string? format = null) => new()
    {
        Title = $"J+{days} {hour}h {status}",
        FromAutopilot = autopilot,
        Status = status,
        Format = format,
        ScheduledAt = BusinessTime.FromLocal(new DateTime(2026, 11, 9, hour, 0, 0).AddDays(days)),
    };

    [Fact]
    public void Seules_les_publications_du_pilote_a_venir_et_pas_publiees_sont_refaites()
    {
        var posts = new List<Post>
        {
            At(0, 20),                                    // aujourd'hui : gardée
            At(1, 10),                                    // à refaire
            At(2, 19, PostStatus.Scheduled),              // approuvée mais pas publiée : à refaire
            At(3, 12, PostStatus.Published),              // déjà publiée : jamais
            At(4, 12, autopilot: false),                  // écrite à la main : jamais
            At(5, 12, PostStatus.Cancelled),              // refusée : rien à faire
            At(6, 12, format: "humour"),                  // déjà au nouveau format
            At(9, 12),                                    // au-delà de la semaine préparée
        };

        var all = AutopilotRunner.RedoCandidates(posts.AsQueryable(), Now, onlyOldFormat: false).Select(p => p.Title).ToList();
        Assert.Equal(["J+1 10h AwaitingApproval", "J+2 19h Scheduled", "J+6 12h AwaitingApproval"], all);

        var old = AutopilotRunner.RedoCandidates(posts.AsQueryable(), Now, onlyOldFormat: true).Select(p => p.Title).ToList();
        Assert.Equal(["J+1 10h AwaitingApproval", "J+2 19h Scheduled"], old);
    }
}
