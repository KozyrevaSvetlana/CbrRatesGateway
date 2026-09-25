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
/// <remarks>Значение хранится как JSON (<see cref="DailyRates"/>), ключ — <c>{KeyPrefix}{yyyy-MM-dd}</c>.</remarks>
public sealed class DistributedRatesCache : IRatesCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly RatesCacheOptions _options;
    private readonly ILogger<DistributedRatesCache> _logger;

    /// <summary>Создаёт кэш.</summary>
    /// <param name="cache">Распределённый кэш (Redis или in-memory).</param>
    /// <param name="options">Настройки кэша курсов.</param>
    /// <param name="logger">Логгер.</param>
    public DistributedRatesCache(IDistributedCache cache, IOptions<RatesCacheOptions> options, ILogger<DistributedRatesCache> logger)
    {
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DailyRates?> GetAsync(DateOnly requestedDate, CancellationToken cancellationToken)
    {
        var key = BuildKey(requestedDate);
        try
        {
            var payload = await _cache.GetAsync(key, cancellationToken);
            if (payload is null)
            {
                _logger.LogDebug("Промах кэша: {Key}", key);
                return null;
            }

            var rates = JsonSerializer.Deserialize<DailyRates>(payload, JsonOptions);
            _logger.LogDebug("Попадание в кэш: {Key}, {RatesCount} валют", key, rates?.Rates.Count ?? 0);
            return rates;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось прочитать курсы из кэша, ключ {Key}", key);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SetAsync(DateOnly requestedDate, DailyRates rates, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var key = BuildKey(requestedDate);
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(rates, JsonOptions);
            var entryOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl };
            await _cache.SetAsync(key, payload, entryOptions, cancellationToken);
            _logger.LogDebug("Курсы сохранены в кэш: {Key}, TTL {Ttl}, {PayloadBytes} байт", key, ttl, payload.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить курсы в кэш, ключ {Key}", key);
        }
    }

    /// <summary>Ключ записи в кэше для даты, например <c>cbr-gateway:rates:2026-09-25</c>.</summary>
    /// <param name="date">Запрошенная дата.</param>
    internal string BuildKey(DateOnly date) =>
        _options.KeyPrefix + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
