namespace CbrRatesGateway.Api.Options;

/// <summary>Настройки подключения к сайту Банка России.</summary>
public sealed class CbrOptions
{
    /// <summary>Имя секции в appsettings.json.</summary>
    public const string SectionName = "Cbr";

    /// <summary>Базовый адрес сайта ЦБ.</summary>
    public string BaseUrl { get; set; } = "https://www.cbr.ru/";

    /// <summary>Путь к XML-сервису ежедневных курсов.</summary>
    public string DailyRatesPath { get; set; } = "scripts/XML_daily.asp";

    /// <summary>User-Agent исходящих запросов (ЦБ может отклонять запросы без него).</summary>
    public string UserAgent { get; set; } = "CbrRatesGateway/1.0";
}
