using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>Клиент сайта Банка России.</summary>
public interface ICbrClient
{
    /// <summary>Получить курсы всех валют на дату.</summary>
    /// <param name="date">Дата курса. Для выходных и праздников ЦБ вернёт курсы последнего рабочего дня.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Дата, на которую ЦБ установил курсы, и курсы всех валют.</returns>
    Task<DailyRates> GetDailyRatesAsync(DateOnly date, CancellationToken cancellationToken);
}
