using System.ComponentModel;
using System.Windows;
using ImageTool.Services;
using ImageTool.ViewModels;

namespace ImageTool;

/// <summary>
/// 主窗口：组合功能页。关闭时仅隐藏到托盘，不退出程序。
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel(new ImageService());
    }

    /// <summary>切换到「设置」页（托盘「设置」菜单调用）</summary>
    public void SelectSettingsTab()
    {
        if (DataContext is MainViewModel vm)
            vm.SelectedIndex = 3;
    }

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
    {
        ((App)System.Windows.Application.Current).StartScreenshot();
    }

    private void OpenImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*",
            Title = "打开图片"
        };
        if (dlg.ShowDialog() == true)
            ((App)System.Windows.Application.Current).OpenImageEditor(dlg.FileName);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // 关闭仅隐藏界面，程序继续在托盘运行（只有托盘「退出」才 Shutdown）
        e.Cancel = true;
        Hide();
    }
}
