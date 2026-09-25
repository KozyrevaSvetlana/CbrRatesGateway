using System.Text;
using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Tests;

/// <summary>Тестовые данные в формате ответа XML_daily.asp (значения курсов условные).</summary>
internal static class CbrTestData
{
    static CbrTestData()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding Windows1251 => Encoding.GetEncoding(1251);

    public const string DailyXml =
        """
        <?xml version="1.0" encoding="windows-1251"?>
        <ValCurs Date="25.09.2026" name="Foreign Currency Market">
          <Valute ID="R01235">
            <NumCode>840</NumCode>
            <CharCode>USD</CharCode>
            <Nominal>1</Nominal>
            <Name>Доллар США</Name>
            <Value>83,5211</Value>
            <VunitRate>83,5211</VunitRate>
          </Valute>
          <Valute ID="R01239">
            <NumCode>978</NumCode>
            <CharCode>EUR</CharCode>
            <Nominal>1</Nominal>
            <Name>Евро</Name>
            <Value>97,1045</Value>
            <VunitRate>97,1045</VunitRate>
          </Valute>
          <Valute ID="R01820">
            <NumCode>392</NumCode>
            <CharCode>JPY</CharCode>
            <Nominal>100</Nominal>
            <Name>Японских иен</Name>
            <Value>57,1234</Value>
          </Valute>
        </ValCurs>
        """;

    public static MemoryStream ToWindows1251Stream(string xml) => new(Windows1251.GetBytes(xml));

    public static DailyRates Sample(DateOnly? date = null) => new(
        date ?? new DateOnly(2026, 9, 25),
        new[]
        {
            new CurrencyRate("R01235", "840", "USD", 1, "Доллар США", 83.5211m, 83.5211m),
            new CurrencyRate("R01239", "978", "EUR", 1, "Евро", 97.1045m, 97.1045m),
        });
}
