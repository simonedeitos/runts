using runts.Models;
using System.Text.RegularExpressions;

namespace runts.Services;

public sealed class SearchEngineService
{
    public Task<string> FindBestWebsiteAsync(Ente ente, CancellationToken cancellationToken = default)
    {
        var queries = new[]
        {
            $"{ente.Denominazione} sito ufficiale",
            $"{ente.Denominazione} ETS",
            $"{ente.Denominazione} Pro Loco"
        };

        _ = queries;
        cancellationToken.ThrowIfCancellationRequested();

        var slug = Regex.Replace(ente.Denominazione.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrWhiteSpace(slug))
        {
            return Task.FromResult(string.Empty);
        }

        return Task.FromResult($"https://www.{slug}.it");
    }
}
