using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Разбор ответа сервиса https://www.cbr.ru/scripts/XML_daily.asp.
/// Ответ приходит в кодировке windows-1251, десятичный разделитель — запятая.
/// </summary>
/// <remarks>
/// Пример ответа:
/// <code>
/// &lt;ValCurs Date="25.09.2026" name="Foreign Currency Market"&gt;
///   &lt;Valute ID="R01235"&gt;
///     &lt;NumCode&gt;840&lt;/NumCode&gt;&lt;CharCode&gt;USD&lt;/CharCode&gt;&lt;Nominal&gt;1&lt;/Nominal&gt;
///     &lt;Name&gt;Доллар США&lt;/Name&gt;&lt;Value&gt;83,5211&lt;/Value&gt;&lt;VunitRate&gt;83,5211&lt;/VunitRate&gt;
///   &lt;/Valute&gt;
/// &lt;/ValCurs&gt;
/// </code>
/// Класс статический и не пишет логи: контекст (дата, URL) логирует вызывающий <see cref="CbrXmlClient"/>.
/// </remarks>
public static class CbrXmlParser
{
    /// <summary>Регистрирует провайдер кодовых страниц, чтобы XmlReader понимал encoding="windows-1251".</summary>
    static CbrXmlParser()
    {
        // windows-1251 не входит в набор кодировок .NET по умолчанию.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Прочитать и разобрать XML-ответ ЦБ из потока.</summary>
    /// <param name="stream">Тело HTTP-ответа (кодировка определяется по XML-декларации).</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Дата установления курсов и список курсов всех валют.</returns>
    /// <exception cref="CbrResponseFormatException">Ответ не является корректным XML или не соответствует формату ЦБ.</exception>
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

    /// <summary>Разобрать уже загруженный XML-документ ЦБ.</summary>
    /// <param name="document">Документ с корневым элементом <c>ValCurs</c>.</param>
    /// <returns>Дата установления курсов (атрибут <c>ValCurs/@Date</c>) и курсы всех валют.</returns>
    /// <exception cref="CbrResponseFormatException">Нет элемента <c>ValCurs</c>, некорректная дата или поле валюты.</exception>
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

    /// <summary>Разобрать один элемент <c>Valute</c>.</summary>
    /// <remarks>Если в ответе нет <c>VunitRate</c> (старые даты), курс за единицу вычисляется как Value / Nominal.</remarks>
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

    /// <summary>Значение обязательного дочернего элемента; пустое или отсутствующее — ошибка формата.</summary>
    private static string Required(XElement parent, string name)
    {
        var value = parent.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value)
            ? throw new CbrResponseFormatException($"В элементе Valute отсутствует обязательное поле {name}.")
            : value;
    }

    /// <summary>
    /// Разобрать число в формате ЦБ: десятичный разделитель — запятая, для очень маленьких курсов
    /// (например, VunitRate иранского риала) — экспоненциальная запись: "6,80237E-05".
    /// </summary>
    /// <param name="text">Строка из XML.</param>
    /// <returns>Значение в <see cref="decimal"/>.</returns>
    /// <exception cref="FormatException">Строка не является числом.</exception>
    internal static decimal ParseDecimal(string text) =>
        decimal.Parse(
            text.Trim().Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
}
