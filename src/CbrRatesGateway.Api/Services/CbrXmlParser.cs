using CbrRatesGateway.Api.Models;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Разбор ответа сервиса https://www.cbr.ru/scripts/XML_daily.asp.
/// Ответ приходит в кодировке windows-1251, десятичный разделитель — запятая.
/// </summary>
public static class CbrXmlParser
{
    static CbrXmlParser()
    {
        // windows-1251 не входит в набор кодировок .NET по умолчанию.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<DailyRates> ParseAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        XDocument document;
        try
        {
            document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        }
        catch (XmlException ex)
        {
            throw new CbrResponseFormatException("Ответ ЦБ не является корректным XML.", ex);
        }

        return Parse(document);
    }

    public static DailyRates Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "ValCurs")
        {
            throw new CbrResponseFormatException("В ответе ЦБ отсутствует корневой элемент ValCurs.");
        }

        var dateText = (string?)root.Attribute("Date");
        if (!DateOnly.TryParseExact(dateText, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new CbrResponseFormatException($"Некорректная дата курсов в ответе ЦБ: '{dateText}'.");
        }

        var rates = root.Elements("Valute").Select(ParseValute).ToList();
        return new DailyRates(date, rates);
    }

    private static CurrencyRate ParseValute(XElement valute)
    {
        try
        {
            var nominal = int.Parse(Required(valute, "Nominal"), NumberStyles.Integer, CultureInfo.InvariantCulture);
            if (nominal <= 0)
            {
                throw new CbrResponseFormatException($"Некорректный номинал валюты: {nominal}.");
            }

            var value = ParseDecimal(Required(valute, "Value"));
            var unitRateElement = valute.Element("VunitRate");
            var unitRate = unitRateElement is not null
                ? ParseDecimal(unitRateElement.Value)
                : Math.Round(value / nominal, 8);

            return new CurrencyRate(
                Id: (string?)valute.Attribute("ID") ?? string.Empty,
                NumCode: Required(valute, "NumCode"),
                CharCode: Required(valute, "CharCode").ToUpperInvariant(),
                Nominal: nominal,
                Name: Required(valute, "Name"),
                Value: value,
                UnitRate: unitRate);
        }
        catch (Exception ex) when (ex is FormatException or OverflowException)
        {
            throw new CbrResponseFormatException("Некорректное числовое значение в ответе ЦБ.", ex);
        }
    }

    private static string Required(XElement parent, string name)
    {
        var value = parent.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value)
            ? throw new CbrResponseFormatException($"В элементе Valute отсутствует обязательное поле {name}.")
            : value;
    }

    internal static decimal ParseDecimal(string text) =>
        decimal.Parse(
            text.Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
}
