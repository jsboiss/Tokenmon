using Microsoft.Win32;

namespace Tokenmon.App;

public static class StartupService
{
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        if (enabled)
        {
            key?.SetValue("Tokenmon", $"\"{Environment.ProcessPath}\" --startup");
        }
        else
        {
            key?.DeleteValue("Tokenmon", throwOnMissingValue: false);
        }
    }
}
