namespace Hub.SharedKernel.Modules;

/// <summary>
/// Exécuté au démarrage, dans l'ordre de <see cref="Order"/> :
/// applique les migrations du module et prépare ses données.
/// </summary>
public interface IModuleInitializer
{
    int Order { get; }
    Task InitializeAsync(IServiceProvider services, CancellationToken ct = default);
}
