using System.Drawing;
using System.Windows.Forms;

namespace ImageTool.Services;

/// <summary>
/// 系统托盘服务：常驻 NotifyIcon + 右键菜单。关闭主窗口不会退出，仅托盘保持。
/// </summary>
public class TrayService : IDisposable
{
    private NotifyIcon? _notify;

    public event Action? ShowMainWindow;
    public event Action? StartScreenshot;
    public event Action? OpenSettings;
    public event Action? ExitRequested;

    public void Initialize()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        var icon = (exePath != null ? Icon.ExtractAssociatedIcon(exePath) : null) ?? SystemIcons.Application;

        _notify = new NotifyIcon
        {
            Icon = icon,
            Text = "图片处理工具",
            Visible = true
        };

        _notify.DoubleClick += (_, _) => ShowMainWindow?.Invoke();

        var menu = new ContextMenuStrip();
        var open = new ToolStripMenuItem("打开主界面");
        open.Click += (_, _) => ShowMainWindow?.Invoke();
        var shot = new ToolStripMenuItem("截图");
        shot.Click += (_, _) => StartScreenshot?.Invoke();
        var settings = new ToolStripMenuItem("设置");
        settings.Click += (_, _) => OpenSettings?.Invoke();
        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        menu.Items.Add(open);
        menu.Items.Add(shot);
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        _notify.ContextMenuStrip = menu;
    }

    public void Dispose()
    {
        if (_notify != null)
        {
            _notify.Visible = false;
            _notify.Dispose();
            _notify = null;
        }
    }
}
