using AngleSharp;
using runts.Helpers;
using runts.Models;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace runts.Services;

public enum ImportMode
{
    Runts = 0,
    ProLocoAlbiPdf = 1,
    ProLocoPerComune = 2
}

public sealed class RuntsImporter
{
    private readonly CsvManager _csvManager;
    private readonly HttpClient _httpClient;
    private readonly LoggerService _logger;
    private readonly PdfProLocoImporter _pdfProLocoImporter;

    private const string RuntsSearchUrl = "https://www.runts.it/ricerca-enti";
    private const string DatiGovSearchUrl = "https://www.dati.gov.it/api/3/action/package_search?q=runts+enti+terzo+settore&rows=25";
    private const string ComuniJsonUrl = "https://raw.githubusercontent.com/matteocontrini/comuni-json/master/comuni.json";

    public RuntsImporter(CsvManager csvManager, HttpClient httpClient, LoggerService logger, PdfProLocoImporter pdfProLocoImporter)
    {
        _csvManager = csvManager;
        _httpClient = httpClient;
        _logger = logger;
        _pdfProLocoImporter = pdfProLocoImporter;
    }

    public IReadOnlyCollection<string> GetSupportedPdfRegions() => _pdfProLocoImporter.GetSupportedRegions();

    public async Task<int> ImportRegioneAsync(string regione, ImportMode mode, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var enti = mode switch
        {
            ImportMode.Runts => await ImportaEntiRealiDaRuntsAsync(regione, progress, cancellationToken),
            ImportMode.ProLocoAlbiPdf => await _pdfProLocoImporter.ImportaDaPdfAlboRegionaleAsync(regione, progress, cancellationToken),
            ImportMode.ProLocoPerComune => await ImportaProLocoPerComuneAsync(regione, progress, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modalità di importazione non supportata.")
        };

        await _csvManager.CreateBackupAsync($"import_{mode}", cancellationToken);
        await _csvManager.UpsertManyAsync(enti, cancellationToken);
        return enti.Count;
    }

    public async Task<List<Ente>> ImportaEntiRealiDaRuntsAsync(string regione, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report($"Import RUNTS {regione}: tentativo portale ufficiale...");
        var enti = await TryImportFromRuntsPortalAsync(regione, progress, cancellationToken);
        if (enti.Count > 0)
        {
            await _logger.LogAsync($"Importati {enti.Count} enti RUNTS per regione {regione} da portale RUNTS.", cancellationToken);
            return enti;
        }

        progress?.Report($"Portale RUNTS non disponibile per {regione}. Tentativo Open Data dati.gov.it...");
        try
        {
            enti = await TryImportFromDatiGovAsync(regione, cancellationToken);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Open Data dati.gov.it non disponibile: {ex.Message}", cancellationToken);
            enti = [];
        }
        if (enti.Count > 0)
        {
            await _logger.LogAsync($"Importati {enti.Count} enti RUNTS per regione {regione} da Open Data.", cancellationToken);
            return enti;
        }

        var importPath = Path.Combine(FileHelper.DataRoot, "Import", $"{regione}.csv");
        if (File.Exists(importPath))
        {
            progress?.Report($"Nessun endpoint remoto disponibile. Uso file locale: {Path.GetFileName(importPath)}");
            var local = await ReadFromSourceAsync(importPath, cancellationToken);
            await _logger.LogAsync($"Importati {local.Count} enti RUNTS per regione {regione} da file locale.", cancellationToken);
            return local;
        }

        throw new InvalidOperationException($"Impossibile scaricare dati reali RUNTS per la regione {regione}. Nessun dato demo disponibile.");
    }

    public async Task<List<Ente>> ImportaProLocoPerComuneAsync(string regione, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report($"Download comuni per la regione {regione}...");
        var comuni = await ScaricaComuniRegioneAsync(regione, cancellationToken);

        var proLoco = comuni.Select(comune => new Ente
        {
            Regione = regione,
            Provincia = comune.Provincia,
            Comune = comune.Nome,
            Denominazione = $"Pro Loco di {comune.Nome}",
            CodiceFiscale = string.Empty,
            Categoria = "Pro Loco",
            Stato = StatoEnte.DA_ELABORARE
        }).ToList();

        await _logger.LogAsync($"Generati {proLoco.Count} record Pro Loco per {comuni.Count} comuni in regione {regione}.", cancellationToken);
        progress?.Report($"Generati {proLoco.Count} record Pro Loco ({comuni.Count} comuni).");
        return proLoco;
    }

    private async Task<List<Ente>> TryImportFromRuntsPortalAsync(string regione, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var config = Configuration.Default;
        var context = BrowsingContext.New(config);
        var result = new List<Ente>();

        for (var page = 1; page <= 250; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = $"{RuntsSearchUrl}?regione={Uri.EscapeDataString(regione)}&page={page}";

            string html;
            try
            {
                html = await GetStringWithRetryAsync(url, cancellationToken);
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"RUNTS page {page} errore: {ex.Message}", cancellationToken);
                break;
            }

            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);
            var rows = document.QuerySelectorAll("table tbody tr");
            if (rows.Length == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                var values = row.QuerySelectorAll("td")
                    .Select(x => Regex.Replace(x.TextContent ?? string.Empty, "\\s+", " ").Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToList();

                if (values.Count < 4)
                {
                    continue;
                }

                var denominazione = values.ElementAtOrDefault(0) ?? string.Empty;
                var codiceFiscale = values.FirstOrDefault(v => Regex.IsMatch(v, "^[0-9A-Za-z]{11,16}$")) ?? string.Empty;
                var provincia = values.ElementAtOrDefault(Math.Min(2, values.Count - 1)) ?? string.Empty;
                var comune = values.ElementAtOrDefault(Math.Min(3, values.Count - 1)) ?? string.Empty;
                var categoria = values.ElementAtOrDefault(Math.Min(4, values.Count - 1)) ?? "ETS";

                if (string.IsNullOrWhiteSpace(denominazione))
                {
                    continue;
                }

                result.Add(new Ente
                {
                    Regione = regione,
                    Provincia = provincia,
                    Comune = comune,
                    Denominazione = denominazione,
                    CodiceFiscale = codiceFiscale,
                    Categoria = categoria,
                    Stato = StatoEnte.DA_ELABORARE
                });
            }

            progress?.Report($"RUNTS {regione}: pagina {page} letta, enti raccolti {result.Count}.");
        }

        return result
            .GroupBy(GetEntityKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<Ente>> TryImportFromDatiGovAsync(string regione, CancellationToken cancellationToken)
    {
        var enti = new List<Ente>();
        var payload = await GetStringWithRetryAsync(DatiGovSearchUrl, cancellationToken);
        using var json = JsonDocument.Parse(payload);

        if (!json.RootElement.TryGetProperty("result", out var resultElement) ||
            !resultElement.TryGetProperty("results", out var packages))
        {
            return enti;
        }

        foreach (var package in packages.EnumerateArray())
        {
            if (!package.TryGetProperty("resources", out var resources))
            {
                continue;
            }

            foreach (var resource in resources.EnumerateArray())
            {
                var format = resource.TryGetProperty("format", out var formatElement) ? formatElement.GetString() : string.Empty;
                if (!"csv".Equals(format, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = resource.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : string.Empty;
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                var parsed = await ReadEntitiesFromOpenDataCsvAsync(url, regione, cancellationToken);
                enti.AddRange(parsed);
            }
        }

        return enti
            .GroupBy(GetEntityKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private async Task<List<Comune>> ScaricaComuniRegioneAsync(string regione, CancellationToken cancellationToken)
    {
        var cachePath = Path.Combine(FileHelper.DataRoot, "Temp", "comuni.json");
        if (!File.Exists(cachePath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) > TimeSpan.FromDays(7))
        {
            var response = await GetStringWithRetryAsync(ComuniJsonUrl, cancellationToken);
            await File.WriteAllTextAsync(cachePath, response, cancellationToken);
        }

        var content = await File.ReadAllTextAsync(cachePath, cancellationToken);
        using var doc = JsonDocument.Parse(content);
        var list = new List<Comune>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var regioneNome = item.GetProperty("regione").GetProperty("nome").GetString() ?? string.Empty;
            if (!NormalizeValue(regioneNome).Equals(NormalizeValue(regione), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            list.Add(new Comune
            {
                Nome = item.GetProperty("nome").GetString() ?? string.Empty,
                Provincia = item.GetProperty("provincia").GetProperty("sigla").GetString() ?? string.Empty,
                Regione = regioneNome
            });
        }

        return list;
    }

    private static async Task<List<Ente>> ReadFromSourceAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken);
        return ParseOpenDataRows(content, null)
            .Select(x =>
            {
                x.Stato = StatoEnte.DA_ELABORARE;
                return x;
            })
            .ToList();
    }

    private async Task<List<Ente>> ReadEntitiesFromOpenDataCsvAsync(string url, string regione, CancellationToken cancellationToken)
    {
        try
        {
            var content = await GetStringWithRetryAsync(url, cancellationToken);
            return ParseOpenDataRows(content, regione);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"OpenData CSV non disponibile ({url}): {ex.Message}", cancellationToken);
            return [];
        }
    }

    private static List<Ente> ParseOpenDataRows(string content, string? regioneFilter)
    {
        foreach (var delimiter in new[] { ";", "," })
        {
            try
            {
                using var reader = new StringReader(content);
                using var csv = new CsvHelper.CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(System.Globalization.CultureInfo.InvariantCulture)
                {
                    Delimiter = delimiter,
                    MissingFieldFound = null,
                    HeaderValidated = null,
                    BadDataFound = null
                });

                if (!csv.Read() || !csv.ReadHeader())
                {
                    continue;
                }

                var headers = csv.HeaderRecord ?? [];
                if (headers.Length < 2)
                {
                    continue;
                }

                var result = new List<Ente>();
                while (csv.Read())
                {
                    var regione = GetField(csv, headers, "regione", "regione_sede");
                    if (!string.IsNullOrWhiteSpace(regioneFilter) &&
                        !NormalizeValue(regione).Equals(NormalizeValue(regioneFilter), StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var denominazione = GetField(csv, headers, "denominazione", "ente", "ragione_sociale", "nome");
                    if (string.IsNullOrWhiteSpace(denominazione))
                    {
                        continue;
                    }

                    result.Add(new Ente
                    {
                        Regione = regione,
                        Provincia = GetField(csv, headers, "provincia", "prov"),
                        Comune = GetField(csv, headers, "comune", "comune_sede"),
                        Denominazione = denominazione,
                        CodiceFiscale = GetField(csv, headers, "codice_fiscale", "codicefiscale", "cf"),
                        Categoria = GetField(csv, headers, "categoria", "sezione", "tipologia", "qualifica", "natura_giuridica"),
                        Stato = StatoEnte.DA_ELABORARE
                    });
                }

                if (result.Count > 0)
                {
                    return result;
                }
            }
            catch
            {
                // Se il parsing fallisce con un delimitatore, provo il successivo.
            }
        }

        return [];
    }

    private static string GetField(CsvHelper.CsvReader csv, string[] headers, params string[] candidates)
    {
        var matchingHeader = headers.FirstOrDefault(h => candidates.Any(c => NormalizeValue(h).Contains(NormalizeValue(c), StringComparison.OrdinalIgnoreCase)));
        return matchingHeader is null ? string.Empty : csv.GetField(matchingHeader)?.Trim() ?? string.Empty;
    }

    private async Task<string> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception ex) when (attempt < 3)
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new HttpRequestException($"Download fallito: {url}", lastError);
    }

    private static string GetEntityKey(Ente ente)
    {
        if (!string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            return ente.CodiceFiscale.Trim().ToUpperInvariant();
        }

        return $"{NormalizeValue(ente.Regione)}|{NormalizeValue(ente.Comune)}|{NormalizeValue(ente.Categoria)}";
    }

    private static string NormalizeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .ToUpperInvariant()
            .Trim();
    }
}
