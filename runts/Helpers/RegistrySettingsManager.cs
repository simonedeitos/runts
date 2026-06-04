using Microsoft.Win32;

namespace runts.Helpers;

/// <summary>
/// Gestisce impostazioni applicazione in Windows Registry.
/// Path: HKEY_CURRENT_USER\Software\RuntsContactFinder
/// </summary>
public static class RegistrySettingsManager
{
    private const string RegistryPath = @"Software\RuntsContactFinder";

    public static void SaveBrightDataApiKey(string apiKey)
    {
        SaveSetting("BrightDataApiKey", apiKey);
    }

    public static string? GetBrightDataApiKey()
    {
        return GetSetting("BrightDataApiKey");
    }

    public static bool IsBrightDataConfigured()
    {
        var apiKey = GetBrightDataApiKey();
        return !string.IsNullOrWhiteSpace(apiKey);
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
