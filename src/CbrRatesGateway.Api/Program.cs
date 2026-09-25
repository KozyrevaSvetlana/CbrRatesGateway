using System.Reflection;
using CbrRatesGateway.Api.Infrastructure;
using CbrRatesGateway.Api.Options;
using CbrRatesGateway.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------- Настройки ----------
builder.Services.Configure<CbrOptions>(builder.Configuration.GetSection(CbrOptions.SectionName));
builder.Services.Configure<RatesCacheOptions>(builder.Configuration.GetSection(RatesCacheOptions.SectionName));
builder.Services.AddSingleton(TimeProvider.System);

// ---------- Клиент ЦБ (HttpClientFactory + retry/timeout/circuit breaker) ----------
builder.Services
    .AddHttpClient<ICbrClient, CbrXmlClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<CbrOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    })
    .AddStandardResilienceHandler();

// ---------- Кэш (Redis; без строки подключения — in-memory для локальной отладки) ----------
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<IRatesCache, DistributedRatesCache>();
builder.Services.AddScoped<ICurrencyRatesService, CurrencyRatesService>();

// ---------- Web API ----------
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CbrExceptionHandler>();
builder.Services.AddHealthChecks();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CBR Rates Gateway",
        Version = "v1",
        Description = "Шлюз получения курсов валют Банка России",
    });

    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        options.IncludeXmlComments(xmlPath);
    }
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

/// <summary>Нужен для WebApplicationFactory в интеграционных тестах.</summary>
public partial class Program { }
