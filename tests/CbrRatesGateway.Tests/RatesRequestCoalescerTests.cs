using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Services;

namespace CbrRatesGateway.Tests;

public class RatesRequestCoalescerTests
{
    private static readonly DateOnly Date = new(2026, 9, 25);

    [Fact]
    public async Task RunAsync_SameDateInParallel_FactoryCalledOnce()
    {
        var sut = new RatesRequestCoalescer();
        var calls = 0;
        var gate = new TaskCompletionSource<DailyRates>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task<DailyRates> Factory()
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        }

        var tasks = Enumerable.Range(0, 50)
            .Select(_ => Task.Run(() => sut.RunAsync(Date, Factory, CancellationToken.None)))
            .ToArray();

        gate.SetResult(CbrTestData.Sample(Date));
        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, calls);
        Assert.All(results, r => Assert.Same(results[0], r));
    }

    [Fact]
    public async Task RunAsync_AfterCompletion_NextCallStartsNewLoad()
    {
        var sut = new RatesRequestCoalescer();
        var calls = 0;

        Task<DailyRates> Factory()
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(CbrTestData.Sample(Date));
        }

        await sut.RunAsync(Date, Factory, CancellationToken.None);
        await sut.RunAsync(Date, Factory, CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(0, sut.InFlightCount);
    }

    [Fact]
    public async Task RunAsync_FactoryFails_ErrorIsNotStuck()
    {
        var sut = new RatesRequestCoalescer();

        await Assert.ThrowsAsync<CbrUnavailableException>(() =>
            sut.RunAsync(Date, () => throw new CbrUnavailableException("down"), CancellationToken.None));

        var result = await sut.RunAsync(Date, () => Task.FromResult(CbrTestData.Sample(Date)), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0, sut.InFlightCount);
    }

    [Fact]
    public async Task RunAsync_DifferentDates_LoadIndependently()
    {
        var sut = new RatesRequestCoalescer();
        var calls = 0;

        Task<DailyRates> Factory()
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(CbrTestData.Sample(Date));
        }

        await Task.WhenAll(
            sut.RunAsync(new DateOnly(2026, 9, 1), Factory, CancellationToken.None),
            sut.RunAsync(new DateOnly(2026, 9, 2), Factory, CancellationToken.None));

        Assert.Equal(2, calls);
    }
}
