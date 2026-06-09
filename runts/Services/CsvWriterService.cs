using System.Text;
using EasySearch.Models;

namespace EasySearch.Services;

/// <summary>
/// Scrive risultati CSV in modo progressivo con flush immediato.
/// </summary>
public sealed class CsvWriterService : IAsyncDisposable, IDisposable
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public CsvWriterService(string filePath)
    {
        _writer = new StreamWriter(filePath, append: false, Encoding.UTF8);
        _writer.WriteLine("Regione,Provincia,Comune,Denominazione,Codice Fiscale,Categoria,Sito Web,Email,PEC,Telefono,Indirizzo,Data Elaborazione");
        _writer.Flush();
    }

    public async Task WriteRowAsync(Ente ente, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var line = string.Join(',',
            EscapeCsv(ente.Regione),
            EscapeCsv(ente.Provincia),
            EscapeCsv(ente.Comune),
            EscapeCsv(ente.Denominazione),
            EscapeCsv(ente.CodiceFiscale),
            EscapeCsv(ente.Categoria),
            EscapeCsv(ente.SitoWeb),
            EscapeCsv(ente.Email),
            EscapeCsv(ente.PEC),
            EscapeCsv(ente.Telefono),
            EscapeCsv(ente.Indirizzo),
            EscapeCsv(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _writer.FlushAsync();
        _writer.Dispose();
        _writeLock.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.Flush();
        _writer.Dispose();
        _writeLock.Dispose();
    }
}
