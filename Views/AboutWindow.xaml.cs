using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using ImageTool.Helpers;

namespace ImageTool.Views;

/// <summary>
/// 「关于我们」窗口：展示版本、简介、开源仓库与联系方式。
/// 版本号来自 VersionHelper（程序集版本），不硬编码。
/// </summary>
public partial class AboutWindow : Window
{
    public string Copyright => $"Copyright © 2026 yangyuanlife · 基于 MIT 许可证开源 · v{VersionHelper.Version}";

    public AboutWindow()
    {
        InitializeComponent();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            // 用系统默认程序打开外部链接（浏览器 / 邮件客户端）
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打开失败（无默认程序等）时静默忽略，不影响窗口
        }
        e.Handled = true;
    }
}
