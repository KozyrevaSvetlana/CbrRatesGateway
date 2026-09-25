using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>Клиент сайта Банка России.</summary>
public interface ICbrClient
{
    /// <summary>Получить курсы всех валют на дату.</summary>
    Task<DailyRates> GetDailyRatesAsync(DateOnly date, CancellationToken cancellationToken);
}
