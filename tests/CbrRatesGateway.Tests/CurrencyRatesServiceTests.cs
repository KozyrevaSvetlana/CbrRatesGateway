using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using CbrRatesGateway.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace CbrRatesGateway.Tests;

public class CurrencyRatesServiceTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    private readonly Mock<ICbrClient> _cbr = new(MockBehavior.Strict);
    private readonly Mock<IRatesCache> _cache = new();
    private readonly RatesCacheOptions _cacheOptions = new();

    // 25.09.2026 10:00 UTC = 13:00 МСК
    private FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 25, 10, 0, 0, TimeSpan.Zero));

    private CurrencyRatesService CreateService() => new(
        _cbr.Object,
        _cache.Object,
        _time,
        Microsoft.Extensions.Options.Options.Create(_cacheOptions),
        NullLogger<CurrencyRatesService>.Instance);

    private void SetupCacheMiss() =>
        _cache.Setup(c => c.GetAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync((DailyRates?)null);

    [Fact]
    public async Task GetRatesAsync_NoDate_UsesCurrentMoscowDate()
    {
        // 24.09.2026 22:30 UTC — в Москве уже 25.09.2026 01:30
        _time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero));
        SetupCacheMiss();
        _cbr.Setup(c => c.GetDailyRatesAsync(Today, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CbrTestData.Sample(Today));

        var result = await CreateService().GetRatesAsync(null, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(Today, result!.RequestedDate);
        _cbr.Verify(c => c.GetDailyRatesAsync(Today, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRatesAsync_CacheHit_DoesNotCallCbr()
    {
        _cache.Setup(c => c.GetAsync(Today, It.IsAny<CancellationToken>()))
              .ReturnsAsync(CbrTestData.Sample());

        var result = await CreateService().GetRatesAsync(Today, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Rates.Count);
        _cbr.VerifyNoOtherCalls();
        _cache.Verify(c => c.SetAsync(It.IsAny<DateOnly>(), It.IsAny<DailyRates>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetRatesAsync_CacheMiss_FetchesFromCbrAndCachesWithFinalTtl()
    {
        var pastDate = new DateOnly(2026, 9, 1);
        var daily = CbrTestData.Sample(pastDate);
        SetupCacheMiss();
        _cbr.Setup(c => c.GetDailyRatesAsync(pastDate, It.IsAny<CancellationToken>())).ReturnsAsync(daily);

        var result = await CreateService().GetRatesAsync(pastDate, null, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(pastDate, result!.RatesDate);
        Assert.Equal(CurrencyRatesService.SourceName, result.Source);
        _cache.Verify(c => c.SetAsync(pastDate, daily, _cacheOptions.FinalRatesTtl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRatesAsync_FutureDateNotYetPublished_CachesWithPendingTtl()
    {
        var tomorrow = Today.AddDays(1);
        var daily = CbrTestData.Sample(Today); // ЦБ ещё не установил курс на завтра
        SetupCacheMiss();
        _cbr.Setup(c => c.GetDailyRatesAsync(tomorrow, It.IsAny<CancellationToken>())).ReturnsAsync(daily);

        var result = await CreateService().GetRatesAsync(tomorrow, null, CancellationToken.None);

        Assert.Equal(tomorrow, result!.RequestedDate);
        Assert.Equal(Today, result.RatesDate);
        _cache.Verify(c => c.SetAsync(tomorrow, daily, _cacheOptions.PendingRatesTtl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRatesAsync_FutureDateAlreadyPublished_CachesWithFinalTtl()
    {
        var tomorrow = Today.AddDays(1);
        var daily = CbrTestData.Sample(tomorrow);
        SetupCacheMiss();
        _cbr.Setup(c => c.GetDailyRatesAsync(tomorrow, It.IsAny<CancellationToken>())).ReturnsAsync(daily);

        await CreateService().GetRatesAsync(tomorrow, null, CancellationToken.None);

        _cache.Verify(c => c.SetAsync(tomorrow, daily, _cacheOptions.FinalRatesTtl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData(" Usd ")]
    public async Task GetRatesAsync_WithCode_ReturnsOnlyThatCurrency_CaseInsensitive(string code)
    {
        _cache.Setup(c => c.GetAsync(Today, It.IsAny<CancellationToken>())).ReturnsAsync(CbrTestData.Sample());

        var result = await CreateService().GetRatesAsync(Today, code, CancellationToken.None);

        var rate = Assert.Single(result!.Rates);
        Assert.Equal("USD", rate.CharCode);
    }

    [Fact]
    public async Task GetRatesAsync_UnknownCode_ReturnsNull()
    {
        _cache.Setup(c => c.GetAsync(Today, It.IsAny<CancellationToken>())).ReturnsAsync(CbrTestData.Sample());

        var result = await CreateService().GetRatesAsync(Today, "XYZ", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRatesAsync_CbrUnavailable_PropagatesException()
    {
        SetupCacheMiss();
        _cbr.Setup(c => c.GetDailyRatesAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CbrUnavailableException("down"));

        await Assert.ThrowsAsync<CbrUnavailableException>(
            () => CreateService().GetRatesAsync(Today, null, CancellationToken.None));
    }
}
