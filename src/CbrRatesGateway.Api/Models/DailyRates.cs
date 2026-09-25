namespace CbrRatesGateway.Api.Models;

/// <summary>Набор курсов, полученный от Банка России на одну дату.</summary>
/// <param name="Date">Дата, на которую ЦБ установил курсы (атрибут ValCurs/@Date).</param>
/// <param name="Rates">Курсы всех валют.</param>
public sealed record DailyRates(DateOnly Date, IReadOnlyList<CurrencyRate> Rates);
