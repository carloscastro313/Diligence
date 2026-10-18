using System.Net;
using System.Text.RegularExpressions;
using Diligencia.IntegrationTests.Support;

namespace Diligencia.IntegrationTests.Logging;

public partial class CorrelationMiddlewareTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Header = "X-Correlation-Id";
    private const string Ping = "/api/backoffice/_test/ping";

    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$")]
    private static partial Regex ValidId();

    private async Task<HttpResponseMessage> Send(string? correlationId)
    {
        using var client = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, Ping);
        if (correlationId is not null) req.Headers.TryAddWithoutValidation(Header, correlationId);
        return await client.SendAsync(req, TestContext.Current.CancellationToken);
    }

    private static string IdOf(HttpResponseMessage res)
    {
        Assert.True(res.Headers.TryGetValues(Header, out var values), "Falta el header X-Correlation-Id en la respuesta.");
        return Assert.Single(values);
    }

    [Fact]
    public async Task ValidHeader_IsReusedAndEchoed()
    {
        using var res = await Send("mi-id.123:abc_X");
        Assert.Equal("mi-id.123:abc_X", IdOf(res));
    }

    [Fact]
    public async Task MissingHeader_GeneratesValidId()
    {
        using var res = await Send(null);
        Assert.Matches(ValidId(), IdOf(res));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("a/b")]
    public async Task InvalidHeader_IsReplacedByGeneratedId(string sent)
    {
        using var res = await Send(sent);
        var id = IdOf(res);
        Assert.NotEqual(sent, id);
        Assert.Matches(ValidId(), id);
    }

    [Fact]
    public async Task TooLongHeader_IsReplacedByGeneratedId()
    {
        var sent = new string('a', 200);
        using var res = await Send(sent);
        var id = IdOf(res);
        Assert.NotEqual(sent, id);
        Assert.Matches(ValidId(), id);
    }

    [Fact]
    public async Task Unauthorized401Response_AlsoCarriesHeader()
    {
        using var res = await Send("id-401");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("id-401", IdOf(res));
    }
}
