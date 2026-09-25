using System.Globalization;
using System.Text.Json;
using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Кэш курсов поверх <see cref="IDistributedCache"/> (в проде — Redis).
/// Недоступность кэша не ломает сервис: ошибки логируются, запрос уходит напрямую в ЦБ.
/// </summary>
public sealed class DistributedRatesCache : IRatesCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly RatesCacheOptions _options;
    private readonly ILogger<DistributedRatesCache> _logger;

    public DistributedRatesCache(IDistributedCache cache, IOptions<RatesCacheOptions> options, ILogger<DistributedRatesCache> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DailyRates?> GetAsync(DateOnly requestedDate, CancellationToken cancellationToken)
    {
        var key = BuildKey(requestedDate);
        try
        {
            var payload = await _cache.GetAsync(key, cancellationToken);
            return payload is null ? null : JsonSerializer.Deserialize<DailyRates>(payload, JsonOptions);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось прочитать курсы из кэша, ключ {Key}", key);
            return null;
        }
    }

    public async Task SetAsync(DateOnly requestedDate, DailyRates rates, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = BuildKey(requestedDate);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(rates, JsonOptions);
            var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await _cache.SetAsync(key, payload, entryOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить курсы в кэш, ключ {Key}", key);
        }
    }

    internal string BuildKey(DateOnly date) =>
        _options.KeyPrefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
