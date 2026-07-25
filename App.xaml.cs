using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ImageTool.Services;
using ImageTool.Views;

namespace ImageTool;

/// <summary>
/// 应用入口：启动后常驻系统托盘，主窗口默认隐藏；全局热键/托盘菜单唤起截图。
/// 关闭主窗口仅隐藏界面，托盘不退出；只有托盘「退出」才真正关闭程序。
/// </summary>
public partial class App : System.Windows.Application
{
    private TrayService? _tray;
    private HotkeyService? _hotkey;
    private ScreenshotService? _screenshot;

    /// <summary>当前生效的全局截图热键提示（如 "Ctrl + Shift + S"），供主窗口底部展示</summary>
    public static string CurrentHotkeyHint { get; private set; } = "";

    /// <summary>热键被重新注册（设置页变更）时触发，主窗口据此刷新底部提示</summary>
    public static event Action? HotkeyChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _screenshot = new ScreenshotService();

        // 创建主窗口但不显示 —— 默认进入托盘
        MainWindow = new MainWindow();

        _tray = new TrayService();
        _tray.Initialize();
        _tray.ShowMainWindow += ShowMainWindow;
        _tray.StartScreenshot += StartScreenshot;
        _tray.OpenSettings += OpenSettings;
        _tray.AboutRequested += ShowAbout;
        _tray.ExitRequested += () => Shutdown();

        _hotkey = new HotkeyService();
        _hotkey.HotkeyPressed += StartScreenshot;
        try { _hotkey.Register(); }
        catch (Exception ex)
        {
            // 不再静默吞掉：注册失败（多为被占用）时明确告知，否则用户只会觉得“热键没反应”
            System.Windows.MessageBox.Show(
                $"全局截图热键注册失败：{ex.Message}\n\n截图功能仍可用，可通过托盘菜单「截图」或主界面「截图」按钮唤起。\n如需更换热键，请在「设置」页重新录制。",
                "ImageTool", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        // 初始化底部快捷键提示（读取当前已保存/默认热键）
        var s = SettingsStore.Load();
        CurrentHotkeyHint = HotkeyFormatter.Format(s.HotkeyModifiers, s.HotkeyKey);

        // 让自启设置与注册表启动项保持一致
        SyncAutoStart();
    }

    private void ShowMainWindow()
    {
        if (MainWindow == null) return;
        MainWindow.Show();
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void OpenSettings()
    {
        ShowMainWindow();
        if (MainWindow is MainWindow mw)
            mw.SelectSettingsTab();
    }

    private void ShowAbout()
    {
        var about = new AboutWindow();
        // 仅当主窗口已显示时才设为 Owner：托盘模式下主窗口默认隐藏，给「未显示」的窗口
        // 设 Owner 会抛 InvalidOperationException（无法将 Owner 设置为之前未显示的 Window）。
        if (MainWindow is { IsVisible: true })
            about.Owner = MainWindow;
        about.ShowDialog();
    }

    /// <summary>唤起截图：先把可见的主窗口移出虚拟屏幕并隐藏，等 DWM 真正重绘后再捕获，避免半透明主窗口残影；编辑器关闭后恢复主窗口。</summary>
    internal void StartScreenshot()
    {
        var wasVisible = MainWindow is { IsVisible: true };
        double savedLeft = 0, savedTop = 0;
        var savedState = WindowState.Normal;

        if (wasVisible)
        {
            savedLeft = MainWindow.Left;
            savedTop = MainWindow.Top;
            savedState = MainWindow.WindowState;
            // 最大化时 Left/Top 不生效，先恢复正常窗口再移动
            if (savedState == WindowState.Maximized)
                MainWindow.WindowState = WindowState.Normal;

            // 1) 移到虚拟屏幕之外：即便 DWM 还没重绘、捕获读到的是上一帧，窗口也已不在捕获矩形内（几何排除）
            var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
            MainWindow.Left = vs.Right + 1000;
            MainWindow.Top = vs.Bottom + 1000;
            // 强制消息泵处理这一次位置变更，确保 Win32 窗口真的被挪走（避免 SetWindowPos 消息排队延迟）
            MainWindow.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
            // 2) 隐藏，彻底从桌面移除
            MainWindow.Hide();
            // 3) 关键：等 DWM 把「不含主窗口」的新帧真正呈现到屏幕，再捕获（消除半透明残影）。
            //    不能用固定 Task.Delay 赌时序——DwmFlush 会阻塞到下一次 DWM 呈现，确定性保证窗口已消失。
            DwmFlush();
        }

        var bmp = _screenshot!.CaptureFullScreen();
        var editor = new ScreenshotEditorWindow(bmp);
        // 编辑器关闭后恢复主窗口（仅当本次截图前主窗口可见）
        if (wasVisible)
        {
            editor.Closed += (_, _) =>
            {
                if (MainWindow is { IsVisible: false })
                {
                    MainWindow.Dispatcher.Invoke(() =>
                    {
                        MainWindow.Left = savedLeft;
                        MainWindow.Top = savedTop;
                        MainWindow.WindowState = savedState;
                        MainWindow.Show();
                        MainWindow.Activate();
                    });
                }
            };
        }
        editor.Show();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    /// <summary>从「打开图片」入口：直接加载一张本地图片进入截图编辑器（不截屏），可标注/马赛克/导出</summary>
    internal void OpenImageEditor(string path)
    {
        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"无法打开图片：{ex.Message}", "ImageTool",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (MainWindow is { IsVisible: true })
            MainWindow.Hide();

        var editor = new ScreenshotEditorWindow(bmp);
        editor.Show();
    }

    /// <summary>设置页保存后调用：用新组合键重新注册全局热键，并刷新底部提示</summary>
    internal void ReregisterHotkey(int mod, int key)
    {
        try { _hotkey?.Register(mod, key); }
        catch { /* 忽略注册失败 */ }
        CurrentHotkeyHint = HotkeyFormatter.Format(mod, key);
        HotkeyChanged?.Invoke();
    }

    private static void SyncAutoStart()
    {
        var s = SettingsStore.Load();
        var registered = StartupManager.IsEnabled();
        if (s.AutoStart && !registered) StartupManager.Enable();
        else if (!s.AutoStart && registered) StartupManager.Disable();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
