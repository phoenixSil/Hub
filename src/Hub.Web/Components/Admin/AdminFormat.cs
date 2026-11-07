using System.Globalization;
using Hub.SharedKernel.Time;

namespace Hub.Web.Components.Admin;

public static class AdminFormat
{
    public static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-CA");

    /// <summary>Date et heure à Québec, ex. « 24 oct. 14:05 ».</summary>
    public static string Local(DateTimeOffset value, string format = "d MMM HH:mm") =>
        TimeZoneInfo.ConvertTime(value, BusinessTime.Zone).ToString(format, Fr);

    /// <summary>Jour court, ex. « 9 nov. ».</summary>
    public static string Day(DateOnly value) => value.ToString("d MMM", Fr);
}
