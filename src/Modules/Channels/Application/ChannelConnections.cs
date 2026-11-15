using System.Text.Json;
using Hub.Modules.Channels.Publishers;
using Hub.SharedKernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hub.Modules.Channels.Application;

/// <summary>État de connexion d'un réseau, sans le jeton.</summary>
public sealed record ConnectionStatus(ChannelType Channel, bool IsConnected, string? AccountId, string? AccountName, DateTimeOffset? ConnectedAt);

/// <summary>
/// Identifiants des réseaux, par entreprise. Le jeton est chiffré (ASP.NET Data Protection)
/// avant d'être stocké : il n'est jamais lisible en base ni réaffiché à l'écran.
/// </summary>
public interface IChannelConnections
{
    Task<ConnectionStatus> GetStatusAsync(ChannelType channel, CancellationToken ct = default);
    Task<Result<string>> ConnectAsync(ChannelType channel, string accountId, string accessToken, CancellationToken ct = default);

    /// <summary>Connecte un réseau lié (ex. Instagram) avec le compte source déjà connecté (la page Facebook).</summary>
    Task<Result<string>> ConnectLinkedAsync(ChannelType channel, CancellationToken ct = default);
    Task DisconnectAsync(ChannelType channel, CancellationToken ct = default);

    /// <summary>Vrai si le réseau se connecte par « Se connecter avec… » (OAuth), ex. Google.</summary>
    bool UsesOAuth(ChannelType channel);

    /// <summary>Adresse de la page d'autorisation du réseau ; null si l'application n'est pas configurée (identifiant, secret).</summary>
    string? AuthorizeUrl(ChannelType channel, string redirectUri, string state);

    /// <summary>Retour d'autorisation : échange le code, choisit la fiche qui ressemble à l'entreprise, enregistre la connexion.</summary>
    Task<Result<string>> ConnectOAuthAsync(ChannelType channel, string? code, string redirectUri, string? preferredName, CancellationToken ct = default);

    /// <summary>Les cibles possibles (ex. les fiches Google du compte), pour en changer.</summary>
    Task<Result<IReadOnlyList<ChannelTarget>>> TargetsAsync(ChannelType channel, CancellationToken ct = default);

    Task<Result<string>> SelectTargetAsync(ChannelType channel, string targetId, CancellationToken ct = default);
    Task<ChannelCredentials?> GetCredentialsAsync(ChannelType channel, CancellationToken ct = default);
}

