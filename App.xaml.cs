using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
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

    /// <summary>唤起截图：先把可见的主窗口移出虚拟屏幕（确定性排除），再捕获全屏并打开编辑器</summary>
    internal async void StartScreenshot()
    {
        if (MainWindow is { IsVisible: true })
        {
            // 确定性修复（不再依赖 DWM 时序）：直接把主窗口移动到虚拟屏幕之外
            // （坐标 (-100000,-100000) 远在任何显示器之外）。CopyFromScreen 只复制虚拟屏幕矩形，
            // 窗口不在该矩形内，因此无论 DWM 何时重绘都不可能被截进去——从根上杜绝半透明主窗口残留。
            // 之后正式 Hide() 并还原坐标，避免下次 Show 时窗口跑到屏幕外看不见。
            var left = MainWindow.Left;
            var top = MainWindow.Top;
            MainWindow.Left = -100000;
            MainWindow.Top = -100000;
            await Task.Delay(30);
            MainWindow.Hide();
            MainWindow.Left = left;
            MainWindow.Top = top;
        }

        var bmp = _screenshot!.CaptureFullScreen();
        var editor = new ScreenshotEditorWindow(bmp);
        editor.Show();
    }

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
