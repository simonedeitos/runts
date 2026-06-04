namespace runts.Helpers;

public static class HttpClientHelper
{
    private const string DefaultUserAgent = "RUNTS-Contact-Finder/1.0 (+https://github.com/simonedeitos/runts)";

    public static HttpClient CreateDefaultClient(IServiceProvider _)
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        var userAgent = Environment.GetEnvironmentVariable("RUNTS_USER_AGENT");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent);
        return client;
    }
}
