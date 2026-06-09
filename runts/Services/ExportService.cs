using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using EasySearch.Models;
using System.Globalization;

namespace EasySearch.Services;

public sealed class ExportService
{
    public async Task ExportCsvAsync(IEnumerable<Ente> enti, string path, CancellationToken cancellationToken = default)
    {
        var rows = enti.ToList();
        await using var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = ";" });
        await csv.WriteRecordsAsync(rows, cancellationToken);
    }

    public Task ExportExcelAsync(IEnumerable<Ente> enti, string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("EasySearch");
        ws.Cell(1, 1).InsertTable(enti.ToList());
        ws.Columns().AdjustToContents();
        wb.SaveAs(path);
        return Task.CompletedTask;
    }
}
