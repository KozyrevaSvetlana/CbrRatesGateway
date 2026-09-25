using CbrRatesGateway.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CbrRatesGateway.Api.Infrastructure;

/// <summary>Преобразует ошибки обращения к ЦБ в ответ 502 Bad Gateway (RFC 7807).</summary>
/// <remarks>
/// Остальные исключения не обрабатывает (возвращает <c>false</c>) — их превращает в 500
/// стандартный обработчик <c>UseExceptionHandler</c> + <c>AddProblemDetails</c>.
/// </remarks>
public sealed class CbrExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<CbrExceptionHandler> _logger;

    /// <summary>Создаёт обработчик.</summary>
    /// <param name="problemDetailsService">Сервис записи ответа в формате ProblemDetails.</param>
    /// <param name="logger">Логгер.</param>
    public CbrExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<CbrExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    /// <summary>Обработать исключение, если это ошибка обращения к ЦБ.</summary>
    /// <param name="httpContext">Контекст текущего запроса.</param>
    /// <param name="exception">Необработанное исключение.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns><c>true</c> — ответ 502 записан; <c>false</c> — исключение не наше, обработку продолжит следующий обработчик.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CbrException cbrException)
        {
            return false;
        }

        _logger.LogError(exception,
            "Ошибка при обращении к сайту Банка России ({ExceptionType}) при обработке {Method} {Path}{Query}, TraceId {TraceId}",
            exception.GetType().Name,
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.Request.QueryString,
            httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status502BadGateway;
        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Сервис Банка России недоступен",
                Detail = cbrException.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.3",
            },
        });
    }
}
