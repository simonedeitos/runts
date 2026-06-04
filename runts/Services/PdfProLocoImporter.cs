using AngleSharp.Html.Parser;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using runts.Helpers;
using runts.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace runts.Services;

/// <summary>
/// Servizio per l'importazione di Pro Loco da PDF degli albi regionali ufficiali
/// o da registri regionali web quando il PDF non è disponibile.
/// </summary>
public sealed class PdfProLocoImporter
{
    private const string EmiliaRomagnaRegistryUrl = "https://wwwservizi.regione.emilia-romagna.it/registropersonegiuridiche/Default.aspx?RipetiRicerca=1";
    private static readonly Regex CodiceFiscaleRegex = new(@"\b(?:\d{11}|[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z])\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProvinciaRegex = new(@"\(([A-Z]{2})\)|\bProv(?:incia)?\.?\s*([A-Z]{2})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProLocoRegex = new(@"\b(?:ASSOCIAZIONE\s+)?PRO\s*LOCO\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ComuneLabelRegex = new(@"(?:COMUNE|SEDE(?:\s+LEGALE)?|LOCALIT[ÀA])[:\s\-]+([A-ZÀ-ÖØ-Ý][A-ZÀ-ÖØ-Ý'`\-\s\.]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex MultipleSpacesRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly string[] SupportedRegions = ["Emilia-Romagna", "Lombardia", "Marche", "Piemonte", "Veneto"];
    private static readonly IReadOnlyDictionary<string, string> AlbiRegionaliPdf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Lombardia"] = "https://www.prolocolombardia.it/wp-content/uploads/2026/03/AlboregionaledelleassociazioniProLoco-decreto2420del26.02.2026.pdf",
        ["Veneto"] = "https://www.regione.veneto.it/documents/10813/3100851/019_Albo+PL+al+31_05_2025_dec+n.+195+del+06_06_2025_PL+San+Mauro+di+Saline.pdf/ecc2a70d-22f6-4fb1-ad1d-3f08760fc77c",
        ["Marche"] = "https://static.regione.marche.it/portals/0/Turismo%20Sport%20Tempo%20Libero/Turismo/Accoglienza%20e%20sistema%20turistico/Pro%20Loco/RProLocoProv%20AN%20anno%202023.pdf",
        ["Piemonte"] = "https://prolocopiemonte.it/wp-content/uploads/2020/04/Elenco-APS-del-Piemonte.pdf"
    };

    private readonly HttpClient _httpClient;
    private readonly LoggerService _logger;
    private readonly HtmlParser _htmlParser = new();

    public PdfProLocoImporter(LoggerService logger)
    {
        _logger = logger;
        _httpClient = HttpClientHelper.CreateClient(TimeSpan.FromSeconds(60));
    }

    public IReadOnlyCollection<string> GetSupportedRegions() => SupportedRegions;

    public async Task<List<Ente>> ImportaDaPdfAlboRegionaleAsync(
        string regione,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var regioneCanonica = CanonicalizeRegion(regione);
        if (!SupportedRegions.Contains(regioneCanonica, StringComparer.OrdinalIgnoreCase))
        {
            throw new NotSupportedException(
                $"Regione '{regione}' non supportata per l'import da albo ufficiale. Regioni disponibili: {string.Join(", ", SupportedRegions)}");
        }

        return NormalizeAndDeduplicate(regioneCanonica.Equals("Emilia-Romagna", StringComparison.OrdinalIgnoreCase)
            ? await ImportaDaRegistroEmiliaRomagnaAsync(progress, cancellationToken)
            : await ImportaDaPdfRegionaleAsync(regioneCanonica, progress, cancellationToken));
    }

    private async Task<List<Ente>> ImportaDaPdfRegionaleAsync(string regione, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var pdfUrl = AlbiRegionaliPdf[regione];
        progress?.Report($"Download PDF albo regionale {regione}...");
        await _logger.LogAsync($"Download PDF albo regionale {regione}: {pdfUrl}", cancellationToken);

        var pdfBytes = await DownloadPdfAsync(regione, pdfUrl, cancellationToken);
        progress?.Report($"PDF scaricato ({Math.Max(pdfBytes.Length / 1024, 1)} KB). Estrazione testo...");
        await _logger.LogAsync($"PDF {regione} scaricato: {pdfBytes.Length} byte.", cancellationToken);

        var testoCompleto = EstraiTestoDaPdf(pdfBytes);
        progress?.Report($"Estrazione testo completata ({testoCompleto.Length} caratteri). Parsing Pro Loco...");
        await _logger.LogAsync($"Testo estratto da {regione}: {testoCompleto.Length} caratteri.", cancellationToken);

        var proLoco = ParseProLocoDaTesto(testoCompleto, regione);
        if (proLoco.Count == 0)
        {
            await _logger.LogAsync($"ATTENZIONE: Nessuna Pro Loco estratta dal PDF {regione}.", cancellationToken);
            throw new InvalidOperationException("Il parsing del PDF non ha prodotto risultati. Possibile cambio di formato del documento.");
        }

        progress?.Report($"Trovate {proLoco.Count} Pro Loco nell'albo regionale {regione}.");
        await _logger.LogAsync($"Parsing completato per {regione}: {proLoco.Count} Pro Loco trovate.", cancellationToken);
        return proLoco;
    }

    /// <summary>
    /// Importa Pro Loco dal registro online dell'Emilia-Romagna in modalità best effort.
    /// </summary>
    private async Task<List<Ente>> ImportaDaRegistroEmiliaRomagnaAsync(IProgress<string>? progress, CancellationToken cancellationToken)
    {
        progress?.Report("Accesso registro Emilia-Romagna...");
        await _logger.LogAsync($"Download registro Emilia-Romagna: {EmiliaRomagnaRegistryUrl}", cancellationToken);

        var html = await DownloadTextAsync(EmiliaRomagnaRegistryUrl, cancellationToken);
        var document = await _htmlParser.ParseDocumentAsync(html, cancellationToken);

        var righeTabellari = document.QuerySelectorAll("tr")
            .Select(row => CleanLine(row.TextContent))
            .Where(ContainsProLocoMarker)
            .ToList();

        var proLoco = righeTabellari
            .Select(line => CreateEntityFromCandidate(line, "Emilia-Romagna"))
            .Where(static entity => entity is not null)
            .Cast<Ente>()
            .ToList();

        if (proLoco.Count == 0)
        {
            var testoCompleto = CleanMultilineText(document.Body?.TextContent ?? html);
            proLoco = ParseProLocoDaTesto(testoCompleto, "Emilia-Romagna");
        }

        if (proLoco.Count == 0)
        {
            await _logger.LogAsync("ATTENZIONE: nessuna Pro Loco individuata nel registro Emilia-Romagna.", cancellationToken);
            throw new InvalidOperationException("Il registro Emilia-Romagna non ha restituito risultati Pro Loco leggibili.");
        }

        progress?.Report($"Registro Emilia-Romagna letto: {proLoco.Count} Pro Loco trovate.");
        await _logger.LogAsync($"Parsing completato per Emilia-Romagna: {proLoco.Count} Pro Loco trovate.", cancellationToken);
        return proLoco;
    }

    private async Task<byte[]> DownloadPdfAsync(string regione, string url, CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(FileHelper.DataRoot, "Temp", "PdfAlbi");
        Directory.CreateDirectory(cacheDirectory);

        var cachePath = Path.Combine(cacheDirectory, $"{NormalizeKey(regione)}.pdf");
        if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
        {
            await _logger.LogAsync($"Uso cache PDF per {regione}: {cachePath}", cancellationToken);
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);
        }

        try
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var pdfBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            await File.WriteAllBytesAsync(cachePath, pdfBytes, cancellationToken);
            return pdfBytes;
        }
        catch (HttpRequestException ex)
        {
            await _logger.LogAsync($"ERRORE download PDF {regione}: {ex.Message}", cancellationToken);
            throw new InvalidOperationException(
                $"PDF albo regionale non disponibile per {regione}. Verificare connessione o disponibilità del file.",
                ex);
        }
        catch (TaskCanceledException ex)
        {
            await _logger.LogAsync($"ERRORE timeout download PDF {regione}: {ex.Message}", cancellationToken);
            throw new InvalidOperationException($"Timeout durante il download del PDF per {regione}.", ex);
        }
    }

    private async Task<string> DownloadTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static string EstraiTestoDaPdf(byte[] pdfBytes)
    {
        try
        {
            using var stream = new MemoryStream(pdfBytes);
            using var reader = new PdfReader(stream);
            using var document = new PdfDocument(reader);
            var builder = new StringBuilder();

            for (var pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
            {
                var page = document.GetPage(pageNumber);
                var strategy = new LocationTextExtractionStrategy();
                builder.AppendLine(PdfTextExtractor.GetTextFromPage(page, strategy));
            }

            return builder.ToString();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Il PDF scaricato non è valido o non è leggibile.", ex);
        }
    }

    private static List<Ente> ParseProLocoDaTesto(string testo, string regione)
    {
        var risultati = new List<Ente>();
        Ente? enteCorrente = null;

        foreach (var rawLine in testo.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = CleanLine(rawLine);
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var nuovaProLoco = CreateEntityFromCandidate(line, regione);
            if (nuovaProLoco is not null)
            {
                AddIfValid(risultati, enteCorrente);
                enteCorrente = nuovaProLoco;
                continue;
            }

            if (enteCorrente is not null)
            {
                EnrichEntity(enteCorrente, line);
            }
        }

        AddIfValid(risultati, enteCorrente);
        return risultati;
    }

    private static Ente? CreateEntityFromCandidate(string line, string regione)
    {
        if (!ContainsProLocoMarker(line))
        {
            return null;
        }

        var denominazione = ExtractDenominazione(line);
        if (string.IsNullOrWhiteSpace(denominazione) || IsNonEntityLine(denominazione))
        {
            return null;
        }

        var ente = new Ente
        {
            Regione = regione,
            Categoria = "Pro Loco",
            Stato = StatoEnte.DA_ELABORARE,
            DataUltimoControllo = DateTime.Now,
            Denominazione = denominazione
        };

        EnrichEntity(ente, line);
        if (string.IsNullOrWhiteSpace(ente.Comune))
        {
            ente.Comune = TryInferComuneFromDenominazione(denominazione);
        }

        return ente;
    }

    private static void EnrichEntity(Ente ente, string line)
    {
        if (string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            var codiceFiscale = CodiceFiscaleRegex.Match(line);
            if (codiceFiscale.Success)
            {
                ente.CodiceFiscale = codiceFiscale.Value.ToUpperInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(ente.Provincia))
        {
            var provincia = ProvinciaRegex.Match(line);
            if (provincia.Success)
            {
                ente.Provincia = (provincia.Groups[1].Success ? provincia.Groups[1].Value : provincia.Groups[2].Value).Trim().ToUpperInvariant();
            }
        }

        if (string.IsNullOrWhiteSpace(ente.Comune))
        {
            var comune = ComuneLabelRegex.Match(line);
            if (comune.Success)
            {
                ente.Comune = CleanFieldValue(comune.Groups[1].Value);
            }
        }
    }

    private static void AddIfValid(List<Ente> enti, Ente? ente)
    {
        if (ente is null || string.IsNullOrWhiteSpace(ente.Denominazione))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(ente.Comune))
        {
            ente.Comune = TryInferComuneFromDenominazione(ente.Denominazione);
        }

        enti.Add(ente);
    }

    private static List<Ente> NormalizeAndDeduplicate(List<Ente> enti)
    {
        return enti
            .Where(ente => !string.IsNullOrWhiteSpace(ente.Denominazione))
            .Select(ente =>
            {
                ente.Regione = CanonicalizeRegion(ente.Regione);
                ente.Categoria = "Pro Loco";
                ente.Stato = StatoEnte.DA_ELABORARE;
                ente.Comune = CleanFieldValue(ente.Comune);
                ente.Provincia = CleanFieldValue(ente.Provincia).ToUpperInvariant();
                ente.Denominazione = CleanFieldValue(ente.Denominazione);
                return ente;
            })
            .GroupBy(BuildEntityKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(ente => ente.Regione)
            .ThenBy(ente => ente.Provincia)
            .ThenBy(ente => ente.Comune)
            .ThenBy(ente => ente.Denominazione)
            .ToList();
    }

    private static bool ContainsProLocoMarker(string line) => ProLocoRegex.IsMatch(line);

    private static string ExtractDenominazione(string line)
    {
        var normalizedLine = CleanLine(line);
        var match = Regex.Match(normalizedLine, @"\bPRO\s*LOCO\b", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return string.Empty;
        }

        var candidate = normalizedLine[match.Index..];
        candidate = Regex.Replace(candidate, @"\b(?:C\.?F\.?|CODICE\s+FISCALE)\b.*$", string.Empty, RegexOptions.IgnoreCase);
        candidate = Regex.Replace(candidate, @"\b(?:COMUNE|SEDE(?:\s+LEGALE)?|LOCALIT[ÀA])\b.*$", string.Empty, RegexOptions.IgnoreCase);
        candidate = candidate.Trim(' ', '-', '.', ',', ';', ':');
        return CleanFieldValue(candidate);
    }

    private static string TryInferComuneFromDenominazione(string denominazione)
    {
        if (string.IsNullOrWhiteSpace(denominazione))
        {
            return string.Empty;
        }

        var match = Regex.Match(
            denominazione,
            @"\bPRO\s*LOCO(?:\s+(?:DI|DEL|DELLA|DELL'|DELLE|DEI))?\s+(.+)$",
            RegexOptions.IgnoreCase);

        return match.Success ? CleanFieldValue(match.Groups[1].Value) : string.Empty;
    }

    private static string CleanLine(string line)
    {
        return MultipleSpacesRegex.Replace(line.Replace('\u00A0', ' '), " ").Trim();
    }

    private static string CleanMultilineText(string content)
    {
        var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }

    private static string CleanFieldValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim(' ', '-', '.', ',', ';', ':');
        cleaned = MultipleSpacesRegex.Replace(cleaned, " ").Trim();
        return cleaned;
    }

    private static bool IsNonEntityLine(string line)
    {
        var upperLine = NormalizeKey(line);
        return upperLine.Contains("ALBO")
            || upperLine.Contains("REGISTRO")
            || upperLine.Contains("DECRETO")
            || upperLine.Contains("PAGINA")
            || upperLine.Contains("ELENCO");
    }

    private static string BuildEntityKey(Ente ente)
    {
        if (!string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            return $"CF:{ente.CodiceFiscale.Trim().ToUpperInvariant()}";
        }

        return $"ALT:{NormalizeKey(ente.Regione)}|{NormalizeKey(ente.Provincia)}|{NormalizeKey(ente.Comune)}|{NormalizeKey(ente.Denominazione)}";
    }

    private static string CanonicalizeRegion(string regione)
    {
        var normalized = NormalizeKey(regione);
        return SupportedRegions.FirstOrDefault(region => NormalizeKey(region).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            ?? regione.Trim();
    }

    private static string NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Normalize(NormalizationForm.FormD)
            .Where(character => char.IsLetterOrDigit(character))
            .Select(char.ToUpperInvariant)
            .ToArray());
    }
}
