using CsvHelper;
using CsvHelper.Configuration;
using runts.Helpers;
using runts.Models;
using System.Globalization;

namespace runts.Services;

public sealed class RuntsImporter
{
    private readonly CsvManager _csvManager;

    public RuntsImporter(CsvManager csvManager)
    {
        _csvManager = csvManager;
    }

    public async Task<int> ImportRegioneAsync(string regione, CancellationToken cancellationToken = default)
    {
        var importPath = Path.Combine(FileHelper.DataRoot, "Import", $"{regione}.csv");
        var enti = File.Exists(importPath)
            ? await ReadFromSourceAsync(importPath, cancellationToken)
            : BuildDemoData(regione);

        await _csvManager.UpsertManyAsync(enti, cancellationToken);
        return enti.Count;
    }

    private static async Task<List<Ente>> ReadFromSourceAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";",
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null
        });

        var result = csv.GetRecords<Ente>()
            .Where(x => !string.IsNullOrWhiteSpace(x.CodiceFiscale))
            .ToList();

        await Task.CompletedTask;
        return result;
    }

    private static List<Ente> BuildDemoData(string regione)
    {
        return
        [
            new Ente
            {
                Regione = regione,
                Provincia = "NA",
                Comune = "Napoli",
                Denominazione = $"{regione} ETS Demo",
                CodiceFiscale = $"{regione[..Math.Min(regione.Length, 4)].ToUpperInvariant()}0000000001",
                Categoria = "ETS",
                Stato = StatoEnte.DA_ELABORARE
            },
            new Ente
            {
                Regione = regione,
                Provincia = "RM",
                Comune = "Roma",
                Denominazione = $"Pro Loco {regione}",
                CodiceFiscale = $"{regione[..Math.Min(regione.Length, 4)].ToUpperInvariant()}0000000002",
                Categoria = "PRO_LOCO",
                Stato = StatoEnte.DA_ELABORARE
            }
        ];
    }
}
