using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using Microsoft.Extensions.Options;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Бизнес-логика шлюза: определить дату, взять курсы из кэша или ЦБ, отфильтровать по коду валюты.
/// </summary>
/// <remarks>
/// Порядок: кэш (Redis) → при промахе одна загрузка из ЦБ на дату (<see cref="RatesRequestCoalescer"/>)
/// → запись в кэш с TTL по <see cref="ChooseTtl"/> → фильтр по коду.
/// </remarks>
public sealed class CurrencyRatesService : ICurrencyRatesService
{
    /// <summary>Значение поля <c>source</c> в ответе.</summary>
    public const string SourceName = "cbr.ru";

    private readonly ICbrClient _cbrClient;
    private readonly IRatesCache _cache;
    private readonly RatesRequestCoalescer _coalescer;
    private readonly TimeProvider _timeProvider;
    private readonly RatesCacheOptions _cacheOptions;
    private readonly ILogger<CurrencyRatesService> _logger;

    /// <summary>Создаёт сервис.</summary>
    /// <param name="cbrClient">Клиент сайта ЦБ.</param>
    /// <param name="cache">Кэш курсов.</param>
    /// <param name="coalescer">Объединение параллельных загрузок за одну дату.</param>
    /// <param name="timeProvider">Источник текущего времени (подменяется в тестах).</param>
    /// <param name="cacheOptions">Настройки TTL кэша.</param>
    /// <param name="logger">Логгер.</param>
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

    /// <inheritdoc />
    public async Task<CurrencyRatesResponse?> GetRatesAsync(DateOnly? date, string? currencyCode, CancellationToken cancellationToken)
    {
        var today = MoscowClock.Today(_timeProvider);
        var requestedDate = date ?? today;

        // Scope добавляет дату и код ко всем логам внутри запроса (включая кэш и клиент ЦБ)
        using var scope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestedDate"] = requestedDate,
            ["CurrencyCode"] = currencyCode,
        });

        var daily = await _cache.GetAsync(requestedDate, cancellationToken);
        if (daily is null)
        {
            _logger.LogDebug("Курсов на {Date} нет в кэше — загружаем из ЦБ", requestedDate);
            daily = await _coalescer.RunAsync(requestedDate, () => LoadAndCacheAsync(requestedDate, today), cancellationToken);
        }

        IReadOnlyList<CurrencyRate> rates = daily.Rates;
        if (!string.IsNullOrWhiteSpace(currencyCode))
        {
            var code = currencyCode.Trim();
            rates = daily.Rates
                .Where(r => string.Equals(r.CharCode, code, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (rates.Count == 0)
            {
                _logger.LogInformation(
                    "Валюта {CurrencyCode} отсутствует в курсах ЦБ на {Date} (всего валют: {RatesCount}) — ответ 204",
                    code, daily.Date, daily.Rates.Count);
                return null;
            }
        }

        if (rates.Count == 0)
        {
            _logger.LogInformation("Нет курсов ЦБ на {Date} — ответ 204", requestedDate);
            return null;
        }

        _logger.LogDebug("Возвращаем {RatesCount} курс(ов) на {Date} (дата курсов ЦБ {RatesDate})",
            rates.Count, requestedDate, daily.Date);
        return new CurrencyRatesResponse(requestedDate, daily.Date, SourceName, rates);
    }

    /// <summary>
    /// Загрузка из ЦБ и запись в кэш. Выполняется одна на дату, результат получают все ожидающие,
    /// поэтому токен отмены конкретного клиента сюда не передаётся (время ограничивает resilience-пайплайн HttpClient).
    /// </summary>
    /// <param name="requestedDate">Дата из запроса (ключ кэша).</param>
    /// <param name="today">Текущая дата по Москве — для выбора TTL.</param>
    /// <returns>Курсы, полученные от ЦБ.</returns>
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
        _logger.LogInformation(
            "Курсы на {Date} закэшированы на {Ttl} ({TtlKind})",
            requestedDate, ttl, ttl == _cacheOptions.FinalRatesTtl ? "окончательные" : "предварительные");
        return daily;
    }

    /// <summary>
    /// Курсы на прошедшую/текущую дату, а также на будущую дату, если ЦБ их уже опубликовал, — окончательные.
    /// Если запрошена будущая дата, а ЦБ вернул более ранние курсы, — данные предварительные, кэшируем недолго.
    /// </summary>
    /// <param name="requestedDate">Дата из запроса.</param>
    /// <param name="ratesDate">Дата, на которую ЦБ фактически установил курсы.</param>
    /// <param name="today">Текущая дата по Москве.</param>
    /// <returns><see cref="RatesCacheOptions.FinalRatesTtl"/> или <see cref="RatesCacheOptions.PendingRatesTtl"/>.</returns>
    internal TimeSpan ChooseTtl(DateOnly requestedDate, DateOnly ratesDate, DateOnly today) =>
        requestedDate <= today || ratesDate >= requestedDate
            ? _cacheOptions.FinalRatesTtl
            : _cacheOptions.PendingRatesTtl;
}
