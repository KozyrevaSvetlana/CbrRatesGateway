using CbrRatesGateway.Api.Options;
using CbrRatesGateway.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CbrRatesGateway.Tests;

public class DistributedRatesCacheTests
{
    private static DistributedRatesCache Create(IDistributedCache cache) =>
        new(cache, MsOptions.Create(new RatesCacheOptions()), NullLogger<DistributedRatesCache>.Instance);

    [Fact]
    public async Task SetThenGet_ReturnsSameRates()
    {
        var sut = Create(new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions())));
        var date = new DateOnly(2026, 9, 25);
        var rates = CbrTestData.Sample(date);

        await sut.SetAsync(date, rates, TimeSpan.FromMinutes(5), CancellationToken.None);
        var cached = await sut.GetAsync(date, CancellationToken.None);

        Assert.NotNull(cached);
        Assert.Equal(rates.Date, cached!.Date);
        Assert.Equal(rates.Rates, cached.Rates); // records сравниваются по значению
    }

    [Fact]
    public async Task Get_MissingKey_ReturnsNull()
    {
        var sut = Create(new MemoryDistributedCache(MsOptions.Create(new MemoryDistributedCacheOptions())));

        Assert.Null(await sut.GetAsync(new DateOnly(2026, 1, 1), CancellationToken.None));
    }

    [Fact]
    public async Task Set_UsesPrefixedDateKeyAndTtl()
    {
        var redis = new Mock<IDistributedCache>();
        var sut = Create(redis.Object);
        var ttl = TimeSpan.FromHours(1);

        await sut.SetAsync(new DateOnly(2026, 9, 25), CbrTestData.Sample(), ttl, CancellationToken.None);

        redis.Verify(r => r.SetAsync(
            "cbr-gateway:rates:2026-09-25",
            It.IsAny<byte[]>(),
            It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == ttl),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RedisFailure_DoesNotBreakRequest()
    {
        var redis = new Mock<IDistributedCache>();
        redis.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("redis is down"));
        redis.Setup(r => r.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new InvalidOperationException("redis is down"));
        var sut = Create(redis.Object);
        var date = new DateOnly(2026, 9, 25);

        Assert.Null(await sut.GetAsync(date, CancellationToken.None));
        await sut.SetAsync(date, CbrTestData.Sample(), TimeSpan.FromMinutes(1), CancellationToken.None); // не бросает
    }
}
