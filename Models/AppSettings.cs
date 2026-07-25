using System.Windows.Input;

namespace ImageTool.Models;

/// <summary>
/// 应用设置（持久化到 %AppData%/ImageTool/settings.json）
/// </summary>
public class AppSettings
{
    /// <summary>是否开机自启</summary>
    public bool AutoStart { get; set; }

    /// <summary>全局热键修饰符（System.Windows.Input.ModifierKeys 的整型值，与 Win32 MOD_* 一致）</summary>
    public int HotkeyModifiers { get; set; } = (int)(ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>全局热键主键（System.Windows.Input.Key 的整型值）</summary>
    public int HotkeyKey { get; set; } = (int)Key.S;

    /// <summary>截图默认保存目录，空字符串表示系统图片库</summary>
    public string DefaultSaveDir { get; set; } = "";
}
