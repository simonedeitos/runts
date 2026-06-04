using CsvHelper;
using CsvHelper.Configuration;
using runts.Helpers;
using runts.Models;
using System.Globalization;

namespace runts.Services;

/// <summary>
/// Gestisce lettura/scrittura del file Enti.csv in modo thread-safe.
/// </summary>
public sealed class CsvManager
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<List<Ente>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var stream = File.Open(FileHelper.EntiFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var csv = new CsvReader(reader, BuildConfig());
            return csv.GetRecords<Ente>().ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertManyAsync(IEnumerable<Ente> enti, CancellationToken cancellationToken = default)
    {
        var incoming = enti
            .Where(x => !string.IsNullOrWhiteSpace(x.CodiceFiscale))
            .GroupBy(x => x.CodiceFiscale.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await InternalLoadAsync(cancellationToken);
            var byCf = all.ToDictionary(x => x.CodiceFiscale, StringComparer.OrdinalIgnoreCase);

            foreach (var ente in incoming)
            {
                byCf[ente.CodiceFiscale] = ente;
            }

            await InternalSaveAsync(byCf.Values.OrderBy(x => x.Regione).ThenBy(x => x.Denominazione), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Ente entity, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await InternalLoadAsync(cancellationToken);
            var index = all.FindIndex(x => x.CodiceFiscale.Equals(entity.CodiceFiscale, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                all[index] = entity;
                await InternalSaveAsync(all, cancellationToken);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<Ente>> InternalLoadAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.Open(FileHelper.EntiFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, BuildConfig());
        return csv.GetRecords<Ente>().ToList();
    }

    private static CsvConfiguration BuildConfig() => new(CultureInfo.InvariantCulture)
    {
        Delimiter = ";",
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null
    };

    private static async Task InternalSaveAsync(IEnumerable<Ente> enti, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(FileHelper.EntiFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, BuildConfig());

        await csv.WriteRecordsAsync(enti, cancellationToken);
    }
}
