using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Hub.SharedKernel.Caching;

/// <summary>
/// Cache en mémoire du contenu public. Toute modification faite dans le
/// back-office appelle <see cref="Invalidate"/> : la base n'est donc lue
/// qu'une fois par changement, ce qui garde la base gratuite sous sa limite.
/// </summary>
public sealed class ContentCache(IMemoryCache cache)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    private CancellationTokenSource _reset = new();
    private readonly Lock _gate = new();

    public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory)
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;

        var value = await factory();

        CancellationToken token;
        lock (_gate) token = _reset.Token;

        var options = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(MaxAge)
            .AddExpirationToken(new CancellationChangeToken(token));
        cache.Set(key, value, options);
        return value;
    }

    public void Invalidate()
    {
        CancellationTokenSource old;
        lock (_gate)
        {
            old = _reset;
            _reset = new CancellationTokenSource();
        }
        old.Cancel();
        old.Dispose();
    }
}
