using Microsoft.Win32;

namespace EasySearch.Helpers;

/// <summary>
/// Gestisce impostazioni applicazione in Windows Registry.
/// Path: HKEY_CURRENT_USER\Software\EasySearch
/// </summary>
public static class RegistrySettingsManager
{
    private const string RegistryPath = @"Software\EasySearch";
    private const string BrightDataApiKeyName = "BrightDataApiKey";
    private const string BrightDataHostName = "BrightDataHost";
    private const string BrightDataPortName = "BrightDataPort";
    private const string ComuniCsvPathName = "ComuniCsvPath";
    private const string DefaultBrightDataHost = "brd.superproxy.io";
    private const int DefaultBrightDataPort = 22225;

    public static void SaveBrightDataApiKey(string apiKey)
    {
        SaveSetting(BrightDataApiKeyName, apiKey);
    }

    public static string? GetBrightDataApiKey()
    {
        return GetSetting(BrightDataApiKeyName);
    }

    public static void SaveBrightDataHost(string host)
    {
        SaveSetting(BrightDataHostName, string.IsNullOrWhiteSpace(host) ? DefaultBrightDataHost : host);
    }

    public static string GetBrightDataHost()
    {
        var value = GetSetting(BrightDataHostName);
        return string.IsNullOrWhiteSpace(value) ? DefaultBrightDataHost : value;
    }

    public static void SaveBrightDataPort(int port)
    {
        using var regKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
        regKey?.SetValue(BrightDataPortName, port, RegistryValueKind.DWord);
    }

    public static int GetBrightDataPort()
    {
        using var regKey = Registry.CurrentUser.OpenSubKey(RegistryPath);
        var value = regKey?.GetValue(BrightDataPortName);
        return value is int port ? port : DefaultBrightDataPort;
    }

    public static void SaveComuniCsvPath(string path)
    {
        SaveSetting(ComuniCsvPathName, path);
    }

    public static string? GetComuniCsvPath()
    {
        return GetSetting(ComuniCsvPathName);
    }

    public static bool IsBrightDataConfigured()
    {
        var apiKey = GetBrightDataApiKey();
        var host = GetBrightDataHost();
        var port = GetBrightDataPort();
        return !string.IsNullOrWhiteSpace(apiKey)
               && apiKey.Contains(':')
               && !string.IsNullOrWhiteSpace(host)
               && port > 0;
    }

    public static void SaveSetting(string key, string value)
    {
        using var regKey = Registry.CurrentUser.CreateSubKey(RegistryPath);
        regKey?.SetValue(key, value ?? string.Empty, RegistryValueKind.String);
    }

    public static string? GetSetting(string key)
    {
        using var regKey = Registry.CurrentUser.OpenSubKey(RegistryPath);
        return regKey?.GetValue(key) as string;
    }
}
