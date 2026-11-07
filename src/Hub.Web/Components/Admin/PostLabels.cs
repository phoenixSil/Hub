using Hub.Modules.Channels;
using Hub.Modules.Channels.Domain;

namespace Hub.Web.Components.Admin;

public static class PostLabels
{
    public static string Of(PostStatus s) => s switch
    {
        PostStatus.Draft => "Brouillon",
        PostStatus.Scheduled => "Programmée",
        PostStatus.Publishing => "En cours",
        PostStatus.Published => "Publiée",
        PostStatus.PartiallyPublished => "Publiée en partie",
        PostStatus.Failed => "Échec",
        PostStatus.AwaitingApproval => "À valider",
        _ => "Annulée",
    };

    public static string Css(PostStatus s) => s switch
    {
        PostStatus.Published => "won",
        PostStatus.PartiallyPublished or PostStatus.Scheduled => "scheduled",
        PostStatus.Failed => "lost",
        PostStatus.Publishing => "contacted",
        PostStatus.AwaitingApproval => "approval",
        PostStatus.Cancelled => "lost",
        _ => "new",
    };

    public static string Of(ChannelType c) => c switch
    {
        ChannelType.GoogleBusiness => "Google Business",
        ChannelType.Website => "Site web",
        _ => c.ToString(),
    };
}
