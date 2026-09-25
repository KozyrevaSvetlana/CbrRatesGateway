using System.Net;
using System.Text.Json;
using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace CbrRatesGateway.Tests;

/// <summary>
/// Проверка HTTP-контракта целиком (маршрут, валидация, коды ответов, JSON).
/// ЦБ подменён моком, Redis — in-memory кэшем, поэтому внешние зависимости не нужны.
/// </summary>
public class ApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(Mock<ICbrClient> cbr) =>
        _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICbrClient>();
            services.AddSingleton(cbr.Object);

            services.RemoveAll<IDistributedCache>();
            services.AddDistributedMemoryCache();

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero)));
        })).CreateClient();

    private static Mock<ICbrClient> CbrReturningSample()
    {
        var cbr = new Mock<ICbrClient>();
        cbr.Setup(c => c.GetDailyRatesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
           .ReturnsAsync((DateOnly d, CancellationToken _) => CbrTestData.Sample(d));
        return cbr;
    }

    [Fact]
    public async Task Get_WithoutParameters_Returns200WithAllRatesForToday()
    {
        var client = CreateClient(CbrReturningSample());

        var response = await client.GetAsync("/api/v1/currency-rates");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2026-09-25", json.RootElement.GetProperty("requestedDate").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("rates").GetArrayLength());
    }

    [Fact]
    public async Task Get_WithDateAndCode_Returns200WithSingleRate()
    {
        var client = CreateClient(CbrReturningSample());

        var response = await client.GetAsync("/api/v1/currency-rates?date=2026-09-01&code=eur");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var rates = json.RootElement.GetProperty("rates");
        Assert.Equal(1, rates.GetArrayLength());
        Assert.Equal("EUR", rates[0].GetProperty("charCode").GetString());
        Assert.Equal(97.1045m, rates[0].GetProperty("value").GetDecimal());
    }

    [Fact]
    public async Task Get_SecondRequestForSameDate_ServedFromCache()
    {
        var cbr = CbrReturningSample();
        var client = CreateClient(cbr);

        await client.GetAsync("/api/v1/currency-rates?date=2026-09-01");
        await client.GetAsync("/api/v1/currency-rates?date=2026-09-01&code=USD");

        cbr.Verify(c => c.GetDailyRatesAsync(new DateOnly(2026, 9, 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_UnknownCurrency_Returns204()
    {
        var client = CreateClient(CbrReturningSample());

        var response = await client.GetAsync("/api/v1/currency-rates?code=XYZ");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("?code=US")]
    [InlineData("?code=1234")]
    [InlineData("?date=25.09.2026")]
    [InlineData("?date=1990-01-01")]
    [InlineData("?date=2026-09-27")]
    [InlineData("?date=2099-01-01")]
    public async Task Get_InvalidParameters_Returns400(string query)
    {
        var client = CreateClient(CbrReturningSample());

        var response = await client.GetAsync("/api/v1/currency-rates" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_CbrUnavailable_Returns502ProblemDetails()
    {
        var cbr = new Mock<ICbrClient>();
        cbr.Setup(c => c.GetDailyRatesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
           .ThrowsAsync(new CbrUnavailableException("Сайт ЦБ вернул HTTP 503."));
        var client = CreateClient(cbr);

        var response = await client.GetAsync("/api/v1/currency-rates?date=2026-08-01");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
