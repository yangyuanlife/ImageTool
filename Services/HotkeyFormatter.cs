using System.Collections.Generic;
using System.Windows.Input;

namespace ImageTool.Services;

/// <summary>
/// 把「修饰符 + 主键」整型值格式化成人类可读字符串，例如 "Ctrl + Shift + S"。
/// 设置页的录制显示与主窗口底部快捷键提示共用，避免两处硬编码不一致。
/// </summary>
public static class HotkeyFormatter
{
    public static string Format(int modifiers, int key)
    {
        var parts = new List<string>();
        var m = (ModifierKeys)modifiers;
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(((Key)key).ToString());
        return string.Join(" + ", parts);
    }
}
