namespace CbrRatesGateway.Api.Options;

/// <summary>Настройки кэширования курсов в Redis.</summary>
public sealed class RatesCacheOptions
{
    public const string SectionName = "RatesCache";

    /// <summary>Префикс ключей в Redis.</summary>
    public string KeyPrefix { get; set; } = "cbr-gateway:rates:";

    /// <summary>
    /// TTL для окончательных курсов: дата в прошлом/сегодня или ЦБ уже опубликовал курс на запрошенную дату.
    /// Такие курсы больше не меняются.
    /// </summary>
    public TimeSpan FinalRatesTtl { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// TTL для «предварительных» данных: запрошена будущая дата, курс на которую ЦБ ещё не установил
    /// (вернулись последние известные курсы). Держим недолго, чтобы подхватить публикацию.
    /// </summary>
    public TimeSpan PendingRatesTtl { get; set; } = TimeSpan.FromMinutes(15);
}
