namespace CbrRatesGateway.Api.Services;

/// <summary>Текущая дата по Москве — ЦБ устанавливает курсы по московскому времени.</summary>
public static class MoscowClock
{
    /// <summary>Москва живёт в UTC+3 без перехода на летнее время (с 2014 г.).</summary>
    private static readonly TimeSpan MoscowOffset = TimeSpan.FromHours(3);

    /// <summary>Текущая дата в Москве.</summary>
    /// <param name="timeProvider">Источник времени (в тестах — FakeTimeProvider).</param>
    /// <returns>Дата по московскому времени: например, 24.09 23:30 UTC — это уже 25.09 в Москве.</returns>
    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(timeProvider.GetUtcNow().ToOffset(MoscowOffset).DateTime);
}
