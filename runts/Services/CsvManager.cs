using CsvHelper;
using CsvHelper.Configuration;
using EasySearch.Helpers;
using EasySearch.Models;
using System.Globalization;

namespace EasySearch.Services;

/// <summary>
/// Gestisce lettura/scrittura del file Enti.csv in modo thread-safe.
/// </summary>
public sealed class CsvManager
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string> CreateBackupAsync(string reason, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var backupDirectory = Path.Combine(FileHelper.DataRoot, "Temp", "Backup");
            Directory.CreateDirectory(backupDirectory);
            var safeReason = string.Concat(reason.Where(char.IsLetterOrDigit));
            var backupPath = Path.Combine(backupDirectory, $"Enti_{DateTime.Now:yyyyMMdd_HHmmss}_{safeReason}.csv");
            File.Copy(FileHelper.EntiFilePath, backupPath, overwrite: true);
            return backupPath;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<Ente>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            return await InternalLoadAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ReplaceAllAsync(IEnumerable<Ente> enti, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await InternalSaveAsync(
                enti.GroupBy(BuildUniqueKey, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                    .OrderBy(x => x.Regione)
                    .ThenBy(x => x.Comune)
                    .ThenBy(x => x.Denominazione),
                cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertManyAsync(IEnumerable<Ente> enti, CancellationToken cancellationToken = default)
    {
        var incoming = enti
            .GroupBy(BuildUniqueKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var all = await InternalLoadAsync(cancellationToken);
            var byKey = all.ToDictionary(BuildUniqueKey, StringComparer.OrdinalIgnoreCase);

            foreach (var ente in incoming)
            {
                byKey[BuildUniqueKey(ente)] = ente;
            }

            await InternalSaveAsync(byKey.Values.OrderBy(x => x.Regione).ThenBy(x => x.Comune).ThenBy(x => x.Denominazione), cancellationToken);
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
            var key = BuildUniqueKey(entity);
            var index = all.FindIndex(x => BuildUniqueKey(x).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                all[index] = entity;
            }
            else
            {
                all.Add(entity);
            }

            await InternalSaveAsync(all.OrderBy(x => x.Regione).ThenBy(x => x.Comune).ThenBy(x => x.Denominazione), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task<List<Ente>> InternalLoadAsync(CancellationToken cancellationToken)
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

    private static string BuildUniqueKey(Ente ente)
    {
        if (!string.IsNullOrWhiteSpace(ente.CodiceFiscale))
        {
            return $"CF:{ente.CodiceFiscale.Trim().ToUpperInvariant()}";
        }

        return string.Join('|',
            "ALT",
            NormalizeKeyPart(ente.Regione),
            NormalizeKeyPart(ente.Comune),
            NormalizeKeyPart(ente.Categoria),
            NormalizeKeyPart(ente.Denominazione),
            NormalizeKeyPart(ente.SitoWeb));
    }

    private static string NormalizeKeyPart(string? value) => (value ?? string.Empty).Trim().ToUpperInvariant();

    private static async Task InternalSaveAsync(IEnumerable<Ente> enti, CancellationToken cancellationToken)
    {
        await using var stream = File.Open(FileHelper.EntiFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(stream);
        await using var csv = new CsvWriter(writer, BuildConfig());

        await csv.WriteRecordsAsync(enti, cancellationToken);
    }
}