internal sealed class ChannelConnections(
    IDbContextFactory<ChannelsDbContext> factory,
    IDataProtectionProvider protection,
    IPublisherResolver publishers) : IChannelConnections
{
    private sealed record Stored(string AccountId, string ProtectedToken, string? AccountName, DateTimeOffset ConnectedAt);

    private readonly IDataProtector _protector = protection.CreateProtector("Hub.Channels.Credentials.v1");

    public async Task<ConnectionStatus> GetStatusAsync(ChannelType channel, CancellationToken ct = default)
    {
        var stored = await ReadAsync(channel, ct);
        return new ConnectionStatus(channel, stored is not null, stored?.AccountId, stored?.AccountName, stored?.ConnectedAt);
    }

    public async Task<Result<string>> ConnectAsync(ChannelType channel, string accountId, string accessToken, CancellationToken ct = default)
    {
        accountId = accountId.Trim();
        accessToken = accessToken.Trim();
        if (accountId.Length == 0 || accessToken.Length == 0)
            return Result<string>.Failure("L'identifiant et le jeton sont obligatoires.");

        // On vérifie auprès du réseau avant d'enregistrer : un jeton invalide n'est jamais conservé.
        var test = await publishers.For(channel).TestAsync(new ChannelCredentials(accountId, accessToken), ct);
        if (test.IsFailure) return test;
        await SaveAsync(channel, accountId, accessToken, test.Value!, ct);
        return test;
    }

    public async Task<Result<string>> ConnectLinkedAsync(ChannelType channel, CancellationToken ct = default)
    {
        var linked = publishers.LinkedFor(channel);
        if (linked is null) return Result<string>.Failure($"{channel} se connecte avec son propre jeton.");
        var source = await GetCredentialsAsync(linked.Source, ct);
        if (source is null && !publishers.IsSimulated)
            return Result<string>.Failure($"Connectez d'abord {linked.Source} : {channel} utilise la même connexion.");

        ChannelCredentials derived;
        if (publishers.IsSimulated || source is null)
        {
            derived = new ChannelCredentials("simule-lie", source?.AccessToken ?? "simulé", "@compte (simulé)");
        }
        else
        {
            var found = await linked.DeriveAsync(source, ct);
            if (found.IsFailure) return Result<string>.Failure(found.Error!);
            derived = found.Value!;
        }
        var name = derived.AccountName ?? derived.AccountId;
        await SaveAsync(channel, derived.AccountId, derived.AccessToken, name, ct);
        return Result<string>.Success(name);
    }

    private async Task SaveAsync(ChannelType channel, string accountId, string accessToken, string accountName, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Channels.FirstOrDefaultAsync(c => c.Type == channel, ct);
        if (row is null)
        {
            row = new Channel { Type = channel };
            db.Channels.Add(row);
        }
        row.SettingsJson = JsonSerializer.Serialize(new Stored(accountId, _protector.Protect(accessToken), accountName, DateTimeOffset.UtcNow));
        row.Status = ChannelStatus.Active;
        await db.SaveChangesAsync(ct);
    }

    public bool UsesOAuth(ChannelType channel) => publishers.OAuthFor(channel) is not null;

    public string? AuthorizeUrl(ChannelType channel, string redirectUri, string state) =>
        publishers.OAuthFor(channel) is { IsConfigured: true } oauth ? oauth.AuthorizeUrl(redirectUri, state) : null;

    private static readonly IReadOnlyList<ChannelTarget> SimulatedTargets =
    [
        new("accounts/simule/locations/1", "Entretien Scintinet (simulée)", "12 rue Boucher, Québec"),
        new("accounts/simule/locations/2", "Autre fiche (simulée)", "Ailleurs"),
    ];

    public async Task<Result<string>> ConnectOAuthAsync(ChannelType channel, string? code, string redirectUri, string? preferredName, CancellationToken ct = default)
    {
        var oauth = publishers.OAuthFor(channel);
        if (oauth is null) return Result<string>.Failure($"{channel} ne se connecte pas de cette façon.");
        ChannelCredentials credentials;
        if (publishers.IsSimulated)
        {
            credentials = new ChannelCredentials(SimulatedTargets[0].Id, "simulé", SimulatedTargets[0].Name);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(code)) return Result<string>.Failure("Connexion annulée : aucune autorisation reçue.");
            var done = await oauth.CompleteAsync(code, redirectUri, preferredName, ct);
            if (done.IsFailure) return Result<string>.Failure(done.Error!);
            credentials = done.Value!;
        }
        var name = credentials.AccountName ?? credentials.AccountId;
        await SaveAsync(channel, credentials.AccountId, credentials.AccessToken, name, ct);
        return Result<string>.Success(name);
    }

    public async Task<Result<IReadOnlyList<ChannelTarget>>> TargetsAsync(ChannelType channel, CancellationToken ct = default)
    {
        if (publishers.IsSimulated) return Result<IReadOnlyList<ChannelTarget>>.Success(SimulatedTargets);
        var oauth = publishers.OAuthFor(channel);
        var credentials = await GetCredentialsAsync(channel, ct);
        if (oauth is null || credentials is null) return Result<IReadOnlyList<ChannelTarget>>.Failure("Connectez d'abord le réseau.");
        return await oauth.ListTargetsAsync(credentials, ct);
    }

    public async Task<Result<string>> SelectTargetAsync(ChannelType channel, string targetId, CancellationToken ct = default)
    {
        var credentials = await GetCredentialsAsync(channel, ct);
        if (credentials is null) return Result<string>.Failure("Connectez d'abord le réseau.");
        var targets = await TargetsAsync(channel, ct);
        if (targets.IsFailure) return Result<string>.Failure(targets.Error!);
        var target = targets.Value!.FirstOrDefault(t => t.Id == targetId);
        if (target is null) return Result<string>.Failure("Fiche introuvable.");
        await SaveAsync(channel, target.Id, credentials.AccessToken, target.Name, ct);
        return Result<string>.Success(target.Name);
    }

    public async Task DisconnectAsync(ChannelType channel, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Channels.FirstOrDefaultAsync(c => c.Type == channel, ct);
        if (row is null) return;
        row.SettingsJson = null;
        row.Status = ChannelStatus.Paused;
        await db.SaveChangesAsync(ct);
    }

    public async Task<ChannelCredentials?> GetCredentialsAsync(ChannelType channel, CancellationToken ct = default)
    {
        var stored = await ReadAsync(channel, ct);
        if (stored is null) return null;
        try { return new ChannelCredentials(stored.AccountId, _protector.Unprotect(stored.ProtectedToken), stored.AccountName); }
        catch (System.Security.Cryptography.CryptographicException) { return null; } // clés de chiffrement changées : reconnecter
    }

    private async Task<Stored?> ReadAsync(ChannelType channel, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = await db.Channels.Where(c => c.Type == channel).Select(c => c.SettingsJson).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<Stored>(json); }
        catch (JsonException) { return null; }
    }
}
