namespace CbrRatesGateway.Api.Models;

/// <summary>Ответ шлюза на запрос курсов валют.</summary>
/// <param name="RequestedDate">Дата из запроса (или текущая дата по Москве, если параметр не передан).</param>
/// <param name="RatesDate">Дата, на которую ЦБ фактически установил курсы.
/// Может отличаться от запрошенной для выходных и праздничных дней.</param>
/// <param name="Source">Источник данных.</param>
/// <param name="Rates">Курсы валют.</param>
public sealed record CurrencyRatesResponse(
    DateOnly RequestedDate,
    DateOnly RatesDate,
    string Source,
    IReadOnlyList<CurrencyRate> Rates);
