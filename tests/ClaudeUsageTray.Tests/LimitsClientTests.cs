using System.Net;
using Microsoft.Extensions.Time.Testing;

namespace ClaudeUsageTray.Tests;

public sealed class LimitsClientTests : IDisposable
{
    private static readonly DateTimeOffset Now = TestClock.Now;

    private readonly string _configDir = Directory.CreateTempSubdirectory("ClaudeUsageTray.Tests.").FullName;
    private readonly FakeTimeProvider _time = new(Now);

    public void Dispose() => Directory.Delete(_configDir, recursive: true);

    [Fact]
    public async Task OkResponseIsParsed()
    {
        WriteCredentials(expiresAt: Now.AddHours(1));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{ "five_hour": { "utilization": 23 } }"""),
        });

        var result = await FetchAsync(handler);

        Assert.Equal(FetchStatus.Ok, result.Status);
        Assert.Equal(23, result.Limits?.FiveHour?.UsedPercent);
    }

    [Fact]
    public async Task SendsTheTokenAndTheBetaHeader()
    {
        WriteCredentials(expiresAt: Now.AddHours(1));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });

        await FetchAsync(handler);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer tok", request.Headers.Authorization?.ToString());
        Assert.Contains("oauth-2025-04-20", request.Headers.GetValues("anthropic-beta"));
        Assert.StartsWith("ClaudeUsageTray/", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, nameof(FetchStatus.Unauthorized))]
    [InlineData(HttpStatusCode.Forbidden, nameof(FetchStatus.Unauthorized))]
    [InlineData(HttpStatusCode.TooManyRequests, nameof(FetchStatus.RateLimited))]
    [InlineData(HttpStatusCode.InternalServerError, nameof(FetchStatus.Failed))]
    public async Task MapsStatusCodes(HttpStatusCode code, string expected)
    {
        WriteCredentials(expiresAt: Now.AddHours(1));

        var result = await FetchAsync(new StubHandler(_ => new HttpResponseMessage(code)));

        Assert.Equal(Enum.Parse<FetchStatus>(expected), result.Status);
    }

    [Fact]
    public async Task ReadsRetryAfterFromA429()
    {
        WriteCredentials(expiresAt: Now.AddHours(1));
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new(TimeSpan.FromSeconds(600));

        var result = await FetchAsync(new StubHandler(_ => response));

        Assert.Equal(TimeSpan.FromSeconds(600), result.RetryAfter);
    }

    [Fact]
    public async Task ANetworkErrorIsAFailureNotAnException()
    {
        WriteCredentials(expiresAt: Now.AddHours(1));

        var result = await FetchAsync(new StubHandler(_ => throw new HttpRequestException("No such host is known.")));

        Assert.Equal(FetchStatus.Failed, result.Status);
    }

    [Fact]
    public async Task ATokenAboutToExpireIsNotSent()
    {
        WriteCredentials(expiresAt: Now.AddSeconds(10));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await FetchAsync(handler);

        Assert.Equal(FetchStatus.TokenExpired, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task NoCredentialsFileMeansNoCall()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await FetchAsync(handler);

        Assert.Equal(FetchStatus.NoToken, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void CredentialsVersionFollowsTheFile()
    {
        using var client = new LimitsClient(_configDir, new StubHandler(_ => new HttpResponseMessage()), _time);
        Assert.Null(client.CredentialsVersion());

        WriteCredentials(expiresAt: Now.AddHours(1));
        var first = client.CredentialsVersion();
        File.SetLastWriteTimeUtc(Path.Combine(_configDir, Credentials.FileName), DateTime.UtcNow.AddMinutes(1));

        Assert.NotNull(first);
        Assert.NotEqual(first, client.CredentialsVersion());
    }

    private async Task<FetchResult> FetchAsync(StubHandler handler)
    {
        using var client = new LimitsClient(_configDir, handler, _time);
        return await client.FetchAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
    }

    private void WriteCredentials(DateTimeOffset expiresAt) =>
        File.WriteAllText(
            Path.Combine(_configDir, Credentials.FileName),
            $$"""{ "claudeAiOauth": { "accessToken": "tok", "expiresAt": {{expiresAt.ToUnixTimeMilliseconds()}} } }""");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }
}
