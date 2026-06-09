using CsvHelper;
using CsvHelper.Configuration;
using EasySearch.Models;
using System.Globalization;
using System.Text;

namespace EasySearch.Services;

/// <summary>
/// Importa la lista ufficiale dei comuni ISTAT da CSV.
/// </summary>
public sealed class IstatComuniImporter
{
    private readonly LoggerService _logger;

    public IstatComuniImporter(LoggerService logger)
    {
        _logger = logger;
    }

    public async Task<List<ComuneIstat>> LoadComuniAsync(string csvPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("Il percorso del CSV ISTAT è obbligatorio.", nameof(csvPath));
        }

        if (!File.Exists(csvPath))
        {
            throw new FileNotFoundException("File CSV ISTAT non trovato.", csvPath);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        await _logger.LogAsync($"📂 Caricamento comuni da: {csvPath}", cancellationToken);

        var comuni = new List<ComuneIstat>();
        var encoding = Encoding.GetEncoding(1252);

        await using var stream = File.OpenRead(csvPath);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        });

        if (!await csv.ReadAsync() || !csv.ReadHeader())
        {
            return comuni;
        }

        var headers = csv.HeaderRecord ?? [];
        await _logger.LogAsync($"Header CSV: {string.Join(";", headers.Take(6))}...", cancellationToken);

        var lineNumber = 1;
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            try
            {
                var comune = ParseRecord(csv.Parser.Record ?? []);
                if (comune is not null)
                {
                    comuni.Add(comune);
                }
            }
            catch (Exception ex)
            {
                await _logger.LogAsync($"⚠ Errore riga {lineNumber}: {ex.Message}", cancellationToken);
            }
        }

        await _logger.LogAsync($"✓ Caricati {comuni.Count} comuni", cancellationToken);
        return comuni;
    }

    public List<ComuneIstat> FilterByRegione(IEnumerable<ComuneIstat> comuni, string regione)
    {
        return FilterByRegioneAsync(comuni, regione).GetAwaiter().GetResult();
    }

    public async Task<List<ComuneIstat>> FilterByRegioneAsync(
        IEnumerable<ComuneIstat> comuni,
        string regione,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filtered = comuni
                .Where(c => NormalizeRegion(c.Regione).Equals(NormalizeRegion(regione), StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Provincia)
                .ThenBy(c => c.Nome)
                .ToList();

            await _logger.LogAsync($"✓ Filtrati {filtered.Count} comuni per regione: {regione}", cancellationToken);
            return filtered;
        }, cancellationToken);
    }

    private static ComuneIstat? ParseRecord(string[] parts)
    {
        if (parts.Length < 15)
        {
            return null;
        }

        var codiceComune = GetField(parts, 4);
        var denominazione = GetField(parts, 6);
        var regione = NormalizeRegion(GetField(parts, 10));
        var provincia = GetField(parts, 12);
        var siglaProvincia = GetField(parts, 14);

        if (string.IsNullOrWhiteSpace(denominazione) || string.IsNullOrWhiteSpace(regione))
        {
            return null;
        }

        return new ComuneIstat
        {
            CodiceComune = codiceComune,
            Nome = denominazione,
            Provincia = provincia,
            SiglaProvincia = siglaProvincia,
            Regione = regione
        };
    }

    private static string GetField(string[] parts, int index) => index < parts.Length ? parts[index].Trim() : string.Empty;

    private static string NormalizeRegion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = RemoveAccents(value)
            .Replace("’", "'")
            .Trim();

        return normalized switch
        {
            var x when x.StartsWith("Valle d'Aosta", StringComparison.OrdinalIgnoreCase) => "Valle d'Aosta",
            var x when x.StartsWith("Trentino-Alto Adige", StringComparison.OrdinalIgnoreCase) => "Trentino-Alto Adige",
            _ => normalized
        };
    }

    private static string RemoveAccents(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var filtered = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(filtered).Normalize(NormalizationForm.FormC);
    }
}
