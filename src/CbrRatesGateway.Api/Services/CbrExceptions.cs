namespace CbrRatesGateway.Api.Services;

/// <summary>Базовая ошибка взаимодействия с Банком России.</summary>
public abstract class CbrException : Exception
{
    protected CbrException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Сайт ЦБ недоступен или вернул неуспешный HTTP-код.</summary>
public sealed class CbrUnavailableException : CbrException
{
    public CbrUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>ЦБ вернул ответ в неожиданном формате.</summary>
public sealed class CbrResponseFormatException : CbrException
{
    public CbrResponseFormatException(string message, Exception? inner = null) : base(message, inner) { }
}
