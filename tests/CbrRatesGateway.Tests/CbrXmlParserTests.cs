using CbrRatesGateway.Api.Services;

namespace CbrRatesGateway.Tests;

public class CbrXmlParserTests
{
    [Fact]
    public async Task ParseAsync_Windows1251Document_ParsesDateAndAllRates()
    {
        await using var stream = CbrTestData.ToWindows1251Stream(CbrTestData.DailyXml);

        var result = await CbrXmlParser.ParseAsync(stream);

        Assert.Equal(new DateOnly(2026, 9, 25), result.Date);
        Assert.Equal(3, result.Rates.Count);

        var usd = Assert.Single(result.Rates, r => r.CharCode == "USD");
        Assert.Equal("R01235", usd.Id);
        Assert.Equal("840", usd.NumCode);
        Assert.Equal(1, usd.Nominal);
        Assert.Equal("Доллар США", usd.Name); // проверка корректной перекодировки windows-1251
        Assert.Equal(83.5211m, usd.Value);
        Assert.Equal(83.5211m, usd.UnitRate);
    }

    [Fact]
    public async Task ParseAsync_NoVunitRate_CalculatesUnitRateFromNominal()
    {
        await using var stream = CbrTestData.ToWindows1251Stream(CbrTestData.DailyXml);

        var result = await CbrXmlParser.ParseAsync(stream);

        var jpy = Assert.Single(result.Rates, r => r.CharCode == "JPY");
        Assert.Equal(100, jpy.Nominal);
        Assert.Equal(57.1234m, jpy.Value);
        Assert.Equal(0.571234m, jpy.UnitRate);
    }

    [Fact]
    public async Task ParseAsync_EmptyValCurs_ReturnsNoRates()
    {
        const string xml = """<?xml version="1.0" encoding="windows-1251"?><ValCurs Date="01.07.1992" name="Foreign Currency Market"></ValCurs>""";
        await using var stream = CbrTestData.ToWindows1251Stream(xml);

        var result = await CbrXmlParser.ParseAsync(stream);

        Assert.Equal(new DateOnly(1992, 7, 1), result.Date);
        Assert.Empty(result.Rates);
    }

    [Fact]
    public async Task ParseAsync_UnexpectedRoot_ThrowsFormatException()
    {
        const string xml = """<?xml version="1.0" encoding="windows-1251"?><Error>Error in parameters</Error>""";
        await using var stream = CbrTestData.ToWindows1251Stream(xml);

        await Assert.ThrowsAsync<CbrResponseFormatException>(() => CbrXmlParser.ParseAsync(stream));
    }

    [Fact]
    public async Task ParseAsync_MalformedXml_ThrowsFormatException()
    {
        await using var stream = CbrTestData.ToWindows1251Stream("<ValCurs Date=\"25.09.2026\"><Valute>");

        await Assert.ThrowsAsync<CbrResponseFormatException>(() => CbrXmlParser.ParseAsync(stream));
    }

    [Fact]
    public async Task ParseAsync_InvalidNumber_ThrowsFormatException()
    {
        const string xml =
            """
            <?xml version="1.0" encoding="windows-1251"?>
            <ValCurs Date="25.09.2026" name="Foreign Currency Market">
              <Valute ID="R01235"><NumCode>840</NumCode><CharCode>USD</CharCode><Nominal>1</Nominal><Name>Доллар США</Name><Value>abc</Value></Valute>
            </ValCurs>
            """;
        await using var stream = CbrTestData.ToWindows1251Stream(xml);

        await Assert.ThrowsAsync<CbrResponseFormatException>(() => CbrXmlParser.ParseAsync(stream));
    }
}
