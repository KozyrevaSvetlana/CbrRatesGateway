using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>Кэш курсов (ключ — запрошенная дата, значение — все курсы на неё).</summary>
public interface IRatesCache
{
    Task<DailyRates?> GetAsync(DateOnly requestedDate, CancellationToken cancellationToken);

    Task SetAsync(DateOnly requestedDate, DailyRates rates, TimeSpan ttl, CancellationToken cancellationToken);
}
