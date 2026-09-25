using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace CbrRatesGateway.Api.Services;

public sealed class CurrencyRatesService : ICurrencyRatesService
{
    public const string SourceName = "cbr.ru";

    private readonly ICbrClient _cbrClient;
    private readonly IRatesCache _cache;
    private readonly RatesRequestCoalescer _coalescer;
    private readonly TimeProvider _timeProvider;
    private readonly RatesCacheOptions _cacheOptions;
    private readonly ILogger<CurrencyRatesService> _logger;

    public CurrencyRatesService(
        ICbrClient cbrClient,
        IRatesCache cache,
        RatesRequestCoalescer coalescer,
        TimeProvider timeProvider,
        IOptions<RatesCacheOptions> cacheOptions,
        ILogger<CurrencyRatesService> logger)
    {
        _cbrClient = cbrClient;
        _cache = cache;
        _coalescer = coalescer;
        _timeProvider = timeProvider;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<CurrencyRatesResponse?> GetRatesAsync(DateOnly? date, string? currencyCode, CancellationToken cancellationToken)
    {
        var today = MoscowClock.Today(_timeProvider);
        var requestedDate = date ?? today;

        var daily = await _cache.GetAsync(requestedDate, cancellationToken)
                    ?? await _coalescer.RunAsync(requestedDate, () => LoadAndCacheAsync(requestedDate, today), cancellationToken);

        IReadOnlyList<CurrencyRate> rates = daily.Rates;
        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            var code = currencyCode.Trim();
            rates = daily.Rates
                .Where(r => string.Equals(r.CharCode, code, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return rates.Count == 0
            ? null
            : new CurrencyRatesResponse(requestedDate, daily.Date, SourceName, rates);
    }

    /// <summary>
    /// Загрузка из ЦБ и запись в кэш. Выполняется одна на дату, результат получают все ожидающие,
    /// поэтому токен отмены конкретного клиента сюда не передаётся (время ограничивает resilience-пайплайн HttpClient).
    /// </summary>
    private async Task<DailyRates> LoadAndCacheAsync(DateOnly requestedDate, DateOnly today)
    {
        var daily = await _cbrClient.GetDailyRatesAsync(requestedDate, CancellationToken.None);

        if (daily.Rates.Count == 0)
        {
            // Пустой ответ может быть временным сбоем на стороне ЦБ — не кэшируем, чтобы не отдавать 204 неделю.
            _logger.LogWarning("ЦБ вернул пустой список курсов на {Date}; результат не кэшируется", requestedDate);
            return daily;
        }

        var ttl = ChooseTtl(requestedDate, daily.Date, today);
        await _cache.SetAsync(requestedDate, daily, ttl, CancellationToken.None);
        _logger.LogDebug("Курсы на {Date} получены из ЦБ и закэшированы на {Ttl}", requestedDate, ttl);
        return daily;
    }

    /// <summary>
    /// Курсы на прошедшую/текущую дату, а также на будущую дату, если ЦБ их уже опубликовал, — окончательные.
    /// Если запрошена будущая дата, а ЦБ вернул более ранние курсы, — данные предварительные, кэшируем недолго.
    /// </summary>
    internal TimeSpan ChooseTtl(DateOnly requestedDate, DateOnly ratesDate, DateOnly today) =>
        requestedDate <= today || ratesDate >= requestedDate
            ? _cacheOptions.FinalRatesTtl
            : _cacheOptions.PendingRatesTtl;
}
