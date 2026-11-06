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
    Task DisconnectAsync(ChannelType channel, CancellationToken ct = default);
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

        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Channels.FirstOrDefaultAsync(c => c.Type == channel, ct);
        if (row is null)
        {
            row = new Channel { Type = channel };
            db.Channels.Add(row);
        }
        row.SettingsJson = JsonSerializer.Serialize(new Stored(accountId, _protector.Protect(accessToken), test.Value, DateTimeOffset.UtcNow));
        row.Status = ChannelStatus.Active;
        await db.SaveChangesAsync(ct);
        return test;
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
