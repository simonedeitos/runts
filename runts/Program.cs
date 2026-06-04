using Microsoft.Extensions.DependencyInjection;
using runts.Forms;
using runts.Helpers;
using runts.Services;

namespace runts;

/// <summary>
/// Entry point applicativo con bootstrap DI e inizializzazione cartelle dati CSV.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        FileHelper.EnsureDataDirectories();

        var services = new ServiceCollection();
        ConfigureServices(services);

        using var serviceProvider = services.BuildServiceProvider();
        var mainForm = serviceProvider.GetRequiredService<MainForm>();
        Application.Run(mainForm);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(HttpClientHelper.CreateDefaultClient);
        services.AddSingleton<LoggerService>();
        services.AddSingleton<CsvManager>();
        services.AddSingleton<RuntsImporter>();
        services.AddSingleton<SearchEngineService>();
        services.AddSingleton<WebScraperService>();
        services.AddSingleton<ContactFinderService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<MainForm>();
    }
}
