using System.Reflection;
using System.Windows;

namespace ImageTool.Helpers;

/// <summary>
/// 全局版本号读取：所有界面/托盘展示的版本都从这里取，
/// 数据源是程序集本身的版本（由 csproj 的 &lt;Version&gt; 统一维护），绝不硬编码。
/// </summary>
public static class VersionHelper
{
    /// <summary>
    /// 返回形如 "1.0.1" 的版本字符串（取 Major.Minor.Build，去掉末尾的 .0 修订号）。
    /// 读取失败时回退为 "1.0.0"，保证界面不崩。
    /// </summary>
    public static string Version
    {
        get
        {
            var asm = System.Windows.Application.ResourceAssembly ?? Assembly.GetEntryAssembly();
            var v = asm?.GetName().Version;
            if (v == null) return "1.0.0";
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>带 "v" 前缀的版本，如 "v1.0.1"，用于托盘菜单/标题等场景。</summary>
    public static string VersionWithPrefix => $"v{Version}";
}
