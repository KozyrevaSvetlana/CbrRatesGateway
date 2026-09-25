using System.ComponentModel.DataAnnotations;
using CbrRatesGateway.Api.Models;
using CbrRatesGateway.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CbrRatesGateway.Api.Controllers;

/// <summary>Курсы валют Банка России.</summary>
/// <remarks>
/// Контроллер только проверяет параметры и переводит результат сервиса в HTTP-код.
/// Ошибки ЦБ сюда не перехватываются — их превращает в 502 <c>CbrExceptionHandler</c>.
/// </remarks>
[ApiController]
[Route("api/v1/currency-rates")]
[Produces("application/json")]
public sealed class CurrencyRatesController : ControllerBase
{
    /// <summary>Самая ранняя дата, за которую ЦБ публикует курсы.</summary>
    internal static readonly DateOnly MinDate = new(1992, 7, 1);

    private readonly ICurrencyRatesService _service;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CurrencyRatesController> _logger;

    /// <summary>Создаёт контроллер.</summary>
    /// <param name="service">Сервис курсов валют.</param>
    /// <param name="timeProvider">Источник текущего времени — для проверки верхней границы даты.</param>
    /// <param name="logger">Логгер.</param>
    public CurrencyRatesController(ICurrencyRatesService service, TimeProvider timeProvider, ILogger<CurrencyRatesController> logger)
    {
        _service = service;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Получить курсы валют на дату.</summary>
    /// <param name="date">Дата курса в формате yyyy-MM-dd, с 1992-07-01 по завтрашний день (МСК).
    /// Если не передана — текущая дата (МСК).</param>
    /// <param name="code">Буквенный код валюты ISO 4217 (USD, EUR, ...). Если не передан — все валюты.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Курсы валют или пустой ответ 204.</returns>
    /// <response code="200">Курсы найдены.</response>
    /// <response code="204">Запрошенная валюта (или курсы на дату) отсутствует.</response>
    /// <response code="400">Некорректные параметры запроса.</response>
    /// <response code="502">Сайт Банка России недоступен или вернул некорректный ответ.</response>
    [HttpGet]
    [ProducesResponseType(typeof(CurrencyRatesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<CurrencyRatesResponse>> GetRates(
        [FromQuery] DateOnly? date,
        [FromQuery, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Код валюты должен состоять из трёх латинских букв (ISO 4217).")]
        string? code,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Запрос курсов: date={Date}, code={Code}", date, code);

        if (date is { } requested)
        {
            // ЦБ публикует курс не более чем на следующий день. Более поздние даты вернули бы
            // последний известный курс под видом будущего и засоряли бы кэш.
            var maxDate = MoscowClock.Today(_timeProvider).AddDays(1);
            if (requested < MinDate || requested > maxDate)
            {
                _logger.LogInformation(
                    "Отклонён запрос с датой {Date} вне диапазона {MinDate}..{MaxDate} — ответ 400",
                    requested, MinDate, maxDate);
                ModelState.AddModelError(nameof(date), $"Дата должна быть в диапазоне с {MinDate:yyyy-MM-dd} по {maxDate:yyyy-MM-dd}.");
                return BadRequest(new ValidationProblemDetails(ModelState) { Status = StatusCodes.Status400BadRequest });
            }
        }

        var result = await _service.GetRatesAsync(date, code, cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }
}
