using CsvHelper;
using CsvHelper.Configuration;
using EasySearch.Models;
using System.Globalization;

namespace EasySearch.Helpers;

public static class FileHelper
{
    public static string DataRoot => Path.Combine(AppContext.BaseDirectory, "Data");
    public static string EntiFilePath => Path.Combine(DataRoot, "Enti.csv");

    public static void EnsureDataDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Path.Combine(DataRoot, "Import"));
        Directory.CreateDirectory(Path.Combine(DataRoot, "Export"));
        Directory.CreateDirectory(Path.Combine(DataRoot, "Logs"));
        Directory.CreateDirectory(Path.Combine(DataRoot, "Temp"));

        if (File.Exists(EntiFilePath))
        {
            return;
        }

        using var stream = File.CreateText(EntiFilePath);
        using var csv = new CsvWriter(stream, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = ";"
        });

        csv.WriteHeader<Ente>();
        csv.NextRecord();
    }
}
