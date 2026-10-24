using Hub.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Hub.Modules.Platform.Application;

/// <summary>
/// Détermine l'entreprise à partir du nom de domaine de la requête.
/// Domaine inconnu (localhost, adresse *.azurewebsites.net) : entreprise par défaut.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, ITenantStore store)
    {
        var tenant = await store.FindByHostAsync(context.Request.Host.Host, context.RequestAborted);
        if (tenant is not null)
            tenantContext.Set(tenant.Id);

        await next(context);
    }
}
