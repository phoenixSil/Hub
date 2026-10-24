namespace Hub.SharedKernel.Time;

/// <summary>
/// Heure locale de l'entreprise (Québec). Les serveurs Azure tournent en UTC :
/// sans cette conversion, « aujourd'hui » basculerait à 20 h l'été.
/// </summary>
public static class BusinessTime
{
    public static readonly TimeZoneInfo Zone = FindZone();

    private static TimeZoneInfo FindZone()
    {
        foreach (var id in new[] { "America/Toronto", "Eastern Standard Time" })
        {
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone))
                return zone;
        }
        return TimeZoneInfo.Utc;
    }

    public static DateTimeOffset Now(TimeProvider clock) => TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(Now(clock).DateTime);

    /// <summary>Date et heure saisies en heure locale → instant avec le bon décalage.</summary>
    public static DateTimeOffset FromLocal(DateTime local)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, Zone.GetUtcOffset(unspecified));
    }
}
