using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using runts.Helpers;
using runts.Models;
using System.Globalization;

namespace runts.Services;

public sealed class ExportService
{
    private readonly CsvManager _csvManager;

    public ExportService(CsvManager csvManager)
    {
        _csvManager = csvManager;
    }

    public async Task<string> ExportRegionCsvAsync(string regione, CancellationToken cancellationToken = default)
    {
        var enti = await FilterByRegionAsync(regione, cancellationToken);
        var path = Path.Combine(FileHelper.DataRoot, "Export", $"{regione}.csv");

        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });
        await csv.WriteRecordsAsync(enti, cancellationToken);
        return path;
    }

    public async Task<string> ExportRegionExcelAsync(string regione, CancellationToken cancellationToken = default)
    {
        var enti = await FilterByRegionAsync(regione, cancellationToken);
        var path = Path.Combine(FileHelper.DataRoot, "Export", $"{regione}.xlsx");

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet(regione);
        ws.Cell(1, 1).InsertTable(enti);
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);

        await Task.CompletedTask;
        return path;
    }

    private async Task<List<Ente>> FilterByRegionAsync(string regione, CancellationToken cancellationToken)
    {
        var all = await _csvManager.LoadAsync(cancellationToken);
        return all.Where(x => x.Regione.Equals(regione, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}
