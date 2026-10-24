using System.Reflection;
using NetArchTest.Rules;

namespace Hub.ArchitectureTests;

/// <summary>
/// Les modules ne se parlent que par leurs Contracts : aucun module ne doit
/// dépendre de l'intérieur (Domain, Application, Infrastructure) d'un autre.
/// </summary>
public class ModuleBoundariesTests
{
    private static readonly Dictionary<string, Assembly> Modules = new()
    {
        ["Platform"] = typeof(Hub.Modules.Platform.PlatformModule).Assembly,
        ["Catalog"] = typeof(Hub.Modules.Catalog.CatalogModule).Assembly,
        ["Content"] = typeof(Hub.Modules.Content.ContentModule).Assembly,
        ["Leads"] = typeof(Hub.Modules.Leads.LeadsModule).Assembly,
        ["Analytics"] = typeof(Hub.Modules.Analytics.AnalyticsModule).Assembly,
        ["Channels"] = typeof(Hub.Modules.Channels.ChannelsModule).Assembly,
    };

    public static TheoryData<string> ModuleNames() => new(Modules.Keys);

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Un_module_ne_depend_pas_de_l_interieur_des_autres(string module)
    {
        var forbidden = Modules.Keys
            .Where(other => other != module)
            .SelectMany(other => new[]
            {
                $"Hub.Modules.{other}.Domain",
                $"Hub.Modules.{other}.Application",
                $"Hub.Modules.{other}.Infrastructure",
            })
            .ToArray();

        var result = Types.InAssembly(Modules[module])
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.True(result.IsSuccessful,
            $"{module} dépend de l'intérieur d'un autre module : {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Le_noyau_partage_ne_connait_aucun_module()
    {
        var result = Types.InAssembly(typeof(Hub.SharedKernel.Result).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Hub.Modules")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }
}
