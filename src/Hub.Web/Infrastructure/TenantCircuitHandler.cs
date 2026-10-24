using Hub.Modules.Platform.Domain;
using Hub.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Hub.Web.Infrastructure;

/// <summary>
/// Un circuit Blazor Server a sa propre portée de services : l'entreprise
/// résolue par le middleware HTTP n'y est pas. On la reprend ici depuis
/// l'identité de l'administrateur connecté.
/// </summary>
internal sealed class TenantCircuitHandler(AuthenticationStateProvider auth, ITenantContext tenant) : CircuitHandler
{
    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        try
        {
            var state = await auth.GetAuthenticationStateAsync();
            var claim = state.User.FindFirst(HubClaims.TenantId)?.Value;
            if (Guid.TryParse(claim, out var tenantId))
                tenant.Set(tenantId);
        }
        catch (InvalidOperationException)
        {
            // État d'authentification pas encore disponible : l'entreprise par défaut s'applique.
        }
    }
}
