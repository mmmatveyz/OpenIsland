using Microsoft.Win32;

namespace OpenIsland;

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "OpenIsland";
    const string LegacyName = "DynamicIsland";

    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            string? exe = Environment.ProcessPath;
            if (exe == null) return false;
            if (key?.GetValue(Name) is string value && value.Contains(exe, StringComparison.OrdinalIgnoreCase)) return true;

            // Переносим прежнюю запись автозапуска со старым именем продукта.
            if (key?.GetValue(LegacyName) is string legacy && legacy.Contains(exe, StringComparison.OrdinalIgnoreCase))
            {
                Set(true);
                return true;
            }
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.DeleteValue(LegacyName, false);
        if (enabled && Environment.ProcessPath is { } exe) key.SetValue(Name, $"\"{exe}\"");
        else key.DeleteValue(Name, false);
    }
}
