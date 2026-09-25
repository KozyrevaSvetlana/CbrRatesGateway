using System.Net;
using CbrRatesGateway.Api.Options;
using CbrRatesGateway.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace CbrRatesGateway.Tests;

public class CbrXmlClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(_responder(request));
        }
    }

    private static CbrXmlClient CreateClient(StubHandler handler)
    {
        var options = new CbrOptions();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };
        return new CbrXmlClient(httpClient, Microsoft.Extensions.Options.Options.Create(options), NullLogger<CbrXmlClient>.Instance);
    }

    [Fact]
    public async Task GetDailyRatesAsync_RequestsXmlDailyWithCbrDateFormat_AndParsesResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(CbrTestData.Windows1251.GetBytes(CbrTestData.DailyXml)),
        });
        var client = CreateClient(handler);

        var result = await client.GetDailyRatesAsync(new DateOnly(2026, 9, 5), CancellationToken.None);

        Assert.Equal("https://www.cbr.ru/scripts/XML_daily.asp?date_req=05/09/2026", handler.LastRequestUri!.ToString());
        Assert.Equal(3, result.Rates.Count);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetDailyRatesAsync_NonSuccessStatus_ThrowsUnavailable(HttpStatusCode status)
    {
        var client = CreateClient(new StubHandler(_ => new HttpResponseMessage(status)));

        await Assert.ThrowsAsync<CbrUnavailableException>(
            () => client.GetDailyRatesAsync(new DateOnly(2026, 9, 25), CancellationToken.None));
    }

    [Fact]
    public async Task GetDailyRatesAsync_NetworkError_ThrowsUnavailable()
    {
        var client = CreateClient(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var ex = await Assert.ThrowsAsync<CbrUnavailableException>(
            () => client.GetDailyRatesAsync(new DateOnly(2026, 9, 25), CancellationToken.None));
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
