using System.Windows;
using System.Windows.Input;
using ImageTool.ViewModels;

namespace ImageTool.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    // 录制时把按键捕获挂到窗口级别（而非 UserControl），否则按钮被禁用失去焦点后，
    // 按键的隧道事件不再经过本 UserControl，OnPreviewKeyDown 永远收不到键。
    private Window? _recordWindow;

    public SettingsView()
    {
        InitializeComponent();
    }

    private SettingsViewModel Vm => (SettingsViewModel)DataContext;

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Recording) return;
        Vm.Recording = true;
        RecordButton.Content = "按下组合键…（Esc 取消）";
        // 禁用按钮，避免空格/回车再次触发 Click；按键改由窗口级 PreviewKeyDown 接收
        RecordButton.IsEnabled = false;

        _recordWindow = Window.GetWindow(this);
        if (_recordWindow != null)
            _recordWindow.PreviewKeyDown += Window_PreviewKeyDown;
    }

    private void Window_PreviewKeyDown(object? sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!Vm.Recording || _recordWindow == null) return;

        // Esc 取消录制
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            StopRecording();
            return;
        }

        // 仅修饰键时不记录，等待主键
        var k = e.Key;
        if (k is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System)
        {
            e.Handled = true;
            return;
        }

        e.Handled = true;
        var mods = ModifierKeys.None;
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control)) mods |= ModifierKeys.Control;
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)) mods |= ModifierKeys.Shift;
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Alt)) mods |= ModifierKeys.Alt;
        if (e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= ModifierKeys.Windows;

        Vm.SetHotkey((int)mods, (int)k);
        StopRecording();
    }

    private void StopRecording()
    {
        Vm.Recording = false;
        RecordButton.Content = "录制快捷键";
        RecordButton.IsEnabled = true;
        if (_recordWindow != null)
            _recordWindow.PreviewKeyDown -= Window_PreviewKeyDown;
        _recordWindow = null;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择截图默认保存目录",
            UseDescriptionForTitle = true
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            Vm.SaveDir = dlg.SelectedPath;
    }
}
