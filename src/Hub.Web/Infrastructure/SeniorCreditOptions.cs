namespace Hub.Web.Infrastructure;

/// <summary>
/// Paramètres du crédit d'impôt pour maintien à domicile des aînés (Québec).
/// À mettre à jour chaque année dans la configuration « SeniorCredit ».
/// </summary>
public sealed class SeniorCreditOptions
{
    public const string Section = "SeniorCredit";

    /// <summary>Taux du crédit (0,40 = 40 % en 2026).</summary>
    public decimal Rate { get; set; } = 0.40m;
    public int Year { get; set; } = 2026;
    public int MinAge { get; set; } = 70;
}
