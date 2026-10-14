using System.Net;
using System.Net.Http.Headers;

namespace ClaudeUsageTray;

/// <summary>
/// Calls the subscription usage endpoint that Claude Code's /usage screen uses. Undocumented and heavily
/// rate limited; <see cref="PollPolicy"/> decides how often this may run.
/// </summary>
internal sealed class LimitsClient : IDisposable
{
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";

    /// <summary>Treat a token this close to expiry as expired: it would die in flight.</summary>
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private static readonly string UserAgentVersion = typeof(LimitsClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private readonly string _credentialsPath;
    private readonly TimeProvider _time;
    private readonly HttpClient _http;

    public LimitsClient(string configDir)
        : this(configDir, new SocketsHttpHandler
        {
            // A process that runs for months must still pick up DNS changes.
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            AllowAutoRedirect = false,
        }, TimeProvider.System)
    {
    }

    public LimitsClient(string configDir, HttpMessageHandler handler, TimeProvider time)
    {
        _credentialsPath = Path.Combine(configDir, Credentials.FileName);
        _time = time;
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15),
            DefaultRequestHeaders = { UserAgent = { new ProductInfoHeaderValue("ClaudeUsageTray", UserAgentVersion) } },
        };
    }

    public async Task<FetchResult> FetchAsync(CancellationToken ct)
    {
        // Re-read every time: Claude Code refreshes the token and rewrites the file.
        var credentials = ReadCredentials();
        if (credentials is null)
            return new FetchResult(FetchStatus.NoToken, Error: "no OAuth token found");
        if (credentials.ExpiresAt <= _time.GetUtcNow() + ExpirySkew)
            return new FetchResult(FetchStatus.TokenExpired, Error: "token expired, start Claude Code");

        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var code = (int)response.StatusCode;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new FetchResult(FetchStatus.RateLimited, RetryAfter: RetryAfter(response.Headers.RetryAfter, _time.GetUtcNow()), Error: "rate limited (429)");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new FetchResult(FetchStatus.Unauthorized, Error: $"token rejected ({code})");
            if (!response.IsSuccessStatusCode)
                return new FetchResult(FetchStatus.Failed, Error: $"usage endpoint returned {code}");

            var limits = UsageParser.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return limits is null
                ? new FetchResult(FetchStatus.Failed, Error: "unexpected response shape")
                : new FetchResult(FetchStatus.Ok, limits);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return new FetchResult(FetchStatus.Failed, Error: "request timed out");
        }
        catch (HttpRequestException ex)
        {
            return new FetchResult(FetchStatus.Failed, Error: ex.Message);
        }
    }

    /// <returns>The current version of the credentials file, or null when there is none.</returns>
    public CredentialsVersion? CredentialsVersion() =>
        SafeFile.LastWriteUtcOrNull(_credentialsPath) is { } written ? new CredentialsVersion(written) : null;

    /// <returns>The wait the server asked for; zero or negative when it named a time already past.</returns>
    internal static TimeSpan? RetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now) =>
        header switch
        {
            { Delta: { } delta } => delta,
            { Date: { } date } => date - now,
            _ => null,
        };

    private Credentials? ReadCredentials() =>
        SafeFile.ReadAllTextOrNull(_credentialsPath) is { } json ? Credentials.Parse(json) : null;

    public void Dispose() => _http.Dispose();
}
