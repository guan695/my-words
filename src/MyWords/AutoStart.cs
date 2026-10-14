using System;
using System.IO;
using Microsoft.Win32;

namespace MyWords;

internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "MyWords";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return IsEnabled(key);
    }

    internal static bool IsEnabled(RegistryKey? key) => key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);

    public static void SetEnabled(bool enabled)
    {
        using var key = enabled ? Registry.CurrentUser.CreateSubKey(RunKey) : Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (key == null)
        {
            if (enabled) throw new InvalidOperationException("无法创建开机启动项，请检查当前 Windows 账户的权限。");
            return;
        }
        SetEnabled(key, enabled, Path.Combine(AppContext.BaseDirectory, "MyWords.exe"));
    }

    internal static void SetEnabled(RegistryKey key, bool enabled, string executable)
    {
        if (!enabled) { key.DeleteValue(ValueName, false); return; }
        if (!Path.IsPathFullyQualified(executable) || !File.Exists(executable))
            throw new InvalidOperationException("找不到拾词程序，请从完整的程序文件夹中启动 MyWords.exe。");
        var command = $"\"{executable}\" --background";
        if (command.Length > 260) throw new InvalidOperationException("程序路径过长，无法设置开机启动。请把程序放到较短的路径后重试。");
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}
