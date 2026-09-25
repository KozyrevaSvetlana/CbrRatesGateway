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
public sealed class CbrXmlClient : ICbrClient
{
    private readonly HttpClient _httpClient;
    private readonly CbrOptions _options;
    private readonly ILogger<CbrXmlClient> _logger;

    public CbrXmlClient(HttpClient httpClient, IOptions<CbrOptions> options, ILogger<CbrXmlClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DailyRates> GetDailyRatesAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var requestUri = BuildRequestUri(date);
        _logger.LogInformation("Запрос курсов ЦБ на {Date}: {Uri}", date, requestUri);

        try
        {
            using var response = await _httpClient.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new CbrUnavailableException($"Сайт ЦБ вернул HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await CbrXmlParser.ParseAsync(stream, cancellationToken);
        }
        catch (CbrException e)
        {
            // Здесь только контекст (дата, URL) на уровне Warning.
            // Ошибку уровня Error пишет CbrExceptionHandler — иначе каждый сбой попадал бы в лог дважды.
            _logger.LogWarning(e, "Ошибка при обработке ответа ЦБ на {Date}: {Uri}", date, requestUri);
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Запрос курсов ЦБ на {Date}: {Uri} был отменен.", date, requestUri);
            throw;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            _logger.LogWarning(ex, "Не удалось получить данные с сайта Банка России на {Date}: {Uri}", date, requestUri);
            throw new CbrUnavailableException("Не удалось получить данные с сайта Банка России.", ex);
        }
        // Прочие исключения (NullReferenceException и т.п.) — это баги, а не недоступность ЦБ:
        // пробрасываем как есть, чтобы получить 500 и не маскировать их под 502.
    }

    /// <summary>Сетевые сбои и срабатывания resilience-пайплайна (таймаут, открытый circuit breaker).</summary>
    private static bool IsTransportFailure(Exception ex) =>
        ex is HttpRequestException
            or IOException
            or TimeoutRejectedException
            or BrokenCircuitException
            or OperationCanceledException; // таймаут самого HttpClient (токен клиента не отменён — это отсеяно выше)

    internal string BuildRequestUri(DateOnly date) =>
        $"{_options.DailyRatesPath}?date_req={date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
}
