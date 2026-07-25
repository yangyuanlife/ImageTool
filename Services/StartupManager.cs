using System.IO;
using Microsoft.Win32;

namespace ImageTool.Services;

/// <summary>
/// 开机自启管理：通过 HKCU\Software\Microsoft\Windows\CurrentVersion\Run 写入当前用户启动项。
/// 使用 HKCU 不需要管理员权限。
/// </summary>
public static class StartupManager
{
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "ImageTool";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey);
        var val = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(val);
    }

    public static void Enable()
    {
        var cmd = GetRunCommand();
        using var key = Registry.CurrentUser.CreateSubKey(SubKey);
        key.SetValue(AppName, cmd);
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, true);
        key?.DeleteValue(AppName, false);
    }

    /// <summary>
    /// 构造开机启动命令行。自包含发布时为 exe 路径；dotnet run 调试时为 "dotnet" "<dll>"。
    /// </summary>
    private static string GetRunCommand()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var exe = process.MainModule?.FileName ?? "";

        if (exe.EndsWith("imagetool.exe", StringComparison.OrdinalIgnoreCase))
            return $"\"{exe}\"";

        // 单文件发布时 Assembly.Location 返回空字符串（IL3000），改用 AppContext.BaseDirectory 拼入口 dll 路径
        var entryName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name ?? "ImageTool";
        var dll = Path.Combine(AppContext.BaseDirectory, entryName + ".dll");
        if (dll.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dotnet = Path.Combine(Path.GetDirectoryName(exe) ?? "", "dotnet.exe");
            if (!File.Exists(dotnet)) dotnet = "dotnet.exe";
            return $"\"{dotnet}\" \"{dll}\"";
        }

        return $"\"{exe}\"";
    }
}
