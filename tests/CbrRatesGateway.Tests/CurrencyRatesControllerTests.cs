using CbrRatesGateway.Api.Controllers;
using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace CbrRatesGateway.Tests;

public class CurrencyRatesControllerTests
{
    private readonly Mock<ICurrencyRatesService> _service = new();

    // 25.09.2026 10:00 UTC = 13:00 МСК
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

    private CurrencyRatesController CreateController() => new(_service.Object, _time);

    [Fact]
    public async Task GetRates_Found_Returns200WithBody()
    {
        var date = new DateOnly(2026, 9, 25);
        var response = new CurrencyRatesResponse(date, date, "cbr.ru", CbrTestData.Sample(date).Rates);
        _service.Setup(s => s.GetRatesAsync(date, "USD", It.IsAny<CancellationToken>())).ReturnsAsync(response);

        var result = await CreateController().GetRates(date, "USD", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(response, ok.Value);
    }

    [Fact]
    public async Task GetRates_NotFound_Returns204()
    {
        _service.Setup(s => s.GetRatesAsync(It.IsAny<DateOnly?>(), "XYZ", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CurrencyRatesResponse?)null);

        var result = await CreateController().GetRates(null, "XYZ", CancellationToken.None);

        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task GetRates_DateBeforeCbrHistory_Returns400()
    {
        var result = await CreateController().GetRates(new DateOnly(1990, 1, 1), null, CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.IsType<ValidationProblemDetails>(badRequest.Value);
        _service.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(2026, 9, 27)]   // послезавтра
    [InlineData(2099, 1, 1)]
    public async Task GetRates_DateAfterTomorrow_Returns400(int y, int m, int d)
    {
        var result = await CreateController().GetRates(new DateOnly(y, m, d), null, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        _service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetRates_Tomorrow_IsAllowed()
    {
        var tomorrow = new DateOnly(2026, 9, 26);
        _service.Setup(s => s.GetRatesAsync(tomorrow, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CurrencyRatesResponse(tomorrow, tomorrow, "cbr.ru", CbrTestData.Sample(tomorrow).Rates));

        var result = await CreateController().GetRates(tomorrow, null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
    }
}
