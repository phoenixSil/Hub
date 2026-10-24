using System.Security.Claims;
using Hub.Modules.Platform.Application;
using Hub.Modules.Platform.Domain;
using Hub.Modules.Platform.Infrastructure;
using Hub.SharedKernel.Modules;
using Hub.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hub.Modules.Platform;

public sealed class PlatformOptions
{
    public const string Section = "Hub";

    /// <summary>Entreprise créée au premier démarrage si elle n'existe pas.</summary>
    public string DefaultTenantSlug { get; set; } = "scintinet";
    public string DefaultTenantName { get; set; } = "ScintiNet";
    public string DefaultTenantDomains { get; set; } = "scintinet.com,www.scintinet.com";

    /// <summary>Premier administrateur, créé au démarrage s'il n'existe pas.</summary>
    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

public static class PlatformModule
{
    public static IServiceCollection AddPlatformModule(this IServiceCollection services, IConfiguration configuration, string connectionString)
    {
        services.Configure<PlatformOptions>(configuration.GetSection(PlatformOptions.Section));

        services.AddDbContextFactory<PlatformDbContext>(o =>
            o.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformDbContext.SchemaName);
                sql.EnableRetryOnFailure(5);
            }));
        // Identity a besoin d'un contexte « scoped » classique.
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContext());

        services.AddIdentity<HubUser, IdentityRole>(o =>
            {
                o.Password.RequiredLength = 12;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.User.RequireUniqueEmail = true;
                o.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<PlatformDbContext>()
            .AddClaimsPrincipalFactory<HubUserClaimsPrincipalFactory>()
            .AddDefaultTokenProviders();

        services.AddSingleton<TenantDefaults>();
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddSingleton<ITenantStore, TenantStore>();
        services.AddScoped<ITenantInfoProvider, TenantInfoProvider>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddSingleton<IModuleInitializer, PlatformInitializer>();
        return services;
    }
}

/// <summary>Ajoute l'entreprise de l'utilisateur à son identité.</summary>
internal sealed class HubUserClaimsPrincipalFactory(
    UserManager<HubUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<HubUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(HubUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(HubClaims.TenantId, user.TenantId.ToString()));
        if (!string.IsNullOrWhiteSpace(user.DisplayName))
            identity.AddClaim(new Claim("hub:name", user.DisplayName));
        return identity;
    }
}

internal sealed class PlatformInitializer(IOptions<PlatformOptions> options, ILogger<PlatformInitializer> logger) : IModuleInitializer
{
    public int Order => 0;

    public async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var factory = services.GetRequiredService<IDbContextFactory<PlatformDbContext>>();
        await using (var db = await factory.CreateDbContextAsync(ct))
        {
            await db.Database.MigrateAsync(ct);
        }

        var opts = options.Value;
        var store = services.GetRequiredService<ITenantStore>();
        var tenant = await store.GetBySlugAsync(opts.DefaultTenantSlug, ct);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Slug = opts.DefaultTenantSlug,
                Name = opts.DefaultTenantName,
                Domains = opts.DefaultTenantDomains,
            };
            await store.SaveAsync(tenant, ct);
            logger.LogInformation("Entreprise {Slug} créée.", tenant.Slug);
        }

        services.GetRequiredService<TenantDefaults>().DefaultTenantId = tenant.Id;
        services.GetRequiredService<ITenantContext>().Set(tenant.Id);

        if (!string.IsNullOrWhiteSpace(opts.AdminEmail) && !string.IsNullOrWhiteSpace(opts.AdminPassword))
        {
            var users = services.GetRequiredService<UserManager<HubUser>>();
            if (await users.FindByEmailAsync(opts.AdminEmail) is null)
            {
                var admin = new HubUser
                {
                    UserName = opts.AdminEmail,
                    Email = opts.AdminEmail,
                    EmailConfirmed = true,
                    TenantId = tenant.Id,
                    DisplayName = "Administrateur",
                };
                var result = await users.CreateAsync(admin, opts.AdminPassword);
                if (result.Succeeded)
                    logger.LogInformation("Administrateur {Email} créé.", opts.AdminEmail);
                else
                    logger.LogError("Création de l'administrateur impossible : {Errors}",
                        string.Join(" ; ", result.Errors.Select(e => e.Description)));
            }
        }
    }
}
