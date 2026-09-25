using System.Diagnostics;
using System.Globalization;
using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Options;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace CbrRatesGateway.Api.Services;

/// <summary>
/// Клиент XML-сервиса ЦБ «Ежедневные курсы валют» (XML_daily.asp).
/// Один запрос возвращает курсы всех валют на дату, что идеально ложится на кэширование «дата → все курсы».
/// </summary>
/// <remarks>
/// Повторы, таймауты и circuit breaker выполняет resilience-пайплайн HttpClient (см. Program.cs),
/// поэтому здесь только сам запрос, разбор ответа и перевод сетевых сбоев в <see cref="CbrUnavailableException"/>.
/// </remarks>
public sealed class CbrXmlClient : ICbrClient
{
    private readonly HttpClient _httpClient;
    private readonly CbrOptions _options;
    private readonly ILogger<CbrXmlClient> _logger;

    /// <summary>Создаёт клиент. Экземпляр выдаёт HttpClientFactory (typed client).</summary>
    /// <param name="httpClient">HttpClient с настроенными BaseAddress, User-Agent и resilience-пайплайном.</param>
    /// <param name="options">Настройки подключения к ЦБ.</param>
    /// <param name="logger">Логгер.</param>
    public CbrXmlClient(HttpClient httpClient, IOptions<CbrOptions> options, ILogger<CbrXmlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <exception cref="CbrUnavailableException">Сайт ЦБ недоступен, вернул неуспешный код или сработал таймаут.</exception>
    /// <exception cref="CbrResponseFormatException">Ответ ЦБ не удалось разобрать.</exception>
    public async Task<DailyRates> GetDailyRatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(date);
        _logger.LogInformation("Запрос курсов ЦБ на {Date}: {Uri}", date, requestUri);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _logger.LogDebug(
                "ЦБ ответил HTTP {StatusCode} на {Date} за {ElapsedMs} мс",
                (int)response.StatusCode, date, stopwatch.ElapsedMilliseconds);

            if (!response.IsSuccessStatusCode)
            {
                throw new CbrUnavailableException($"Сайт ЦБ вернул HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var result = await CbrXmlParser.ParseAsync(stream, cancellationToken);

            _logger.LogInformation(
                "Курсы ЦБ на {Date} получены за {ElapsedMs} мс: {RatesCount} валют, дата установления курсов {RatesDate}",
                date, stopwatch.ElapsedMilliseconds, result.Rates.Count, result.Date);
            return result;
        }
        catch (CbrException e)
        {
            // Здесь только контекст (дата, URL) на уровне Warning.
            // Ошибку уровня Error пишет CbrExceptionHandler — иначе каждый сбой попадал бы в лог дважды.
            _logger.LogWarning(e, "Ошибка при обработке ответа ЦБ на {Date} через {ElapsedMs} мс: {Uri}",
                date, stopwatch.ElapsedMilliseconds, requestUri);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Запрос курсов ЦБ на {Date}: {Uri} был отменен через {ElapsedMs} мс.",
                date, requestUri, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            _logger.LogWarning(ex, "Не удалось получить данные с сайта Банка России на {Date} через {ElapsedMs} мс: {Uri}",
                date, stopwatch.ElapsedMilliseconds, requestUri);
            throw new CbrUnavailableException("Не удалось получить данные с сайта Банка России.", ex);
        }
        // Прочие исключения (NullReferenceException и т.п.) — это баги, а не недоступность ЦБ:
        // пробрасываем как есть, чтобы получить 500 и не маскировать их под 502.
    }

    /// <summary>Сетевые сбои и срабатывания resilience-пайплайна (таймаут, открытый circuit breaker).</summary>
    /// <param name="ex">Исключение, выброшенное при запросе.</param>
    /// <returns><c>true</c>, если это недоступность ЦБ, а не ошибка в коде.</returns>
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException
            or IOException
            or TimeoutRejectedException
            or BrokenCircuitException
            or OperationCanceledException; // таймаут самого HttpClient (токен клиента не отменён — это отсеяно выше)

    /// <summary>Относительный URL запроса к ЦБ, например <c>scripts/XML_daily.asp?date_req=25/09/2026</c>.</summary>
    /// <param name="date">Дата курса.</param>
    internal string BuildRequestUri(DateOnly date) =>
        $"{_options.DailyRatesPath}?date_req={date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
}
