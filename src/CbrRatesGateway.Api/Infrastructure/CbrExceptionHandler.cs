using CbrRatesGateway.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CbrRatesGateway.Api.Infrastructure;

/// <summary>Преобразует ошибки обращения к ЦБ в ответ 502 Bad Gateway (RFC 7807).</summary>
public sealed class CbrExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<CbrExceptionHandler> _logger;

    public CbrExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<CbrExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CbrException cbrException)
        {
            return false;
        }

        _logger.LogError(exception, "Ошибка при обращении к сайту Банка России");

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
