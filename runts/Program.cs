using Microsoft.Extensions.DependencyInjection;
using EasySearch.Forms;
using EasySearch.Helpers;
using EasySearch.Services;

namespace EasySearch;

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
        services.AddSingleton<SearchEngineService>();
        services.AddSingleton<IstatComuniImporter>();
        services.AddSingleton<WebScraperService>();
        services.AddSingleton<ContactFinderService>();
        services.AddSingleton<ExportService>();
        services.AddSingleton<MainForm>();
    }
}
