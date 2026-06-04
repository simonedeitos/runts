using runts.Models;
using System.Text.RegularExpressions;

namespace runts.Services;

public sealed class SearchEngineService
{
    public List<string> CostruisciQuery(Ente ente)
    {
        if (ente.Categoria.Equals("Pro Loco", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                $"Pro Loco {ente.Comune} sito ufficiale",
                $"Pro Loco di {ente.Comune}",
                $"{ente.Comune} pro loco contatti",
                $"Associazione Pro Loco {ente.Comune} {ente.Provincia}"
            ];
        }

        return
        [
            $"{ente.Denominazione} sito ufficiale",
            $"{ente.Denominazione} ETS",
            $"{ente.Denominazione} {ente.Comune}"
        ];
    }

    public Task<string> FindBestWebsiteAsync(Ente ente, CancellationToken cancellationToken = default)
    {
        var queries = CostruisciQuery(ente);

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
