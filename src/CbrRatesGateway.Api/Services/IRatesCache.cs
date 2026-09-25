using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>Кэш курсов (ключ — запрошенная дата, значение — все курсы на неё).</summary>
/// <remarks>Реализации не должны выбрасывать исключения при недоступности хранилища: кэш — оптимизация, а не источник данных.</remarks>
public interface IRatesCache
{
    /// <summary>Прочитать курсы из кэша.</summary>
    /// <param name="requestedDate">Дата из запроса клиента.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Курсы или <c>null</c>, если записи нет или кэш недоступен.</returns>
    Task<DailyRates?> GetAsync(DateOnly requestedDate, CancellationToken cancellationToken);

    /// <summary>Сохранить курсы в кэш.</summary>
    /// <param name="requestedDate">Дата из запроса клиента (ключ).</param>
    /// <param name="rates">Курсы, полученные от ЦБ.</param>
    /// <param name="ttl">Время жизни записи.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    Task SetAsync(DateOnly requestedDate, DailyRates rates, TimeSpan ttl, CancellationToken cancellationToken);
}
