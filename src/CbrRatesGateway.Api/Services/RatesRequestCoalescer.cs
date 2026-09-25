using System.Collections.Concurrent;
using CbrRatesGateway.Api.Models;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Объединяет одновременные запросы к ЦБ за одну и ту же дату (защита от «cache stampede»).
/// Если кэш пуст и за одну дату пришло N параллельных запросов, в ЦБ уйдёт один запрос,
/// а остальные дождутся его результата.
/// Регистрируется как singleton — общий для всех HTTP-запросов экземпляра сервиса.
/// </summary>
public sealed class RatesRequestCoalescer
{
    private readonly ConcurrentDictionary<DateOnly, Lazy<Task<DailyRates>>> _inFlight = new();
    private readonly ILogger<RatesRequestCoalescer> _logger;

    /// <summary>Создаёт экземпляр.</summary>
    /// <param name="logger">Логгер.</param>
    public RatesRequestCoalescer(ILogger<RatesRequestCoalescer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Выполнить <paramref name="factory"/> для даты или присоединиться к уже выполняющемуся вызову.
    /// </summary>
    /// <param name="date">Ключ — запрошенная дата.</param>
    /// <param name="factory">Загрузка курсов. Не должна зависеть от токена отмены конкретного клиента:
    /// её результат нужен всем ожидающим.</param>
    /// <param name="cancellationToken">Отмена ожидания для текущего клиента (сама загрузка продолжится).</param>
    /// <returns>Результат загрузки — общий для всех, кто ждал эту дату.</returns>
    public Task<DailyRates> RunAsync(DateOnly date, Func<Task<DailyRates>> factory, CancellationToken cancellationToken)
    {
        Lazy<Task<DailyRates>>? created = null;
        var lazy = _inFlight.GetOrAdd(date, _ => created = new Lazy<Task<DailyRates>>(() => InvokeAsync(factory)));

        if (ReferenceEquals(lazy, created))
        {
            _logger.LogDebug("Старт загрузки курсов на {Date} (выполняющихся загрузок: {InFlight})", date, _inFlight.Count);

            // Наш вызов стал «ведущим» — по завершении убираем запись, чтобы следующий промах снова пошёл в ЦБ.
            _ = lazy.Value.ContinueWith(
                _ => _inFlight.TryRemove(new KeyValuePair<DateOnly, Lazy<Task<DailyRates>>>(date, lazy)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        else
        {
            _logger.LogDebug("Загрузка курсов на {Date} уже выполняется — ожидаем её результат", date);
        }

        return lazy.Value.WaitAsync(cancellationToken);
    }

    /// <summary>Даже синхронное исключение фабрики попадает в Task, а не «залипает» в Lazy.</summary>
    private static async Task<DailyRates> InvokeAsync(Func<Task<DailyRates>> factory) => await factory();

    /// <summary>Количество загрузок, выполняющихся прямо сейчас (для тестов и диагностики).</summary>
    internal int InFlightCount => _inFlight.Count;
}
