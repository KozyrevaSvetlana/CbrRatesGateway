namespace CbrRatesGateway.Api.Services;

/// <summary>Базовая ошибка взаимодействия с Банком России. Обрабатывается <c>CbrExceptionHandler</c> → HTTP 502.</summary>
public abstract class CbrException : Exception
{
    /// <summary>Создаёт исключение.</summary>
    /// <param name="message">Описание ошибки (попадает в поле <c>detail</c> ответа 502).</param>
    /// <param name="inner">Исходное исключение, если есть.</param>
    protected CbrException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Сайт ЦБ недоступен, вернул неуспешный HTTP-код или не ответил вовремя.</summary>
public sealed class CbrUnavailableException : CbrException
{
    /// <summary>Создаёт исключение.</summary>
    /// <param name="message">Описание ошибки.</param>
    /// <param name="inner">Исходное сетевое исключение, если есть.</param>
    public CbrUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>ЦБ вернул ответ в неожиданном формате (не XML, нет обязательных полей, некорректные числа).</summary>
public sealed class CbrResponseFormatException : CbrException
{
    /// <summary>Создаёт исключение.</summary>
    /// <param name="message">Описание ошибки.</param>
    /// <param name="inner">Исходное исключение разбора, если есть.</param>
    public CbrResponseFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
