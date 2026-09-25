namespace CbrRatesGateway.Api.Models;

/// <summary>Курс одной валюты, установленный Банком России.</summary>
/// <param name="Id">Внутренний идентификатор валюты в справочнике ЦБ (например, R01235).</param>
/// <param name="NumCode">Цифровой код ISO 4217 (например, 840).</param>
/// <param name="CharCode">Буквенный код ISO 4217 (например, USD).</param>
/// <param name="Nominal">Номинал — за сколько единиц валюты указан курс.</param>
/// <param name="Name">Наименование валюты.</param>
/// <param name="Value">Курс в рублях за <see cref="Nominal"/> единиц валюты.</param>
/// <param name="UnitRate">Курс в рублях за одну единицу валюты.</param>
public sealed record CurrencyRate(
    string Id,
    string NumCode,
    string CharCode,
    int Nominal,
    string Name,
    decimal Value,
    decimal UnitRate);
