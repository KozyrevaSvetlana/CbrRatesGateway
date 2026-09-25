using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace CbrRatesGateway.Api.Services;

public sealed class CurrencyRatesService : ICurrencyRatesService
{
    public const string SourceName = "cbr.ru";

    /// <summary>Москва живёт в UTC+3 без перехода на летнее время (с 2014 г.).</summary>
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    private readonly ICbrClient _cbrClient;
    private readonly IRatesCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly RatesCacheOptions _cacheOptions;
    private readonly ILogger<CurrencyRatesService> _logger;

    public CurrencyRatesService(
        ICbrClient cbrClient,
        IRatesCache cache,
        TimeProvider timeProvider,
        IOptions<RatesCacheOptions> cacheOptions,
        ILogger<CurrencyRatesService> logger)
    {
        _cbrClient = cbrClient;
        _cache = cache;
        _timeProvider = timeProvider;
        _cacheOptions = cacheOptions.Value;
        _logger = logger;
    }

    public async Task<CurrencyRatesResponse?> GetRatesAsync(DateOnly? date, string? currencyCode, CancellationToken cancellationToken)
    {
        var today = GetMoscowToday();
        var requestedDate = date ?? today;

        var daily = await _cache.GetAsync(requestedDate, cancellationToken);
        if (daily is null)
        {
            daily = await _cbrClient.GetDailyRatesAsync(requestedDate, cancellationToken);
            var ttl = ChooseTtl(requestedDate, daily.Date, today);
            await _cache.SetAsync(requestedDate, daily, ttl, cancellationToken);
            _logger.LogDebug("Курсы на {Date} получены из ЦБ и закэшированы на {Ttl}", requestedDate, ttl);
        }

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

    internal DateOnly GetMoscowToday() =>
        DateOnly.FromDateTime(_timeProvider.GetUtcNow().ToOffset(MoscowOffset).DateTime);

    /// <summary>
    /// Курсы на прошедшую/текущую дату, а также на будущую дату, если ЦБ их уже опубликовал, — окончательные.
    /// Если запрошена будущая дата, а ЦБ вернул более ранние курсы, — данные предварительные, кэшируем недолго.
    /// </summary>
    internal TimeSpan ChooseTtl(DateOnly requestedDate, DateOnly ratesDate, DateOnly today) =>
        requestedDate <= today || ratesDate >= requestedDate
            ? _cacheOptions.FinalRatesTtl
            : _cacheOptions.PendingRatesTtl;
}
