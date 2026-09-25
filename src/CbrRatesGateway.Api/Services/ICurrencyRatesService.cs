using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

public interface ICurrencyRatesService
{
    /// <summary>
    /// Получить курсы валют.
    /// </summary>
    /// <param name="date">Дата курса; null — текущая дата по Москве.</param>
    /// <param name="currencyCode">Буквенный код валюты; null/пусто — все валюты.</param>
    /// <param name="cancellationToken">Токен отмены запроса.</param>
    /// <returns>Курсы или null, если запрошенной валюты (или курсов на дату) нет.</returns>
    Task<CurrencyRatesResponse?> GetRatesAsync(DateOnly? date, string? currencyCode, CancellationToken cancellationToken);
}
