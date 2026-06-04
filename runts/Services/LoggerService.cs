using runts.Helpers;
using System.Text;

namespace runts.Services;

public sealed class LoggerService
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task LogAsync(string message, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(FileHelper.DataRoot, "Logs", $"{DateTime.Now:yyyyMMdd}.log");
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, line, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}
