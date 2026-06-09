namespace EasySearch.Helpers;

public static class HttpClientHelper
{
    private const string DefaultUserAgent = "EasySearch/1.0 (+https://github.com/simonedeitos/runts)";

    public static HttpClient CreateDefaultClient(IServiceProvider _)
        => CreateClient(TimeSpan.FromSeconds(15));

    public static HttpClient CreateClient(TimeSpan timeout)
    {
        var client = new HttpClient
        {
            Timeout = timeout
        };

        var userAgent = Environment.GetEnvironmentVariable("RUNTS_USER_AGENT");
        client.DefaultRequestHeaders.UserAgent.ParseAdd(string.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent);
        return client;
    }
}
